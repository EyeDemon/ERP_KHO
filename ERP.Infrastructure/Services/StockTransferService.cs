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

public sealed class StockTransferService(
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

    public async Task<StockTransferDto> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Map(await GetScopedAsync(id, cancellationToken));

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
        var entity = await GetScopedAsync(id, cancellationToken);
        if (entity.Status != StockTransferStatus.Draft) throw Conflict("Chỉ phiếu nháp được chỉnh sửa.");
        await ValidateDraftAsync(request, cancellationToken);
        entity.SourceWarehouseId = request.SourceWarehouseId;
        entity.DestinationWarehouseId = request.DestinationWarehouseId;
        entity.Note = request.Note;
        entity.Details.Clear();
        foreach (var line in request.Details) entity.Details.Add(new StockTransferDetail { ProductId = line.ProductId, RequestedQuantity = line.Quantity, Note = line.Note });
        await context.SaveChangesAsync(cancellationToken);
        await AuditAsync(entity, "StockTransfer.Updated", entity.Status, entity.Status, cancellationToken);
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
        await using var transaction = ownsTransaction ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            var entity = await GetScopedAsync(id, cancellationToken);
            await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.SourceWarehouseId, cancellationToken);
            var now = DateTime.UtcNow;
            var claimed = await context.StockTransfers.Where(x => x.Id == id && x.Status == StockTransferStatus.Approved)
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
        await using var transaction = ownsTransaction ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            var entity = await GetScopedAsync(id, cancellationToken);
            await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.DestinationWarehouseId, cancellationToken);
            if (entity.Status != StockTransferStatus.InTransit) throw Conflict("Phiếu không ở trạng thái có thể nhận hoặc đã được xử lý.");
            ValidateReceipt(entity, request);
            var now = DateTime.UtcNow;
            var claimed = await context.StockTransfers.Where(x => x.Id == id && x.Status == StockTransferStatus.InTransit)
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
        if (request.Details.Count == 0 || request.Details.Any(x => x.ProductId <= 0 || x.Quantity <= 0)) throw new BusinessRuleException("Phiếu phải có sản phẩm với số lượng lớn hơn 0.");
        if (request.Details.GroupBy(x => x.ProductId).Any(x => x.Count() > 1)) throw new BusinessRuleException("Một sản phẩm không được lặp nhiều dòng.");
        var ids = request.Details.Select(x => x.ProductId).Distinct().ToList();
        if (await context.Products.CountAsync(x => ids.Contains(x.Id), cancellationToken) != ids.Count) throw new BusinessRuleException("Có sản phẩm không tồn tại.");
    }

    private static void ValidateReceipt(StockTransfer entity, ReceiveStockTransferDto request)
    {
        if (request.Details.Count != entity.Details.Count || request.Details.Select(x => x.ProductId).Distinct().Count() != entity.Details.Count)
            throw new BusinessRuleException("Phải khai báo kết quả nhận cho từng sản phẩm đúng một lần.");
        foreach (var input in request.Details)
        {
            var line = entity.Details.SingleOrDefault(x => x.ProductId == input.ProductId) ?? throw new BusinessRuleException("Sản phẩm không thuộc phiếu.");
            if (input.ReceivedQuantity < 0 || input.MissingQuantity < 0 || input.DamagedQuantity < 0 || input.ReceivedQuantity + input.MissingQuantity + input.DamagedQuantity > line.DispatchedQuantity)
                throw new BusinessRuleException("Số lượng nhận, thiếu và hư hỏng không hợp lệ.");
        }
    }

    private async Task IncreaseStockAsync(int productId, int warehouseId, decimal quantity, DateTime now, CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlInterpolatedAsync($@"
MERGE INTO InventoryStocks WITH (HOLDLOCK) AS target
USING (SELECT {productId} AS ProductId, {warehouseId} AS WarehouseId, {quantity} AS Quantity, {now} AS LastUpdated) AS source
ON target.ProductId = source.ProductId AND target.WarehouseId = source.WarehouseId
WHEN MATCHED THEN
    UPDATE SET Quantity = target.Quantity + source.Quantity, LastUpdated = source.LastUpdated
WHEN NOT MATCHED THEN
    INSERT (ProductId, WarehouseId, Quantity, LastUpdated)
    VALUES (source.ProductId, source.WarehouseId, source.Quantity, source.LastUpdated);", cancellationToken);
    }

    private async Task TransitionAsync(StockTransfer entity, StockTransferStatus from, StockTransferStatus to, string action, CancellationToken cancellationToken)
    {
        var id = entity.Id;
        var now = DateTime.UtcNow;
        var target = context.StockTransfers.Where(x => x.Id == id && x.Status == from);
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
    private static StockTransferDto Map(StockTransfer x) { var dto = MapSummary(x); dto.Details = x.Details.OrderBy(d => d.ProductId).Select(d => new StockTransferDetailDto { ProductId = d.ProductId, ProductCode = d.Product.Code, ProductName = d.Product.Name, RequestedQuantity = d.RequestedQuantity, DispatchedQuantity = d.DispatchedQuantity, ReceivedQuantity = d.ReceivedQuantity, MissingQuantity = d.MissingQuantity, DamagedQuantity = d.DamagedQuantity, Note = d.Note }).ToList(); return dto; }
}
