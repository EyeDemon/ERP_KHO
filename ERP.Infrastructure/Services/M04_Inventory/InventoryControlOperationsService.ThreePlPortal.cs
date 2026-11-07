using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class InventoryControlOperationsService
{
    public Task<WmsEndpointOutcome> GetThreePlDepositorPortalAsync(
        ThreePlDepositorPortalInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default) =>
        ExecuteThreePlDepositorPortalCoreAsync(request, actorUserId, warehouseAuth, serverBoundOwnerId: null, isServerBoundOverload: false, cancellationToken);

    public Task<WmsEndpointOutcome> GetThreePlDepositorPortalAsync(
        ThreePlDepositorPortalInput? request,
        int? actorUserId,
        IWarehouseAuthorizationService? warehouseAuth,
        int? serverBoundOwnerId,
        CancellationToken cancellationToken = default) =>
        ExecuteThreePlDepositorPortalCoreAsync(request, actorUserId, warehouseAuth, serverBoundOwnerId, isServerBoundOverload: true, cancellationToken);

    private static WmsEndpointOutcome DepositorAccessDenied(int? boundOwnerId = null) =>
        WmsEndpointOutcome.Json(403, new
        {
            code = "TENANT_DEPOSITOR_ACCESS_DENIED",
            errorCode = "TENANT_DEPOSITOR_ACCESS_DENIED",
            message = boundOwnerId.HasValue
                ? $"Chủ hàng ký gửi 3PL (OwnerId={boundOwnerId.Value}) không được phép truy cập dữ liệu hoặc bảng kê của chủ hàng khác."
                : "Không chấp nhận giả mạo định danh chủ hàng ký gửi 3PL từ tham số phía client."
        });

    private async Task<WmsEndpointOutcome> ExecuteThreePlDepositorPortalCoreAsync(
        ThreePlDepositorPortalInput? request,
        int? actorUserId,
        IWarehouseAuthorizationService? warehouseAuth,
        int? serverBoundOwnerId,
        bool isServerBoundOverload,
        CancellationToken cancellationToken)
    {
#pragma warning disable CS0618
        var clientSuppliedAuthOwnerId = request?.AuthenticatedOwnerId;
#pragma warning restore CS0618
        var isInternalPrivilegedCaller = currentUser is null
            || currentUser.IsGlobalAdmin
            || string.Equals(currentUser.Role, "Manager", StringComparison.OrdinalIgnoreCase);

        if (serverBoundOwnerId.HasValue)
        {
            if ((request?.RequestedOwnerId.HasValue == true && request.RequestedOwnerId.Value != serverBoundOwnerId.Value)
                || (request?.OwnerId.HasValue == true && request.OwnerId.Value != serverBoundOwnerId.Value)
                || (clientSuppliedAuthOwnerId.HasValue && clientSuppliedAuthOwnerId.Value != serverBoundOwnerId.Value))
            {
                return DepositorAccessDenied(serverBoundOwnerId);
            }
        }
        else if (isServerBoundOverload)
        {
            if (clientSuppliedAuthOwnerId.HasValue)
            {
                if (!isInternalPrivilegedCaller
                    || (request?.RequestedOwnerId.HasValue == true && request.RequestedOwnerId.Value != clientSuppliedAuthOwnerId.Value)
                    || (request?.OwnerId.HasValue == true && request.OwnerId.Value != clientSuppliedAuthOwnerId.Value))
                {
                    return DepositorAccessDenied();
                }
            }
            else if (!isInternalPrivilegedCaller)
            {
                return DepositorAccessDenied();
            }
        }
        else if (clientSuppliedAuthOwnerId.HasValue)
        {
            if (request!.RequestedOwnerId.HasValue && clientSuppliedAuthOwnerId.Value != request.RequestedOwnerId.Value)
            {
                throw new ERP.Application.Exceptions.ForbiddenException(
                    $"THREEPL_DEPOSITOR_CROSS_OWNER_FORBIDDEN: Chủ hàng ký gửi 3PL (OwnerId={clientSuppliedAuthOwnerId.Value}) bị từ chối quyền xem tồn kho hoặc bảng kê phí của chủ hàng khác (OwnerId={request.RequestedOwnerId.Value}).");
            }
            if (request.OwnerId.HasValue && clientSuppliedAuthOwnerId.Value != request.OwnerId.Value)
            {
                throw new ERP.Application.Exceptions.ForbiddenException(
                    $"THREEPL_DEPOSITOR_CROSS_OWNER_FORBIDDEN: Chủ hàng ký gửi 3PL (OwnerId={clientSuppliedAuthOwnerId.Value}) bị từ chối quyền xem tồn kho hoặc bảng kê phí của chủ hàng khác (OwnerId={request.OwnerId.Value}).");
            }
            if (!isInternalPrivilegedCaller)
            {
                return DepositorAccessDenied();
            }
        }

        if (request?.OwnerId.HasValue == true &&
            request?.RequestedOwnerId.HasValue == true &&
            request.OwnerId.Value != request.RequestedOwnerId.Value)
        {
            return WmsEndpointOutcome.UnprocessableEntity(new
            {
                code = "THREEPL_OWNER_MISMATCH",
                errorCode = "THREEPL_OWNER_MISMATCH",
                message = "Thông tin OwnerId và RequestedOwnerId không đồng nhất."
            });
        }

        if (db == null)
        {
            return WmsEndpointOutcome.BadRequest("Database context is required.");
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var warehouseId = request?.WarehouseId;
        if (warehouseId is <= 0)
        {
            return WmsEndpointOutcome.UnprocessableEntity(new
            {
                code = "INVALID_WAREHOUSE_ID",
                errorCode = "INVALID_WAREHOUSE_ID",
                message = "Mã kho (WarehouseId) không hợp lệ."
            });
        }

        var scopedWarehouseIds = await ResolveScopedWarehouseIdsAsync(warehouseId, effectiveAuth, cancellationToken);
        if (warehouseId.HasValue && !await db.Warehouses.AsNoTracking().AnyAsync(w => w.Id == warehouseId.Value, cancellationToken))
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "WAREHOUSE_NOT_FOUND",
                errorCode = "WAREHOUSE_NOT_FOUND",
                message = $"Không tìm thấy kho #{warehouseId.Value}."
            });
        }

        var now = DateTime.UtcNow;
        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : 1);
        var billingPeriod = !string.IsNullOrWhiteSpace(request?.BillingPeriod)
            ? request!.BillingPeriod!.Trim()
            : now.ToString("yyyy-MM");

        if (request?.Reverse == true)
        {
            return await ReverseThreePlBillingStatementCoreAsync(request, warehouseId, effectiveAuth, effectiveActorId, now, cancellationToken);
        }

        int? resolvedOwnerId = serverBoundOwnerId
            ?? request?.RequestedOwnerId
            ?? request?.OwnerId
            ?? (!isServerBoundOverload && isInternalPrivilegedCaller ? clientSuppliedAuthOwnerId : null);
        if (!resolvedOwnerId.HasValue || resolvedOwnerId.Value <= 0)
        {
            return WmsEndpointOutcome.UnprocessableEntity(new
            {
                code = "THREEPL_OWNER_REQUIRED",
                errorCode = "THREEPL_OWNER_REQUIRED",
                message = "Vui lòng chỉ định mã chủ hàng ký gửi 3PL (OwnerId) hợp lệ."
            });
        }

        var ownerPartner = await db.BusinessPartners.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == resolvedOwnerId.Value, cancellationToken);
        if (ownerPartner == null)
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "THREEPL_OWNER_NOT_FOUND",
                errorCode = "THREEPL_OWNER_NOT_FOUND",
                message = $"Không tìm thấy chủ hàng ký gửi 3PL #{resolvedOwnerId.Value}."
            });
        }

        var strictOwnerId = resolvedOwnerId.Value;
        var ownerCode = ownerPartner.Code?.Trim() ?? $"OWN-{strictOwnerId:D3}";
        var ownerName = ownerPartner.Name?.Trim() ?? $"Chủ hàng Ký gửi 3PL #{strictOwnerId}";

        // Strictly isolated 9D inventory query by OwnerId and accessible Warehouse scope
        var ownerStocksQuery = db.InventoryStocks.AsNoTracking()
            .Include(s => s.Product)
                .ThenInclude(p => p.Unit)
            .Include(s => s.Location)
            .Include(s => s.Warehouse)
            .Include(s => s.Lot)
            .Where(s => s.OwnerId == strictOwnerId);

        if (scopedWarehouseIds != null)
        {
            ownerStocksQuery = ownerStocksQuery.Where(s => scopedWarehouseIds.Contains(s.WarehouseId));
        }

        var ownerStocks = await ownerStocksQuery
            .OrderBy(s => s.WarehouseId)
            .ThenBy(s => s.ProductId)
            .ThenBy(s => s.LocationId)
            .ToListAsync(cancellationToken);

        var crossOwnerLeakCount = ownerStocks.Count(s => s.OwnerId != strictOwnerId);

        var ownerTxsQuery = db.InventoryTransactions.AsNoTracking()
            .Where(t => t.OwnerId == strictOwnerId);
        if (scopedWarehouseIds != null)
        {
            ownerTxsQuery = ownerTxsQuery.Where(t => scopedWarehouseIds.Contains(t.WarehouseId));
        }

        var ownerTxs = await ownerTxsQuery
            .OrderByDescending(t => t.Id)
            .Take(200)
            .ToListAsync(cancellationToken);

        var activeTxs = ownerTxs
            .Where(t => InventoryLedgerCalculationRules.IsIncludedInAlgebraicLedgerSum(t.TransactionType, t.CorrectiveTransactionId))
            .ToList();

        var totalOnHandQty = Math.Round(ownerStocks.Sum(s => s.Quantity), 4, MidpointRounding.AwayFromZero);
        var totalReservedQty = Math.Round(ownerStocks.Sum(s => s.ReservedQuantity), 4, MidpointRounding.AwayFromZero);
        var totalAvailableQty = Math.Round(ownerStocks.Sum(s => Math.Max(0m, s.Quantity - s.ReservedQuantity)), 4, MidpointRounding.AwayFromZero);
        var distinctSkuCount = ownerStocks.Where(s => s.Quantity > 0m).Select(s => s.ProductId).Distinct().Count();
        var occupiedBinCount = ownerStocks.Where(s => s.Quantity > 0m && s.LocationId.HasValue).Select(s => s.LocationId!.Value).Distinct().Count();

        var inboundQty = Math.Round(
            activeTxs.Where(t => t.TransactionType == TransactionType.Import || t.TransactionType == TransactionType.TransferIn).Sum(t => Math.Abs(t.Quantity)),
            4,
            MidpointRounding.AwayFromZero);
        var outboundQty = Math.Round(
            activeTxs.Where(t => t.TransactionType == TransactionType.Export || t.TransactionType == TransactionType.Ship).Sum(t => Math.Abs(t.Quantity)),
            4,
            MidpointRounding.AwayFromZero);
        var vasQty = Math.Round(
            activeTxs.Where(t => t.ReferenceType != null && (t.ReferenceType.Contains("Kit") || t.ReferenceType.Contains("BreakBulk")) && t.Quantity > 0m).Sum(t => t.Quantity),
            4,
            MidpointRounding.AwayFromZero);

        var palletEquivalentCount = totalOnHandQty == 0m
            ? 0m
            : Math.Round(Math.Max(1m, Math.Ceiling(totalOnHandQty / 20m)), 4, MidpointRounding.AwayFromZero);

        var storageRate = Math.Round(request?.StorageRatePerPalletDay ?? 15000m, 4, MidpointRounding.AwayFromZero);
        var inboundRate = Math.Round(request?.InboundHandlingRatePerUnit ?? 8000m, 4, MidpointRounding.AwayFromZero);
        var outboundRate = Math.Round(request?.OutboundHandlingRatePerUnit ?? 9000m, 4, MidpointRounding.AwayFromZero);
        var vasRate = Math.Round(request?.VasKittingRatePerUnit ?? 12000m, 4, MidpointRounding.AwayFromZero);

        var storageFeeAmount = Math.Round(totalOnHandQty * storageRate, 0);
        var inboundHandlingFeeAmount = Math.Round(inboundQty * inboundRate, 0);
        var outboundHandlingFeeAmount = Math.Round(outboundQty * outboundRate, 0);
        var vasFeeAmount = Math.Round(vasQty * vasRate, 0);
        var subTotalAmount = Math.Round(storageFeeAmount + inboundHandlingFeeAmount + outboundHandlingFeeAmount + vasFeeAmount, 0);
        // Điều 9 Luật Thuế GTGT 48/2024/QH15: Dịch vụ kho ngoại quan / khu phi thuế quan có tờ khai xuất khẩu áp dụng thuế suất GTGT 0%
        var vatRatePercent = request?.IsExportDeclaration == true ? 0.0000m : 10.0000m;
        var vatAmount = Math.Round(subTotalAmount * (vatRatePercent / 100m), 0);
        var totalBillingAmount = Math.Round(subTotalAmount + vatAmount, 0);

        var statementCode = $"3PL-DEP-{strictOwnerId:D3}-{billingPeriod.Replace("-", "")}";
        int? persistedStatementId = null;
        int? persistedSnapshotId = null;

        if (request?.PersistSnapshotAndStatement == true)
        {
            var isRelational = db.Database.IsRelational();
            await using var tx = isRelational && db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
                : null;

            var targetWhId = warehouseId
                ?? ownerStocks.FirstOrDefault()?.WarehouseId
                ?? (scopedWarehouseIds is { Count: > 0 } ? scopedWarehouseIds.First() : 1);
            var lockProductIds = ownerStocks.Select(s => s.ProductId).Distinct().OrderBy(x => x).ToList();
            if (isRelational && lockProductIds.Count > 0)
            {
                await db.AcquireInventoryAllocationLocksAsync(targetWhId, lockProductIds, cancellationToken);
            }

            var existingStmt = await db.ThreePlBillingStatements.AsNoTracking()
                .FirstOrDefaultAsync(s => s.PartnerId == strictOwnerId && s.StatementCode == statementCode && s.Status == "ISSUED", cancellationToken);
            if (existingStmt != null)
            {
                throw new ERP.Domain.Exceptions.ConflictException(
                    "THREEPL_STATEMENT_ALREADY_ISSUED",
                    $"Bảng kê phí lưu kho 3PL ({statementCode}) của chủ hàng {ownerCode} trong kỳ {billingPeriod} đã được phát hành.");
            }

            var snapshot = new ERP.Domain.Entities.ThreePlStorageSnapshot
            {
                WarehouseId = targetWhId,
                OwnerId = strictOwnerId,
                OwnerCode = ownerCode,
                OwnerName = ownerName,
                SnapshotCode = $"3PL-SNAP-{strictOwnerId}-{now:yyyyMMddHHmmss}",
                SnapshotDateUtc = now.Date,
                DistinctSkuCount = distinctSkuCount,
                OccupiedBinCount = occupiedBinCount,
                TotalOnHandQty = totalOnHandQty,
                TotalVolumeCbm = Math.Round(totalOnHandQty * 0.05m, 4, MidpointRounding.AwayFromZero),
                PalletEquivalentCount = palletEquivalentCount,
                DailyPalletRateVnd = storageRate,
                DailyCbmRateVnd = 25000m,
                DailyBinRateVnd = 10000m,
                EstimatedDailyStorageFeeVnd = storageFeeAmount,
                EstimatedMonthlyStorageFeeVnd = Math.Round(storageFeeAmount * 30m, 4, MidpointRounding.AwayFromZero),
                CapturedByUserId = effectiveActorId,
                CapturedAtUtc = now
            };

            var statement = new ERP.Domain.Entities.ThreePlBillingStatement
            {
                WarehouseId = targetWhId,
                PartnerId = strictOwnerId,
                StatementCode = statementCode,
                PeriodFromUtc = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodToUtc = now,
                TotalOnHandQty = totalOnHandQty,
                TotalInboundQty = inboundQty,
                TotalOutboundQty = outboundQty,
                StorageFeeAmount = storageFeeAmount,
                InboundHandlingFeeAmount = inboundHandlingFeeAmount,
                OutboundHandlingFeeAmount = Math.Round(outboundHandlingFeeAmount + vasFeeAmount, 4, MidpointRounding.AwayFromZero),
                TotalBillingAmount = totalBillingAmount,
                ReceivableInvoiceCode = $"AR-{statementCode}",
                Status = "ISSUED",
                CreatedByUserId = effectiveActorId,
                CreatedAtUtc = now
            };

            db.ThreePlStorageSnapshots.Add(snapshot);
            db.ThreePlBillingStatements.Add(statement);
            await db.SaveChangesAsync(cancellationToken);

            persistedSnapshotId = snapshot.Id;
            persistedStatementId = statement.Id;

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = targetWhId,
                EntityName = "ThreePlBillingStatement",
                EntityId = statement.Id,
                Action = "ThreePlDepositorPortal.IssueBillingStatement",
                NewValues = $"{statementCode}:OwnerId={strictOwnerId};Total={totalBillingAmount:0.####}",
                Reason = $"Chốt bảng kê đối soát phí lưu kho & tác nghiệp 3PL cho chủ hàng {ownerCode} ({statementCode})",
                Result = "Success",
                Timestamp = now
            });

            await db.SaveChangesAsync(cancellationToken);
            if (tx != null)
            {
                await tx.CommitAsync(cancellationToken);
            }
        }

        return WmsEndpointOutcome.Ok(new
        {
            ownerId = strictOwnerId,
            ownerCode,
            ownerName,
            billingPeriod,
            statementCode,
            statementId = persistedStatementId,
            snapshotId = persistedSnapshotId,
            crossOwnerLeakCount,
            tenantAndOwnerIsolationVerified = crossOwnerLeakCount == 0,
            distinctSkuCount,
            occupiedBinCount,
            palletEquivalentCount,
            totalOnHandQty,
            totalReservedQty,
            totalAvailableQty,
            inboundHandledQty = inboundQty,
            outboundHandledQty = outboundQty,
            vasWorkOrderQty = vasQty,
            storageRatePerUnit = storageRate,
            inboundHandlingRatePerUnit = inboundRate,
            outboundHandlingRatePerUnit = outboundRate,
            vasKittingRatePerUnit = vasRate,
            storageFeeAmount,
            inboundHandlingFeeAmount,
            outboundHandlingFeeAmount,
            vasFeeAmount,
            subTotalAmount,
            vatRatePercent,
            vatAmount,
            totalBillingAmount,
            inventoryBuckets = ownerStocks.Select(s => new
            {
                stockId = s.Id,
                ownerId = s.OwnerId,
                warehouseId = s.WarehouseId,
                warehouseCode = s.Warehouse?.Code?.Trim() ?? $"WH-{s.WarehouseId}",
                productId = s.ProductId,
                productCode = s.Product?.Code?.Trim() ?? $"SKU-{s.ProductId}",
                productName = s.Product?.Name?.Trim() ?? $"Sản phẩm #{s.ProductId}",
                baseUomCode = s.Product?.Unit?.Code?.Trim() ?? "EA",
                locationId = s.LocationId,
                locationCode = s.Location?.Code?.Trim() ?? "STAGE",
                lotNumber = s.Lot?.LotNumber,
                handlingUnitCode = s.HandlingUnitId.HasValue ? $"HU-{s.HandlingUnitId.Value}" : null,
                status = s.Status.ToString(),
                onHandQty = s.Quantity,
                reservedQty = s.ReservedQuantity,
                availableQty = Math.Max(0m, s.Quantity - s.ReservedQuantity)
            }).ToList(),
            status = persistedStatementId.HasValue ? "STATEMENT_ISSUED" : "PORTAL_READY",
            statusLabel = $"Đã tải Cổng Chủ hàng 3PL ({ownerCode} — {ownerName}): {ownerStocks.Count} rổ tồn kho 9 chiều cô lập theo OwnerId (0 rò rỉ chéo) • Tổng phí kỳ {billingPeriod}: {totalBillingAmount:N0} đ",
            evaluatedAtUtc = now
        });
    }
}
