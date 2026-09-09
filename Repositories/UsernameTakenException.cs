namespace SerbleAPI.Repositories;

/// <summary>
/// Thrown by <see cref="IUserRepository"/> when a write would give two accounts the same username.
/// <para>
/// The register and account-edit paths both check availability before writing, so this surfaces
/// only when another request claimed the name in between -- the unique index on the users table
/// rejects the loser. Callers should translate it into the same response their pre-check produces,
/// so a lost race is indistinguishable to the client from losing the check outright.
/// </para>
/// </summary>
public class UsernameTakenException(string username)
    : Exception($"The username '{username}' is already taken.") {

    /// <summary>The username that could not be claimed.</summary>
    public string Username { get; } = username;
}
