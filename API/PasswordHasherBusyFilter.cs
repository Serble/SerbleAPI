using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SerbleAPI.Services;

namespace SerbleAPI.API;

public sealed class PasswordHasherBusyFilter : IExceptionFilter {
    public void OnException(ExceptionContext context) {
        if (context.Exception is not PasswordHasherBusyException) return;
        context.HttpContext.Response.Headers.RetryAfter = "1";
        context.Result = new ObjectResult(new { error = "busy" }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
        context.ExceptionHandled = true;
    }
}
