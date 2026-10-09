using System.Data;
using ERP.Application.DTOs;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class StockTransferService
{
    public async Task ReturnAsync(int id, ReturnStockTransferDto request, CancellationToken cancellationToken = default)
    {
        EnsureWriteRole();
        var (reasonCode, reason) = ValidateReturnReason(request);
        var ownsTransaction = context.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        try
        {
            var entity = await GetLockedScopedAsync(id, cancellationToken);
            await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.SourceWarehouseId, cancellationToken);
            await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.DestinationWarehouseId, cancellationToken);
            var originalOut = await ValidateReturnPostingAsync(entity, cancellationToken);
            var claimed = await context.StockTransfers.Where(x => x.Id == id && x.Status == StockTransferStatus.InTransit &&
                x.SourceWarehouseId == entity.SourceWarehouseId && x.DestinationWarehouseId == entity.DestinationWarehouseId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, StockTransferStatus.Returned), cancellationToken);
            if (claimed != 1) throw Conflict("Phiếu đã được nhận hoặc hoàn trả bởi thao tác khác.");
            await PostReturnAsync(entity, originalOut, reasonCode, reason, DateTime.UtcNow, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            if (ownsTransaction) context.ChangeTracker.Clear();
        }
        catch { if (transaction is not null) await transaction.RollbackAsync(cancellationToken); throw; }
    }
}
