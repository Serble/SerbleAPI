using Microsoft.AspNetCore.Mvc;

namespace SerbleAPI.API.v1.People; 

[ApiController]
[Route("api/v1/tristan/")]
public class TristanController : ControllerManager {
    
    [HttpGet]
    public IActionResult Get() {
        return Ok("Gotten what?");
    }
    
    [HttpGet("{arg}")]
    public IActionResult Get(string arg) {
        return Redirect($"Oh damn");
    }
    
    [HttpPost]
    public IActionResult Post() {
        return Ok("Not again");
    }
    
    [HttpPut]
    public IActionResult Put() {
        return Ok("I'd rather pull");
    }
    
    [HttpDelete]
    public IActionResult Delete() {
        return Ok($"");
    }
    
    [HttpPatch]
    public IActionResult Patch() {
        return Ok("Why?");
    }
    
    // OPTIONS are dead code, but these are kept
    // because they're funny.
    
    [HttpOptions]
    public ActionResult Options() {
        return Ok("Can I pick a different one?");
    }
    
    [HttpOptions("{cat:int}")]
    public ActionResult OptionsArg() {
        return Ok("Can I pick a different one?");
    }
    
}