using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public interface IShipmentService
{
    Task<IReadOnlyList<ShipmentListDto>> ListAsync(
        int? warehouseId = null,
        string? status = null,
        CancellationToken cancellationToken = default);
    Task<ShipmentDto> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<ShipmentDto> StageAsync(
        int id,
        ShipmentStateCommandDto request,
        CancellationToken cancellationToken = default);
    Task<ShipmentDto> StartLoadingAsync(
        int id,
        StartShipmentLoadingDto request,
        CancellationToken cancellationToken = default);
    Task<ShipmentDto> LoadHandlingUnitAsync(
        int id,
        LoadShipmentHandlingUnitDto request,
        CancellationToken cancellationToken = default);
    Task<ShipmentDto> CompleteLoadingAsync(
        int id,
        CompleteShipmentLoadingDto request,
        CancellationToken cancellationToken = default);
}

public interface IShipmentIntegration
{
    Task EnsureForPackingSessionAsync(
        int packingSessionId,
        int actorId,
        CancellationToken cancellationToken = default);
}
