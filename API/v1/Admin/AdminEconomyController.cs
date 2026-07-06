using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    public async Task<ActionResult<TaxPreviewView>> RunTaxNow() {
        TaxRunResult result = await taxService.RunTaxNow();
        if (!result.CanRun) return BadRequest(result.BlockedReason ?? "Tax run is currently unavailable.");
        return Ok(TaxPreviewView.From(result));
    }
}
