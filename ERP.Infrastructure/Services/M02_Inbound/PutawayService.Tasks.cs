using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class PutawayService
{
    public async Task CreateForPostedReceiptAsync(ImportReceipt receipt, int receivingLocationId, int actorId, CancellationToken token = default)
    {
        if (await context.PutawayTasks.AnyAsync(x => x.ReceiptId == receipt.Id, token)) throw Conflict("Nhiệm vụ cất hàng của phiếu nhập đã tồn tại.");
        var task = new PutawayTask { ReceiptId = receipt.Id, WarehouseId = receipt.WarehouseId, CreatedBy = actorId, CreatedAt = DateTime.UtcNow };
        foreach (var line in receipt.Details)
        {
            Add(line, InventoryStatus.Available, line.AcceptedQuantity, line.BaseAcceptedQuantity);
            Add(line, InventoryStatus.Damaged, line.DamagedQuantity, line.BaseDamagedQuantity);
            Add(line, InventoryStatus.Rejected, line.RejectedQuantity, line.BaseRejectedQuantity);
        }
        if (task.Items.Count == 0) throw new BusinessRuleException("Phiếu nhập không có số lượng hợp lệ để tạo nhiệm vụ cất hàng.");
        var availProductIds = task.Items.Where(i => i.InventoryStatus == InventoryStatus.Available && i.RequiredBaseQuantity > 0).Select(i => i.ProductId).Distinct().ToList();
        if (availProductIds.Count > 0)
        {
            var matchedBo = await context.Backorders.AsNoTracking()
                .Include(b => b.SalesOrderLine)
                .Where(b => b.WarehouseId == receipt.WarehouseId &&
                            b.Status == BackorderStatus.Open &&
                            b.Quantity - b.RecoveredQuantity - b.CancelledQuantity > 0 &&
                            availProductIds.Contains(b.SalesOrderLine.ProductId))
                .OrderByDescending(b => b.SalesOrderLine.SalesOrder.Priority)
                .ThenBy(b => b.CreatedAt)
                .Select(b => new { b.Id, b.SalesOrderLine.SalesOrderId })
                .FirstOrDefaultAsync(token);
            if (matchedBo != null)
            {
                task.CrossDockTargetBackorderId = matchedBo.Id;
                task.CrossDockTargetSalesOrderId = matchedBo.SalesOrderId;
            }
        }
        context.PutawayTasks.Add(task);
        await context.SaveChangesAsync(token);
        context.AuditLogs.Add(Audit(actorId, "PutawayTask.Created", task.Id, receipt.WarehouseId, $"ReceiptId: {receipt.Id}; Items: {task.Items.Count}"));
        void Add(ImportReceiptDetail line, InventoryStatus status, decimal operation, decimal quantity)
        {
            if (quantity <= 0) return;
            task.Items.Add(new PutawayTaskItem { ReceiptLineId = line.Id, ProductId = line.ProductId, InventoryStatus = status, SourceLocationId = receivingLocationId,
                OperationUnitId = line.OperationUnitId, OperationUnitCodeSnapshot = line.OperationUnitCodeSnapshot, BaseUnitId = line.BaseUnitId,
                BaseUnitCodeSnapshot = line.BaseUnitCodeSnapshot, ConversionFactorSnapshot = line.ConversionFactor, ConversionVersionSnapshot = line.ConversionVersion,
                BaseUnitDecimalPlaces = line.BaseUnitDecimalPlaces, RequiredOperationQuantity = operation, RequiredBaseQuantity = quantity });
        }
    }

    public async Task<IReadOnlyList<PutawayTaskListDto>> ListAsync(CancellationToken token = default)
    {
        var ids = await warehouses.GetAccessibleWarehouseIdsAsync(token);
        return await context.PutawayTasks.AsNoTracking().Where(x => ids.Contains(x.WarehouseId)).OrderByDescending(x => x.CreatedAt)
            .Select(x => new PutawayTaskListDto { Id=x.Id, ReceiptId=x.ReceiptId, ReceiptCode=x.Receipt.Code.Trim(), WarehouseId=x.WarehouseId, WarehouseCode=x.Warehouse.Code.Trim(), WarehouseName=x.Warehouse.Name.Trim(),
                Status=x.Status.ToString(), AssignedUserId=x.AssignedUserId, RequiredBaseQuantity=x.Items.Sum(i=>i.RequiredBaseQuantity), MovedBaseQuantity=x.Items.Sum(i=>i.MovedBaseQuantity),
                IsCrossDock=x.IsCrossDock, CrossDockTargetSalesOrderId=x.CrossDockTargetSalesOrderId, CrossDockTargetExportReceiptId=x.CrossDockTargetExportReceiptId, CrossDockTargetBackorderId=x.CrossDockTargetBackorderId, CrossDockAllocatedQty=x.CrossDockAllocatedQty,
                CreatedAt=x.CreatedAt }).ToListAsync(token);
    }

    public async Task<PutawayTaskDto> GetAsync(int id, CancellationToken token = default)
    {
        var task = await LoadTaskWithFallbackAsync(id, asNoTracking: true, token) ?? throw new NotFoundException("Không tìm thấy nhiệm vụ hoặc bạn không có quyền truy cập.");
        await warehouses.EnsureWarehouseAccessAsync(task.WarehouseId, token); return await MapAsync(task, token);
    }

    public async Task<IReadOnlyList<WarehouseLocationDto>> GetDestinationsAsync(int taskId, int itemId, CancellationToken token = default)
    {
        var task = await Load(taskId, token);
        var item = task.Items.SingleOrDefault(x => x.Id == itemId) ?? throw new NotFoundException("Không tìm thấy dòng hàng.");
        var type = RequiredType(item.InventoryStatus);
        var product = item.ReceiptLine.Product;
        var productStorageClass = product.StorageClass;
        var candidates = await context.WarehouseLocations.AsNoTracking()
            .Where(x => x.WarehouseId == task.WarehouseId
                && x.IsActive
                && !x.IsBlocked
                && x.LocationType == type
                && (item.InventoryStatus != InventoryStatus.Available || x.IsPickable)
                && (x.StorageClass == null || (productStorageClass != null && x.StorageClass == productStorageClass)))
            .ToListAsync(token);

        if (candidates.Count == 0)
            return [];

        var now = DateTime.UtcNow;
        var activeLocks = await context.InventoryLocks.AsNoTracking()
            .EffectiveAt(now)
            .Where(l => l.WarehouseId == task.WarehouseId
                && (!l.ProductId.HasValue || l.ProductId == item.ProductId)
                && (!l.InventoryStatus.HasValue || l.InventoryStatus == item.InventoryStatus))
            .Select(l => l.LocationId)
            .ToListAsync(token);

        if (activeLocks.Any(id => !id.HasValue))
            return [];

        var lockedLocationIds = activeLocks.Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
        var candidateIds = candidates.Select(c => c.Id).ToList();
        var stockRows = await context.InventoryStocks.AsNoTracking()
            .Where(x => x.WarehouseId == task.WarehouseId && x.LocationId.HasValue && candidateIds.Contains(x.LocationId.Value) && x.Quantity > 0)
            .Join(context.Products.AsNoTracking(), s => s.ProductId, p => p.Id, (s, p) => new
            {
                LocationId = s.LocationId!.Value,
                s.ProductId,
                s.Status,
                s.Quantity,
                p.StorageClass,
                p.UnitWeightKg,
                p.UnitVolumeM3,
                p.UnitPalletEquivalent
            })
            .ToListAsync(token);

        var incomingBaseQty = item.RemainingBaseQuantity > 0 ? item.RemainingBaseQuantity : 1m;
        var eligible = new List<(WarehouseLocation Location, bool HasSameSku, bool ExactStorageClassMatch, decimal UtilizationRatio)>();

        foreach (var location in candidates)
        {
            if (lockedLocationIds.Contains(location.Id))
                continue;

            var rows = stockRows.Where(x => x.LocationId == location.Id).ToList();
            if (location.StorageClass is not null && rows.Any(x => !string.Equals(x.StorageClass, location.StorageClass, StringComparison.Ordinal)))
                continue;

            var ratios = new List<decimal>();
            if (location.MaxWeightKg.HasValue && location.MaxWeightKg.Value > 0)
            {
                if (!product.UnitWeightKg.HasValue || rows.Any(x => !x.UnitWeightKg.HasValue))
                    continue;
                var used = rows.Sum(x => x.Quantity * x.UnitWeightKg!.Value);
                if (used + incomingBaseQty * product.UnitWeightKg.Value > location.MaxWeightKg.Value)
                    continue;
                ratios.Add(used / location.MaxWeightKg.Value);
            }
            if (location.MaxVolumeM3.HasValue && location.MaxVolumeM3.Value > 0)
            {
                if (!product.UnitVolumeM3.HasValue || rows.Any(x => !x.UnitVolumeM3.HasValue))
                    continue;
                var used = rows.Sum(x => x.Quantity * x.UnitVolumeM3!.Value);
                if (used + incomingBaseQty * product.UnitVolumeM3.Value > location.MaxVolumeM3.Value)
                    continue;
                ratios.Add(used / location.MaxVolumeM3.Value);
            }
            if (location.MaxPalletEquivalent.HasValue && location.MaxPalletEquivalent.Value > 0)
            {
                if (!product.UnitPalletEquivalent.HasValue || rows.Any(x => !x.UnitPalletEquivalent.HasValue))
                    continue;
                var used = rows.Sum(x => x.Quantity * x.UnitPalletEquivalent!.Value);
                if (used + incomingBaseQty * product.UnitPalletEquivalent.Value > location.MaxPalletEquivalent.Value)
                    continue;
                ratios.Add(used / location.MaxPalletEquivalent.Value);
            }

            var hasSameSku = rows.Any(x => x.ProductId == item.ProductId && x.Status == item.InventoryStatus);
            var exactStorageClass = location.StorageClass is not null && string.Equals(location.StorageClass, productStorageClass, StringComparison.Ordinal);
            var utilization = ratios.Count == 0 ? 0m : ratios.Max();
            eligible.Add((location, hasSameSku, exactStorageClass, utilization));
        }

        return eligible
            .OrderByDescending(x => x.HasSameSku)
            .ThenByDescending(x => x.ExactStorageClassMatch)
            .ThenBy(x => x.UtilizationRatio)
            .ThenBy(x => x.Location.Code)
            .Select(x => Location(x.Location))
            .ToList();
    }

    public Task<PutawayTaskDto> AssignAsync(int id, PutawayStateCommandDto dto, CancellationToken token = default) => Mutate(id,dto, [PutawayTaskStatus.Open,PutawayTaskStatus.Assigned], task=>{ task.AssignedUserId=dto.AssignedUserId ?? throw new BusinessRuleException("Người thực hiện là bắt buộc."); task.Status=PutawayTaskStatus.Assigned; },"Assigned",token);
    public Task<PutawayTaskDto> StartAsync(int id, PutawayStateCommandDto dto, CancellationToken token = default) => Mutate(id,dto,[PutawayTaskStatus.Assigned],task=>{ EnsureActor(task); task.Status=PutawayTaskStatus.InProgress; task.StartedAt=DateTime.UtcNow; },"Started",token);
    public Task<PutawayTaskDto> OpenExceptionAsync(int id, PutawayStateCommandDto dto, CancellationToken token = default) => Mutate(id,dto,[PutawayTaskStatus.InProgress],task=>{ EnsureActor(task); task.ExceptionReason=string.IsNullOrWhiteSpace(dto.Reason)?throw new BusinessRuleException("Lý do cần xử lý là bắt buộc."):dto.Reason.Trim(); task.Status=PutawayTaskStatus.Exception; },"ExceptionOpened",token);
    public Task<PutawayTaskDto> ResumeAsync(int id, PutawayStateCommandDto dto, CancellationToken token = default) => Mutate(id,dto,[PutawayTaskStatus.Exception],task=>{ EnsureActor(task); task.Status=PutawayTaskStatus.InProgress; task.ExceptionReason=null; },"Resumed",token);
    public Task<PutawayTaskDto> CancelAsync(int id, PutawayStateCommandDto dto, CancellationToken token = default) => Mutate(id,dto,[PutawayTaskStatus.Open,PutawayTaskStatus.Assigned],task=>{ if(task.Items.Any(x=>x.MovedBaseQuantity>0)) throw Conflict("Không thể hủy nhiệm vụ đã phát sinh di chuyển."); task.Status=PutawayTaskStatus.Cancelled; task.CancelledAt=DateTime.UtcNow; },"Cancelled",token);

    private async Task<PutawayTaskDto> Mutate(int id,PutawayStateCommandDto dto,PutawayTaskStatus[] allowed,Action<PutawayTask> change,string action,CancellationToken token)
    {
        await using var tx=context.Database.CurrentTransaction is null?await context.Database.BeginTransactionAsync(token):null; try{var task=await Load(id,token);if(!allowed.Contains(task.Status))throw Conflict("Trạng thái nhiệm vụ không còn phù hợp.");ApplyVersion(task.RowVersion,dto.RowVersion,context.Entry(task).Property(x=>x.RowVersion));change(task);context.AuditLogs.Add(Audit(currentUser.UserId,$"PutawayTask.{action}",task.Id,task.WarehouseId,$"Status: {task.Status}"));await context.SaveChangesAsync(token);if(tx is not null)await tx.CommitAsync(token);return await GetAsync(id,token);}catch(DbUpdateConcurrencyException ex){if(tx is not null)await tx.RollbackAsync(token);throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.",ex);}catch{if(tx is not null)await tx.RollbackAsync(token);throw;}
    }
    private async Task<PutawayTask> Load(int id,CancellationToken token){var task=await LoadTaskWithFallbackAsync(id, asNoTracking: false, token)??throw new NotFoundException("Không tìm thấy nhiệm vụ hoặc bạn không có quyền truy cập.");await warehouses.EnsureWarehouseAccessAsync(task.WarehouseId,token);return task;}
    private async Task<PutawayTask?> LoadTaskWithFallbackAsync(int id, bool asNoTracking, CancellationToken token)
    {
        var query = asNoTracking ? Query().AsNoTracking() : Query();
        var task = await query.SingleOrDefaultAsync(x => x.Id == id, token);
        if (task is not null) return task;

        var fallbackQuery = context.PutawayTasks
            .Include(x => x.Warehouse)
            .Include(x => x.Items)
                .ThenInclude(x => x.SourceLocation)
            .AsQueryable();
        if (asNoTracking) fallbackQuery = fallbackQuery.AsNoTracking();
        task = await fallbackQuery.SingleOrDefaultAsync(x => x.Id == id, token);
        if (task is null) return null;

        if (task.Items.Count == 0)
        {
            var itemsQuery = context.PutawayTaskItems
                .Include(x => x.SourceLocation)
                .Where(x => x.PutawayTaskId == id);
            if (asNoTracking) itemsQuery = itemsQuery.AsNoTracking();
            var items = await itemsQuery.ToListAsync(token);
            foreach (var it in items) task.Items.Add(it);
        }

        return task;
    }
}
