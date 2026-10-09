using Microsoft.EntityFrameworkCore;
using WatchTower.Application.Interfaces;
using WatchTower.Domain.Entities;
using WatchTower.Infrastructure.Persistence;

namespace WatchTower.Infrastructure.Services;

public class IncidentTracker(AppDbContext db, IMonitorEventPublisher publisher, IncidentNotifier notifier)
{
    private const int MaxSummaryLength = 500;

    public async Task ApplyAsync(
        UptimeMonitor monitor,
        CheckResult checkResult,
        bool? previousIsUp,
        CancellationToken cancellationToken = default)
    {
        if (checkResult.IsUp)
        {
            if (previousIsUp != false)
                return;

            var openIncident = await db.Incidents
                .Where(i => i.MonitorId == monitor.Id && i.ResolvedAt == null)
                .OrderByDescending(i => i.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (openIncident is null)
                return;

            openIncident.ResolvedAt = checkResult.CheckedAt;
            await notifier.QueueAsync(NotificationKind.IncidentResolved, monitor, openIncident, checkResult, cancellationToken);
            await publisher.PublishIncidentClosedAsync(openIncident, cancellationToken);
            return;
        }

        if (previousIsUp == false)
            return;

        var summary = string.IsNullOrWhiteSpace(checkResult.ErrorMessage)
            ? $"Monitor reported down (HTTP {checkResult.StatusCode})."
            : checkResult.ErrorMessage;

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            MonitorId = monitor.Id,
            StartedAt = checkResult.CheckedAt,
            Summary = summary.Length <= MaxSummaryLength ? summary : summary[..MaxSummaryLength]
        };
        db.Incidents.Add(incident);
        await notifier.QueueAsync(NotificationKind.IncidentOpened, monitor, incident, checkResult, cancellationToken);
        await publisher.PublishIncidentOpenedAsync(incident, cancellationToken);
    }
}
