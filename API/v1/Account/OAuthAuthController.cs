using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SerbleAPI.Config;

namespace SerbleAPI.API.v1.Account; 

[ApiController]
[Route("api/v1/oauth/auth")]
public class OAuthAuthController(IOptions<ApiSettings> apiSettings) : ControllerManager {

    [HttpGet]
    public ActionResult<string> Get() {
        string url = apiSettings.Value.WebsiteUrl + "/oauth/authorize" + Request.QueryString;
        return Redirect(url);
    }
}