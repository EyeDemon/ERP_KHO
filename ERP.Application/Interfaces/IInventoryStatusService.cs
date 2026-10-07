using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public interface IInventoryStatusService
{
    Task<IReadOnlyList<InventoryStatusDefinitionDto>> GetStatusesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryStatusBucketDto>> GetBucketsAsync(
        int? warehouseId = null,
        int? productId = null,
        string? status = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryStatusBucketDto>> GetQuarantineAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default);
    Task<InventoryStatusChangeResultDto> ChangeStatusAsync(
        InventoryStatusChangeRequestDto request,
        CancellationToken cancellationToken = default);
    Task<InventoryStatusChangeResultDto> ReleaseQuarantineAsync(
        int stockId,
        QuarantineReleaseRequestDto request,
        CancellationToken cancellationToken = default);
}
