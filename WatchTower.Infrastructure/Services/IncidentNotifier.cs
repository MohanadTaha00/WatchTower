using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WatchTower.Application.Notifications;
using WatchTower.Application.Options;
using WatchTower.Domain.Entities;
using WatchTower.Infrastructure.Persistence;

namespace WatchTower.Infrastructure.Services;

/// <summary>
/// Queues incident emails on the current DbContext, so they are saved atomically with the incident change.
/// </summary>
public class IncidentNotifier(
    AppDbContext db,
    IOptions<NotificationOptions> notificationOptions,
    IOptions<SmtpOptions> smtpOptions)
{
    private readonly NotificationOptions _options = notificationOptions.Value;

    public bool IsEnabled => _options.Enabled && !string.IsNullOrWhiteSpace(smtpOptions.Value.Host);

    public async Task QueueAsync(
        NotificationKind kind,
        UptimeMonitor monitor,
        Incident incident,
        CheckResult check,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
            return;

        var email = await db.Users
            .Where(u => u.Id == monitor.UserId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);
        if (email is null)
            return;

        var content = kind == NotificationKind.IncidentOpened
            ? IncidentEmailComposer.ComposeOpened(monitor, incident, check, _options.DashboardUrl)
            : IncidentEmailComposer.ComposeResolved(monitor, incident, check, _options.DashboardUrl);

        var now = DateTime.UtcNow;
        db.EmailNotifications.Add(new EmailNotification
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            Kind = kind,
            ToAddress = email,
            Subject = content.Subject,
            TextBody = content.TextBody,
            HtmlBody = content.HtmlBody,
            CreatedAt = now,
            NextAttemptAt = now
        });
    }
}
