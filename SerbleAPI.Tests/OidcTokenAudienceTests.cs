using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SerbleAPI.Config;
using SerbleAPI.Services;
using SerbleAPI.Services.Impl;

namespace SerbleAPI.Tests;

public class OidcTokenAudienceTests {
    private static OidcTokenService Service(string? apiAudience = null) {
        using RSA rsa = RSA.Create(2048);
        IOptions<OidcSettings> settings = Options.Create(new OidcSettings {
            Issuer      = "https://auth.example/",
            ApiAudience = apiAudience,
            SigningKeys = [new OidcSigningKeySettings { Kid = "k1", PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem() }]
        });
        return new OidcTokenService(settings, new OidcKeyService(settings, NullLogger<OidcKeyService>.Instance),
            NullLogger<OidcTokenService>.Instance);
    }

    [Fact]
    public void AccessToken_IsForTheApi_AndRoundTrips() {
        OidcTokenService service = Service();
        string token = service.GenerateAccessToken("user", "client", "openid vault");

        Assert.Equal(["https://auth.example/api"], new JwtSecurityTokenHandler().ReadJwtToken(token).Audiences);
        Assert.True(service.ValidateAccessToken(token, out OidcAccessTokenInfo? info));
        Assert.Equal("client", info!.ClientId);
        Assert.Equal("openid vault", info.Scope);
    }

    [Fact]
    public void ConfiguredAudience_IsUsed() {
        string token = Service("https://api.example").GenerateAccessToken("user", "client", "openid");
        Assert.Equal(["https://api.example"], new JwtSecurityTokenHandler().ReadJwtToken(token).Audiences);
    }

    [Fact]
    public void IdToken_IsNotAnAccessToken() {
        OidcTokenService service = Service();
        string idToken = service.GenerateIdToken("user", "client", null, 0, []);
        Assert.False(service.ValidateAccessToken(idToken, out _));
    }
}
