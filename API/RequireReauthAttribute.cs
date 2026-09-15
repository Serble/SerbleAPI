using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using SerbleAPI.Authentication;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;

namespace SerbleAPI.API;

/// <summary>
/// Requires a <see cref="HeaderName"/> reauth token for the calling user, showing they completed a
/// sign-in flow in the last few minutes. Answers 403 <c>reauth_required</c> otherwise.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireReauthAttribute : Attribute, IAsyncActionFilter {
    public const string HeaderName = "Serble-Reauth";

    private const string ExpiresItemKey = "serble.reauth.expires";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next) {
        if (!await IsSatisfied(context.HttpContext)) {
            context.Result = new ObjectResult(new { error = "reauth_required" }) { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }
        await next();
    }

    /// <summary>When the reauth token this request presented expires.</summary>
    public static DateTime? ExpiresAt(HttpContext http) =>
        http.Items.TryGetValue(ExpiresItemKey, out object? value) ? value as DateTime? : null;

    private static async Task<bool> IsSatisfied(HttpContext http) {
        if (!http.User.IsUser() || http.User.GetUserId() is not { } userId) return false;
        if (!http.Request.Headers.TryGetValue(HeaderName, out StringValues header)) return false;

        ITokenService tokens = http.RequestServices.GetRequiredService<ITokenService>();
        if (!tokens.ValidateReauthToken(header.ToString(), out string? tokenUserId, out DateTime? issuedAt, out DateTime expiresAt)
            || tokenUserId != userId
            || issuedAt == null) return false;

        // Read from the database rather than the authentication cache, so a sign-out applies at once.
        User? user = await http.RequestServices.GetRequiredService<IUserRepository>().GetUser(userId);
        if (user == null || user.IsDisabled()) return false;
        if (user.TokensValidFrom is { } cutoff && issuedAt < cutoff) return false;

        http.Items[ExpiresItemKey] = expiresAt;
        return true;
    }
}
