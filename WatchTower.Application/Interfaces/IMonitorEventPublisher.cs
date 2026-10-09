using WatchTower.Domain.Entities;

namespace WatchTower.Application.Interfaces;

public interface IMonitorEventPublisher
{
    Task PublishCheckCompletedAsync(CheckResult result, CancellationToken cancellationToken = default);
    Task PublishIncidentOpenedAsync(Incident incident, CancellationToken cancellationToken = default);
    Task PublishIncidentClosedAsync(Incident incident, CancellationToken cancellationToken = default);
}
