using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public interface IPackingService
{
    Task<IReadOnlyList<PackingSessionListDto>> ListAsync(
        int? warehouseId = null,
        string? status = null,
        CancellationToken cancellationToken = default);
    Task<PackingSessionDto> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<PackingSessionDto> CreateAsync(CreatePackingSessionDto request, CancellationToken cancellationToken = default);
    Task<PackingSessionDto> CreateHandlingUnitAsync(
        int sessionId,
        CreateHandlingUnitDto request,
        CancellationToken cancellationToken = default);
    Task<PackingSessionDto> PackAsync(
        int sessionId,
        PackIntoHandlingUnitDto request,
        CancellationToken cancellationToken = default);
    Task<PackingSessionDto> CloseHandlingUnitAsync(
        int sessionId,
        int handlingUnitId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken = default);
    Task<PackingSessionDto> CancelHandlingUnitAsync(
        int sessionId,
        int handlingUnitId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken = default);
    Task<PackingSessionDto> NestHandlingUnitAsync(
        int sessionId,
        int handlingUnitId,
        NestHandlingUnitDto request,
        CancellationToken cancellationToken = default);
    Task<PackingSessionDto> UnnestHandlingUnitAsync(
        int sessionId,
        int handlingUnitId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken = default);
    Task<PackingSessionDto> CompleteAsync(
        int sessionId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken = default);
    Task<PackingSessionDto> CloseAsync(
        int sessionId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken = default);
    Task<PackingSessionDto> CancelAsync(
        int sessionId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HandlingUnitDto>> ListHandlingUnitsAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default);
    Task<HandlingUnitDto> GetHandlingUnitAsync(int id, CancellationToken cancellationToken = default);
}

public interface IPackingSessionIntegration
{
    Task EnsureForPickingTaskAsync(
        int pickingTaskId,
        int actorId,
        CancellationToken cancellationToken = default);
}

public interface IPackingDispatchReadiness
{
    Task EnsureSourceReadyAsync(
        string sourceType,
        int sourceId,
        CancellationToken cancellationToken = default);
}
