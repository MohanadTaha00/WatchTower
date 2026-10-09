using System.Text.Json;
using WatchTower.Application.DTOs;
using WatchTower.Domain.Entities;

namespace WatchTower.Application.Mapping;

public static class MonitorMapping
{
    public static MonitorDto ToDto(UptimeMonitor monitor)
    {
        LastCheckDto? lastCheck = monitor.LastCheckAt is null
            ? null
            : new LastCheckDto(
                monitor.LastCheckAt.Value,
                monitor.LastIsUp ?? false,
                monitor.LastStatusCode ?? 0,
                monitor.LastResponseTimeMs ?? 0);

        return new MonitorDto(
            monitor.Id,
            monitor.Name,
            monitor.Type,
            monitor.Url,
            monitor.ExpectedStatusCode,
            monitor.HttpMethod,
            DeserializeHeaders(monitor.RequestHeadersJson),
            monitor.RequestBody,
            monitor.ExpectedBodyContains,
            monitor.IpAddress,
            monitor.Port,
            monitor.IntervalSeconds,
            monitor.TimeoutSeconds,
            monitor.IsEnabled,
            lastCheck);
    }

    public static void Apply(MonitorRequest request, UptimeMonitor monitor)
    {
        monitor.Name = request.Name;
        monitor.Type = request.Type;
        monitor.Url = request.Url;
        monitor.ExpectedStatusCode = request.ExpectedStatusCode;
        monitor.HttpMethod = request.HttpMethod;
        monitor.RequestHeadersJson = request.RequestHeaders is null ? null : JsonSerializer.Serialize(request.RequestHeaders);
        monitor.RequestBody = request.RequestBody;
        monitor.ExpectedBodyContains = request.ExpectedBodyContains;
        monitor.IpAddress = request.IpAddress;
        monitor.Port = request.Port;
        monitor.IntervalSeconds = request.IntervalSeconds;
        monitor.TimeoutSeconds = request.TimeoutSeconds;
        monitor.IsEnabled = request.IsEnabled;
    }

    public static Dictionary<string, string>? DeserializeHeaders(string? json) =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(json);

    public static CheckResultDto ToDto(CheckResult result) =>
        new(
            result.Id,
            result.IsUp,
            result.StatusCode,
            result.ResponseTimeMs,
            result.ErrorMessage,
            result.CheckedAt);

    public static IncidentDto ToDto(Incident incident, DateTime now) =>
        new(
            incident.Id,
            incident.MonitorId,
            incident.Monitor.Name,
            incident.Monitor.Type,
            incident.StartedAt,
            incident.ResolvedAt,
            ((incident.ResolvedAt ?? now) - incident.StartedAt).TotalSeconds,
            incident.Summary,
            incident.IsOpen);
}
