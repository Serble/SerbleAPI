using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Apps;

/// <summary>
/// Official-app-only economy endpoints for directly modifying a user's coin balance.
///
/// The controller-level <c>[Authorize(Policy = "OfficialAppKeyOnly")]</c> requires pure app
/// authentication: a request authenticated with an app API key whose backing app is flagged
/// official. User tokens and OAuth delegated app tokens (a user acting through an app) are
/// rejected with 403, regardless of scope.
/// </summary>
[ApiController]
[Route("api/v1/official/users")]
[Authorize(Policy = "OfficialAppKeyOnly")]
public class OfficialAppBalanceController(
    ILogger<OfficialAppBalanceController> logger,
    IUserRepository userRepo,
    IBalanceRepository balanceRepo) : ControllerManager {

    public class CoinBalanceResponse {
        public string UserId { get; set; } = "";
        public ulong Coins { get; set; }
    }

    public class CoinAmountBody {
        public ulong Amount { get; set; }
    }

    /// <summary>Resolves a target user by storage id or username, or null if not found.</summary>
    private async Task<User?> ResolveUser(string idOrName) =>
        await userRepo.GetUser(idOrName) ?? await userRepo.GetUserFromName(idOrName);

    /// <summary>Returns the current coin balance of the target user.</summary>
    [HttpGet("{id}/coins")]
    public async Task<ActionResult<CoinBalanceResponse>> GetCoins(string id) {
        User? user = await ResolveUser(id);
        if (user == null) return NotFound("User not found.");
        Balance bal = await balanceRepo.GetBalance(BalanceOwnerType.User, user.Id);
        return Ok(new CoinBalanceResponse { UserId = user.Id, Coins = bal.Coins });
    }

    /// <summary>Adds coins to the target user's balance (saturates at ulong.MaxValue).</summary>
    [HttpPost("{id}/coins/add")]
    public async Task<ActionResult<CoinBalanceResponse>> AddCoins(string id, [FromBody] CoinAmountBody body) {
        if (body.Amount == 0) return BadRequest("Amount must be greater than zero.");
        User? user = await ResolveUser(id);
        if (user == null) return NotFound("User not found.");
        Balance bal = await balanceRepo.AddCoins(BalanceOwnerType.User, user.Id, body.Amount,
            $"Official app mint (by {HttpContext.User.GetAppId()})");
        logger.LogInformation("Official app {AppId} added {Amount} coins to user {TargetId} (new balance {Balance})",
            HttpContext.User.GetAppId(), body.Amount, user.Id, bal.Coins);
        return Ok(new CoinBalanceResponse { UserId = user.Id, Coins = bal.Coins });
    }

    /// <summary>Removes coins from the target user's balance (clamped at 0).</summary>
    [HttpPost("{id}/coins/remove")]
    public async Task<ActionResult<CoinBalanceResponse>> RemoveCoins(string id, [FromBody] CoinAmountBody body) {
        if (body.Amount == 0) return BadRequest("Amount must be greater than zero.");
        User? user = await ResolveUser(id);
        if (user == null) return NotFound("User not found.");
        Balance bal = await balanceRepo.RemoveCoins(BalanceOwnerType.User, user.Id, body.Amount,
            $"Official app burn (by {HttpContext.User.GetAppId()})");
        logger.LogInformation("Official app {AppId} removed {Amount} coins from user {TargetId} (new balance {Balance})",
            HttpContext.User.GetAppId(), body.Amount, user.Id, bal.Coins);
        return Ok(new CoinBalanceResponse { UserId = user.Id, Coins = bal.Coins });
    }
}
