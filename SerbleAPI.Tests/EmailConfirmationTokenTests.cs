using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SerbleAPI.Config;
using SerbleAPI.Services.Impl;

namespace SerbleAPI.Tests;

public class EmailConfirmationTokenTests {
    private readonly TokenService _tokens = new(Options.Create(new JwtSettings {
        Audience = "test", Issuer = "test", Secret = new string('k', 64)
    }), NullLogger<TokenService>.Instance);

    [Fact]
    public void ConfirmationLink_ExpiresWithinADay() {
        string token = _tokens.GenerateEmailConfirmationToken("user", "a@example.com");

        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        TimeSpan lifetime = jwt.ValidTo - jwt.ValidFrom;

        Assert.Equal(TimeSpan.FromHours(TokenService.EmailConfirmationLifetimeHours), lifetime);
        Assert.True(_tokens.ValidateEmailConfirmationToken(token, out string? userId, out string email));
        Assert.Equal("user", userId);
        Assert.Equal("a@example.com", email);
    }
}
