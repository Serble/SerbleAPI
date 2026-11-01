using Microsoft.Extensions.Caching.Memory;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.Services.Impl;

public class AccountStateService(IUserRepository users, IMemoryCache cache) : IAccountStateService {

    /// <summary>
    /// Cache key prefix for the per-user account-state lookup. Routes that change that state drop
    /// the entry (via <see cref="Authentication.SerbleAuthenticationHandler.AccountStateCachePrefix"/>)
    /// so the change takes effect at once.
    /// </summary>
    public const string CachePrefix = "authstate:usable:";

    /// <summary>
    /// How long an account-state lookup is reused: the window in which a token belonging to a
    /// just-disabled or just-signed-out account still works. Not zero, because the alternative is a
    /// user row read on every authenticated request. Routes that change the state drop the entry, so
    /// this only bounds other replicas.
    /// </summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    /// <param name="Usable">Whether the account exists and is not disabled.</param>
    /// <param name="TokensValidFrom">The revocation cut-off, or null if nothing has been revoked.</param>
    private readonly record struct AccountState(bool Usable, DateTime? TokensValidFrom);

    public async Task<bool> IsTokenUsable(string userId, DateTime? issuedAt) {
        AccountState state = await GetAccountState(userId);
        return state.Usable && !IsRevoked(state, issuedAt);
    }

    public async Task<bool> IsAccountUsable(string userId) => (await GetAccountState(userId)).Usable;

    /// <summary>
    /// What the account behind a token currently says about it. Fails closed: a user row that
    /// cannot be found is unusable, which covers a token outliving the account it names, and a
    /// lookup that throws propagates rather than passing.
    /// </summary>
    private async Task<AccountState> GetAccountState(string userId) {
        if (string.IsNullOrEmpty(userId)) return new AccountState(false, null);

        string cacheKey = CachePrefix + userId;
        if (cache.TryGetValue(cacheKey, out AccountState cached)) return cached;

        User? user = await users.GetUser(userId);
        AccountState state = user == null || user.IsDisabled()
            ? new AccountState(false, null)
            : new AccountState(true, user.TokensValidFrom);
        cache.Set(cacheKey, state, Ttl);
        return state;
    }

    /// <summary>
    /// Whether a token predates the account's revocation cut-off. A token with no issue time to
    /// compare cannot be shown to postdate the cut-off, so it is rejected once one exists.
    /// </summary>
    private static bool IsRevoked(AccountState state, DateTime? issuedAt) {
        if (state.TokensValidFrom is not { } cutoff) return false;
        return issuedAt is not { } iat || iat < cutoff;
    }
}
