using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WatchTower.Application.Interfaces;
using WatchTower.Infrastructure.Persistence;
using WatchTower.Infrastructure.Services;

namespace WatchTower.Tests;

public class WatchTowerFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"watchtower-tests-{Guid.NewGuid()}";

    public RecordingEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("RateLimiting:AuthPermitsPerMinute", "1000");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            foreach (var worker in new[] { typeof(MonitorSchedulerService), typeof(NotificationDispatcherService) })
                services.Remove(services.Single(d => d.ImplementationType == worker));

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }
}

public class RecordingEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];
    public Exception? FailWith { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (FailWith is not null)
            throw FailWith;
        lock (Sent)
            Sent.Add(message);
        return Task.CompletedTask;
    }
}

internal static class ServiceCollectionExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(T)).ToList())
            services.Remove(descriptor);
    }
}
