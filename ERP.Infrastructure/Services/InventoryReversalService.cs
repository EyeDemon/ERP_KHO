using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Common;
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
    public async Task<PagedResult<InventoryReversalCandidateDto>> GetCandidatesAsync(
        int? warehouseId = null, int page = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (warehouseId.HasValue && warehouseId.Value <= 0)
            throw new BusinessRuleException("Mã kho không hợp lệ.");
        if (warehouseId.HasValue)
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);

        var permittedWarehouseIds = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        var safePage = Math.Max(1, page);
        var safeSize = pageSize is >= 1 and <= 100 ? pageSize : 20;

        var candidates = context.InventoryTransactions.AsNoTracking()
            .Where(x => permittedWarehouseIds.Contains(x.WarehouseId)
                && (!warehouseId.HasValue || x.WarehouseId == warehouseId.Value)
                && (x.TransactionType == TransactionType.Move || x.TransactionType == TransactionType.StatusChange));

        var count = await candidates.CountAsync(cancellationToken);
        var items = await candidates.OrderByDescending(x => x.TransactionDate).ThenByDescending(x => x.Id)
            .Skip((safePage - 1) * safeSize).Take(safeSize)
            .Select(x => new InventoryReversalCandidateDto
            {
                Id = x.Id,
                ProductId = x.ProductId,
                ProductCode = x.Product.Code,
                ProductName = x.Product.Name,
                WarehouseId = x.WarehouseId,
                WarehouseName = x.Warehouse.Name,
                TransactionType = x.TransactionType.ToString(),
                InventoryStatus = x.InventoryStatus.ToString(),
                FromInventoryStatus = x.FromInventoryStatus.HasValue ? x.FromInventoryStatus.Value.ToString() : null,
                ToInventoryStatus = x.ToInventoryStatus.HasValue ? x.ToInventoryStatus.Value.ToString() : null,
                LotNumber = x.Lot == null ? null : x.Lot.LotNumber,
                SerialNumber = x.Serial == null ? null : x.Serial.SerialNumber,
                Quantity = x.Quantity,
                TransactionDate = x.TransactionDate,
                // Database-authoritative: never infer reversal state from a limited frontend page.
                IsReversed = context.InventoryTransactions.Any(marker => marker.ReversalOfTransactionId == x.Id)
            }).ToListAsync(cancellationToken);

        return new PagedResult<InventoryReversalCandidateDto>
        {
            Items = items, TotalRecords = count, PageIndex = safePage, PageSize = safeSize
        };
    }

    public async Task<InventoryReversalResultDto> ReverseAsync(
        CreateInventoryReversalDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.OriginalTransactionId <= 0)
            throw new BusinessRuleException("ID giao dịch gốc không hợp lệ.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BusinessRuleException("Lý do đảo giao dịch là bắt buộc.");

        var own = context.Database.CurrentTransaction is null;
        await using var tx = own
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var original = await context.InventoryTransactions
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == request.OriginalTransactionId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy giao dịch tồn kho.");

            await warehouseAuthorization.EnsureWarehouseAccessAsync(original.WarehouseId, cancellationToken);

            if (original.TransactionType is not (TransactionType.Move or TransactionType.StatusChange))
                throw Conflict(
                    "INV_REVERSAL_UNSUPPORTED",
                    "Phạm vi hiện tại chỉ hỗ trợ đảo giao dịch di chuyển vị trí nội bộ và đổi trạng thái tồn kho; giao dịch gắn với chứng từ phải được đảo tại quy trình nghiệp vụ chuyên biệt.");

            var reason = request.Reason.Trim();
            int? fromLocationId = null;
            int? toLocationId = null;
            InventoryStatus? fromStatus = null;
            InventoryStatus? toStatus = null;

            if (original.TransactionType == TransactionType.Move)
            {
                if (!original.FromLocationId.HasValue || !original.ToLocationId.HasValue)
                    throw Conflict("INV_REVERSAL_INVALID_SOURCE", "Giao dịch MOVE thiếu vị trí nguồn/đích để thực hiện đảo giao dịch.");

                fromLocationId = original.ToLocationId;
                toLocationId = original.FromLocationId;
            }
            else
            {
                if (!original.FromInventoryStatus.HasValue || !original.ToInventoryStatus.HasValue)
                    throw Conflict("INV_REVERSAL_INVALID_SOURCE", "Giao dịch StatusChange thiếu trạng thái trước/sau để thực hiện đảo giao dịch.");

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
                    ?? throw Conflict("INV_REVERSAL_SOURCE_NOT_FOUND", "Không còn nhóm tồn kho tại vị trí đích của giao dịch MOVE gốc để thực hiện đảo giao dịch.");

                var correction = await movementService.MoveAsync(new CreateInventoryMoveDto
                {
                    InventoryStockId = source.Id,
                    DestinationLocationId = original.FromLocationId!.Value,
                    Quantity = original.Quantity,
                    Reason = $"Đảo giao dịch {original.Id}: {reason}"
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
                    ?? throw Conflict("INV_REVERSAL_SOURCE_NOT_FOUND", "Không còn nhóm tồn kho ở trạng thái đích của giao dịch gốc để thực hiện đảo giao dịch.");

                var correction = await statusService.ChangeAsync(new CreateInventoryStatusChangeDto
                {
                    InventoryStockId = source.Id,
                    Quantity = original.Quantity,
                    ToStatus = original.FromInventoryStatus!.Value.ToString(),
                    Reason = $"Đảo giao dịch {original.Id}: {reason}"
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
            throw Conflict("INV_ALREADY_REVERSED", "Giao dịch tồn kho đã được đảo trước đó.");
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
