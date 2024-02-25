using System.Text.Json;
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
    public ActionResult<dynamic> CreateCheckoutSession([FromHeader] SerbleAuthorizationHeader authorization, [FromBody] JsonDocument body, [FromQuery] string mode = "subscription") {
        if (!authorization.Check(out string scopes, out SerbleAuthorizationHeaderType? _, out string _, out User? target)) {
            return Unauthorized();
        }

        ScopeHandler.ScopesEnum[] scopeStringToEnums = ScopeHandler.ScopeStringToEnums(scopes).ToArray();
        if (!scopeStringToEnums.Contains(ScopeHandler.ScopesEnum.PaymentInfo) && !scopeStringToEnums.Contains(ScopeHandler.ScopesEnum.FullAccess)) {
            return Forbid();
        }

        string domain = Program.Config!["website_url"];

        string[] lookupKeys;
        try {
            lookupKeys = ProductManager.CheckoutBodyToLookupIds(body, out _);
        }
        catch (KeyNotFoundException e) {
            return NotFound("Invalid product ID: " + e.Message);
        }
        PriceListOptions priceOptions = new() {
            LookupKeys = lookupKeys.ToList()
        };
        PriceService priceService = new();
        StripeList<Price> prices = priceService.List(priceOptions);

        if (!prices.Any()) {
            return BadRequest("No valid items were provided");
        }

        target.EnsureStripeCustomer();
        SessionCreateOptions options = new() {
            LineItems = new List<SessionLineItemOptions> {
                new() {
                    Price = prices.Data[0].Id,
                    Quantity = 1,
                },
            },
            Mode = mode,
            SuccessUrl = domain + "/store/success?session_id={CHECKOUT_SESSION_ID}",
            CancelUrl = domain + "/store/cancel",
            ClientReferenceId = target.Id,
            Customer = target.StripeCustomerId
        };

        SessionService service = new();
        Session session = service.Create(options);

        Response.Headers.Add("Location", session.Url);
        return new { url = session.Url };
    }
    
    [HttpPost("checkoutanon")]
    public ActionResult<dynamic> CreateAnonCheckoutSession([FromBody] JsonDocument body, [FromQuery] string mode = "payment") {
        string domain = Program.Config!["website_url"];

        string[] lookupKeys;
        List<SerbleProduct> prods;
        try {
            lookupKeys = ProductManager.CheckoutBodyToLookupIds(body, out prods);
        }
        catch (KeyNotFoundException e) {
            return NotFound("Invalid product ID: " + e.Message);
        }
        PriceListOptions priceOptions = new() {
            LookupKeys = lookupKeys.ToList()
        };
        PriceService priceService = new();
        StripeList<Price> prices = priceService.List(priceOptions);

        if (!prices.Any()) {
            return BadRequest("No valid items were provided");
        }
        
        string surl = domain + "/store/success?session_id={CHECKOUT_SESSION_ID}";
        if (prods.Count == 1 && prods.Single().SuccessRedirect != null) {
            // Generate a token for if it succeeds
            string tok =
                TokenHandler.GenerateCheckoutSuccessToken(prods.Single().Id, prods.Single().SuccessTokenSecret!);
            surl = prods.Single().SuccessRedirect!.Replace("{token}", tok);
        }

        SessionCreateOptions options = new() {
            LineItems = new List<SessionLineItemOptions> {
                new() {
                    Price = prices.Data[0].Id,
                    Quantity = 1,
                },
            },
            Mode = mode,
            SuccessUrl = surl,
            CancelUrl = domain + "/store/cancel"
        };

        SessionService service = new();
        Session session = service.Create(options);

        Response.Headers.Add("Location", session.Url);
        return new { url = session.Url };
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

        Response.Headers.Add("Location", session.Url);
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