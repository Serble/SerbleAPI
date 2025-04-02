using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data;

namespace SerbleAPI.API.v1; 

[ApiController]
[Route("/")]
public class RootController : ControllerManager {
    
    [HttpGet]
    public IActionResult Get() {
        return Ok("Serble API. View the project on GitHub (https://github.com/Serble/SerbleAPI).");
    }
    
    [HttpOptions]
    public ActionResult Options() {
        HttpContext.Response.Headers.Add("Allow", "GET, OPTIONS");
        return Ok();
    }
}
