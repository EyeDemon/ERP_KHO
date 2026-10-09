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
        if (request.ToDate.HasValue) query = query.Where(x => x.CreatedAt < request.ToDate.Value.AddDays(1));
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.Include(x => x.SourceWarehouse).Include(x => x.DestinationWarehouse)
            .OrderByDescending(x => x.CreatedAt).Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
        return new PagedResult<StockTransferDto> { Items = rows.Select(MapSummary).ToList(), TotalRecords = total, PageIndex = page, PageSize = size };
    }

    public Task<StockTransferDto> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        GetDetailAsync(id, cancellationToken);

    public async Task<StockTransferDto> CreateAsync(CreateStockTransferDto request, CancellationToken cancellationToken = default)
    {
        EnsureWriteRole();
        await ValidateDraftAsync(request, cancellationToken);
        var now = DateTime.UtcNow;
        var entity = new StockTransfer
        {
            Code = $"TRF-{now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            SourceWarehouseId = request.SourceWarehouseId,
            DestinationWarehouseId = request.DestinationWarehouseId,
            Note = request.Note,
            CreatedBy = currentUser.UserId,
            CreatedAt = now,
            Details = request.Details.Select(x => new StockTransferDetail { ProductId = x.ProductId, RequestedQuantity = x.Quantity, Note = x.Note }).ToList()
        };
        context.StockTransfers.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        await AuditAsync(entity, "StockTransfer.Created", entity.Status, entity.Status, cancellationToken);
        return await GetByIdAsync(entity.Id, cancellationToken);
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
            var lockedQuery = context.Database.IsSqlServer()
                ? context.StockTransfers.FromSqlInterpolated(
                    $"SELECT * FROM dbo.StockTransfers WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}")
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

            await ValidateDraftAsync(request, cancellationToken);
            entity.SourceWarehouseId = request.SourceWarehouseId;
            entity.DestinationWarehouseId = request.DestinationWarehouseId;
            entity.Note = request.Note;
            entity.Details.Clear();
            foreach (var line in request.Details)
                entity.Details.Add(new StockTransferDetail
                {
                    ProductId = line.ProductId,
                    RequestedQuantity = line.Quantity,
                    Note = line.Note
                });

            await context.SaveChangesAsync(cancellationToken);
            await AuditAsync(entity, "StockTransfer.Updated", StockTransferStatus.Draft,
                StockTransferStatus.Draft, cancellationToken);
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
        var entity = await GetScopedAsync(id, cancellationToken);
        await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.SourceWarehouseId, cancellationToken);
        await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.DestinationWarehouseId, cancellationToken);
        ERP.Application.Security.ApprovalSafetyGuard.EnsureDifferentChecker(entity.CreatedBy, currentUser.UserId);
        await TransitionAsync(entity, StockTransferStatus.Draft, StockTransferStatus.Approved,
            "StockTransfer.Approved", cancellationToken);
    }

    public async Task DispatchAsync(int id, CancellationToken cancellationToken = default)
    {
        EnsureWriteRole();
        var ownsTransaction = context.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        try
        {
            var entity = await GetLockedScopedAsync(id, cancellationToken);
            await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.SourceWarehouseId, cancellationToken);
            if (entity.Status != StockTransferStatus.Approved)
                throw Conflict("Phiếu không còn ở trạng thái có thể xuất kho.");
            var now = DateTime.UtcNow;
            var claimed = await context.StockTransfers.Where(x => x.Id == id && x.Status == StockTransferStatus.Approved &&
                x.SourceWarehouseId == entity.SourceWarehouseId && x.DestinationWarehouseId == entity.DestinationWarehouseId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, StockTransferStatus.InTransit).SetProperty(x => x.DispatchedBy, currentUser.UserId).SetProperty(x => x.DispatchedAt, now), cancellationToken);
            if (claimed != 1) throw Conflict("Phiếu không ở trạng thái có thể xuất kho hoặc đã được xử lý.");
            foreach (var line in entity.Details.OrderBy(x => x.ProductId))
            {
                if (!await stockRepository.TryDecreaseStockAsync(line.ProductId, entity.SourceWarehouseId, line.RequestedQuantity, cancellationToken))
                    throw Conflict($"Không đủ tồn kho cho sản phẩm {line.ProductId}.");
                line.DispatchedQuantity = line.RequestedQuantity;
                context.InventoryTransactions.Add(Transaction(entity, line, entity.SourceWarehouseId, TransactionType.TransferOut, line.RequestedQuantity, now));
            }
            await context.SaveChangesAsync(cancellationToken);
            context.AuditLogs.Add(Audit(entity, "StockTransfer.Dispatched", StockTransferStatus.Approved, StockTransferStatus.InTransit, now));
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            if (ownsTransaction) context.ChangeTracker.Clear();
        }
        catch { if (transaction is not null) await transaction.RollbackAsync(cancellationToken); throw; }
    }

    public async Task ReceiveAsync(int id, ReceiveStockTransferDto request, CancellationToken cancellationToken = default)
    {
        EnsureWriteRole();
        var ownsTransaction = context.Database.CurrentTransaction is null;
        // The document uses UPDLOCK/HOLDLOCK and the destination bucket gets
        // an exclusive transaction-owned sp_getapplock before the stock MERGE.
        // SERIALIZABLE across *all* reads here held compatible S/range locks
        // on unrelated tables until commit, causing a lock conversion cycle
        // while a second receipt waited for the app lock (SQL -3/1205).
        // READ COMMITTED releases read locks after each statement. Document
        // and canonical bucket write locks still persist to commit/rollback.
        await using var transaction = ownsTransaction ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken) : null;
        try
        {
            var entity = await GetLockedScopedAsync(id, cancellationToken);
            await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.DestinationWarehouseId, cancellationToken);
            if (entity.Status != StockTransferStatus.InTransit) throw Conflict("Phiếu không ở trạng thái có thể nhận hoặc đã được xử lý.");
            ValidateReceipt(entity, request);
            var now = DateTime.UtcNow;
            var claimed = await context.StockTransfers.Where(x => x.Id == id && x.Status == StockTransferStatus.InTransit &&
                x.SourceWarehouseId == entity.SourceWarehouseId && x.DestinationWarehouseId == entity.DestinationWarehouseId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, StockTransferStatus.Received).SetProperty(x => x.ReceivedBy, currentUser.UserId).SetProperty(x => x.ReceivedAt, now), cancellationToken);
            if (claimed != 1) throw Conflict("Phiếu không ở trạng thái có thể nhận hoặc đã được xử lý.");
            foreach (var input in request.Details.OrderBy(x => x.ProductId))
            {
                var line = entity.Details.Single(x => x.ProductId == input.ProductId);
                line.ReceivedQuantity = input.ReceivedQuantity;
                line.MissingQuantity = input.MissingQuantity;
                line.DamagedQuantity = input.DamagedQuantity;
                line.Note = input.Note;
                if (input.ReceivedQuantity > 0)
                {
                    await IncreaseStockAsync(line.ProductId, entity.DestinationWarehouseId, input.ReceivedQuantity, now, cancellationToken);
                    context.InventoryTransactions.Add(Transaction(entity, line, entity.DestinationWarehouseId, TransactionType.TransferIn, input.ReceivedQuantity, now));
                }
            }
            await context.SaveChangesAsync(cancellationToken);
            context.AuditLogs.Add(Audit(entity, "StockTransfer.Received", StockTransferStatus.InTransit, StockTransferStatus.Received, now));
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            if (ownsTransaction) context.ChangeTracker.Clear();
        }
        catch { if (transaction is not null) await transaction.RollbackAsync(cancellationToken); throw; }
    }

    public async Task CompleteAsync(int id, CancellationToken cancellationToken = default)
    {
        EnsureWriteRole();
        var entity = await GetScopedAsync(id, cancellationToken);
        await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.DestinationWarehouseId, cancellationToken);
        await TransitionAsync(entity, StockTransferStatus.Received, StockTransferStatus.Completed,
            "StockTransfer.Completed", cancellationToken);
    }

    public async Task CancelAsync(int id, CancellationToken cancellationToken = default)
    {
        EnsureWriteRole();
        var entity = await GetScopedAsync(id, cancellationToken);
        await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.SourceWarehouseId, cancellationToken);
        if (entity.Status is not (StockTransferStatus.Draft or StockTransferStatus.Approved)) throw Conflict("Chỉ phiếu chưa xuất kho mới được hủy.");
        await TransitionAsync(entity, entity.Status, StockTransferStatus.Cancelled,
            "StockTransfer.Cancelled", cancellationToken);
    }

    private async Task ValidateDraftAsync(CreateStockTransferDto request, CancellationToken cancellationToken)
    {
        if (request.SourceWarehouseId <= 0 || request.DestinationWarehouseId <= 0 || request.SourceWarehouseId == request.DestinationWarehouseId)
            throw new BusinessRuleException("Kho nguồn và kho đích phải hợp lệ và khác nhau.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(request.SourceWarehouseId, cancellationToken);
        if (!await context.Warehouses.AnyAsync(x => x.Id == request.DestinationWarehouseId, cancellationToken)) throw new BusinessRuleException("Kho đích không tồn tại.");
        if (request.Details == null || request.Details.Count == 0 ||
            request.Details.Any(x => x == null || x.ProductId <= 0 ||
                x.Quantity <= 0 || x.Quantity >= 100_000_000_000_000m ||
                decimal.Round(x.Quantity, 4) != x.Quantity))
            throw new BusinessRuleException("Phiếu phải có sản phẩm với số lượng dương và tối đa 4 chữ số thập phân.");
        if (request.Note?.Length > 500 || request.Details.Any(x => x.Note?.Length > 500))
            throw new BusinessRuleException("Ghi chú phiếu hoặc từng sản phẩm không được quá 500 ký tự.");
        if (request.Details.GroupBy(x => x.ProductId).Any(x => x.Count() > 1)) throw new BusinessRuleException("Một sản phẩm không được lặp nhiều dòng.");
        var ids = request.Details.Select(x => x.ProductId).Distinct().ToList();
        if (await context.Products.CountAsync(x => ids.Contains(x.Id), cancellationToken) != ids.Count) throw new BusinessRuleException("Có sản phẩm không tồn tại.");
    }

    private static void ValidateReceipt(StockTransfer entity, ReceiveStockTransferDto request)
    {
        if (request.Details is null || request.Details.Any(x => x is null) ||
            request.Details.Count != entity.Details.Count ||
            request.Details.Select(x => x.ProductId).Distinct().Count() != entity.Details.Count)
            throw new BusinessRuleException("Phải khai báo kết quả nhận cho từng sản phẩm đúng một lần.");
        foreach (var input in request.Details)
        {
            var line = entity.Details.SingleOrDefault(x => x.ProductId == input.ProductId) ?? throw new BusinessRuleException("Sản phẩm không thuộc phiếu.");
            if (input.Note?.Length > 500)
                throw new BusinessRuleException("Ghi chú nhận hàng không được quá 500 ký tự.");
            if (input.ReceivedQuantity < 0 || input.MissingQuantity < 0 || input.DamagedQuantity < 0 ||
                decimal.Round(input.ReceivedQuantity, 4) != input.ReceivedQuantity ||
                decimal.Round(input.MissingQuantity, 4) != input.MissingQuantity ||
                decimal.Round(input.DamagedQuantity, 4) != input.DamagedQuantity)
                throw new BusinessRuleException("Số lượng nhận, thiếu và hư hỏng phải không âm và có tối đa 4 chữ số thập phân.");

            // Bound each part before summing to avoid overflow from malformed
            // decimal payloads; the posted quantity uses decimal(18,4).
            if (input.ReceivedQuantity > line.DispatchedQuantity ||
                input.MissingQuantity > line.DispatchedQuantity ||
                input.DamagedQuantity > line.DispatchedQuantity)
                throw new BusinessRuleException("Số lượng thực nhận, thiếu hoặc hỏng không được vượt quá số đã xuất.");

            // Receiving is a terminal movement for each line, not a partial
            // receiving workflow. Every dispatched unit must be accounted for
            // as received, missing or damaged before the document leaves transit.
            if (input.ReceivedQuantity + input.MissingQuantity + input.DamagedQuantity != line.DispatchedQuantity)
                throw new BusinessRuleException("Tổng thực nhận, thiếu và hỏng phải đúng bằng số lượng đã xuất của từng sản phẩm.");
        }
    }

    private async Task IncreaseStockAsync(int productId, int warehouseId, decimal quantity, DateTime now, CancellationToken cancellationToken)
    {
        var locationId = await context.WarehouseLocations.Where(x => x.WarehouseId == warehouseId && x.Code == "LEGACY" && x.IsSystemManaged && x.IsActive && !x.IsBlocked && x.IsPickable).Select(x => x.Id).SingleOrDefaultAsync(cancellationToken);
        if (locationId == 0) throw new BusinessRuleException("Kho đích chưa có vị trí tương thích để nhận điều chuyển.");
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Nhận hoặc hoàn trả điều chuyển phải nằm trong giao dịch SQL.");

        // Serialize just this canonical stock bucket across different transfer
        // documents. Without an application lock, two SERIALIZABLE transactions
        // can both take compatible range-read locks on a missing destination row
        // and then deadlock when MERGE converts them to write locks (SQL 1205).
        // The lock is held until the caller commits/rolls back ledger + audit.
        var bucketLock = $"ERP:TransferStock:{productId}:{warehouseId}:{locationId}:0";
        await context.Database.ExecuteSqlInterpolatedAsync($@"
DECLARE @lockResult int;
EXEC @lockResult = sys.sp_getapplock
    @Resource = {bucketLock},
    @LockMode = 'Exclusive',
    @LockOwner = 'Transaction',
    @LockTimeout = 15000;
IF @lockResult < 0
    THROW 51032, 'Khong the khoa o ton kho de nhan dieu chuyen.', 1;", cancellationToken);

        await context.Database.ExecuteSqlInterpolatedAsync($@"
MERGE INTO InventoryStocks WITH (HOLDLOCK) AS target
USING (SELECT {productId} AS ProductId, {warehouseId} AS WarehouseId, {locationId} AS LocationId, {quantity} AS Quantity, {now} AS LastUpdated, 0 AS Status) AS source
ON target.ProductId = source.ProductId AND target.WarehouseId = source.WarehouseId AND target.Status = source.Status AND target.LocationId = source.LocationId
   AND target.LotId IS NULL AND target.SerialId IS NULL
WHEN MATCHED THEN
    UPDATE SET Quantity = target.Quantity + source.Quantity, LastUpdated = source.LastUpdated
WHEN NOT MATCHED THEN
    INSERT (ProductId, WarehouseId, LocationId, Status, Quantity, ReservedQuantity, LastUpdated)
    VALUES (source.ProductId, source.WarehouseId, source.LocationId, source.Status, source.Quantity, 0, source.LastUpdated);", cancellationToken);
    }

    private async Task TransitionAsync(StockTransfer entity, StockTransferStatus from, StockTransferStatus to, string action, CancellationToken cancellationToken)
    {
        var id = entity.Id;
        var now = DateTime.UtcNow;
        // Recheck the exact warehouse pair observed during authorization.
        // A parallel draft edit may change either warehouse before this CAS
        // executes; stale scope must never approve/complete/cancel that version.
        var target = context.StockTransfers.Where(x =>
            x.Id == id && x.Status == from &&
            x.SourceWarehouseId == entity.SourceWarehouseId &&
            x.DestinationWarehouseId == entity.DestinationWarehouseId);
        var affected = action switch
        {
            "StockTransfer.Approved" => await target.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, to).SetProperty(x => x.ApprovedBy, currentUser.UserId).SetProperty(x => x.ApprovedAt, now), cancellationToken),
            "StockTransfer.Completed" => await target.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, to).SetProperty(x => x.CompletedBy, currentUser.UserId).SetProperty(x => x.CompletedAt, now), cancellationToken),
            "StockTransfer.Cancelled" => await target.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, to).SetProperty(x => x.CancelledBy, currentUser.UserId).SetProperty(x => x.CancelledAt, now), cancellationToken),
            _ => throw new InvalidOperationException("Unsupported stock-transfer transition.")
        };
        if (affected != 1) throw Conflict("Trạng thái phiếu đã thay đổi hoặc thao tác không hợp lệ.");
        await AuditAsync(entity, action, from, to, cancellationToken, now);
        if (context.Database.CurrentTransaction is null) context.ChangeTracker.Clear();
    }

    // Lock the same header row as Draft edits before reading posting lines.
    // Caller owns a transaction to keep ledger and document version consistent.
    private async Task<StockTransfer> GetLockedScopedAsync(int id, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Stock transfer posting requires a database transaction.");
        var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        var locked = context.Database.IsSqlServer()
            ? context.StockTransfers.FromSqlInterpolated(
                $"SELECT * FROM dbo.StockTransfers WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}")
            : context.StockTransfers.AsQueryable();
        return await locked.Include(x => x.SourceWarehouse).Include(x => x.DestinationWarehouse)
            .Include(x => x.Details).ThenInclude(x => x.Product)
            .SingleOrDefaultAsync(x => x.Id == id &&
                (allowed.Contains(x.SourceWarehouseId) || allowed.Contains(x.DestinationWarehouseId)),
                cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy phiếu điều chuyển.");
    }

    private async Task<StockTransfer> GetScopedAsync(int id, CancellationToken cancellationToken)
    {
        var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        return await context.StockTransfers.Include(x => x.SourceWarehouse).Include(x => x.DestinationWarehouse)
            .Include(x => x.Details).ThenInclude(x => x.Product)
            .SingleOrDefaultAsync(x => x.Id == id && (allowed.Contains(x.SourceWarehouseId) || allowed.Contains(x.DestinationWarehouseId)), cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy phiếu điều chuyển.");
    }

    private void EnsureWriteRole()
    {
        if (!currentUser.IsGlobalAdmin && currentUser.Role is not ("Manager" or "WarehouseStaff")) throw new ForbiddenException("Vai trò hiện tại chỉ được xem.");
    }
    private void EnsureApproveRole()
    {
        if (!currentUser.IsGlobalAdmin && currentUser.Role != "Manager") throw new ForbiddenException("Bạn không có quyền duyệt phiếu.");
    }

    private InventoryTransaction Transaction(StockTransfer transfer, StockTransferDetail line, int warehouseId, TransactionType type, decimal quantity, DateTime now) => new()
        { ProductId = line.ProductId, WarehouseId = warehouseId, TransactionType = type, Quantity = quantity, ReferenceId = transfer.Id, ReferenceType = "StockTransfer", TransactionDate = now, CreatedBy = currentUser.UserId, Note = line.Note };
    private AuditLog Audit(StockTransfer entity, string action, StockTransferStatus from, StockTransferStatus to, DateTime now) => new()
    {
        UserId = currentUser.UserId, Action = action, EntityName = "StockTransfer", EntityId = entity.Id,
        SourceWarehouseId = entity.SourceWarehouseId, DestinationWarehouseId = entity.DestinationWarehouseId,
        OldValues = $"Status: {from}", NewValues = $"Status: {to}", Result = "Success",
        Severity = "Information", Timestamp = now
    };
    private async Task AuditAsync(StockTransfer entity, string action, StockTransferStatus from, StockTransferStatus to, CancellationToken cancellationToken, DateTime? now = null)
    { context.AuditLogs.Add(Audit(entity, action, from, to, now ?? DateTime.UtcNow)); await context.SaveChangesAsync(cancellationToken); }
    private static ConcurrencyException Conflict(string message) => new(message);

    private static StockTransferDto MapSummary(StockTransfer x) => new() { Id = x.Id, Code = x.Code, SourceWarehouseId = x.SourceWarehouseId, SourceWarehouseName = x.SourceWarehouse.Name, DestinationWarehouseId = x.DestinationWarehouseId, DestinationWarehouseName = x.DestinationWarehouse.Name, Status = x.Status, Note = x.Note, CreatedBy = x.CreatedBy, CreatedAt = x.CreatedAt, ApprovedAt = x.ApprovedAt, DispatchedAt = x.DispatchedAt, ReceivedAt = x.ReceivedAt, CompletedAt = x.CompletedAt, CancelledAt = x.CancelledAt };
    private static StockTransferDto Map(StockTransfer x) { var dto = MapSummary(x); dto.Details = x.Details.OrderBy(d => d.ProductId).Select(d => new StockTransferDetailDto { ProductId = d.ProductId, ProductCode = d.Product.Code, ProductName = d.Product.Name, RequestedQuantity = d.RequestedQuantity, DispatchedQuantity = d.DispatchedQuantity, ReceivedQuantity = d.ReceivedQuantity, MissingQuantity = d.MissingQuantity, DamagedQuantity = d.DamagedQuantity, InTransitQuantity = x.Status == StockTransferStatus.InTransit ? d.DispatchedQuantity - d.ReceivedQuantity - d.MissingQuantity - d.DamagedQuantity : 0, Note = d.Note }).ToList(); return dto; }
}
