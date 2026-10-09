using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WatchTower.Application.DTOs;
using WatchTower.Application.Exceptions;
using WatchTower.Application.Interfaces;
using WatchTower.Application.Notifications;
using WatchTower.Application.Options;
using WatchTower.Domain.Entities;
using WatchTower.Infrastructure.Persistence;

namespace WatchTower.Infrastructure.Services;

public class PasswordService(
    AppDbContext db,
    IJwtTokenService jwtTokenService,
    IEmailSender emailSender,
    IOptions<PasswordResetOptions> resetOptions,
    IOptions<NotificationOptions> notificationOptions,
    IOptions<SmtpOptions> smtpOptions,
    ILogger<PasswordService> logger) : IPasswordService
{
    private const string InvalidLinkMessage = "This reset link is invalid or has expired. Request a new one.";

    private readonly PasswordHasher<User> _passwordHasher = new();
    private readonly PasswordResetOptions _resetOptions = resetOptions.Value;

    public async Task<LoginResponse> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        if (_passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword ?? string.Empty)
            == PasswordVerificationResult.Failed)
            throw new ValidationException("Your current password is incorrect.");

        PasswordPolicy.Validate(request.NewPassword);
        if (request.NewPassword == request.CurrentPassword)
            throw new ValidationException("Choose a new password that is different from your current one.");

        SetPassword(user, request.NewPassword);
        await db.SaveChangesAsync(cancellationToken);

        var (token, expiresIn) = jwtTokenService.CreateToken(user);
        return new LoginResponse(token, expiresIn, "Bearer");
    }

    public async Task RequestResetAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var smtp = smtpOptions.Value;
        if (!notificationOptions.Value.Enabled || string.IsNullOrWhiteSpace(smtp.Host))
            throw new ValidationException("Password reset by email is unavailable because email sending is not configured.");

        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!email.Contains('@'))
            throw new ValidationException("A valid email address is required.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null)
            return;

        var now = DateTime.UtcNow;
        var cooldownStart = now.AddSeconds(-_resetOptions.ResendCooldownSeconds);
        if (await db.PasswordResetTokens.AnyAsync(t => t.UserId == user.Id && t.CreatedAt > cooldownStart, cancellationToken))
            return;

        var token = SecureTokens.NewToken();
        var resetToken = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = SecureTokens.Hash(token),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_resetOptions.TokenLifetimeMinutes)
        };
        db.PasswordResetTokens.Add(resetToken);
        await db.SaveChangesAsync(cancellationToken);

        var link = $"{notificationOptions.Value.DashboardUrl.TrimEnd('/')}/#/reset-password?token={token}";
        var content = PasswordResetEmailComposer.Compose(link, _resetOptions.TokenLifetimeMinutes);

        try
        {
            await emailSender.SendAsync(new EmailMessage(user.Email, content.Subject, content.TextBody, content.HtmlBody), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not send password reset email for user {UserId}", user.Id);
            db.PasswordResetTokens.Remove(resetToken);
            await db.SaveChangesAsync(CancellationToken.None);
            throw new EmailDeliveryException("The reset email could not be sent right now. Try again in a few minutes.", ex);
        }
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            throw new ValidationException(InvalidLinkMessage);
        PasswordPolicy.Validate(request.NewPassword);

        var tokenHash = SecureTokens.Hash(request.Token.Trim());
        var resetToken = await db.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        var now = DateTime.UtcNow;
        if (resetToken is null || resetToken.UsedAt is not null || resetToken.ExpiresAt <= now)
            throw new ValidationException(InvalidLinkMessage);

        SetPassword(resetToken.User, request.NewPassword);

        var outstanding = await db.PasswordResetTokens
            .Where(t => t.UserId == resetToken.UserId && t.UsedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var t in outstanding)
            t.UsedAt = now;

        await db.SaveChangesAsync(cancellationToken);
    }

    private void SetPassword(User user, string password)
    {
        user.PasswordHash = _passwordHasher.HashPassword(user, password);
        user.SecurityStamp = NewSecurityStamp();
    }

    internal static string NewSecurityStamp() => Guid.NewGuid().ToString("N");
}

internal static class PasswordPolicy
{
    public const int MinLength = 8;

    public static void Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinLength)
            throw new ValidationException($"Password must be at least {MinLength} characters.");
    }
}
