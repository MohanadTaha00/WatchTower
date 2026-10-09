using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WatchTower.Api.Services;
using WatchTower.Application.DTOs;
using WatchTower.Application.Interfaces;

namespace WatchTower.Api.Controllers;

[ApiController]
[EnableRateLimiting("auth")]
[Route("api/auth")]
public class AuthController(
    IAuthService authService,
    IPasswordService passwordService,
    ITwoFactorService twoFactorService,
    ICurrentUserService currentUser) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var response = await authService.RegisterAsync(request, cancellationToken);
        return Created(string.Empty, response);
    }

    /// <summary>Returns a token, or a two-factor challenge when the account has a second step turned on.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TwoFactorChallengeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var outcome = await authService.LoginAsync(request, cancellationToken);
        return outcome.Session is not null ? Ok(outcome.Session) : Ok(outcome.Challenge);
    }

    [HttpPost("login/2fa")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyTwoFactor([FromBody] VerifyTwoFactorRequest request, CancellationToken cancellationToken) =>
        Ok(await twoFactorService.VerifyLoginAsync(request, cancellationToken));

    [HttpPost("login/2fa/resend")]
    [ProducesResponseType(typeof(TwoFactorChallengeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendTwoFactorCode([FromBody] ResendTwoFactorCodeRequest request, CancellationToken cancellationToken) =>
        Ok(await twoFactorService.ResendLoginCodeAsync(request, cancellationToken));

    [Authorize]
    [HttpPost("change-password")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken) =>
        Ok(await passwordService.ChangePasswordAsync(currentUser.GetUserId(), request, cancellationToken));

    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await passwordService.RequestResetAsync(request, cancellationToken);
        return Accepted();
    }

    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await passwordService.ResetPasswordAsync(request, cancellationToken);
        return NoContent();
    }
}
