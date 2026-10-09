using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WatchTower.Application.Options;
using WatchTower.Domain.Entities;
using WatchTower.Infrastructure.Persistence;
using WatchTower.Infrastructure.Services;

namespace WatchTower.Tests;

public class IncidentTrackerTests
{
    private const string OwnerEmail = "owner@example.com";

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase($"incidents-{Guid.NewGuid()}")
        .Options);

    private readonly UptimeMonitor _monitor;

    public IncidentTrackerTests()
    {
        var user = new User { Id = Guid.NewGuid(), Email = OwnerEmail, PasswordHash = "hash" };
        _db.Users.Add(user);
        _db.SaveChanges();

        _monitor = new UptimeMonitor
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Name = "Shop",
            Url = "https://example.com",
            IntervalSeconds = 60,
            TimeoutSeconds = 10,
            ExpectedStatusCode = 200
        };
    }

    private IncidentTracker CreateTracker(bool notificationsEnabled = true, string smtpHost = "localhost") =>
        new(_db,
            new NoOpMonitorEventPublisher(NullLogger<NoOpMonitorEventPublisher>.Instance),
            new IncidentNotifier(_db,
                Options.Create(new NotificationOptions { Enabled = notificationsEnabled, DashboardUrl = "https://watch.example.com/" }),
                Options.Create(new SmtpOptions { Host = smtpHost })));

    private IncidentTracker Tracker => CreateTracker();

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task Going_down_opens_incident(bool? previousIsUp)
    {
        await Tracker.ApplyAsync(_monitor, Check(isUp: false, "Expected status 200, got 503."), previousIsUp);
        await _db.SaveChangesAsync();

        var incident = await _db.Incidents.SingleAsync();
        Assert.Null(incident.ResolvedAt);
        Assert.Equal("Expected status 200, got 503.", incident.Summary);
    }

    [Fact]
    public async Task Going_down_queues_an_explained_email_to_the_owner()
    {
        await Tracker.ApplyAsync(_monitor, Check(isUp: false, "Expected status 200, got 503."), previousIsUp: true);
        await _db.SaveChangesAsync();

        var incident = await _db.Incidents.SingleAsync();
        var email = await _db.EmailNotifications.SingleAsync();
        Assert.Equal(NotificationKind.IncidentOpened, email.Kind);
        Assert.Equal(NotificationStatus.Pending, email.Status);
        Assert.Equal(incident.Id, email.IncidentId);
        Assert.Equal(OwnerEmail, email.ToAddress);
        Assert.Equal("[WatchTower] DOWN: Shop", email.Subject);
        Assert.Contains("HTTP 503 (Service Unavailable)", email.TextBody);
        Assert.Contains("maintenance or overload", email.TextBody);
        Assert.Contains("https://watch.example.com/#/incidents", email.HtmlBody);
    }

    [Fact]
    public async Task Staying_down_does_not_open_another_incident_or_email()
    {
        await Tracker.ApplyAsync(_monitor, Check(isUp: false), previousIsUp: true);
        await _db.SaveChangesAsync();

        await Tracker.ApplyAsync(_monitor, Check(isUp: false), previousIsUp: false);
        await _db.SaveChangesAsync();

        Assert.Equal(1, await _db.Incidents.CountAsync());
        Assert.Equal(1, await _db.EmailNotifications.CountAsync());
    }

    [Fact]
    public async Task Recovering_resolves_open_incident_and_queues_recovery_email()
    {
        await Tracker.ApplyAsync(_monitor, Check(isUp: false), previousIsUp: true);
        await _db.SaveChangesAsync();

        var recovery = Check(isUp: true);
        await Tracker.ApplyAsync(_monitor, recovery, previousIsUp: false);
        await _db.SaveChangesAsync();

        var incident = await _db.Incidents.SingleAsync();
        Assert.Equal(recovery.CheckedAt, incident.ResolvedAt);

        var resolvedEmail = await _db.EmailNotifications.SingleAsync(n => n.Kind == NotificationKind.IncidentResolved);
        Assert.Equal("[WatchTower] RESOLVED: Shop is back up", resolvedEmail.Subject);
        Assert.Contains("The outage lasted", resolvedEmail.TextBody);
    }

    [Fact]
    public async Task Staying_up_creates_no_incident()
    {
        await Tracker.ApplyAsync(_monitor, Check(isUp: true), previousIsUp: true);
        await _db.SaveChangesAsync();

        Assert.Empty(await _db.Incidents.ToListAsync());
        Assert.Empty(await _db.EmailNotifications.ToListAsync());
    }

    [Theory]
    [InlineData(false, "localhost")]
    [InlineData(true, "")]
    public async Task No_email_is_queued_when_notifications_are_off(bool enabled, string smtpHost)
    {
        await CreateTracker(enabled, smtpHost).ApplyAsync(_monitor, Check(isUp: false), previousIsUp: true);
        await _db.SaveChangesAsync();

        Assert.Equal(1, await _db.Incidents.CountAsync());
        Assert.Empty(await _db.EmailNotifications.ToListAsync());
    }

    private CheckResult Check(bool isUp, string? error = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            MonitorId = _monitor.Id,
            IsUp = isUp,
            StatusCode = isUp ? 200 : 503,
            ErrorMessage = error,
            FailureReason = isUp ? null : CheckFailureReason.UnexpectedStatus,
            CheckedAt = DateTime.UtcNow
        };
}
