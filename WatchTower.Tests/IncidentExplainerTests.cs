using WatchTower.Application.Notifications;
using WatchTower.Domain.Entities;

namespace WatchTower.Tests;

public class IncidentExplainerTests
{
    private static readonly UptimeMonitor Website = new()
    {
        Name = "Shop",
        Url = "https://shop.example.com/health",
        ExpectedStatusCode = 200,
        IntervalSeconds = 60,
        TimeoutSeconds = 10
    };

    [Theory]
    [InlineData(CheckFailureReason.Timeout, 0, "did not respond within 10 seconds")]
    [InlineData(CheckFailureReason.UnexpectedStatus, 503, "HTTP 503 (Service Unavailable) instead of the expected 200")]
    [InlineData(CheckFailureReason.UnexpectedStatus, 500, "server-side error")]
    [InlineData(CheckFailureReason.UnexpectedStatus, 404, "was not found")]
    [InlineData(CheckFailureReason.UnexpectedStatus, 401, "API key or credentials")]
    [InlineData(CheckFailureReason.UnexpectedStatus, 301, "redirects somewhere else")]
    [InlineData(CheckFailureReason.DnsFailure, 0, "shop.example.com could not be resolved")]
    [InlineData(CheckFailureReason.ConnectionFailed, 0, "could not open a connection to shop.example.com")]
    [InlineData(CheckFailureReason.TlsFailure, 0, "certificate may be expired")]
    public void Website_failures_are_explained(CheckFailureReason reason, int statusCode, string expected)
    {
        var text = IncidentExplainer.Explain(Website, Failed(reason, statusCode));

        Assert.Contains(expected, text);
    }

    [Fact]
    public void Missing_keyword_is_explained()
    {
        var api = new UptimeMonitor
        {
            Name = "API", Type = MonitorType.Api, Url = "https://api.example.com", HttpMethod = "POST",
            ExpectedStatusCode = 200, ExpectedBodyContains = "\"ok\"", IntervalSeconds = 60, TimeoutSeconds = 10
        };

        var text = IncidentExplainer.Explain(api, Failed(CheckFailureReason.MissingKeyword, 200));

        Assert.Contains("did not contain \"\"ok\"\"", text);
        Assert.Equal("POST https://api.example.com", IncidentExplainer.Describe(api));
    }

    [Fact]
    public void Closed_tcp_port_is_explained()
    {
        var host = new UptimeMonitor
        {
            Name = "DB", Type = MonitorType.IpAddress, IpAddress = "203.0.113.10", Port = 5432,
            IntervalSeconds = 60, TimeoutSeconds = 5
        };

        var text = IncidentExplainer.Explain(host, Failed(CheckFailureReason.ConnectionFailed, 0));

        Assert.Contains("Nothing accepted a connection on port 5432 at 203.0.113.10", text);
    }

    [Fact]
    public void Missing_ping_reply_is_explained()
    {
        var host = new UptimeMonitor
        {
            Name = "Gateway", Type = MonitorType.IpAddress, IpAddress = "203.0.113.1",
            IntervalSeconds = 60, TimeoutSeconds = 5
        };

        var text = IncidentExplainer.Explain(host, Failed(CheckFailureReason.NoPingReply, 0));

        Assert.Contains("203.0.113.1 did not answer a ping within 5 seconds", text);
    }

    [Fact]
    public void Unknown_failure_includes_the_raw_error()
    {
        var check = Failed(CheckFailureReason.Other, 0);
        check.ErrorMessage = "Something odd happened.";

        Assert.Contains("The error was: Something odd happened.", IncidentExplainer.Explain(Website, check));
    }

    [Fact]
    public void Monitor_name_is_html_encoded_in_the_email()
    {
        var monitor = new UptimeMonitor
        {
            Name = "<script>alert(1)</script>", Url = "https://example.com",
            ExpectedStatusCode = 200, IntervalSeconds = 60, TimeoutSeconds = 10
        };
        var incident = new Incident { Id = Guid.NewGuid(), StartedAt = DateTime.UtcNow, Summary = "down" };

        var email = IncidentEmailComposer.ComposeOpened(monitor, incident, Failed(CheckFailureReason.Timeout, 0), "http://localhost/");

        Assert.DoesNotContain("<script>", email.HtmlBody);
        Assert.Contains("&lt;script&gt;", email.HtmlBody);
    }

    [Theory]
    [InlineData(45, "45 seconds")]
    [InlineData(60, "1 minute")]
    [InlineData(150, "2 minutes 30 seconds")]
    [InlineData(3_900, "1 hour 5 minutes")]
    [InlineData(90_000, "1 day 1 hour")]
    public void Durations_are_human_readable(double seconds, string expected)
    {
        Assert.Equal(expected, IncidentEmailComposer.FormatDuration(seconds));
    }

    private static CheckResult Failed(CheckFailureReason reason, int statusCode) =>
        new() { Id = Guid.NewGuid(), IsUp = false, StatusCode = statusCode, FailureReason = reason, CheckedAt = DateTime.UtcNow };
}
