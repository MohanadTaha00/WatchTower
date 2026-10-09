namespace WatchTower.Application.Options;

public class TwoFactorOptions
{
    public const string SectionName = "TwoFactor";

    /// <summary>Account name prefix shown in authenticator apps.</summary>
    public string Issuer { get; set; } = "WatchTower";

    /// <summary>How long the second login step (and an emailed code) stays valid.</summary>
    public int ChallengeLifetimeMinutes { get; set; } = 10;

    /// <summary>Wrong codes allowed per challenge before the user has to log in again.</summary>
    public int MaxAttempts { get; set; } = 5;

    public int ResendCooldownSeconds { get; set; } = 30;
    public int RecoveryCodeCount { get; set; } = 10;
}
