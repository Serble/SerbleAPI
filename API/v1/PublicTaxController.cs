using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Config;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1;

/// <summary>
/// The published tax schedule, readable by anyone. Holding coins costs a percentage of the balance
/// every cycle, so anyone deciding whether to hold — a user, or an app holding coins on behalf of
/// its users — needs to know the rate and when it lands before they commit. Keeping that behind
/// admin auth meant the cost of a decision was only visible to the people who did not have to make
/// it.
///
/// Unauthenticated by design: every value here is a server-wide constant that applies uniformly to
/// all taxable balances, reveals nothing about any individual balance, and is already observable
/// after the fact through the <c>tax.collected</c> webhook. Publishing it up front only changes
/// <i>when</i> it can be known, not by whom.
/// </summary>
[ApiController]
[Route("api/v1/economy/tax")]
[AllowAnonymous]
[RequireFeature(FeatureFlagCatalog.Economy)]
public class PublicTaxController(ITaxService taxService) : ControllerManager {

    /// <summary>
    /// Percentages are decimal strings (e.g. <c>"2.5"</c> meaning 2.5%), matching how rates are
    /// carried everywhere else in the economy API.
    /// </summary>
    public class TaxInfoResponse {
        /// <summary>True when periodic collection is armed. When false, no cycle is scheduled and
        /// <see cref="NextCycleUtc"/> is null — but an admin can still trigger a run manually.</summary>
        public bool Scheduled { get; set; }

        /// <summary>Hours between cycles, or 0 when periodic collection is off.</summary>
        public ulong PeriodHours { get; set; }

        /// <summary>True when the rate is recomputed each cycle rather than fixed.</summary>
        public bool DynamicRate { get; set; }

        /// <summary>
        /// The most one cycle can take from a balance. The realised rate is never above this, so it
        /// is the figure to price against when committing to hold coins for a known period.
        /// </summary>
        public string MaxRatePercent { get; set; } = "0";

        /// <summary>The configured fixed rate — what is charged when <see cref="DynamicRate"/> is false.</summary>
        public string FixedRatePercent { get; set; } = "0";

        /// <summary>The ceiling dynamic mode is clamped to. Only meaningful when <see cref="DynamicRate"/> is true.</summary>
        public string MaxDynamicRatePercent { get; set; } = "0";

        /// <summary>
        /// When the next cycle boundary falls, or null when there is none to report. A timestamp in
        /// the past means a cycle is overdue and settles on the next scheduler pass.
        /// </summary>
        public DateTime? NextCycleUtc { get; set; }

        /// <summary>
        /// The most cycles chargeable in a single catch-up burst after an outage; older boundaries
        /// are written off instead of collected. Counting elapsed hours therefore over-estimates
        /// cycles charged after downtime and under-estimates them during a burst — settle against
        /// cycles that actually fired rather than against the clock.
        /// </summary>
        public int MaxCatchUpCycles { get; set; }
    }

    /// <summary>Current tax schedule and rate ceiling.</summary>
    [HttpGet("info")]
    public async Task<ActionResult<TaxInfoResponse>> GetInfo(CancellationToken cancellationToken) {
        TaxScheduleInfo info = await taxService.GetScheduleInfo(cancellationToken);
        return Ok(new TaxInfoResponse {
            Scheduled             = info.Scheduled,
            PeriodHours           = info.PeriodHours,
            DynamicRate           = info.DynamicRate,
            MaxRatePercent        = info.MaxRatePercent,
            FixedRatePercent      = info.FixedRatePercent,
            MaxDynamicRatePercent = info.MaxDynamicRatePercent,
            NextCycleUtc          = info.NextCycleUtc,
            MaxCatchUpCycles      = info.MaxCatchUpCycles
        });
    }
}
