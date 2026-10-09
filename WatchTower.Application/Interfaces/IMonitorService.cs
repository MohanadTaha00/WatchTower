using WatchTower.Application.DTOs;

namespace WatchTower.Application.Interfaces;

public interface IMonitorService
{
    Task<MonitorListResponse> ListAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<MonitorDto> GetAsync(Guid userId, Guid monitorId, CancellationToken cancellationToken = default);
    Task<MonitorDto> CreateAsync(Guid userId, MonitorRequest request, CancellationToken cancellationToken = default);
    Task<MonitorDto> UpdateAsync(Guid userId, Guid monitorId, MonitorRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid userId, Guid monitorId, CancellationToken cancellationToken = default);
    Task<CheckResultListResponse> GetChecksAsync(
        Guid userId,
        Guid monitorId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<MonitorStatsDto> GetStatsAsync(
        Guid userId,
        Guid monitorId,
        string? range,
        CancellationToken cancellationToken = default);
}
