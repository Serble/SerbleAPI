using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Repositories;

namespace SerbleAPI.Services.Impl;

/// <summary>
/// <see cref="IServerConfigService"/> backed by the generic key-value store
/// (<see cref="IKvRepository"/>). A setting that has never been written reads as its catalog
/// default; writes are validated against the catalog before being persisted.
/// </summary>
public class ServerConfigService(IKvRepository kv) : IServerConfigService {

    private static string EffectiveValue(ServerConfigDefinition def, string? stored) {
        string value = stored ?? def.Default;
        if (def.TryValidate(value, out string normalised, out _)) return normalised;

        // When a setting's type changes over time, old persisted values may no longer validate.
        // Render the effective clamped/default value so the admin UI matches runtime behaviour.
        if (def.Type == ServerConfigValueType.Percent
            && decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal pct)) {
            decimal clamped = pct < 0 ? 0 : pct > 100 ? 100 : pct;
            return clamped.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);
        }

        return def.Default;
    }

    public async Task<IReadOnlyList<ServerConfigItem>> GetAll() {
        List<ServerConfigItem> items = new(ServerConfigCatalog.All.Count);
        foreach (ServerConfigDefinition def in ServerConfigCatalog.All) {
            string value = EffectiveValue(def, await kv.Get(def.Key));
            items.Add(new ServerConfigItem { Definition = def, Value = value });
        }
        return items;
    }

    public async Task<ServerConfigItem?> Get(string key) {
        ServerConfigDefinition? def = ServerConfigCatalog.Find(key);
        if (def == null) return null;
        string value = EffectiveValue(def, await kv.Get(key));
        return new ServerConfigItem { Definition = def, Value = value };
    }

    public async Task<ServerConfigSetResult> Set(string key, string? value) {
        ServerConfigDefinition? def = ServerConfigCatalog.Find(key);
        if (def == null) return ServerConfigSetResult.Fail("Unknown config key.");
        if (!def.TryValidate(value, out string normalised, out string? error))
            return ServerConfigSetResult.Fail(error!);
        await kv.Set(key, normalised);
        return ServerConfigSetResult.Ok(new ServerConfigItem { Definition = def, Value = normalised });
    }

    public async Task<ItemCreationFee> GetItemCreationFee() {
        ServerConfigItem? item = await Get(ServerConfigCatalog.ItemCreationFee);
        string coins = item?.Value ?? "0";
        ulong raw = CoinFixedPoint.TryParseCoins(coins, out ulong r, out _) ? r : 0;
        return new ItemCreationFee(coins, raw);
    }

    public async Task<ulong> GetCoinsRaw(string key) {
        ServerConfigItem? item = await Get(key);
        return item != null && CoinFixedPoint.TryParseCoins(item.Value, out ulong r, out _) ? r : 0;
    }

    public async Task<string[]> GetStringList(string key) {
        ServerConfigItem? item = await Get(key);
        if (item == null) return [];
        return item.Value
            .Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
