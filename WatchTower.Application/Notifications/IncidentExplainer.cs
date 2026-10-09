using System.Net;
using System.Text.RegularExpressions;
using WatchTower.Domain.Entities;

namespace WatchTower.Application.Notifications;

/// <summary>Turns a failed check into a plain-language explanation for the incident email.</summary>
public static partial class IncidentExplainer
{
    public static string Describe(UptimeMonitor monitor) => monitor.Type switch
    {
        MonitorType.IpAddress when monitor.Port is not null => $"{monitor.IpAddress}:{monitor.Port} (TCP)",
        MonitorType.IpAddress => $"{monitor.IpAddress} (ping)",
        MonitorType.Api => $"{monitor.HttpMethod ?? "GET"} {monitor.Url}",
        _ => monitor.Url ?? ""
    };

    public static string Explain(UptimeMonitor monitor, CheckResult check)
    {
        var target = monitor.Type == MonitorType.IpAddress ? monitor.IpAddress : monitor.Url;
        var host = HostOf(monitor);

        return check.FailureReason switch
        {
            CheckFailureReason.Timeout =>
                $"{target} did not respond within {monitor.TimeoutSeconds} seconds. " +
                "The server may be overloaded, stuck, or unreachable over the network.",

            CheckFailureReason.UnexpectedStatus =>
                $"{target} responded with HTTP {check.StatusCode} ({StatusName(check.StatusCode)}) " +
                $"instead of the expected {monitor.ExpectedStatusCode ?? 200}. {StatusHint(check.StatusCode)}",

            CheckFailureReason.MissingKeyword =>
                $"{target} returned HTTP {check.StatusCode} as expected, but the response did not contain " +
                $"\"{monitor.ExpectedBodyContains}\". The service may be returning an error page, an empty result, or degraded data.",

            CheckFailureReason.DnsFailure =>
                $"The domain name {host} could not be resolved. " +
                "Its DNS records may be misconfigured, or the domain may have expired.",

            CheckFailureReason.ConnectionFailed when monitor.Type == MonitorType.IpAddress =>
                $"Nothing accepted a connection on port {monitor.Port} at {monitor.IpAddress}. " +
                "The service on that port may be stopped, or a firewall may be blocking it.",

            CheckFailureReason.ConnectionFailed =>
                $"WatchTower could not open a connection to {host}. " +
                "The server may be down, or a firewall may be blocking it.",

            CheckFailureReason.TlsFailure =>
                $"A secure (HTTPS) connection to {host} could not be established. " +
                "The SSL/TLS certificate may be expired, invalid, or misconfigured.",

            CheckFailureReason.NoPingReply =>
                $"{monitor.IpAddress} did not answer a ping within {monitor.TimeoutSeconds} seconds. " +
                "The server may be offline, or the network path to it may be down.",

            _ => $"WatchTower could not reach {target}." +
                 (string.IsNullOrWhiteSpace(check.ErrorMessage) ? "" : $" The error was: {check.ErrorMessage}")
        };
    }

    private static string StatusHint(int statusCode) => statusCode switch
    {
        502 => "A proxy or load balancer could not get a valid response from the application behind it.",
        503 => "The service says it is unavailable, which usually means maintenance or overload.",
        504 => "A proxy or load balancer timed out waiting for the application behind it.",
        >= 500 => "This is a server-side error: the application may have crashed, be overloaded, or be failing to reach one of its own dependencies, such as a database.",
        401 or 403 => "The request was rejected as unauthorized. An API key or credentials may be missing, wrong, or expired.",
        404 => "The page or endpoint was not found. It may have been moved, renamed, or deleted.",
        429 => "The server is rate limiting requests.",
        >= 400 => "The server rejected the request as invalid.",
        >= 300 => "The URL now redirects somewhere else. If the move is intentional, update the monitor to the new URL.",
        _ => ""
    };

    private static string StatusName(int statusCode) =>
        Enum.IsDefined(typeof(HttpStatusCode), statusCode)
            ? PascalCaseBoundary().Replace(((HttpStatusCode)statusCode).ToString(), " $1")
            : "unknown status";

    private static string? HostOf(UptimeMonitor monitor) =>
        monitor.Type == MonitorType.IpAddress
            ? monitor.IpAddress
            : Uri.TryCreate(monitor.Url, UriKind.Absolute, out var uri) ? uri.Host : monitor.Url;

    [GeneratedRegex("(?<=[a-z])([A-Z])")]
    private static partial Regex PascalCaseBoundary();
}
