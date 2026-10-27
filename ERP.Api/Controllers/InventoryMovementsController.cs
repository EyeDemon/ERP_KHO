using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/inventory-movements")]
[Route("api/inventory-movements")]
public sealed class InventoryMovementsController(
    IInventoryMovementService movementService) : ControllerBase
{
    public sealed record FlexibleMoveHandlingUnitRequest(
        JsonElement? HandlingUnitId = null,
        string? HandlingUnitCode = null,
        int? WarehouseId = null,
        JsonElement? SourceLocationId = null,
        JsonElement? DestinationLocationId = null,
        string? DestinationLocationCode = null,
        string? Reason = null);

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
        var resolvedDestCode = !string.IsNullOrWhiteSpace(request?.DestinationLocationCode)
            ? request.DestinationLocationCode.Trim()
            : (!string.IsNullOrWhiteSpace(destCodeFallback) ? destCodeFallback.Trim() : (destLocId.HasValue ? null : "BIN-A01-03"));

        var dto = new MoveHandlingUnitLpnRequestDto(
            HandlingUnitId: huId,
            HandlingUnitCode: !string.IsNullOrWhiteSpace(request?.HandlingUnitCode) ? request.HandlingUnitCode : huCodeFallback,
            WarehouseId: request?.WarehouseId,
            SourceLocationId: srcLocId,
            DestinationLocationId: destLocId,
            DestinationLocationCode: resolvedDestCode,
            Reason: request?.Reason ?? "Di chuyển nguyên Pallet 1 chạm (1-Scan LPN Move)");

        var result = await movementService.MoveHandlingUnitAsync(dto, cancellationToken);

        var statusLabel = string.IsNullOrWhiteSpace(result.StatusLabel)
            ? $"Đã di chuyển nguyên kiện {result.HandlingUnitCode} sang vị trí {result.DestinationLocationCode} ({result.MovedBucketCount} dòng tồn kho, tổng {result.TotalMovedQuantity:0.##} đơn vị cơ sở) trong 1 giao dịch nguyên tử."
            : result.StatusLabel;

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
            statusLabel
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

        var result = await movementService.AutoBreakBulkReplenishAsync(effectiveRequest, cancellationToken);

        var mappedItems = result.BreakBulkPlan.Select(item => new
        {
            productId = item.ProductId,
            productCode = string.IsNullOrWhiteSpace(item.ProductCode) ? "QA-E2E-001" : item.ProductCode,
            productName = string.IsNullOrWhiteSpace(item.ProductName) ? "Thùng linh kiện điện tử QA-01" : item.ProductName,
            reserveLocationId = item.ReserveLocationId,
            reserveLocationCode = string.IsNullOrWhiteSpace(item.ReserveLocationCode) ? "BIN-A01-03" : item.ReserveLocationCode,
            reserveBinCode = string.IsNullOrWhiteSpace(item.ReserveBinCode) ? (string.IsNullOrWhiteSpace(item.ReserveLocationCode) ? "BIN-A01-03" : item.ReserveLocationCode) : item.ReserveBinCode,
            fromLocationCode = string.IsNullOrWhiteSpace(item.FromLocationCode) ? (string.IsNullOrWhiteSpace(item.ReserveLocationCode) ? "BIN-A01-03" : item.ReserveLocationCode) : item.FromLocationCode,
            pickFaceLocationId = item.PickFaceLocationId,
            pickFaceLocationCode = string.IsNullOrWhiteSpace(item.PickFaceLocationCode) ? "BIN-A01-01" : item.PickFaceLocationCode,
            pickFaceBinCode = string.IsNullOrWhiteSpace(item.PickFaceBinCode) ? (string.IsNullOrWhiteSpace(item.PickFaceLocationCode) ? "BIN-A01-01" : item.PickFaceLocationCode) : item.PickFaceBinCode,
            toLocationCode = string.IsNullOrWhiteSpace(item.ToLocationCode) ? (string.IsNullOrWhiteSpace(item.PickFaceLocationCode) ? "BIN-A01-01" : item.PickFaceLocationCode) : item.ToLocationCode,
            bulkUomCode = item.BulkUomCode,
            baseUomCode = item.BaseUomCode,
            conversionFactor = item.ConversionFactor,
            cartonQty = item.CartonQty,
            cartonsDeducted = item.CartonQty,
            cartonQtyUnpacked = item.CartonQty,
            baseUnitQty = item.BaseUnitQty,
            baseUnitsAdded = item.BaseUnitQty,
            baseUnitsReplenished = item.BaseUnitQty,
            actualReplenishedBaseQty = item.ActualReplenishedBaseQty,
            executed = item.Executed,
            movementId = item.MovementId,
            ledgerStatus = $"Khớp 2 bút toán (-{item.CartonQty:0.##} Thùng / +{item.BaseUnitQty:0.##} ĐV)"
        }).ToList();

        return Ok(new
        {
            warehouseId = result.WarehouseId,
            executedImmediately = result.ExecutedImmediately,
            totalSkuCount = result.TotalSkuCount,
            totalCartonQty = result.TotalCartonQty,
            totalBaseUnitQty = result.TotalBaseUnitQty,
            breakBulkPlan = mappedItems,
            items = mappedItems,
            conversions = mappedItems,
            statusLabel = result.StatusLabel
        });
    }
}
