using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SerbleAPI.Config;
using Stripe.Checkout;

namespace SerbleAPI.API.v1.Payments; 

[Route("api/v1/payments/ordersuccess")]
[ApiController]
public class OrderSuccessController(ILogger<OrderSuccessController> logger, IOptions<ApiSettings> apiSettings) : ControllerManager {
    
    [HttpGet("{session_id}")]
    public IActionResult Get(string session_id) {
        SessionService sessionService = new();
        Session session = sessionService.Get(session_id);
        logger.LogDebug(!session.Livemode ? "Order Success - Test Session" : "Order Success");
        return Redirect(apiSettings.Value.WebsiteUrl + $"/store/success?session_id={session_id}");
    }
    
}