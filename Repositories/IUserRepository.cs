using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

public interface IUserRepository {
    Task<User?> GetUser(string userId);
    Task<User?> GetUserFromName(string userName);
    Task<User?> GetUserFromStripeCustomerId(string customerId);
    Task<User> AddUser(User user);
    Task UpdateUser(User user);
    Task DeleteUser(string userId);
    Task<long> CountUsers();

    Task AddAuthorizedApp(string userId, AuthorizedApp app);
    Task<AuthorizedApp[]> GetAuthorizedApps(string userId);
    Task DeleteAuthorizedApp(string userId, string appId);
}
