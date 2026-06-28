using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

public interface IUserRepository {
    Task<User?> GetUser(string userId);
    /// <summary>Returns the users matching any of the given ids (unknown ids omitted).</summary>
    Task<User[]> GetUsers(string[] userIds);
    Task<User?> GetUserFromName(string userName);
    Task<User?> GetUserFromStripeCustomerId(string customerId);
    Task<User> AddUser(User user);
    Task UpdateUser(User user);
    Task DeleteUser(string userId);
    Task<long> CountUsers();
    Task<long> CountVerifiedEmailUsers();
    Task<User[]> SearchUsers(string query, int limit);

    Task AddAuthorizedApp(string userId, AuthorizedApp app);
    Task<AuthorizedApp[]> GetAuthorizedApps(string userId);
    Task DeleteAuthorizedApp(string userId, string appId);
}
