using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data.Raw;

namespace SerbleAPI.API.v1.Services;

[ApiController]
[Route("api/v1/raw/")]
public class RawDataController : ControllerManager {
    
    [HttpGet("dictionary")]
    public ActionResult<string[]> Get() {
        return Ok(RawDataManager.EnglishWords);
    }
}