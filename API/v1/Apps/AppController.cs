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
public class AppController(
    ILogger<AppController> logger,
    IAppRepository appRepo,
    IUserRepository userRepo,
    IAppApiKeyRepository keyRepo,
    IBalanceRepository balanceRepo) : ControllerManager {

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

    // -------- API keys (owner-managed) --------

    public class CreateApiKeyBody {
        public string Name { get; set; } = "";
    }

    /// <summary>Loads an app and ensures the authenticated user owns it.</summary>
    private async Task<(OAuthApp? app, ActionResult? error)> GetOwnedApp(string appid) {
        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return (null, Unauthorized());
        OAuthApp? app = await appRepo.GetOAuthApp(appid);
        if (app == null) return (null, NotFound());
        if (app.OwnerId != user.Id) return (null, Forbid());
        return (app, null);
    }

    [HttpPost("{appid}/keys")]
    [Authorize(Policy = "Scope:ManageApps")]
    public async Task<IActionResult> CreateApiKey(string appid, [FromBody] CreateApiKeyBody body) {
        (OAuthApp? app, ActionResult? error) = await GetOwnedApp(appid);
        if (error != null) return error;
        if (string.IsNullOrWhiteSpace(body.Name)) return BadRequest("Key name is required");
        if (body.Name.Length > 128) return BadRequest("Key name cannot be longer than 128 characters");

        CreatedAppApiKey created = await keyRepo.CreateKey(app!.Id, body.Name.Trim());
        logger.LogInformation("User {UserId} created API key {KeyId} for app {AppId}",
            HttpContext.User.GetUserId(), created.Info.Id, appid);
        // Plaintext key is returned only here and never again.
        return Ok(new {
            id          = created.Info.Id,
            name        = created.Info.Name,
            keyPrefix   = created.Info.KeyPrefix,
            dateCreated = created.Info.DateCreated,
            key         = created.PlaintextKey
        });
    }

    [HttpGet("{appid}/keys")]
    [Authorize(Policy = "Scope:ManageApps")]
    public async Task<IActionResult> ListApiKeys(string appid) {
        (OAuthApp? app, ActionResult? error) = await GetOwnedApp(appid);
        if (error != null) return error;
        AppApiKeyInfo[] keys = await keyRepo.GetKeysForApp(app!.Id);
        return Ok(keys.Select(k => new {
            id          = k.Id,
            name        = k.Name,
            keyPrefix   = k.KeyPrefix,
            dateCreated = k.DateCreated
        }));
    }

    [HttpDelete("{appid}/keys/{keyId}")]
    [Authorize(Policy = "Scope:ManageApps")]
    public async Task<IActionResult> DeleteApiKey(string appid, string keyId) {
        (OAuthApp? app, ActionResult? error) = await GetOwnedApp(appid);
        if (error != null) return error;
        bool deleted = await keyRepo.DeleteKey(app!.Id, keyId);
        if (!deleted) return NotFound("API key not found");
        logger.LogInformation("User {UserId} deleted API key {KeyId} for app {AppId}",
            HttpContext.User.GetUserId(), keyId, appid);
        return Ok(new { success = true });
    }

    // -------- App balance (owner-managed, read-only) --------

    public class AppBalanceResponse {
        public string AppId { get; set; } = "";
        public string BalanceId { get; set; } = "";
        public ulong Coins { get; set; }
    }

    private static AppBalanceResponse BalanceResponse(string appId, Balance b) => new() {
        AppId = appId, BalanceId = b.Id, Coins = b.Coins
    };

    [HttpGet("{appid}/balance")]
    [Authorize(Policy = "Scope:Economy")]
    public async Task<ActionResult<AppBalanceResponse>> GetAppBalance(string appid) {
        (OAuthApp? app, ActionResult? error) = await GetOwnedApp(appid);
        if (error != null) return error;
        Balance bal = await balanceRepo.GetBalance(BalanceOwnerType.App, app!.Id);
        return Ok(BalanceResponse(app.Id, bal));
    }

    // -------- App self (API key auth) --------

    /// <summary>
    /// Capabilities check for an app authenticated with its own API key. Returns basic identity
    /// info and confirms the key is valid.
    /// </summary>
    [HttpGet("me/capabilities")]
    [Authorize(Policy = "AppOnly")]
    public async Task<IActionResult> Capabilities() {
        string? appId = HttpContext.User.GetAppId();
        if (appId == null) return Unauthorized();
        OAuthApp? app = await appRepo.GetOAuthApp(appId);
        if (app == null) return NotFound();
        return Ok(new {
            authenticated = true,
            appId         = app.Id,
            name          = app.Name,
            isOfficial    = app.IsOfficial
        });
    }
}