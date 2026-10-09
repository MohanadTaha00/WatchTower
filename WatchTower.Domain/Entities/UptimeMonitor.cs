namespace WatchTower.Domain.Entities;

public class UptimeMonitor
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public MonitorType Type { get; set; } = MonitorType.Website;

    // Website and Api monitors
    public string? Url { get; set; }
    public int? ExpectedStatusCode { get; set; }

    // Api monitors
    public string? HttpMethod { get; set; }
    public string? RequestHeadersJson { get; set; }
    public string? RequestBody { get; set; }
    public string? ExpectedBodyContains { get; set; }

    // IpAddress monitors: ping when Port is null, TCP connect otherwise
    public string? IpAddress { get; set; }
    public int? Port { get; set; }

    public int IntervalSeconds { get; set; }
    public int TimeoutSeconds { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public DateTime? LastCheckAt { get; set; }
    public bool? LastIsUp { get; set; }
    public int? LastStatusCode { get; set; }
    public int? LastResponseTimeMs { get; set; }

    public User User { get; set; } = null!;
    public ICollection<CheckResult> CheckResults { get; set; } = [];
    public ICollection<Incident> Incidents { get; set; } = [];
}
