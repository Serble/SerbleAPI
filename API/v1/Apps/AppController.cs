using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using SerbleAPI.Authentication;
using SerbleAPI.Data;
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
    IBalanceRepository balanceRepo,
    IItemRepository itemRepo) : ControllerManager {

    // Public endpoint — no auth required
    [HttpGet("{appid}/public")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicInfo(string appid) {
        OAuthApp? app = await appRepo.GetOAuthApp(appid);
        if (app == null) return NotFound();
        return Ok(JsonConvert.SerializeObject(new SanitisedOAuthApp(app)));
    }

    public class BatchPublicAppsBody {
        public string[] Ids { get; set; } = [];
    }

    /// <summary>
    /// Resolves many app ids to their public info in a single request — used by clients (e.g. the
    /// inventory view) that need the creating-app details for a list of items without an N+1 of
    /// <see cref="GetPublicInfo"/> calls. Unknown ids are simply omitted from the result.
    /// </summary>
    [HttpPost("public/batch")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicInfoBatch([FromBody] BatchPublicAppsBody body) {
        string[] ids = (body.Ids ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .Take(200)
            .ToArray();
        if (ids.Length == 0) return Ok(JsonConvert.SerializeObject(Array.Empty<SanitisedOAuthApp>()));
        OAuthApp[] apps = await appRepo.GetOAuthApps(ids);
        return Ok(JsonConvert.SerializeObject(apps.Select(a => new SanitisedOAuthApp(a)).ToArray()));
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

    // -------- App items (owner-managed, read-only) --------

    public class AppItemResponse {
        public string Id { get; set; } = "";
        public string ReadableId { get; set; } = "";
        public string OwnerType { get; set; } = "";
        public string OwnerId { get; set; } = "";
        public string CreatorAppId { get; set; } = "";
        public DateTime DateCreated { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? IconUrl { get; set; }

        public static AppItemResponse From(Item i) => new() {
            Id           = i.Id,
            ReadableId   = WordId.Encode(i.Id),
            OwnerType    = i.OwnerType.ToString(),
            OwnerId      = i.OwnerId,
            CreatorAppId = i.CreatorAppId,
            DateCreated  = i.DateCreated,
            Name         = i.Name,
            Description  = i.Description,
            IconUrl      = i.IconUrl
        };
    }

    /// <summary>
    /// Lists items owned by an app the authenticated user owns (newest first), paginated. This is
    /// the owner-facing view of an app's items.
    /// </summary>
    [HttpGet("{appid}/items")]
    [Authorize(Policy = "Scope:ManageApps")]
    public async Task<ActionResult<AppItemResponse[]>> GetAppItems(
        string appid,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0) {
        (OAuthApp? app, ActionResult? error) = await GetOwnedApp(appid);
        if (error != null) return error;

        limit = Math.Clamp(limit, 1, 200);
        offset = Math.Max(0, offset);

        Item[] items = await itemRepo.GetItemsForOwner(BalanceOwnerType.App, app!.Id, limit, offset);
        return Ok(items.Select(AppItemResponse.From).ToArray());
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