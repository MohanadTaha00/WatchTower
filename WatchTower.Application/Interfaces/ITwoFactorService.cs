using WatchTower.Application.DTOs;

namespace WatchTower.Application.Interfaces;

public interface ITwoFactorService
{
    Task<TwoFactorStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Creates a new authenticator secret. Two-factor stays off until <see cref="EnableAuthenticatorAsync"/> confirms a code.</summary>
    Task<AuthenticatorSetupResponse> BeginAuthenticatorSetupAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<RecoveryCodesResponse> EnableAuthenticatorAsync(Guid userId, EnableTwoFactorRequest request, CancellationToken cancellationToken = default);

    /// <summary>Emails a code. Two-factor stays off until <see cref="EnableEmailAsync"/> confirms it.</summary>
    Task<EmailSetupResponse> BeginEmailSetupAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<RecoveryCodesResponse> EnableEmailAsync(Guid userId, EnableTwoFactorRequest request, CancellationToken cancellationToken = default);

    Task DisableAsync(Guid userId, PasswordConfirmationRequest request, CancellationToken cancellationToken = default);
    Task<RecoveryCodesResponse> RegenerateRecoveryCodesAsync(Guid userId, PasswordConfirmationRequest request, CancellationToken cancellationToken = default);

    Task<LoginResponse> VerifyLoginAsync(VerifyTwoFactorRequest request, CancellationToken cancellationToken = default);
    Task<TwoFactorChallengeResponse> ResendLoginCodeAsync(ResendTwoFactorCodeRequest request, CancellationToken cancellationToken = default);
}
