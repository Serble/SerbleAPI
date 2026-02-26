using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SerbleAPI.Authentication;
using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;
using Stripe;
using Stripe.Checkout;

namespace SerbleAPI.API.v1.Payments;

[Route("api/v1/payments")]
[Controller]
public class CreateCheckoutController(
    IOptions<ApiSettings> apiSettings,
    IUserRepository userRepo,
    ITokenService tokens) : ControllerManager {

    [HttpPost("checkout")]
    [Authorize(Policy = "Scope:PaymentInfo")]
    public async Task<ActionResult<dynamic>> CreateCheckoutSession([FromBody] JsonDocument body, [FromQuery] string mode = "subscription") {
        User? target = await HttpContext.User.GetUser(userRepo);
        if (target == null) {
            return Unauthorized();
        }

        string domain = apiSettings.Value.WebsiteUrl;
        string[] lookupKeys;
        try {
            lookupKeys = ProductManager.CheckoutBodyToLookupIds(body, out _);
        }
        catch (KeyNotFoundException e) {
            return NotFound("Invalid product ID: " + e.Message);
        }

        StripeList<Price> prices = await new PriceService().ListAsync(new PriceListOptions {
            LookupKeys = lookupKeys.ToList()
        });
        if (!prices.Any()) {
            return BadRequest("No valid items were provided");
        }

        await target.EnsureStripeCustomer();
        Session session = await new SessionService().CreateAsync(new SessionCreateOptions {
            LineItems = [
                new SessionLineItemOptions {
                    Price = prices.Data[0].Id, Quantity = 1
                }
            ],
            Mode = mode,
            SuccessUrl = domain + "/store/success?session_id={CHECKOUT_SESSION_ID}",
            CancelUrl = domain + "/store/cancel",
            ClientReferenceId = target.Id,
            Customer = target.StripeCustomerId
        });
        Response.Headers.Append("Location", session.Url);
        return new {
            url = session.Url
        };
    }

    // Anonymous checkout — no user account needed
    [HttpPost("checkoutanon")]
    [AllowAnonymous]
    public async Task<ActionResult<dynamic>> CreateAnonCheckoutSession([FromBody] JsonDocument body, [FromQuery] string mode = "payment") {
        string domain = apiSettings.Value.WebsiteUrl;
        string[] lookupKeys;
        List<SerbleProduct> prods;
        try {
            lookupKeys = ProductManager.CheckoutBodyToLookupIds(body, out prods);
        }
        catch (KeyNotFoundException e) {
            return NotFound("Invalid product ID: " + e.Message);
        }

        StripeList<Price> prices = await new PriceService().ListAsync(new PriceListOptions { LookupKeys = lookupKeys.ToList() });
        if (!prices.Any()) return BadRequest("No valid items were provided");

        string surl = domain + "/store/success?session_id={CHECKOUT_SESSION_ID}";
        if (prods.Count == 1 && prods.Single().SuccessRedirect != null) {
            string tok = tokens.GenerateCheckoutSuccessToken(prods.Single().Id, prods.Single().SuccessTokenSecret!);
            surl = prods.Single().SuccessRedirect!.Replace("{token}", tok);
        }
        Session session = await new SessionService().CreateAsync(new SessionCreateOptions {
            LineItems = [new SessionLineItemOptions { Price = prices.Data[0].Id, Quantity = 1 }],
            Mode = mode, SuccessUrl = surl, CancelUrl = domain + "/store/cancel"
        });
        Response.Headers.Append("Location", session.Url);
        return new { url = session.Url };
    }

    [HttpPost("portal")]
    [Obsolete("Customer ID is no longer provided to clients.")]
    [AllowAnonymous]
    public async Task<ActionResult> CreatePortalSession() {
        string customerId = Request.Form["customer_id"]!;
        Stripe.BillingPortal.SessionCreateOptions options = new() { Customer = customerId, ReturnUrl = apiSettings.Value.WebsiteUrl };
        Stripe.BillingPortal.Session? session;
        try {
            session = await new Stripe.BillingPortal.SessionService().CreateAsync(options);
        }
        catch (StripeException) {
            return BadRequest("Invalid Customer");
        }
        Response.Headers.Append("Location", session.Url);
        return new StatusCodeResult(303);
    }

    [HttpGet("portal")]
    [Authorize(Policy = "Scope:PaymentInfo")]
    public async Task<ActionResult<dynamic>> SendUserToPortal() {
        User? target = await HttpContext.User.GetUser(userRepo);
        if (target == null) return Unauthorized();
        await target.EnsureStripeCustomer();
        Stripe.BillingPortal.Session session = await new Stripe.BillingPortal.SessionService().CreateAsync(
            new Stripe.BillingPortal.SessionCreateOptions { Customer = target.StripeCustomerId, ReturnUrl = apiSettings.Value.WebsiteUrl });
        Response.Headers.Append("Location", session.Url);
        return new { url = session.Url };
    }
}