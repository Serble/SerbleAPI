using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Admin;

/// <summary>
/// Admin-only economy statistics. The controller-level <c>[Authorize(Policy = "AdminOnly")]</c>
/// ensures the caller is an admin User-token holder; the endpoint additionally requires the
/// <c>economy</c> scope (admins hold full access).
/// </summary>
[ApiController]
[Route("api/v1/admin/economy")]
[Authorize(Policy = "AdminOnly")]
public class AdminEconomyController(IBalanceRepository balanceRepo) : ControllerManager {

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
}
