namespace WatchTower.Application.Interfaces;

public record NotificationSettingsDto(bool Enabled, string Recipient, string SmtpHost, string FromAddress);

public record TestEmailResponse(string SentTo);

public interface INotificationService
{
    Task<NotificationSettingsDto> GetSettingsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Sends a test email to the user right away, bypassing the queue, and throws if delivery fails.</summary>
    Task<TestEmailResponse> SendTestEmailAsync(Guid userId, CancellationToken cancellationToken = default);
}
