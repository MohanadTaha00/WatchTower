using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WatchTower.Application.Interfaces;
using WatchTower.Application.Options;
using WatchTower.Domain.Entities;
using WatchTower.Infrastructure.Persistence;

namespace WatchTower.Infrastructure.Services;

public class EmailNotificationProcessor(
    AppDbContext db,
    IEmailSender sender,
    IOptions<NotificationOptions> options,
    ILogger<EmailNotificationProcessor> logger)
{
    private const int BatchSize = 20;
    private const int MaxErrorLength = 1000;
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(30);

    /// <summary>Sends notifications that are due. Returns how many were attempted.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var due = await db.EmailNotifications
            .Where(n => n.Status == NotificationStatus.Pending && n.NextAttemptAt <= now)
            .OrderBy(n => n.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var notification in due)
        {
            notification.Attempts++;
            try
            {
                await sender.SendAsync(
                    new EmailMessage(notification.ToAddress, notification.Subject, notification.TextBody, notification.HtmlBody),
                    cancellationToken);

                notification.Status = NotificationStatus.Sent;
                notification.SentAt = DateTime.UtcNow;
                notification.LastError = null;
                logger.LogInformation("Sent {Kind} email for incident {IncidentId} to {To}",
                    notification.Kind, notification.IncidentId, notification.ToAddress);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                notification.LastError = ex.Message.Length <= MaxErrorLength ? ex.Message : ex.Message[..MaxErrorLength];

                if (notification.Attempts >= options.Value.MaxAttempts)
                {
                    notification.Status = NotificationStatus.Failed;
                    logger.LogError(ex, "Giving up on email {NotificationId} after {Attempts} attempts",
                        notification.Id, notification.Attempts);
                }
                else
                {
                    notification.NextAttemptAt = DateTime.UtcNow + RetryDelay(notification.Attempts);
                    logger.LogWarning(ex, "Email {NotificationId} failed (attempt {Attempts}); retrying at {NextAttemptAt}",
                        notification.Id, notification.Attempts, notification.NextAttemptAt);
                }
            }

            // Save after each email so a crash never causes an already-sent email to be sent again.
            await db.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }

    public static TimeSpan RetryDelay(int attempts)
    {
        var delay = BaseRetryDelay * Math.Pow(2, Math.Max(0, attempts - 1));
        return delay < MaxRetryDelay ? delay : MaxRetryDelay;
    }
}
