using SerbleAPI.Config;

namespace SerbleAPI.Services;

/// <summary>The item-creation fee, both as its raw fixed-point amount (what is charged) and its
/// human-facing decimal coin string (what was configured).</summary>
public record ItemCreationFee(string Coins, ulong Raw);

/// <summary>A known setting paired with its current (effective) value.</summary>
public class ServerConfigItem {
    public ServerConfigDefinition Definition { get; init; } = null!;
    public string Value { get; init; } = "";
}

/// <summary>Result of attempting to update a setting.</summary>
public class ServerConfigSetResult {
    public bool Success { get; init; }
    public string? Error { get; init; }
    public ServerConfigItem? Item { get; init; }

    public static ServerConfigSetResult Fail(string error) => new() { Success = false, Error = error };
    public static ServerConfigSetResult Ok(ServerConfigItem item) => new() { Success = true, Item = item };
}

/// <summary>
/// Reads and writes server-wide settings, overlaying persisted values (in the key-value store) on
/// the defaults declared in <see cref="ServerConfigCatalog"/>. This is the single entry point both
/// for the admin dashboard (list/update) and for feature code that needs a configured value.
/// </summary>
public interface IServerConfigService {
    /// <summary>Every known setting with its current effective value.</summary>
    Task<IReadOnlyList<ServerConfigItem>> GetAll();

    /// <summary>One setting with its current effective value, or null if the key is unknown.</summary>
    Task<ServerConfigItem?> Get(string key);

    /// <summary>Validates and persists a new value for a known setting.</summary>
    Task<ServerConfigSetResult> Set(string key, string? value);

    /// <summary>The configured item-creation fee (raw amount + decimal coin string; 0 if disabled).</summary>
    Task<ItemCreationFee> GetItemCreationFee();

    /// <summary>Reads a <see cref="ServerConfigValueType.Coins"/> setting as raw fixed-point units (0 if unset/invalid).</summary>
    Task<ulong> GetCoinsRaw(string key);

    /// <summary>Reads a <see cref="ServerConfigValueType.StringList"/> setting as its entries (empty if unset).</summary>
    Task<string[]> GetStringList(string key);
}
