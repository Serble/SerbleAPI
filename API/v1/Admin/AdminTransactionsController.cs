using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Admin;

/// <summary>
/// Admin-only coin transaction audit log. The controller-level
/// <c>[Authorize(Policy = "AdminOnly")]</c> ensures the caller is an admin User-token holder;
/// the endpoint additionally requires the <c>economy</c> scope (admins hold full access).
/// </summary>
[ApiController]
[Route("api/v1/admin/transactions")]
[Authorize(Policy = "AdminOnly")]
public class AdminTransactionsController(
    ITransactionRepository transactionRepo,
    IBalanceRepository balanceRepo,
    IUserRepository userRepo) : ControllerManager {

    public class AdminTransactionView {
        public string Id { get; set; } = "";
        public string? FromBalanceId { get; set; }
        public string? FromOwnerType { get; set; }
        public string? FromOwnerId { get; set; }
        public string? ToBalanceId { get; set; }
        public string? ToOwnerType { get; set; }
        public string? ToOwnerId { get; set; }
        public ulong Amount { get; set; }
        public string? Description { get; set; }
        public DateTime DateCreated { get; set; }
    }

    /// <summary>
    /// Lists coin transactions for auditing, newest first. Optional user filters (each accepts a
    /// username or user id):
    ///   <c>user</c> — the user is sender OR recipient;
    ///   <c>from</c> — the user is the sender;
    ///   <c>to</c>   — the user is the recipient.
    /// Filters combine (AND). With no filter, returns the global log. Paginated via
    /// <c>limit</c> (1..200, default 50) and <c>offset</c>.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "Scope:Economy")]
    public async Task<ActionResult<AdminTransactionView[]>> Get(
        [FromQuery] string? user = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0) {

        limit = Math.Clamp(limit, 1, 200);
        offset = Math.Max(0, offset);

        (BalanceOwnerType type, string id)? anyFilter = null, fromFilter = null, toFilter = null;

        if (!string.IsNullOrWhiteSpace(user)) {
            User? u = await ResolveUser(user);
            if (u == null) return NotFound($"User '{user}' not found.");
            anyFilter = (BalanceOwnerType.User, u.Id);
        }
        if (!string.IsNullOrWhiteSpace(from)) {
            User? u = await ResolveUser(from);
            if (u == null) return NotFound($"User '{from}' not found.");
            fromFilter = (BalanceOwnerType.User, u.Id);
        }
        if (!string.IsNullOrWhiteSpace(to)) {
            User? u = await ResolveUser(to);
            if (u == null) return NotFound($"User '{to}' not found.");
            toFilter = (BalanceOwnerType.User, u.Id);
        }

        Transaction[] txs = await transactionRepo.QueryTransactions(anyFilter, fromFilter, toFilter, limit, offset);

        // Resolve referenced balances to owner identities in a single lookup.
        IEnumerable<string> ids = txs
            .SelectMany(t => new[] { t.FromBalanceId, t.ToBalanceId })
            .Where(x => x != null)
            .Select(x => x!);
        Dictionary<string, Balance> balances = (await balanceRepo.GetBalancesByIds(ids))
            .ToDictionary(b => b.Id);

        AdminTransactionView[] views = txs.Select(t => {
            Balance? fromBal = t.FromBalanceId != null && balances.TryGetValue(t.FromBalanceId, out Balance? fb) ? fb : null;
            Balance? toBal = t.ToBalanceId != null && balances.TryGetValue(t.ToBalanceId, out Balance? tb) ? tb : null;
            return new AdminTransactionView {
                Id            = t.Id,
                FromBalanceId = t.FromBalanceId,
                FromOwnerType = fromBal?.OwnerType.ToString(),
                FromOwnerId   = fromBal?.OwnerId,
                ToBalanceId   = t.ToBalanceId,
                ToOwnerType   = toBal?.OwnerType.ToString(),
                ToOwnerId     = toBal?.OwnerId,
                Amount        = t.Amount,
                Description   = t.Description,
                DateCreated   = t.DateCreated
            };
        }).ToArray();

        return Ok(views);
    }

    /// <summary>Resolves a username or user id to a user (tried as id first, then name).</summary>
    private async Task<User?> ResolveUser(string idOrName)
        => await userRepo.GetUser(idOrName) ?? await userRepo.GetUserFromName(idOrName);
}
