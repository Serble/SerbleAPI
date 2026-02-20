using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace SerbleAPI.API.v1.Services; 

[ApiController]
[Route("api/v1/checkuser")]
public class CheckUser : ControllerManager {
    
    [HttpGet]
    public IActionResult Get([FromQuery] string redirect, [FromQuery] bool redirectOnFail = false) {
        
        // On Fail Check
#pragma warning disable CS0162
        if (false) {
            if (redirectOnFail) {
                return Redirect(QueryHelpers.AddQueryString(redirect, "success", "false"));
            }
            return Redirect("/accessdenied");
        }
#pragma warning restore CS0162
        
        return Redirect(QueryHelpers.AddQueryString(redirect, "success", "true"));
    }
}