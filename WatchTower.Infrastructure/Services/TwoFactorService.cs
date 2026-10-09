using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QRCoder;
using WatchTower.Application.DTOs;
using WatchTower.Application.Exceptions;
using WatchTower.Application.Interfaces;
using WatchTower.Application.Notifications;
using WatchTower.Application.Options;
using WatchTower.Domain.Entities;
using WatchTower.Infrastructure.Persistence;

namespace WatchTower.Infrastructure.Services;

public class TwoFactorService(
    AppDbContext db,
    IJwtTokenService jwtTokenService,
    IEmailSender emailSender,
    IDataProtectionProvider dataProtectionProvider,
    IOptions<TwoFactorOptions> options,
    IOptions<NotificationOptions> notificationOptions,
    IOptions<SmtpOptions> smtpOptions,
    ILogger<TwoFactorService> logger) : ITwoFactorService
{
    private const string ExpiredLoginMessage = "This sign-in attempt has expired. Log in again.";
    private const string RecoveryAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    private readonly TwoFactorOptions _options = options.Value;
    private readonly IDataProtector _keyProtector = dataProtectionProvider.CreateProtector("WatchTower.TwoFactor.AuthenticatorKey");
    private readonly PasswordHasher<User> _passwordHasher = new();

    public async Task<TwoFactorStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(userId, cancellationToken);
        var left = await db.RecoveryCodes.CountAsync(c => c.UserId == userId && c.UsedAt == null, cancellationToken);
        return new TwoFactorStatusDto(user.TwoFactorMethod is not null, user.TwoFactorMethod?.ToString(), left);
    }

    // ---------- Setup ----------

    public async Task<AuthenticatorSetupResponse> BeginAuthenticatorSetupAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(userId, cancellationToken);
        EnsureOff(user);

        var secret = Totp.ToBase32(Totp.GenerateKey());
        user.AuthenticatorKey = _keyProtector.Protect(secret);
        user.LastTotpStep = null;
        await db.SaveChangesAsync(cancellationToken);

        var issuer = Uri.EscapeDataString(_options.Issuer);
        var uri = $"otpauth://totp/{issuer}:{Uri.EscapeDataString(user.Email)}?secret={secret}&issuer={issuer}&digits=6&period={Totp.StepSeconds}";

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(6);

        var grouped = string.Join(' ', secret.Chunk(4).Select(c => new string(c))).ToLowerInvariant();
        return new AuthenticatorSetupResponse(grouped, uri, $"data:image/png;base64,{Convert.ToBase64String(png)}");
    }

    public async Task<RecoveryCodesResponse> EnableAuthenticatorAsync(Guid userId, EnableTwoFactorRequest request, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(userId, cancellationToken);
        EnsureOff(user);
        if (user.AuthenticatorKey is null)
            throw new ValidationException("Start the authenticator setup first.");

        var step = VerifyAuthenticatorCode(user, NormalizeCode(request.Code))
            ?? throw new ValidationException("That code is not correct. Make sure your phone's clock is right and enter the newest code.");

        user.LastTotpStep = step;
        user.TwoFactorMethod = TwoFactorMethod.Authenticator;
        var codes = await ReplaceRecoveryCodesAsync(user, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return codes;
    }

    public async Task<EmailSetupResponse> BeginEmailSetupAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        EnsureEmailConfigured();
        var user = await GetUserAsync(userId, cancellationToken);
        EnsureOff(user);

        var now = DateTime.UtcNow;
        var pending = await db.TwoFactorChallenges
            .Where(c => c.UserId == userId && c.Purpose == TwoFactorPurpose.EnableEmail && c.ConsumedAt == null)
            .ToListAsync(cancellationToken);
        if (pending.Any(c => c.CodeSentAt > now.AddSeconds(-_options.ResendCooldownSeconds)))
            throw new ValidationException("A code was just sent. Wait a few seconds before asking for another one.");
        foreach (var old in pending)
            old.ConsumedAt = now;

        var code = NewEmailCode();
        db.TwoFactorChallenges.Add(new TwoFactorChallenge
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Purpose = TwoFactorPurpose.EnableEmail,
            Method = TwoFactorMethod.Email,
            CodeHash = SecureTokens.Hash(code),
            CreatedAt = now,
            CodeSentAt = now,
            ExpiresAt = now.AddMinutes(_options.ChallengeLifetimeMinutes)
        });
        await db.SaveChangesAsync(cancellationToken);

        var content = TwoFactorEmailComposer.ComposeSetupCode(code, _options.ChallengeLifetimeMinutes);
        await SendOrThrowAsync(user.Email, content, "The confirmation code could not be emailed. Try again in a few minutes.", cancellationToken);

        return new EmailSetupResponse(MaskEmail(user.Email), _options.ChallengeLifetimeMinutes * 60);
    }

    public async Task<RecoveryCodesResponse> EnableEmailAsync(Guid userId, EnableTwoFactorRequest request, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(userId, cancellationToken);
        EnsureOff(user);

        var now = DateTime.UtcNow;
        var challenge = await db.TwoFactorChallenges
            .Where(c => c.UserId == userId && c.Purpose == TwoFactorPurpose.EnableEmail && c.ConsumedAt == null && c.ExpiresAt > now)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException("The code has expired. Send a new one.");

        if (!SecureTokens.HashMatches(NormalizeCode(request.Code), challenge.CodeHash))
            await RejectAsync(challenge, "Send a new code.", cancellationToken);

        challenge.ConsumedAt = now;
        user.TwoFactorMethod = TwoFactorMethod.Email;
        user.AuthenticatorKey = null;
        var codes = await ReplaceRecoveryCodesAsync(user, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return codes;
    }

    public async Task DisableAsync(Guid userId, PasswordConfirmationRequest request, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(userId, cancellationToken);
        VerifyPassword(user, request.Password);
        if (user.TwoFactorMethod is null)
            return;

        user.TwoFactorMethod = null;
        user.AuthenticatorKey = null;
        user.LastTotpStep = null;
        db.RecoveryCodes.RemoveRange(await db.RecoveryCodes.Where(c => c.UserId == userId).ToListAsync(cancellationToken));
        var now = DateTime.UtcNow;
        foreach (var challenge in await db.TwoFactorChallenges.Where(c => c.UserId == userId && c.ConsumedAt == null).ToListAsync(cancellationToken))
            challenge.ConsumedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<RecoveryCodesResponse> RegenerateRecoveryCodesAsync(Guid userId, PasswordConfirmationRequest request, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(userId, cancellationToken);
        VerifyPassword(user, request.Password);
        if (user.TwoFactorMethod is null)
            throw new ValidationException("Turn on two-factor authentication first.");

        var codes = await ReplaceRecoveryCodesAsync(user, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return codes;
    }

    // ---------- Login ----------

    /// <summary>Starts the second login step for a user whose password was just verified.</summary>
    public async Task<TwoFactorChallengeResponse> CreateLoginChallengeAsync(User user, CancellationToken cancellationToken = default)
    {
        var method = user.TwoFactorMethod ?? throw new InvalidOperationException("Two-factor authentication is off for this user.");
        var now = DateTime.UtcNow;
        var token = SecureTokens.NewToken();
        var challenge = new TwoFactorChallenge
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Purpose = TwoFactorPurpose.Login,
            Method = method,
            TokenHash = SecureTokens.Hash(token),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_options.ChallengeLifetimeMinutes)
        };
        db.TwoFactorChallenges.Add(challenge);

        string? code = null;
        if (method == TwoFactorMethod.Email)
        {
            code = NewEmailCode();
            challenge.CodeHash = SecureTokens.Hash(code);
            challenge.CodeSentAt = now;
        }
        await db.SaveChangesAsync(cancellationToken);

        var sent = code is null || await TrySendLoginCodeAsync(user, code, cancellationToken);
        return ChallengeResponse(token, challenge, user, sent);
    }

    public async Task<LoginResponse> VerifyLoginAsync(VerifyTwoFactorRequest request, CancellationToken cancellationToken = default)
    {
        var challenge = await FindLoginChallengeAsync(request.ChallengeToken, cancellationToken);
        var user = challenge.User;
        var code = NormalizeCode(request.Code);

        var accepted = false;
        if (code.Length == 6 && code.All(char.IsAsciiDigit))
        {
            if (challenge.Method == TwoFactorMethod.Authenticator && VerifyAuthenticatorCode(user, code) is { } step)
            {
                user.LastTotpStep = step;
                accepted = true;
            }
            else if (challenge.Method == TwoFactorMethod.Email)
            {
                accepted = SecureTokens.HashMatches(code, challenge.CodeHash);
            }
        }
        else if (code.Length > 0)
        {
            var hash = SecureTokens.Hash(code.ToLowerInvariant());
            var recovery = await db.RecoveryCodes.FirstOrDefaultAsync(
                c => c.UserId == user.Id && c.CodeHash == hash && c.UsedAt == null, cancellationToken);
            if (recovery is not null)
            {
                recovery.UsedAt = DateTime.UtcNow;
                accepted = true;
            }
        }

        if (!accepted)
        {
            var reused = challenge.Method == TwoFactorMethod.Authenticator && IsReusedAuthenticatorCode(user, code);
            await RejectAsync(challenge, "Log in again.", cancellationToken,
                reused ? "That code was already used. Wait for the next code in your app." : null);
        }

        challenge.ConsumedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var (token, expiresIn) = jwtTokenService.CreateToken(user);
        return new LoginResponse(token, expiresIn, "Bearer");
    }

    public async Task<TwoFactorChallengeResponse> ResendLoginCodeAsync(ResendTwoFactorCodeRequest request, CancellationToken cancellationToken = default)
    {
        var challenge = await FindLoginChallengeAsync(request.ChallengeToken, cancellationToken);
        if (challenge.Method != TwoFactorMethod.Email)
            throw new ValidationException("Codes come from your authenticator app, so there is nothing to resend.");

        var now = DateTime.UtcNow;
        if (challenge.CodeSentAt > now.AddSeconds(-_options.ResendCooldownSeconds))
            throw new ValidationException("A code was just sent. Wait a few seconds before asking for another one.");

        var code = NewEmailCode();
        challenge.CodeHash = SecureTokens.Hash(code);
        challenge.CodeSentAt = now;
        await db.SaveChangesAsync(cancellationToken);

        var content = TwoFactorEmailComposer.ComposeLoginCode(code, _options.ChallengeLifetimeMinutes);
        await SendOrThrowAsync(challenge.User.Email, content, "The sign-in code could not be emailed. Try again in a few minutes, or use a recovery code.", cancellationToken);
        return ChallengeResponse(request.ChallengeToken.Trim(), challenge, challenge.User, sent: true);
    }

    // ---------- Helpers ----------

    private async Task<User> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
        ?? throw new NotFoundException("User not found.");

    private async Task<TwoFactorChallenge> FindLoginChallengeAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ValidationException(ExpiredLoginMessage);

        var hash = SecureTokens.Hash(token.Trim());
        var challenge = await db.TwoFactorChallenges
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.TokenHash == hash && c.Purpose == TwoFactorPurpose.Login, cancellationToken);

        if (challenge is null || challenge.ConsumedAt is not null || challenge.ExpiresAt <= DateTime.UtcNow
            || challenge.User.TwoFactorMethod != challenge.Method)
            throw new ValidationException(ExpiredLoginMessage);
        return challenge;
    }

    /// <summary>Counts a wrong code and always throws. The challenge is burned after too many attempts.</summary>
    private async Task RejectAsync(TwoFactorChallenge challenge, string startOver, CancellationToken cancellationToken, string? reason = null)
    {
        challenge.Attempts++;
        var exhausted = challenge.Attempts >= _options.MaxAttempts;
        if (exhausted)
            challenge.ConsumedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        throw new ValidationException(exhausted
            ? $"Too many incorrect codes. {startOver}"
            : reason ?? "That code is not correct.");
    }

    private bool IsReusedAuthenticatorCode(User user, string code) =>
        user.LastTotpStep is not null && user.AuthenticatorKey is not null && code.Length == 6 && code.All(char.IsAsciiDigit) &&
        Totp.Verify(Totp.FromBase32(_keyProtector.Unprotect(user.AuthenticatorKey)), code, DateTimeOffset.UtcNow, null) is not null;

    private long? VerifyAuthenticatorCode(User user, string code)
    {
        if (user.AuthenticatorKey is null || code.Length != 6 || !code.All(char.IsAsciiDigit))
            return null;
        var key = Totp.FromBase32(_keyProtector.Unprotect(user.AuthenticatorKey));
        return Totp.Verify(key, code, DateTimeOffset.UtcNow, user.LastTotpStep);
    }

    private async Task<RecoveryCodesResponse> ReplaceRecoveryCodesAsync(User user, CancellationToken cancellationToken)
    {
        db.RecoveryCodes.RemoveRange(await db.RecoveryCodes.Where(c => c.UserId == user.Id).ToListAsync(cancellationToken));

        var now = DateTime.UtcNow;
        var codes = new List<string>(_options.RecoveryCodeCount);
        for (var i = 0; i < _options.RecoveryCodeCount; i++)
        {
            var raw = RandomNumberGenerator.GetString(RecoveryAlphabet, 10);
            codes.Add($"{raw[..5]}-{raw[5..]}");
            db.RecoveryCodes.Add(new RecoveryCode
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                CodeHash = SecureTokens.Hash(raw),
                CreatedAt = now
            });
        }
        return new RecoveryCodesResponse(codes);
    }

    private void VerifyPassword(User user, string? password)
    {
        if (_passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password ?? string.Empty) == PasswordVerificationResult.Failed)
            throw new ValidationException("Your password is incorrect.");
    }

    private static void EnsureOff(User user)
    {
        if (user.TwoFactorMethod is not null)
            throw new ValidationException("Two-factor authentication is already on. Turn it off first to switch methods.");
    }

    private void EnsureEmailConfigured()
    {
        if (!notificationOptions.Value.Enabled || string.IsNullOrWhiteSpace(smtpOptions.Value.Host))
            throw new ValidationException("Email codes are unavailable because email sending is not configured.");
    }

    private async Task<bool> TrySendLoginCodeAsync(User user, string code, CancellationToken cancellationToken)
    {
        try
        {
            var content = TwoFactorEmailComposer.ComposeLoginCode(code, _options.ChallengeLifetimeMinutes);
            await emailSender.SendAsync(new EmailMessage(user.Email, content.Subject, content.TextBody, content.HtmlBody), cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not email a sign-in code to user {UserId}", user.Id);
            return false;
        }
    }

    private async Task SendOrThrowAsync(string to, EmailContent content, string failureMessage, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(new EmailMessage(to, content.Subject, content.TextBody, content.HtmlBody), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not send two-factor email");
            throw new EmailDeliveryException(failureMessage, ex);
        }
    }

    private TwoFactorChallengeResponse ChallengeResponse(string token, TwoFactorChallenge challenge, User user, bool sent) => new(
        RequiresTwoFactor: true,
        ChallengeToken: token,
        Method: challenge.Method.ToString(),
        SentTo: challenge.Method == TwoFactorMethod.Email && sent ? MaskEmail(user.Email) : null,
        ExpiresIn: (int)Math.Max(0, (challenge.ExpiresAt - DateTime.UtcNow).TotalSeconds));

    private static string NewEmailCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static string NormalizeCode(string? code) =>
        new((code ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? email : $"{email[0]}•••{email[(at - 1)..]}";
    }
}
