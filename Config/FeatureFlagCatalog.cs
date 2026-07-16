namespace SerbleAPI.Config;

public class FeatureFlagDefinition {
    public string Key { get; init; } = "";
    public string ModeConfigKey { get; init; } = "";
    public string GroupsConfigKey { get; init; } = "";
}

public static class FeatureFlagCatalog {
    public const string Economy = "economy";

    public static readonly IReadOnlyList<FeatureFlagDefinition> All = [
        new() {
            Key = Economy,
            ModeConfigKey = ServerConfigCatalog.EconomyFeatureMode,
            GroupsConfigKey = ServerConfigCatalog.EconomyFeatureGroups
        }
    ];

    public static FeatureFlagDefinition? Find(string key) =>
        All.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
}
