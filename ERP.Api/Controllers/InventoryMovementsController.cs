using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/inventory-movements")]
[Route("api/inventory-movements")]
public sealed class InventoryMovementsController(
    IInventoryMovementService movementService,
    ErpKhoDbContext db = null!,
    IInventoryLockEvaluator lockEvaluator = null!,
    ICurrentUser currentUser = null!,
    IWarehouseAuthorizationService warehouseAccess = null!) : ControllerBase
{
    public sealed record FlexibleMoveHandlingUnitRequest(
        JsonElement? HandlingUnitId = null,
        string? HandlingUnitCode = null,
        int? WarehouseId = null,
        JsonElement? SourceLocationId = null,
        JsonElement? DestinationLocationId = null,
        string? DestinationLocationCode = null,
        string? Reason = null);

    private InventoryMovementService ResolveConcreteService() =>
        movementService as InventoryMovementService
        ?? new InventoryMovementService(db, warehouseAccess, currentUser, lockEvaluator);

    private static int? ParseOptionalInt(JsonElement? element, out string? stringFallback)
    {
        stringFallback = null;
        if (element is null)
            return null;

        var el = element.Value;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var intVal))
            return intVal;

        if (el.ValueKind == JsonValueKind.String)
        {
            var raw = el.GetString()?.Trim();
            if (int.TryParse(raw, out var parsedInt))
                return parsedInt;
            stringFallback = raw;
        }

        return null;
    }

    [HttpPost]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate)]
    [IdempotentCommand("InventoryMovement.Create")]
    public async Task<ActionResult<InventoryMoveResultDto>> Move(
        [FromBody] CreateInventoryMoveDto request,
        CancellationToken cancellationToken = default) =>
        Ok(await movementService.MoveAsync(request, cancellationToken));

    [HttpPost("move-handling-unit")]
    [HttpPost("~/api/v1/inventory/move-handling-unit")]
    [HttpPost("~/api/inventory/move-handling-unit")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    [IdempotentCommand("InventoryMovement.MoveHandlingUnitLpn")]
    public async Task<IActionResult> MoveHandlingUnit(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] FlexibleMoveHandlingUnitRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var huId = ParseOptionalInt(request?.HandlingUnitId, out var huCodeFallback);
        var srcLocId = ParseOptionalInt(request?.SourceLocationId, out _);
        var destLocId = ParseOptionalInt(request?.DestinationLocationId, out var destCodeFallback);

        var dto = new MoveHandlingUnitLpnRequestDto(
            HandlingUnitId: huId,
            HandlingUnitCode: !string.IsNullOrWhiteSpace(request?.HandlingUnitCode) ? request.HandlingUnitCode : huCodeFallback,
            WarehouseId: request?.WarehouseId,
            SourceLocationId: srcLocId,
            DestinationLocationId: destLocId,
            DestinationLocationCode: !string.IsNullOrWhiteSpace(request?.DestinationLocationCode) ? request.DestinationLocationCode : destCodeFallback,
            Reason: request?.Reason ?? "Di chuyển nguyên Pallet 1 chạm (1-Scan LPN Move)");

        var service = ResolveConcreteService();
        var result = await service.MoveHandlingUnitAsync(dto, cancellationToken);

        return Ok(new
        {
            handlingUnitId = result.HandlingUnitId,
            handlingUnitCode = result.HandlingUnitCode,
            warehouseId = result.WarehouseId,
            destinationLocationId = result.DestinationLocationId,
            destinationLocationCode = result.DestinationLocationCode,
            movedBucketCount = result.MovedBucketCount,
            totalMovedQuantity = result.TotalMovedQuantity,
            reason = result.Reason,
            movements = result.Movements,
            items = result.Movements,
            status = "COMPLETED",
            statusLabel = result.StatusLabel
        });
    }

    [HttpGet("auto-break-bulk-replenish")]
    [HttpPost("auto-break-bulk-replenish")]
    [HttpPost("~/api/v1/inventory/auto-break-bulk-replenish")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> AutoBreakBulkReplenish(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AutoBreakBulkReplenishRequestDto? request = null,
        [FromQuery] int? warehouseId = null,
        [FromQuery] int? productId = null,
        [FromQuery] bool? executeImmediately = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveExecute = executeImmediately ?? request?.ExecuteImmediately ?? false;
        if (effectiveExecute && User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var effectiveRequest = new AutoBreakBulkReplenishRequestDto(
            WarehouseId: warehouseId ?? request?.WarehouseId,
            ProductId: productId ?? request?.ProductId,
            CartonQuantity: request?.CartonQuantity ?? 1m,
            MinPickFaceThresholdQty: request?.MinPickFaceThresholdQty ?? 50m,
            ExecuteImmediately: effectiveExecute);

        var service = ResolveConcreteService();
        var result = await service.AutoBreakBulkReplenishAsync(effectiveRequest, cancellationToken);

        return Ok(new
        {
            warehouseId = result.WarehouseId,
            executedImmediately = result.ExecutedImmediately,
            totalSkuCount = result.TotalSkuCount,
            totalCartonQty = result.TotalCartonQty,
            totalBaseUnitQty = result.TotalBaseUnitQty,
            breakBulkPlan = result.BreakBulkPlan,
            items = result.BreakBulkPlan,
            statusLabel = result.StatusLabel
        });
    }
}
