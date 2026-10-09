namespace WatchTower.Application.DTOs;

/// <summary>Returned by login instead of a token when the account has two-factor authentication on.</summary>
public record TwoFactorChallengeResponse(
    bool RequiresTwoFactor,
    string ChallengeToken,
    string Method,
    string? SentTo,
    int ExpiresIn);

/// <summary>Either a session (login finished) or a challenge (a second step is needed).</summary>
public record LoginOutcome(LoginResponse? Session, TwoFactorChallengeResponse? Challenge);

/// <summary>Code is a 6-digit authenticator or email code, or a recovery code.</summary>
public record VerifyTwoFactorRequest(string ChallengeToken, string Code);

public record ResendTwoFactorCodeRequest(string ChallengeToken);

public record TwoFactorStatusDto(bool Enabled, string? Method, int RecoveryCodesLeft);

public record AuthenticatorSetupResponse(string SharedKey, string OtpauthUri, string QrCodeDataUri);

public record EmailSetupResponse(string SentTo, int ExpiresIn);

public record EnableTwoFactorRequest(string Code);

public record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

public record PasswordConfirmationRequest(string Password);
