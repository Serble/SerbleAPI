using System.Text;
using Fido2NetLib;
using Fido2NetLib.Objects;
using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;
using SerbleAPI.Repositories;
using SerbleAPI.Services.Auth;

namespace SerbleAPI.Services.Impl;

public class LoginSessionService(
    ILogger<LoginSessionService> logger,
    IFido2 fido,
    ITokenService tokens,
    IPasswordHasher hasher,
    IRateLimitService rateLimit,
    IUserRepository users,
    ICredentialRepository credentials,
    IPasskeyRepository passkeys,
    ILoginSessionRepository sessions) : ILoginSessionService {

    public static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(10);

    private sealed record SessionContext(string Handle, string Hash, DbLoginSession Session, User? User, int[] Flows) {
        public int Attemptable => LoginFlowRules.Attemptable(Flows, Session.CompletedMask);
        public bool CanAttempt(CredentialType type) => (Attemptable & CredentialTypes.Bit(type)) != 0;
    }

    private static LoginStepResult Invalid => new(LoginStepOutcome.InvalidSession);

    public async Task<LoginStarted?> StartLogin(string username) {
        User? user = await users.GetUserFromName(username);
        return user == null ? null : await Create(user.Id, LoginPurpose.Login);
    }

    public Task<LoginStarted> StartReauth(string userId) => Create(userId, LoginPurpose.Reauth);

    private async Task<LoginStarted> Create(string? userId, LoginPurpose purpose) {
        await sessions.DeleteExpired();

        string handle = OidcCrypto.NewHandle();
        DateTime now = DateTime.UtcNow;
        await sessions.Create(new DbLoginSession {
            IdHash    = OidcCrypto.HashToken(handle),
            UserId    = userId,
            Purpose   = (int)purpose,
            CreatedAt = now,
            ExpiresAt = now + SessionLifetime
        });

        int methods = userId == null
            ? CredentialTypes.Bit(CredentialType.Passkey)
            : LoginFlowRules.Attemptable(await UsableFlows(userId), 0);
        return new LoginStarted(handle, methods, now + SessionLifetime);
    }

    public async Task<LoginStepResult> Password(string handle, string password, string? callerUserId, LoginClient client,
        LoginPurpose? requiredPurpose = null, CancellationToken cancellationToken = default) {
        SessionContext? ctx = await Load(handle, callerUserId, requiredPurpose);
        if (ctx?.User == null || !ctx.CanAttempt(CredentialType.Password)) return Invalid;

        RateLimitDecision limit = LoginAttempts.Charge(rateLimit, tokens, CredentialType.Password, ctx.User, client);
        if (!limit.Allowed) return RateLimited(limit);

        UserCredential? credential = await credentials.GetActivePassword(ctx.User.Id);
        if (credential == null) return Wrong(ctx);

        PasswordCheck check = await hasher.Verify(credential, password, cancellationToken);
        if (!check.Valid) return Wrong(ctx);

        if (check.NeedsUpgrade) await TryUpgradePassword(credential, password, check, cancellationToken);

        return await Record(ctx, CredentialType.Password, credential.Id);
    }

    private async Task TryUpgradePassword(UserCredential credential, string password, PasswordCheck check,
        CancellationToken cancellationToken) {
        try {
            string upgraded = await hasher.Hash(password, cancellationToken);
            await credentials.UpgradePassword(credential.Id, credential.UserId, credential.Secret!, upgraded, check.LegacyDigest);
        }
        catch (PasswordHasherBusyException) {
            // Upgraded on a later sign-in instead.
        }
    }

    public async Task<LoginStepResult> Totp(string handle, string code, string? callerUserId, LoginClient client,
        LoginPurpose? requiredPurpose = null) {
        SessionContext? ctx = await Load(handle, callerUserId, requiredPurpose);
        if (ctx?.User == null || !ctx.CanAttempt(CredentialType.Totp)) return Invalid;

        RateLimitDecision limit = LoginAttempts.Charge(rateLimit, tokens, CredentialType.Totp, ctx.User, client);
        if (!limit.Allowed) return RateLimited(limit);

        UserCredential? credential = await TotpCodes.VerifyAndConsume(credentials, ctx.User.Id, code);
        if (credential == null) return Wrong(ctx);

        return await Record(ctx, CredentialType.Totp, credential.Id);
    }

    public async Task<PasskeyOptionsResult?> PasskeyOptions(string? handle, string? callerUserId,
        LoginPurpose? requiredPurpose = null) {
        List<PublicKeyCredentialDescriptor> allowed = [];
        string hash;

        if (handle == null) {
            if (requiredPurpose == LoginPurpose.Reauth) return null;
            handle = (await Create(null, LoginPurpose.Login)).Handle;
            hash = OidcCrypto.HashToken(handle);
        }
        else {
            SessionContext? ctx = await Load(handle, callerUserId, requiredPurpose);
            if (ctx?.User == null || !ctx.CanAttempt(CredentialType.Passkey)) return null;
            hash = ctx.Hash;
            allowed = (await passkeys.GetUsersPasskeys(ctx.User.Id)).Select(k => k.Descriptor!).ToList();
        }

        AuthenticationExtensionsClientInputs extensions = new() {
            Extensions             = true,
            UserVerificationMethod = true,
            DevicePubKey           = new AuthenticationExtensionsDevicePublicKeyInputs()
        };
        AssertionOptions options = fido.GetAssertionOptions(allowed, UserVerificationRequirement.Required, extensions);
        await sessions.SetPasskeyChallenge(hash, options.ToJson());
        return new PasskeyOptionsResult(handle, options);
    }

    public async Task<LoginStepResult> Passkey(string handle, AuthenticatorAssertionRawResponse assertion,
        string? callerUserId, LoginPurpose? requiredPurpose = null, CancellationToken cancellationToken = default) {
        SessionContext? ctx = await Load(handle, callerUserId, requiredPurpose);
        if (ctx?.Session.PasskeyChallenge is not { } challengeJson) return Invalid;
        if (!await sessions.TakePasskeyChallenge(ctx.Hash)) return Invalid;

        SavedPasskey? passkey = await passkeys.GetPasskey(assertion.Id);
        if (passkey == null) return Wrong(ctx);
        if (ctx.User != null && passkey.OwnerId != ctx.User.Id) return Wrong(ctx);

        // A usernameless session learns its user from the passkey.
        SessionContext? owner = ctx.User != null ? ctx : await WithUser(ctx, passkey.OwnerId);
        if (owner == null) return Invalid;
        if (!owner.CanAttempt(CredentialType.Passkey)) return Wrong(ctx);

        try {
            IsUserHandleOwnerOfCredentialIdAsync ownsCredential = async (args, _) => {
                string userHandle = Encoding.UTF8.GetString(args.UserHandle);
                SavedPasskey[] owned = await passkeys.GetUsersPasskeys(userHandle);
                return owned.Any(c => c.Descriptor!.Id.SequenceEqual(args.CredentialId));
            };

            VerifyAssertionResult result = await fido.MakeAssertionAsync(
                assertion, AssertionOptions.FromJson(challengeJson),
                passkey.PublicKey!, passkey.DevicePublicKeys ?? [], passkey.SignCount,
                ownsCredential, cancellationToken: cancellationToken);

            await passkeys.SetPasskeySignCount(result.CredentialId, (int)result.SignCount);
            if (result.DevicePublicKey is not null) {
                byte[][] updated = (passkey.DevicePublicKeys ?? []).Append(result.DevicePublicKey).ToArray();
                await passkeys.UpdatePasskeyDevicePublicKeys(result.CredentialId, updated);
            }
        }
        catch (Exception e) {
            logger.LogDebug("Passkey assertion failed: {Message}", e.Message);
            return Wrong(ctx);
        }

        if (ctx.User == null && !await sessions.BindUser(ctx.Hash, passkey.OwnerId)) return Invalid;
        return await Record(owner, CredentialType.Passkey, passkey.UserCredentialId);
    }

    public Task Cancel(string handle) => sessions.Delete(OidcCrypto.HashToken(handle));

    private async Task<SessionContext?> Load(string? handle, string? callerUserId, LoginPurpose? requiredPurpose) {
        if (string.IsNullOrEmpty(handle) || handle.Length > 128) return null;

        string hash = OidcCrypto.HashToken(handle);
        DbLoginSession? session = await sessions.GetLive(hash);
        if (session == null) return null;

        LoginPurpose purpose = (LoginPurpose)session.Purpose;
        if (requiredPurpose != null && purpose != requiredPurpose) return null;
        if (purpose == LoginPurpose.Reauth && (callerUserId == null || callerUserId != session.UserId)) return null;

        SessionContext ctx = new(handle, hash, session, null, []);
        return session.UserId == null ? ctx : await WithUser(ctx, session.UserId);
    }

    private async Task<SessionContext?> WithUser(SessionContext ctx, string userId) {
        User? user = await users.GetUser(userId);
        if (user == null) return null;
        // Signing out everywhere also ends sign-ins that were still in progress.
        if (user.TokensValidFrom is { } cutoff && ctx.Session.CreatedAt < cutoff) return null;
        return ctx with { User = user, Flows = await UsableFlows(userId) };
    }

    private async Task<int[]> UsableFlows(string userId) =>
        LoginFlowRules.PruneUnsatisfiable(await credentials.GetFlowMasks(userId), await credentials.GetActiveTypeMask(userId));

    private async Task<LoginStepResult> Record(SessionContext ctx, CredentialType type, string credentialId) {
        User user = ctx.User!;
        // Checked only once a credential is proven, so a disabled account reads like a wrong credential.
        if (user.IsDisabled()) {
            await sessions.Delete(ctx.Hash);
            return new LoginStepResult(LoginStepOutcome.WrongCredential);
        }

        if (!await sessions.AddCompleted(ctx.Hash, CredentialTypes.Bit(type))) return Invalid;
        await credentials.TouchCredential(credentialId);

        DbLoginSession? session = await sessions.GetLive(ctx.Hash);
        if (session == null) return Invalid;

        LoginPurpose purpose = (LoginPurpose)session.Purpose;
        int[] flows = await UsableFlows(user.Id);
        if (!LoginFlowRules.IsSatisfied(flows, session.CompletedMask)) {
            int next = LoginFlowRules.Attemptable(flows, session.CompletedMask);
            if (next == 0) {
                await sessions.Delete(ctx.Hash);
                return Invalid;
            }
            return new LoginStepResult(LoginStepOutcome.Continue, ctx.Handle, next, Purpose: purpose);
        }

        if (!await sessions.TryConsume(ctx.Hash)) return Invalid;

        string device = tokens.GenerateDeviceToken(user.Id);
        if (purpose == LoginPurpose.Reauth) {
            return new LoginStepResult(LoginStepOutcome.Complete, Token: tokens.GenerateReauthToken(user.Id),
                Purpose: purpose, DeviceToken: device);
        }

        await users.SetLastLogin(user.Id, DateTime.UtcNow);
        return new LoginStepResult(LoginStepOutcome.Complete, Token: tokens.GenerateLoginToken(user.Id), Purpose: purpose,
            DeviceToken: device);
    }

    private static LoginStepResult Wrong(SessionContext ctx) => ctx.User == null
        ? new LoginStepResult(LoginStepOutcome.WrongCredential)
        : new LoginStepResult(LoginStepOutcome.WrongCredential, ctx.Handle, ctx.Attemptable,
            Purpose: (LoginPurpose)ctx.Session.Purpose);

    private static LoginStepResult RateLimited(RateLimitDecision decision) =>
        new(LoginStepOutcome.RateLimited, RetryAfter: decision.RetryAfter);
}
