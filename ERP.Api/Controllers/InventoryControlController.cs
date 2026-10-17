using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/inventory")]
public sealed class InventoryControlController(
    IInventoryLockService lockService,
    IInventoryMovementService movementService) : ControllerBase
{
    [HttpGet("locks")]
    [PermissionAuthorize(AppPermissions.InventoryLockRead)]
    public async Task<ActionResult<IReadOnlyList<InventoryLockDto>>> Locks(
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await lockService.ListAsync(warehouseId, status, cancellationToken));

    [HttpGet("locks/{id:int}")]
    [PermissionAuthorize(AppPermissions.InventoryLockRead)]
    public async Task<ActionResult<InventoryLockDto>> Lock(
        int id,
        CancellationToken cancellationToken) =>
        Ok(await lockService.GetAsync(id, cancellationToken));

    [HttpPost("locks")]
    [PermissionAuthorize(AppPermissions.InventoryLockManage)]
    [IdempotentCommand("InventoryLock.Create")]
    public async Task<ActionResult<InventoryLockDto>> CreateLock(
        CreateInventoryLockDto request,
        CancellationToken cancellationToken) =>
        Ok(await lockService.CreateAsync(request, cancellationToken));

    [HttpPost("locks/{id:int}/release")]
    [PermissionAuthorize(AppPermissions.InventoryLockManage)]
    [IdempotentCommand("InventoryLock.Release")]
    public async Task<ActionResult<InventoryLockDto>> ReleaseLock(
        int id,
        ReleaseInventoryLockDto request,
        CancellationToken cancellationToken) =>
        Ok(await lockService.ReleaseAsync(id, request, cancellationToken));

    [HttpPost("movements")]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate)]
    [IdempotentCommand("InventoryMovement.Create")]
    public async Task<ActionResult<InventoryMoveResultDto>> Move(
        CreateInventoryMoveDto request,
        CancellationToken cancellationToken) =>
        Ok(await movementService.MoveAsync(request, cancellationToken));

    [HttpGet("replenishment-suggestions")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> ReplenishmentSuggestions(
        [FromServices] ErpKhoDbContext db,
        [FromServices] IWarehouseAuthorizationService warehouseAuth,
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var accessibleIds = await warehouseAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (warehouseId.HasValue && !accessibleIds.Contains(warehouseId.Value))
            return Ok(Array.Empty<object>());

        var scopedWarehouseIds = warehouseId.HasValue
            ? new[] { warehouseId.Value }
            : accessibleIds.ToArray();

        var pickLocations = await db.WarehouseLocations.AsNoTracking()
            .Where(l => scopedWarehouseIds.Contains(l.WarehouseId) && l.IsActive && !l.IsBlocked && !l.IsSystemManaged && l.IsPickable)
            .OrderBy(l => l.Code)
            .Select(l => new { l.Id, l.WarehouseId, l.Code, l.Name })
            .ToListAsync(cancellationToken);

        var activeStocks = await db.InventoryStocks.AsNoTracking()
            .Where(s => scopedWarehouseIds.Contains(s.WarehouseId) &&
                        s.Status == InventoryStatus.Available &&
                        s.LocationId.HasValue)
            .Select(s => new
            {
                s.Id,
                s.WarehouseId,
                WarehouseName = s.Warehouse.Name,
                s.ProductId,
                ProductCode = s.Product.Code,
                ProductName = s.Product.Name,
                LocationId = s.LocationId!.Value,
                LocationCode = s.Location != null ? s.Location.Code : "DEFAULT",
                IsPickableLocation = s.Location != null && s.Location.IsPickable,
                AvailableQty = s.Quantity - s.ReservedQuantity,
                s.Quantity
            })
            .ToListAsync(cancellationToken);

        var suggestions = new List<object>();
        var sourceCandidates = activeStocks
            .Where(s => s.AvailableQty > 0 && (!s.IsPickableLocation || s.AvailableQty >= 15m))
            .OrderBy(s => s.IsPickableLocation ? 1 : 0)
            .ThenByDescending(s => s.AvailableQty)
            .ToList();

        foreach (var src in sourceCandidates)
        {
            var candidatePickBins = pickLocations
                .Where(l => l.WarehouseId == src.WarehouseId && l.Id != src.LocationId)
                .ToList();

            foreach (var targetBin in candidatePickBins)
            {
                var currentAtTarget = activeStocks
                    .Where(s => s.WarehouseId == src.WarehouseId && s.ProductId == src.ProductId && s.LocationId == targetBin.Id)
                    .Sum(s => s.Quantity);

                if (currentAtTarget < 20m && src.AvailableQty > currentAtTarget)
                {
                    var suggestedQty = Math.Min(src.AvailableQty, Math.Max(5m, 25m - currentAtTarget));
                    suggestions.Add(new
                    {
                        inventoryStockId = src.Id,
                        productId = src.ProductId,
                        productCode = src.ProductCode,
                        productName = src.ProductName,
                        warehouseId = src.WarehouseId,
                        warehouseName = src.WarehouseName,
                        sourceLocationId = src.LocationId,
                        sourceLocationCode = src.LocationCode,
                        availableAtSource = src.AvailableQty,
                        targetLocationId = targetBin.Id,
                        targetLocationCode = targetBin.Code,
                        currentAtTarget,
                        suggestedQuantity = suggestedQty
                    });
                    break;
                }
            }
        }

        return Ok(suggestions.Take(25).ToList());
    }
}

