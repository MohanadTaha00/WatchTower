using WatchTower.Domain.Entities;

namespace WatchTower.Application.Interfaces;

public interface IJwtTokenService
{
    (string Token, int ExpiresInSeconds) CreateToken(User user);
}
