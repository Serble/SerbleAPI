using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Services;

public enum CredentialChangeStatus {
    Ok,
    NotFound,
    Invalid,
    /// <summary>The change would leave the account with no way to sign in.</summary>
    Conflict,
    RateLimited
}

/// <param name="RevokedAt">Set when the change ended the account's other sessions; tokens issued before it no longer work.</param>
public sealed record CredentialChangeResult(
    CredentialChangeStatus Status,
    string? Error = null,
    DateTime? RevokedAt = null,
    TimeSpan RetryAfter = default) {

    public static CredentialChangeResult Ok(DateTime? revokedAt = null) => new(CredentialChangeStatus.Ok, RevokedAt: revokedAt);
}

public sealed record TotpEnrolment(string Id, string Secret, string OtpAuthUri, byte[] QrPng);

/// <summary>Adding, changing and removing credentials and sign-in flows, with the checks that keep an account reachable.</summary>
public interface ICredentialService {
    /// <param name="includeSchemes">Also report how each password is stored, for admins.</param>
    Task<CredentialOverview> GetOverview(string userId, bool includeSchemes = false);

    Task<CredentialChangeResult> SetPassword(string userId, string password);

    /// <summary>Also gives the account a password-only flow if no flow uses a password, and always revokes sessions.</summary>
    Task<CredentialChangeResult> AdminSetPassword(string userId, string password);

    Task<TotpEnrolment> BeginTotp(User user, string? name);
    Task<CredentialChangeResult> VerifyTotp(string userId, string credentialId, string code);
    Task<CredentialChangeResult> Rename(string userId, string credentialId, string name);

    /// <param name="asAdmin">Skips the check that some way to sign in remains.</param>
    Task<CredentialChangeResult> Delete(string userId, string credentialId, bool asAdmin = false);

    Task<CredentialChangeResult> SetFlows(string userId, IReadOnlyList<int> masks);

    /// <summary>Adds a passkey-only flow if there is not one already.</summary>
    Task AllowPasskeyAlone(string userId);

    /// <summary>Removes TOTP credentials, dropping TOTP from the flows that used it.</summary>
    Task<CredentialChangeResult> AdminRemoveTotp(string userId);
}
