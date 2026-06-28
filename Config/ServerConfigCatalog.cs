using System.Globalization;
using SerbleAPI.Data;

namespace SerbleAPI.Config;


/// <summary>The value kind of a <see cref="ServerConfigDefinition"/>, used for validation and UI rendering.</summary>
public enum ServerConfigValueType {
    Integer,
    Boolean,
    String,
    /// <summary>A coin amount entered as a decimal (e.g. <c>0.5</c>); stored as a trimmed decimal string.</summary>
    Coins,
    /// <summary>A list of strings, one per line; stored newline-separated, blanks/dupes removed.</summary>
    StringList
}

/// <summary>
/// Definition of one known server-wide setting: its key, human-facing metadata, value type and
/// default. Values are persisted (as strings) in the generic key-value store and overlaid on these
/// defaults at read time, so a setting that has never been edited reads as its <see cref="Default"/>.
/// </summary>
public class ServerConfigDefinition {
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string Description { get; init; } = "";
    public ServerConfigValueType Type { get; init; }
    public string Default { get; init; } = "";
    /// <summary>True if non-admin callers (e.g. apps) may read this value.</summary>
    public bool Public { get; init; }

    /// <summary>
    /// Validates and normalises a candidate string value against this setting's type. On success
    /// <paramref name="normalised"/> holds the canonical form to persist.
    /// </summary>
    public bool TryValidate(string? value, out string normalised, out string? error) {
        normalised = (value ?? "").Trim();
        error = null;
        switch (Type) {
            case ServerConfigValueType.Integer:
                if (!ulong.TryParse(normalised, out ulong n)) {
                    error = $"{Label} must be a non-negative whole number.";
                    return false;
                }
                normalised = n.ToString();
                return true;
            case ServerConfigValueType.Boolean:
                if (!bool.TryParse(normalised, out bool b)) {
                    error = $"{Label} must be 'true' or 'false'.";
                    return false;
                }
                normalised = b ? "true" : "false";
                return true;
            case ServerConfigValueType.String:
                if (normalised.Length > 2048) {
                    error = $"{Label} cannot be longer than 2048 characters.";
                    return false;
                }
                return true;
            case ServerConfigValueType.Coins:
                if (!CoinFixedPoint.TryParseCoins(normalised, out _, out string? coinsError)) {
                    error = $"{Label}: {coinsError}";
                    return false;
                }
                // Canonical, human-friendly form: trim to the precision the fixed-point can carry.
                normalised = decimal
                    .Parse(normalised, NumberStyles.Number, CultureInfo.InvariantCulture)
                    .ToString("0.########", CultureInfo.InvariantCulture);
                return true;
            case ServerConfigValueType.StringList:
                List<string> lines = normalised
                    .Replace("\r\n", "\n").Replace('\r', '\n')
                    .Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .Distinct()
                    .ToList();
                if (lines.Any(l => l.Length > 512)) {
                    error = $"{Label}: each entry must be 512 characters or fewer.";
                    return false;
                }
                if (lines.Count > 50) {
                    error = $"{Label}: at most 50 entries are allowed.";
                    return false;
                }
                normalised = string.Join('\n', lines);
                if (normalised.Length > 2048) {
                    error = $"{Label} is too long (2048 characters max).";
                    return false;
                }
                return true;
            default:
                error = "Unknown setting type.";
                return false;
        }
    }
}

/// <summary>
/// The registry of every server-wide setting the application knows about. Adding a setting is a
/// one-line entry here — the admin API and dashboard render the catalog generically, and code reads
/// values through <see cref="SerbleAPI.Services.IServerConfigService"/>.
/// </summary>
public static class ServerConfigCatalog {
    /// <summary>Coins an app is charged each time it mints an item (0 disables the fee).</summary>
    public const string ItemCreationFee = "economy.item_creation_fee";

    /// <summary>Allowed prefixes for an item's icon URL (a <see cref="ServerConfigValueType.StringList"/>).</summary>
    public const string AllowedIconUrlPrefixes = "items.allowed_icon_url_prefixes";

    private const string TaskRewardPrefix = "economy.task_reward.";

    /// <summary>The config key holding the coin reward for a given reward task.</summary>
    public static string TaskRewardKey(string taskKey) => TaskRewardPrefix + taskKey;

    public static readonly IReadOnlyList<ServerConfigDefinition> All = Build();

    private static IReadOnlyList<ServerConfigDefinition> Build() {
        List<ServerConfigDefinition> list = new() {
            new() {
                Key         = ItemCreationFee,
                Label       = "Item creation fee",
                Description = "Coins an app is charged each time it mints an item (decimals allowed, e.g. 0.5). Set to 0 to disable the fee.",
                Type        = ServerConfigValueType.Coins,
                Default     = "0",
                Public      = true
            }
        };

        // One coin setting per known reward task (generated from the task registry).
        foreach ((string key, string label, string @default) in RewardTasks.All) {
            list.Add(new ServerConfigDefinition {
                Key         = TaskRewardKey(key),
                Label       = $"Reward: {label}",
                Description = $"Coins granted the first time a user completes the '{key}' task. 0 grants nothing.",
                Type        = ServerConfigValueType.Coins,
                Default     = @default,
                Public      = false
            });
        }

        list.Add(new ServerConfigDefinition {
            Key         = AllowedIconUrlPrefixes,
            Label       = "Allowed item icon URL prefixes",
            Description = "One prefix per line. An item icon URL is accepted only if it is empty or starts with one of these.",
            Type        = ServerConfigValueType.StringList,
            Default     = "https://api.files.serble.net/files/",
            Public      = false
        });

        return list;
    }

    public static ServerConfigDefinition? Find(string key) =>
        All.FirstOrDefault(d => d.Key == key);
}
