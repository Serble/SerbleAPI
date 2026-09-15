using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Services.Impl;

/// <summary>
/// Wraps every legacy SHA-256 password hash in Argon2id, then clears the copy in <c>Users.Password</c>.
/// Runs until nothing is left to wrap; one replica at a time.
/// </summary>
public class PasswordHashUpgradeService(
    IServiceScopeFactory scopeFactory,
    ILogger<PasswordHashUpgradeService> logger) : BackgroundService {

    private const string LockName = "serble.auth.password-upgrade";
    private const int BatchSize = 100;

    private static readonly int PasswordType = (int)CredentialType.Password;
    private static readonly int LegacyScheme = (int)PasswordScheme.LegacySha256;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                using IServiceScope scope = scopeFactory.CreateScope();
                if (await RunOnce(scope.ServiceProvider, stoppingToken)) return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                return;
            }
            catch (Exception ex) {
                logger.LogError(ex, "Password hash upgrade failed");
            }

            try {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) {
                return;
            }
        }
    }

    /// <summary>True when the work is finished; false if another replica holds the lock.</summary>
    private async Task<bool> RunOnce(IServiceProvider services, CancellationToken cancellationToken) {
        SerbleDbContext db = services.GetRequiredService<SerbleDbContext>();
        IPasswordHasher hasher = services.GetRequiredService<IPasswordHasher>();

        await using DbAdvisoryLock runLock = await DbAdvisoryLock.TryAcquire(db, LockName, cancellationToken);
        if (!runLock.Acquired) return false;

        int pending = await db.UserCredentials.CountAsync(c => c.Type == PasswordType && c.Scheme == LegacyScheme, cancellationToken);
        if (pending > 0) logger.LogInformation("Wrapping {Count} legacy password hashes in Argon2id", pending);

        string lastId = "";
        int wrapped = 0;
        int failed = 0;
        while (true) {
            var batch = await db.UserCredentials.AsNoTracking()
                .Where(c => c.Type == PasswordType && c.Scheme == LegacyScheme && string.Compare(c.Id, lastId) > 0)
                .OrderBy(c => c.Id)
                .Take(BatchSize)
                .Select(c => new { c.Id, c.UserId, c.Secret })
                .ToListAsync(cancellationToken);
            if (batch.Count == 0) break;

            foreach (var row in batch) {
                lastId = row.Id;
                if (string.IsNullOrEmpty(row.Secret)) continue;

                string hash = await hasher.Hash(row.Secret, cancellationToken);
                if (!await hasher.VerifyHash(hash, row.Secret, cancellationToken)) {
                    failed++;
                    logger.LogError("Wrapped hash for credential {CredentialId} did not verify; left unchanged", row.Id);
                    continue;
                }

                await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                int updated = await db.UserCredentials
                    .Where(c => c.Id == row.Id && c.Scheme == LegacyScheme && c.Secret == row.Secret)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.Secret, hash)
                        .SetProperty(c => c.Scheme, (int)PasswordScheme.Argon2idOverSha256), cancellationToken);
                if (updated == 1) {
                    await db.Users
                        .Where(u => u.Id == row.UserId && u.Password == row.Secret)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(u => u.Password, (string?)null)
                            .SetProperty(u => u.PasswordSalt, (string?)null), cancellationToken);
                    wrapped++;
                }
                await transaction.CommitAsync(cancellationToken);
            }

            logger.LogInformation("Wrapped {Wrapped} of {Pending} legacy password hashes", wrapped, pending);
        }

        // Accounts whose password was upgraded or replaced some other way.
        await db.Users
            .Where(u => u.Password != null && db.UserCredentials.Any(c =>
                c.UserId == u.Id && c.Type == PasswordType && c.Scheme == (int)PasswordScheme.Argon2id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Password, (string?)null)
                .SetProperty(u => u.PasswordSalt, (string?)null), cancellationToken);

        int legacyLeft = await db.UserCredentials.CountAsync(c => c.Type == PasswordType && c.Scheme == LegacyScheme, cancellationToken);
        int copiesLeft = await db.Users.CountAsync(u => u.Password != null, cancellationToken);
        if (legacyLeft > 0 || copiesLeft > 0) {
            logger.LogWarning("Password hash upgrade finished with {Legacy} unwrapped credentials ({Failed} failed) " +
                              "and {Copies} legacy hashes still in Users.Password", legacyLeft, failed, copiesLeft);
        }
        else {
            logger.LogInformation("No legacy password hashes remain");
        }
        return true;
    }
}
