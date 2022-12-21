using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.API.v1.Account; 

[Route("api/v1/account/products")]
[Controller]
public class AccountProductsController : ControllerManager {
    
    [HttpGet]
    public ActionResult<SerbleProduct> Get([FromHeader] SerbleAuthorizationHeader authorizationHeader) {
        if (!authorizationHeader.Check(out string? scopes, out SerbleAuthorizationHeaderType? authType, out string? _, out User target)) {
            return Unauthorized();
        }

        ScopeHandler.ScopesEnum[] scopesEnums = ScopeHandler.ScopeStringToEnums(scopes).ToArray();
        if (authType == SerbleAuthorizationHeaderType.App &&
            !scopesEnums.Contains(ScopeHandler.ScopesEnum.PaymentInfo) && !scopesEnums.Contains(ScopeHandler.ScopesEnum.FullAccess)) {
            return Forbid("Insufficient scope");
        }
        return Ok(ProductManager.ListOfProductsFromUser(target));
    }
    
    [HttpOptions]
    public ActionResult Options() {
        HttpContext.Response.Headers.Add("Allow", "GET, OPTIONS");
        return Ok();
    }
    
}