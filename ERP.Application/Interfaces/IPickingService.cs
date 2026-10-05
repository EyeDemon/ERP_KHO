using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public interface IPickingService
{
    Task<IReadOnlyList<PickingTaskListDto>> ListAsync(int? warehouseId = null, string? status = null, CancellationToken cancellationToken = default);
    Task<PickingTaskDto> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<PickingTaskDto> AssignAsync(int id, PickingStateCommandDto request, CancellationToken cancellationToken = default);
    Task<PickingTaskDto> StartAsync(int id, PickingStateCommandDto request, CancellationToken cancellationToken = default);
    Task<PickingTaskDto> PickAsync(int id, PickScanDto request, CancellationToken cancellationToken = default);
    Task<PickingTaskDto> ReportShortPickAsync(int id, ReportShortPickDto request, CancellationToken cancellationToken = default);
    Task<PickingTaskDto> ResolveShortPickAsync(int taskId, int exceptionId, ResolveShortPickDto request, CancellationToken cancellationToken = default);
    Task<PickingTaskDto> OverrideShortPickAsync(int taskId, int exceptionId, OverrideShortPickDto request, CancellationToken cancellationToken = default);
    Task<PickingTaskDto> CompleteAsync(int id, PickingStateCommandDto request, CancellationToken cancellationToken = default);
}

public interface IPickingTaskIntegration
{
    Task EnsureForReservationAsync(int reservationId, int actorId, CancellationToken cancellationToken = default);
    Task PrepareReservationReleaseAsync(
        int reservationId,
        int actorId,
        string reason,
        bool sourceWide,
        CancellationToken cancellationToken = default);
}

public interface IPickingDispatchReadiness
{
    Task EnsureSourceReadyAsync(string sourceType, int sourceId, CancellationToken cancellationToken = default);
}
