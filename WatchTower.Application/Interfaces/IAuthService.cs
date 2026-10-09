using WatchTower.Application.DTOs;

namespace WatchTower.Application.Interfaces;

public interface IAuthService
{
    Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<LoginOutcome> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
}
