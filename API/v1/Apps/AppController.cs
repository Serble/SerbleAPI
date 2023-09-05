using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using SerbleAPI.Data;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.API.v1.Apps; 

[ApiController]
[Route("api/v1/app/")]
public class AppController : ControllerManager {
    
    [HttpGet("{appid}/public")]
    public IActionResult GetPublicInfo(string appid) {
        Program.StorageService!.GetOAuthApp(appid, out OAuthApp? app);
        if (app == null) {
            return NotFound();
        }
        string jsonObj = JsonConvert.SerializeObject(new SanitisedOAuthApp(app));
        return Ok(jsonObj);
    }
    
    [HttpGet("{appid}")]
    public IActionResult GetInfo(string appid, [FromHeader] SerbleAuthorizationHeader authorizationHeader) {
        if (!authorizationHeader.Check(out string? scopes, out SerbleAuthorizationHeaderType? authType, out string? msg, out User target)) {
            return BadRequest(msg);
        }
        
        if (!scopes.SerbleHasScope(ScopeHandler.ScopesEnum.ManageApps)) {
            return Forbid("Scope ManageApps is required.");
        }
        
        Program.StorageService!.GetOAuthApp(appid, out OAuthApp? app);
        if (app == null) {
            return NotFound();
        }
        
        if (target.Id != app.OwnerId) {
            return BadRequest("User does not own app");
        }
        
        string jsonObj = JsonConvert.SerializeObject(app);
        return Ok(jsonObj);
    }
    
    [HttpGet]
    public IActionResult GetAll([FromHeader] SerbleAuthorizationHeader authorizationHeader) {
        if (!authorizationHeader.Check(out string? scopes, out SerbleAuthorizationHeaderType? authType, out string? msg, out User target)) {
            return Unauthorized(msg);
        }
        
        if (!scopes.SerbleHasScope(ScopeHandler.ScopesEnum.ManageApps)) {
            return Forbid("Scope ManageApps is required.");
        }
        
        Program.StorageService!.GetOAuthAppsFromUser(target.Id, out OAuthApp[] apps);

        string jsonObj = JsonConvert.SerializeObject(apps);
        return Ok(jsonObj);
    }
    
    [HttpDelete("{appid}")]
    public IActionResult Delete(string appid, [FromHeader] SerbleAuthorizationHeader authorizationHeader) {
        if (!authorizationHeader.Check(out string? scopes, out SerbleAuthorizationHeaderType? authType, out string? msg, out User target)) {
            return Unauthorized(msg);
        }
        
        if (!scopes.SerbleHasScope(ScopeHandler.ScopesEnum.ManageApps)) {
            return Forbid("Scope ManageApps is required.");
        }

        Program.StorageService!.GetOAuthApp(appid, out OAuthApp? app);
        if (app == null) {
            return NotFound();
        }
        
        if (target.Id != app.OwnerId) {
            return Forbid("User does not own app");
        }
        
        Program.StorageService.DeleteOAuthApp(appid);
        return Ok();
    }
    
    [HttpPost]
    public IActionResult CreateApp([FromHeader] SerbleAuthorizationHeader authorizationHeader, [FromBody] NewOAuthApp app) {
        if (!authorizationHeader.Check(out string? scopes, out SerbleAuthorizationHeaderType? _, out string? msg, out User target)) {
            return Unauthorized(msg);
        }
        
        if (!scopes.SerbleHasScope(ScopeHandler.ScopesEnum.ManageApps)) {
            return Forbid("Scope ManageApps is required.");
        }

        Program.StorageService!.AddOAuthApp(new OAuthApp(target.Id) {Description = app.Description, Name = app.Name, RedirectUri = app.RedirectUri});
        return Ok();
    }
    
    [HttpPatch("{appid}")]
    public ActionResult<OAuthApp> EditApp([FromHeader] SerbleAuthorizationHeader authorizationHeader, [FromBody] AppEditRequest[] edits, string appid) {
        if (!authorizationHeader.Check(out string scope, out SerbleAuthorizationHeaderType? _, out string? msg, out User user)) {
            return Unauthorized();
        }

        if (!scope.SerbleHasScope(ScopeHandler.ScopesEnum.AppsControl)) {
            return Forbid("Scope AppsControl is required.");
        }
        
        Program.StorageService!.GetOAuthApp(appid, out OAuthApp? target);
        if (target == null) {
            return NotFound();
        }
        
        OAuthApp newApp = target;
        foreach (AppEditRequest editRequest in edits) {
            if (!editRequest.TryApplyChanges(newApp, out OAuthApp modApp, out string applyErrorMsg)) {
                return BadRequest(applyErrorMsg);
            }
            newApp = modApp;
        }
        
        Program.StorageService.UpdateOAuthApp(newApp);
        return newApp;
    }

    [HttpOptions("{appid}/public")]
    public ActionResult OptionsPublic() {
        HttpContext.Response.Headers.Add("Allow", "GET, OPTIONS");
        return Ok();
    }
    
    [HttpOptions("{appid}")]
    public ActionResult OptionsAppId() {
        HttpContext.Response.Headers.Add("Allow", "GET, DELETE, PATCH, OPTIONS");
        return Ok();
    }
    
    [HttpOptions]
    public ActionResult Options() {
        HttpContext.Response.Headers.Add("Allow", "POST, OPTIONS");
        return Ok();
    }

}