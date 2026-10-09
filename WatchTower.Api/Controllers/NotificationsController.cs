using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WatchTower.Api.Services;
using WatchTower.Application.Interfaces;

namespace WatchTower.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationsController(INotificationService notificationService, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet("settings")]
    [ProducesResponseType(typeof(NotificationSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Settings(CancellationToken cancellationToken) =>
        Ok(await notificationService.GetSettingsAsync(currentUser.GetUserId(), cancellationToken));

    [HttpPost("test")]
    [ProducesResponseType(typeof(TestEmailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SendTest(CancellationToken cancellationToken) =>
        Ok(await notificationService.SendTestEmailAsync(currentUser.GetUserId(), cancellationToken));
}
