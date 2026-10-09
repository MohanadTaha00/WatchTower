namespace WatchTower.Domain.Entities;

public enum NotificationKind
{
    IncidentOpened,
    IncidentResolved
}

public enum NotificationStatus
{
    Pending,
    Sent,
    Failed
}

/// <summary>
/// An email waiting to be sent (outbox). It is saved in the same transaction as the incident
/// change that caused it, then delivered and retried by the notification dispatcher.
/// </summary>
public class EmailNotification
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public NotificationKind Kind { get; set; }
    public required string ToAddress { get; set; }
    public required string Subject { get; set; }
    public required string TextBody { get; set; }
    public required string HtmlBody { get; set; }
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }

    public Incident Incident { get; set; } = null!;
}
