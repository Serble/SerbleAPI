using Microsoft.AspNetCore.Mvc;

namespace SerbleAPI.API.v1.Account; 

[ApiController]
[Route("api/v1/oauth/auth")]
public class OAuthAuth : ControllerManager {

    [HttpGet]
    public ActionResult<string> Get() {
        throw new NotImplementedException();
    }
    
}