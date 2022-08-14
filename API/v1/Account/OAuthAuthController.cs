using Microsoft.AspNetCore.Mvc;

namespace SerbleAPI.API.v1.Account; 

[ApiController]
[Route("api/v1/oauth/auth")]
public class OAuthAuthController : ControllerManager {

    [HttpGet]
    public ActionResult<string> Get() {
        return Redirect(Program.Config!["website_url"] + "/oauth/authorize");
    }
    
    [HttpOptions]
    public ActionResult Options() {
        HttpContext.Response.Headers.Add("Allow", "GET, OPTIONS");
        return Ok();
    }
    
}