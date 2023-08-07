using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.API.v1.Payments; 

[ApiController]
[Route("api/v1/products")]
public class ProductsController : ControllerManager {
    
    [HttpGet]
    public ActionResult<string[]> Get([FromHeader] SerbleAuthorizationHeader authorizationHeader) {
        if (!authorizationHeader.CheckAndGetInfo(out User target, out Dictionary<string, string> t, null, true, Request)) {
            return Unauthorized();
        }
        Program.StorageService!.GetOwnedProducts(target.Id, out string[] prods);
        return prods;
    }
    
    [HttpOptions]
    public IActionResult Options() {
        HttpContext.Response.Headers.Add("Allow", "GET, OPTIONS");
        return Ok();
    }
    
}