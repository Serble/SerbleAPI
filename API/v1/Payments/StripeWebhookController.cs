using GeneralPurposeLib;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using Stripe;
using Stripe.Checkout;

namespace SerbleAPI.API.v1.Payments; 

[Route("api/v1/payments/webhook")]
[ApiController]
public class StripeWebhookController : ControllerManager {
    
    [HttpPost]
    public async Task<IActionResult> StripeWebhookCallback() {
        string json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
        string endpointSecret = Program.Testing ? Program.Config!["stripe_testing_webhook_secret"] : Program.Config!["stripe_webhook_secret"];
        try {
            Event? stripeEvent = EventUtility.ParseEvent(json);
            StringValues signatureHeader = Request.Headers["Stripe-Signature"];
            stripeEvent = EventUtility.ConstructEvent(json,
                signatureHeader, endpointSecret);
            bool liveMode = stripeEvent.Livemode;
            bool fulfillOrderForNonAdmins = Program.Config["give_products_to_non_admins_while_testing"] == "true";
            switch (stripeEvent.Type) {

                case Events.CustomerSubscriptionDeleted: {
                    if (stripeEvent.Data.Object is not Subscription subscription) break;
                    Logger.Debug("Subscription canceled: " + subscription.Id);
                    // Remove the user's subscription
                    Program.StorageService!.GetUserFromStripeCustomerId(subscription.CustomerId, out User? user);
                    if (user == null) {
                        // User probably deleted their account
                        Logger.Debug("User not found for subscription: " + subscription.Id);
                        break;
                    }
                    
                    foreach (SubscriptionItem subscriptionItem in subscription.Items) {
                        SerbleProduct? prod = ProductManager.GetProductFromPriceId(subscriptionItem.Price.Id);
                        if (prod == null) {
                            continue;
                        }
                        Program.StorageService.RemoveOwnedProduct(user.Id, prod.Id);
                        Logger.Debug("Removed product " + prod.Name + " from user " + user.Username);
                    }

                    if (liveMode || fulfillOrderForNonAdmins || user.IsAdmin()) {
                        Program.StorageService.UpdateUser(user);
                    }
                    else {
                        Logger.Debug("Not removing subscription for user " + user.Username +
                                     " because they are not an admin and we are not in live mode");
                    }

                    // Send email
                    if (user.VerifiedEmail) {
                        string emailBody = EmailSchemasService.GetEmailSchema(EmailSchema.SubscriptionEnded, LocalisationHandler.LanguageOrDefault(user));
                        emailBody = emailBody.Replace("{name}", user.Username);
                        Email email = new(user.Email.ToSingleItemEnumerable().ToArray(), FromAddress.System,
                            "Subscription Cancelled", emailBody);
                        email.SendNonBlocking();
                    }

                    break;
                }

                case Events.CustomerSubscriptionUpdated: {
                    Subscription? subscription = stripeEvent.Data.Object as Subscription;
                    Logger.Debug("Subscription updated: " + subscription!.Id);
                    break;
                }

                case Events.CheckoutSessionCompleted: {
                    if (stripeEvent.Data.Object is not Session session) {
                        break; // Error maybe?
                    }

                    Program.StorageService!.GetUser(session.ClientReferenceId, out User? user);
                    if (user == null) {
                        // User probably deleted their account
                        Logger.Debug("User not found for checkout session: " + session.Id);
                        break;
                    }

                    Logger.Debug("Checkout session completed: " + session.Id + " for user " + user.Username);

                    // Get what was purchased
                    SessionListLineItemsOptions options = new() {
                        Limit = 5
                    };
                    SessionService service = new();
                    StripeList<LineItem> lineItems = await service.ListLineItemsAsync(session.Id, options);
                    if (lineItems.Data.Count == 0) {
                        Logger.Warn("No line items found for session: " + session.Id);
                    }

                    List<string> purchasedItems = new();
                    List<string> itemIds = new();
                    lineItems.Data.ForEach(item => {
                        Logger.Debug("Item Bought: " + item.Description);
                        purchasedItems.Add($"<li>{item.Description}</li>");

                        SerbleProduct? product = ProductManager.GetProductFromPriceId(item.Price.Id);
                        if (product == null) {
                            Logger.Error("Unknown item bought: " + item.Price.Id);
                        }
                        
                        itemIds.Add(product!.Id);
                    });
                    if (liveMode || fulfillOrderForNonAdmins || user.IsAdmin()) {
                        Program.StorageService.AddOwnedProducts(user.Id, itemIds.ToArray());
                    }
                    else {
                        Logger.Debug("Not fulfilling order because we are not in live mode and user is not admin");
                    }

                    if (user.VerifiedEmail) {
                        string emailBody = EmailSchemasService.GetEmailSchema(EmailSchema.PurchaseReceipt, LocalisationHandler.LanguageOrDefault(user));
                        emailBody = emailBody.Replace("{name}", user.Username);
                        emailBody = emailBody.Replace("{products}", string.Join(", ", purchasedItems));
                        Email email = new(new[] {user.Email}, FromAddress.System, "Purchase Receipt", emailBody);
                        email.SendNonBlocking();
                    }

                    break;
                }

                case Events.CustomerSubscriptionCreated: {
                    break;
                }

                case Events.CustomerSubscriptionTrialWillEnd: {
                    Subscription subscription = (stripeEvent.Data.Object as Subscription).ThrowIfNull();
                    Program.StorageService!.GetUserFromStripeCustomerId(subscription.CustomerId, out User? user);
                    if (user == null) {
                        // User probably deleted their account
                        Logger.Debug("User not found for subscription: " + subscription.Id);
                        break;
                    }

                    if (!user.VerifiedEmail) {
                        break;
                    }

                    if (subscription.TrialEnd == null) {
                        // No trial
                        Logger.Error(
                            "No trial end date found for subscription in Trial End webhook: " + subscription.Id);
                        break;
                    }

                    string emailBody = EmailSchemasService.GetEmailSchema(EmailSchema.FreeTrialEnding, LocalisationHandler.LanguageOrDefault(user));
                    emailBody = emailBody.Replace("{name}", user.Username)
                        .Replace("{trial_end_date}", subscription.TrialEnd.Value.ToString("MMMM dd, yyyy"))
                        .Replace("{trial_end_time}", subscription.TrialEnd.Value.ToString("h:mm tt"));
                    Email email = new(user.Email.ToSingleItemEnumerable().ToArray(), FromAddress.System,
                        "Subscription Trial Ending", emailBody);
                    email.SendNonBlocking();
                    break;
                }

                default:
                    Logger.Error("Unhandled event type: " + stripeEvent.Type);
                    break;
            }

            return Ok();
        }
        catch (StripeException e) {
            Logger.Debug("Stripe exception: " + e.Message);
            return BadRequest();
        }
        catch (Exception e) {
            Logger.Error(e);
            return BadRequest();
        }
    }
    
    [HttpOptions]
    public IActionResult OptionsWeb() {
        HttpContext.Response.Headers.Add("Allow", "POST, OPTIONS");
        return Ok();
    }
    
}