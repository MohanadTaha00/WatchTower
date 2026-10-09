using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WatchTower.Application.DTOs;
using WatchTower.Application.Exceptions;
using WatchTower.Application.Interfaces;
using WatchTower.Domain.Entities;
using WatchTower.Infrastructure.Persistence;

namespace WatchTower.Infrastructure.Services;

public class AuthService(
    AppDbContext db,
    IJwtTokenService jwtTokenService,
    TwoFactorService twoFactorService) : IAuthService
{
    private readonly PasswordHasher<User> _passwordHasher = new();

    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        ValidateCredentials(email, request.Password);

        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
            throw new DuplicateEmailException(email);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = string.Empty,
            SecurityStamp = PasswordService.NewSecurityStamp(),
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return new RegisterResponse(user.Id, user.Email);
    }

    public async Task<LoginOutcome> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null)
            throw new InvalidCredentialsException();

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
            throw new InvalidCredentialsException();

        if (user.TwoFactorMethod is not null)
            return new LoginOutcome(null, await twoFactorService.CreateLoginChallengeAsync(user, cancellationToken));

        var (token, expiresIn) = jwtTokenService.CreateToken(user);
        return new LoginOutcome(new LoginResponse(token, expiresIn, "Bearer"), null);
    }

    private static void ValidateCredentials(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw new ValidationException("A valid email address is required.");

        PasswordPolicy.Validate(password);
    }
}
