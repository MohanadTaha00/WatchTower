using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WatchTower.Application.Exceptions;
using WatchTower.Application.Interfaces;
using WatchTower.Application.Options;
using WatchTower.Infrastructure.Persistence;

namespace WatchTower.Infrastructure.Services;

public class NotificationService(
    AppDbContext db,
    IEmailSender sender,
    IOptions<NotificationOptions> notificationOptions,
    IOptions<SmtpOptions> smtpOptions) : INotificationService
{
    public async Task<NotificationSettingsDto> GetSettingsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var smtp = smtpOptions.Value;
        return new NotificationSettingsDto(
            notificationOptions.Value.Enabled && !string.IsNullOrWhiteSpace(smtp.Host),
            await GetEmailAsync(userId, cancellationToken),
            smtp.Host,
            smtp.FromAddress);
    }

    public async Task<TestEmailResponse> SendTestEmailAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var smtp = smtpOptions.Value;
        if (!notificationOptions.Value.Enabled || string.IsNullOrWhiteSpace(smtp.Host))
            throw new ValidationException("Email notifications are turned off. Set Smtp:Host and Notifications:Enabled.");

        var email = await GetEmailAsync(userId, cancellationToken);
        var dashboardUrl = notificationOptions.Value.DashboardUrl;
        var text =
            "This is a test email from WatchTower.\n\n" +
            $"If you can read this, incident alerts will reach {email}.\n" +
            $"Sent through {smtp.Host}:{smtp.Port} at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC.\n\n" +
            $"Dashboard: {dashboardUrl}";
        var html =
            "<div style=\"font-family:Segoe UI,Roboto,Arial,sans-serif;font-size:14px;color:#1c2430;\">" +
            "<h2 style=\"color:#1f9d55;\">WatchTower email alerts are working</h2>" +
            $"<p>If you can read this, incident alerts will reach <strong>{WebUtility.HtmlEncode(email)}</strong>.</p>" +
            $"<p style=\"color:#6b7686;\">Sent through {WebUtility.HtmlEncode(smtp.Host)}:{smtp.Port} at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC.</p>" +
            $"<p><a href=\"{WebUtility.HtmlEncode(dashboardUrl)}\">Open the dashboard</a></p></div>";

        try
        {
            await sender.SendAsync(new EmailMessage(email, "[WatchTower] Test email", text, html), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new EmailDeliveryException($"Could not send through {smtp.Host}:{smtp.Port}: {ex.Message}", ex);
        }

        return new TestEmailResponse(email);
    }

    private async Task<string> GetEmailAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("User not found.");
}
