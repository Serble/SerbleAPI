using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Tests;

public class LoginFlowRulesTests {
    private static readonly int Password = CredentialTypes.Bit(CredentialType.Password);
    private static readonly int Totp = CredentialTypes.Bit(CredentialType.Totp);
    private static readonly int Passkey = CredentialTypes.Bit(CredentialType.Passkey);
    private static readonly int All = Password | Totp | Passkey;

    [Fact]
    public void PasswordAndTotp_RequiresPasswordBeforeCode() {
        int[] flows = [Password | Totp];
        Assert.Equal(Password, LoginFlowRules.Attemptable(flows, 0));
        Assert.Equal(Totp, LoginFlowRules.Attemptable(flows, Password));
        Assert.False(LoginFlowRules.IsSatisfied(flows, Password));
        Assert.True(LoginFlowRules.IsSatisfied(flows, Password | Totp));
    }

    [Fact]
    public void CodeAlone_CanBeFirst() {
        int[] flows = [Totp];
        Assert.Equal(Totp, LoginFlowRules.Attemptable(flows, 0));
        Assert.True(LoginFlowRules.IsSatisfied(flows, Totp));
    }

    [Fact]
    public void PasskeyAndTotp_EitherOrderOfNonCodeFirst() {
        int[] flows = [Passkey | Totp, Password];
        Assert.Equal(Passkey | Password, LoginFlowRules.Attemptable(flows, 0));
        Assert.Equal(Totp, LoginFlowRules.Attemptable(flows, Passkey));
        Assert.Equal(0, LoginFlowRules.Attemptable(flows, Password));
        Assert.True(LoginFlowRules.IsSatisfied(flows, Password));
    }

    [Fact]
    public void CompletedOutsideAFlow_DisqualifiesIt() {
        int[] flows = [Password | Totp, Passkey];
        Assert.Equal(0, LoginFlowRules.Attemptable(flows, Passkey) & Totp);
    }

    [Fact]
    public void Validate_AcceptsTypicalSets() {
        Assert.Null(LoginFlowRules.Validate([Password], All));
        Assert.Null(LoginFlowRules.Validate([Password | Totp, Passkey], All));
        Assert.Null(LoginFlowRules.Validate([Totp], All));
        Assert.Null(LoginFlowRules.Validate([Passkey | Totp, Password | Totp], All));
    }

    [Fact]
    public void Validate_RejectsBadSets() {
        Assert.NotNull(LoginFlowRules.Validate([], All));
        Assert.NotNull(LoginFlowRules.Validate([0], All));
        Assert.NotNull(LoginFlowRules.Validate([Password, Password], All));
        Assert.NotNull(LoginFlowRules.Validate([Passkey], Password | Totp));
        Assert.NotNull(LoginFlowRules.Validate([1 << 9], All));
        Assert.NotNull(LoginFlowRules.Validate(Enumerable.Range(0, 9).Select(_ => Password).ToArray(), All));
    }

    [Fact]
    public void PruneUnsatisfiable_DropsFlowsWithMissingMethods() {
        int[] flows = [Password | Totp, Passkey];
        Assert.Equal([Passkey], LoginFlowRules.PruneUnsatisfiable(flows, Password | Passkey));
    }

    [Fact]
    public void Validate_AllowsRedundantFlows() {
        Assert.Null(LoginFlowRules.Validate([Password, Password | Totp], All));
        Assert.Null(LoginFlowRules.Validate([Totp, Password | Totp], All));
        Assert.Null(LoginFlowRules.Validate([Passkey, Passkey | Totp, Password | Totp], All));
    }

    [Fact]
    public void IsRedundant_WhenAnotherFlowIsAStrictSubset() {
        int[] flows = [Password, Password | Totp, Passkey | Totp];
        Assert.True(LoginFlowRules.IsRedundant(flows, Password | Totp));
        Assert.False(LoginFlowRules.IsRedundant(flows, Password));
        Assert.False(LoginFlowRules.IsRedundant(flows, Passkey | Totp));
    }

    [Fact]
    public void RedundantFlow_ShorterFlowStillSignsIn() {
        int[] flows = [Password, Password | Totp];
        Assert.Equal(Password, LoginFlowRules.Attemptable(flows, 0));
        Assert.True(LoginFlowRules.IsSatisfied(flows, Password));
    }
}
