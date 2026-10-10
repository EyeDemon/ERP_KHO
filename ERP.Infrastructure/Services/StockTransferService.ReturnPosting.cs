using ERP.Domain.Entities;
using ERP.Domain.Enums;

namespace ERP.Infrastructure.Services;

public sealed partial class StockTransferService
{
    private async Task PostReturnAsync(
        StockTransfer entity, Dictionary<int, InventoryTransaction> originalOut,
        string reasonCode, string reason, DateTime now, CancellationToken cancellationToken)
    {
        foreach (var line in entity.Details.OrderBy(x => x.ProductId))
        {
            await IncreaseStockAsync(line.ProductId, entity.SourceWarehouseId, line.DispatchedQuantity, now, cancellationToken);
            var returned = Transaction(entity, line, entity.SourceWarehouseId, TransactionType.TransferIn, line.DispatchedQuantity, now);
            returned.ReversalOfTransactionId = originalOut[line.ProductId].Id;
            returned.ReasonCode = reasonCode;
            returned.Note = reason;
            context.InventoryTransactions.Add(returned);
        }
        var audit = Audit(entity, "StockTransfer.Returned", StockTransferStatus.InTransit, StockTransferStatus.Returned, now);
        audit.Reason = reason;
        audit.NewValues = $"Status: Returned; ReasonCode: {reasonCode}";
        audit.Severity = "Warning";
        context.AuditLogs.Add(audit);
        await context.SaveChangesAsync(cancellationToken);
    }
}
