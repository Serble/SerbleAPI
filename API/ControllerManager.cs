using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using SerbleAPI.Services;

namespace SerbleAPI.API; 

public class ControllerManager : Controller {

    /// <summary>Carries the device token a completed sign-in gave the client.</summary>
    public const string DeviceTokenHeader = "Serble-Device";

    private ILogger Logger => HttpContext.RequestServices
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger(GetType());
    
    public override void OnActionExecuting(ActionExecutingContext context) {
        IPAddress? ip = Request.HttpContext.Connection.RemoteIpAddress;
        string ipStr = ip == null ? "Unknown IP" : ip.ToString();

        base.OnActionExecuting(context);

        Logger.LogDebug(context.HttpContext.Request.Headers.TryGetValue("User-Agent", out StringValues header) 
            ? $"New request from: {ipStr} ({header})"
            : $"New request from: {ipStr} (Unknown user agent)");
    }

    protected LoginClient CurrentLoginClient =>
        new(HttpContext.Connection.RemoteIpAddress, Request.Headers[DeviceTokenHeader].FirstOrDefault());

    protected ObjectResult TooManyRequests(TimeSpan retryAfter, object? body = null) {
        Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        return StatusCode(StatusCodes.Status429TooManyRequests, body ?? new { error = "rate_limited" });
    }
}
