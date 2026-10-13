using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class StockTransferService
{
    private async Task<Dictionary<int, InventoryTransaction>> ValidateReturnPostingAsync(
        StockTransfer entity, CancellationToken cancellationToken)
    {
        if (entity.Status != StockTransferStatus.InTransit)
            throw Conflict("Chỉ phiếu đã xuất kho nhưng chưa nhận mới được hoàn trả về kho nguồn.");

        var originalOut = await context.InventoryTransactions.AsNoTracking()
            .Where(x => x.ReferenceType == "StockTransfer" && x.ReferenceId == entity.Id &&
                        x.TransactionType == TransactionType.TransferOut && x.WarehouseId == entity.SourceWarehouseId)
            .ToDictionaryAsync(x => x.ProductId, cancellationToken);
        if (originalOut.Count != entity.Details.Count ||
            entity.Details.Any(line => line.DispatchedQuantity <= 0 ||
                !originalOut.TryGetValue(line.ProductId, out var posted) ||
                posted.Quantity != line.DispatchedQuantity))
            throw Conflict("Chứng từ xuất kho không khớp với sổ cái; không thể tự động hoàn trả.");
        var originalIds = originalOut.Values.Select(x => x.Id).ToArray();
        if (await context.InventoryTransactions.AsNoTracking().AnyAsync(x =>
                (x.ReversalOfTransactionId.HasValue &&
                 originalIds.Contains(x.ReversalOfTransactionId.Value)) ||
                (x.TransactionType == TransactionType.Reversal &&
                 x.ReversalOfTransactionId == null &&
                 x.ReferenceType == "InventoryReversal" &&
                 x.ReferenceId.HasValue &&
                 originalIds.Contains(x.ReferenceId.Value) &&
                 x.WarehouseId == entity.SourceWarehouseId), cancellationToken))
            throw Conflict("Giao dịch xuất điều chuyển đã có liên kết đảo; không được hoàn trả lần nữa.");
        return originalOut;
    }
}
