using Microsoft.EntityFrameworkCore;
using WatchTower.Application.DTOs;
using WatchTower.Application.Exceptions;
using WatchTower.Application.Interfaces;
using WatchTower.Application.Mapping;
using WatchTower.Domain.Entities;
using WatchTower.Infrastructure.Persistence;

namespace WatchTower.Infrastructure.Services;

public class IncidentService(AppDbContext db) : IIncidentService
{
    public async Task<IncidentListResponse> ListAsync(
        Guid userId,
        string? status,
        Guid? monitorId,
        int? days,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var now = DateTime.UtcNow;

        var query = IncidentsInPeriod(userId, days, now);

        if (string.Equals(status, "open", StringComparison.OrdinalIgnoreCase))
            query = query.Where(i => i.ResolvedAt == null);
        else if (string.Equals(status, "resolved", StringComparison.OrdinalIgnoreCase))
            query = query.Where(i => i.ResolvedAt != null);

        if (monitorId is not null)
            query = query.Where(i => i.MonitorId == monitorId);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(i => i.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new IncidentListResponse(
            items.Select(i => MonitorMapping.ToDto(i, now)).ToList(),
            page,
            pageSize,
            total);
    }

    public async Task<IncidentSummaryDto> GetSummaryAsync(
        Guid userId,
        int? days,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var periodStart = days is null ? (DateTime?)null : now.AddDays(-days.Value);

        var incidents = await IncidentsInPeriod(userId, days, now).ToListAsync(cancellationToken);

        double Downtime(Incident i)
        {
            var start = periodStart is not null && i.StartedAt < periodStart ? periodStart.Value : i.StartedAt;
            return Math.Max(0, ((i.ResolvedAt ?? now) - start).TotalSeconds);
        }

        var resolved = incidents.Where(i => i.ResolvedAt is not null).ToList();

        var byMonitor = incidents
            .GroupBy(i => i.MonitorId)
            .Select(g => new MonitorIncidentSummaryDto(
                g.Key,
                g.First().Monitor.Name,
                g.First().Monitor.Type,
                g.Count(),
                g.Count(i => i.ResolvedAt is null),
                g.Sum(Downtime),
                g.Max(i => i.StartedAt)))
            .OrderByDescending(m => m.DowntimeSeconds)
            .ToList();

        return new IncidentSummaryDto(
            days,
            incidents.Count,
            incidents.Count - resolved.Count,
            resolved.Count,
            resolved.Count == 0 ? null : resolved.Average(i => (i.ResolvedAt!.Value - i.StartedAt).TotalSeconds),
            incidents.Sum(Downtime),
            byMonitor);
    }

    private IQueryable<Incident> IncidentsInPeriod(Guid userId, int? days, DateTime now)
    {
        if (days is < 1 or > 3650)
            throw new ValidationException("Days must be between 1 and 3650.");

        var query = db.Incidents
            .AsNoTracking()
            .Include(i => i.Monitor)
            .Where(i => i.Monitor.UserId == userId);

        if (days is not null)
        {
            var periodStart = now.AddDays(-days.Value);
            query = query.Where(i => i.ResolvedAt == null || i.ResolvedAt >= periodStart);
        }

        return query;
    }
}
