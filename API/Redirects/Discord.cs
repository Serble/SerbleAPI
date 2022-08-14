using Microsoft.AspNetCore.Mvc;

namespace SerbleAPI.API.Redirects; 

[ApiController]
[Route("discord")]
public class Discord : Controller {
    
    [HttpGet]
    public IActionResult Get() {
        return Redirect("https://discord.gg/fzvcNhW");
    }
    
}