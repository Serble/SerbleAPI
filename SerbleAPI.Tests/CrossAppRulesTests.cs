using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Tests;

public class CrossAppRulesTests {
    private static OAuthApp App(string id, bool official = false,
        CrossAppPolicy policy = CrossAppPolicy.Disabled, params string[] allowed) =>
        new("owner") { Id = id, IsOfficial = official, CrossAppPolicy = policy, CrossAppAllowedCallers = [..allowed] };

    // -------- Decide --------

    [Fact]
    public void Disabled_Forbids_EvenOfficialPairs() =>
        Assert.Equal(CrossAppDecision.Forbidden,
            CrossAppRules.Decide(App("chat", true), App("files", true), userHasConsented: true));

    [Fact]
    public void OfficialPair_AllowedWithoutConsent() =>
        Assert.Equal(CrossAppDecision.Allowed,
            CrossAppRules.Decide(App("chat", true), App("files", true, CrossAppPolicy.OfficialOnly), false));

    [Fact]
    public void OfficialOnly_ForbidsNonOfficialCaller_EvenWithConsent() =>
        Assert.Equal(CrossAppDecision.Forbidden,
            CrossAppRules.Decide(App("third"), App("files", true, CrossAppPolicy.OfficialOnly), true));

    [Fact]
    public void OfficialOnly_NonOfficialTarget_NeedsConsent() {
        OAuthApp target = App("selfhosted", false, CrossAppPolicy.OfficialOnly);
        Assert.Equal(CrossAppDecision.ConsentRequired, CrossAppRules.Decide(App("chat", true), target, false));
        Assert.Equal(CrossAppDecision.Allowed, CrossAppRules.Decide(App("chat", true), target, true));
    }

    [Fact]
    public void AnyWithConsent_ThirdPartyCaller_NeedsConsent() {
        OAuthApp target = App("files", true, CrossAppPolicy.AnyWithConsent);
        Assert.Equal(CrossAppDecision.ConsentRequired, CrossAppRules.Decide(App("third"), target, false));
        Assert.Equal(CrossAppDecision.Allowed, CrossAppRules.Decide(App("third"), target, true));
    }

    [Fact]
    public void Allowlist_ForbidsUnlistedCaller_EvenOfficial() =>
        Assert.Equal(CrossAppDecision.Forbidden,
            CrossAppRules.Decide(App("chat", true), App("files", true, CrossAppPolicy.AnyWithConsent, "other"), true));

    [Fact]
    public void Allowlist_ListedCaller_FollowsPolicy() =>
        Assert.Equal(CrossAppDecision.Allowed,
            CrossAppRules.Decide(App("chat", true), App("files", true, CrossAppPolicy.OfficialOnly, "chat"), false));

    [Fact]
    public void SameApp_Forbidden() {
        OAuthApp app = App("files", true, CrossAppPolicy.AnyWithConsent);
        Assert.Equal(CrossAppDecision.Forbidden, CrossAppRules.Decide(app, app, true));
    }

    // -------- Scopes and settings --------

    [Fact]
    public void EffectiveScopes_DefaultIsAccountInfo() {
        OAuthApp app = App("files");
        Assert.Equal(["user_info"], CrossAppRules.EffectiveScopes(app));
        app.CrossAppScopes = "file_host user_info";
        Assert.Equal(["file_host", "user_info"], CrossAppRules.EffectiveScopes(app));
    }

    [Fact]
    public void OidcScopesFor_AlwaysIdentityAndRefresh_EmailOnlyWithAccountInfo() {
        Assert.Equal(["openid", "profile", "email", "offline_access"], CrossAppRules.OidcScopesFor(["user_info"]));
        Assert.Equal(["openid", "profile", "email", "offline_access"], CrossAppRules.OidcScopesFor(["full_access"]));
        Assert.Equal(["openid", "profile", "offline_access"], CrossAppRules.OidcScopesFor(["file_host", "vault"]));
        Assert.Equal(["openid", "profile", "offline_access"], CrossAppRules.OidcScopesFor([]));
    }

    [Fact]
    public void TrySetScopes_AcceptsSerblePermissionsOnly() {
        OAuthApp app = App("files");
        Assert.True(CrossAppRules.TrySetScopes(app, "user_info  file_host", out _));
        Assert.Equal("user_info file_host", app.CrossAppScopes);
        Assert.False(CrossAppRules.TrySetScopes(app, "openid", out string? error));
        Assert.NotNull(error);
        Assert.Equal("user_info file_host", app.CrossAppScopes);
        Assert.True(CrossAppRules.TrySetScopes(app, "  ", out _));
        Assert.Null(app.CrossAppScopes);
    }

    [Fact]
    public void TrySetPolicy_RejectsUndefinedValues() =>
        Assert.False(CrossAppRules.TrySetPolicy(App("files"), (CrossAppPolicy)7, out _));

    [Fact]
    public void TrySetAllowedCallers_Validates() {
        OAuthApp app = App("files");
        Assert.True(CrossAppRules.TrySetAllowedCallers(app, [" a ", "b", "a", ""], out _));
        Assert.Equal(["a", "b"], app.CrossAppAllowedCallers);
        Assert.False(CrossAppRules.TrySetAllowedCallers(app, ["files"], out _));
        Assert.False(CrossAppRules.TrySetAllowedCallers(app,
            Enumerable.Range(0, CrossAppRules.MaxAllowedCallers + 1).Select(i => "app" + i), out _));
        Assert.False(CrossAppRules.TrySetAllowedCallers(app, [new string('x', 65)], out _));
        Assert.True(CrossAppRules.TrySetAllowedCallers(app, [], out _));
        Assert.Empty(app.CrossAppAllowedCallers);
    }

    [Theory]
    [InlineData("disabled", CrossAppPolicy.Disabled)]
    [InlineData("Official_Only", CrossAppPolicy.OfficialOnly)]
    [InlineData("any_with_consent", CrossAppPolicy.AnyWithConsent)]
    public void TryParsePolicy_Names(string value, CrossAppPolicy expected) {
        Assert.True(CrossAppRules.TryParsePolicy(value, out CrossAppPolicy policy));
        Assert.Equal(expected, policy);
    }

    [Fact]
    public void TryParsePolicy_RejectsUnknown() =>
        Assert.False(CrossAppRules.TryParsePolicy("1", out _));

    // -------- Grant record --------

    [Fact]
    public void GrantToRecord_NoExisting_CreatesLegacyBitmask() {
        AuthorizedApp? record = CrossAppRules.GrantToRecord("files", AuthorizedAppGrantType.Legacy, ["user_info"], null);
        Assert.NotNull(record);
        Assert.Equal(AuthorizedAppGrantType.Legacy, record.GrantType);
        Assert.Equal(ScopeHandler.ListOfScopeIdsToString(["user_info"]), record.Scopes);
    }

    [Fact]
    public void GrantToRecord_ExistingCovers_LeavesAlone() {
        AuthorizedApp existing = new("files", ScopeHandler.ListOfScopeIdsToString(["user_info", "file_host"]));
        Assert.Null(CrossAppRules.GrantToRecord("files", AuthorizedAppGrantType.Legacy, ["user_info"], existing));
    }

    [Fact]
    public void GrantToRecord_ExistingNarrower_Widens_NeverNarrows() {
        AuthorizedApp existing = new("files", ScopeHandler.ListOfScopeIdsToString(["file_host"]));
        AuthorizedApp? record = CrossAppRules.GrantToRecord("files", AuthorizedAppGrantType.Legacy, ["user_info"], existing);
        Assert.NotNull(record);
        Assert.Equal(["file_host", "user_info"], record.ScopeIds.OrderBy(s => s));
    }

    [Fact]
    public void GrantToRecord_OtherGrantType_LeftAlone() {
        AuthorizedApp oidc = new("files", "openid profile") { GrantType = AuthorizedAppGrantType.Oidc };
        Assert.Null(CrossAppRules.GrantToRecord("files", AuthorizedAppGrantType.Legacy, ["user_info"], oidc));
        AuthorizedApp legacy = new("files", ScopeHandler.ListOfScopeIdsToString(["user_info"]));
        Assert.Null(CrossAppRules.GrantToRecord("files", AuthorizedAppGrantType.Oidc, ["openid"], legacy));
    }

    [Fact]
    public void GrantToRecord_Oidc_MergesSpaceDelimited() {
        AuthorizedApp existing = new("files", "openid") { GrantType = AuthorizedAppGrantType.Oidc };
        AuthorizedApp? record = CrossAppRules.GrantToRecord("files", AuthorizedAppGrantType.Oidc, ["openid", "profile"], existing);
        Assert.NotNull(record);
        Assert.Equal(AuthorizedAppGrantType.Oidc, record.GrantType);
        Assert.Equal("openid profile", record.Scopes);
    }
}
