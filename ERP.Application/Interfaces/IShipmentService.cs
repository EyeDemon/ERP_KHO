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
        StageShipmentDto request,
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
    Task<ShipmentDto> DispatchAsync(
        int id,
        ShipmentStateCommandDto request,
        CancellationToken cancellationToken = default);
    Task<ShipmentTrackingDto> GetTrackingAsync(int id, CancellationToken cancellationToken = default);
    Task<ShipmentDto> MarkInTransitAsync(int id, MarkShipmentInTransitDto request, CancellationToken cancellationToken = default);
    Task<ShipmentDto> ConfirmDeliveryAsync(int id, ConfirmShipmentDeliveryDto request, CancellationToken cancellationToken = default);
    Task<ShipmentDto> FailDeliveryAsync(int id, FailShipmentDeliveryDto request, CancellationToken cancellationToken = default);
    Task<ShipmentDto> RetryDeliveryAsync(int id, RetryShipmentDeliveryDto request, CancellationToken cancellationToken = default);
    Task<ShipmentDto> InitiateReturnAsync(int id, InitiateShipmentReturnDto request, CancellationToken cancellationToken = default);
    Task<ShipmentDto> CompleteAsync(int id, ShipmentStateCommandDto request, CancellationToken cancellationToken = default);
}

public interface IShipmentIntegration
{
    Task EnsureForPackingSessionAsync(
        int packingSessionId,
        int actorId,
        CancellationToken cancellationToken = default);
}

public interface IShipmentDispatchReadiness
{
    Task EnsureSourceReadyAsync(
        string sourceType,
        int sourceId,
        CancellationToken cancellationToken = default);
}
