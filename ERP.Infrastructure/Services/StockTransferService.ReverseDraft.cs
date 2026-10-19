using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Inventory;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class StockTransferService
{
    public async Task<StockTransferDto> CreateReverseDraftAsync(
        int id, CreateReverseStockTransferDto request, CancellationToken cancellationToken = default)
    {
        EnsureWriteRole();
        if (id <= 0) throw new BusinessRuleException("ID phiếu điều chuyển không hợp lệ.");
        var code = request.ReasonCode?.Trim().ToUpperInvariant();
        var reason = request.Reason?.Trim();
        if (!StockTransferReturnReasonCatalog.IsAllowed(code))
            throw new BusinessRuleException("Mã lý do điều chuyển ngược không hợp lệ.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 400)
            throw new BusinessRuleException("Diễn giải điều chuyển ngược bắt buộc và không được quá 400 ký tự.");

        var ownsTransaction = context.Database.CurrentTransaction is null;
        await using var tx = ownsTransaction
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
            // Lock the original before checking the reverse link. The unique filtered
            // index is a second defense against concurrent requests with different keys.
            var tenantId = context.CurrentTenantId;
            var lockedQuery = context.Database.IsSqlServer()
                ? context.StockTransfers.FromSqlInterpolated(
                    $"SELECT * FROM dbo.StockTransfers WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND Id = {id}")
                : context.StockTransfers.AsQueryable();
            var locked = await lockedQuery.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == id && allowed.Contains(x.SourceWarehouseId) &&
                allowed.Contains(x.DestinationWarehouseId), cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy phiếu trong phạm vi kho được cấp quyền.");
            await warehouseAuthorization.EnsureWarehouseAccessAsync(locked.SourceWarehouseId, cancellationToken);
            await warehouseAuthorization.EnsureWarehouseAccessAsync(locked.DestinationWarehouseId, cancellationToken);
            var original = await GetScopedAsync(id, cancellationToken);
            if (original.ReverseOfTransferId.HasValue ||
                original.Status is not (StockTransferStatus.Received or StockTransferStatus.Completed))
                throw Conflict("Chỉ tạo điều chuyển ngược cho phiếu gốc đã nhận hoặc hoàn tất.");
            if (await context.StockTransfers.AsNoTracking().AnyAsync(
                    x => x.ReverseOfTransferId == id && x.Status != StockTransferStatus.Cancelled,
                    cancellationToken))
                throw Conflict("Phiếu gốc đã có chứng từ điều chuyển ngược.");
            var lines = original.Details.Where(x => x.ReceivedQuantity > 0)
                .OrderBy(x => x.ProductId).ToList();
            if (lines.Count == 0)
                throw Conflict("Phiếu gốc không có số lượng thực nhận để điều chuyển ngược.");
            await EnsureReverseDraftAvailableLedgerAsync(original, cancellationToken);

            var now = DateTime.UtcNow;
            var reverse = new StockTransfer
            {
                Code = $"TRF-{now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                SourceWarehouseId = original.DestinationWarehouseId,
                DestinationWarehouseId = original.SourceWarehouseId,
                ReverseOfTransferId = original.Id,
                ReverseReasonCode = code,
                ReverseReason = reason,
                Note = $"Điều chuyển ngược từ {original.Code.Trim()}",
                CreatedBy = currentUser.UserId,
                CreatedAt = now,
                Details = lines.Select(x => new StockTransferDetail
                {
                    ProductId = x.ProductId, RequestedQuantity = x.ReceivedQuantity
                }).ToList()
            };
            context.StockTransfers.Add(reverse);
            await context.SaveChangesAsync(cancellationToken);
            var audit = Audit(reverse, "StockTransfer.ReverseDraftCreated",
                StockTransferStatus.Draft, StockTransferStatus.Draft, now);
            audit.Reason = reason;
            audit.NewValues = $"ReverseOfTransferId: {id}; ReasonCode: {code}; Status: Draft";
            context.AuditLogs.Add(audit);
            await context.SaveChangesAsync(cancellationToken);
            if (tx is not null) await tx.CommitAsync(cancellationToken);
            if (ownsTransaction) context.ChangeTracker.Clear();
            return await GetByIdAsync(reverse.Id, cancellationToken);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task EnsureReverseDraftAvailableLedgerAsync(StockTransfer original, CancellationToken cancellationToken)
    {
        var outbound = await context.InventoryTransactions.AsNoTracking()
            .Where(x => x.ReferenceType == "StockTransfer" && x.ReferenceId == original.Id &&
                x.TransactionType == TransactionType.TransferOut && x.WarehouseId == original.SourceWarehouseId)
            .ToListAsync(cancellationToken);
        var inboundAvailable = await context.InventoryTransactions.AsNoTracking()
            .Where(x => x.ReferenceType == "StockTransfer" && x.ReferenceId == original.Id &&
                x.TransactionType == TransactionType.TransferIn &&
                x.InventoryStatus == InventoryStatus.Available &&
                x.WarehouseId == original.DestinationWarehouseId)
            .ToListAsync(cancellationToken);
        if (outbound.Count != original.Details.Count ||
            inboundAvailable.Count != original.Details.Count(x => x.ReceivedQuantity > 0))
            throw Conflict("Sổ cái xuất/nhận không khớp chứng từ.");
        foreach (var line in original.Details)
        {
            if (outbound.Count(x => x.ProductId == line.ProductId && x.Quantity == line.DispatchedQuantity) != 1 ||
                inboundAvailable.Count(x => x.ProductId == line.ProductId && x.Quantity == line.ReceivedQuantity) !=
                    (line.ReceivedQuantity > 0 ? 1 : 0))
                throw Conflict("Sổ cái xuất/nhận không khớp chứng từ.");
        }
    }
}
