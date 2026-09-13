using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SerbleAPI.Authentication;
using SerbleAPI.Config;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Account;

/// <summary>Ending sessions in bulk.</summary>
[ApiController]
[Route("api/v1/account/sessions")]
[Authorize]
public class SessionsController(
    ILogger<SessionsController> logger,
    IUserRepository userRepo,
    ITokenService tokens,
    IMemoryCache cache) : ControllerManager {

    public class LogoutAllResponse {
        /// <summary>The cut-off that was set. Tokens issued before this are no longer accepted.</summary>
        public DateTime TokensValidFrom { get; set; }

        /// <summary>
        /// A replacement token for this caller, stamped at <see cref="TokensValidFrom"/> so the cut-off
        /// does not reject it. Clients should store it over the one they sent. Named as it is on
        /// <see cref="Data.Schemas.SanitisedUser.ReplacementToken"/>: one name for one thing, so a
        /// client can adopt it the same way wherever it turns up.
        /// </summary>
        public string ReplacementToken { get; set; } = "";
    }

    /// <summary>
    /// Ends every session on the account, then hands this caller a replacement so the device that
    /// asked stays signed in. User-token only: an OAuth app must not be able to sign its owner out.
    /// </summary>
    [RateLimit(RateLimitTiers.Write)]
    [HttpPost("logoutAll")]
    [Authorize(Policy = "UserOnly")]
    public async Task<ActionResult<LogoutAllResponse>> LogoutAll() {
        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();

        DateTime cutoff = RevocationCutoff();
        await userRepo.RevokeTokensIssuedBefore(user.Id, cutoff);
        // Otherwise the retired tokens keep working until the cached account state expires.
        cache.Remove(SerbleAuthenticationHandler.AccountStateCachePrefix + user.Id);

        logger.LogInformation("User {UserId} signed out of all sessions (cut-off {Cutoff:o})", user.Id, cutoff);
        return Ok(new LogoutAllResponse {
            TokensValidFrom  = cutoff,
            ReplacementToken = tokens.GenerateLoginToken(user.Id, cutoff)
        });
    }

    /// <summary>
    /// Now, to the millisecond: every token issued before this instant stops being accepted.
    /// Milliseconds because that is the precision a token stamps its issue time at, so the two
    /// compare exactly; it also survives a <c>datetime(6)</c> round trip unchanged.
    /// </summary>
    internal static DateTime RevocationCutoff() {
        DateTime now = DateTime.UtcNow;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }
}
