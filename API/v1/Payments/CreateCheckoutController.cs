using System.ComponentModel.Design;
using GeneralPurposeLib;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;
using Stripe;
using Stripe.Checkout;

namespace SerbleAPI.API.v1.Payments; 

[Route("api/v1/payments")]
[Controller]
public class CreateCheckoutController : ControllerManager {
    
    [HttpPost("checkout")]
    public ActionResult CreateCheckoutSession([FromQuery] string user_id) {
        // Try get user
        Program.StorageService!.GetUser(user_id, out User? target);
        if (target == null) return BadRequest("User not found");
        
        string domain = Program.Config!["website_url"];
        
        Logger.Debug("Price key: " + Request.Form["lookup_key"]);

        PriceListOptions priceOptions = new() {
            LookupKeys = new List<string> {
                Request.Form["lookup_key"]
            }
        };
        PriceService priceService = new();
        StripeList<Price> prices = priceService.List(priceOptions);

        if (!prices.Any()) {
            return BadRequest();
        }

        SessionCreateOptions options = new() {
            LineItems = new List<SessionLineItemOptions> {
                new() {
                    Price = prices.Data[0].Id,
                    Quantity = 1,
                },
            },
            Mode = "subscription",
            SuccessUrl = domain + "/store/success?session_id={CHECKOUT_SESSION_ID}",
            CancelUrl = domain + "/store/cancel",
            ClientReferenceId = user_id
        };
        SessionService service = new();
        Session session = service.Create(options);

        Response.Headers.Add("Location", session.Url);
        return new StatusCodeResult(303);
    }
    
    [HttpPost("portal")]
    public ActionResult CreatePortalSession() {
        // For demonstration purposes, we're using the Checkout session to retrieve the customer ID.
        // Typically this is stored alongside the authenticated user in your database.
        SessionService checkoutService = new();
        Session? checkoutSession = checkoutService.Get(Request.Form["session_id"]);

        // This is the URL to which your customer will return after
        // they are done managing billing in the Customer Portal.
        string returnUrl = Program.Config!["website_url"];

        Stripe.BillingPortal.SessionCreateOptions options = new() {
            Customer = checkoutSession.CustomerId,
            ReturnUrl = returnUrl,
        };
        Stripe.BillingPortal.SessionService service = new();
        Stripe.BillingPortal.Session? session = service.Create(options);

        Response.Headers.Add("Location", session.Url);
        return new StatusCodeResult(303);
    }
    
        [HttpPost("webhook")]
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
                        Subscription? subscription = stripeEvent.Data.Object as Subscription;
                        Logger.Debug("Subscription canceled: " + subscription.Id);
                        // TODO: handle the successful payment intent.
                        break;
                    }
                    case Events.CustomerSubscriptionUpdated: {
                        Subscription? subscription = stripeEvent.Data.Object as Subscription;
                        Logger.Debug("Subscription updated: " + subscription.Id);
                        // TODO: handle the successful payment intent.
                        break;
                    }
                    case Events.CheckoutSessionCompleted: {
                        Session? session = stripeEvent.Data.Object as Session;
                        Program.StorageService!.GetUser(session.ClientReferenceId, out User? user);
                        if (user == null) {
                            Logger.Error("User not found for session: " + session.Id);
                            break;
                        }
                        Logger.Debug("Checkout session completed: " + session.Id + " for user " + user.Username);

                        if (session.Customer != null && session.Customer.Email != null) {
                            Logger.Debug("Subscription created: " + session.Id + " Email: " + session.Customer.Email);
                        }
                        else {
                            Logger.Debug("Something is null");
                        }
                        
                        // Get what was purchased
                        SessionListLineItemsOptions options = new() {
                            Limit = 5
                        };
                        SessionService service = new();
                        StripeList<LineItem> lineItems = await service.ListLineItemsAsync(session.Id, options);
                        if (lineItems.Data.Count == 0) {
                            Logger.Warn("No line items found for session: " + session.Id);
                        }
                        lineItems.Data.ForEach(item => Logger.Debug("Item: " + item.Description));

                        break;
                    }
                    case Events.CustomerSubscriptionCreated: {
                        Subscription? subscription = stripeEvent.Data.Object as Subscription;
                        Logger.Debug("Subscription created: " + subscription!.Id);
                        // TODO: handle the successful payment intent.
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
        
        [HttpOptions("webhook")]
        public IActionResult OptionsWeb() {
            HttpContext.Response.Headers.Add("Allow", "POST, OPTIONS");
            return Ok();
        }
        
        [HttpOptions("checkout")]
        public IActionResult OptionsCheckout() {
            HttpContext.Response.Headers.Add("Allow", "POST, OPTIONS");
            return Ok();
        }
        
        [HttpOptions("portal")]
        public IActionResult OptionsPortal() {
            HttpContext.Response.Headers.Add("Allow", "POST, OPTIONS");
            return Ok();
        }
    
}