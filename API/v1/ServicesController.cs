using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data;

namespace SerbleAPI.API.v1; 

[ApiController]
[Route("api/v1/services")]
public class ServicesController : ControllerManager {
    
    [HttpGet]
    public async Task<ActionResult<Service[]>> GetAllServices() {
        return Ok(await ServicesStatusService.GetServiceStatuses());
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<Service>> GetService(string id) {
        Service[] status = (await ServicesStatusService.GetServiceStatuses())
            .Where(service => service.Name == id)
            .ToArray();
        if (!status.Any()) {
            return NotFound();
        }
        return Ok(status.First());
    }
    
    [HttpOptions]
    public ActionResult Options() {
        HttpContext.Response.Headers.Add("Allow", "GET, OPTIONS");
        return Ok();
    }
    
    [HttpOptions("{id}")]
    public ActionResult OptionsArg() {
        HttpContext.Response.Headers.Add("Allow", "GET, OPTIONS");
        return Ok();
    }
    
}
