using GeneralPurposeLib;
using Microsoft.AspNetCore.Mvc;
using Stripe.Checkout;

namespace SerbleAPI.API.v1.Payments; 

[Route("api/v1/payments/ordersuccess")]
[ApiController]
public class OrderSuccessController : ControllerManager {
    
    [HttpGet("{session_id}")]
    public IActionResult Get(string session_id) {
        SessionService sessionService = new();
        Session session = sessionService.Get(session_id);
        Logger.Debug(!session.Livemode ? "Order Success - Test Session" : "Order Success");
        return Redirect(Program.Config!["website_url"] + $"/store/success?session_id={session_id}");
    }
    
}