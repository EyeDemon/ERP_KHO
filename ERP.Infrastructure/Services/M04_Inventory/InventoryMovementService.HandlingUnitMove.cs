using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class InventoryMovementService
{
    public async Task<MoveHandlingUnitLpnResultDto> MoveHandlingUnitAsync(
        MoveHandlingUnitLpnRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw BadRequestWithCode("HANDLING_UNIT_REQUIRED", "Vui lòng cung cấp mã đơn vị chứa hàng (LPN/Pallet).");
        }

        if (request.WarehouseId is > 0)
        {
            await EnsureCommandWarehouseAccessAsync(request.WarehouseId.Value, cancellationToken);

            var requestedWarehouse = await context.Warehouses.AsNoTracking()
                .Where(w => w.Id == request.WarehouseId.Value)
                .Select(w => new { w.Id, w.IsActive })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw NotFoundWithCode("WAREHOUSE_NOT_FOUND", $"Không tìm thấy kho #{request.WarehouseId.Value}.");

            if (!requestedWarehouse.IsActive)
            {
                throw BadRequestWithCode("WAREHOUSE_INACTIVE", $"Kho #{request.WarehouseId.Value} hiện đang ngừng hoạt động.");
            }
        }

        if ((request.HandlingUnitId is null or <= 0) && string.IsNullOrWhiteSpace(request.HandlingUnitCode))
        {
            throw BadRequestWithCode("HANDLING_UNIT_REQUIRED", "Vui lòng cung cấp mã đơn vị chứa hàng (LPN/Pallet).");
        }

        var now = DateTime.UtcNow;
        var reasonText = !string.IsNullOrWhiteSpace(request.Reason)
            ? request.Reason.Trim()
            : "Di chuyển nguyên Pallet 1 chạm (1-Scan LPN Move)";

        HandlingUnit? hu;
        if (request.HandlingUnitId is > 0)
        {
            var huByIdQuery = context.HandlingUnits
                .Include(h => h.Contents)
                .Where(h => h.Id == request.HandlingUnitId.Value);

            if (request.WarehouseId is > 0)
            {
                huByIdQuery = huByIdQuery.Where(h => h.WarehouseId == request.WarehouseId.Value);
            }
            else if (warehouseAuthorization is not null)
            {
                var accessibleIds = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (accessibleIds is not null)
                {
                    huByIdQuery = huByIdQuery.Where(h => accessibleIds.Contains(h.WarehouseId));
                }
            }

            hu = await huByIdQuery.FirstOrDefaultAsync(cancellationToken)
                ?? throw NotFoundWithCode("HANDLING_UNIT_NOT_FOUND", $"Không tìm thấy đơn vị đóng gói/Pallet #{request.HandlingUnitId.Value}.");
        }
        else
        {
            var code = request.HandlingUnitCode!.Trim();
            var huQuery = context.HandlingUnits
                .Include(h => h.Contents)
                .Where(h => h.HuCode == code || h.Barcode == code || h.Sscc == code);

            if (request.WarehouseId is > 0)
            {
                hu = await huQuery.FirstOrDefaultAsync(h => h.WarehouseId == request.WarehouseId.Value, cancellationToken);
            }
            else
            {
                if (warehouseAuthorization is not null)
                {
                    var accessibleIds = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
                    if (accessibleIds is not null)
                    {
                        huQuery = huQuery.Where(h => accessibleIds.Contains(h.WarehouseId));
                    }
                }

                hu = await huQuery.FirstOrDefaultAsync(cancellationToken);
            }

            if (hu is null)
            {
                throw NotFoundWithCode("HANDLING_UNIT_NOT_FOUND", $"Không tìm thấy đơn vị đóng gói/Pallet '{code}'.");
            }
        }

        var sourceWarehouseId = hu.WarehouseId;
        if (sourceWarehouseId <= 0)
        {
            throw NotFoundWithCode("WAREHOUSE_NOT_FOUND", "Đơn vị đóng gói không gắn với kho đang hoạt động hợp lệ.");
        }

        await EnsureCommandWarehouseAccessAsync(sourceWarehouseId, cancellationToken);

        var sourceWarehouse = await context.Warehouses.AsNoTracking()
            .Where(w => w.Id == sourceWarehouseId)
            .Select(w => new { w.Id, w.IsActive })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFoundWithCode("WAREHOUSE_NOT_FOUND", $"Không tìm thấy kho #{sourceWarehouseId}.");

        if (!sourceWarehouse.IsActive)
        {
            throw BadRequestWithCode("WAREHOUSE_INACTIVE", $"Kho #{sourceWarehouseId} hiện đang ngừng hoạt động.");
        }

        WarehouseLocation? destLocation;
        if (request.DestinationLocationId is > 0)
        {
            destLocation = await context.WarehouseLocations
                .FirstOrDefaultAsync(l => l.Id == request.DestinationLocationId.Value, cancellationToken)
                ?? throw NotFoundWithCode("DESTINATION_LOCATION_NOT_FOUND", $"Không tìm thấy vị trí kệ đích #{request.DestinationLocationId.Value}.");

            if (!string.IsNullOrWhiteSpace(request.DestinationLocationCode) &&
                !string.Equals(destLocation.Code, request.DestinationLocationCode.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw BadRequestWithCode("DESTINATION_LOCATION_MISMATCH", $"Mã vị trí đích '{request.DestinationLocationCode.Trim()}' không khớp với vị trí #{destLocation.Id} ({destLocation.Code}).");
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.DestinationLocationCode))
        {
            var locCode = request.DestinationLocationCode.Trim();
            destLocation = await context.WarehouseLocations
                .FirstOrDefaultAsync(
                    l => l.WarehouseId == sourceWarehouseId && l.Code == locCode,
                    cancellationToken)
                ?? throw NotFoundWithCode("DESTINATION_LOCATION_NOT_FOUND", $"Không tìm thấy vị trí kệ đích '{locCode}' tại kho #{sourceWarehouseId}.");
        }
        else
        {
            throw BadRequestWithCode("DESTINATION_LOCATION_REQUIRED", "Vui lòng chỉ định vị trí kệ đích (DestinationLocationId hoặc DestinationLocationCode).");
        }

        if (destLocation.WarehouseId != sourceWarehouseId)
        {
            await EnsureCommandWarehouseAccessAsync(destLocation.WarehouseId, cancellationToken);
            throw Validation("CROSS_WAREHOUSE_LPN_MOVE_NOT_ALLOWED", "Không thể di chuyển nội bộ nguyên kiện sang vị trí thuộc kho khác; vui lòng lập phiếu chuyển kho.");
        }

        if (!destLocation.IsActive || destLocation.IsBlocked)
        {
            throw Validation("DESTINATION_LOCATION_BLOCKED", $"Vị trí kệ đích '{destLocation.Code}' đang ngừng hoạt động hoặc bị khóa.");
        }

        var warehouseId = sourceWarehouseId;

        var isRelational = context.Database.IsRelational();
        await using var tx = isRelational && context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        var sourceQuery = context.InventoryStocks
            .Include(s => s.Product)
            .Include(s => s.Location)
            .Where(s => s.WarehouseId == warehouseId &&
                        s.HandlingUnitId == hu.Id &&
                        s.LocationId != null &&
                        s.LocationId != destLocation.Id &&
                        (s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity) > 0m);
        if (request.SourceLocationId is > 0)
        {
            sourceQuery = sourceQuery.Where(s => s.LocationId == request.SourceLocationId.Value);
        }

        var sourceBuckets = await sourceQuery.ToListAsync(cancellationToken);
        var alreadyAtDestBuckets = new List<InventoryStock>();
        if (sourceBuckets.Count == 0)
        {
            alreadyAtDestBuckets = await context.InventoryStocks
                .Include(s => s.Product)
                .Include(s => s.Location)
                .Where(s => s.WarehouseId == warehouseId &&
                            s.HandlingUnitId == hu.Id &&
                            s.LocationId == destLocation.Id &&
                            s.Quantity > 0m)
                .OrderBy(s => s.ProductId)
                .ThenBy(s => s.Id)
                .ToListAsync(cancellationToken);
        }

        if (sourceBuckets.Count == 0 && alreadyAtDestBuckets.Count == 0)
        {
            throw Validation("HANDLING_UNIT_EMPTY_OR_UNAVAILABLE", $"Đơn vị đóng gói '{hu.HuCode}' không có tồn kho khả dụng để di chuyển.");
        }

        var actorId = currentUser?.UserId is > 0 ? currentUser.UserId : 1;
        var (movedItems, totalMovedQty) = await ExecuteHandlingUnitBucketMovesAsync(
            warehouseId,
            hu,
            destLocation,
            sourceBuckets,
            alreadyAtDestBuckets,
            reasonText,
            now,
            actorId,
            cancellationToken);

        var resolvedHuCode = !string.IsNullOrWhiteSpace(hu.HuCode)
            ? hu.HuCode
            : (request.HandlingUnitCode?.Trim() ?? $"LPN-{hu.Id}");
        context.AuditLogs.Add(new AuditLog
        {
            UserId = actorId > 0 ? actorId : null,
            WarehouseId = warehouseId,
            EntityName = "HandlingUnit",
            EntityId = hu.Id,
            Action = "MOVE_FULL_HANDLING_UNIT_LPN",
            OldValues = movedItems.FirstOrDefault()?.FromLocationCode ?? "SOURCE_BIN",
            NewValues = $"{resolvedHuCode} -> {destLocation.Code} ({movedItems.Count} dòng tồn kho, {totalMovedQty:0.##} ĐV)",
            Reason = reasonText,
            Result = "Success",
            Timestamp = now
        });

        await context.SaveChangesAsync(cancellationToken);
        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return new MoveHandlingUnitLpnResultDto(
            hu.Id,
            resolvedHuCode,
            warehouseId,
            destLocation.Id,
            destLocation.Code,
            movedItems.Count,
            totalMovedQty,
            reasonText,
            movedItems,
            $"Đã di chuyển nguyên kiện {resolvedHuCode} sang vị trí {destLocation.Code} ({movedItems.Count} dòng tồn kho, tổng {totalMovedQty:0.##} đơn vị cơ sở) trong 1 giao dịch nguyên tử.");
    }

    private async Task EnsureCommandWarehouseAccessAsync(int targetWarehouseId, CancellationToken cancellationToken)
    {
        if (warehouseAuthorization is null)
            return;

        if (currentUser?.IsAuthenticated == true)
        {
            await (warehouseAuthorization.EnsureWarehouseCommandAccessAsync(targetWarehouseId, cancellationToken)
                ?? warehouseAuthorization.EnsureWarehouseAccessAsync(targetWarehouseId, cancellationToken));
        }
        else
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(targetWarehouseId, cancellationToken);
        }
    }

    private static BusinessRuleException BadRequestWithCode(string code, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["ErrorCode"] = code;
        return ex;
    }

    private static NotFoundException NotFoundWithCode(string code, string message)
    {
        var ex = new NotFoundException(message);
        ex.Data["ErrorCode"] = code;
        return ex;
    }
}
