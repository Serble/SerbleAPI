using GeneralPurposeLib;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data;
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

        target.EnsureStripeCustomer();
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
            ClientReferenceId = user_id,
            Customer = target.StripeCustomerId
        };

        SessionService service = new();
        Session session = service.Create(options);

        Response.Headers.Add("Location", session.Url);
        return new StatusCodeResult(303);
    }
    
    [HttpPost("portal")]
    [Obsolete("Customer ID is no longer provided to clients.")]
    public ActionResult CreatePortalSession() {
        string customerId = Request.Form["customer_id"];

        // This is the URL to which your customer will return after
        // they are done managing billing in the Customer Portal.
        string returnUrl = Program.Config!["website_url"];

        Stripe.BillingPortal.SessionCreateOptions options = new() {
            Customer = customerId,
            ReturnUrl = returnUrl,
        };
        Stripe.BillingPortal.SessionService service = new();
        Stripe.BillingPortal.Session? session;
        try {
            session = service.Create(options);
        }
        catch (StripeException) {
            return BadRequest("Invalid Customer");
        }

        Response.Headers.Add("Location", session.Url);
        return new StatusCodeResult(303);
    }

    /// <summary>
    /// Get the URL for the customer portal.
    /// </summary>
    /// <remarks>
    /// Requires the payment_info scope.
    /// </remarks>
    /// <returns>A URL that will send the user to their Stripe portal.</returns>
    [HttpGet("portal")]
    public ActionResult<dynamic> SendUserToPortal([FromHeader] SerbleAuthorizationHeader authorizationHeader) {
        if (!authorizationHeader.Check(out string scopes, out SerbleAuthorizationHeaderType? type, out string _, out User target)) {
            return Unauthorized();
        }

        ScopeHandler.ScopesEnum[] scopeStringToEnums = ScopeHandler.ScopeStringToEnums(scopes).ToArray();
        if (!scopeStringToEnums.Contains(ScopeHandler.ScopesEnum.PaymentInfo) && !scopeStringToEnums.Contains(ScopeHandler.ScopesEnum.FullAccess)) {
            return Forbid("Insufficient scope");
        }
        
        target.EnsureStripeCustomer();
        string? stripeCustomerId = target.StripeCustomerId;

        string returnUrl = Program.Config!["website_url"];

        Stripe.BillingPortal.SessionCreateOptions options = new() {
            Customer = stripeCustomerId,
            ReturnUrl = returnUrl,
        };
        Stripe.BillingPortal.SessionService service = new();
        Stripe.BillingPortal.Session? session = service.Create(options);

        return new { url = session.Url };
    }

    [HttpOptions("checkout")]
    public IActionResult OptionsCheckout() {
        HttpContext.Response.Headers.Add("Allow", "POST, OPTIONS");
        return Ok();
    }
    
    [HttpOptions("portal")]
    public IActionResult OptionsPortal() {
        HttpContext.Response.Headers.Add("Allow", "GET, POST, OPTIONS");
        return Ok();
    }
    
}