namespace WatchTower.Application.Options;

public class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    public bool RequireHttpsUrls { get; set; } = true;
    public bool BlockPrivateAddresses { get; set; } = true;
    public int MinIntervalSeconds { get; set; } = 30;
    public int MaxIntervalSeconds { get; set; } = 86400;
}
