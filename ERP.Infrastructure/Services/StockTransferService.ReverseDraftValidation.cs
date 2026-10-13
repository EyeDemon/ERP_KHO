using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class StockTransferService
{
    private async Task EnsureReverseDraftReceiptLedgerAsync(StockTransfer original, CancellationToken cancellationToken)
    {
        var outbound = await context.InventoryTransactions.AsNoTracking()
            .Where(x => x.ReferenceType == "StockTransfer" && x.ReferenceId == original.Id &&
                x.TransactionType == TransactionType.TransferOut && x.WarehouseId == original.SourceWarehouseId)
            .ToListAsync(cancellationToken);
        var inbound = await context.InventoryTransactions.AsNoTracking()
            .Where(x => x.ReferenceType == "StockTransfer" && x.ReferenceId == original.Id &&
                x.TransactionType == TransactionType.TransferIn && x.WarehouseId == original.DestinationWarehouseId)
            .ToListAsync(cancellationToken);
        if (outbound.Count != original.Details.Count ||
            inbound.Count != original.Details.Count(x => x.ReceivedQuantity > 0))
            throw Conflict("Sổ cái xuất/nhận không khớp chứng từ.");
        foreach (var line in original.Details)
        {
            if (outbound.Count(x => x.ProductId == line.ProductId && x.Quantity == line.DispatchedQuantity) != 1 ||
                inbound.Count(x => x.ProductId == line.ProductId && x.Quantity == line.ReceivedQuantity) !=
                    (line.ReceivedQuantity > 0 ? 1 : 0))
                throw Conflict("Sổ cái xuất/nhận không khớp chứng từ.");
        }
    }
}
