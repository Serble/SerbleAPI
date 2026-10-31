using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SerbleAPI.Config;
using SerbleAPI.Services;
using SerbleAPI.Services.Impl;

namespace SerbleAPI.Tests;

public class RateLimitPeekTests {
    private readonly RateLimitService _limiter =
        new(Options.Create(new RateLimitSettings()), NullLogger<RateLimitService>.Instance);

    private const string Address = "203.0.113.5";

    [Fact]
    public void Peek_DoesNotDrawPermits() {
        for (int i = 0; i < 100; i++)
            Assert.True(_limiter.Peek(RateLimitTiers.Auth, RateLimitScope.Ip, Address).Allowed);

        Assert.True(_limiter.Check(RateLimitTiers.Auth, RateLimitScope.Ip, Address).Allowed);
    }

    [Fact]
    public void Peek_RefusesOnceTheBudgetIsSpent() {
        for (int i = 0; i < 20; i++) _limiter.Check(RateLimitTiers.Auth, RateLimitScope.Ip, Address);
        Assert.False(_limiter.Peek(RateLimitTiers.Auth, RateLimitScope.Ip, Address).Allowed);

        // The overrun that triggers the lockout, which peeking must also respect.
        _limiter.Check(RateLimitTiers.Auth, RateLimitScope.Ip, Address);
        RateLimitDecision decision = _limiter.Peek(RateLimitTiers.Auth, RateLimitScope.Ip, Address);
        Assert.False(decision.Allowed);
        Assert.True(decision.Penalised);
        Assert.True(decision.RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public void Peek_IsPerAddress() {
        for (int i = 0; i < 25; i++) _limiter.Check(RateLimitTiers.Auth, RateLimitScope.Ip, Address);

        Assert.True(_limiter.Peek(RateLimitTiers.Auth, RateLimitScope.Ip, "198.51.100.7").Allowed);
    }
}
