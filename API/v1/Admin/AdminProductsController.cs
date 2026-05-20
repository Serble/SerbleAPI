using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Admin;

/// <summary>
/// Admin-only CRUD for SerbleProducts. Returns full product including secrets
/// (WebhookSecret, SuccessTokenSecret) — never reuse this view for non-admin
/// endpoints.
/// </summary>
[ApiController]
[Route("api/v1/admin/products")]
[Authorize(Policy = "AdminOnly")]
public class AdminProductsController(
    ILogger<AdminProductsController> logger,
    IProductRepository productRepo) : ControllerManager {

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AdminProductView>>> List() {
        SerbleProduct[] products = await productRepo.ListProducts();
        return Ok(products.Select(AdminProductView.From));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AdminProductView>> Get(string id) {
        SerbleProduct? product = await productRepo.GetProductFromId(id);
        if (product == null) return NotFound();
        return Ok(AdminProductView.From(product));
    }

    [HttpPost]
    public async Task<ActionResult<AdminProductView>> Create([FromBody] AdminProductView body) {
        if (string.IsNullOrWhiteSpace(body.Id)) return BadRequest("Id is required");
        if (string.IsNullOrWhiteSpace(body.Name)) return BadRequest("Name is required");

        SerbleProduct product = body.ToProduct();
        bool created = await productRepo.CreateProduct(product);
        if (!created) return Conflict("A product with that Id already exists");

        logger.LogInformation("Admin {AdminId} created product {ProductId}",
            HttpContext.User.GetUserId(), product.Id);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, AdminProductView.From(product));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<AdminProductView>> Update(string id, [FromBody] AdminProductView body) {
        SerbleProduct? existing = await productRepo.GetProductFromId(id);
        if (existing == null) return NotFound();

        // Body's Id is ignored — path id wins; we don't support renaming the PK.
        body.Id = id;
        SerbleProduct product = body.ToProduct();
        bool ok = await productRepo.UpdateProduct(product);
        if (!ok) return NotFound();

        logger.LogInformation("Admin {AdminId} updated product {ProductId}",
            HttpContext.User.GetUserId(), id);
        return Ok(AdminProductView.From(product));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id) {
        bool ok = await productRepo.DeleteProduct(id);
        if (!ok) return NotFound();
        logger.LogWarning("Admin {AdminId} DELETED product {ProductId}",
            HttpContext.User.GetUserId(), id);
        return Ok(new { success = true });
    }
}

/// <summary>
/// Admin-facing product view. Exposes secrets that are hidden from
/// regular API responses; never reuse this on non-admin routes.
/// </summary>
public class AdminProductView {
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string[] PriceIds { get; set; } = [];
    public Dictionary<string, string> PriceLookupIds { get; set; } = new();
    public bool Purchasable { get; set; }
    public string? SuccessRedirect { get; set; }
    public string? SuccessTokenSecret { get; set; }
    public string? Webhook { get; set; }
    public string? WebhookSecret { get; set; }
    public bool AllowAnonymous { get; set; }

    public static AdminProductView From(SerbleProduct p) => new() {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        PriceIds = p.PriceIds ?? [],
        PriceLookupIds = p.PriceLookupIds ?? new Dictionary<string, string>(),
        Purchasable = p.Purchasable,
        SuccessRedirect = p.SuccessRedirect,
        SuccessTokenSecret = p.SuccessTokenSecret,
        Webhook = p.Webhook,
        WebhookSecret = p.WebhookSecret,
        AllowAnonymous = p.AllowAnonymous
    };

    public SerbleProduct ToProduct() => new() {
        Id = Id,
        Name = Name,
        Description = Description,
        PriceIds = PriceIds ?? [],
        PriceLookupIds = PriceLookupIds ?? new Dictionary<string, string>(),
        Purchasable = Purchasable,
        SuccessRedirect = SuccessRedirect,
        SuccessTokenSecret = SuccessTokenSecret,
        Webhook = Webhook,
        WebhookSecret = WebhookSecret,
        AllowAnonymous = AllowAnonymous
    };
}
