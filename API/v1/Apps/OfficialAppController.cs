using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;

namespace SerbleAPI.API.v1.Apps;

/// <summary>
/// Scaffold for official-app-only endpoints. Every route here requires the
/// <c>OfficialAppOnly</c> policy, so it can only be reached by an OAuth app access
/// token whose app is flagged official. Add new first-party-only endpoints here (or
/// apply <c>[Authorize(Policy = "OfficialAppOnly")]</c> to any other action).
/// </summary>
[ApiController]
[Route("api/v1/official")]
[Authorize(Policy = "OfficialAppOnly")]
public class OfficialAppController : ControllerManager {

    /// <summary>
    /// Returns 200 if the calling app is official, otherwise 403 (handled by the policy).
    /// Useful as a cheap capability probe before calling other official-only endpoints.
    /// </summary>
    [HttpGet("check")]
    public IActionResult Check() {
        return Ok(new { official = true, appId = HttpContext.User.GetAppId() });
    }
}
