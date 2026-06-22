using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Apps;

[ApiController]
[Route("api/v1/catalog/services")]
public class ServiceCatalogController(
    IServiceCatalogRepository serviceRepo,
    IUserRepository userRepo) : ControllerManager {

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<PublicServiceCatalogView>>> List() {
        string? userId = null;
        if (HttpContext.User.Identity?.IsAuthenticated == true && HttpContext.User.IsUser()) {
            User? user = await HttpContext.User.GetUser(userRepo);
            userId = user?.Id;
        }

        ServiceCatalogItem[] services = await serviceRepo.ListServicesVisibleToUser(userId);
        return Ok(services.Select(PublicServiceCatalogView.From));
    }
}

public class PublicServiceCatalogView {
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Url { get; set; } = "";
    public string? IconUrl { get; set; }

    public static PublicServiceCatalogView From(ServiceCatalogItem service) => new() {
        Id = service.Id,
        Name = service.Name,
        Description = service.Description,
        Url = service.Url,
        IconUrl = service.IconUrl
    };
}
