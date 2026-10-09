using WatchTower.Application.DTOs;

namespace WatchTower.Application.Interfaces;

public interface IPasswordService
{
    /// <summary>Changes the password and returns a fresh token; all other sessions are signed out.</summary>
    Task<LoginResponse> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);

    /// <summary>Emails a reset link if the account exists. Never reveals whether it does.</summary>
    Task RequestResetAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);

    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
}
