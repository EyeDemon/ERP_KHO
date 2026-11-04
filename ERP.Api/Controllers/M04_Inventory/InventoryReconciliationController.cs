using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Route("api/v1/inventory-reconciliation")]
[Authorize]
[PermissionAuthorize(AppPermissions.InventoryLedgerRead)]
public partial class InventoryReconciliationController : ControllerBase
{
    private readonly IInventoryReconciliationQueryService _queryService;
    private readonly IWmsGovernanceOperationsService _govOps;
    private readonly IWarehouseAuthorizationService _warehouseAccess;
    private readonly IStockReservationService _reservationService;
    private readonly ICurrentUser _currentUser;

    public InventoryReconciliationController(
        IInventoryReconciliationQueryService queryService,
        IWmsGovernanceOperationsService govOps,
        IWarehouseAuthorizationService warehouseAccess,
        IStockReservationService reservationService,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(queryService);
        ArgumentNullException.ThrowIfNull(govOps);
        ArgumentNullException.ThrowIfNull(warehouseAccess);
        ArgumentNullException.ThrowIfNull(reservationService);
        ArgumentNullException.ThrowIfNull(currentUser);
        _queryService = queryService;
        _govOps = govOps;
        _warehouseAccess = warehouseAccess;
        _reservationService = reservationService;
        _currentUser = currentUser;
    }

    public sealed record RebuildBalancesRequest(int? WarehouseId = null, int? ProductId = null);

    [HttpPost("rebuild")]
    [HttpPost("rebuild-stocks")]
    [HttpPost("~/api/inventory/reconciliation/rebuild-stocks")]
    [HttpPost("~/api/v1/inventory/reconciliation/rebuild-stocks")]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate, AppPermissions.InventoryReversalCreate)]
    public async Task<IActionResult> RebuildBalances(
        [FromQuery] int? warehouseId = null,
        [FromQuery] int? productId = null,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RebuildBalancesRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(_currentUser, AppPermissions.InventoryMovementCreate, AppPermissions.InventoryReversalCreate))
            return Forbid();

        var effectiveWarehouseId = warehouseId ?? request?.WarehouseId;
        var effectiveProductId = productId ?? request?.ProductId;
        int rebuiltBucketsCount;
        if (effectiveWarehouseId.HasValue)
        {
            await _warehouseAccess.EnsureWarehouseCommandAccessAsync(effectiveWarehouseId.Value, cancellationToken);
            rebuiltBucketsCount = await _reservationService.RebuildBalancesAsync(effectiveWarehouseId, effectiveProductId, cancellationToken);
        }
        else if (!IsGlobalAdminOrUnitStub())
        {
            var accessibleIds = await _warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
            if (accessibleIds.Count == 0)
                return Forbid();

            rebuiltBucketsCount = 0;
            foreach (var whId in accessibleIds)
            {
                rebuiltBucketsCount += await _reservationService.RebuildBalancesAsync(whId, effectiveProductId, cancellationToken);
            }
        }
        else
        {
            rebuiltBucketsCount = await _reservationService.RebuildBalancesAsync(null, effectiveProductId, cancellationToken);
        }

        return Ok(new
        {
            rebuiltBucketsCount,
            warehouseId = effectiveWarehouseId,
            productId = effectiveProductId,
            status = "REBUILT",
            statusLabel = "Đã đồng bộ lại số dư tồn kho, giữ chỗ và phân bổ từ sổ cái bất biến",
            rebuiltAtUtc = DateTime.UtcNow
        });
    }

    [HttpGet("valuation")]
    [PermissionAuthorize(AppPermissions.InventoryCostRead)]
    public async Task<IActionResult> GetValuation(
        [FromQuery] int? warehouseId = null,
        [FromQuery] int? productId = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<int>? accessibleIds = null;
        if (warehouseId.HasValue)
        {
            await _warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
        }
        else if (!IsGlobalAdminOrUnitStub())
        {
            accessibleIds = await _warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var outcome = await _govOps.GetInventoryValuationAsync(warehouseId, productId, accessibleIds, cancellationToken);
        return outcome.ToActionResult(this);
    }

    private bool IsGlobalAdminOrUnitStub()
    {
        if (_currentUser.IsGlobalAdmin) return true;
        var claimRole = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (string.Equals(claimRole, AppRoles.Admin, StringComparison.OrdinalIgnoreCase)
            || string.Equals(claimRole, AppRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return HttpContext is null && !_currentUser.IsAuthenticated && _currentUser.UserId == 0 && string.IsNullOrWhiteSpace(_currentUser.Role);
    }

    [HttpGet("warehouses")]
    [PermissionAuthorize(AppPermissions.InventoryLedgerRead)]
    public async Task<ActionResult<IReadOnlyList<InventoryReconciliationWarehouseDto>>> Warehouses()
        => Ok(await _queryService.GetAccessibleWarehousesAsync());

    [HttpGet("investigation")]
    [PermissionAuthorize(AppPermissions.InventoryLedgerRead)]
    public async Task<ActionResult<InventoryReconciliationInvestigationDto>> Investigation(
        [FromQuery] int warehouseId, [FromQuery] int productId,
        [FromQuery] int? eventAnchorId = null, [FromQuery] int limit = 50,
        [FromQuery] int? eventBeforeId = null, [FromQuery] int? bucketAnchorId = null,
        [FromQuery] int? bucketAfterId = null, [FromQuery] string bucketStatus = "Available",
        [FromQuery] string eventStatus = "Available",
        [FromQuery] int? allStatusBucketAnchorId = null,
        [FromQuery] string? allStatusBucketAggregateFingerprint = null)
        => Ok(await _queryService.GetInvestigationAsync(warehouseId, productId, eventAnchorId, limit,
            eventBeforeId, bucketAnchorId, bucketAfterId, bucketStatus, eventStatus,
            allStatusBucketAnchorId, allStatusBucketAggregateFingerprint));

    [HttpGet]
    [PermissionAuthorize(AppPermissions.InventoryLedgerRead)]
    public async Task<IActionResult> GetReconciliations(
        [FromQuery] int? warehouseId,
        [FromQuery] int? productId,
        [FromQuery] string? keyword,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _queryService.GetReconciliationsAsync(
            warehouseId,
            productId,
            keyword,
            page,
            pageSize);

        return Ok(result);
    }

    [HttpGet("summary")]
    [PermissionAuthorize(AppPermissions.InventoryLedgerRead)]
    public async Task<IActionResult> Summary(
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<int>? accessibleIds = null;
        if (_warehouseAccess != null)
        {
            if (warehouseId.HasValue)
                await _warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await _warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var page = await _queryService.GetReconciliationsAsync(warehouseId, null, null, 1, int.MaxValue);
        var items = page.Items;
        var totalReservedQuantity = _govOps != null
            ? await _govOps.SumReservedQuantityAsync(warehouseId, accessibleIds, cancellationToken)
            : 0m;

        return Ok(new
        {
            totalRows = page.TotalRecords,
            matchedCount = items.Count(x => x.Status == "Match" || x.Difference == 0m),
            mismatchCount = items.Count(x => x.Status != "Match" && x.Difference != 0m),
            totalStockQuantity = items.Sum(x => x.CurrentQuantity),
            totalLedgerQuantity = items.Sum(x => x.ExpectedQuantity ?? 0m),
            totalReservedQuantity,
            invalidReservationBucketCount = items.Sum(x => x.InvalidReservationBucketCount),
        });
    }
}
