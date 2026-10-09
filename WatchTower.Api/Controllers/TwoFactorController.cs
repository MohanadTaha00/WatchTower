using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WatchTower.Api.Services;
using WatchTower.Application.DTOs;
using WatchTower.Application.Interfaces;

namespace WatchTower.Api.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("auth")]
[Route("api/account/2fa")]
public class TwoFactorController(ITwoFactorService twoFactorService, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(TwoFactorStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(CancellationToken cancellationToken) =>
        Ok(await twoFactorService.GetStatusAsync(currentUser.GetUserId(), cancellationToken));

    [HttpPost("authenticator/setup")]
    [ProducesResponseType(typeof(AuthenticatorSetupResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> BeginAuthenticator(CancellationToken cancellationToken) =>
        Ok(await twoFactorService.BeginAuthenticatorSetupAsync(currentUser.GetUserId(), cancellationToken));

    [HttpPost("authenticator/enable")]
    [ProducesResponseType(typeof(RecoveryCodesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> EnableAuthenticator([FromBody] EnableTwoFactorRequest request, CancellationToken cancellationToken) =>
        Ok(await twoFactorService.EnableAuthenticatorAsync(currentUser.GetUserId(), request, cancellationToken));

    [HttpPost("email/setup")]
    [ProducesResponseType(typeof(EmailSetupResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> BeginEmail(CancellationToken cancellationToken) =>
        Ok(await twoFactorService.BeginEmailSetupAsync(currentUser.GetUserId(), cancellationToken));

    [HttpPost("email/enable")]
    [ProducesResponseType(typeof(RecoveryCodesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> EnableEmail([FromBody] EnableTwoFactorRequest request, CancellationToken cancellationToken) =>
        Ok(await twoFactorService.EnableEmailAsync(currentUser.GetUserId(), request, cancellationToken));

    [HttpPost("disable")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Disable([FromBody] PasswordConfirmationRequest request, CancellationToken cancellationToken)
    {
        await twoFactorService.DisableAsync(currentUser.GetUserId(), request, cancellationToken);
        return NoContent();
    }

    [HttpPost("recovery-codes")]
    [ProducesResponseType(typeof(RecoveryCodesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RegenerateRecoveryCodes([FromBody] PasswordConfirmationRequest request, CancellationToken cancellationToken) =>
        Ok(await twoFactorService.RegenerateRecoveryCodesAsync(currentUser.GetUserId(), request, cancellationToken));
}
