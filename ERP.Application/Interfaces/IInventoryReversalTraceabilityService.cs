using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public interface IInventoryReversalService
{
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
        CancellationToken cancellationToken = default);
}
