using GeneralPurposeLib;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.API.v1;

[ApiController]
[Route("api/v1/account/")]
public class AccountController : ControllerManager {
    
    [HttpGet]
    public ActionResult<SanitisedUser> Get([FromHeader] SerbleAuthorizationHeader authorizationHeader) {
        if (!authorizationHeader.Check(out string? scopes, out SerbleAuthorizationHeaderType? _, out object? userObj, out string? msg)) {
            Logger.Debug("Check failed: " + msg);
            return Unauthorized();
        }

        if (scopes == null! || userObj == null!) {
            Logger.Debug("NULL");
            return Unauthorized();
        }
        User user = (User) userObj;

        return new SanitisedUser(user, scopes);
    }
    
    [HttpDelete]
    public ActionResult Delete([FromHeader] SerbleAuthorizationHeader authorizationHeader) {
        if (!authorizationHeader.Check(out string? scopes, out SerbleAuthorizationHeaderType? _, out object? userObj, out string? msg)) {
            Logger.Debug("Check failed: " + msg);
            return Unauthorized();
        }
        
        if (scopes == null! || userObj == null!) {
            Logger.Debug("NULL");
            return Unauthorized();
        }
        User user = (User) userObj;

        IEnumerable<ScopeHandler.ScopesEnum> scopesListEnum = ScopeHandler.ScopesIdsToEnumArray(ScopeHandler.StringToListOfScopeIds(scopes));
        if (!scopesListEnum.Contains(ScopeHandler.ScopesEnum.FullAccess)) {
            Logger.Debug("No full access");
            return Unauthorized();
        }
        
        // Delete the user's account
        Program.StorageService!.DeleteUser(user.Id);
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
        if (!authorizationHeader.Check(out string? scopes, out SerbleAuthorizationHeaderType? _, out object? userObj, out string? msg)) {
            Logger.Debug("Check failed: " + msg);
            return Unauthorized();
        }

        if (scopes == null! || userObj == null!) {
            Logger.Debug("NULL");
            return Unauthorized();
        }
        User user = (User) userObj;

        User newUser = user;
        foreach (AccountEditRequest editRequest in edits) {
            if (!editRequest.TryApplyChanges(newUser, out User modUser, out string applyErrorMsg)) {
                return BadRequest(applyErrorMsg);
            }
            newUser = modUser;
        }
        Program.StorageService!.UpdateUser(newUser);

        return new SanitisedUser(user, scopes);
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