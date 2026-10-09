using WatchTower.Application.Exceptions;
using WatchTower.Application.Statistics;

namespace WatchTower.Tests;

public class MonitorStatsTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 10, 47, 12, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, "24h")]
    [InlineData("", "24h")]
    [InlineData("7D", "7d")]
    [InlineData(" 30d ", "30d")]
    public void Parse_accepts_known_ranges(string? key, string expected)
    {
        Assert.Equal(expected, StatsRange.Parse(key).Key);
    }

    [Fact]
    public void Parse_rejects_unknown_range()
    {
        Assert.Throws<ValidationException>(() => StatsRange.Parse("1y"));
    }

    [Fact]
    public void Range_start_is_bucket_aligned_and_last_bucket_contains_now()
    {
        var from = MonitorStats.RangeStart(StatsRange.Day, Now);

        Assert.Equal(new DateTime(2026, 10, 8, 11, 0, 0, DateTimeKind.Utc), from);
        Assert.Equal(48, StatsRange.Day.BucketCount);
        var lastBucketStart = from + StatsRange.Day.Bucket * (StatsRange.Day.BucketCount - 1);
        Assert.InRange(Now, lastBucketStart, lastBucketStart + StatsRange.Day.Bucket);
    }

    [Fact]
    public void Buckets_average_successful_checks_and_count_failures()
    {
        var range = StatsRange.Day;
        var from = MonitorStats.RangeStart(range, Now);
        var samples = new[]
        {
            new CheckSample(from.AddMinutes(1), true, 100),
            new CheckSample(from.AddMinutes(10), true, 302),
            new CheckSample(from.AddMinutes(20), false, 10000),
            new CheckSample(from.AddMinutes(31), false, 10000),
            new CheckSample(from.AddMinutes(-5), true, 999),
        };

        var buckets = MonitorStats.BuildBuckets(range, from, samples);

        Assert.Equal(range.BucketCount, buckets.Count);
        Assert.Equal(from, buckets[0].Start);
        Assert.Equal(3, buckets[0].Checks);
        Assert.Equal(2, buckets[0].UpChecks);
        Assert.Equal(201, buckets[0].AverageResponseMs);
        Assert.Equal(302, buckets[0].MaxResponseMs);

        Assert.Equal(1, buckets[1].Checks);
        Assert.Equal(0, buckets[1].UpChecks);
        Assert.Null(buckets[1].AverageResponseMs);

        Assert.All(buckets.Skip(2), b => Assert.Equal(0, b.Checks));
    }

    [Fact]
    public void Response_time_summary_ignores_failed_checks()
    {
        var samples = Enumerable.Range(1, 20)
            .Select(i => new CheckSample(Now, true, i * 10))
            .Append(new CheckSample(Now, false, 30000))
            .ToList();

        var summary = MonitorStats.SummarizeResponseTimes(samples);

        Assert.Equal(105, summary.AverageMs);
        Assert.Equal(10, summary.MinMs);
        Assert.Equal(200, summary.MaxMs);
        Assert.Equal(190, summary.P95Ms);
    }

    [Fact]
    public void Response_time_summary_is_empty_without_successful_checks()
    {
        var summary = MonitorStats.SummarizeResponseTimes([new CheckSample(Now, false, 5000)]);

        Assert.Null(summary.AverageMs);
        Assert.Null(summary.P95Ms);
    }

    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(3, 3, 100.0)]
    [InlineData(2, 3, 66.667)]
    public void Uptime_percent_handles_empty_and_rounds(int up, int total, double? expected)
    {
        Assert.Equal(expected, MonitorStats.UptimePercent(up, total));
    }
}
