using WatchTower.Domain.Entities;

namespace WatchTower.Application.DTOs;

public record MonitorRequest
{
    public required string Name { get; init; }
    public MonitorType Type { get; init; } = MonitorType.Website;

    public string? Url { get; init; }
    public int? ExpectedStatusCode { get; init; }

    public string? HttpMethod { get; init; }
    public Dictionary<string, string>? RequestHeaders { get; init; }
    public string? RequestBody { get; init; }
    public string? ExpectedBodyContains { get; init; }

    public string? IpAddress { get; init; }
    public int? Port { get; init; }

    public int IntervalSeconds { get; init; }
    public int TimeoutSeconds { get; init; }
    public bool IsEnabled { get; init; } = true;
}

public record LastCheckDto(
    DateTime CheckedAt,
    bool IsUp,
    int StatusCode,
    int ResponseTimeMs);

public record MonitorDto(
    Guid Id,
    string Name,
    MonitorType Type,
    string? Url,
    int? ExpectedStatusCode,
    string? HttpMethod,
    IReadOnlyDictionary<string, string>? RequestHeaders,
    string? RequestBody,
    string? ExpectedBodyContains,
    string? IpAddress,
    int? Port,
    int IntervalSeconds,
    int TimeoutSeconds,
    bool IsEnabled,
    LastCheckDto? LastCheck);

public record MonitorListResponse(IReadOnlyList<MonitorDto> Items);

public record CheckResultDto(
    Guid Id,
    bool IsUp,
    int StatusCode,
    int ResponseTimeMs,
    string? ErrorMessage,
    DateTime CheckedAt);

public record CheckResultListResponse(
    IReadOnlyList<CheckResultDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public record UptimeWindowDto(
    string Window,
    int Checks,
    int UpChecks,
    double? UptimePercent);

/// <summary>Response times of successful checks only; failed checks usually just report the timeout.</summary>
public record ResponseTimeSummaryDto(
    int? AverageMs,
    int? MinMs,
    int? MaxMs,
    int? P95Ms);

public record StatsBucketDto(
    DateTime Start,
    int Checks,
    int UpChecks,
    int? AverageResponseMs,
    int? MaxResponseMs);

public record MonitorStatsDto(
    string Range,
    DateTime From,
    DateTime To,
    int BucketSeconds,
    IReadOnlyList<UptimeWindowDto> Uptime,
    ResponseTimeSummaryDto ResponseTime,
    IReadOnlyList<StatsBucketDto> Buckets);

public record IncidentDto(
    Guid Id,
    Guid MonitorId,
    string MonitorName,
    MonitorType MonitorType,
    DateTime StartedAt,
    DateTime? ResolvedAt,
    double DurationSeconds,
    string Summary,
    bool IsOpen);

public record IncidentListResponse(
    IReadOnlyList<IncidentDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public record MonitorIncidentSummaryDto(
    Guid MonitorId,
    string MonitorName,
    MonitorType MonitorType,
    int Incidents,
    int OpenIncidents,
    double DowntimeSeconds,
    DateTime LastIncidentAt);

public record IncidentSummaryDto(
    int? Days,
    int TotalIncidents,
    int OpenIncidents,
    int ResolvedIncidents,
    double? AverageResolutionSeconds,
    double TotalDowntimeSeconds,
    IReadOnlyList<MonitorIncidentSummaryDto> ByMonitor);
