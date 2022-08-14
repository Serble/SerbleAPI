using Microsoft.AspNetCore.Mvc;

namespace SerbleAPI.API.Redirects; 

[ApiController]
[Route("adam")]
public class Adam : Controller {
    
    [HttpGet]
    public IActionResult Get() {
        return Redirect("https://adamflore.com/");
    }
    
}