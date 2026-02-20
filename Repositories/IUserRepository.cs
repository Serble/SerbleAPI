using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

public interface IUserRepository {
    User? GetUser(string userId);
    User? GetUserFromName(string userName);
    User? GetUserFromStripeCustomerId(string customerId);
    void AddUser(User user, out User newUser);
    void UpdateUser(User user);
    void DeleteUser(string userId);
    long CountUsers();

    void AddAuthorizedApp(string userId, AuthorizedApp app);
    AuthorizedApp[] GetAuthorizedApps(string userId);
    void DeleteAuthorizedApp(string userId, string appId);
}
