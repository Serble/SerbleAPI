namespace SerbleAPI.Data.Schemas;

public enum ServiceCatalogVisibilityMode {
    Public = 0,
    RestrictedToGroups = 1
}

public class ServiceCatalogItem {
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Url { get; set; } = null!;
    public string? IconUrl { get; set; }
    public ServiceCatalogVisibilityMode VisibilityMode { get; set; }
    public bool New { get; set; }
    public string[] AllowedGroupIds { get; set; } = [];
}
