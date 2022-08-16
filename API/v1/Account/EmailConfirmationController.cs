using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.API.v1.Account;

[ApiController]
[Route("api/v1/emailconfirm")]
public class EmailConfirmationController : ControllerManager {

    [HttpGet]
    public ActionResult Confirm([FromQuery] string token) {
        if (!TokenHandler.ValidateEmailConfirmationToken(token, out User? user, out string email) || user.Email != email || user.VerifiedEmail) {
            return Redirect($"{Program.Config!["website_url"]}/emailconfirm/error");
        }
        
        user.VerifiedEmail = true;
        user.RegisterChanges();
        
        return Redirect($"{Program.Config!["website_url"]}/emailconfirm/success"); 
    }
    
}