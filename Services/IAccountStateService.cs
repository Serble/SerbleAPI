namespace SerbleAPI.Services;

/// <summary>
/// Whether a token naming an account may still be honoured: a valid signature only proves the
/// token was issued. Shared by request authentication and by anything else that accepts a token on
/// a user's behalf (such as cross-app sign-in), so both apply the same disabled and revoked rules.
/// </summary>
public interface IAccountStateService {
    /// <summary>
    /// True when the account exists, is not disabled, and <paramref name="issuedAt"/> postdates the
    /// account's revocation cut-off (if it has one). Fails closed: an unknown account is unusable,
    /// and a token with no issue time is rejected once a cut-off exists.
    /// </summary>
    Task<bool> IsTokenUsable(string userId, DateTime? issuedAt);

    /// <summary>The account half of <see cref="IsTokenUsable"/>: it exists and is not disabled.</summary>
    Task<bool> IsAccountUsable(string userId);
}
