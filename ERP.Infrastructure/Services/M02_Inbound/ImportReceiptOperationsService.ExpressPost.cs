using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ImportReceiptOperationsService
{
    public async Task<WmsEndpointOutcome> ExpressPostAsync(
        int id,
        ExpressPostImportReceiptInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null)
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "RECEIPT_NOT_FOUND",
                errorCode = "RECEIPT_NOT_FOUND",
                message = $"Không tìm thấy phiếu nhập kho #{id}."
            });
        }

        var receipt = await db.ImportReceipts
            .Include(r => r.Details)
                .ThenInclude(d => d.Product)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (receipt == null)
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "RECEIPT_NOT_FOUND",
                errorCode = "RECEIPT_NOT_FOUND",
                message = $"Không tìm thấy phiếu nhập kho #{id}."
            });
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (effectiveAuth != null)
            await effectiveAuth.EnsureWarehouseAccessAsync(receipt.WarehouseId, cancellationToken);

        if (receipt.Status == ERP.Domain.Enums.ReceiptStatus.Posted)
        {
            return WmsEndpointOutcome.Conflict(new
            {
                code = "RECEIPT_ALREADY_POSTED",
                errorCode = "RECEIPT_ALREADY_POSTED",
                message = "Phiếu nhập kho đã được ghi sổ tồn kho (Posted) trước đó."
            });
        }

        if (receipt.Status == ERP.Domain.Enums.ReceiptStatus.Cancelled)
        {
            return WmsEndpointOutcome.Conflict(new
            {
                code = "RECEIPT_CANCELLED",
                errorCode = "RECEIPT_CANCELLED",
                message = "Phiếu nhập kho đã bị hủy, không thể thực hiện nhập nhanh 1 bước."
            });
        }

        if (receipt.Details.Any(d => d.RequiresQc && d.QcState != ERP.Domain.Enums.ReceiptLineQcState.QcCompleted))
        {
            return WmsEndpointOutcome.UnprocessableEntity(new
            {
                code = "EXPRESS_POST_REQUIRES_QC_COMPLETION",
                errorCode = "EXPRESS_POST_REQUIRES_QC_COMPLETION",
                message = "Phiếu nhập có mặt hàng bắt buộc kiểm định QC; không thể nhập nhanh 1 bước khi chưa hoàn tất QC."
            });
        }

        var targetLocationId = request?.TargetLocationId;
        if (!targetLocationId.HasValue && !string.IsNullOrWhiteSpace(request?.TargetLocationCode))
        {
            var code = request.TargetLocationCode.Trim();
            targetLocationId = await db.WarehouseLocations.AsNoTracking()
                .Where(l => l.WarehouseId == receipt.WarehouseId && l.Code == code)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        targetLocationId ??= await db.WarehouseLocations.AsNoTracking()
            .Where(l => l.WarehouseId == receipt.WarehouseId && l.IsActive && !l.IsBlocked)
            .OrderBy(l => l.LocationType == ERP.Domain.Enums.WarehouseLocationType.Storage ? 0 : (l.LocationType == ERP.Domain.Enums.WarehouseLocationType.Receiving ? 1 : 2))
            .ThenBy(l => l.Id)
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : receipt.CreatedBy);
        int? validAuditUserId = null;
        if (effectiveActorId > 0)
        {
            if (await db.Users.AsNoTracking().AnyAsync(u => u.Id == effectiveActorId, cancellationToken) ||
                (!db.Database.IsRelational() && !await db.Users.AsNoTracking().AnyAsync(cancellationToken)))
            {
                validAuditUserId = effectiveActorId;
            }
        }

        var tenantId = db.CurrentTenantId > 0 ? db.CurrentTenantId : 1;
        var gate = _dispositionLocks.GetOrAdd((tenantId, -id), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var tx = db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;
            try
            {
                await db.AcquireInventoryAllocationLocksAsync(
                    receipt.WarehouseId,
                    receipt.Details.Select(d => d.ProductId),
                    cancellationToken);

                var totalBasePostedQuantity = 0m;
                foreach (var detail in receipt.Details.OrderBy(d => d.ProductId).ThenBy(d => d.Id))
                {
                    var opQty = detail.AcceptedQuantity > 0m
                        ? detail.AcceptedQuantity
                        : (detail.ReceivedQuantity > 0m
                            ? detail.ReceivedQuantity
                            : (detail.ExpectedQuantity > 0m ? detail.ExpectedQuantity : detail.Quantity));
                    var conv = detail.ConversionFactor > 0m ? detail.ConversionFactor : 1m;
                    var baseQty = detail.BaseAcceptedQuantity > 0m
                        ? detail.BaseAcceptedQuantity
                        : (detail.BaseReceivedQuantity > 0m
                            ? detail.BaseReceivedQuantity
                            : (detail.BaseExpectedQuantity > 0m
                                ? detail.BaseExpectedQuantity
                                : Math.Round(opQty * conv, 4, MidpointRounding.AwayFromZero)));

                    if (detail.ExpectedQuantity <= 0m) detail.ExpectedQuantity = opQty;
                    if (detail.BaseExpectedQuantity <= 0m) detail.BaseExpectedQuantity = baseQty;
                    if (detail.ReceivedQuantity <= 0m) detail.ReceivedQuantity = opQty;
                    if (detail.AcceptedQuantity <= 0m) detail.AcceptedQuantity = opQty;
                    detail.PostedQuantity = opQty;
                    detail.FinalReceivedQuantity = opQty;
                    if (detail.BaseReceivedQuantity <= 0m) detail.BaseReceivedQuantity = baseQty;
                    if (detail.BaseAcceptedQuantity <= 0m) detail.BaseAcceptedQuantity = baseQty;
                    detail.BasePostedQuantity = baseQty;
                    detail.BaseFinalReceivedQuantity = baseQty;

                    totalBasePostedQuantity += baseQty;

                    var unitCost = detail.UnitPrice > 0m
                        ? (conv > 0m ? Math.Round(detail.UnitPrice / conv, 4, MidpointRounding.AwayFromZero) : detail.UnitPrice)
                        : (detail.Product?.MovingAverageCost ?? 0m);

                    var stock = await db.InventoryStocks.FirstOrDefaultAsync(
                        s => s.WarehouseId == receipt.WarehouseId &&
                             s.ProductId == detail.ProductId &&
                             s.LocationId == targetLocationId &&
                             s.LotId == null &&
                             s.SerialId == null &&
                             s.HandlingUnitId == null &&
                             s.OwnerId == null &&
                             s.Status == ERP.Domain.Enums.InventoryStatus.Available,
                        cancellationToken);

                    if (stock == null)
                    {
                        stock = new ERP.Domain.Entities.InventoryStock
                        {
                            TenantId = receipt.TenantId,
                            WarehouseId = receipt.WarehouseId,
                            ProductId = detail.ProductId,
                            LocationId = targetLocationId,
                            Status = ERP.Domain.Enums.InventoryStatus.Available,
                            Quantity = baseQty,
                            ReservedQuantity = 0m,
                            MovingAverageCost = unitCost,
                            LastUpdated = now
                        };
                        stock.RefreshGrainHash();
                        db.InventoryStocks.Add(stock);
                    }
                    else
                    {
                        var oldQty = Math.Max(0m, stock.Quantity);
                        var newTotalQty = oldQty + baseQty;
                        if (newTotalQty > 0m && unitCost > 0m)
                        {
                            stock.MovingAverageCost = Math.Round(
                                ((oldQty * stock.MovingAverageCost) + (baseQty * unitCost)) / newTotalQty,
                                4,
                                MidpointRounding.AwayFromZero);
                        }

                        stock.Quantity += baseQty;
                        stock.LastUpdated = now;
                        stock.RefreshGrainHash();
                    }

                    if (detail.Product != null && stock.MovingAverageCost > 0m)
                    {
                        detail.Product.MovingAverageCost = stock.MovingAverageCost;
                    }

                    db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
                    {
                        TenantId = receipt.TenantId,
                        ProductId = detail.ProductId,
                        WarehouseId = receipt.WarehouseId,
                        LocationId = targetLocationId,
                        ToLocationId = targetLocationId,
                        InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                        ToInventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                        TransactionType = ERP.Domain.Enums.TransactionType.Import,
                        Quantity = baseQty,
                        UnitCost = unitCost,
                        ReferenceId = receipt.Id,
                        ReferenceType = "ImportReceipt",
                        SourceDocumentLineId = detail.Id,
                        TransactionDate = now,
                        CreatedBy = effectiveActorId > 0 ? effectiveActorId : 1,
                        Note = string.IsNullOrWhiteSpace(request?.Note)
                            ? $"{receipt.Code}: Nhập kho nhanh 1 bước (Express Inbound)"
                            : request.Note.Trim()
                    });
                }

                receipt.Status = ERP.Domain.Enums.ReceiptStatus.Posted;
                receipt.ApprovedBy ??= effectiveActorId > 0 ? effectiveActorId : 1;
                receipt.ApprovedAt ??= now;
                if (!db.Database.IsSqlServer())
                {
                    receipt.RowVersion = Guid.NewGuid().ToByteArray()[..8];
                }

                db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
                {
                    UserId = validAuditUserId,
                    WarehouseId = receipt.WarehouseId,
                    EntityName = "ImportReceipt",
                    EntityId = receipt.Id,
                    Action = "ImportReceipt.ExpressPosted",
                    NewValues = $"Status: Posted; ExpressMode: true; TargetLocationId: {targetLocationId}; TotalBaseQty: {totalBasePostedQuantity}",
                    Reason = string.IsNullOrWhiteSpace(request?.Note)
                        ? $"Nhập kho nhanh 1 bước cho phiếu nhập {receipt.Code}"
                        : request.Note.Trim(),
                    Result = "Success",
                    Timestamp = now
                });

                await db.SaveChangesAsync(cancellationToken);
                if (tx != null) await tx.CommitAsync(cancellationToken);

                return WmsEndpointOutcome.Ok(new
                {
                    id = receipt.Id,
                    code = receipt.Code,
                    status = "Posted",
                    expressMode = true,
                    targetLocationId,
                    postedLineCount = receipt.Details.Count,
                    totalBasePostedQuantity,
                    message = "Đã nhập kho nhanh 1 bước và ghi sổ tồn kho thành công."
                });
            }
            catch
            {
                if (tx != null) await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
