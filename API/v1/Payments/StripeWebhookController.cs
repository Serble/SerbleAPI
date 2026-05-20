using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
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
    IProductRepository productRepo,
    IHttpClientFactory httpClientFactory) : ControllerManager {

    [HttpPost]
    public async Task<IActionResult> StripeWebhookCallback() {
        string json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
        string endpointSecret = settings.Value.WebhookSecret;
        try {
            StringValues signatureHeader = Request.Headers["Stripe-Signature"];
            Event? stripeEvent = EventUtility.ConstructEvent(json, signatureHeader, endpointSecret);
            bool liveMode = stripeEvent.Livemode;

            switch (stripeEvent.Type) {

                case Events.CustomerSubscriptionDeleted: {
                    if (stripeEvent.Data.Object is not Subscription subscription) break;
                    logger.LogDebug("Subscription canceled: " + subscription.Id);
                    User? user = await userRepo.GetUserFromStripeCustomerId(subscription.CustomerId);
                    if (user == null) { logger.LogDebug("User not found for subscription: " + subscription.Id); break; }

                    foreach (SubscriptionItem subscriptionItem in subscription.Items) {
                        SerbleProduct? prod = await productRepo.GetProductFromPriceId(subscriptionItem.Price.Id);
                        if (prod == null) continue;
                        await productRepo.RemoveOwnedProduct(user.Id, prod.Id);
                        logger.LogDebug("Removed product " + prod.Name + " from user " + user.Username);
                    }

                    if (liveMode || user.IsAdmin()) {
                        await userRepo.UpdateUser(user);
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

                    User? user = session.ClientReferenceId == null
                        ? null
                        : await userRepo.GetUser(session.ClientReferenceId);
                    if (user == null && session.ClientReferenceId != null) {
                        logger.LogDebug("User not found for checkout session: " + session.Id);
                        break;
                    }

                    logger.LogDebug("Checkout session completed: " + session.Id + " for user "
                        + (user?.Username ?? "<anonymous>"));

                    StripeList<LineItem> lineItems = await new SessionService()
                        .ListLineItemsAsync(session.Id, new SessionListLineItemsOptions { Limit = 5 });

                    if (lineItems.Data.Count == 0)
                        logger.LogWarning("No line items found for session: " + session.Id);

                    List<string> purchasedItems = [];
                    List<string> itemIds = [];
                    List<(SerbleProduct product, long? amount, string? currency)> webhookTargets = [];
                    foreach (LineItem item in lineItems.Data) {
                        logger.LogDebug("Item Bought: " + item.Description);
                        purchasedItems.Add($"<li>{item.Description}</li>");
                        SerbleProduct? product = await productRepo.GetProductFromPriceId(item.Price.Id);
                        if (product == null) {
                            logger.LogError("Unknown item bought: " + item.Price.Id);
                            continue;
                        }
                        itemIds.Add(product.Id);
                        webhookTargets.Add((product, item.AmountTotal, item.Currency));
                    }

                    if (user != null) {
                        await productRepo.AddOwnedProducts(user.Id, itemIds.ToArray());
                    }

                    // Fire per-product webhooks now that fulfillment is committed.
                    foreach ((SerbleProduct product, long? amount, string? currency) in webhookTargets) {
                        if (string.IsNullOrWhiteSpace(product.Webhook)) continue;
                        try {
                            await FireProductWebhook(product, user?.Id, amount, currency);
                        }
                        catch (Exception ex) {
                            logger.LogError(ex, "Failed to fire product webhook for {ProductId}", product.Id);
                        }
                    }

                    if (user is { VerifiedEmail: true }) {
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
                    User? user = await userRepo.GetUserFromStripeCustomerId(subscription.CustomerId);
                    if (user == null) {
                        logger.LogDebug("User not found for subscription: " + subscription.Id); break;
                    }
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

    private async Task FireProductWebhook(SerbleProduct product, string? userId, long? amountTotal, string? currency) {
        if (string.IsNullOrWhiteSpace(product.Webhook)) return;

        logger.LogInformation("Making webhook request to {Webhook} for product {ProductId}", product.Webhook, product.Id);
        HttpClient http = httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(5);

        HttpRequestMessage req = new(HttpMethod.Post, product.Webhook) {
            Content = JsonContent.Create(new {
                userId,
                productId = product.Id,
                amountTotal,
                currency
            })
        };
        if (!string.IsNullOrEmpty(product.WebhookSecret)) {
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", product.WebhookSecret);
        }

        HttpResponseMessage resp = await http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) {
            logger.LogWarning("Product webhook for {ProductId} returned {Status}", product.Id, (int)resp.StatusCode);
        }
    }
}