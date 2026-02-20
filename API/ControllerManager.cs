using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;

namespace SerbleAPI.API; 

public class ControllerManager : Controller {
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
}
