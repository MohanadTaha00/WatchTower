namespace WatchTower.Domain.Entities;

public class Incident
{
    public Guid Id { get; set; }
    public Guid MonitorId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public required string Summary { get; set; }

    public UptimeMonitor Monitor { get; set; } = null!;
    public ICollection<EmailNotification> Notifications { get; set; } = [];

    public bool IsOpen => ResolvedAt is null;
}
