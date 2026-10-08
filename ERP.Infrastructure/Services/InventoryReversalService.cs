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
    public async Task<IReadOnlyList<InventoryReversalWarehouseDto>> GetReversalWarehousesAsync(
        CancellationToken cancellationToken = default)
    {
        var accessible = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        return await context.Warehouses.AsNoTracking()
            .Where(x => accessible.Contains(x.Id))
            .OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Select(x => new InventoryReversalWarehouseDto
            {
                Id = x.Id, Code = x.Code, Name = x.Name
            }).ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<InventoryReversalCandidateDto>> GetCandidatesAsync(
        int? warehouseId = null, int page = 1, int pageSize = 20,
        int? transactionId = null, CancellationToken cancellationToken = default)
    {
        if (transactionId.HasValue && transactionId.Value <= 0)
            throw new BusinessRuleException("ID giao dịch tìm kiếm không hợp lệ.");
        if (warehouseId.HasValue && warehouseId.Value <= 0)
            throw new BusinessRuleException("Mã kho không hợp lệ.");
        if (warehouseId.HasValue)
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);

        var permittedWarehouseIds = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        var safePage = Math.Max(1, page);
        var safeSize = pageSize is >= 1 and <= 100 ? pageSize : 20;
        var offset = (long)(safePage - 1) * safeSize;
        if (offset > int.MaxValue)
            throw new BusinessRuleException("Số trang vượt quá phạm vi tra cứu cho phép.");

        var candidates = context.InventoryTransactions.AsNoTracking()
            .Where(x => permittedWarehouseIds.Contains(x.WarehouseId)
                && (!warehouseId.HasValue || x.WarehouseId == warehouseId.Value)
                && (!transactionId.HasValue || x.Id == transactionId.Value)
                && (x.TransactionType == TransactionType.Move || x.TransactionType == TransactionType.StatusChange)
                // A corrective leg already belongs to an immutable reversal chain.
                && !context.InventoryTransactions.Any(marker => marker.CorrectiveTransactionId == x.Id));

        var count = await candidates.CountAsync(cancellationToken);
        var items = await candidates.OrderByDescending(x => x.TransactionDate).ThenByDescending(x => x.Id)
            .Skip((int)offset).Take(safeSize)
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
        if (request.Reason.Trim().Length > 400)
            throw new BusinessRuleException("Lý do đảo giao dịch không được quá 400 ký tự.");

        var own = context.Database.CurrentTransaction is null;
        await using var tx = own
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            // An UPDLOCK on the immutable original serializes concurrent reversal claims.
            // Without it, two SERIALIZABLE readers may both hold shared locks and
            // deadlock when they attempt to insert competing reversal markers.
            // The unique ReversalOfTransactionId index remains the final invariant.
            var originalQuery = context.Database.IsSqlServer()
                ? context.InventoryTransactions.FromSqlInterpolated(
                    $"SELECT * FROM dbo.InventoryTransactions WITH (UPDLOCK, HOLDLOCK) WHERE Id = {request.OriginalTransactionId}")
                : context.InventoryTransactions.AsQueryable();

            var original = await originalQuery.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == request.OriginalTransactionId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy giao dịch tồn kho.");

            await warehouseAuthorization.EnsureWarehouseAccessAsync(original.WarehouseId, cancellationToken);

            // This check occurs only after the original row lock is acquired.
            // A second concurrent request sees the first transaction's committed marker.
            if (await context.InventoryTransactions.AsNoTracking().AnyAsync(
                    x => x.ReversalOfTransactionId == original.Id, cancellationToken))
                throw Conflict("INV_ALREADY_REVERSED", "Giao dịch tồn kho đã được đảo trước đó.");

            if (original.TransactionType is not (TransactionType.Move or TransactionType.StatusChange))
                throw Conflict(
                    "INV_REVERSAL_UNSUPPORTED",
                    "Phạm vi hiện tại chỉ hỗ trợ đảo giao dịch di chuyển vị trí nội bộ và đổi trạng thái tồn kho; giao dịch gắn với chứng từ phải được đảo tại quy trình nghiệp vụ chuyên biệt.");

            if (await context.InventoryTransactions.AsNoTracking().AnyAsync(
                    x => x.CorrectiveTransactionId == original.Id, cancellationToken))
                throw Conflict(
                    "INV_REVERSAL_CORRECTIVE_NOT_ALLOWED",
                    "Giao dịch hiệu chỉnh thuộc một chuỗi đảo đã ghi sổ; không được đảo riêng lẻ. Hãy thực hiện quy trình hiệu chỉnh mới có kiểm soát.");

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

                if (source.Quantity - source.ReservedQuantity < original.Quantity)
                    throw Conflict("INV_REVERSAL_INSUFFICIENT_STOCK",
                        "Tồn khả dụng tại vị trí/trạng thái đích không đủ để đảo toàn bộ giao dịch. Hãy kiểm tra các nghiệp vụ đã phát sinh sau giao dịch gốc.");

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

                if (source.Quantity - source.ReservedQuantity < original.Quantity)
                    throw Conflict("INV_REVERSAL_INSUFFICIENT_STOCK",
                        "Tồn khả dụng tại vị trí/trạng thái đích không đủ để đảo toàn bộ giao dịch. Hãy kiểm tra các nghiệp vụ đã phát sinh sau giao dịch gốc.");

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
