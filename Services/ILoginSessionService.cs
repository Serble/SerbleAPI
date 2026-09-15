using Fido2NetLib;

namespace SerbleAPI.Services;

public enum LoginPurpose {
    Login = 0,
    Reauth = 1
}

public enum LoginStepOutcome {
    /// <summary>The step was accepted and another is needed.</summary>
    Continue,
    Complete,
    /// <summary>The credential was wrong. The session, if any is returned, can be retried.</summary>
    WrongCredential,
    /// <summary>The session is unknown, finished or expired, or the method cannot be used now.</summary>
    InvalidSession,
    RateLimited
}

/// <param name="Methods">Bits of the methods that can be attempted next.</param>
/// <param name="Token">A login token, or a reauth token for a reauth session.</param>
public sealed record LoginStepResult(
    LoginStepOutcome Outcome,
    string? Handle = null,
    int Methods = 0,
    string? Token = null,
    LoginPurpose Purpose = LoginPurpose.Login,
    TimeSpan RetryAfter = default);

public sealed record LoginStarted(string Handle, int Methods, DateTime ExpiresAt);

public sealed record PasskeyOptionsResult(string Handle, AssertionOptions Options);

/// <summary>
/// Runs sign-in and re-authentication sessions. <c>callerUserId</c> is the authenticated user making
/// the request, if any; a reauth session only accepts steps from its own user.
/// <c>requiredPurpose</c> restricts a call to one kind of session.
/// </summary>
public interface ILoginSessionService {
    /// <summary>Null if there is no such user.</summary>
    Task<LoginStarted?> StartLogin(string username);

    Task<LoginStarted> StartReauth(string userId);

    Task<LoginStepResult> Password(string handle, string password, string? callerUserId,
        LoginPurpose? requiredPurpose = null, CancellationToken cancellationToken = default);

    Task<LoginStepResult> Totp(string handle, string code, string? callerUserId, LoginPurpose? requiredPurpose = null);

    /// <summary>Without a handle, starts a usernameless passkey sign-in. Null if a passkey cannot be used.</summary>
    Task<PasskeyOptionsResult?> PasskeyOptions(string? handle, string? callerUserId, LoginPurpose? requiredPurpose = null);

    Task<LoginStepResult> Passkey(string handle, AuthenticatorAssertionRawResponse assertion, string? callerUserId,
        LoginPurpose? requiredPurpose = null, CancellationToken cancellationToken = default);

    Task Cancel(string handle);
}
