using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public interface IInventoryStatusService
{
    Task<IReadOnlyList<InventoryStatusDefinitionDto>> GetStatusesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryBucketDto>> GetBucketsAsync(
        int? warehouseId = null,
        int? productId = null,
        string? status = null,
        string? lotNumber = null,
        string? serialNumber = null,
        CancellationToken cancellationToken = default);

    Task<InventoryStatusChangeResultDto> ChangeAsync(
        CreateInventoryStatusChangeDto request,
        CancellationToken cancellationToken = default);
}
