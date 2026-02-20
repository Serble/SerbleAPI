using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;
using SerbleAPI.Config;

namespace SerbleAPI.API;

/// <summary>
/// Single middleware that handles both CORS and automatic OPTIONS responses.
///
/// CORS policy:
///   • Passkey routes (<c>api/v1/auth/passkey/**</c>): reflects the request
///     <c>Origin</c> back only when it is in <see cref="PasskeySettings.AllowedOrigins"/>.
///     Allowed headers: serbleauth, Content-Type, authorization.
///     No origin reflected (= browser-blocked) when the origin is not in the list.
///   • All other routes: <c>Access-Control-Allow-Origin: *</c> (open).
///     All headers and methods allowed.
///
/// OPTIONS handling:
///   Every OPTIONS request is short-circuited with 200 OK and an <c>Allow</c> header
///   built by inspecting the live MVC action descriptor registry — no explicit
///   <c>[HttpOptions]</c> endpoints needed anywhere in the codebase.
/// </summary>
public class SerbleCorsMiddleware(
    RequestDelegate next,
    IOptions<PasskeySettings> passkeySettings,
    IActionDescriptorCollectionProvider descriptors) {

    private const string PasskeyPathPrefix = "/api/v1/auth/passkey";
    private const string AllowedHeaders = "serbleauth, Content-Type, authorization";
    private const string AllowedMethods = "GET, POST, PUT, DELETE, OPTIONS, PATCH";

    public Task InvokeAsync(HttpContext context) {
        string path   = context.Request.Path.Value ?? "/";
        string? origin = context.Request.Headers.Origin;

        bool isPasskeyPath = path.StartsWith(PasskeyPathPrefix, StringComparison.OrdinalIgnoreCase);

        ApplyCorsHeaders(context.Response, origin, isPasskeyPath);

        // Short-circuit OPTIONS — browser CORS preflight and plain method-discovery alike.
        if (HttpMethods.IsOptions(context.Request.Method)) {
            return HandleOptions(context, path);
        }

        return next(context);
    }

    // CORS header logic
    private void ApplyCorsHeaders(HttpResponse response, string? origin, bool isPasskeyPath) {
        if (isPasskeyPath) {
            // Reflect the origin only when it is in the passkey allow-list.
            if (origin != null &&
                passkeySettings.Value.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)) {
                response.Headers.AccessControlAllowOrigin = origin;
                response.Headers.AccessControlAllowHeaders = AllowedHeaders;
                response.Headers.AccessControlAllowMethods = AllowedMethods;
                response.Headers.AccessControlAllowCredentials = "true";
                response.Headers.Vary = "Origin";
            }
            // No ACAO header → browser enforces block for non-whitelisted origins.
        }
        else {
            // Open policy for all non-passkey routes.
            response.Headers.AccessControlAllowOrigin = "*";
            response.Headers.AccessControlAllowHeaders = "*";
            response.Headers.AccessControlAllowMethods = AllowedMethods;
        }
    }

    // OPTIONS short-circuit
    private Task HandleOptions(HttpContext context, string path) {
        HashSet<string> methods = MethodsForPath(path);

        // No registered route matched → fall through so the 404 pipeline runs.
        if (methods.Count == 0) {
            return next(context);
        }

        methods.Add("OPTIONS");
        context.Response.Headers.Allow = string.Join(", ", methods.OrderBy(m => m));
        context.Response.StatusCode = StatusCodes.Status200OK;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Walks the MVC route registry and returns every HTTP method registered
    /// for any action whose route template matches <paramref name="requestPath"/>.
    /// Route parameter segments (<c>{id}</c>, <c>{id:int}</c>, etc.) are treated
    /// as wildcards so <c>api/v1/app/{appid}</c> matches <c>/api/v1/app/abc</c>.
    /// </summary>
    private HashSet<string> MethodsForPath(string requestPath) {
        HashSet<string> methods = new(StringComparer.OrdinalIgnoreCase);

        foreach (ControllerActionDescriptor d in
            descriptors.ActionDescriptors.Items.OfType<ControllerActionDescriptor>()) {
            string? template = d.AttributeRouteInfo?.Template;
            if (template == null || !TemplateMatches(template, requestPath)) {
                continue;
            }

            IEnumerable<string> actionMethods = 
                d.ActionConstraints?.OfType<HttpMethodActionConstraint>()
                    .SelectMany(c => c.HttpMethods)
                ?? [];

            foreach (string m in actionMethods) {
                methods.Add(m.ToUpperInvariant());
            }
        }

        return methods;
    }

    private static bool TemplateMatches(string template, string requestPath) {
        ReadOnlySpan<char> tSpan = template.TrimStart('/');
        ReadOnlySpan<char> pSpan = requestPath.TrimStart('/');

        // Drop query string
        int q = pSpan.IndexOf('?');
        if (q >= 0) pSpan = pSpan[..q];

        string[] tParts = tSpan.Length == 0 ? [] : tSpan.ToString().Split('/');
        string[] pParts = pSpan.Length == 0 ? [] : pSpan.ToString().Split('/');

        if (tParts.Length != pParts.Length) {
            return false;
        }

        for (int i = 0; i < tParts.Length; i++) {
            string t = tParts[i];
            // Route parameters: {id}, {id?}, {id:guid}, etc.
            if (t.StartsWith('{') && t.EndsWith('}')) continue;
            if (!t.Equals(pParts[i], StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }
}
