using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using WatchTower.Domain.Entities;
using WatchTower.Infrastructure.Services;

namespace WatchTower.Tests;

public class MonitorCheckExecutorTests
{
    [Fact]
    public async Task Expected_status_code_marks_monitor_up()
    {
        var executor = CreateExecutor(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await executor.ExecuteAsync(CreateMonitor(expectedStatusCode: 200));

        Assert.True(result.IsUp);
        Assert.Equal(200, result.StatusCode);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task Unexpected_status_code_marks_monitor_down()
    {
        var executor = CreateExecutor(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var result = await executor.ExecuteAsync(CreateMonitor(expectedStatusCode: 200));

        Assert.False(result.IsUp);
        Assert.Equal(500, result.StatusCode);
        Assert.Contains("Expected status 200", result.ErrorMessage);
        Assert.Equal(CheckFailureReason.UnexpectedStatus, result.FailureReason);
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, CheckFailureReason.DnsFailure)]
    [InlineData(HttpRequestError.ConnectionError, CheckFailureReason.ConnectionFailed)]
    [InlineData(HttpRequestError.SecureConnectionError, CheckFailureReason.TlsFailure)]
    [InlineData(HttpRequestError.Unknown, CheckFailureReason.Other)]
    public async Task Transport_errors_are_classified(HttpRequestError error, CheckFailureReason expected)
    {
        var executor = CreateExecutor(_ => throw new HttpRequestException(error, "boom"));

        var result = await executor.ExecuteAsync(CreateMonitor());

        Assert.Equal(expected, result.FailureReason);
    }

    [Fact]
    public async Task Timeout_marks_monitor_down()
    {
        var executor = CreateExecutor(async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var result = await executor.ExecuteAsync(CreateMonitor(timeoutSeconds: 1));

        Assert.False(result.IsUp);
        Assert.Equal(0, result.StatusCode);
        Assert.Contains("timed out", result.ErrorMessage);
        Assert.Equal(CheckFailureReason.Timeout, result.FailureReason);
    }

    [Fact]
    public async Task Transport_error_marks_monitor_down()
    {
        var executor = CreateExecutor(_ => throw new HttpRequestException("No such host is known."));

        var result = await executor.ExecuteAsync(CreateMonitor());

        Assert.False(result.IsUp);
        Assert.Equal("No such host is known.", result.ErrorMessage);
    }

    [Fact]
    public async Task Api_monitor_sends_method_headers_and_body()
    {
        HttpRequestMessage? sent = null;
        string? sentBody = null;
        var executor = CreateExecutor(async (request, _) =>
        {
            sent = request;
            sentBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        var monitor = CreateApiMonitor();
        monitor.HttpMethod = "POST";
        monitor.ExpectedStatusCode = 201;
        monitor.RequestBody = """{"ping":true}""";
        monitor.RequestHeadersJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["X-Api-Key"] = "secret",
            ["Content-Type"] = "application/vnd.test+json"
        });

        var result = await executor.ExecuteAsync(monitor);

        Assert.True(result.IsUp);
        Assert.Equal(HttpMethod.Post, sent!.Method);
        Assert.Equal("secret", sent.Headers.GetValues("X-Api-Key").Single());
        Assert.Equal("application/vnd.test+json", sent.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("""{"ping":true}""", sentBody);
    }

    [Theory]
    [InlineData("""{"status":"ok"}""", true)]
    [InlineData("""{"status":"degraded"}""", false)]
    public async Task Api_monitor_checks_response_keyword(string responseBody, bool expectedUp)
    {
        var executor = CreateExecutor(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody)
        }));
        var monitor = CreateApiMonitor();
        monitor.ExpectedBodyContains = "\"ok\"";

        var result = await executor.ExecuteAsync(monitor);

        Assert.Equal(expectedUp, result.IsUp);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(expectedUp ? null : CheckFailureReason.MissingKeyword, result.FailureReason);
        if (!expectedUp)
            Assert.Contains("did not contain", result.ErrorMessage);
    }

    [Fact]
    public async Task Tcp_check_is_up_when_port_accepts_connections()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var result = await CreateExecutor(_ => throw new InvalidOperationException("HTTP not expected"))
            .ExecuteAsync(CreateIpMonitor(port));

        Assert.True(result.IsUp, result.ErrorMessage);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task Tcp_check_is_down_when_port_is_closed()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var result = await CreateExecutor(_ => throw new InvalidOperationException("HTTP not expected"))
            .ExecuteAsync(CreateIpMonitor(port));

        Assert.False(result.IsUp);
        Assert.Contains($"port {port}", result.ErrorMessage);
        Assert.Equal(CheckFailureReason.ConnectionFailed, result.FailureReason);
    }

    [Fact]
    public async Task Ping_check_is_up_for_loopback()
    {
        var result = await CreateExecutor(_ => throw new InvalidOperationException("HTTP not expected"))
            .ExecuteAsync(CreateIpMonitor(port: null));

        Assert.True(result.IsUp, result.ErrorMessage);
    }

    private static MonitorCheckExecutor CreateExecutor(Func<CancellationToken, Task<HttpResponseMessage>> respond) =>
        CreateExecutor((_, ct) => respond(ct));

    private static MonitorCheckExecutor CreateExecutor(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
        new(new StubHttpClientFactory(new StubHandler(respond)));

    private static UptimeMonitor CreateMonitor(int expectedStatusCode = 200, int timeoutSeconds = 5) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Url = "https://example.com",
            IntervalSeconds = 60,
            TimeoutSeconds = timeoutSeconds,
            ExpectedStatusCode = expectedStatusCode
        };

    private static UptimeMonitor CreateApiMonitor() =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "API",
            Type = MonitorType.Api,
            Url = "https://api.example.com/health",
            HttpMethod = "GET",
            ExpectedStatusCode = 200,
            IntervalSeconds = 60,
            TimeoutSeconds = 5
        };

    private static UptimeMonitor CreateIpMonitor(int? port) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "Host",
            Type = MonitorType.IpAddress,
            IpAddress = "127.0.0.1",
            Port = port,
            IntervalSeconds = 60,
            TimeoutSeconds = 3
        };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
