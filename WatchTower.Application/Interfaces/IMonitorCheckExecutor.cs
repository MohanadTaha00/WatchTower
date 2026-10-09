using WatchTower.Domain.Entities;

namespace WatchTower.Application.Interfaces;

public interface IMonitorCheckExecutor
{
    Task<CheckResult> ExecuteAsync(UptimeMonitor monitor, CancellationToken cancellationToken = default);
}
