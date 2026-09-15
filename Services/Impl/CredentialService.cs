using Microsoft.Extensions.Caching.Memory;
using SerbleAPI.API.v1.Account;
using SerbleAPI.Authentication;
using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services.Auth;

namespace SerbleAPI.Services.Impl;

public class CredentialService(
    ILogger<CredentialService> logger,
    IPasswordHasher hasher,
    IRateLimitService rateLimit,
    IUserRepository users,
    ICredentialRepository credentials,
    IPasskeyRepository passkeys,
    IMemoryCache cache) : ICredentialService {

    public static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(15);

    private static readonly int PasswordBit = CredentialTypes.Bit(CredentialType.Password);
    private static readonly int TotpBit = CredentialTypes.Bit(CredentialType.Totp);
    private static readonly int PasskeyBit = CredentialTypes.Bit(CredentialType.Passkey);

    public async Task<CredentialOverview> GetOverview(string userId, bool includeSchemes = false) {
        UserCredential[] active = (await credentials.GetCredentials(userId))
            .Where(c => c.Status == CredentialStatus.Active)
            .ToArray();
        Dictionary<string, SavedPasskey> keys = (await passkeys.GetUsersPasskeys(userId))
            .ToDictionary(k => k.UserCredentialId);
        LoginFlow[] flows = await credentials.GetFlows(userId);

        return new CredentialOverview {
            Credentials = active.Select(c => new CredentialView(
                c.Id,
                CredentialTypes.ToName(c.Type),
                c.Name,
                c.CreatedAt,
                c.LastUsedAt,
                keys.TryGetValue(c.Id, out SavedPasskey? key) ? new PasskeyDetails(key.IsBackupEligible, key.IsBackedUp) : null,
                includeSchemes && c.Type == CredentialType.Password ? ((PasswordScheme)c.Scheme).ToString() : null
            )).ToArray(),
            Flows = flows.Select(f => new LoginFlowView(f.Id, CredentialTypes.Names(f.Mask))).ToArray()
        };
    }

    public async Task<CredentialChangeResult> SetPassword(string userId, string password) {
        string hash = await hasher.Hash(password);
        bool replaced = await credentials.WithUserLock(userId, () => credentials.SetPassword(userId, hash));
        return CredentialChangeResult.Ok(replaced ? await RevokeSessions(userId) : null);
    }

    public async Task<CredentialChangeResult> AdminSetPassword(string userId, string password) {
        string hash = await hasher.Hash(password);
        await credentials.WithUserLock(userId, async () => {
            await credentials.SetPassword(userId, hash);
            int[] flows = await credentials.GetFlowMasks(userId);
            if (!flows.Any(f => (f & PasswordBit) != 0)) {
                await credentials.ReplaceFlows(userId, flows.Append(PasswordBit));
            }
            return true;
        });
        return CredentialChangeResult.Ok(await RevokeSessions(userId));
    }

    public async Task<TotpEnrolment> BeginTotp(User user, string? name) {
        await credentials.DeleteStalePending(user.Id, CredentialType.Totp, DateTime.UtcNow - PendingLifetime);

        UserCredential credential = await credentials.AddCredential(new UserCredential {
            UserId = user.Id,
            Type   = CredentialType.Totp,
            Name   = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            Status = CredentialStatus.Pending,
            Secret = TotpCodes.NewBase32Secret(),
            Scheme = (int)TotpScheme.Base32Secret
        });

        string uri = TotpCodes.OtpAuthUri(TotpCodes.KeyBytes(credential), user.Username);
        return new TotpEnrolment(credential.Id, credential.Secret!, uri, TotpCodes.QrPng(uri));
    }

    public async Task<CredentialChangeResult> VerifyTotp(string userId, string credentialId, string code) {
        RateLimitDecision limit = rateLimit.Check(RateLimitTiers.Auth, RateLimitScope.Identity, "totp:" + userId);
        if (!limit.Allowed) return new CredentialChangeResult(CredentialChangeStatus.RateLimited, RetryAfter: limit.RetryAfter);

        UserCredential? credential = await credentials.GetCredential(userId, credentialId);
        if (credential is not { Type: CredentialType.Totp, Status: CredentialStatus.Pending }
            || credential.CreatedAt < DateTime.UtcNow - PendingLifetime) {
            return new CredentialChangeResult(CredentialChangeStatus.NotFound);
        }

        if (TotpCodes.MatchStep(credential, code) is not { } step) {
            return new CredentialChangeResult(CredentialChangeStatus.Invalid, "Invalid code");
        }

        return await credentials.ActivatePending(credential.Id, step)
            ? CredentialChangeResult.Ok()
            : new CredentialChangeResult(CredentialChangeStatus.NotFound);
    }

    public async Task<CredentialChangeResult> Rename(string userId, string credentialId, string name) {
        name = name.Trim();
        if (name.Length is 0 or > 255) {
            return new CredentialChangeResult(CredentialChangeStatus.Invalid, "Name must be 1 to 255 characters");
        }
        if (await credentials.GetCredential(userId, credentialId) == null) {
            return new CredentialChangeResult(CredentialChangeStatus.NotFound);
        }
        await credentials.RenameCredential(credentialId, name);
        return CredentialChangeResult.Ok();
    }

    public async Task<CredentialChangeResult> Delete(string userId, string credentialId, bool asAdmin = false) {
        (CredentialChangeResult result, bool revoke) = await credentials.WithUserLock(userId, async () => {
            UserCredential? target = await credentials.GetCredential(userId, credentialId);
            if (target == null) return (new CredentialChangeResult(CredentialChangeStatus.NotFound), false);

            if (target.Status == CredentialStatus.Pending) {
                await credentials.DeleteCredential(target.Id);
                return (CredentialChangeResult.Ok(), false);
            }

            UserCredential[] all = await credentials.GetCredentials(userId);
            int activeBefore = ActiveMask(all);
            int activeAfter = ActiveMask(all.Where(c => c.Id != target.Id));
            int[] flows = await credentials.GetFlowMasks(userId);
            int[] kept = LoginFlowRules.PruneUnsatisfiable(flows, activeAfter);

            if (!asAdmin && kept.Length == 0 && LoginFlowRules.PruneUnsatisfiable(flows, activeBefore).Length > 0) {
                return (new CredentialChangeResult(CredentialChangeStatus.Conflict,
                    "Removing this would leave no way to sign in"), false);
            }

            await credentials.DeleteCredential(target.Id);
            if (kept.Length != flows.Length) await credentials.ReplaceFlows(userId, kept);
            return (CredentialChangeResult.Ok(), true);
        });

        return revoke ? result with { RevokedAt = await RevokeSessions(userId) } : result;
    }

    public async Task<CredentialChangeResult> SetFlows(string userId, IReadOnlyList<int> masks) {
        (CredentialChangeResult result, bool revoke) = await credentials.WithUserLock(userId, async () => {
            string? error = LoginFlowRules.Validate(masks, await credentials.GetActiveTypeMask(userId));
            if (error != null) return (new CredentialChangeResult(CredentialChangeStatus.Invalid, error), false);

            int[] existing = await credentials.GetFlowMasks(userId);
            if (existing.Order().SequenceEqual(masks.Order())) return (CredentialChangeResult.Ok(), false);

            await credentials.ReplaceFlows(userId, masks);
            return (CredentialChangeResult.Ok(), true);
        });

        return revoke ? result with { RevokedAt = await RevokeSessions(userId) } : result;
    }

    public Task AllowPasskeyAlone(string userId) =>
        credentials.WithUserLock(userId, async () => {
            int[] flows = await credentials.GetFlowMasks(userId);
            if (flows.Contains(PasskeyBit)) return false;

            int[] updated = [..flows, PasskeyBit];
            if (LoginFlowRules.Validate(updated, await credentials.GetActiveTypeMask(userId)) != null) return false;

            await credentials.ReplaceFlows(userId, updated);
            return true;
        });

    public async Task<CredentialChangeResult> AdminRemoveTotp(string userId) {
        await credentials.WithUserLock(userId, async () => {
            await credentials.DeleteCredentials(userId, CredentialType.Totp);
            int active = await credentials.GetActiveTypeMask(userId);
            int[] flows = await credentials.GetFlowMasks(userId);

            int[] updated = LoginFlowRules.PruneUnsatisfiable(flows.Select(f => f & ~TotpBit), active)
                .Distinct()
                .ToArray();
            if (updated.Length == 0 && (active & PasswordBit) != 0) updated = [PasswordBit];

            await credentials.ReplaceFlows(userId, updated);
            return true;
        });
        return CredentialChangeResult.Ok(await RevokeSessions(userId));
    }

    private static int ActiveMask(IEnumerable<UserCredential> all) =>
        CredentialTypes.ToMask(all.Where(c => c.Status == CredentialStatus.Active).Select(c => c.Type));

    private async Task<DateTime> RevokeSessions(string userId) {
        DateTime cutoff = SessionsController.RevocationCutoff();
        await users.RevokeTokensIssuedBefore(userId, cutoff);
        cache.Remove(SerbleAuthenticationHandler.AccountStateCachePrefix + userId);
        logger.LogInformation("Sign-in credentials changed for {UserId}; tokens issued before {Cutoff:o} revoked",
            userId, cutoff);
        return cutoff;
    }
}
