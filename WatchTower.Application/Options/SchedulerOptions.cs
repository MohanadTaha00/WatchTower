namespace WatchTower.Application.Options;

public class SchedulerOptions
{
    public const string SectionName = "Scheduler";

    public int PollIntervalSeconds { get; set; } = 5;
    public int MaxConcurrentChecks { get; set; } = 10;
}
