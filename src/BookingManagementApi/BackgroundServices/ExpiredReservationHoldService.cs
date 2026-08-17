using BookingManagementApi.Configuration;
using BookingManagementApi.Services.Reservations;
using Microsoft.Extensions.Options;

namespace BookingManagementApi.BackgroundServices;

public sealed class ExpiredReservationHoldService(
    IServiceScopeFactory scopeFactory,
    IOptions<SchedulingOptions> options,
    TimeProvider timeProvider,
    ILogger<ExpiredReservationHoldService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.Value.HoldCleanupIntervalSeconds);
        using var timer = new PeriodicTimer(interval, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<ExpiredReservationHoldProcessor>();
                    var expiredCount = await processor.ProcessBatchAsync(stoppingToken);
                    logger.LogInformation(
                        "Expired reservation hold cleanup completed. {ExpiredCount} holds expired from a batch of up to {BatchSize}.",
                        expiredCount, ExpiredReservationHoldProcessor.BatchSize);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Expired reservation hold cleanup failed; the next scheduled cycle will retry.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }
}
