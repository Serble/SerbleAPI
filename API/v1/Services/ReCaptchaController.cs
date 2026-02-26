using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Services; 

[ApiController]
[Route("api/v1/recaptcha")]
public class ReCaptchaController(IGoogleReCaptchaService reCaptchaService) : ControllerManager {

    [HttpPost]
    public async Task<ActionResult<int>> Post([FromQuery] string token) {
        // Verify
        GoogleReCaptchaResponse response = await reCaptchaService.VerifyReCaptcha(token);
        
        if (!response.Success) {
            return BadRequest("Error validating recaptcha");
        }
        
        return Ok(response.Score);
    }
}