using Microsoft.Extensions.Logging;
using WatchTower.Application.Interfaces;
using WatchTower.Domain.Entities;

namespace WatchTower.Infrastructure.Services;

public class NoOpMonitorEventPublisher(ILogger<NoOpMonitorEventPublisher> logger) : IMonitorEventPublisher
{
    public Task PublishCheckCompletedAsync(CheckResult result, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Check completed for monitor {MonitorId}, isUp={IsUp}", result.MonitorId, result.IsUp);
        return Task.CompletedTask;
    }

    public Task PublishIncidentOpenedAsync(Incident incident, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Incident opened for monitor {MonitorId}", incident.MonitorId);
        return Task.CompletedTask;
    }

    public Task PublishIncidentClosedAsync(Incident incident, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Incident closed for monitor {MonitorId}", incident.MonitorId);
        return Task.CompletedTask;
    }
}
