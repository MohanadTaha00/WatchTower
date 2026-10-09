using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using WatchTower.Application.Interfaces;
using WatchTower.Application.Mapping;
using WatchTower.Domain.Entities;

namespace WatchTower.Infrastructure.Services;

public class MonitorCheckExecutor(IHttpClientFactory httpClientFactory) : IMonitorCheckExecutor
{
    public const string ClientName = "MonitorCheck";
    private const int MaxErrorLength = 500;
    private const int MaxBodyBytes = 1024 * 1024;

    private sealed record Outcome(
        bool IsUp,
        int StatusCode,
        string? Error = null,
        CheckFailureReason? Reason = null,
        int? ResponseTimeMs = null);

    public async Task<CheckResult> ExecuteAsync(UptimeMonitor monitor, CancellationToken cancellationToken = default)
    {
        var result = new CheckResult
        {
            Id = Guid.NewGuid(),
            MonitorId = monitor.Id,
            CheckedAt = DateTime.UtcNow
        };

        var sw = Stopwatch.StartNew();
        Outcome outcome;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(monitor.TimeoutSeconds));

            outcome = monitor.Type switch
            {
                MonitorType.IpAddress when monitor.Port is not null => await CheckTcpAsync(monitor, timeoutCts.Token),
                MonitorType.IpAddress => await CheckPingAsync(monitor),
                _ => await CheckHttpAsync(monitor, sw, timeoutCts.Token)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            outcome = new Outcome(false, 0, $"Request timed out after {monitor.TimeoutSeconds}s.", CheckFailureReason.Timeout);
        }
        catch (HttpRequestException ex)
        {
            var reason = ex.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => CheckFailureReason.DnsFailure,
                HttpRequestError.ConnectionError => CheckFailureReason.ConnectionFailed,
                HttpRequestError.SecureConnectionError => CheckFailureReason.TlsFailure,
                _ => CheckFailureReason.Other
            };
            outcome = new Outcome(false, 0, ex.Message, reason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            outcome = new Outcome(false, 0, ex.Message, CheckFailureReason.Other);
        }

        sw.Stop();
        result.IsUp = outcome.IsUp;
        result.StatusCode = outcome.StatusCode;
        result.ResponseTimeMs = outcome.ResponseTimeMs ?? (int)sw.ElapsedMilliseconds;
        result.ErrorMessage = outcome.Error is null ? null : Truncate(outcome.Error);
        result.FailureReason = outcome.IsUp ? null : outcome.Reason ?? CheckFailureReason.Other;
        return result;
    }

    private async Task<Outcome> CheckHttpAsync(UptimeMonitor monitor, Stopwatch sw, CancellationToken cancellationToken)
    {
        using var request = BuildRequest(monitor);
        var client = httpClientFactory.CreateClient(ClientName);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseTimeMs = (int)sw.ElapsedMilliseconds;

        var statusCode = (int)response.StatusCode;
        var expected = monitor.ExpectedStatusCode ?? 200;
        if (statusCode != expected)
        {
            return new Outcome(false, statusCode, $"Expected status {expected}, got {statusCode}.",
                CheckFailureReason.UnexpectedStatus, responseTimeMs);
        }

        if (monitor.ExpectedBodyContains is { } keyword)
        {
            var body = await ReadBodyAsync(response, cancellationToken);
            if (!body.Contains(keyword, StringComparison.Ordinal))
            {
                return new Outcome(false, statusCode, $"Response did not contain \"{keyword}\".",
                    CheckFailureReason.MissingKeyword, responseTimeMs);
            }
        }

        return new Outcome(true, statusCode, ResponseTimeMs: responseTimeMs);
    }

    private static HttpRequestMessage BuildRequest(UptimeMonitor monitor)
    {
        var request = new HttpRequestMessage(new HttpMethod(monitor.HttpMethod ?? "GET"), monitor.Url);
        var contentHeaders = new List<KeyValuePair<string, string>>();
        string? contentType = null;

        foreach (var (name, value) in MonitorMapping.DeserializeHeaders(monitor.RequestHeadersJson) ?? [])
        {
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                contentType = value;
            else if (!request.Headers.TryAddWithoutValidation(name, value))
                contentHeaders.Add(new(name, value));
        }

        if (monitor.RequestBody is not null)
        {
            request.Content = new StringContent(monitor.RequestBody, Encoding.UTF8);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var mediaType)
                ? mediaType
                : new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

            foreach (var (name, value) in contentHeaders)
                request.Content.Headers.TryAddWithoutValidation(name, value);
        }

        return request;
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while (buffer.Length < MaxBodyBytes && (read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            buffer.Write(chunk, 0, (int)Math.Min(read, MaxBodyBytes - buffer.Length));

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static async Task<Outcome> CheckTcpAsync(UptimeMonitor monitor, CancellationToken cancellationToken)
    {
        var address = IPAddress.Parse(monitor.IpAddress!);
        using var client = new TcpClient(address.AddressFamily);
        try
        {
            await client.ConnectAsync(address, monitor.Port!.Value, cancellationToken);
            return new Outcome(true, 0);
        }
        catch (SocketException ex)
        {
            return new Outcome(false, 0, $"Could not connect to port {monitor.Port}: {ex.Message}",
                CheckFailureReason.ConnectionFailed);
        }
    }

    private static async Task<Outcome> CheckPingAsync(UptimeMonitor monitor)
    {
        using var ping = new Ping();
        var reply = await ping.SendPingAsync(IPAddress.Parse(monitor.IpAddress!), monitor.TimeoutSeconds * 1000);

        return reply.Status switch
        {
            IPStatus.Success => new Outcome(true, 0, ResponseTimeMs: (int)reply.RoundtripTime),
            IPStatus.TimedOut => new Outcome(false, 0, $"No ping reply within {monitor.TimeoutSeconds}s.", CheckFailureReason.NoPingReply),
            _ => new Outcome(false, 0, $"Ping failed: {reply.Status}.", CheckFailureReason.NoPingReply)
        };
    }

    private static string Truncate(string message) =>
        message.Length <= MaxErrorLength ? message : message[..MaxErrorLength];
}
