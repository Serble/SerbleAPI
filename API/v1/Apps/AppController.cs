using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.API.v1; 

[ApiController]
[Route("api/v1/app/{appid}")]
public class AppController : Controller {
    
    [HttpGet]
    public IActionResult Get(string appid) {
        Program.StorageService!.GetOAuthApp(appid, out OAuthApp? app);
        if (app == null) {
            return NotFound();
        }
        string jsonObj = JsonConvert.SerializeObject(new SanitisedOAuthApp(app));
        return Ok(jsonObj);
    }
    
    [HttpDelete]
    public IActionResult Delete(string appid, [FromHeader] AuthorizationHeaderUser authorizationHeader) {
        if (!authorizationHeader.Check(out string? msg)) {
            return Unauthorized(msg);
        }
        
        Program.StorageService!.GetOAuthApp(appid, out OAuthApp? app);
        if (app == null) {
            return NotFound();
        }
        
        Program.StorageService.DeleteOAuthApp(appid);
        return Ok();
    }
    
    [HttpOptions]
    public ActionResult Options() {
        HttpContext.Response.Headers.Add("Allow", "GET, DELETE, OPTIONS");
        return Ok();
    }

}