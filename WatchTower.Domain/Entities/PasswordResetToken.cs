namespace WatchTower.Domain.Entities;

/// <summary>
/// A single-use "forgot password" link. Only the SHA-256 hash of the token is stored;
/// the token itself exists only in the email sent to the user.
/// </summary>
public class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }

    public User User { get; set; } = null!;
}
