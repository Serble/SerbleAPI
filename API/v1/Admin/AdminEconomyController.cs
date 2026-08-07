using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Config;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Admin;

/// <summary>
/// Admin-only economy statistics. The controller-level <c>[Authorize(Policy = "AdminOnly")]</c>
/// ensures the caller is an admin User-token holder; the endpoint additionally requires the
/// <c>economy</c> scope (admins hold full access).
/// </summary>
[ApiController]
[Route("api/v1/admin/economy")]
[Authorize(Policy = "AdminOnly")]
[RequireFeature(FeatureFlagCatalog.Economy)]
public class AdminEconomyController(
    IBalanceRepository balanceRepo,
    ITaxService taxService) : ControllerManager {

    /// <summary>
    /// Coin totals across the whole economy. Coin amounts are returned as strings to avoid
    /// precision loss for very large values (the grand total can exceed <c>ulong.MaxValue</c>).
    /// </summary>
    public class EconomyTotalView {
        public string TotalCoins { get; set; } = "0";
        public string UserCoins { get; set; } = "0";
        public string AppCoins { get; set; } = "0";
        public long BalanceCount { get; set; }
    }

    public class TaxPreviewView {
        public bool CanRun { get; set; }
        public string? BlockedReason { get; set; }
        public bool DynamicRate { get; set; }
        public string BossAppId { get; set; } = "";
        public string Rate { get; set; } = "0";
        public string FixedRate { get; set; } = "0";
        public string MaxDynamicRate { get; set; } = "0";
        public int UsersTaxed { get; set; }
        public string Collected { get; set; } = "0";
        public int AppsNeedingFunds { get; set; }
        public string Distributed { get; set; } = "0";
        public string BossStartingBalance { get; set; } = "0";
        public string BossEndingBalance { get; set; } = "0";

        public static TaxPreviewView From(TaxPreview preview) => new() {
            CanRun = preview.CanRun,
            BlockedReason = preview.BlockedReason,
            DynamicRate = preview.DynamicRate,
            BossAppId = preview.BossAppId,
            Rate = preview.Rate,
            FixedRate = preview.FixedRate,
            MaxDynamicRate = preview.MaxDynamicRate,
            UsersTaxed = preview.UsersTaxed,
            Collected = preview.Collected.ToString(),
            AppsNeedingFunds = preview.AppsNeedingFunds,
            Distributed = preview.Distributed.ToString(),
            BossStartingBalance = preview.BossStartingBalance.ToString(),
            BossEndingBalance = preview.BossEndingBalance.ToString()
        };
    }

    /// <summary>A completed run, with the id of the cycle record it was written to.</summary>
    public class TaxRunResultView : TaxPreviewView {
        public long? CycleId { get; set; }

        public static TaxRunResultView From(TaxRunResult result) => new() {
            CanRun = result.CanRun,
            BlockedReason = result.BlockedReason,
            DynamicRate = result.DynamicRate,
            BossAppId = result.BossAppId,
            Rate = result.Rate,
            FixedRate = result.FixedRate,
            MaxDynamicRate = result.MaxDynamicRate,
            UsersTaxed = result.UsersTaxed,
            Collected = result.Collected.ToString(),
            AppsNeedingFunds = result.AppsNeedingFunds,
            Distributed = result.Distributed.ToString(),
            BossStartingBalance = result.BossStartingBalance.ToString(),
            BossEndingBalance = result.BossEndingBalance.ToString(),
            CycleId = result.CycleId
        };
    }

    /// <summary>
    /// A recorded tax run. Coin amounts are strings for the same reason as everywhere else in this
    /// controller: they can exceed what JSON numbers represent exactly.
    /// </summary>
    public class TaxCycleView {
        public long Id { get; set; }
        public DateTime? ScheduledForUtc { get; set; }
        public bool Manual { get; set; }
        public string Status { get; set; } = "";
        public string Phase { get; set; } = "";
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool DynamicRate { get; set; }
        public string Rate { get; set; } = "0";
        public string BossAppId { get; set; } = "";
        public string Collected { get; set; } = "0";
        public int AccountsTaxed { get; set; }
        public string Distributed { get; set; } = "0";
        public int AppsPaid { get; set; }
        public int AppsNeedingFunds { get; set; }
        public string BossStartingBalance { get; set; } = "0";
        public string BossEndingBalance { get; set; } = "0";
        public int Attempts { get; set; }
        public string? BlockedReason { get; set; }

        public static TaxCycleView From(TaxCycleRecord cycle) => new() {
            Id = cycle.Id,
            ScheduledForUtc = cycle.ScheduledForUtc,
            Manual = cycle.Manual,
            Status = cycle.Status.ToString(),
            Phase = cycle.Phase.ToString(),
            StartedAt = cycle.StartedAt,
            CompletedAt = cycle.CompletedAt,
            DynamicRate = cycle.DynamicRate,
            Rate = cycle.Rate,
            BossAppId = cycle.BossAppId,
            Collected = cycle.Collected.ToString(),
            AccountsTaxed = cycle.AccountsTaxed,
            Distributed = cycle.Distributed.ToString(),
            AppsPaid = cycle.AppsPaid,
            AppsNeedingFunds = cycle.AppsNeedingFunds,
            BossStartingBalance = cycle.BossStartingBalance.ToString(),
            BossEndingBalance = cycle.BossEndingBalance.ToString(),
            Attempts = cycle.Attempts,
            BlockedReason = cycle.BlockedReason
        };
    }

    public class TaxCyclePageView {
        public long TotalCount { get; set; }
        public List<TaxCycleView> Cycles { get; set; } = [];
    }

    /// <summary>
    /// Returns the total coin value in circulation, summed across every balance in the database,
    /// with a breakdown by owner type (users vs. apps) and the number of balances counted.
    /// </summary>
    [HttpGet("total")]
    [Authorize(Policy = "Scope:Economy")]
    public async Task<ActionResult<EconomyTotalView>> GetTotal() {
        EconomyTotal total = await balanceRepo.GetTotalEconomyValue();
        return Ok(new EconomyTotalView {
            TotalCoins   = total.TotalCoins.ToString(),
            UserCoins    = total.UserCoins.ToString(),
            AppCoins     = total.AppCoins.ToString(),
            BalanceCount = total.BalanceCount
        });
    }

    [HttpGet("tax/preview")]
    [Authorize(Policy = "Scope:Economy")]
    public async Task<ActionResult<TaxPreviewView>> PreviewTax() {
        TaxPreview preview = await taxService.PreviewTaxRun();
        return Ok(TaxPreviewView.From(preview));
    }

    [HttpPost("tax/run-now")]
    [Authorize(Policy = "Scope:ManageEconomy")]
    public async Task<ActionResult<TaxRunResultView>> RunTaxNow() {
        TaxRunResult result = await taxService.RunTaxNow();
        if (!result.CanRun) return BadRequest(result.BlockedReason ?? "Tax run is currently unavailable.");
        return Ok(TaxRunResultView.From(result));
    }

    /// <summary>
    /// Paged history of every recorded tax run, newest first. Blocked and skipped cycles appear
    /// alongside successful ones so gaps in the schedule are visible rather than implied.
    /// </summary>
    [HttpGet("tax/cycles")]
    [Authorize(Policy = "Scope:Economy")]
    public async Task<ActionResult<TaxCyclePageView>> GetTaxCycles([FromQuery] int skip = 0, [FromQuery] int take = 50) {
        if (skip < 0) return BadRequest("skip must not be negative.");
        if (take is < 1 or > 200) return BadRequest("take must be between 1 and 200.");

        TaxCyclePage page = await taxService.GetTaxCycles(skip, take);
        return Ok(new TaxCyclePageView {
            TotalCount = page.TotalCount,
            Cycles = page.Cycles.Select(TaxCycleView.From).ToList()
        });
    }

    [HttpGet("tax/cycles/{id:long}")]
    [Authorize(Policy = "Scope:Economy")]
    public async Task<ActionResult<TaxCycleView>> GetTaxCycle(long id) {
        TaxCycleRecord? cycle = await taxService.GetTaxCycle(id);
        return cycle == null ? NotFound() : Ok(TaxCycleView.From(cycle));
    }
}
