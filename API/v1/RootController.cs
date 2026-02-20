using Microsoft.AspNetCore.Mvc;

namespace SerbleAPI.API.v1; 

[ApiController]
[Route("/")]
public class RootController : ControllerManager {
    
    [HttpGet]
    public IActionResult Get() {
        return Ok("Serble API. View the project on GitHub (https://github.com/Serble/SerbleAPI).");
    }
}