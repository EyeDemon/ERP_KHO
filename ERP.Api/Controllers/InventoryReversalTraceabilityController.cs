using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/inventory")]
public sealed class InventoryReversalTraceabilityController(
    IInventoryReversalService reversalService,
    IInventoryTraceabilityQueryService traceabilityService) : ControllerBase
{
    [HttpGet("reversal-warehouses")]
    [PermissionAuthorize(AppPermissions.InventoryLedgerRead)]
    public async Task<ActionResult<IReadOnlyList<InventoryReversalWarehouseDto>>> ReversalWarehouses(
        CancellationToken cancellationToken = default) =>
        Ok(await reversalService.GetReversalWarehousesAsync(cancellationToken));

    [HttpGet("reversal-candidates")]
    [PermissionAuthorize(AppPermissions.InventoryLedgerRead)]
    public async Task<ActionResult<PagedResult<InventoryReversalCandidateDto>>> Candidates(
        [FromQuery] int? warehouseId, [FromQuery] int? transactionId,
        [FromQuery] bool? isReversed, [FromQuery] string? productCode,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await reversalService.GetCandidatesAsync(warehouseId, page, pageSize, transactionId, isReversed, productCode, cancellationToken));

    [HttpPost("reversals")]
    [PermissionAuthorize(AppPermissions.InventoryReversalCreate)]
    [IdempotentCommand("InventoryReversal.Create")]
    public async Task<ActionResult<InventoryReversalResultDto>> Reverse(
        CreateInventoryReversalDto request,
        CancellationToken cancellationToken) =>
        Ok(await reversalService.ReverseAsync(request, cancellationToken));

    [HttpGet("traceability")]
    [PermissionAuthorize(AppPermissions.InventoryTraceabilityRead)]
    public async Task<ActionResult<InventoryTraceabilityResultDto>> Trace(
        [FromQuery] int? warehouseId,
        [FromQuery] int? productId,
        [FromQuery] string? lotNumber,
        [FromQuery] string? serialNumber,
        [FromQuery] string? referenceType,
        [FromQuery] int? referenceId,
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default) =>
        Ok(await traceabilityService.TraceAsync(
            warehouseId, productId, lotNumber, serialNumber, referenceType, referenceId, limit, cancellationToken));
}
