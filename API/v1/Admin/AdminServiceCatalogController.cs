using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Admin;

/// <summary>
/// Admin-only CRUD for service catalog items and their group visibility rules.
/// Returns visibility metadata that must never be exposed through public routes.
/// </summary>
[ApiController]
[Route("api/v1/admin/service-catalog")]
[Authorize(Policy = "AdminOnly")]
public class AdminServiceCatalogController(
    ILogger<AdminServiceCatalogController> logger,
    IServiceCatalogRepository serviceRepo) : ControllerManager {

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AdminServiceCatalogView>>> List() {
        ServiceCatalogItem[] services = await serviceRepo.ListServices();
        return Ok(services.Select(AdminServiceCatalogView.From));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AdminServiceCatalogView>> Get(string id) {
        ServiceCatalogItem? service = await serviceRepo.GetService(id);
        if (service == null) return NotFound();
        return Ok(AdminServiceCatalogView.From(service));
    }

    [HttpPost]
    public async Task<ActionResult<AdminServiceCatalogView>> Create([FromBody] AdminServiceCatalogView body) {
        ActionResult? validationError = Validate(body);
        if (validationError != null) return validationError;

        ServiceCatalogItem service = body.ToService();
        bool created = await serviceRepo.CreateService(service);
        if (!created) return Conflict("A service with that Id already exists");

        logger.LogInformation("Admin {AdminId} created service catalog item {ServiceId}",
            HttpContext.User.GetUserId(), service.Id);
        return CreatedAtAction(nameof(Get), new { id = service.Id }, AdminServiceCatalogView.From(service));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<AdminServiceCatalogView>> Update(string id, [FromBody] AdminServiceCatalogView body) {
        body.Id = id;
        ActionResult? validationError = Validate(body);
        if (validationError != null) return validationError;

        ServiceCatalogItem service = body.ToService();
        bool updated = await serviceRepo.UpdateService(service);
        if (!updated) return NotFound();

        logger.LogInformation("Admin {AdminId} updated service catalog item {ServiceId}",
            HttpContext.User.GetUserId(), id);
        return Ok(AdminServiceCatalogView.From(service));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id) {
        bool ok = await serviceRepo.DeleteService(id);
        if (!ok) return NotFound();
        logger.LogWarning("Admin {AdminId} DELETED service catalog item {ServiceId}",
            HttpContext.User.GetUserId(), id);
        return Ok(new { success = true });
    }

    private ActionResult? Validate(AdminServiceCatalogView body) {
        if (string.IsNullOrWhiteSpace(body.Id)) return BadRequest("Id is required");
        if (string.IsNullOrWhiteSpace(body.Name)) return BadRequest("Name is required");
        if (string.IsNullOrWhiteSpace(body.Url)) return BadRequest("Url is required");
        if (!Uri.TryCreate(body.Url, UriKind.Absolute, out _)) return BadRequest("Url must be an absolute URL");
        if (!string.IsNullOrWhiteSpace(body.IconUrl) && !Uri.TryCreate(body.IconUrl, UriKind.Absolute, out _))
            return BadRequest("IconUrl must be an absolute URL");
        if (body.VisibilityMode == ServiceCatalogVisibilityMode.RestrictedToGroups && body.AllowedGroupIds.Length == 0)
            return BadRequest("Restricted services must have at least one allowed group");
        return null;
    }
}

public class AdminServiceCatalogView {
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Url { get; set; } = "";
    public string? IconUrl { get; set; }
    public ServiceCatalogVisibilityMode VisibilityMode { get; set; }
    public string[] AllowedGroupIds { get; set; } = [];

    public static AdminServiceCatalogView From(ServiceCatalogItem service) => new() {
        Id = service.Id,
        Name = service.Name,
        Description = service.Description,
        Url = service.Url,
        IconUrl = service.IconUrl,
        VisibilityMode = service.VisibilityMode,
        AllowedGroupIds = service.AllowedGroupIds ?? []
    };

    public ServiceCatalogItem ToService() => new() {
        Id = Id,
        Name = Name,
        Description = Description,
        Url = Url,
        IconUrl = IconUrl,
        VisibilityMode = VisibilityMode,
        AllowedGroupIds = AllowedGroupIds ?? []
    };
}
