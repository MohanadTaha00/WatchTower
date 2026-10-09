using WatchTower.Application.DTOs;

namespace WatchTower.Application.Interfaces;

public interface IIncidentService
{
    Task<IncidentListResponse> ListAsync(
        Guid userId,
        string? status,
        Guid? monitorId,
        int? days,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<IncidentSummaryDto> GetSummaryAsync(
        Guid userId,
        int? days,
        CancellationToken cancellationToken = default);
}
