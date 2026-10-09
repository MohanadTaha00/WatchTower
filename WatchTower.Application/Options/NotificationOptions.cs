namespace WatchTower.Application.Options;

public class NotificationOptions
{
    public const string SectionName = "Notifications";

    public bool Enabled { get; set; } = true;

    /// <summary>Public address of the dashboard, used for links in emails.</summary>
    public string DashboardUrl { get; set; } = "http://localhost:5073/";

    public int DispatchIntervalSeconds { get; set; } = 5;
    public int MaxAttempts { get; set; } = 6;
}
