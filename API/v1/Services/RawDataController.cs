using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data.Raw;

namespace SerbleAPI.API.v1;

[ApiController]
[Route("api/v1/raw/")]
public class RawDataController : ControllerManager {
    
    [HttpGet("dictionary")]
    public ActionResult<string[]> Get() {
        return Ok(RawDataManager.EnglishWords);
    }
    
    [HttpOptions]
    public ActionResult Options() {
        HttpContext.Response.Headers.Add("Allow", "GET, OPTIONS");
        return Ok();
    }
    
}