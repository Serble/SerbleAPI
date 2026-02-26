using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SerbleAPI.Config;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Account;

[ApiController]
[Route("api/v1/emailconfirm")]
public class EmailConfirmationController(IOptions<ApiSettings> apiSettings, ITokenService tokens, IUserRepository users) : ControllerManager {

    [HttpGet]
    public async Task<ActionResult> Confirm([FromQuery] string token, [FromQuery] string? redirect = null, [FromQuery] string? failureRedirect = null) {
        if (!tokens.ValidateEmailConfirmationToken(token, out string? userId, out string email)) {
            return Redirect(failureRedirect ?? $"{apiSettings.Value.WebsiteUrl}/emailconfirm/error");
        }
        
        User? user = await users.GetUser(userId!);
        if (user == null || user.Email != email || user.VerifiedEmail) {
            return Redirect(failureRedirect ?? $"{apiSettings.Value.WebsiteUrl}/emailconfirm/error");
        }
        
        user.VerifiedEmail = true;
        await user.RegisterChanges();
        
        return Redirect(redirect ?? $"{apiSettings.Value.WebsiteUrl}/emailconfirm/success"); 
    }
}
