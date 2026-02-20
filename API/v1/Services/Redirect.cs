using Microsoft.AspNetCore.Mvc;

namespace SerbleAPI.API.v1.Services; 

[ApiController]
[Route("/api/v1/redirect")]
public class Redirect : ControllerManager {
    
    [HttpGet]
    public IActionResult Get([FromQuery] string to) {
        return Redirect(to);
    }
    
    [HttpPost]
    public IActionResult Post([FromBody] string to) {
        return Redirect(to);
    }
}