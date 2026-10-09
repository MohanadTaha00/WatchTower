using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WatchTower.Application.DTOs;
using WatchTower.Application.Interfaces;
using WatchTower.Api.Services;

namespace WatchTower.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/incidents")]
public class IncidentsController(IIncidentService incidentService, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IncidentListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] Guid? monitorId,
        [FromQuery] int? days,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId();
        return Ok(await incidentService.ListAsync(userId, status, monitorId, days, page, pageSize, cancellationToken));
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(IncidentSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary([FromQuery] int? days, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId();
        return Ok(await incidentService.GetSummaryAsync(userId, days, cancellationToken));
    }
}
