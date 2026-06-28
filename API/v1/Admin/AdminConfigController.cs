using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Admin;

/// <summary>
/// Admin-only read/update of server-wide settings. The catalog of known settings lives in
/// <see cref="SerbleAPI.Config.ServerConfigCatalog"/>; this controller renders it generically so the
/// dashboard can list every setting (with its metadata + current value) and edit it.
/// </summary>
[ApiController]
[Route("api/v1/admin/config")]
[Authorize(Policy = "AdminOnly")]
public class AdminConfigController(IServerConfigService config) : ControllerManager {

    public class ConfigItemDto {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Description { get; set; } = "";
        public string Type { get; set; } = "";
        public string Default { get; set; } = "";
        public bool Public { get; set; }
        public string Value { get; set; } = "";

        public static ConfigItemDto From(ServerConfigItem i) => new() {
            Key         = i.Definition.Key,
            Label       = i.Definition.Label,
            Description = i.Definition.Description,
            Type        = i.Definition.Type.ToString(),
            Default     = i.Definition.Default,
            Public      = i.Definition.Public,
            Value       = i.Value
        };
    }

    public class SetConfigBody {
        public string? Value { get; set; }
    }

    /// <summary>Lists every known setting with its current value.</summary>
    [HttpGet]
    public async Task<ActionResult<ConfigItemDto[]>> GetAll() {
        IReadOnlyList<ServerConfigItem> items = await config.GetAll();
        return Ok(items.Select(ConfigItemDto.From).ToArray());
    }

    /// <summary>Updates one setting's value. Returns the updated item, or 400 on a bad key/value.</summary>
    [HttpPut("{key}")]
    public async Task<ActionResult<ConfigItemDto>> Set(string key, [FromBody] SetConfigBody body) {
        ServerConfigSetResult result = await config.Set(key, body.Value);
        if (!result.Success) return BadRequest(result.Error);
        return Ok(ConfigItemDto.From(result.Item!));
    }
}
