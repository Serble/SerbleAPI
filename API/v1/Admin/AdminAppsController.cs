using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Admin;

/// <summary>
/// Admin-only management endpoints for OAuth apps. Mirrors AdminUsersController
/// shape: list/search, lookup by id, list-by-owner, edit, delete, secret cycle.
/// </summary>
[ApiController]
[Route("api/v1/admin/apps")]
[Authorize(Policy = "AdminOnly")]
public class AdminAppsController(
    ILogger<AdminAppsController> logger,
    IAppRepository appRepo,
    IUserRepository userRepo) : ControllerManager {

    // -------- Stats --------

    public class AppStatsResponse {
        public long TotalApps { get; set; }
    }

    [HttpGet("stats")]
    public async Task<ActionResult<AppStatsResponse>> Stats() {
        return Ok(new AppStatsResponse { TotalApps = await appRepo.CountApps() });
    }

    // -------- Search / lookup --------

    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<AdminAppView>>> Search(
        [FromQuery] string? query = null, [FromQuery] int limit = 25) {
        OAuthApp[] results = await appRepo.SearchApps(query ?? "", limit);
        return Ok(results.Select(AdminAppView.From));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AdminAppView>> Get(string id) {
        OAuthApp? app = await appRepo.GetOAuthApp(id);
        if (app == null) return NotFound();
        return Ok(AdminAppView.From(app));
    }

    /// <summary>Find all apps owned by the specified user.</summary>
    [HttpGet("by-user/{userId}")]
    public async Task<ActionResult<IEnumerable<AdminAppView>>> ByUser(string userId) {
        User? user = await userRepo.GetUser(userId);
        if (user == null) return NotFound("User not found");
        OAuthApp[] apps = await appRepo.GetOAuthAppsFromUser(userId);
        return Ok(apps.Select(AdminAppView.From));
    }

    // -------- Edit --------

    public class EditAppBody {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? RedirectUri { get; set; }
        public string? OwnerId { get; set; }
    }

    [HttpPatch("{id}")]
    public async Task<ActionResult<AdminAppView>> Edit(string id, [FromBody] EditAppBody body) {
        OAuthApp? app = await appRepo.GetOAuthApp(id);
        if (app == null) return NotFound();

        if (body.OwnerId != null && body.OwnerId != app.OwnerId) {
            User? newOwner = await userRepo.GetUser(body.OwnerId);
            if (newOwner == null) return BadRequest("New owner does not exist");
            app.OwnerId = body.OwnerId;
        }
        if (body.Name        != null) app.Name        = body.Name;
        if (body.Description != null) app.Description = body.Description;
        if (body.RedirectUri != null) app.RedirectUri = body.RedirectUri;

        await appRepo.UpdateOAuthApp(app);
        logger.LogInformation("Admin {AdminId} edited app {AppId}",
            HttpContext.User.GetUserId(), id);
        return Ok(AdminAppView.From(app));
    }

    // -------- Official flag --------

    public class OfficialBody {
        public bool IsOfficial { get; set; }
    }

    /// <summary>
    /// Mark or unmark an app as official (first-party). Admin-only and settable
    /// regardless of the app's owner.
    /// </summary>
    [HttpPut("{id}/official")]
    public async Task<ActionResult<AdminAppView>> SetOfficial(string id, [FromBody] OfficialBody body) {
        OAuthApp? app = await appRepo.GetOAuthApp(id);
        if (app == null) return NotFound();
        app.IsOfficial = body.IsOfficial;
        await appRepo.UpdateOAuthApp(app);
        logger.LogInformation("Admin {AdminId} set official={IsOfficial} for app {AppId}",
            HttpContext.User.GetUserId(), body.IsOfficial, id);
        return Ok(AdminAppView.From(app));
    }

    // -------- Cycle client secret --------

    [HttpPost("{id}/cycle-secret")]
    public async Task<IActionResult> CycleSecret(string id) {
        OAuthApp? app = await appRepo.GetOAuthApp(id);
        if (app == null) return NotFound();
        app.CycleClientSecret();
        await appRepo.UpdateOAuthApp(app);
        logger.LogWarning("Admin {AdminId} cycled client secret for app {AppId}",
            HttpContext.User.GetUserId(), id);
        return Ok(new { success = true, clientSecret = app.ClientSecret });
    }

    // -------- Delete --------

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id) {
        OAuthApp? app = await appRepo.GetOAuthApp(id);
        if (app == null) return NotFound();
        await appRepo.DeleteOAuthApp(id);
        logger.LogWarning("Admin {AdminId} DELETED app {AppId} (owner {OwnerId})",
            HttpContext.User.GetUserId(), id, app.OwnerId);
        return Ok(new { success = true });
    }
}

/// <summary>
/// Admin-facing app view. Includes ClientSecret because admins are trusted to
/// see and rotate it; never expose this through non-admin endpoints.
/// </summary>
public class AdminAppView {
    public string Id { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public bool IsOfficial { get; set; }

    public static AdminAppView From(OAuthApp a) => new() {
        Id           = a.Id,
        OwnerId      = a.OwnerId,
        Name         = a.Name,
        Description  = a.Description,
        RedirectUri  = a.RedirectUri,
        ClientSecret = a.ClientSecret,
        IsOfficial   = a.IsOfficial
    };
}
