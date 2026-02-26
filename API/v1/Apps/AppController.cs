using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using SerbleAPI.Authentication;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Apps;

[ApiController]
[Route("api/v1/app/")]
public class AppController(IAppRepository appRepo, IUserRepository userRepo) : ControllerManager {

    // Public endpoint — no auth required
    [HttpGet("{appid}/public")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicInfo(string appid) {
        OAuthApp? app = await appRepo.GetOAuthApp(appid);
        if (app == null) return NotFound();
        return Ok(JsonConvert.SerializeObject(new SanitisedOAuthApp(app)));
    }

    [HttpGet("{appid}")]
    [Authorize(Policy = "Scope:ManageApps")]
    public async Task<IActionResult> GetInfo(string appid) {
        User? target = await HttpContext.User.GetUser(userRepo);
        if (target == null) return Unauthorized();
        OAuthApp? app = await appRepo.GetOAuthApp(appid);
        if (app == null) return NotFound();
        if (target.Id != app.OwnerId) return BadRequest("User does not own app");
        return Ok(JsonConvert.SerializeObject(app));
    }

    [HttpGet]
    [Authorize(Policy = "Scope:ManageApps")]
    public async Task<IActionResult> GetAll() {
        User? target = await HttpContext.User.GetUser(userRepo);
        if (target == null) return Unauthorized();
        OAuthApp[] apps = await appRepo.GetOAuthAppsFromUser(target.Id);
        return Ok(JsonConvert.SerializeObject(apps));
    }

    [HttpDelete("{appid}")]
    [Authorize(Policy = "Scope:ManageApps")]
    public async Task<IActionResult> Delete(string appid) {
        User? target = await HttpContext.User.GetUser(userRepo);
        if (target == null) return Unauthorized();
        OAuthApp? app = await appRepo.GetOAuthApp(appid);
        if (app == null) return NotFound();
        if (target.Id != app.OwnerId) return Forbid("User does not own app");
        await appRepo.DeleteOAuthApp(appid);
        return Ok();
    }

    [HttpPost]
    [Authorize(Policy = "Scope:ManageApps")]
    public async Task<IActionResult> CreateApp([FromBody] NewOAuthApp app) {
        User? target = await HttpContext.User.GetUser(userRepo);
        if (target == null) return Unauthorized();
        await appRepo.AddOAuthApp(new OAuthApp(target.Id) {
            Description = app.Description,
            Name        = app.Name,
            RedirectUri = app.RedirectUri
        });
        return Ok();
    }

    [HttpPatch("{appid}")]
    [Authorize(Policy = "Scope:AppsControl")]
    public async Task<ActionResult<OAuthApp>> EditApp([FromBody] AppEditRequest[] edits, string appid) {
        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        OAuthApp? target = await appRepo.GetOAuthApp(appid);
        if (target == null) return NotFound();

        OAuthApp newApp = target;
        foreach (AppEditRequest editRequest in edits) {
            if (!editRequest.TryApplyChanges(newApp, out OAuthApp modApp, out string applyErrorMsg))
                return BadRequest(applyErrorMsg);
            newApp = modApp;
        }
        await appRepo.UpdateOAuthApp(newApp);
        return newApp;
    }
}