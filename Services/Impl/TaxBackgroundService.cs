namespace SerbleAPI.Services.Impl;

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
