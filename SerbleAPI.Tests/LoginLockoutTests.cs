using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SerbleAPI.API;
using SerbleAPI.Config;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Services;
using SerbleAPI.Services.Auth;
using SerbleAPI.Services.Impl;

namespace SerbleAPI.Tests;

public class LoginLockoutTests {
    private readonly RateLimitService _limiter =
        new(Options.Create(new RateLimitSettings()), NullLogger<RateLimitService>.Instance);

    private readonly TokenService _tokens = new(Options.Create(new JwtSettings {
        Audience = "test", Issuer = "test", Secret = new string('k', 64)
    }), NullLogger<TokenService>.Instance);

    private static readonly User Victim = new() { Id = "victim" };

    private bool Attempt(User user, string address, string? deviceToken = null) =>
        LoginAttempts.Charge(_limiter, _tokens, CredentialType.Password, user,
            new LoginClient(IPAddress.Parse(address), deviceToken)).Allowed;

    private void ExhaustSharedBudget(User user) {
        for (int a = 1; a <= 10; a++)
        for (int i = 0; i < 5; i++)
            Attempt(user, $"203.0.113.{a}");
    }

    [Fact]
    public void OneAddress_CannotLockOutAnother() {
        for (int i = 0; i < 100; i++) Attempt(Victim, "203.0.113.9");

        Assert.False(Attempt(Victim, "203.0.113.9"));
        Assert.True(Attempt(Victim, "198.51.100.7"));
    }

    [Fact]
    public void ManyAddresses_AreBoundedByTheSharedBudget() {
        int allowed = 0;
        for (int a = 1; a <= 50; a++)
        for (int i = 0; i < 5; i++)
            if (Attempt(Victim, $"203.0.113.{a}")) allowed++;

        Assert.Equal(30, allowed);
        Assert.False(Attempt(Victim, "198.51.100.7"));
    }

    [Fact]
    public void RecognisedDevice_IsNotHeldBackByOthers() {
        string device = _tokens.GenerateDeviceToken(Victim.Id);
        ExhaustSharedBudget(Victim);

        Assert.True(Attempt(Victim, "203.0.113.1", device));
    }

    [Fact]
    public void RecognisedDevice_OnlyLocksOutItself() {
        string device = _tokens.GenerateDeviceToken(Victim.Id);
        for (int i = 0; i < 5; i++) Assert.True(Attempt(Victim, "198.51.100.7", device));

        Assert.False(Attempt(Victim, "198.51.100.7", device));
        Assert.True(Attempt(Victim, "198.51.100.7"));
    }

    [Fact]
    public void DeviceTokenForAnotherAccount_IsNotRecognised() {
        string attackersOwn = _tokens.GenerateDeviceToken("attacker");
        ExhaustSharedBudget(Victim);

        Assert.False(Attempt(Victim, "198.51.100.7", attackersOwn));
    }

    [Fact]
    public void DeviceFromBeforeSignOutEverywhere_IsStillRecognised() {
        string device = _tokens.GenerateDeviceToken("signed-out");
        User user = new() { Id = "signed-out", TokensValidFrom = DateTime.UtcNow.AddSeconds(1) };
        ExhaustSharedBudget(user);

        Assert.True(Attempt(user, "198.51.100.7", device));
    }

    [Fact]
    public void Ipv6_CountsBySlash64() {
        for (int i = 1; i <= 5; i++) Assert.True(Attempt(Victim, $"2001:db8::{i}"));

        Assert.False(Attempt(Victim, "2001:db8::ffff"));
        Assert.True(Attempt(Victim, "2001:db8:0:1::1"));
    }

    [Fact]
    public void Ipv4MappedAddress_CountsAsIpv4() =>
        Assert.Equal("192.0.2.1", LoginAttempts.AddressKey(IPAddress.Parse("::ffff:192.0.2.1")));

    [Fact]
    public async Task Middleware_DoesNotChargeAnUnauthenticatedLoginName() {
        RateLimitMiddleware middleware = new(_ => Task.CompletedTask, _limiter, NullLogger<RateLimitMiddleware>.Instance);
        Endpoint endpoint = new(null, new EndpointMetadataCollection(new RateLimitAttribute(RateLimitTiers.Auth)), "auth");

        async Task<int> Send(string address) {
            DefaultHttpContext http = new();
            http.SetEndpoint(endpoint);
            http.Connection.RemoteIpAddress = IPAddress.Parse(address);
            http.Request.Headers.Authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("victim:wrong"));
            await middleware.InvokeAsync(http);
            return http.Response.StatusCode;
        }

        for (int i = 0; i < 50; i++) await Send("203.0.113.9");

        Assert.Equal(StatusCodes.Status429TooManyRequests, await Send("203.0.113.9"));
        Assert.Equal(StatusCodes.Status200OK, await Send("198.51.100.7"));
    }
}
