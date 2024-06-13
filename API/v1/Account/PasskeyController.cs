using Fido2NetLib;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.API.v1.Account;

[ApiController]
[Route("api/v1/auth/passkey")]
public class PasskeyController(IFido2 fido) : ControllerManager {
    private IFido2 _fido = fido;
    
    [HttpPost("create")]
    public IActionResult PasskeyAuth([FromHeader] SerbleAuthorizationHeader auth) {
        if (!auth.CheckAndGetInfo(out User? user, 
                out _, 
                ScopeHandler.ScopesEnum.FullAccess, 
                false)) {
            return Unauthorized();
        }
        
        
    }
}