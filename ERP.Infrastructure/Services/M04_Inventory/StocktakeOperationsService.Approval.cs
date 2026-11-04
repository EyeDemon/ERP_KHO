using System.Data;
using System.Globalization;
using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class StocktakeOperationsService
{
    public async Task<WmsEndpointOutcome> ApproveAdjustmentProposalAsync(
        long proposalAuditId,
        ApproveAdjustmentProposalInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null)
            return WmsEndpointOutcome.BadRequest(new { message = "Không thể kết nối cơ sở dữ liệu để phê duyệt đề xuất điều chỉnh." });

        var effectiveAuth = warehouseAuth ?? warehouseAccess;

        var directProposal = proposalAuditId <= int.MaxValue
            ? await db.StocktakeAdjustmentProposals
                .FirstOrDefaultAsync(p => p.Id == (int)proposalAuditId, cancellationToken)
            : null;

        var original = await db.AuditLogs.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == proposalAuditId && a.EntityName == "StocktakeAdjustmentProposal", cancellationToken);

        if (directProposal == null && original == null)
            return WmsEndpointOutcome.NotFound(new { message = "Không tìm thấy đề xuất điều chỉnh tồn kho." });

        var warehouseId = directProposal?.WarehouseId ?? original?.WarehouseId ?? 1;
        if (effectiveAuth != null)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);
        }

        if (directProposal != null && string.Equals(directProposal.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            return WmsEndpointOutcome.Conflict(new { message = "Đề xuất điều chỉnh này đã được phê duyệt và ghi sổ trước đó." });
        }

        if (original != null)
        {
            var refEntityId = (int)(proposalAuditId % int.MaxValue);
            var alreadyApproved = string.Equals(original.Result, "Approved", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(original.Result, "Đã phê duyệt & Ghi sổ", StringComparison.OrdinalIgnoreCase) ||
                                  original.Action.EndsWith(":APPROVED", StringComparison.OrdinalIgnoreCase) ||
                                  await db.AuditLogs.AsNoTracking().AnyAsync(
                                      a => a.EntityName == "StocktakeAdjustmentProposal" &&
                                           (a.Result == "Approved" || a.Result == "Đã phê duyệt & Ghi sổ" || a.Action.EndsWith(":APPROVED")) &&
                                           (a.EntityId == refEntityId || (original.NewValues != null && a.NewValues == original.NewValues)),
                                      cancellationToken);
            if (alreadyApproved)
            {
                return WmsEndpointOutcome.Conflict(new { message = "Đề xuất điều chỉnh này đã được phê duyệt và ghi sổ trước đó." });
            }
        }

        var deltaQuantity = directProposal?.DeltaQuantity ?? 0m;
        if (deltaQuantity == 0m && original != null)
        {
            decimal.TryParse(original.OldValues, NumberStyles.Any, CultureInfo.InvariantCulture, out deltaQuantity);
        }
        if (deltaQuantity == 0m)
            deltaQuantity = request?.DeltaQuantity ?? request?.QuantityDelta ?? 0m;
        if (deltaQuantity == 0m)
            return WmsEndpointOutcome.BadRequest(new { message = "Số lượng điều chỉnh bằng 0, không thể thực thi ghi sổ." });

        var resolvedProductId = directProposal?.ProductId ?? original?.EntityId;
        if (!resolvedProductId.HasValue && original != null && !string.IsNullOrWhiteSpace(original.CorrelationId))
        {
            resolvedProductId = await db.Products.AsNoTracking()
                .Where(p => p.Code == original.CorrelationId)
                .Select(p => (int?)p.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        if (!resolvedProductId.HasValue)
        {
            resolvedProductId = await db.InventoryStocks.AsNoTracking()
                .Where(s => s.WarehouseId == warehouseId)
                .Select(s => (int?)s.ProductId)
                .FirstOrDefaultAsync(cancellationToken)
                ?? await db.Products.AsNoTracking().Select(p => (int?)p.Id).FirstOrDefaultAsync(cancellationToken);
        }
        if (!resolvedProductId.HasValue)
            return WmsEndpointOutcome.BadRequest(new { message = "Không xác định được sản phẩm cần điều chỉnh tồn kho." });

        var userId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : null);
        var proposalCode = directProposal?.ProposalCode
            ?? original?.NewValues
            ?? $"ADJ-{DateTime.UtcNow:yyyyMMdd}-{proposalAuditId:D3}";
        var reasonCode = directProposal?.ReasonCode ?? original?.Action ?? "COUNT_VARIANCE";
        var effectiveAuditId = original?.Id ?? proposalAuditId;

        var execResult = await ExecuteProposalAdjustmentCoreAsync(
            effectiveAuditId,
            proposalCode,
            reasonCode,
            request?.Note?.Trim() ?? directProposal?.Note ?? original?.Reason ?? reasonCode,
            original?.CorrelationId,
            warehouseId,
            resolvedProductId.Value,
            request?.LocationId ?? directProposal?.LocationId ?? (!string.IsNullOrWhiteSpace(request?.LocationCode)
                ? await db.WarehouseLocations.AsNoTracking()
                    .Where(l => l.WarehouseId == warehouseId && l.Code == request.LocationCode.Trim())
                    .Select(l => (int?)l.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                : null),
            request?.LotNumber,
            deltaQuantity,
            userId ?? directProposal?.CreatedBy ?? original?.UserId,
            cancellationToken);

        if (!execResult.Success)
            return WmsEndpointOutcome.BadRequest(new { message = execResult.ErrorMessage });

        var linkedProposal = directProposal ?? await db.StocktakeAdjustmentProposals
            .FirstOrDefaultAsync(p => p.ProposalCode == $"{proposalCode}-{effectiveAuditId}" || p.ProposalCode == proposalCode, cancellationToken);
        if (linkedProposal != null)
        {
            linkedProposal.Status = "APPROVED";
            linkedProposal.ApprovedBy = userId ?? directProposal?.CreatedBy ?? original?.UserId;
            linkedProposal.ApprovedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return WmsEndpointOutcome.Ok(new
        {
            id = linkedProposal?.Id ?? effectiveAuditId,
            proposalId = linkedProposal?.Id,
            proposalAuditId = effectiveAuditId,
            proposalCode = linkedProposal?.ProposalCode ?? proposalCode,
            warehouseId,
            productId = resolvedProductId.Value,
            locationId = execResult.LocationId,
            deltaQuantity,
            resultingQuantity = execResult.ResultingQuantity,
            ledgerTransactionId = execResult.LedgerTransactionId,
            referenceCode = $"ADJ-PROP-{effectiveAuditId}",
            afterState = "Đã phê duyệt",
            status = "APPROVED",
            statusLabel = "Đã phê duyệt & Ghi sổ",
            approvedAtUtc = DateTime.UtcNow
        });
    }

    private async Task<(bool Success, string? ErrorMessage, int? LocationId, decimal ResultingQuantity, int? LedgerTransactionId)> ExecuteProposalAdjustmentCoreAsync(
        long proposalAuditId,
        string proposalCode,
        string actionCode,
        string reasonNote,
        string? correlationProductCode,
        int warehouseId,
        int productId,
        int? requestedLocationId,
        string? lotNumber,
        decimal deltaQuantity,
        int? actorUserId,
        CancellationToken cancellationToken)
    {
        var isRelational = db.Database.IsRelational();
        await using var tx = isRelational && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        try
        {
            await db.AcquireInventoryAllocationLocksAsync(warehouseId, new[] { productId }, cancellationToken);

            int? resolvedLotId = null;
            if (!string.IsNullOrWhiteSpace(lotNumber))
            {
                var normalizedLot = lotNumber.Trim();
                resolvedLotId = await db.InventoryLots.AsNoTracking()
                    .Where(l => l.ProductId == productId && l.LotNumber == normalizedLot)
                    .Select(l => (int?)l.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var stockQuery = db.InventoryStocks
                .Where(s => s.WarehouseId == warehouseId &&
                            s.ProductId == productId &&
                            s.Status == InventoryStatus.Available &&
                            s.SerialId == null);

            if (requestedLocationId.HasValue)
                stockQuery = stockQuery.Where(s => s.LocationId == requestedLocationId.Value);
            if (resolvedLotId.HasValue)
                stockQuery = stockQuery.Where(s => s.LotId == resolvedLotId.Value);

            var stock = await stockQuery
                .OrderByDescending(s => s.Quantity - s.ReservedQuantity)
                .ThenBy(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (stock == null)
            {
                var fallbackLocationId = requestedLocationId ?? await db.WarehouseLocations.AsNoTracking()
                    .Where(l => l.WarehouseId == warehouseId && l.IsActive && !l.IsBlocked)
                    .OrderBy(l => l.LocationType == WarehouseLocationType.Storage ? 0 : 1)
                    .ThenBy(l => l.Id)
                    .Select(l => (int?)l.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (deltaQuantity < 0m)
                {
                    if (tx != null) await tx.RollbackAsync(cancellationToken);
                    return (false, "Không tìm thấy tồn kho khả dụng để thực hiện điều chỉnh giảm.", fallbackLocationId, 0m, null);
                }

                stock = new InventoryStock
                {
                    WarehouseId = warehouseId,
                    ProductId = productId,
                    LocationId = fallbackLocationId,
                    LotId = resolvedLotId,
                    Status = InventoryStatus.Available,
                    Quantity = 0m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                };
                db.InventoryStocks.Add(stock);
            }

            var newQty = stock.Quantity + deltaQuantity;
            if (newQty < stock.ReservedQuantity || newQty < 0m)
            {
                if (tx != null) await tx.RollbackAsync(cancellationToken);
                return (
                    false,
                    $"Không thể điều chỉnh tồn kho xuống dưới mức đang giữ chỗ hoặc âm (Tồn hiện tại: {stock.Quantity}, Đang giữ: {stock.ReservedQuantity}, Lượng điều chỉnh: {deltaQuantity}).",
                    stock.LocationId,
                    stock.Quantity,
                    null);
            }

            var now = DateTime.UtcNow;
            stock.Quantity = newQty;
            stock.LastUpdated = now;

            var referenceId = (int)(proposalAuditId % int.MaxValue);
            var referenceCode = $"ADJ-PROP-{proposalAuditId}";
            var ledgerTx = new InventoryTransaction
            {
                ProductId = productId,
                WarehouseId = warehouseId,
                LocationId = stock.LocationId,
                LotId = stock.LotId,
                SerialId = stock.SerialId,
                InventoryStatus = stock.Status,
                TransactionType = deltaQuantity > 0m ? TransactionType.AdjustmentIncrease : TransactionType.AdjustmentDecrease,
                Quantity = Math.Abs(deltaQuantity),
                ReferenceId = referenceId,
                ReferenceType = "StocktakeAdjustment",
                TransactionDate = now,
                CreatedBy = actorUserId ?? 1,
                Note = $"{referenceCode}: {reasonNote}"
            };
            db.InventoryTransactions.Add(ledgerTx);

            db.AuditLogs.Add(new AuditLog
            {
                UserId = actorUserId,
                EntityName = "StocktakeAdjustmentProposal",
                EntityId = referenceId,
                WarehouseId = warehouseId,
                Action = $"{actionCode}:APPROVED",
                OldValues = deltaQuantity.ToString(CultureInfo.InvariantCulture),
                NewValues = proposalCode,
                Reason = reasonNote,
                CorrelationId = correlationProductCode,
                Result = "Success",
                Timestamp = now
            });

            await db.SaveChangesAsync(cancellationToken);
            if (tx != null) await tx.CommitAsync(cancellationToken);

            return (true, null, stock.LocationId, newQty, ledgerTx.Id);
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
