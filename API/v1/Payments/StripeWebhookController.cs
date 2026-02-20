using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using Stripe;
using Stripe.Checkout;

namespace SerbleAPI.API.v1.Payments;

[Route("api/v1/payments/webhook")]
[ApiController]
public class StripeWebhookController(
    ILogger<StripeWebhookController> logger,
    IOptions<StripeSettings> settings,
    IOptions<EmailSettings> emailSettings,
    IUserRepository userRepo,
    IProductRepository productRepo) : ControllerManager {

    [HttpPost]
    public async Task<IActionResult> StripeWebhookCallback() {
        string json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
        string endpointSecret = settings.Value.ApiKey;
        try {
            Event? stripeEvent = EventUtility.ParseEvent(json);
            StringValues signatureHeader = Request.Headers["Stripe-Signature"];
            stripeEvent = EventUtility.ConstructEvent(json, signatureHeader, endpointSecret);
            bool liveMode = stripeEvent.Livemode;

            switch (stripeEvent.Type) {

                case Events.CustomerSubscriptionDeleted: {
                    if (stripeEvent.Data.Object is not Subscription subscription) break;
                    logger.LogDebug("Subscription canceled: " + subscription.Id);
                    User? user = userRepo.GetUserFromStripeCustomerId(subscription.CustomerId);
                    if (user == null) { logger.LogDebug("User not found for subscription: " + subscription.Id); break; }

                    foreach (SubscriptionItem subscriptionItem in subscription.Items) {
                        SerbleProduct? prod = ProductManager.GetProductFromPriceId(subscriptionItem.Price.Id);
                        if (prod == null) continue;
                        productRepo.RemoveOwnedProduct(user.Id, prod.Id);
                        logger.LogDebug("Removed product " + prod.Name + " from user " + user.Username);
                    }

                    if (liveMode || user.IsAdmin()) {
                        userRepo.UpdateUser(user);
                    }
                    else {
                        logger.LogDebug("Not removing subscription for user " + user.Username +
                            " because they are not an admin and we are not in live mode");
                    }

                    if (user.VerifiedEmail) {
                        string emailBody = EmailSchemasService.GetEmailSchema(EmailSchema.SubscriptionEnded, LocalisationHandler.LanguageOrDefault(user));
                        emailBody = emailBody.Replace("{name}", user.Username);
                        Email email = new(logger, emailSettings.Value, user.Email.ToSingleItemEnumerable().ToArray(),
                            FromAddress.System, "Subscription Cancelled", emailBody);
                        email.SendNonBlocking();
                    }
                    break;
                }

                case Events.CustomerSubscriptionUpdated: {
                    Subscription? subscription = stripeEvent.Data.Object as Subscription;
                    logger.LogDebug("Subscription updated: " + subscription!.Id);
                    break;
                }

                case Events.CheckoutSessionCompleted: {
                    if (stripeEvent.Data.Object is not Session session) break;

                    User? user = userRepo.GetUser(session.ClientReferenceId);
                    if (user == null) { logger.LogDebug("User not found for checkout session: " + session.Id); break; }

                    logger.LogDebug("Checkout session completed: " + session.Id + " for user " + user.Username);

                    StripeList<LineItem> lineItems = await new SessionService()
                        .ListLineItemsAsync(session.Id, new SessionListLineItemsOptions { Limit = 5 });

                    if (lineItems.Data.Count == 0)
                        logger.LogWarning("No line items found for session: " + session.Id);

                    List<string> purchasedItems = [];
                    List<string> itemIds = [];
                    lineItems.Data.ForEach(item => {
                        logger.LogDebug("Item Bought: " + item.Description);
                        purchasedItems.Add($"<li>{item.Description}</li>");
                        SerbleProduct? product = ProductManager.GetProductFromPriceId(item.Price.Id);
                        if (product == null) { logger.LogError("Unknown item bought: " + item.Price.Id); }
                        else { itemIds.Add(product.Id); }
                    });

                    if (liveMode || user.IsAdmin()) {
                        productRepo.AddOwnedProducts(user.Id, itemIds.ToArray());
                    }
                    else {
                        logger.LogDebug("Not fulfilling order because we are not in live mode and user is not admin");
                    }

                    if (user.VerifiedEmail) {
                        string emailBody = EmailSchemasService.GetEmailSchema(EmailSchema.PurchaseReceipt, LocalisationHandler.LanguageOrDefault(user));
                        emailBody = emailBody.Replace("{name}", user.Username)
                                             .Replace("{products}", string.Join(", ", purchasedItems));
                        Email email = new(logger, emailSettings.Value, [user.Email],
                            FromAddress.System, "Purchase Receipt", emailBody);
                        email.SendNonBlocking();
                    }
                    break;
                }

                case Events.CustomerSubscriptionCreated:
                    break;

                case Events.CustomerSubscriptionTrialWillEnd: {
                    Subscription subscription = (stripeEvent.Data.Object as Subscription).ThrowIfNull();
                    User? user = userRepo.GetUserFromStripeCustomerId(subscription.CustomerId);
                    if (user == null) { logger.LogDebug("User not found for subscription: " + subscription.Id); break; }
                    if (!user.VerifiedEmail) break;
                    if (subscription.TrialEnd == null) {
                        logger.LogError("No trial end date found for subscription in Trial End webhook: " + subscription.Id);
                        break;
                    }
                    string emailBody2 = EmailSchemasService.GetEmailSchema(EmailSchema.FreeTrialEnding, LocalisationHandler.LanguageOrDefault(user));
                    emailBody2 = emailBody2.Replace("{name}", user.Username)
                        .Replace("{trial_end_date}", subscription.TrialEnd.Value.ToString("MMMM dd, yyyy"))
                        .Replace("{trial_end_time}", subscription.TrialEnd.Value.ToString("h:mm tt"));
                    Email email2 = new(logger, emailSettings.Value,
                        user.Email.ToSingleItemEnumerable().ToArray(),
                        FromAddress.System, "Subscription Trial Ending", emailBody2);
                    email2.SendNonBlocking();
                    break;
                }

                default:
                    logger.LogError("Unhandled event type: " + stripeEvent.Type);
                    break;
            }

            return Ok();
        }
        catch (StripeException e) {
            logger.LogDebug("Stripe exception: " + e.Message);
            return BadRequest();
        }
        catch (Exception e) {
            logger.LogError(e.ToString());
            return BadRequest();
        }
    }
}