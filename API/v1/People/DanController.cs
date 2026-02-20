using Microsoft.AspNetCore.Mvc;

namespace SerbleAPI.API.v1.People; 

[ApiController]
[Route("api/v1/dan/")]
public class DanController : ControllerManager {
    
    [HttpGet]
    public IActionResult Get() {
        return Ok("I've been gotten");
    }
    
    [HttpGet("{arg}")]
    public IActionResult Get(string arg) {
        return Ok("I've been gotten with an argument oh no");
    }
    
    [HttpPost]
    public IActionResult Post() {
        return Ok("What the fuck");
    }
    
    [HttpPut]
    public IActionResult Put() {
        return Ok("Oh no I've been put");
    }
    
    [HttpDelete]
    public IActionResult Delete() {
        return Ok("Oh no I've been de-");
    }
    
    [HttpPatch]
    public IActionResult Patch() {
        return Ok("Yay I've been patched");
    }
    
    // OPTIONS are dead code, but these are kept
    // because they're funny.
    
    [HttpOptions]
    public ActionResult Options() {
        return Ok("Stop");
    }
    
    [HttpOptions("{arg}")]
    public ActionResult OptionsArg() {
        return Ok("Stop");
    }
    
}