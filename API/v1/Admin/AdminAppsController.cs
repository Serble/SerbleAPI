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
    IBalanceRepository balanceRepo,
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
        List<AdminAppView> views = new(results.Length);
        foreach (OAuthApp a in results) {
            Balance bal = await balanceRepo.GetBalance(BalanceOwnerType.App, a.Id);
            views.Add(AdminAppView.From(a, bal.Coins));
        }
        return Ok(views);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AdminAppView>> Get(string id) {
        OAuthApp? app = await appRepo.GetOAuthApp(id);
        if (app == null) return NotFound();
        Balance bal = await balanceRepo.GetBalance(BalanceOwnerType.App, id);
        return Ok(AdminAppView.From(app, bal.Coins));
    }

    /// <summary>Find all apps owned by the specified user.</summary>
    [HttpGet("by-user/{userId}")]
    public async Task<ActionResult<IEnumerable<AdminAppView>>> ByUser(string userId) {
        User? user = await userRepo.GetUser(userId);
        if (user == null) return NotFound("User not found");
        OAuthApp[] apps = await appRepo.GetOAuthAppsFromUser(userId);
        List<AdminAppView> views = new(apps.Length);
        foreach (OAuthApp a in apps) {
            Balance bal = await balanceRepo.GetBalance(BalanceOwnerType.App, a.Id);
            views.Add(AdminAppView.From(a, bal.Coins));
        }
        return Ok(views);
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

    // -------- Coins (economy) --------

    public class AppCoinBalanceResponse {
        public string AppId { get; set; } = "";
        public string BalanceId { get; set; } = "";
        public ulong Coins { get; set; }
    }

    public class SetCoinsBody {
        public ulong Balance { get; set; }
    }

    public class CoinAmountBody {
        public ulong Amount { get; set; }
    }

    [HttpGet("{id}/coins")]
    [Authorize(Policy = "Scope:Economy")]
    public async Task<ActionResult<AppCoinBalanceResponse>> GetCoins(string id) {
        OAuthApp? app = await appRepo.GetOAuthApp(id);
        if (app == null) return NotFound();
        Balance bal = await balanceRepo.GetBalance(BalanceOwnerType.App, id);
        return Ok(new AppCoinBalanceResponse { AppId = id, BalanceId = bal.Id, Coins = bal.Coins });
    }

    [HttpPost("{id}/coins/set")]
    [Authorize(Policy = "Scope:Economy")]
    public async Task<ActionResult<AppCoinBalanceResponse>> SetCoins(string id, [FromBody] SetCoinsBody body) {
        OAuthApp? app = await appRepo.GetOAuthApp(id);
        if (app == null) return NotFound();
        Balance bal = await balanceRepo.SetBalance(BalanceOwnerType.App, id, body.Balance);
        logger.LogInformation("Admin {AdminId} set coins of app {AppId} to {Balance}",
            HttpContext.User.GetUserId(), id, body.Balance);
        return Ok(new AppCoinBalanceResponse { AppId = id, BalanceId = bal.Id, Coins = bal.Coins });
    }

    [HttpPost("{id}/coins/add")]
    [Authorize(Policy = "Scope:Economy")]
    public async Task<ActionResult<AppCoinBalanceResponse>> AddCoins(string id, [FromBody] CoinAmountBody body) {
        OAuthApp? app = await appRepo.GetOAuthApp(id);
        if (app == null) return NotFound();
        Balance bal = await balanceRepo.AddCoins(BalanceOwnerType.App, id, body.Amount);
        logger.LogInformation("Admin {AdminId} added {Amount} coins to app {AppId} (new balance {Balance})",
            HttpContext.User.GetUserId(), body.Amount, id, bal.Coins);
        return Ok(new AppCoinBalanceResponse { AppId = id, BalanceId = bal.Id, Coins = bal.Coins });
    }

    [HttpPost("{id}/coins/remove")]
    [Authorize(Policy = "Scope:Economy")]
    public async Task<ActionResult<AppCoinBalanceResponse>> RemoveCoins(string id, [FromBody] CoinAmountBody body) {
        OAuthApp? app = await appRepo.GetOAuthApp(id);
        if (app == null) return NotFound();
        Balance bal = await balanceRepo.RemoveCoins(BalanceOwnerType.App, id, body.Amount);
        logger.LogInformation("Admin {AdminId} removed {Amount} coins from app {AppId} (new balance {Balance})",
            HttpContext.User.GetUserId(), body.Amount, id, bal.Coins);
        return Ok(new AppCoinBalanceResponse { AppId = id, BalanceId = bal.Id, Coins = bal.Coins });
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
    public ulong Coins { get; set; }
    public DateTime DateCreated { get; set; }

    public static AdminAppView From(OAuthApp a, ulong coins = 0) => new() {
        Id           = a.Id,
        OwnerId      = a.OwnerId,
        Name         = a.Name,
        Description  = a.Description,
        RedirectUri  = a.RedirectUri,
        ClientSecret = a.ClientSecret,
        IsOfficial   = a.IsOfficial,
        Coins        = coins,
        DateCreated  = a.DateCreated
    };
}
