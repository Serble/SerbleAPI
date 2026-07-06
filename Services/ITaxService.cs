namespace SerbleAPI.Services;

/// <summary>The configured tax target for an official app.</summary>
public class OfficialAppTaxTarget {
    public string AppId { get; init; } = "";
    public ulong TargetBalance { get; init; }
}

public class TaxPreview {
    public bool CanRun { get; init; }
    public string? BlockedReason { get; init; }
    public bool DynamicRate { get; init; }
    public string BossAppId { get; init; } = "";
    public string Rate { get; init; } = "0";
    public string FixedRate { get; init; } = "0";
    public string MaxDynamicRate { get; init; } = "0";
    public int UsersTaxed { get; init; }
    public ulong Collected { get; init; }
    public int AppsNeedingFunds { get; init; }
    public ulong Distributed { get; init; }
    public ulong BossStartingBalance { get; init; }
    public ulong BossEndingBalance { get; init; }
}

public class TaxRunResult : TaxPreview {
}

public interface ITaxService {
    Task<OfficialAppTaxTarget> GetOfficialAppTarget(string appId, CancellationToken cancellationToken = default);
    Task SetOfficialAppTarget(string appId, ulong targetBalance, CancellationToken cancellationToken = default);
    Task<TaxPreview> PreviewTaxRun(CancellationToken cancellationToken = default);
    Task<TaxRunResult> RunTaxNow(CancellationToken cancellationToken = default);
    Task RunDueTaxCycles(CancellationToken cancellationToken = default);
}
