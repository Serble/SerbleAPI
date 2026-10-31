using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Tests;

public class OidcConsentRulesTests {
    private static AuthorizedApp Oidc(string scopes) =>
        new("app", scopes) { GrantType = AuthorizedAppGrantType.Oidc };

    [Fact]
    public void NoGrant_Asks() =>
        Assert.False(OidcConsentRules.CanSkipConsent(false, null, ["openid"], null));

    [Fact]
    public void GrantCoversRequest_Skips() =>
        Assert.True(OidcConsentRules.CanSkipConsent(false, null, ["openid", "email"], Oidc("openid profile email")));

    [Fact]
    public void NewScopeRequested_Asks() =>
        Assert.False(OidcConsentRules.CanSkipConsent(false, null, ["openid", "groups"], Oidc("openid profile")));

    [Fact]
    public void LegacyGrant_NeverCounts() =>
        Assert.False(OidcConsentRules.CanSkipConsent(false, null, [], new AuthorizedApp("app", "1111")));

    [Fact]
    public void OfficialApp_SkipsWithoutGrant() =>
        Assert.True(OidcConsentRules.CanSkipConsent(true, null, ["openid", "offline_access"], null));

    [Theory]
    [InlineData("consent")]
    [InlineData("login consent")]
    public void PromptConsent_AlwaysAsks(string prompt) {
        Assert.False(OidcConsentRules.CanSkipConsent(false, prompt, ["openid"], Oidc("openid")));
        Assert.False(OidcConsentRules.CanSkipConsent(true, prompt, ["openid"], null));
    }

    [Fact]
    public void OtherPrompt_DoesNotForce() =>
        Assert.True(OidcConsentRules.CanSkipConsent(false, "login", ["openid"], Oidc("openid")));

    [Fact]
    public void Merge_UnionsWithOidcGrant() =>
        Assert.Equal("openid profile email",
            OidcConsentRules.MergedGrantScopes(["openid", "email"], Oidc("openid profile")));

    [Fact]
    public void Merge_ReplacesLegacyGrant() =>
        Assert.Equal("openid", OidcConsentRules.MergedGrantScopes(["openid"], new AuthorizedApp("app", "1111")));
}
