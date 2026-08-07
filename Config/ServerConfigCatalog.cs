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
    /// <summary>A percentage entered as a decimal number from 0 to 100 (e.g. <c>12.5</c> for 12.5%).</summary>
    Percent,
    /// <summary>A list of strings, one per line; stored newline-separated, blanks/dupes removed.</summary>
    StringList,
    /// <summary>A feature flag mode: enabled for everyone, disabled for everyone, or enabled for configured groups.</summary>
    FeatureFlagMode
}

/// <summary>
/// Definition of one known server-wide setting: its key, human-facing metadata, value type and
/// default. Values are persisted (as strings) in the generic key-value store and overlaid on these
/// defaults at read time, so a setting that has never been edited reads as its <see cref="Default"/>.
/// </summary>
public class ServerConfigDefinition {
    public string Key { get; init; } = "";
    public string Group { get; init; } = "General";
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
            case ServerConfigValueType.Percent:
                if (!decimal.TryParse(normalised, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal pct)) {
                    error = $"{Label} must be a number.";
                    return false;
                }
                if (pct < 0 || pct > 100) {
                    error = $"{Label} must be between 0 and 100.";
                    return false;
                }
                normalised = pct.ToString("0.########", CultureInfo.InvariantCulture);
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
            case ServerConfigValueType.FeatureFlagMode:
                string mode = normalised.ToLowerInvariant();
                if (mode is not ("enabled" or "disabled" or "groups")) {
                    error = $"{Label} must be 'enabled', 'disabled', or 'groups'.";
                    return false;
                }
                normalised = mode;
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
    public const string FeatureFlagsGroup = "Feature flags";
    public const string EconomyGroup = "Economy";
    public const string WebhooksGroup = "Webhooks";

    /// <summary>Mode for the economy feature flag: enabled, disabled, or groups.</summary>
    public const string EconomyFeatureMode = "features.economy.mode";

    /// <summary>Group ids that can use the economy feature when its mode is groups.</summary>
    public const string EconomyFeatureGroups = "features.economy.groups";

    /// <summary>Coins an app is charged each time it mints an item (0 disables the fee).</summary>
    public const string ItemCreationFee = "economy.item_creation_fee";

    /// <summary>How often to run the tax cycle, in hours (0 disables tax collection).</summary>
    public const string TaxPeriodHours = "economy.tax.period_hours";

    /// <summary>The fixed percentage of each user's balance charged each cycle when dynamic mode is off.</summary>
    public const string TaxFixedRate = "economy.tax.fixed_rate";

    /// <summary>Whether the tax rate is computed from official-app target deficits instead of using the fixed rate.</summary>
    public const string TaxUseDynamicRate = "economy.tax.use_dynamic_rate";

    /// <summary>The highest percentage of each user's balance dynamic mode is allowed to charge in one cycle.</summary>
    public const string TaxMaxDynamicRate = "economy.tax.max_dynamic_rate";

    /// <summary>The app id whose balance receives tax collections before redistribution.</summary>
    public const string TaxBossAppId = "economy.tax.boss_app_id";

    /// <summary>Allowed prefixes for an item's icon URL (a <see cref="ServerConfigValueType.StringList"/>).</summary>
    public const string AllowedIconUrlPrefixes = "items.allowed_icon_url_prefixes";

    /// <summary>Whether apps may register plain-http webhook endpoints instead of https only.</summary>
    public const string WebhooksAllowInsecureUrls = "webhooks.allow_insecure_urls";

    /// <summary>Whether apps may register webhook endpoints on private or loopback addresses.</summary>
    public const string WebhooksAllowPrivateHosts = "webhooks.allow_private_hosts";

    /// <summary>How many webhook subscriptions a single app may have.</summary>
    public const string WebhooksMaxPerApp = "webhooks.max_per_app";

    private const string TaskRewardPrefix = "economy.task_reward.";

    /// <summary>The config key holding the coin reward for a given reward task.</summary>
    public static string TaskRewardKey(string taskKey) => TaskRewardPrefix + taskKey;

    public static readonly IReadOnlyList<ServerConfigDefinition> All = Build();

    private static IReadOnlyList<ServerConfigDefinition> Build() {
        List<ServerConfigDefinition> list = [
            new() {
                Key = EconomyFeatureMode,
                Group = FeatureFlagsGroup,
                Label = "Economy feature flag",
                Description =
                    "Controls whether economy features, including coins, trades, and items, are enabled for everyone, disabled for everyone, or enabled only for configured groups.",
                Type = ServerConfigValueType.FeatureFlagMode,
                Default = "disabled",
                Public = false
            },

            new() {
                Key = EconomyFeatureGroups,
                Group = FeatureFlagsGroup,
                Label = "Economy enabled group ids",
                Description = "One group id per line. Used only when the economy feature flag mode is 'groups'.",
                Type = ServerConfigValueType.StringList,
                Default = "",
                Public = false
            },

            new() {
                Key = ItemCreationFee,
                Group = EconomyGroup,
                Label = "Item creation fee",
                Description =
                    "Coins an app is charged each time it mints an item (decimals allowed, e.g. 0.5). Set to 0 to disable the fee.",
                Type = ServerConfigValueType.Coins,
                Default = "0",
                Public = true
            },

            new() {
                Key = TaxPeriodHours,
                Group = EconomyGroup,
                Label = "Tax period (hours)",
                Description = "How often the server runs the tax cycle. Set to 0 to disable periodic tax collection.",
                Type = ServerConfigValueType.Integer,
                Default = "0",
                Public = false
            },

            new() {
                Key = TaxFixedRate,
                Group = EconomyGroup,
                Label = "Fixed tax rate (%)",
                Description =
                    "Percentage of each user's current balance charged each cycle when dynamic tax mode is off. Decimals are allowed, for example 12.5 means 12.5%.",
                Type = ServerConfigValueType.Percent,
                Default = "0",
                Public = false
            },

            new() {
                Key = TaxUseDynamicRate,
                Group = EconomyGroup,
                Label = "Use dynamic tax rate",
                Description =
                    "When true, the server computes the percentage of each user's current balance needed to try to meet official-app target balances. When false, the fixed tax rate is used.",
                Type = ServerConfigValueType.Boolean,
                Default = "false",
                Public = false
            },

            new() {
                Key = TaxMaxDynamicRate,
                Group = EconomyGroup,
                Label = "Max dynamic tax rate (%)",
                Description =
                    "Upper limit for the percentage of each user's current balance that dynamic mode may charge in one cycle. Decimals are allowed. Set to 0 to prevent dynamic tax from charging anything.",
                Type = ServerConfigValueType.Percent,
                Default = "0",
                Public = false
            },

            new() {
                Key = TaxBossAppId,
                Group = EconomyGroup,
                Label = "BOSS app id",
                Description =
                    "The app id whose balance receives collected tax before it is redistributed to official apps.",
                Type = ServerConfigValueType.String,
                Default = "",
                Public = false
            }
        ];

        // One coin setting per known reward task (generated from the task registry).
        foreach ((string key, string label, string @default) in RewardTasks.All) {
            list.Add(new ServerConfigDefinition {
                Key         = TaskRewardKey(key),
                Group       = EconomyGroup,
                Label       = $"Reward: {label}",
                Description = $"Coins granted the first time a user completes the '{key}' task. 0 grants nothing.",
                Type        = ServerConfigValueType.Coins,
                Default     = @default,
                Public      = false
            });
        }

        list.AddRange([
            new ServerConfigDefinition {
                Key         = WebhooksAllowInsecureUrls,
                Group       = WebhooksGroup,
                Label       = "Allow insecure (http) webhook URLs",
                Description =
                    "When false, an app can only register an https webhook endpoint. Enable only for local development — event payloads and their signatures travel in the clear over http.",
                Type        = ServerConfigValueType.Boolean,
                Default     = "false",
                Public      = false
            },

            new ServerConfigDefinition {
                Key         = WebhooksAllowPrivateHosts,
                Group       = WebhooksGroup,
                Label       = "Allow private-network webhook hosts",
                Description =
                    "When false, webhook endpoints resolving to loopback, private or link-local addresses are rejected. Leave off unless apps genuinely run inside this network: a private endpoint lets an app probe hosts the server can reach but the internet cannot.",
                Type        = ServerConfigValueType.Boolean,
                Default     = "false",
                Public      = false
            },

            new ServerConfigDefinition {
                Key         = WebhooksMaxPerApp,
                Group       = WebhooksGroup,
                Label       = "Max webhooks per app",
                Description = "How many webhook subscriptions one app may register. Set to 0 to stop apps registering new webhooks.",
                Type        = ServerConfigValueType.Integer,
                Default     = "5",
                Public      = false
            }
        ]);

        list.Add(new ServerConfigDefinition {
            Key         = AllowedIconUrlPrefixes,
            Group       = EconomyGroup,
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
