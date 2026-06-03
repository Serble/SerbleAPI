using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Admin;

/// <summary>
/// Admin-only management of an app's OIDC client configuration and its access policy.
/// The access policy, group allow/deny lists and group→claim mappings are admin-owned
/// metadata that must never be exposed through owner-facing app responses, so they live
/// behind this AdminOnly controller and the dedicated <see cref="IAppAccessRepository"/>.
/// </summary>
[ApiController]
[Route("api/v1/admin/apps")]
[Authorize(Policy = "AdminOnly")]
public class AdminAppAccessController(
    ILogger<AdminAppAccessController> logger,
    IAppRepository appRepo,
    IAppAccessRepository accessRepo) : ControllerManager {

    public class ClientConfigBody {
        public List<string>? AdditionalRedirectUris { get; set; }
        public bool? IsPublicClient { get; set; }
        public bool? RequirePkce { get; set; }
    }

    public class AccessPolicyBody {
        public AppAccessPolicy AccessPolicy { get; set; }
        public int? RequiredPermLevel { get; set; }
    }

    public class GroupRulesBody {
        public string[] AllowedGroupIds { get; set; } = [];
        public string[] DeniedGroupIds { get; set; } = [];
    }

    public class ClaimMappingsBody {
        public Dictionary<string, string> Mappings { get; set; } = new();
    }

    // -------- OIDC client config --------

    [HttpGet("{id}/client")]
    public async Task<IActionResult> GetClient(string id) {
        OAuthApp? app = await appRepo.GetOAuthApp(id);
        if (app == null) return NotFound();
        return Ok(new {
            app.Id,
            app.RedirectUri,
            app.AdditionalRedirectUris,
            app.IsPublicClient,
            app.RequirePkce
        });
    }

    [HttpPut("{id}/client")]
    public async Task<IActionResult> SetClient(string id, [FromBody] ClientConfigBody body) {
        OAuthApp? app = await appRepo.GetOAuthApp(id);
        if (app == null) return NotFound();
        if (body.AdditionalRedirectUris != null) app.AdditionalRedirectUris = body.AdditionalRedirectUris;
        if (body.IsPublicClient         != null) app.IsPublicClient         = body.IsPublicClient.Value;
        if (body.RequirePkce            != null) app.RequirePkce            = body.RequirePkce.Value;
        await appRepo.UpdateOAuthApp(app);
        logger.LogInformation("Admin {AdminId} updated OIDC client config for app {AppId}",
            HttpContext.User.GetUserId(), id);
        return Ok(new { success = true });
    }

    // -------- Access policy --------

    [HttpGet("{id}/access")]
    public async Task<ActionResult<AppAccessConfig>> GetAccess(string id) {
        OAuthApp? app = await appRepo.GetOAuthApp(id);
        if (app == null) return NotFound();
        return Ok(await accessRepo.GetAppAccessConfig(id));
    }

    [HttpPut("{id}/access/policy")]
    public async Task<IActionResult> SetPolicy(string id, [FromBody] AccessPolicyBody body) {
        OAuthApp? app = await appRepo.GetOAuthApp(id);
        if (app == null) return NotFound();
        await accessRepo.SetAccessPolicy(id, body.AccessPolicy, body.RequiredPermLevel);
        logger.LogInformation("Admin {AdminId} set access policy {Policy} for app {AppId}",
            HttpContext.User.GetUserId(), body.AccessPolicy, id);
        return Ok(new { success = true });
    }

    [HttpPut("{id}/access/groups")]
    public async Task<IActionResult> SetGroupRules(string id, [FromBody] GroupRulesBody body) {
        OAuthApp? app = await appRepo.GetOAuthApp(id);
        if (app == null) return NotFound();
        await accessRepo.SetGroupRules(id, body.AllowedGroupIds, body.DeniedGroupIds);
        return Ok(new { success = true });
    }

    [HttpPut("{id}/access/claim-mappings")]
    public async Task<IActionResult> SetClaimMappings(string id, [FromBody] ClaimMappingsBody body) {
        OAuthApp? app = await appRepo.GetOAuthApp(id);
        if (app == null) return NotFound();
        await accessRepo.SetGroupClaimMappings(id, body.Mappings);
        return Ok(new { success = true });
    }
}
