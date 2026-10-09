namespace WatchTower.Application.Options;

public class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    public int TokenLifetimeMinutes { get; set; } = 30;

    /// <summary>Minimum time between reset emails for the same account.</summary>
    public int ResendCooldownSeconds { get; set; } = 60;
}
