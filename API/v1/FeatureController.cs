using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1;

[ApiController]
[Route("api/v1/features")]
[AllowAnonymous]
public class FeatureController(IFeatureFlagService flags) : ControllerManager {
    public class FeatureEnabledResponse {
        public string Feature { get; set; } = "";
        public bool Enabled { get; set; }
    }

    [HttpGet("{feature}/enabled")]
    public async Task<ActionResult<FeatureEnabledResponse>> IsEnabled(string feature) {
        bool? enabled = await flags.IsEnabled(feature, HttpContext.User);
        if (enabled == null) return NotFound();
        return Ok(new FeatureEnabledResponse { Feature = feature, Enabled = enabled.Value });
    }
}
