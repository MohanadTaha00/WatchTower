using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WatchTower.Application.DTOs;
using WatchTower.Domain.Entities;
using WatchTower.Infrastructure.Persistence;

namespace WatchTower.Tests;

public class ApiTests(WatchTowerFactory factory) : IClassFixture<WatchTowerFactory>
{
    private const string Password = "password123";

    [Theory]
    [InlineData("/api/monitors")]
    [InlineData("/api/incidents")]
    public async Task Protected_endpoints_return_401_json_without_token(string url)
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Protected_endpoint_rejects_invalid_token()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        var response = await client.GetAsync("/api/monitors");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_stores_hashed_password_only()
    {
        var client = factory.CreateClient();
        var email = NewEmail();

        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.Equal(email, body!.Email);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        Assert.NotEqual(Password, user.PasswordHash);
        Assert.DoesNotContain(Password, user.PasswordHash);
    }

    [Fact]
    public async Task Register_duplicate_email_returns_409()
    {
        var client = factory.CreateClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password));

        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Register_short_password_returns_400()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(NewEmail(), "short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var client = factory.CreateClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password));

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Monitor_crud_flow_returns_json()
    {
        var client = await CreateAuthenticatedClientAsync();

        var createResponse = await client.PostAsJsonAsync("/api/monitors", Website("Example", "https://example.com"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<MonitorDto>();
        Assert.Equal(MonitorType.Website, created!.Type);
        Assert.Equal("https://example.com", created.Url);
        Assert.Equal(60, created.IntervalSeconds);
        Assert.Equal(10, created.TimeoutSeconds);
        Assert.Equal(200, created.ExpectedStatusCode);
        Assert.Null(created.LastCheck);

        var list = await client.GetFromJsonAsync<MonitorListResponse>("/api/monitors");
        Assert.Contains(list!.Items, m => m.Id == created.Id);

        var updateResponse = await client.PutAsJsonAsync($"/api/monitors/{created.Id}",
            Website("Renamed", "https://example.org") with
            {
                IntervalSeconds = 120,
                TimeoutSeconds = 15,
                ExpectedStatusCode = 204,
                IsEnabled = false
            });
        var updated = await updateResponse.Content.ReadFromJsonAsync<MonitorDto>();
        Assert.Equal("Renamed", updated!.Name);
        Assert.Equal(204, updated.ExpectedStatusCode);
        Assert.False(updated.IsEnabled);

        var checks = await client.GetFromJsonAsync<CheckResultListResponse>($"/api/monitors/{created.Id}/checks");
        Assert.Equal(0, checks!.TotalCount);

        var deleteResponse = await client.DeleteAsync($"/api/monitors/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/monitors/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Users_cannot_access_each_others_monitors()
    {
        var owner = await CreateAuthenticatedClientAsync();
        var other = await CreateAuthenticatedClientAsync();
        var createResponse = await owner.PostAsJsonAsync("/api/monitors", Website("Private", "https://example.com"));
        var created = await createResponse.Content.ReadFromJsonAsync<MonitorDto>();

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/monitors/{created!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/monitors/{created.Id}")).StatusCode);
        var otherList = await other.GetFromJsonAsync<MonitorListResponse>("/api/monitors");
        Assert.DoesNotContain(otherList!.Items, m => m.Id == created.Id);
    }

    [Theory]
    [InlineData("https://example.com", 60, 60, 200)]
    [InlineData("https://example.com", 10, 5, 200)]
    [InlineData("https://example.com", 60, 10, 700)]
    [InlineData("not-a-url", 60, 10, 200)]
    [InlineData("javascript:alert(1)", 60, 10, 200)]
    [InlineData("ftp://example.com/file", 60, 10, 200)]
    [InlineData("http://127.0.0.1/admin", 60, 10, 200)]
    [InlineData("http://localhost:5432", 60, 10, 200)]
    public async Task Invalid_monitor_settings_return_400(string url, int interval, int timeout, int expectedStatus)
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/monitors", Website("Bad", url) with
        {
            IntervalSeconds = interval,
            TimeoutSeconds = timeout,
            ExpectedStatusCode = expectedStatus
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ip_monitor_round_trips_and_drops_http_fields()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/monitors", new MonitorRequest
        {
            Name = "DNS",
            Type = MonitorType.IpAddress,
            IpAddress = "8.8.8.8",
            Port = 53,
            Url = "https://ignored.example.com",
            ExpectedStatusCode = 200,
            IntervalSeconds = 60,
            TimeoutSeconds = 5
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<MonitorDto>();
        Assert.Equal(MonitorType.IpAddress, created!.Type);
        Assert.Equal("8.8.8.8", created.IpAddress);
        Assert.Equal(53, created.Port);
        Assert.Null(created.Url);
        Assert.Null(created.ExpectedStatusCode);
    }

    [Theory]
    [InlineData("not-an-ip", null)]
    [InlineData("10.0.0.1", null)]
    [InlineData("127.0.0.1", null)]
    [InlineData("192.168.1.10", 80)]
    [InlineData("::1", null)]
    [InlineData("8.8.8.8", 0)]
    [InlineData("8.8.8.8", 70000)]
    public async Task Invalid_ip_monitor_returns_400(string ip, int? port)
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/monitors", new MonitorRequest
        {
            Name = "Bad IP",
            Type = MonitorType.IpAddress,
            IpAddress = ip,
            Port = port,
            IntervalSeconds = 60,
            TimeoutSeconds = 5
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Api_monitor_round_trips_method_headers_body_and_keyword()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/monitors", Api() with
        {
            HttpMethod = "post",
            RequestHeaders = new() { ["X-Api-Key"] = "secret", ["Content-Type"] = "application/json" },
            RequestBody = """{"ping":true}""",
            ExpectedBodyContains = "ok",
            ExpectedStatusCode = 201
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<MonitorDto>();
        Assert.Equal(MonitorType.Api, created!.Type);
        Assert.Equal("POST", created.HttpMethod);
        Assert.Equal("secret", created.RequestHeaders!["X-Api-Key"]);
        Assert.Equal("""{"ping":true}""", created.RequestBody);
        Assert.Equal("ok", created.ExpectedBodyContains);
        Assert.Equal(201, created.ExpectedStatusCode);
    }

    public static TheoryData<string, string?, string?, string?> InvalidApiMonitors => new()
    {
        { "GET", "{}", null, null },
        { "HEAD", null, null, null },
        { "TRACE", null, null, null },
        { "GET", null, "Host", "evil.example.com" },
        { "GET", null, "Bad Header", "x" },
        { "GET", null, "X-Test", "line1\r\nInjected: yes" }
    };

    [Theory]
    [MemberData(nameof(InvalidApiMonitors))]
    public async Task Invalid_api_monitor_returns_400(string method, string? body, string? headerName, string? headerValue)
    {
        var client = await CreateAuthenticatedClientAsync();
        var request = Api() with
        {
            HttpMethod = method,
            RequestBody = body,
            ExpectedBodyContains = method == "HEAD" ? "ok" : null,
            RequestHeaders = headerName is null ? null : new() { [headerName] = headerValue! }
        };

        var response = await client.PostAsJsonAsync("/api/monitors", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Incident_report_summarizes_open_and_resolved_incidents()
    {
        var client = await CreateAuthenticatedClientAsync();
        var other = await CreateAuthenticatedClientAsync();
        var monitor = await (await client.PostAsJsonAsync("/api/monitors", Website("Shop", "https://example.com")))
            .Content.ReadFromJsonAsync<MonitorDto>();
        var otherMonitor = await (await other.PostAsJsonAsync("/api/monitors", Website("Other", "https://example.com")))
            .Content.ReadFromJsonAsync<MonitorDto>();

        var now = DateTime.UtcNow;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Incidents.AddRange(
                new Incident { Id = Guid.NewGuid(), MonitorId = monitor!.Id, StartedAt = now.AddHours(-3), ResolvedAt = now.AddHours(-2), Summary = "500" },
                new Incident { Id = Guid.NewGuid(), MonitorId = monitor.Id, StartedAt = now.AddMinutes(-10), Summary = "timeout" },
                new Incident { Id = Guid.NewGuid(), MonitorId = monitor.Id, StartedAt = now.AddDays(-40), ResolvedAt = now.AddDays(-39), Summary = "old" },
                new Incident { Id = Guid.NewGuid(), MonitorId = otherMonitor!.Id, StartedAt = now.AddHours(-1), Summary = "not mine" });
            await db.SaveChangesAsync();
        }

        var summary = await client.GetFromJsonAsync<IncidentSummaryDto>("/api/incidents/summary?days=30");
        Assert.Equal(2, summary!.TotalIncidents);
        Assert.Equal(1, summary.OpenIncidents);
        Assert.Equal(1, summary.ResolvedIncidents);
        Assert.InRange(summary.AverageResolutionSeconds!.Value, 3599, 3601);
        var row = Assert.Single(summary.ByMonitor);
        Assert.Equal("Shop", row.MonitorName);
        Assert.Equal(2, row.Incidents);

        var allTime = await client.GetFromJsonAsync<IncidentSummaryDto>("/api/incidents/summary");
        Assert.Equal(3, allTime!.TotalIncidents);

        var open = await client.GetFromJsonAsync<IncidentListResponse>("/api/incidents?status=open");
        var openIncident = Assert.Single(open!.Items);
        Assert.True(openIncident.IsOpen);
        Assert.Equal("Shop", openIncident.MonitorName);

        var resolved = await client.GetFromJsonAsync<IncidentListResponse>("/api/incidents?status=resolved&days=30");
        Assert.All(resolved!.Items, i => Assert.False(i.IsOpen));
        Assert.Single(resolved.Items);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/incidents/summary?days=0")).StatusCode);
    }

    [Fact]
    public async Task Monitor_stats_report_uptime_windows_and_response_times()
    {
        var client = await CreateAuthenticatedClientAsync();
        var other = await CreateAuthenticatedClientAsync();
        var monitor = await (await client.PostAsJsonAsync("/api/monitors", Website("Shop", "https://example.com")))
            .Content.ReadFromJsonAsync<MonitorDto>();

        var now = DateTime.UtcNow;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            CheckResult Check(TimeSpan ago, bool isUp, int ms) => new()
            {
                Id = Guid.NewGuid(), MonitorId = monitor!.Id, IsUp = isUp, StatusCode = isUp ? 200 : 500,
                ResponseTimeMs = ms, CheckedAt = now - ago
            };
            db.CheckResults.AddRange(
                Check(TimeSpan.FromMinutes(5), true, 100),
                Check(TimeSpan.FromMinutes(10), true, 300),
                Check(TimeSpan.FromMinutes(15), false, 10000),
                Check(TimeSpan.FromDays(3), true, 200),
                Check(TimeSpan.FromDays(20), false, 10000),
                Check(TimeSpan.FromDays(40), false, 10000));
            await db.SaveChangesAsync();
        }

        var day = await client.GetFromJsonAsync<MonitorStatsDto>($"/api/monitors/{monitor!.Id}/stats");
        Assert.Equal("24h", day!.Range);
        Assert.Equal(48, day.Buckets.Count);
        Assert.Equal(1800, day.BucketSeconds);
        Assert.Equal(3, day.Buckets.Sum(b => b.Checks));
        Assert.Equal(200, day.ResponseTime.AverageMs);
        Assert.Equal(300, day.ResponseTime.MaxMs);

        var uptime = day.Uptime.ToDictionary(u => u.Window);
        Assert.Equal(3, uptime["24h"].Checks);
        Assert.Equal(66.667, uptime["24h"].UptimePercent);
        Assert.Equal(4, uptime["7d"].Checks);
        Assert.Equal(75.0, uptime["7d"].UptimePercent);
        Assert.Equal(5, uptime["30d"].Checks);
        Assert.Equal(60.0, uptime["30d"].UptimePercent);

        var month = await client.GetFromJsonAsync<MonitorStatsDto>($"/api/monitors/{monitor.Id}/stats?range=30d");
        Assert.Equal(60, month!.Buckets.Count);
        Assert.Equal(5, month.Buckets.Sum(b => b.Checks));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/monitors/{monitor.Id}/stats?range=1y")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/monitors/{monitor.Id}/stats")).StatusCode);
    }

    [Fact]
    public async Task Dashboard_is_served_at_root()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("WatchTower", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_endpoints_return_200()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = factory.CreateClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password));
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal("Bearer", login!.TokenType);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private static MonitorRequest Website(string name, string url) => new()
    {
        Name = name,
        Url = url,
        IntervalSeconds = 60,
        TimeoutSeconds = 10,
        ExpectedStatusCode = 200
    };

    private static MonitorRequest Api() => new()
    {
        Name = "API",
        Type = MonitorType.Api,
        Url = "https://api.example.com/health",
        IntervalSeconds = 60,
        TimeoutSeconds = 10
    };
}
