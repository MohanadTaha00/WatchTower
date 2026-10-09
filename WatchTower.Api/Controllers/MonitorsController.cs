using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WatchTower.Application.DTOs;
using WatchTower.Application.Interfaces;
using WatchTower.Api.Services;

namespace WatchTower.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/monitors")]
public class MonitorsController(IMonitorService monitorService, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(MonitorListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId();
        return Ok(await monitorService.ListAsync(userId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(MonitorDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId();
        return Ok(await monitorService.GetAsync(userId, id, cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType(typeof(MonitorDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] MonitorRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId();
        var monitor = await monitorService.CreateAsync(userId, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = monitor.Id }, monitor);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(MonitorDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] MonitorRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId();
        return Ok(await monitorService.UpdateAsync(userId, id, request, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId();
        await monitorService.DeleteAsync(userId, id, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/checks")]
    [ProducesResponseType(typeof(CheckResultListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChecks(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId();
        return Ok(await monitorService.GetChecksAsync(userId, id, page, pageSize, cancellationToken));
    }

    [HttpGet("{id:guid}/stats")]
    [ProducesResponseType(typeof(MonitorStatsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(
        Guid id,
        [FromQuery] string? range = "24h",
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId();
        return Ok(await monitorService.GetStatsAsync(userId, id, range, cancellationToken));
    }
}
