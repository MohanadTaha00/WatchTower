using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WatchTower.Application.DTOs;
using WatchTower.Application.Exceptions;
using WatchTower.Application.Interfaces;
using WatchTower.Application.Mapping;
using WatchTower.Application.Options;
using WatchTower.Application.Statistics;
using WatchTower.Application.Validation;
using WatchTower.Domain.Entities;
using WatchTower.Infrastructure.Persistence;

namespace WatchTower.Infrastructure.Services;

public class MonitorService(
    AppDbContext db,
    IOptions<MonitoringOptions> monitoringOptions) : IMonitorService
{
    private readonly MonitoringOptions _monitoringOptions = monitoringOptions.Value;

    public async Task<MonitorListResponse> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var monitors = await db.Monitors
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        return new MonitorListResponse(monitors.Select(MonitorMapping.ToDto).ToList());
    }

    public async Task<MonitorDto> GetAsync(Guid userId, Guid monitorId, CancellationToken cancellationToken = default)
    {
        var monitor = await db.Monitors.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == monitorId && m.UserId == userId, cancellationToken);
        return MonitorMapping.ToDto(monitor ?? throw new NotFoundException("Monitor not found."));
    }

    public async Task<MonitorDto> CreateAsync(Guid userId, MonitorRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = MonitorValidator.Normalize(request, _monitoringOptions);

        var now = DateTime.UtcNow;
        var monitor = new UptimeMonitor
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = normalized.Name,
            CreatedAt = now,
            UpdatedAt = now
        };
        MonitorMapping.Apply(normalized, monitor);

        db.Monitors.Add(monitor);
        await db.SaveChangesAsync(cancellationToken);
        return MonitorMapping.ToDto(monitor);
    }

    public async Task<MonitorDto> UpdateAsync(
        Guid userId,
        Guid monitorId,
        MonitorRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = MonitorValidator.Normalize(request, _monitoringOptions);

        var monitor = await FindOwnedMonitorTrackedAsync(userId, monitorId, cancellationToken);
        MonitorMapping.Apply(normalized, monitor);
        monitor.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return MonitorMapping.ToDto(monitor);
    }

    public async Task DeleteAsync(Guid userId, Guid monitorId, CancellationToken cancellationToken = default)
    {
        var monitor = await FindOwnedMonitorTrackedAsync(userId, monitorId, cancellationToken);
        db.Monitors.Remove(monitor);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CheckResultListResponse> GetChecksAsync(
        Guid userId,
        Guid monitorId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var exists = await db.Monitors.AnyAsync(m => m.Id == monitorId && m.UserId == userId, cancellationToken);
        if (!exists)
            throw new NotFoundException("Monitor not found.");

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.CheckResults.AsNoTracking().Where(c => c.MonitorId == monitorId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(c => c.CheckedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new CheckResultListResponse(
            items.Select(MonitorMapping.ToDto).ToList(),
            page,
            pageSize,
            total);
    }

    public async Task<MonitorStatsDto> GetStatsAsync(
        Guid userId,
        Guid monitorId,
        string? range,
        CancellationToken cancellationToken = default)
    {
        var statsRange = StatsRange.Parse(range);

        var exists = await db.Monitors.AnyAsync(m => m.Id == monitorId && m.UserId == userId, cancellationToken);
        if (!exists)
            throw new NotFoundException("Monitor not found.");

        var now = DateTime.UtcNow;
        var checks = db.CheckResults.AsNoTracking().Where(c => c.MonitorId == monitorId);

        var uptime = new List<UptimeWindowDto>();
        foreach (var window in StatsRange.All)
        {
            var since = now - window.Length;
            var counts = await checks
                .Where(c => c.CheckedAt >= since)
                .GroupBy(c => c.IsUp)
                .Select(g => new { IsUp = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            var total = counts.Sum(c => c.Count);
            var up = counts.Where(c => c.IsUp).Sum(c => c.Count);
            uptime.Add(new UptimeWindowDto(window.Key, total, up, MonitorStats.UptimePercent(up, total)));
        }

        var from = MonitorStats.RangeStart(statsRange, now);
        var samples = await checks
            .Where(c => c.CheckedAt >= from)
            .Select(c => new CheckSample(c.CheckedAt, c.IsUp, c.ResponseTimeMs))
            .ToListAsync(cancellationToken);

        return new MonitorStatsDto(
            statsRange.Key,
            from,
            now,
            (int)statsRange.Bucket.TotalSeconds,
            uptime,
            MonitorStats.SummarizeResponseTimes(samples),
            MonitorStats.BuildBuckets(statsRange, from, samples));
    }

    private async Task<UptimeMonitor> FindOwnedMonitorTrackedAsync(Guid userId, Guid monitorId, CancellationToken cancellationToken)
    {
        var monitor = await db.Monitors
            .FirstOrDefaultAsync(m => m.Id == monitorId && m.UserId == userId, cancellationToken);
        return monitor ?? throw new NotFoundException("Monitor not found.");
    }
}
