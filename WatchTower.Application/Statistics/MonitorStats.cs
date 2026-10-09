using WatchTower.Application.DTOs;
using WatchTower.Application.Exceptions;

namespace WatchTower.Application.Statistics;

public sealed record CheckSample(DateTime CheckedAt, bool IsUp, int ResponseTimeMs);

public sealed record StatsRange(string Key, TimeSpan Length, TimeSpan Bucket)
{
    public static readonly StatsRange Day = new("24h", TimeSpan.FromHours(24), TimeSpan.FromMinutes(30));
    public static readonly StatsRange Week = new("7d", TimeSpan.FromDays(7), TimeSpan.FromHours(3));
    public static readonly StatsRange Month = new("30d", TimeSpan.FromDays(30), TimeSpan.FromHours(12));

    public static IReadOnlyList<StatsRange> All { get; } = [Day, Week, Month];

    public int BucketCount => (int)(Length / Bucket);

    public static StatsRange Parse(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Day;
        return All.FirstOrDefault(r => string.Equals(r.Key, key.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ValidationException($"Range must be one of: {string.Join(", ", All.Select(r => r.Key))}.");
    }
}

public static class MonitorStats
{
    /// <summary>
    /// Start of the first bucket. Buckets are aligned to whole multiples of the bucket size (UTC), so the
    /// same check always lands in the same bucket between refreshes; the last bucket contains <paramref name="now"/>.
    /// </summary>
    public static DateTime RangeStart(StatsRange range, DateTime now)
    {
        var currentBucket = new DateTime(now.Ticks - now.Ticks % range.Bucket.Ticks, DateTimeKind.Utc);
        return currentBucket - range.Bucket * (range.BucketCount - 1);
    }

    public static IReadOnlyList<StatsBucketDto> BuildBuckets(StatsRange range, DateTime from, IEnumerable<CheckSample> samples)
    {
        var groups = samples
            .Where(s => s.CheckedAt >= from)
            .GroupBy(s => (int)((s.CheckedAt - from).Ticks / range.Bucket.Ticks))
            .Where(g => g.Key < range.BucketCount)
            .ToDictionary(g => g.Key, g => g.ToList());

        var buckets = new List<StatsBucketDto>(range.BucketCount);
        for (var i = 0; i < range.BucketCount; i++)
        {
            var start = DateTime.SpecifyKind(from + range.Bucket * i, DateTimeKind.Utc);
            if (!groups.TryGetValue(i, out var checks))
            {
                buckets.Add(new StatsBucketDto(start, 0, 0, null, null));
                continue;
            }

            var upTimes = checks.Where(c => c.IsUp).Select(c => c.ResponseTimeMs).ToList();
            buckets.Add(new StatsBucketDto(
                start,
                checks.Count,
                upTimes.Count,
                upTimes.Count > 0 ? (int)Math.Round(upTimes.Average()) : null,
                upTimes.Count > 0 ? upTimes.Max() : null));
        }

        return buckets;
    }

    public static ResponseTimeSummaryDto SummarizeResponseTimes(IEnumerable<CheckSample> samples)
    {
        var times = samples.Where(s => s.IsUp).Select(s => s.ResponseTimeMs).Order().ToList();
        if (times.Count == 0)
            return new ResponseTimeSummaryDto(null, null, null, null);

        var p95Index = (int)Math.Ceiling(times.Count * 0.95) - 1;
        return new ResponseTimeSummaryDto(
            (int)Math.Round(times.Average()),
            times[0],
            times[^1],
            times[p95Index]);
    }

    public static double? UptimePercent(int upChecks, int totalChecks) =>
        totalChecks == 0 ? null : Math.Round(upChecks * 100.0 / totalChecks, 3);
}
