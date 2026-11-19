using SerbleAPI.Data;

namespace SerbleAPI.Tests;

public class OidcScopesTests {
    [Fact]
    public void ParseSupported_KeepsOidcAndSerblePermissions_DropsUnknown() =>
        Assert.Equal(["openid", "email", "vault", "manage_economy"],
            OidcScopes.ParseSupported("openid  email vault bogus manage_economy openid 1111"));

    [Fact]
    public void ParseSupported_Empty() {
        Assert.Empty(OidcScopes.ParseSupported(null));
        Assert.Empty(OidcScopes.ParseSupported("   "));
    }

    [Fact]
    public void OidcScopeList_BecomesPermissionBitmask() {
        string bitmask = ScopeHandler.ListOfScopeIdsToString(OidcScopes.Parse("openid profile vault economy"));
        Assert.True(bitmask.SerbleHasScope(ScopeHandler.ScopesEnum.Vault));
        Assert.True(bitmask.SerbleHasScope(ScopeHandler.ScopesEnum.Economy));
        Assert.False(bitmask.SerbleHasScope(ScopeHandler.ScopesEnum.ManageEconomy));
        Assert.False(bitmask.SerbleHasScope(ScopeHandler.ScopesEnum.UserInfo));
    }

    [Fact]
    public void OidcOnlyScopes_GrantNoPermissions() =>
        Assert.Equal("0000000000", ScopeHandler.ListOfScopeIdsToString(OidcScopes.Parse("openid profile email groups offline_access")));
}
