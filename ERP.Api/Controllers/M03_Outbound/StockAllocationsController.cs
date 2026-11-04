using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/inventory/allocations")]
[Route("api/v1/inventory/allocations")]
[Route("api/v1/stock-allocations")]
[Authorize]
public sealed class StockAllocationsController(
    IStockAllocationService service,
    IWmsGovernanceOperationsService govOps,
    IWarehouseAuthorizationService warehouseAccess) : ControllerBase
{
    [HttpGet]
    [PermissionAuthorize(AppPermissions.AllocationRead)]
    public async Task<ActionResult<StockAllocationPageDto>> GetPage(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? warehouseId = null,
        [FromQuery] int? reservationId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await service.GetPageAsync(page, pageSize, warehouseId, reservationId, status, cancellationToken));

    [HttpGet("{id:int}")]
    [PermissionAuthorize(AppPermissions.AllocationRead)]
    public async Task<ActionResult<StockAllocationDto>> Get(int id, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(id, cancellationToken));

    [HttpGet("reservations")]
    [PermissionAuthorize(AppPermissions.AllocationRead)]
    public async Task<ActionResult<IReadOnlyList<AllocatableReservationDto>>> GetReservations(
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default) =>
        Ok(await service.GetReservationsAsync(warehouseId, cancellationToken));

    [HttpGet("candidates")]
    [PermissionAuthorize(AppPermissions.AllocationRead)]
    public async Task<ActionResult<IReadOnlyList<StockAllocationCandidateDto>>> GetCandidates(
        [FromQuery] int reservationId,
        CancellationToken cancellationToken = default) =>
        Ok(await service.GetCandidatesAsync(reservationId, cancellationToken));

    [HttpGet("summary")]
    [PermissionAuthorize(AppPermissions.AllocationRead)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<int>? accessibleIds = null;
        if (warehouseAccess != null)
        {
            if (warehouseId.HasValue)
                await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var reservations = await service.GetReservationsAsync(warehouseId, cancellationToken);
        var unallocatedReservationCount = reservations.Count(x => x.AllocatableQuantity > 0);
        var totalUnallocatedQuantity = reservations.Sum(x => x.AllocatableQuantity);

        var outcome = await govOps.GetStockAllocationSummaryAsync(
            warehouseId,
            accessibleIds,
            unallocatedReservationCount,
            totalUnallocatedQuantity,
            cancellationToken);
        return outcome.ToActionResult(this);
    }

    public sealed record VerifyAllocationFefoRequest(int? WarehouseId = null);

    [HttpPost("verify-fefo-compliance")]
    [PermissionAuthorize(AppPermissions.AllocationCreate)]
    public async Task<IActionResult> VerifyFefoCompliance(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyAllocationFefoRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var warehouseId = request?.WarehouseId;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (warehouseAccess != null)
        {
            if (warehouseId.HasValue)
                await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var outcome = await govOps.VerifyAllocationFefoComplianceAsync(warehouseId, accessibleIds, cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpPost]
    [PermissionAuthorize(AppPermissions.AllocationCreate)]
    [IdempotentCommand("InventoryAllocation.Create")]
    public async Task<ActionResult<IReadOnlyList<StockAllocationDto>>> Create(
        CreateStockAllocationDto request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("auto")]
    [PermissionAuthorize(AppPermissions.AllocationCreate)]
    [IdempotentCommand("InventoryAllocation.Auto")]
    public async Task<ActionResult<IReadOnlyList<StockAllocationDto>>> AutoAllocate(
        CreateStockAllocationDto request,
        CancellationToken cancellationToken)
    {
        request.LocationId = null;
        var result = await service.AutoAllocateAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:int}/release")]
    [PermissionAuthorize(AppPermissions.AllocationRelease)]
    [IdempotentCommand("InventoryAllocation.Release")]
    public async Task<IActionResult> Release(
        int id,
        ReleaseStockAllocationDto request,
        CancellationToken cancellationToken)
    {
        await service.ReleaseAsync(id, request, cancellationToken);
        return Ok(new { message = "Đã giải phóng Allocation." });
    }

    [HttpPost("{id:int}/reallocate")]
    [PermissionAuthorize(AppPermissions.AllocationReallocate)]
    [IdempotentCommand("InventoryAllocation.Reallocate")]
    public async Task<ActionResult<IReadOnlyList<StockAllocationDto>>> Reallocate(
        int id,
        ReallocateStockAllocationDto request,
        CancellationToken cancellationToken)
    {
        var result = await service.ReallocateAsync(id, request, cancellationToken);
        return Ok(result);
    }
}
