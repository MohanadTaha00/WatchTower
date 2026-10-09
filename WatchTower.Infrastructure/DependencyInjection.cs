using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WatchTower.Application.Interfaces;
using WatchTower.Application.Options;
using WatchTower.Infrastructure.Persistence;
using WatchTower.Infrastructure.Services;

namespace WatchTower.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<SchedulerOptions>(configuration.GetSection(SchedulerOptions.SectionName));
        services.Configure<MonitoringOptions>(configuration.GetSection(MonitoringOptions.SectionName));
        services.Configure<NotificationOptions>(configuration.GetSection(NotificationOptions.SectionName));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.Configure<PasswordResetOptions>(configuration.GetSection(PasswordResetOptions.SectionName));
        services.Configure<TwoFactorOptions>(configuration.GetSection(TwoFactorOptions.SectionName));

        // Encrypts authenticator secrets. If the keys are lost, users with an authenticator must use a recovery code.
        var dataProtection = services.AddDataProtection().SetApplicationName("WatchTower");
        var keysPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.AddHttpClient(MonitorCheckExecutor.ClientName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WatchTower/1.0");
        });

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IPasswordService, PasswordService>();
        services.AddScoped<TwoFactorService>();
        services.AddScoped<ITwoFactorService>(sp => sp.GetRequiredService<TwoFactorService>());
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IMonitorService, MonitorService>();
        services.AddScoped<IIncidentService, IncidentService>();
        services.AddScoped<IMonitorCheckExecutor, MonitorCheckExecutor>();
        services.AddScoped<IncidentTracker>();
        services.AddScoped<IncidentNotifier>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<EmailNotificationProcessor>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<IMonitorEventPublisher, NoOpMonitorEventPublisher>();
        services.AddHostedService<MonitorSchedulerService>();
        services.AddHostedService<NotificationDispatcherService>();

        return services;
    }
}
