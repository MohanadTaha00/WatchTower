namespace WatchTower.Domain.Entities;

public class CheckResult
{
    public Guid Id { get; set; }
    public Guid MonitorId { get; set; }
    public bool IsUp { get; set; }
    public int StatusCode { get; set; }
    public int ResponseTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
    public CheckFailureReason? FailureReason { get; set; }
    public DateTime CheckedAt { get; set; }

    public UptimeMonitor Monitor { get; set; } = null!;
}
