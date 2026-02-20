using Microsoft.Extensions.Options;
using SerbleAPI.Config;

namespace SerbleAPI.API;

/// <summary>
/// Simple redirection middleware that just uses the config
/// to redirect certain paths to their specified URLs.
/// </summary>
/// <param name="next"></param>
/// <param name="settings"></param>
public class RedirectsMiddleware(RequestDelegate next, IOptions<ApiSettings> settings) {

    public async Task InvokeAsync(HttpContext context) {
        string path = context.Request.Path;
        
        if (settings.Value.Redirects.TryGetValue(path.Trim('/'), out string? redirect)) {
            context.Response.Redirect(redirect);
            return;
        }

        await next(context);
    }
}
