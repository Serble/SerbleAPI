using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Config;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Apps;

[ApiController]
[Route("api/v1/official/tax")]
[Authorize(Policy = "OfficialAppKeyOnly")]
[RequireFeature(FeatureFlagCatalog.Economy)]
public class OfficialAppTaxController(
    IBalanceRepository balanceRepo,
    ITaxService taxService) : ControllerManager {

    public class TaxTargetResponse {
        public string AppId { get; set; } = "";
        public ulong CurrentBalance { get; set; }
        public ulong TargetBalance { get; set; }
    }

    public class SetTaxTargetBody {
        public ulong TargetBalance { get; set; }
    }

    private async Task<ActionResult<TaxTargetResponse>> BuildResponse(string appId) {
        Balance balance = await balanceRepo.GetBalance(BalanceOwnerType.App, appId);
        OfficialAppTaxTarget target = await taxService.GetOfficialAppTarget(appId);
        return Ok(new TaxTargetResponse {
            AppId = appId,
            CurrentBalance = balance.Coins,
            TargetBalance = target.TargetBalance
        });
    }

    [HttpGet("target-balance")]
    public Task<ActionResult<TaxTargetResponse>> GetTargetBalance() {
        string? appId = HttpContext.User.GetAppId();
        return appId == null
            ? Task.FromResult<ActionResult<TaxTargetResponse>>(Unauthorized())
            : BuildResponse(appId);
    }

    [HttpPut("target-balance")]
    public async Task<ActionResult<TaxTargetResponse>> SetTargetBalance([FromBody] SetTaxTargetBody body) {
        string? appId = HttpContext.User.GetAppId();
        if (appId == null) return Unauthorized();
        await taxService.SetOfficialAppTarget(appId, body.TargetBalance);
        return await BuildResponse(appId);
    }
}
