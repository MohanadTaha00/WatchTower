using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WatchTower.Application.Interfaces;
using WatchTower.Application.Options;
using WatchTower.Domain.Entities;
using WatchTower.Infrastructure.Persistence;
using WatchTower.Infrastructure.Services;

namespace WatchTower.Tests;

public class EmailNotificationProcessorTests
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase($"notifications-{Guid.NewGuid()}")
        .Options);

    private readonly FakeEmailSender _sender = new();

    private EmailNotificationProcessor Processor(int maxAttempts = 3) =>
        new(_db, _sender, Options.Create(new NotificationOptions { MaxAttempts = maxAttempts }),
            NullLogger<EmailNotificationProcessor>.Instance);

    [Fact]
    public async Task Due_notification_is_sent_and_marked_sent()
    {
        var notification = await AddNotificationAsync();

        var processed = await Processor().ProcessDueAsync();

        Assert.Equal(1, processed);
        var sent = Assert.Single(_sender.Sent);
        Assert.Equal("owner@example.com", sent.To);
        Assert.Equal("[WatchTower] DOWN: Shop", sent.Subject);
        Assert.Equal(NotificationStatus.Sent, notification.Status);
        Assert.NotNull(notification.SentAt);
        Assert.Equal(1, notification.Attempts);
    }

    [Fact]
    public async Task Sent_and_future_notifications_are_skipped()
    {
        var future = await AddNotificationAsync(nextAttemptAt: DateTime.UtcNow.AddMinutes(5));
        var alreadySent = await AddNotificationAsync();
        alreadySent.Status = NotificationStatus.Sent;
        await _db.SaveChangesAsync();

        var processed = await Processor().ProcessDueAsync();

        Assert.Equal(0, processed);
        Assert.Empty(_sender.Sent);
        Assert.Equal(NotificationStatus.Pending, future.Status);
    }

    [Fact]
    public async Task Failure_schedules_a_retry_with_backoff()
    {
        _sender.FailWith = new InvalidOperationException("SMTP server unavailable");
        var notification = await AddNotificationAsync();

        await Processor().ProcessDueAsync();

        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Equal(1, notification.Attempts);
        Assert.Equal("SMTP server unavailable", notification.LastError);
        Assert.InRange(notification.NextAttemptAt, DateTime.UtcNow.AddSeconds(25), DateTime.UtcNow.AddSeconds(35));
    }

    [Fact]
    public async Task Gives_up_after_max_attempts()
    {
        _sender.FailWith = new InvalidOperationException("Mailbox unavailable");
        var notification = await AddNotificationAsync();
        notification.Attempts = 2;
        await _db.SaveChangesAsync();

        await Processor(maxAttempts: 3).ProcessDueAsync();

        Assert.Equal(NotificationStatus.Failed, notification.Status);
        Assert.Equal(3, notification.Attempts);
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(10, 1800)]
    public void Retry_delay_doubles_up_to_a_cap(int attempts, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), EmailNotificationProcessor.RetryDelay(attempts));
    }

    private async Task<EmailNotification> AddNotificationAsync(DateTime? nextAttemptAt = null)
    {
        var notification = new EmailNotification
        {
            Id = Guid.NewGuid(),
            IncidentId = Guid.NewGuid(),
            Kind = NotificationKind.IncidentOpened,
            ToAddress = "owner@example.com",
            Subject = "[WatchTower] DOWN: Shop",
            TextBody = "text",
            HtmlBody = "<p>html</p>",
            CreatedAt = DateTime.UtcNow,
            NextAttemptAt = nextAttemptAt ?? DateTime.UtcNow
        };
        _db.EmailNotifications.Add(notification);
        await _db.SaveChangesAsync();
        return notification;
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public Exception? FailWith { get; set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (FailWith is not null)
                throw FailWith;
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
