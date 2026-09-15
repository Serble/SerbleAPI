using System.Security.Claims;
using SerbleAPI.Authentication;
using SerbleAPI.Services;

namespace SerbleAPI.API;

/// <summary>
/// Enforces the tier declared by <see cref="RateLimitAttribute"/> on the matched endpoint.
///
/// <para>A request is charged against the caller's address <i>and</i> against the account behind
/// it. Either alone is easy to walk around: an address limit falls to a botnet, and an account
/// limit does nothing to someone trying a thousand account names.</para>
///
/// <para>Only an authenticated account is charged. An account name an unauthenticated request merely
/// claims is not, or anyone could spend that account's budget and lock its owner out; sign-in steps
/// count guesses per account themselves (<see cref="Services.Auth.LoginAttempts"/>).</para>
///
/// <para>It runs after authentication so there is an account to charge, and before authorization
/// so an over-limit caller is turned away before any handler work. Authentication is cheap for
/// the routes that matter most: the login endpoint takes Basic credentials, which the
/// authentication handler ignores, so no password is hashed for a request about to be refused.</para>
/// </summary>
public class RateLimitMiddleware(
    RequestDelegate next,
    IRateLimitService limiter,
    ILogger<RateLimitMiddleware> logger) {

    public async Task InvokeAsync(HttpContext context) {
        Endpoint? endpoint = context.GetEndpoint();
        RateLimitAttribute? rule = endpoint?.Metadata.GetMetadata<RateLimitAttribute>();

        if (rule == null || endpoint!.Metadata.GetMetadata<NoRateLimitAttribute>() != null) {
            await next(context);
            return;
        }

        // Null only for in-memory transports. One shared bucket is the safe way to be wrong; the
        // alternative is a free pass.
        string ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        string subject = "ip:" + ip;
        RateLimitDecision decision = limiter.Check(rule.Tier, RateLimitScope.Ip, ip, rule.Cost);

        // Only charge the identity when the address still has room, or one refusal would cost two
        // permits and a throttled client would burn down its other budget as well.
        if (decision.Allowed && ResolveIdentity(context.User) is { } identity) {
            RateLimitDecision byIdentity = limiter.Check(rule.Tier, RateLimitScope.Identity, identity, rule.Cost);

            // Report whichever budget is closer to running out, so the headers describe the limit
            // that will actually stop the client.
            if (!byIdentity.Allowed || (byIdentity.Tracked && byIdentity.Remaining < decision.Remaining)) {
                decision = byIdentity;
                subject = identity;
            }
        }

        ApplyHeaders(context.Response, decision);

        if (decision.Allowed) {
            await next(context);
            return;
        }

        logger.Log(
            // The overrun is worth knowing about; the retries during the lockout are the same
            // event repeating and would drown the log.
            decision.Penalised ? LogLevel.Debug : LogLevel.Warning,
            "Rate limited {Subject} on {Method} {Path} (tier {Tier}); retry in {Seconds}s",
            subject, context.Request.Method, context.Request.Path, rule.Tier,
            (int)Math.Ceiling(decision.RetryAfter.TotalSeconds));

        await WriteTooManyRequests(context, decision);
    }

    private static string? ResolveIdentity(ClaimsPrincipal user) {
        if (user.GetUserId() is { } userId) return "user:" + userId;
        if (user.GetAppId() is { } appId) return "app:" + appId;
        return null;
    }

    private static void ApplyHeaders(HttpResponse response, RateLimitDecision decision) {
        if (!decision.Tracked) return;

        response.Headers["X-RateLimit-Limit"] = decision.Limit.ToString();
        response.Headers["X-RateLimit-Remaining"] = decision.Remaining.ToString();
        response.Headers["X-RateLimit-Reset"] = ((int)Math.Ceiling(decision.Reset.TotalSeconds)).ToString();
    }

    private static Task WriteTooManyRequests(HttpContext context, RateLimitDecision decision) {
        int seconds = Math.Max(1, (int)Math.Ceiling(decision.RetryAfter.TotalSeconds));

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = seconds.ToString();
        context.Response.ContentType = "application/json";

        return context.Response.WriteAsync(
            $$"""{"error":"too_many_requests","message":"Too many requests. Retry in {{seconds}} seconds.","retry_after":{{seconds}}}""");
    }
}
