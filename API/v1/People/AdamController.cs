using Microsoft.AspNetCore.Mvc;

namespace SerbleAPI.API.v1; 

[ApiController]
[Route("api/v1/adam/")]
public class AdamController : ControllerManager {
    
    /// <summary>
    /// Get Adam
    /// </summary>
    /// <returns>Adam's response to being got</returns>
    [HttpGet]
    public IActionResult Get() {
        return Ok("Did you know that cats are cute?");
    }
    
    [HttpGet("{cat:int}")]
    public IActionResult Get(int cat) {
        return Redirect($"https://http.cat/{cat}");
    }
    
    [HttpPost]
    public IActionResult Post() {
        return Ok("You posted me");
    }
    
    [HttpPut]
    public IActionResult Put() {
        return Ok("I have been put");
    }
    
    [HttpDelete]
    public IActionResult Delete() {
        return Ok($"NOOOOOOOO DON'T DELETE ME");
    }
    
    [HttpPatch]
    public IActionResult Patch() {
        return Ok("I have been patched");
    }
    
    [HttpOptions]
    public ActionResult Options() {
        HttpContext.Response.Headers.Add("Allow", "GET, POST, PATCH, PUT, DELETE, OPTIONS");
        return Ok("Thank you");
    }
    
    [HttpOptions("{cat:int}")]
    public ActionResult OptionsArg() {
        HttpContext.Response.Headers.Add("Allow", "GET, POST, PATCH, PUT, DELETE, OPTIONS");
        return Ok("Thank you");
    }
    
}