using ERP.Application.DTOs;
using ERP.Application.Common;

namespace ERP.Application.Interfaces;

public interface IInventoryReversalService
{
    Task<IReadOnlyList<InventoryReversalReasonDto>> GetReversalReasonsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryReversalWarehouseDto>> GetReversalWarehousesAsync(
        CancellationToken cancellationToken = default);

    Task<PagedResult<InventoryReversalCandidateDto>> GetCandidatesAsync(
        int? warehouseId = null, int page = 1, int pageSize = 20,
        int? transactionId = null, bool? isReversed = null,
        string? productCode = null, CancellationToken cancellationToken = default);

    Task<InventoryReversalResultDto> ReverseAsync(
        CreateInventoryReversalDto request,
        CancellationToken cancellationToken = default);
}

public interface IInventoryTraceabilityQueryService
{
    Task<InventoryTraceabilityResultDto> TraceAsync(
        int? warehouseId = null,
        int? productId = null,
        string? lotNumber = null,
        string? serialNumber = null,
        string? referenceType = null,
        int? referenceId = null,
        int limit = 200,
        CancellationToken cancellationToken = default,
        int bucketOffset = 0);
}
