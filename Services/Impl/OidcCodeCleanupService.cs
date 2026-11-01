using SerbleAPI.Repositories;

namespace SerbleAPI.Services.Impl;

/// <summary>
/// Deletes expired authorization codes (browser-flow OIDC codes and cross-app codes). A code is
/// useless once expired, consumed or not, but nothing else ever removes the row. Runs on every
/// replica: the delete is idempotent, so there is nothing to coordinate.
/// </summary>
public class OidcCodeCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<OidcCodeCleanupService> logger) : BackgroundService {

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>
    /// How long past expiry a row is kept. Comfortably more than any clock drift between replicas,
    /// so a code one instance still honours is never deleted under it by another.
    /// </summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                using IServiceScope scope = scopeFactory.CreateScope();
                IOidcCodeRepository codes = scope.ServiceProvider.GetRequiredService<IOidcCodeRepository>();
                long cutoff = DateTimeOffset.UtcNow.Subtract(Grace).ToUnixTimeSeconds();
                int deleted = await codes.PurgeExpired(cutoff);
                if (deleted > 0) logger.LogInformation("Deleted {Count} expired authorization codes", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                return;
            }
            catch (Exception ex) {
                logger.LogError(ex, "Authorization code cleanup failed");
            }

            try {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) {
                return;
            }
        }
    }
}
