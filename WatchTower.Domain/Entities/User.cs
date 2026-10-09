namespace WatchTower.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }

    /// <summary>
    /// Embedded in every JWT. Rotating it (on password change or reset) invalidates all existing sessions.
    /// </summary>
    public string SecurityStamp { get; set; } = string.Empty;

    /// <summary>Second login step, or null when two-factor authentication is off.</summary>
    public TwoFactorMethod? TwoFactorMethod { get; set; }

    /// <summary>TOTP secret, encrypted with ASP.NET Data Protection. Set during setup, used once the method is Authenticator.</summary>
    public string? AuthenticatorKey { get; set; }

    /// <summary>Time step of the last accepted authenticator code, so a code cannot be used twice.</summary>
    public long? LastTotpStep { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<UptimeMonitor> Monitors { get; set; } = [];
}
