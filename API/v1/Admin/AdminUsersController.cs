using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Admin;

/// <summary>
/// Admin-only management endpoints. The controller-level
/// <c>[Authorize(Policy = "AdminOnly")]</c> ensures the caller is a logged-in
/// User-token holder whose <c>PermLevel</c> is 2 (admin). OAuth app tokens
/// cannot reach any of these endpoints regardless of scope.
/// </summary>
[ApiController]
[Route("api/v1/admin/users")]
[Authorize(Policy = "AdminOnly")]
public class AdminUsersController(
    ILogger<AdminUsersController> logger,
    IUserRepository userRepo,
    IBalanceRepository balanceRepo,
    IPasskeyRepository passkeyRepo,
    ITokenService tokens) : ControllerManager {

    // -------- Stats --------

    public class UserStatsResponse {
        public long TotalUsers { get; set; }
        public long VerifiedEmailUsers { get; set; }
        public double VerifiedEmailPercent { get; set; }
    }

    [HttpGet("stats")]
    public async Task<ActionResult<UserStatsResponse>> Stats() {
        long total = await userRepo.CountUsers();
        long verified = await userRepo.CountVerifiedEmailUsers();
        double pct = total == 0 ? 0d : Math.Round(verified * 100d / total, 2);
        return Ok(new UserStatsResponse {
            TotalUsers = total,
            VerifiedEmailUsers = verified,
            VerifiedEmailPercent = pct
        });
    }

    // -------- Search / lookup --------

    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<AdminUserView>>> Search(
        [FromQuery] string? query = null, [FromQuery] int limit = 25) {
        User[] results = await userRepo.SearchUsers(query ?? "", limit);
        List<AdminUserView> views = new(results.Length);
        foreach (User u in results) {
            Balance bal = await balanceRepo.GetBalance(BalanceOwnerType.User, u.Id);
            views.Add(AdminUserView.From(u, bal.Coins));
        }
        return Ok(views);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AdminUserView>> Get(string id) {
        User? user = await userRepo.GetUser(id);
        if (user == null) return NotFound();
        Balance bal = await balanceRepo.GetBalance(BalanceOwnerType.User, user.Id);
        return Ok(AdminUserView.From(user, bal.Coins));
    }

    // -------- Login as user --------

    public class LoginAsResponse {
        public string Token { get; set; } = "";
        public string ImpersonatedBy { get; set; } = "";
        public string TargetUserId { get; set; } = "";
    }

    [HttpPost("{id}/login-as")]
    public async Task<ActionResult<LoginAsResponse>> LoginAs(string id) {
        User? target = await userRepo.GetUser(id);
        if (target == null) return NotFound();

        string adminId = HttpContext.User.GetUserId()!;
        logger.LogWarning(
            "ADMIN IMPERSONATION: admin {AdminId} issued login token for user {TargetId} ({TargetUsername})",
            adminId, target.Id, target.Username);

        string token = tokens.GenerateLoginToken(target.Id);
        return Ok(new LoginAsResponse {
            Token = token,
            ImpersonatedBy = adminId,
            TargetUserId = target.Id
        });
    }

    // -------- Disable / enable --------

    [HttpPost("{id}/disable")]
    public async Task<IActionResult> Disable(string id) {
        if (IsSelf(id)) return BadRequest("Admins cannot disable themselves");
        User? user = await userRepo.GetUser(id);
        if (user == null) return NotFound();
        user.PermLevel = 0;
        await userRepo.UpdateUser(user);
        logger.LogInformation("Admin {AdminId} disabled user {TargetId}", HttpContext.User.GetUserId(), id);
        return Ok(new { success = true });
    }

    [HttpPost("{id}/enable")]
    public async Task<IActionResult> Enable(string id) {
        User? user = await userRepo.GetUser(id);
        if (user == null) return NotFound();
        // Only promote disabled accounts; don't demote existing admins back to 1.
        if (user.PermLevel == 0) {
            user.PermLevel = 1;
            await userRepo.UpdateUser(user);
        }
        logger.LogInformation("Admin {AdminId} enabled user {TargetId}", HttpContext.User.GetUserId(), id);
        return Ok(new { success = true });
    }

    // -------- Delete --------

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id) {
        if (IsSelf(id)) return BadRequest("Admins cannot delete themselves through this endpoint");
        User? user = await userRepo.GetUser(id);
        if (user == null) return NotFound();

        // Clean up passkeys first (UserRepository.DeleteUser handles authorized apps).
        SavedPasskey[] passkeys = await passkeyRepo.GetUsersPasskeys(id);
        foreach (SavedPasskey pk in passkeys) {
            if (pk.CredentialId != null) await passkeyRepo.DeletePasskey(pk.CredentialId);
        }

        await userRepo.DeleteUser(id);
        logger.LogWarning("Admin {AdminId} DELETED user {TargetId} ({TargetUsername})",
            HttpContext.User.GetUserId(), id, user.Username);
        return Ok(new { success = true });
    }

    // -------- Change password --------

    public class ChangePasswordBody {
        public string Password { get; set; } = "";
    }

    [HttpPost("{id}/password")]
    public async Task<IActionResult> ChangePassword(string id, [FromBody] ChangePasswordBody body) {
        if (string.IsNullOrEmpty(body.Password)) return BadRequest("Password is required");
        if (body.Password.Length > 256) return BadRequest("Password cannot be longer than 256 characters");

        User? user = await userRepo.GetUser(id);
        if (user == null) return NotFound();

        string salt = SerbleUtils.RandomString(64);
        user.PasswordSalt = salt;
        user.PasswordHash = (body.Password + salt).Sha256Hash();
        await userRepo.UpdateUser(user);
        logger.LogWarning("Admin {AdminId} reset password for user {TargetId}",
            HttpContext.User.GetUserId(), id);
        return Ok(new { success = true });
    }

    // -------- Disable 2FA --------

    [HttpPost("{id}/disable-2fa")]
    public async Task<IActionResult> Disable2Fa(string id) {
        User? user = await userRepo.GetUser(id);
        if (user == null) return NotFound();
        user.TotpEnabled = false;
        user.TotpSecret = null;
        await userRepo.UpdateUser(user);
        logger.LogWarning("Admin {AdminId} disabled 2FA for user {TargetId}",
            HttpContext.User.GetUserId(), id);
        return Ok(new { success = true });
    }

    // -------- Passkeys --------

    [HttpGet("{id}/passkeys")]
    public async Task<IActionResult> ListPasskeys(string id) {
        User? user = await userRepo.GetUser(id);
        if (user == null) return NotFound();
        SavedPasskey[] keys = await passkeyRepo.GetUsersPasskeys(id);
        return Ok(keys.Select(k => new {
            name             = k.Name,
            credentialId     = Convert.ToBase64String(k.CredentialId!),
            isBackupEligible = k.IsBackupEligible,
            isBackedUp       = k.IsBackedUp
        }));
    }

    [HttpDelete("{id}/passkeys/{name}")]
    public async Task<IActionResult> DeletePasskey(string id, string name) {
        User? user = await userRepo.GetUser(id);
        if (user == null) return NotFound();
        SavedPasskey[] keys = await passkeyRepo.GetUsersPasskeys(id);
        SavedPasskey? target = keys.FirstOrDefault(k => k.Name == name);
        if (target == null) return NotFound("Passkey not found");
        await passkeyRepo.DeletePasskey(target.CredentialId!);
        logger.LogInformation("Admin {AdminId} deleted passkey {Name} of user {TargetId}",
            HttpContext.User.GetUserId(), name, id);
        return Ok(new { success = true });
    }

    // -------- Promote / demote admin --------

    public class SetAdminBody {
        public bool Admin { get; set; }
    }

    [HttpPost("{id}/admin")]
    public async Task<IActionResult> SetAdmin(string id, [FromBody] SetAdminBody body) {
        if (IsSelf(id) && !body.Admin)
            return BadRequest("Admins cannot demote themselves");

        User? user = await userRepo.GetUser(id);
        if (user == null) return NotFound();

        // Don't re-enable a disabled account just because the admin granted/revoked admin.
        if (user.PermLevel != 0) {
            user.PermLevel = body.Admin ? 2 : 1;
            await userRepo.UpdateUser(user);
        }
        logger.LogWarning("Admin {AdminId} set admin={Admin} for user {TargetId}",
            HttpContext.User.GetUserId(), body.Admin, id);
        return Ok(new { success = true });
    }

    // -------- Coins (economy) --------

    public class CoinBalanceResponse {
        public string UserId { get; set; } = "";
        public ulong Coins { get; set; }
    }

    public class SetCoinsBody {
        public ulong Balance { get; set; }
    }

    public class CoinAmountBody {
        public ulong Amount { get; set; }
    }

    [HttpGet("{id}/coins")]
    [Authorize(Policy = "Scope:Economy")]
    public async Task<ActionResult<CoinBalanceResponse>> GetCoins(string id) {
        User? user = await userRepo.GetUser(id);
        if (user == null) return NotFound();
        Balance bal = await balanceRepo.GetBalance(BalanceOwnerType.User, id);
        return Ok(new CoinBalanceResponse { UserId = id, Coins = bal.Coins });
    }

    [HttpPost("{id}/coins/set")]
    [Authorize(Policy = "Scope:ManageEconomy")]
    public async Task<ActionResult<CoinBalanceResponse>> SetCoins(string id, [FromBody] SetCoinsBody body) {
        User? user = await userRepo.GetUser(id);
        if (user == null) return NotFound();
        Balance bal = await balanceRepo.SetBalance(BalanceOwnerType.User, id, body.Balance,
            $"Admin set balance (by {HttpContext.User.GetUserId()})");
        logger.LogInformation("Admin {AdminId} set coins of user {TargetId} to {Balance}",
            HttpContext.User.GetUserId(), id, body.Balance);
        return Ok(new CoinBalanceResponse { UserId = id, Coins = bal.Coins });
    }

    [HttpPost("{id}/coins/add")]
    [Authorize(Policy = "Scope:ManageEconomy")]
    public async Task<ActionResult<CoinBalanceResponse>> AddCoins(string id, [FromBody] CoinAmountBody body) {
        User? user = await userRepo.GetUser(id);
        if (user == null) return NotFound();
        Balance bal = await balanceRepo.AddCoins(BalanceOwnerType.User, id, body.Amount,
            $"Admin mint (by {HttpContext.User.GetUserId()})");
        logger.LogInformation("Admin {AdminId} added {Amount} coins to user {TargetId} (new balance {Balance})",
            HttpContext.User.GetUserId(), body.Amount, id, bal.Coins);
        return Ok(new CoinBalanceResponse { UserId = id, Coins = bal.Coins });
    }

    [HttpPost("{id}/coins/remove")]
    [Authorize(Policy = "Scope:ManageEconomy")]
    public async Task<ActionResult<CoinBalanceResponse>> RemoveCoins(string id, [FromBody] CoinAmountBody body) {
        User? user = await userRepo.GetUser(id);
        if (user == null) return NotFound();
        Balance bal = await balanceRepo.RemoveCoins(BalanceOwnerType.User, id, body.Amount,
            $"Admin burn (by {HttpContext.User.GetUserId()})");
        logger.LogInformation("Admin {AdminId} removed {Amount} coins from user {TargetId} (new balance {Balance})",
            HttpContext.User.GetUserId(), body.Amount, id, bal.Coins);
        return Ok(new CoinBalanceResponse { UserId = id, Coins = bal.Coins });
    }

    private bool IsSelf(string id) => HttpContext.User.GetUserId() == id;
}

/// <summary>
/// Admin-facing user view. Unlike <c>SanitisedUser</c>, this exposes the fields
/// an admin reasonably needs (perm level, email-verified flag, totp on/off,
/// language) but never password hashes/salts or totp secrets.
/// </summary>
public class AdminUserView {
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public bool VerifiedEmail { get; set; }
    public int PermLevel { get; set; }
    public bool TotpEnabled { get; set; }
    public string? Language { get; set; }
    public bool HasPasswordSalt { get; set; }
    public ulong Coins { get; set; }
    public DateTime DateCreated { get; set; }

    public static AdminUserView From(User u, ulong coins = 0) => new() {
        Id              = u.Id,
        Username        = u.Username,
        Email           = u.Email,
        VerifiedEmail   = u.VerifiedEmail,
        PermLevel       = u.PermLevel,
        TotpEnabled     = u.TotpEnabled,
        Language        = u.Language,
        HasPasswordSalt = !string.IsNullOrEmpty(u.PasswordSalt),
        Coins           = coins,
        DateCreated     = u.DateCreated
    };
}
