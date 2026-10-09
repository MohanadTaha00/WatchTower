using Microsoft.EntityFrameworkCore;
using WatchTower.Infrastructure.Persistence;

namespace WatchTower.Tests;

public class SchedulerQueryTests
{
    [Fact]
    public void Due_monitor_query_translates_to_postgres_sql()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=unused;Database=unused")
            .Options);
        var now = DateTime.UtcNow;

        var sql = db.Monitors
            .Where(m => m.IsEnabled)
            .Where(m => m.LastCheckAt == null || m.LastCheckAt.Value.AddSeconds(m.IntervalSeconds) <= now)
            .Select(m => m.Id)
            .ToQueryString();

        Assert.Contains("\"IntervalSeconds\"", sql);
    }
}
