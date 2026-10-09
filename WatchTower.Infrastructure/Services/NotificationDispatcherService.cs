using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WatchTower.Application.Options;

namespace WatchTower.Infrastructure.Services;

/// <summary>Background worker that delivers queued incident emails and retries failures.</summary>
public class NotificationDispatcherService(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationOptions> notificationOptions,
    IOptions<SmtpOptions> smtpOptions,
    ILogger<NotificationDispatcherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!notificationOptions.Value.Enabled || string.IsNullOrWhiteSpace(smtpOptions.Value.Host))
        {
            logger.LogWarning("Email notifications are disabled (Notifications:Enabled is false or Smtp:Host is empty).");
            return;
        }

        logger.LogInformation("Notification dispatcher started, sending through {Host}:{Port}.",
            smtpOptions.Value.Host, smtpOptions.Value.Port);
        var interval = TimeSpan.FromSeconds(Math.Max(1, notificationOptions.Value.DispatchIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<EmailNotificationProcessor>();
                await processor.ProcessDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Notification dispatch iteration failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
