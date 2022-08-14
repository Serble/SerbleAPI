using GeneralPurposeLib;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.API.v1.Account;

[ApiController]
[Route("api/v1/account/")]
public class AccountController : ControllerManager {
    
    [HttpGet]
    public ActionResult<SanitisedUser> Get([FromHeader] SerbleAuthorizationHeader authorizationHeader) {
        if (!authorizationHeader.Check(out string? scopes, out SerbleAuthorizationHeaderType? _, out string? msg, out User target)) {
            Logger.Debug("Check failed: " + msg);
            return Unauthorized();
        }

        return new SanitisedUser(target, scopes);
    }
    
    [HttpDelete]
    public ActionResult Delete([FromHeader] SerbleAuthorizationHeader authorizationHeader) {
        if (!authorizationHeader.Check(out string scopes, out SerbleAuthorizationHeaderType? _, out string msg, out User target)) {
            Logger.Debug("Check failed: " + msg);
            return Unauthorized();
        }

        IEnumerable<ScopeHandler.ScopesEnum> scopesListEnum = ScopeHandler.ScopesIdsToEnumArray(ScopeHandler.StringToListOfScopeIds(scopes));
        if (!scopesListEnum.Contains(ScopeHandler.ScopesEnum.FullAccess)) {
            Logger.Debug("No full access");
            return Unauthorized();
        }
        
        // Delete the user's account
        Program.StorageService!.DeleteUser(target.Id);
        return Ok();
    }

    [HttpPost]
    public ActionResult<SanitisedUser> Register([FromBody] RegisterRequestBody requestBody) {
        Program.StorageService!.GetUserFromName(requestBody.Username, out User? existingUser);
        if (existingUser != null) {
            return Conflict("User already exists");
        }
        User newUser = new() {
            Username = requestBody.Username,
            PasswordHash = requestBody.Password.Sha256Hash(),
            PermLevel = 1,
            PermString = "0"
        };
        Program.StorageService.AddUser(newUser, out User user);
        return Ok(new SanitisedUser(user, "1", true)); // Ignore authed apps to stop error
    }

    [HttpPatch]
    public ActionResult<SanitisedUser> EditAccount([FromHeader] SerbleAuthorizationHeader authorizationHeader, [FromBody] AccountEditRequest[] edits) {
        if (!authorizationHeader.Check(out string? scopes, out SerbleAuthorizationHeaderType? _, out string? msg, out User target)) {
            Logger.Debug("Check failed: " + msg);
            return Unauthorized();
        }

        User newUser = target;
        foreach (AccountEditRequest editRequest in edits) {
            if (!editRequest.TryApplyChanges(newUser, out User modUser, out string applyErrorMsg)) {
                return BadRequest(applyErrorMsg);
            }
            newUser = modUser;
        }
        Program.StorageService!.UpdateUser(newUser);

        return new SanitisedUser(target, scopes);
    }

    [HttpOptions]
    public ActionResult Options() {
        HttpContext.Response.Headers.Add("Allow", "GET, DELETE, POST, PATCH, OPTIONS");
        return Ok();
    }
    
}

public class Adam {
    public Adam() {
        Logger.Error("OH NO U UNLEASHED ADAM");
        throw new Exception("Adam");
    }
}