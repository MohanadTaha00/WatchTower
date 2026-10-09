using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WatchTower.Application.Interfaces;
using WatchTower.Application.Options;
using WatchTower.Domain.Entities;
using WatchTower.Infrastructure.Persistence;

namespace WatchTower.Infrastructure.Services;

public class MonitorSchedulerService(
    IServiceScopeFactory scopeFactory,
    IOptions<SchedulerOptions> schedulerOptions,
    ILogger<MonitorSchedulerService> logger) : BackgroundService
{
    private readonly SchedulerOptions _options = schedulerOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Monitor scheduler started.");

        var semaphore = new SemaphoreSlim(_options.MaxConcurrentChecks);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDueChecksAsync(semaphore, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Monitor scheduler iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
        }
    }

    private async Task RunDueChecksAsync(SemaphoreSlim semaphore, CancellationToken stoppingToken)
    {
        List<Guid> dueMonitorIds;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;

            dueMonitorIds = await db.Monitors
                .AsNoTracking()
                .Where(m => m.IsEnabled)
                .Where(m => m.LastCheckAt == null || m.LastCheckAt.Value.AddSeconds(m.IntervalSeconds) <= now)
                .Select(m => m.Id)
                .ToListAsync(stoppingToken);
        }

        var tasks = dueMonitorIds.Select(id => RunSingleCheckAsync(id, semaphore, stoppingToken));
        await Task.WhenAll(tasks);
    }

    private async Task RunSingleCheckAsync(Guid monitorId, SemaphoreSlim semaphore, CancellationToken stoppingToken)
    {
        await semaphore.WaitAsync(stoppingToken);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var executor = scope.ServiceProvider.GetRequiredService<IMonitorCheckExecutor>();
            var publisher = scope.ServiceProvider.GetRequiredService<IMonitorEventPublisher>();
            var incidentTracker = scope.ServiceProvider.GetRequiredService<IncidentTracker>();

            var monitor = await db.Monitors.FirstOrDefaultAsync(m => m.Id == monitorId, stoppingToken);
            if (monitor is null || !monitor.IsEnabled)
                return;

            var checkResult = await executor.ExecuteAsync(monitor, stoppingToken);
            db.CheckResults.Add(checkResult);

            var previousIsUp = monitor.LastIsUp;
            monitor.LastCheckAt = checkResult.CheckedAt;
            monitor.LastIsUp = checkResult.IsUp;
            monitor.LastStatusCode = checkResult.StatusCode;
            monitor.LastResponseTimeMs = checkResult.ResponseTimeMs;
            monitor.UpdatedAt = DateTime.UtcNow;

            await incidentTracker.ApplyAsync(monitor, checkResult, previousIsUp, stoppingToken);

            await db.SaveChangesAsync(stoppingToken);
            await publisher.PublishCheckCompletedAsync(checkResult, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Check failed for monitor {MonitorId}", monitorId);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
