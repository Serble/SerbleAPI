using System.Net;
using GeneralPurposeLib;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;

namespace SerbleAPI.API; 

public class ControllerManager : Controller {
    
    public override void OnActionExecuting(ActionExecutingContext context) {
        
        // Add CORS headers to all responses
        context.HttpContext.Response.Headers.Add("Access-Control-Allow-Origin", "*");
        context.HttpContext.Response.Headers.Add("Access-Control-Allow-Headers", "*");
        context.HttpContext.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS, PATCH");
        context.HttpContext.Response.Headers.Add("Access-Control-Allow-Credentials", "true");

        // get ip address
        IPAddress? ip = Request.HttpContext.Connection.RemoteIpAddress;
        
        // Somehow it can be null
        string ipStr = ip == null ? "Unknown IP" : ip.ToString();

        base.OnActionExecuting(context);

        // Log the users information for debugging purposes
        Logger.Debug(context.HttpContext.Request.Headers.TryGetValue("User-Agent", out StringValues header) 
            ? $"New request from: {ipStr} ({header})"
            : $"New request from: {ipStr} (Unknown user agent)");
        
    }
    
}