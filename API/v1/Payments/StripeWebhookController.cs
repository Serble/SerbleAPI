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
        // Replace this endpoint secret with your endpoint's unique secret
        // If you are testing with the CLI, find the secret by running 'stripe listen'
        // If you are using an endpoint defined with the API or dashboard, look in your webhook settings
        // at https://dashboard.stripe.com/webhooks
        string endpointSecret = Program.Config!["stripe_webhook_secret"];
        try {
            Event? stripeEvent = EventUtility.ParseEvent(json);
            StringValues signatureHeader = Request.Headers["Stripe-Signature"];
            stripeEvent = EventUtility.ConstructEvent(json,
                    signatureHeader, endpointSecret);
            switch (stripeEvent.Type) {
                case Events.CustomerSubscriptionDeleted: {
                    if (stripeEvent.Data.Object is not Subscription subscription) break;
                    Logger.Debug("Subscription canceled: " + subscription.Id);
                    // Remove the user's subscription
                    Program.StorageService!.GetUserFromSubscription(subscription.Id, out User? user);
                    if (user == null) {
                        // User probably deleted their account
                        break;
                    }
                    user.SubscriptionId = null;
                    user.PremiumLevel = 0;
                    Program.StorageService.UpdateUser(user);
                    
                    // Send email
                    if (user.VerifiedEmail) {
                        string emailBody = EmailSchemasService.GetEmailSchema(EmailSchema.PurchaseReceipt);
                        emailBody = emailBody.Replace("{name}", user.Username);
                        Email email = new(new []{user.Email}, FromAddress.System, "Subscription Cancelled", emailBody);
                        email.SendNonBlocking();
                    }
                    break;
                }
                case Events.CustomerSubscriptionUpdated: {
                    Subscription? subscription = stripeEvent.Data.Object as Subscription;
                    Logger.Debug("Subscription updated: " + subscription.Id);
                    break;
                }
                case Events.CheckoutSessionCompleted: {
                    if (stripeEvent.Data.Object is not Session session) {
                        break;  // Error maybe?
                    }
                    Program.StorageService!.GetUser(session.ClientReferenceId, out User? user);
                    if (user == null) {
                        Logger.Error("User not found for session: " + session.Id);
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
                    lineItems.Data.ForEach(item => {
                        Logger.Debug("Item Bought: " + item.Description);
                        purchasedItems.Add($"<li>{item.Description}</li>");

                        switch (item.Price.Id) {
                            
                            case "price_1LewIkLys49IgQv1ge1sgLJ0":
                                Logger.Debug("Giving user " + user.Username + " 1 month of premium (Product ID)");
                                user.PremiumLevel = 10;
                                // Get the id of their new subscription
                                SubscriptionService subscriptionService = new();
                                SubscriptionListOptions subscriptionOptions = new() {
                                    Limit = 1,
                                    Customer = session.CustomerId
                                };
                                StripeList<Subscription> subscriptions = subscriptionService.ListAsync(subscriptionOptions).Result;
                                if (subscriptions.Data.Count == 0) {
                                    Logger.Error("No subscriptions found for customer: " + session.CustomerId);
                                    break;
                                }
                                user.SubscriptionId = subscriptions.Data[0].Id;
                                Logger.Debug("SETTING ID, Subscription ID: " + user.SubscriptionId);
                                break;
                            
                            default:
                                Logger.Error("Unknown item bought: " + item.Id);
                                break;
                            
                        }
                    });
                    user.RegisterChanges();

                    if (user.VerifiedEmail) {
                        string emailBody = EmailSchemasService.GetEmailSchema(EmailSchema.PurchaseReceipt);
                        emailBody = emailBody.Replace("{name}", user.Username);
                        emailBody = emailBody.Replace("{products}", string.Join(", ", purchasedItems));
                        Email email = new(new []{user.Email}, FromAddress.System, "Purchase Receipt", emailBody);
                        email.SendNonBlocking();
                    }

                    break;
                }
                case Events.CustomerSubscriptionCreated: {
                    Subscription? subscription = stripeEvent.Data.Object as Subscription;
                    Logger.Debug("Subscription created: " + subscription!.Id);
                    break;
                }
                case Events.CustomerSubscriptionTrialWillEnd: {
                    Subscription? subscription = stripeEvent.Data.Object as Subscription;
                    Logger.Debug("Subscription trial will end: " + subscription!.Id);
                    // TODO: handle the successful payment intent.
                    break;
                }
                default:
                    Logger.Error("Unhandled event type: " + stripeEvent.Type);
                    break;
            }
            return Ok();
        }
        catch (StripeException e) {
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