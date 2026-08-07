namespace SerbleAPI.Services.Impl;

/// <summary>
/// Drives the tax schedule. Runs on every replica; <see cref="TaxRunLock"/> and the unique cycle
/// claim make that safe. Each tick settles at most one cycle boundary, so a backlog drains over
/// successive ticks rather than in one oversized transaction.
/// </summary>
public class TaxBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<TaxBackgroundService> logger) : BackgroundService {

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(1));

        while (!stoppingToken.IsCancellationRequested) {
            try {
                using IServiceScope scope = scopeFactory.CreateScope();
                ITaxService taxService = scope.ServiceProvider.GetRequiredService<ITaxService>();
                await taxService.RunDueTaxCycles(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
            catch (Exception ex) {
                logger.LogError(ex, "Tax background service failed");
            }

            try {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
        }
    }
}
