using Microsoft.EntityFrameworkCore;
using WatchTower.Domain.Entities;

namespace WatchTower.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UptimeMonitor> Monitors => Set<UptimeMonitor>();
    public DbSet<CheckResult> CheckResults => Set<CheckResult>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<EmailNotification> EmailNotifications => Set<EmailNotification>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<TwoFactorChallenge> TwoFactorChallenges => Set<TwoFactorChallenge>();
    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Email).HasMaxLength(320);
            entity.Property(e => e.PasswordHash).HasMaxLength(512);
            entity.Property(e => e.SecurityStamp).HasMaxLength(64);
            entity.Property(e => e.TwoFactorMethod).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.AuthenticatorKey).HasMaxLength(1000);
        });

        modelBuilder.Entity<TwoFactorChallenge>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.Purpose });
            entity.Property(e => e.Purpose).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Method).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.TokenHash).HasMaxLength(64);
            entity.Property(e => e.CodeHash).HasMaxLength(64);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecoveryCode>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.CodeHash });
            entity.Property(e => e.CodeHash).HasMaxLength(64);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });
            entity.Property(e => e.TokenHash).HasMaxLength(64);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UptimeMonitor>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Url).HasMaxLength(2048);
            entity.Property(e => e.HttpMethod).HasMaxLength(10);
            entity.Property(e => e.RequestBody).HasMaxLength(10_000);
            entity.Property(e => e.ExpectedBodyContains).HasMaxLength(200);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.HasOne(e => e.User)
                .WithMany(u => u.Monitors)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CheckResult>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.MonitorId, e.CheckedAt });
            entity.Property(e => e.ErrorMessage).HasMaxLength(500);
            entity.Property(e => e.FailureReason).HasConversion<string>().HasMaxLength(30);
            entity.HasOne(e => e.Monitor)
                .WithMany(m => m.CheckResults)
                .HasForeignKey(e => e.MonitorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Incident>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.MonitorId);
            entity.Property(e => e.Summary).HasMaxLength(500);
            entity.HasOne(e => e.Monitor)
                .WithMany(m => m.Incidents)
                .HasForeignKey(e => e.MonitorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmailNotification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Status, e.NextAttemptAt });
            entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.ToAddress).HasMaxLength(320);
            entity.Property(e => e.Subject).HasMaxLength(300);
            entity.Property(e => e.LastError).HasMaxLength(1000);
            entity.HasOne(e => e.Incident)
                .WithMany(i => i.Notifications)
                .HasForeignKey(e => e.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
