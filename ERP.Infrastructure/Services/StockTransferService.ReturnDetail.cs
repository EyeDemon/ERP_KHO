using ERP.Application.DTOs;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class StockTransferService
{
    private async Task<StockTransferDto> GetDetailAsync(int id, CancellationToken cancellationToken)
    {
        var entity = await GetScopedAsync(id, cancellationToken);
        var dto = Map(entity);
        dto.ReverseOfTransferId = entity.ReverseOfTransferId;
        dto.ReverseReasonCode = entity.ReverseReasonCode;
        dto.ReverseReason = entity.ReverseReason;
        dto.ReverseTransferId = await context.StockTransfers.AsNoTracking()
            .Where(x => x.ReverseOfTransferId == id)
            .Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (entity.Status == StockTransferStatus.Returned)
        {
            var returned = await context.InventoryTransactions.AsNoTracking()
                .Where(x => x.ReferenceType == "StockTransfer" && x.ReferenceId == id &&
                            x.TransactionType == TransactionType.TransferIn &&
                            x.WarehouseId == entity.SourceWarehouseId && x.ReversalOfTransactionId != null)
                .OrderBy(x => x.Id)
                .Select(x => new { x.ReasonCode, x.Note })
                .FirstOrDefaultAsync(cancellationToken);
            dto.ReturnReasonCode = returned?.ReasonCode;
            dto.ReturnReason = returned?.Note;
        }
        return dto;
    }
}
