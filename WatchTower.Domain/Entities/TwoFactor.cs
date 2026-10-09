namespace WatchTower.Domain.Entities;

public enum TwoFactorMethod
{
    Authenticator,
    Email
}

public enum TwoFactorPurpose
{
    /// <summary>Second step of a login, identified by the challenge token returned after the password check.</summary>
    Login,

    /// <summary>Confirms the user can receive email codes before email two-factor is turned on.</summary>
    EnableEmail
}

/// <summary>
/// A pending second-factor check. Tokens and email codes are stored as SHA-256 hashes only.
/// </summary>
public class TwoFactorChallenge
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public TwoFactorPurpose Purpose { get; set; }
    public TwoFactorMethod Method { get; set; }
    public string? TokenHash { get; set; }
    public string? CodeHash { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CodeSentAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }

    public User User { get; set; } = null!;
}

/// <summary>A single-use backup code for logging in without the second factor. Stored as a SHA-256 hash.</summary>
public class RecoveryCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string CodeHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UsedAt { get; set; }

    public User User { get; set; } = null!;
}
