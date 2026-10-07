using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class InventoryReversalService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser,
    IInventoryMovementService movementService,
    IInventoryStatusService statusService) : IInventoryReversalService
{
    public async Task<InventoryReversalResultDto> ReverseAsync(
        CreateInventoryReversalDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.OriginalTransactionId <= 0)
            throw new BusinessRuleException("OriginalTransactionId phải hợp lệ.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BusinessRuleException("Lý do reversal là bắt buộc.");

        var own = context.Database.CurrentTransaction is null;
        await using var tx = own
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var original = await context.InventoryTransactions
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == request.OriginalTransactionId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy inventory transaction.");

            await warehouseAuthorization.EnsureWarehouseAccessAsync(original.WarehouseId, cancellationToken);

            if (original.TransactionType is not (TransactionType.Move or TransactionType.StatusChange))
                throw Conflict(
                    "INV_REVERSAL_UNSUPPORTED",
                    "Foundation reversal chỉ hỗ trợ Internal Move và Inventory Status Change; document-bound transaction phải đảo tại workflow chuyên biệt.");

            var alreadyReversed = await context.InventoryTransactions.AsNoTracking().AnyAsync(
                x => x.TransactionType == TransactionType.Reversal &&
                     x.ReferenceType == "InventoryReversal" &&
                     x.ReferenceId == original.Id,
                cancellationToken);
            if (alreadyReversed)
                throw Conflict("INV_ALREADY_REVERSED", "Inventory transaction đã được reversal trước đó.");

            var reason = request.Reason.Trim();
            int correctiveTransactionId;
            int? fromLocationId = null;
            int? toLocationId = null;
            InventoryStatus? fromStatus = null;
            InventoryStatus? toStatus = null;

            if (original.TransactionType == TransactionType.Move)
            {
                if (!original.FromLocationId.HasValue || !original.ToLocationId.HasValue)
                    throw Conflict("INV_REVERSAL_INVALID_SOURCE", "MOVE transaction thiếu From/To Location để reversal.");

                var source = await context.InventoryStocks.SingleOrDefaultAsync(
                    x => x.ProductId == original.ProductId &&
                         x.WarehouseId == original.WarehouseId &&
                         x.LocationId == original.ToLocationId &&
                         x.Status == original.InventoryStatus &&
                         x.LotId == original.LotId &&
                         x.SerialId == original.SerialId,
                    cancellationToken)
                    ?? throw Conflict("INV_REVERSAL_SOURCE_NOT_FOUND", "Không còn bucket tại location đích của MOVE gốc để reversal.");

                var correction = await movementService.MoveAsync(new CreateInventoryMoveDto
                {
                    InventoryStockId = source.Id,
                    DestinationLocationId = original.FromLocationId.Value,
                    Quantity = original.Quantity,
                    Reason = $"Reversal transaction {original.Id}: {reason}"
                }, cancellationToken);

                correctiveTransactionId = correction.TransactionId;
                fromLocationId = original.ToLocationId;
                toLocationId = original.FromLocationId;
            }
            else
            {
                if (!original.FromInventoryStatus.HasValue || !original.ToInventoryStatus.HasValue)
                    throw Conflict("INV_REVERSAL_INVALID_SOURCE", "StatusChange transaction thiếu From/To Status để reversal.");

                var source = await context.InventoryStocks.SingleOrDefaultAsync(
                    x => x.ProductId == original.ProductId &&
                         x.WarehouseId == original.WarehouseId &&
                         x.LocationId == original.LocationId &&
                         x.Status == original.ToInventoryStatus.Value &&
                         x.LotId == original.LotId &&
                         x.SerialId == original.SerialId,
                    cancellationToken)
                    ?? throw Conflict("INV_REVERSAL_SOURCE_NOT_FOUND", "Không còn bucket ở status đích của transaction gốc để reversal.");

                var correction = await statusService.ChangeAsync(new CreateInventoryStatusChangeDto
                {
                    InventoryStockId = source.Id,
                    Quantity = original.Quantity,
                    ToStatus = original.FromInventoryStatus.Value.ToString(),
                    Reason = $"Reversal transaction {original.Id}: {reason}"
                }, cancellationToken);

                correctiveTransactionId = correction.TransactionId;
                fromStatus = original.ToInventoryStatus;
                toStatus = original.FromInventoryStatus;
            }

            var marker = new InventoryTransaction
            {
                ProductId = original.ProductId,
                WarehouseId = original.WarehouseId,
                LocationId = original.LocationId,
                FromLocationId = fromLocationId,
                ToLocationId = toLocationId,
                LotId = original.LotId,
                SerialId = original.SerialId,
                InventoryStatus = toStatus ?? original.InventoryStatus,
                FromInventoryStatus = fromStatus,
                ToInventoryStatus = toStatus,
                TransactionType = TransactionType.Reversal,
                Quantity = original.Quantity,
                ReferenceId = original.Id,
                ReferenceType = "InventoryReversal",
                TransactionDate = DateTime.UtcNow,
                CreatedBy = currentUser.UserId,
                Note = $"CorrectiveTransactionId={correctiveTransactionId}; {reason}"
            };
            context.InventoryTransactions.Add(marker);
            context.AuditLogs.Add(new AuditLog
            {
                UserId = currentUser.UserId,
                Action = "Inventory.Reversed",
                EntityName = "InventoryTransaction",
                EntityId = original.Id,
                WarehouseId = original.WarehouseId,
                Timestamp = DateTime.UtcNow,
                OldValues = $"OriginalTransactionId: {original.Id}; Type: {original.TransactionType}; Quantity: {original.Quantity}",
                NewValues = $"CorrectiveTransactionId: {correctiveTransactionId}; ReversalType: {original.TransactionType}",
                Reason = reason,
                Result = "Success",
                Severity = "Warning"
            });

            await context.SaveChangesAsync(cancellationToken);
            if (tx is not null) await tx.CommitAsync(cancellationToken);

            return new InventoryReversalResultDto
            {
                OriginalTransactionId = original.Id,
                CorrectiveTransactionId = correctiveTransactionId,
                ReversalTransactionId = marker.Id,
                OriginalTransactionType = original.TransactionType.ToString(),
                WarehouseId = original.WarehouseId,
                ProductId = original.ProductId,
                Quantity = original.Quantity,
                FromLocationId = fromLocationId,
                ToLocationId = toLocationId,
                FromInventoryStatus = fromStatus?.ToString(),
                ToInventoryStatus = toStatus?.ToString(),
                LotId = original.LotId,
                SerialId = original.SerialId
            };
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static BusinessRuleException Conflict(string code, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["HttpStatusCode"] = 409;
        ex.Data["ErrorCode"] = code;
        return ex;
    }
}
