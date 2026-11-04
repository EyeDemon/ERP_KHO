using System.Data;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class StockTransferService(
    ErpKhoDbContext context,
    IInventoryStockRepository stockRepository,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser) : IStockTransferService
{
    public async Task<PagedResult<StockTransferDto>> GetAsync(StockTransferQueryDto request, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, request.PageIndex);
        var size = Math.Clamp(request.PageSize, 1, 100);
        var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        var query = context.StockTransfers.AsNoTracking()
            .Where(x => allowed.Contains(x.SourceWarehouseId) || allowed.Contains(x.DestinationWarehouseId));
        if (!string.IsNullOrWhiteSpace(request.Search)) query = query.Where(x => x.Code.Contains(request.Search));
        if (request.Status.HasValue) query = query.Where(x => x.Status == request.Status);
        if (request.SourceWarehouseId.HasValue) query = query.Where(x => x.SourceWarehouseId == request.SourceWarehouseId);
        if (request.DestinationWarehouseId.HasValue) query = query.Where(x => x.DestinationWarehouseId == request.DestinationWarehouseId);
        if (request.FromDate.HasValue) query = query.Where(x => x.CreatedAt >= request.FromDate.Value);
        if (request.ToDate.HasValue)
        {
            // A date-only upper bound includes the entire day. DateTime.MaxValue
            // must not be incremented, otherwise a valid query throws 500.
            var inclusiveEndDate = request.ToDate.Value.Date;
            if (inclusiveEndDate < DateTime.MaxValue.Date)
            {
                var exclusiveEnd = inclusiveEndDate.AddDays(1);
                query = query.Where(x => x.CreatedAt < exclusiveEnd);
            }
        }

        var total = await query.CountAsync(cancellationToken);
        // Skip uses Int32 in EF/SQL Server. Compute outside Int32 and avoid
        // querying an impossible page instead of overflowing into a negative offset.
        var offset = ((long)page - 1) * size;
        if (offset >= total)
        {
            return new PagedResult<StockTransferDto>
            {
                Items = [], TotalRecords = total, PageIndex = page, PageSize = size
            };
        }

        var rows = await query.Include(x => x.SourceWarehouse).Include(x => x.DestinationWarehouse)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(checked((int)offset)).Take(size).ToListAsync(cancellationToken);
        return new PagedResult<StockTransferDto> { Items = rows.Select(MapSummary).ToList(), TotalRecords = total, PageIndex = page, PageSize = size };
    }

    public Task<StockTransferDto> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        GetDetailAsync(id, cancellationToken);

    public async Task<StockTransferDto> CreateAsync(CreateStockTransferDto request, CancellationToken cancellationToken = default)
    {
        EnsureWriteRole();
        var ownsTransaction = context.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        int id;
        try
        {
            await ValidateDraftAsync(request, cancellationToken);
            var now = DateTime.UtcNow;
            var entity = new StockTransfer
            {
                Code = $"TRF-{now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                SourceWarehouseId = request.SourceWarehouseId,
                DestinationWarehouseId = request.DestinationWarehouseId,
                Note = request.Note,
                InternalTransferOrderNo = string.IsNullOrWhiteSpace(request.InternalTransferOrderNo) ? null : request.InternalTransferOrderNo.Trim(),
                TransporterName = string.IsNullOrWhiteSpace(request.TransporterName) ? null : request.TransporterName.Trim(),
                VehiclePlateNumber = string.IsNullOrWhiteSpace(request.VehiclePlateNumber) ? null : request.VehiclePlateNumber.Trim(),
                CreatedBy = currentUser.UserId,
                CreatedAt = now,
                Details = request.Details.Select(x => new StockTransferDetail
                {
                    ProductId = x.ProductId, RequestedQuantity = x.Quantity, Note = x.Note
                }).ToList()
            };
            context.StockTransfers.Add(entity);
            await context.SaveChangesAsync(cancellationToken);
            await AuditAsync(entity, "StockTransfer.Created", entity.Status, entity.Status, cancellationToken);
            id = entity.Id;
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            if (ownsTransaction) context.ChangeTracker.Clear();
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            if (ownsTransaction) context.ChangeTracker.Clear();
            throw;
        }

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task UpdateAsync(int id, UpdateStockTransferDto request, CancellationToken cancellationToken = default)
    {
        EnsureWriteRole();
        if (id <= 0) throw new BusinessRuleException("ID phiếu điều chuyển không hợp lệ.");

        var ownsTransaction = context.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
            // Lock the header before checking Draft. A concurrent approval must
            // never be able to commit while we replace the posted document lines.
            var activeTenantId = context.CurrentTenantId;
            var lockedQuery = context.Database.IsSqlServer()
                ? context.StockTransfers.FromSqlInterpolated(
                    $"SELECT * FROM dbo.StockTransfers WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id} AND TenantId = {activeTenantId}")
                : context.StockTransfers.AsQueryable();
            var entity = await lockedQuery
                .Include(x => x.Details)
                .SingleOrDefaultAsync(x => x.Id == id &&
                    (allowed.Contains(x.SourceWarehouseId) || allowed.Contains(x.DestinationWarehouseId)),
                    cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy phiếu điều chuyển.");

            if (entity.ReverseOfTransferId.HasValue)
                throw Conflict("Phiếu điều chuyển ngược không được sửa.");
            if (entity.Status != StockTransferStatus.Draft)
                throw Conflict("Chỉ phiếu nháp được chỉnh sửa.");
            if (request.ExpectedDraftRevision <= 0 || request.ExpectedDraftRevision != entity.DraftRevision)
                throw Conflict("Phiên bản phiếu nháp đã thay đổi. Vui lòng tải lại trước khi lưu.");
            if (entity.DraftRevision == int.MaxValue)
                throw Conflict("Phiên bản phiếu đã đạt giới hạn; cần hỗ trợ quản trị.");

            await ValidateDraftAsync(request, cancellationToken);
            var previousRevision = entity.DraftRevision;
            entity.SourceWarehouseId = request.SourceWarehouseId;
            entity.DestinationWarehouseId = request.DestinationWarehouseId;
            entity.Note = request.Note;
            entity.InternalTransferOrderNo = string.IsNullOrWhiteSpace(request.InternalTransferOrderNo) ? entity.InternalTransferOrderNo : request.InternalTransferOrderNo.Trim();
            entity.TransporterName = string.IsNullOrWhiteSpace(request.TransporterName) ? entity.TransporterName : request.TransporterName.Trim();
            entity.VehiclePlateNumber = string.IsNullOrWhiteSpace(request.VehiclePlateNumber) ? entity.VehiclePlateNumber : request.VehiclePlateNumber.Trim();
            entity.DraftRevision = previousRevision + 1;
            entity.Details.Clear();
            foreach (var line in request.Details)
                entity.Details.Add(new StockTransferDetail
                {
                    ProductId = line.ProductId,
                    RequestedQuantity = line.Quantity,
                    Note = line.Note
                });

            await context.SaveChangesAsync(cancellationToken);
            var editAudit = Audit(entity, "StockTransfer.Updated", StockTransferStatus.Draft,
                StockTransferStatus.Draft, DateTime.UtcNow);
            editAudit.OldValues = $"Status: Draft; DraftRevision: {previousRevision}";
            editAudit.NewValues = $"Status: Draft; DraftRevision: {entity.DraftRevision}";
            context.AuditLogs.Add(editAudit);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            if (ownsTransaction) context.ChangeTracker.Clear();
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task ApproveAsync(int id, CancellationToken cancellationToken = default)
    {
        EnsureApproveRole();
        var ownsTransaction = context.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        try
        {
            var entity = await GetLockedScopedAsync(id, cancellationToken);
            await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.SourceWarehouseId, cancellationToken);
            await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.DestinationWarehouseId, cancellationToken);
            ERP.Application.Security.ApprovalSafetyGuard.EnsureDifferentChecker(entity.CreatedBy, currentUser.UserId);
            await TransitionAsync(entity, StockTransferStatus.Draft, StockTransferStatus.Approved,
                "StockTransfer.Approved", cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            if (ownsTransaction) context.ChangeTracker.Clear();
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            if (ownsTransaction) context.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task DispatchAsync(int id, CancellationToken cancellationToken = default)
    {
        EnsureWriteRole();
        var ownsTransaction = context.Database.CurrentTransaction is null;
        var maxAttempts = ownsTransaction && context.Database.IsSqlServer() ? 4 : 1;
        for (var attempt = 1; ; attempt++)
        {
            await using var transaction = ownsTransaction ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken) : null;
            try
            {
                var entity = await GetLockedScopedAsync(id, cancellationToken);
                await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.SourceWarehouseId, cancellationToken);
                if (entity.Status != StockTransferStatus.Approved)
                    throw Conflict("Phiếu không còn ở trạng thái có thể xuất kho.");
                if (entity.Details.Any(x => x.Product.TrackingType != ProductTrackingType.None))
                    throw new BusinessRuleException("Phiếu điều chuyển kho chưa hỗ trợ sản phẩm quản lý theo Lô/Serial.");
                var now = DateTime.UtcNow;
                var pxkkvcnbNo = string.IsNullOrWhiteSpace(entity.ElectronicPxkkvcnbNo)
                    ? $"PXKKVCNB-{now:yyyyMMdd}-{entity.Id:D5}"
                    : entity.ElectronicPxkkvcnbNo;
                if (context.Database.IsSqlServer())
                {
                    var claimed = await context.StockTransfers.Where(x => x.Id == id && x.Status == StockTransferStatus.Approved &&
                        x.SourceWarehouseId == entity.SourceWarehouseId && x.DestinationWarehouseId == entity.DestinationWarehouseId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.Status, StockTransferStatus.InTransit)
                            .SetProperty(x => x.DispatchedBy, currentUser.UserId)
                            .SetProperty(x => x.DispatchedAt, now)
                            .SetProperty(x => x.ElectronicPxkkvcnbNo, pxkkvcnbNo), cancellationToken);
                    if (claimed != 1) throw Conflict("Phiếu không ở trạng thái có thể xuất kho hoặc đã được xử lý.");
                }
                else
                {
                    entity.Status = StockTransferStatus.InTransit;
                    entity.DispatchedBy = currentUser.UserId;
                    entity.DispatchedAt = now;
                    entity.ElectronicPxkkvcnbNo = pxkkvcnbNo;
                }
                var sourceLegacyLocationId = await context.WarehouseLocations
                    .Where(x => x.WarehouseId == entity.SourceWarehouseId && x.Code == "LEGACY" && x.IsSystemManaged)
                    .Select(x => (int?)x.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                await stockRepository.AcquireAllocationLocksAsync(
                    entity.SourceWarehouseId,
                    entity.Details.Select(x => x.ProductId),
                    cancellationToken);
                foreach (var line in entity.Details.OrderBy(x => x.ProductId))
                {
                    if (!await stockRepository.TryDecreaseStockAsync(line.ProductId, entity.SourceWarehouseId, line.RequestedQuantity, cancellationToken))
                        throw Conflict($"Không đủ tồn kho cho sản phẩm {line.ProductId}.");
                    line.DispatchedQuantity = line.RequestedQuantity;
                    context.InventoryTransactions.Add(Transaction(entity, line, entity.SourceWarehouseId, TransactionType.TransferOut, line.RequestedQuantity, now, sourceLegacyLocationId, InventoryStatus.Available));
                }
                await context.SaveChangesAsync(cancellationToken);
                context.AuditLogs.Add(Audit(entity, "StockTransfer.Dispatched", StockTransferStatus.Approved, StockTransferStatus.InTransit, now));
                await context.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                if (ownsTransaction) context.ChangeTracker.Clear();
                return;
            }
            catch (Exception ex) when (ownsTransaction && attempt < maxAttempts &&
                                       (ex is DeadlockException ||
                                        ex is Microsoft.Data.SqlClient.SqlException { Number: 1205 } ||
                                        ex is DbUpdateException { InnerException: Microsoft.Data.SqlClient.SqlException { Number: 1205 } }))
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                context.ChangeTracker.Clear();
                await Task.Delay(15 * attempt, cancellationToken);
            }
            catch { if (transaction is not null) await transaction.RollbackAsync(cancellationToken); throw; }
        }
    }

    public async Task ReceiveAsync(int id, ReceiveStockTransferDto request, CancellationToken cancellationToken = default)
    {
        EnsureWriteRole();
        var ownsTransaction = context.Database.CurrentTransaction is null;
        var maxAttempts = ownsTransaction && context.Database.IsSqlServer() ? 4 : 1;
        for (var attempt = 1; ; attempt++)
        {
            await using var transaction = ownsTransaction ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken) : null;
            try
            {
                var entity = await GetLockedScopedAsync(id, cancellationToken);
                await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.DestinationWarehouseId, cancellationToken);
                if (entity.Status != StockTransferStatus.InTransit) throw Conflict("Phiếu không ở trạng thái có thể nhận hoặc đã được xử lý.");
                ValidateReceipt(entity, request);
                var now = DateTime.UtcNow;
                if (context.Database.IsSqlServer())
                {
                    var claimed = await context.StockTransfers.Where(x => x.Id == id && x.Status == StockTransferStatus.InTransit &&
                        x.SourceWarehouseId == entity.SourceWarehouseId && x.DestinationWarehouseId == entity.DestinationWarehouseId)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, StockTransferStatus.Received).SetProperty(x => x.ReceivedBy, currentUser.UserId).SetProperty(x => x.ReceivedAt, now), cancellationToken);
                    if (claimed != 1) throw Conflict("Phiếu không ở trạng thái có thể nhận hoặc đã được xử lý.");
                }
                else
                {
                    entity.Status = StockTransferStatus.Received;
                    entity.ReceivedBy = currentUser.UserId;
                    entity.ReceivedAt = now;
                }
                await context.AcquireInventoryAllocationLocksAsync(
                    entity.DestinationWarehouseId,
                    request.Details.Select(x => x.ProductId),
                    cancellationToken);
                foreach (var input in request.Details.OrderBy(x => x.ProductId))
                {
                    var line = entity.Details.Single(x => x.ProductId == input.ProductId);
                    line.ReceivedQuantity = input.ReceivedQuantity;
                    line.MissingQuantity = input.MissingQuantity;
                    line.DamagedQuantity = input.DamagedQuantity;
                    line.Note = input.Note;
                    if (input.ReceivedQuantity > 0)
                    {
                        var receivedLocationId = await IncreaseStockAsync(line.ProductId, entity.DestinationWarehouseId, input.ReceivedQuantity, now, cancellationToken, InventoryStatus.Available);
                        context.InventoryTransactions.Add(Transaction(entity, line, entity.DestinationWarehouseId, TransactionType.TransferIn, input.ReceivedQuantity, now, receivedLocationId, InventoryStatus.Available));
                    }
                    if (input.DamagedQuantity > 0)
                    {
                        var damagedLocationId = await IncreaseStockAsync(line.ProductId, entity.DestinationWarehouseId, input.DamagedQuantity, now, cancellationToken, InventoryStatus.Damaged);
                        var damagedTx = Transaction(entity, line, entity.DestinationWarehouseId, TransactionType.TransferIn, input.DamagedQuantity, now, damagedLocationId, InventoryStatus.Damaged);
                        damagedTx.ReferenceType = "StockTransferDamage";
                        damagedTx.Note = string.IsNullOrWhiteSpace(line.Note)
                            ? "Nhận điều chuyển hàng hỏng"
                            : $"[DAMAGED] {line.Note}";
                        context.InventoryTransactions.Add(damagedTx);
                    }
                }
                await context.SaveChangesAsync(cancellationToken);
                context.AuditLogs.Add(Audit(entity, "StockTransfer.Received", StockTransferStatus.InTransit, StockTransferStatus.Received, now));
                await context.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                if (ownsTransaction) context.ChangeTracker.Clear();
                return;
            }
            catch (Exception ex) when (ownsTransaction && attempt < maxAttempts &&
                                       (ex is DeadlockException ||
                                        ex is Microsoft.Data.SqlClient.SqlException { Number: 1205 } ||
                                        ex is DbUpdateException { InnerException: Microsoft.Data.SqlClient.SqlException { Number: 1205 } }))
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                context.ChangeTracker.Clear();
                await Task.Delay(15 * attempt, cancellationToken);
            }
            catch { if (transaction is not null) await transaction.RollbackAsync(cancellationToken); throw; }
        }
    }

    public async Task CompleteAsync(int id, CancellationToken cancellationToken = default)
    {
        EnsureWriteRole();
        var ownsTransaction = context.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        try
        {
            var entity = await GetLockedScopedAsync(id, cancellationToken);
            await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.DestinationWarehouseId, cancellationToken);
            await TransitionAsync(entity, StockTransferStatus.Received, StockTransferStatus.Completed,
                "StockTransfer.Completed", cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            if (ownsTransaction) context.ChangeTracker.Clear();
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            if (ownsTransaction) context.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task CancelAsync(int id, CancellationToken cancellationToken = default)
    {
        EnsureWriteRole();
        var ownsTransaction = context.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        try
        {
            var entity = await GetLockedScopedAsync(id, cancellationToken);
            await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.SourceWarehouseId, cancellationToken);
            if (entity.Status is not (StockTransferStatus.Draft or StockTransferStatus.Approved))
                throw Conflict("Chỉ phiếu chưa xuất kho mới được hủy.");
            await TransitionAsync(entity, entity.Status, StockTransferStatus.Cancelled,
                "StockTransfer.Cancelled", cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            if (ownsTransaction) context.ChangeTracker.Clear();
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            if (ownsTransaction) context.ChangeTracker.Clear();
            throw;
        }
    }
}
