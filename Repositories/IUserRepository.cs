using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

public interface IUserRepository {
    Task<User?> GetUser(string userId);
    /// <summary>Returns the users matching any of the given ids (unknown ids omitted).</summary>
    Task<User[]> GetUsers(string[] userIds);
    Task<User?> GetUserFromName(string userName);
    Task<User?> GetUserFromStripeCustomerId(string customerId);
    /// <summary>Creates the account. Throws <see cref="UsernameTakenException"/> if the username is taken.</summary>
    Task<User> AddUser(User user);
    /// <summary>Saves the account. Throws <see cref="UsernameTakenException"/> if the username is taken by another account.</summary>
    Task UpdateUser(User user);
    Task SetLastLogin(string userId, DateTime lastLogin);

    /// <summary>
    /// Refuses every token for the account issued before <paramref name="validFrom"/>. Writes
    /// <see cref="Models.DbUser.TokensValidFrom"/> directly rather than going through
    /// <see cref="UpdateUser"/>, which would let a stale <see cref="User"/> undo it.
    /// </summary>
    Task RevokeTokensIssuedBefore(string userId, DateTime validFrom);

    /// <summary>
    /// Claims TOTP step <paramref name="counter"/>, returning whether this caller got it. False means
    /// the step was already used, directly or by a later one retiring it. The test and the write are
    /// one statement, so concurrent attempts with the same code cannot both pass.
    /// </summary>
    Task<bool> TryConsumeTotpCounter(string userId, long counter);

    Task DeleteUser(string userId);
    Task<long> CountUsers();
    Task<long> CountVerifiedEmailUsers();
    Task<User[]> SearchUsers(string query, int limit);

    /// <summary>Records a grant, replacing any existing one for the same app.</summary>
    Task AddAuthorizedApp(string userId, AuthorizedApp app);
    Task<AuthorizedApp[]> GetAuthorizedApps(string userId);
    Task DeleteAuthorizedApp(string userId, string appId);
}
