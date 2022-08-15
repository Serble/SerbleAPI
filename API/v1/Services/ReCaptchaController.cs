using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.API.v1.Services; 

[ApiController]
[Route("api/v1/recaptcha")]
public class ReCaptchaController : ControllerManager {

    [HttpPost]
    public async Task<ActionResult<int>> Post([FromQuery] string token) {
        
        // Verify
        GoogleReCaptchaResponse response = await GoogleReCaptchaHandler.VerifyReCaptcha(token);
        
        if (!response.Success) {
            return BadRequest("Error validating recaptcha");
        }
        
        return Ok(response.Score);
    }
    
    [HttpOptions]
    public ActionResult Options() {
        HttpContext.Response.Headers.Add("Allow", "POST, OPTIONS");
        return Ok("Thank you");
    }
    
}