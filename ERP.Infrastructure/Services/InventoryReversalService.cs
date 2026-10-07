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

            var reason = request.Reason.Trim();
            int? fromLocationId = null;
            int? toLocationId = null;
            InventoryStatus? fromStatus = null;
            InventoryStatus? toStatus = null;

            if (original.TransactionType == TransactionType.Move)
            {
                if (!original.FromLocationId.HasValue || !original.ToLocationId.HasValue)
                    throw Conflict("INV_REVERSAL_INVALID_SOURCE", "MOVE transaction thiếu From/To Location để reversal.");

                fromLocationId = original.ToLocationId;
                toLocationId = original.FromLocationId;
            }
            else
            {
                if (!original.FromInventoryStatus.HasValue || !original.ToInventoryStatus.HasValue)
                    throw Conflict("INV_REVERSAL_INVALID_SOURCE", "StatusChange transaction thiếu From/To Status để reversal.");

                fromStatus = original.ToInventoryStatus;
                toStatus = original.FromInventoryStatus;
            }

            // Claim the original transaction before touching stock. The filtered unique index on
            // ReversalOfTransactionId is the database-authoritative one-reversal guard.
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
                ReversalOfTransactionId = original.Id,
                TransactionDate = DateTime.UtcNow,
                CreatedBy = currentUser.UserId,
                Note = reason
            };
            context.InventoryTransactions.Add(marker);
            await context.SaveChangesAsync(cancellationToken);

            int correctiveTransactionId;
            if (original.TransactionType == TransactionType.Move)
            {
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
                    DestinationLocationId = original.FromLocationId!.Value,
                    Quantity = original.Quantity,
                    Reason = $"Reversal transaction {original.Id}: {reason}"
                }, cancellationToken);

                correctiveTransactionId = correction.TransactionId;
            }
            else
            {
                var source = await context.InventoryStocks.SingleOrDefaultAsync(
                    x => x.ProductId == original.ProductId &&
                         x.WarehouseId == original.WarehouseId &&
                         x.LocationId == original.LocationId &&
                         x.Status == original.ToInventoryStatus!.Value &&
                         x.LotId == original.LotId &&
                         x.SerialId == original.SerialId,
                    cancellationToken)
                    ?? throw Conflict("INV_REVERSAL_SOURCE_NOT_FOUND", "Không còn bucket ở status đích của transaction gốc để reversal.");

                var correction = await statusService.ChangeAsync(new CreateInventoryStatusChangeDto
                {
                    InventoryStockId = source.Id,
                    Quantity = original.Quantity,
                    ToStatus = original.FromInventoryStatus!.Value.ToString(),
                    Reason = $"Reversal transaction {original.Id}: {reason}"
                }, cancellationToken);

                correctiveTransactionId = correction.TransactionId;
            }

            marker.CorrectiveTransactionId = correctiveTransactionId;
            context.AuditLogs.Add(new AuditLog
            {
                UserId = currentUser.UserId,
                Action = "Inventory.Reversed",
                EntityName = "InventoryTransaction",
                EntityId = original.Id,
                WarehouseId = original.WarehouseId,
                Timestamp = DateTime.UtcNow,
                OldValues = $"OriginalTransactionId: {original.Id}; Type: {original.TransactionType}; Quantity: {original.Quantity}",
                NewValues = $"ReversalTransactionId: {marker.Id}; CorrectiveTransactionId: {correctiveTransactionId}; ReversalType: {original.TransactionType}",
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
        catch (DbUpdateException ex) when (IsReversalClaimDuplicate(ex))
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
            throw Conflict("INV_ALREADY_REVERSED", "Inventory transaction đã được reversal trước đó.");
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static bool IsReversalClaimDuplicate(DbUpdateException exception)
    {
        const string reversalClaimIndex = "UX_InventoryTransactions_ReversalOfTransaction";
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is not SqlException sqlException)
                continue;

            foreach (SqlError error in sqlException.Errors)
                if (error.Number is 2601 or 2627 &&
                    error.Message.Contains(reversalClaimIndex, StringComparison.OrdinalIgnoreCase))
                    return true;
        }

        return false;
    }

    private static BusinessRuleException Conflict(string code, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["HttpStatusCode"] = 409;
        ex.Data["ErrorCode"] = code;
        return ex;
    }
}
