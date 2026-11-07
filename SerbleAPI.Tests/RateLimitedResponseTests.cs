using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SerbleAPI.API;
using SerbleAPI.Config;
using SerbleAPI.Services.Impl;

namespace SerbleAPI.Tests;

/// <summary>A refusal from a handler must read the same as one from the middleware.</summary>
public class RateLimitedResponseTests {

    private sealed class Probe : ControllerManager {
        public ObjectResult Refuse(TimeSpan retryAfter) => TooManyRequests(retryAfter);
    }

    [Fact]
    public void Handler_ReturnsTheSharedBody() {
        Probe probe = new() { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

        ObjectResult result = probe.Refuse(TimeSpan.FromSeconds(839.2));

        Assert.Equal(StatusCodes.Status429TooManyRequests, result.StatusCode);
        Assert.Equal("840", probe.Response.Headers.RetryAfter.ToString());
        RateLimitedResponse body = Assert.IsType<RateLimitedResponse>(result.Value);
        Assert.Equal(new RateLimitedResponse("too_many_requests", "Too many requests. Retry in 840 seconds.", 840), body);
    }

    [Fact]
    public async Task Middleware_WritesTheSharedBody() {
        RateLimitService limiter = new(Options.Create(new RateLimitSettings()), NullLogger<RateLimitService>.Instance);
        RateLimitMiddleware middleware = new(_ => Task.CompletedTask, limiter, NullLogger<RateLimitMiddleware>.Instance);
        Endpoint endpoint = new(null, new EndpointMetadataCollection(new RateLimitAttribute(RateLimitTiers.Auth)), "auth");

        async Task<DefaultHttpContext> Send() {
            DefaultHttpContext http = new();
            http.SetEndpoint(endpoint);
            http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.42");
            http.Response.Body = new MemoryStream();
            await middleware.InvokeAsync(http);
            return http;
        }

        for (int i = 0; i < 20; i++) await Send();
        DefaultHttpContext refused = await Send();

        Assert.Equal(StatusCodes.Status429TooManyRequests, refused.Response.StatusCode);
        refused.Response.Body.Position = 0;
        RateLimitedResponse? body = await JsonSerializer.DeserializeAsync<RateLimitedResponse>(refused.Response.Body);

        Assert.NotNull(body);
        Assert.Equal("too_many_requests", body.Error);
        Assert.Equal(refused.Response.Headers.RetryAfter.ToString(), body.RetryAfter.ToString());
        Assert.Equal($"Too many requests. Retry in {body.RetryAfter} seconds.", body.Message);
    }
}
