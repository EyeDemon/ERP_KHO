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

public sealed class PutawayService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouses,
    ICurrentUser currentUser,
    IInventoryLockEvaluator? inventoryLocks = null)
    : IPutawayService, IReceiptPutawayIntegration
{
    private readonly IInventoryLockEvaluator? _inventoryLocks = inventoryLocks;
    public async Task<IReadOnlyList<WarehouseDto>> ListLocationWarehousesAsync(CancellationToken token = default)
    {
        var ids = await warehouses.GetAccessibleWarehouseIdsAsync(token);
        return await context.Warehouses.AsNoTracking()
            .Where(x => ids.Contains(x.Id) && x.IsActive)
            .OrderBy(x => x.Code)
            .Select(x => new WarehouseDto { Id=x.Id, Code=x.Code.Trim(), Name=x.Name.Trim(), Address=x.Address == null ? null : x.Address.Trim(), IsActive=x.IsActive, CreatedAt=x.CreatedAt })
            .ToListAsync(token);
    }

    public async Task<IReadOnlyList<WarehouseLocationDto>> ListLocationsAsync(int warehouseId, CancellationToken token = default)
    {
        await warehouses.EnsureWarehouseAccessAsync(warehouseId, token);
        return (await context.WarehouseLocations.AsNoTracking().Where(x => x.WarehouseId == warehouseId).OrderBy(x => x.Code).ToListAsync(token)).Select(Location).ToList();
    }

    public async Task<IReadOnlyList<WarehouseLocationCapacityDto>> ListLocationCapacitiesAsync(int warehouseId, CancellationToken token = default)
    {
        await warehouses.EnsureWarehouseAccessAsync(warehouseId, token);
        var locations = await context.WarehouseLocations.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId && !x.IsSystemManaged)
            .OrderBy(x => x.Code)
            .ToListAsync(token);
        var stocks = await context.InventoryStocks.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId && x.Quantity > 0)
            .Join(context.Products.AsNoTracking(), stock => stock.ProductId, product => product.Id,
                (stock, product) => new
                {
                    stock.LocationId,
                    stock.Quantity,
                    product.StorageClass,
                    product.UnitWeightKg,
                    product.UnitVolumeM3,
                    product.UnitPalletEquivalent
                })
            .ToListAsync(token);

        return locations.Select(location =>
        {
            var rows = stocks.Where(x => x.LocationId == location.Id).ToList();
            var usedWeight = rows.Count == 0 ? 0m : rows.All(x => x.UnitWeightKg.HasValue) ? rows.Sum(x => x.Quantity * x.UnitWeightKg!.Value) : (decimal?)null;
            var usedVolume = rows.Count == 0 ? 0m : rows.All(x => x.UnitVolumeM3.HasValue) ? rows.Sum(x => x.Quantity * x.UnitVolumeM3!.Value) : (decimal?)null;
            var usedPallet = rows.Count == 0 ? 0m : rows.All(x => x.UnitPalletEquivalent.HasValue) ? rows.Sum(x => x.Quantity * x.UnitPalletEquivalent!.Value) : (decimal?)null;
            var profileIncomplete = (location.MaxWeightKg.HasValue && !usedWeight.HasValue)
                || (location.MaxVolumeM3.HasValue && !usedVolume.HasValue)
                || (location.MaxPalletEquivalent.HasValue && !usedPallet.HasValue);
            var compatibilityConflict = location.StorageClass is not null
                && rows.Any(x => !string.Equals(x.StorageClass?.Trim(), location.StorageClass.Trim(), StringComparison.Ordinal));
            var overCapacity = (location.MaxWeightKg.HasValue && usedWeight.HasValue && usedWeight > location.MaxWeightKg)
                || (location.MaxVolumeM3.HasValue && usedVolume.HasValue && usedVolume > location.MaxVolumeM3)
                || (location.MaxPalletEquivalent.HasValue && usedPallet.HasValue && usedPallet > location.MaxPalletEquivalent);
            var nearCapacity = (location.MaxWeightKg.HasValue && usedWeight.HasValue && usedWeight >= location.MaxWeightKg * 0.85m)
                || (location.MaxVolumeM3.HasValue && usedVolume.HasValue && usedVolume >= location.MaxVolumeM3 * 0.85m)
                || (location.MaxPalletEquivalent.HasValue && usedPallet.HasValue && usedPallet >= location.MaxPalletEquivalent * 0.85m);

            var state = !location.IsActive ? "Inactive"
                : location.IsBlocked ? "Blocked"
                : compatibilityConflict ? "CompatibilityConflict"
                : profileIncomplete ? "ProfileIncomplete"
                : overCapacity ? "OverCapacity"
                : nearCapacity ? "NearCapacity"
                : "Available";

            return new WarehouseLocationCapacityDto
            {
                LocationId = location.Id,
                Code = location.Code.Trim(),
                Name = location.Name.Trim(),
                StructurePath = location.StructurePath?.Trim(),
                StorageClass = location.StorageClass?.Trim(),
                MaxWeightKg = location.MaxWeightKg,
                UsedWeightKg = usedWeight,
                MaxVolumeM3 = location.MaxVolumeM3,
                UsedVolumeM3 = usedVolume,
                MaxPalletEquivalent = location.MaxPalletEquivalent,
                UsedPalletEquivalent = usedPallet,
                ProfileIncomplete = profileIncomplete,
                CompatibilityConflict = compatibilityConflict,
                IsActive = location.IsActive,
                IsBlocked = location.IsBlocked,
                State = state
            };
        }).ToList();
    }

    public async Task<WarehouseMapDto> GetWarehouseMapAsync(int warehouseId, CancellationToken token = default)
    {
        await warehouses.EnsureWarehouseAccessAsync(warehouseId, token);
        var generatedAt = DateTime.UtcNow;
        var capacities = await ListLocationCapacitiesAsync(warehouseId, token);
        var capacityById = capacities.ToDictionary(x => x.LocationId);
        var locations = await context.WarehouseLocations.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId && !x.IsSystemManaged)
            .OrderBy(x => x.Code)
            .ToListAsync(token);

        var recentSince = generatedAt.AddMinutes(-60);
        var recentMovements = await context.InventoryLocationMovements.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId && x.CreatedAt >= recentSince)
            .Select(x => new { x.FromLocationId, x.ToLocationId })
            .ToListAsync(token);
        var recentCounts = new Dictionary<int, int>();
        foreach (var movement in recentMovements)
        {
            recentCounts[movement.FromLocationId] = recentCounts.GetValueOrDefault(movement.FromLocationId) + 1;
            recentCounts[movement.ToLocationId] = recentCounts.GetValueOrDefault(movement.ToLocationId) + 1;
        }

        var activeCounts = await context.PutawayTaskItems.AsNoTracking()
            .Where(x => x.PutawayTask.WarehouseId == warehouseId
                && x.PutawayTask.Status != PutawayTaskStatus.Completed
                && x.PutawayTask.Status != PutawayTaskStatus.Cancelled)
            .GroupBy(x => x.SourceLocationId)
            .Select(group => new { LocationId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.LocationId, x => x.Count, token);

        var canMutate = currentUser.Role is "Admin" or "Manager" or "WarehouseStaff";
        return new WarehouseMapDto
        {
            WarehouseId = warehouseId,
            GeneratedAtUtc = generatedAt,
            Items = locations.Select(location =>
            {
                capacityById.TryGetValue(location.Id, out var capacity);
                var utilization = CapacityUtilizationPercent(capacity);
                var recent = recentCounts.GetValueOrDefault(location.Id);
                var active = activeCounts.GetValueOrDefault(location.Id);
                var activity = recent >= 10 || active >= 5 ? "High" : recent >= 4 || active >= 2 ? "Medium" : "Low";
                return new WarehouseLocationMapItemDto
                {
                    LocationId = location.Id,
                    Code = location.Code.Trim(),
                    Name = location.Name.Trim(),
                    StructurePath = location.StructurePath?.Trim(),
                    StorageClass = location.StorageClass?.Trim(),
                    MapX = location.MapX,
                    MapY = location.MapY,
                    MapWidth = location.MapWidth,
                    MapHeight = location.MapHeight,
                    UtilizationPercent = utilization,
                    CapacityState = capacity?.State ?? "Available",
                    RecentMovementCount = recent,
                    ActivePutawayCount = active,
                    ActivityLevel = activity,
                    IsActive = location.IsActive,
                    IsBlocked = location.IsBlocked,
                    RowVersion = canMutate ? Convert.ToBase64String(location.RowVersion) : null
                };
            }).ToList()
        };
    }

    public async Task<WarehouseLocationDto> UpdateLocationLayoutAsync(int id, UpdateWarehouseLocationLayoutDto dto, CancellationToken token = default)
    {
        await using var transaction = context.Database.CurrentTransaction is null ? await context.Database.BeginTransactionAsync(token) : null;
        var entity = await LocationForMutation(id, token) ?? throw new NotFoundException("Không tìm thấy vị trí hoặc bạn không có quyền truy cập.");
        await warehouses.EnsureWarehouseAccessAsync(entity.WarehouseId, token);
        ApplyLocationVersion(entity, dto.RowVersion);
        if (entity.IsSystemManaged || entity.Code is "RECEIVING" or "LEGACY" or "DAMAGED") throw new BusinessRuleException("Không thể cấu hình layout cho vị trí hệ thống.");

        ValidateMapLayout(dto.MapX, dto.MapY, dto.MapWidth, dto.MapHeight);
        entity.MapX = dto.MapX;
        entity.MapY = dto.MapY;
        entity.MapWidth = dto.MapWidth;
        entity.MapHeight = dto.MapHeight;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = currentUser.UserId;
        context.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = "WarehouseLocation.LayoutUpdated",
            EntityName = "WarehouseLocation",
            EntityId = entity.Id,
            WarehouseId = entity.WarehouseId,
            NewValues = $"MapX: {entity.MapX}; MapY: {entity.MapY}; MapWidth: {entity.MapWidth}; MapHeight: {entity.MapHeight}",
            Result = "Success",
            Timestamp = DateTime.UtcNow
        });

        try { await context.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException ex) { throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.", ex); }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 or 547 })
        {
            throw Conflict("Không thể cập nhật layout vị trí kho do xung đột ràng buộc dữ liệu.");
        }
        if (transaction is not null) await transaction.CommitAsync(token);
        return Location(entity);
    }

    public async Task<WarehouseLocationDto> CreateLocationAsync(CreateWarehouseLocationDto dto, CancellationToken token = default)
    {
        await warehouses.EnsureWarehouseAccessAsync(dto.WarehouseId, token);
        var code = Required(dto.Code, "Mã vị trí").ToUpperInvariant();
        var name = Required(dto.Name, "Tên vị trí");
        var structurePath = NormalizeStructurePath(dto.StructurePath);
        var storageClass = NormalizeStorageClass(dto.StorageClass);
        ValidateCapacityLimits(dto.MaxWeightKg, dto.MaxVolumeM3, dto.MaxPalletEquivalent);
        if (code is "RECEIVING" or "LEGACY" or "DAMAGED")
            throw new BusinessRuleException("Mã vị trí hệ thống (RECEIVING, LEGACY, DAMAGED) được quản lý tự động.");
        if (!Enum.TryParse<WarehouseLocationType>(dto.LocationType, true, out var type) || type is WarehouseLocationType.Receiving or WarehouseLocationType.Legacy)
            throw new BusinessRuleException("Loại vị trí không hợp lệ cho vị trí do người dùng tạo.");
        if (await context.WarehouseLocations.AnyAsync(x => x.WarehouseId == dto.WarehouseId && x.Code == code, token)) throw Conflict("Mã vị trí đã tồn tại trong kho.");
        ValidateFlags(type, dto.IsPickable, dto.IsReceivable);
        var entity = new WarehouseLocation { WarehouseId=dto.WarehouseId, Code=code, Name=name, StructurePath=structurePath, StorageClass=storageClass, MaxWeightKg=dto.MaxWeightKg, MaxVolumeM3=dto.MaxVolumeM3, MaxPalletEquivalent=dto.MaxPalletEquivalent, LocationType=type, IsPickable=dto.IsPickable, IsReceivable=dto.IsReceivable, CreatedBy=currentUser.UserId };
        context.WarehouseLocations.Add(entity);
        try { await context.SaveChangesAsync(token); }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 or 547 })
        {
            throw Conflict("Mã vị trí đã tồn tại trong kho hoặc dữ liệu tham chiếu không hợp lệ.");
        }
        return Location(entity);
    }

    public async Task<WarehouseLocationDto> UpdateLocationAsync(int id, UpdateWarehouseLocationDto dto, CancellationToken token = default)
    {
        await using var transaction = context.Database.CurrentTransaction is null ? await context.Database.BeginTransactionAsync(token) : null;
        var entity = await LocationForMutation(id, token) ?? throw new NotFoundException("Không tìm thấy vị trí hoặc bạn không có quyền truy cập.");
        await warehouses.EnsureWarehouseAccessAsync(entity.WarehouseId, token);
        ApplyLocationVersion(entity, dto.RowVersion);
        var structurePath = NormalizeStructurePath(dto.StructurePath);
        if ((entity.IsSystemManaged || entity.Code is "RECEIVING" or "LEGACY" or "DAMAGED") && (!dto.IsActive || dto.IsBlocked || dto.IsPickable != entity.IsPickable || dto.IsReceivable != entity.IsReceivable || structurePath is not null || dto.UpdateConstraints))
            throw new BusinessRuleException("Không thể thay đổi thuộc tính vận hành, cấu trúc hoặc capacity của vị trí hệ thống.");
        if (entity.StructurePath is not null && structurePath is not null && !string.Equals(entity.StructurePath, structurePath, StringComparison.Ordinal))
            throw Conflict("Đường dẫn cấu trúc đã gán không được thay đổi. Hãy tạo vị trí mới để bảo toàn truy vết lịch sử.");
        if (entity.StructurePath is null && structurePath is not null)
        {
            var hasUsage = await context.InventoryStocks.AnyAsync(x => x.LocationId == id && x.Quantity != 0, token)
                || await context.InventoryLocationMovements.AnyAsync(x => x.FromLocationId == id || x.ToLocationId == id, token)
                || await context.InventoryTransactions.AnyAsync(x => x.LocationId == id || x.FromLocationId == id || x.ToLocationId == id, token)
                || await context.PutawayTaskItems.AnyAsync(x => x.SourceLocationId == id && x.PutawayTask.Status != PutawayTaskStatus.Completed && x.PutawayTask.Status != PutawayTaskStatus.Cancelled, token)
                || await context.PickingTaskLines.AnyAsync(x => x.SourceLocationId == id && x.PickingTask.Status != PickingTaskStatus.Completed && x.PickingTask.Status != PickingTaskStatus.Cancelled, token);
            if (hasUsage) throw Conflict("Không thể gắn cấu trúc cho vị trí đã có tồn kho, movement hoặc nhiệm vụ đang hoạt động.");
            entity.StructurePath = structurePath;
        }
        if (dto.UpdateConstraints)
        {
            var storageClass = NormalizeStorageClass(dto.StorageClass);
            ValidateCapacityLimits(dto.MaxWeightKg, dto.MaxVolumeM3, dto.MaxPalletEquivalent);
            await EnsureExistingStockFitsConstraintsAsync(entity.Id, storageClass, dto.MaxWeightKg, dto.MaxVolumeM3, dto.MaxPalletEquivalent, token);
            entity.StorageClass = storageClass;
            entity.MaxWeightKg = dto.MaxWeightKg;
            entity.MaxVolumeM3 = dto.MaxVolumeM3;
            entity.MaxPalletEquivalent = dto.MaxPalletEquivalent;
        }
        ValidateFlags(entity.LocationType, dto.IsPickable, dto.IsReceivable);
        if (!dto.IsActive && (await context.InventoryStocks.AnyAsync(x => x.LocationId == id && (x.Quantity != 0 || x.ReservedQuantity != 0), token) ||
            await context.StockAllocations.AnyAsync(x => x.LocationId == id && (x.Status == StockAllocationStatus.Active || x.Status == StockAllocationStatus.Picking || x.Status == StockAllocationStatus.Picked), token) ||
            await context.PickingTaskLines.AnyAsync(x => x.SourceLocationId == id && x.PickingTask.Status != PickingTaskStatus.Completed && x.PickingTask.Status != PickingTaskStatus.Cancelled, token) ||
            await context.PutawayTaskItems.AnyAsync(x => x.SourceLocationId == id && x.PutawayTask.Status != PutawayTaskStatus.Completed && x.PutawayTask.Status != PutawayTaskStatus.Cancelled, token) ||
            await context.InventoryLocks.AnyAsync(x => x.LocationId == id && x.Status == InventoryLockStatus.Active, token)))
            throw Conflict("Không thể ngừng hoạt động vị trí đang có tồn kho, giữ hàng hoặc nhiệm vụ chưa hoàn tất.");
        if ((dto.IsBlocked || !dto.IsPickable) && (await context.InventoryStocks.AnyAsync(x => x.LocationId == id && x.ReservedQuantity > 0, token) ||
            await context.StockAllocations.AnyAsync(x => x.LocationId == id && (x.Status == StockAllocationStatus.Active || x.Status == StockAllocationStatus.Picking || x.Status == StockAllocationStatus.Picked), token) ||
            await context.PickingTaskLines.AnyAsync(x => x.SourceLocationId == id && x.PickingTask.Status != PickingTaskStatus.Completed && x.PickingTask.Status != PickingTaskStatus.Cancelled, token)))
            throw Conflict("Không thể khóa hoặc tắt quyền lấy hàng của vị trí đang có cam kết giữ hàng hoặc phân bổ đang mở.");
        entity.Name=Required(dto.Name,"Tên vị trí"); entity.IsActive=dto.IsActive; entity.IsBlocked=dto.IsBlocked; entity.IsPickable=dto.IsPickable; entity.IsReceivable=dto.IsReceivable; entity.UpdatedAt=DateTime.UtcNow; entity.UpdatedBy=currentUser.UserId;
        try { await context.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException ex) { throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.", ex); }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 or 547 })
        {
            throw Conflict("Không thể cập nhật vị trí kho do đang có dữ liệu phát sinh liên quan.");
        }
        if (transaction is not null) await transaction.CommitAsync(token);
        return Location(entity);
    }
    public async Task<int> GetReceivingLocationIdAsync(int warehouseId, CancellationToken token = default) =>
        await context.WarehouseLocations.Where(x => x.WarehouseId == warehouseId && x.LocationType == WarehouseLocationType.Receiving && x.IsSystemManaged && x.IsActive)
            .Select(x => x.Id).SingleOrDefaultAsync(token) is var id && id > 0 ? id : throw new BusinessRuleException("Kho chưa được cấu hình vị trí nhận hàng.");

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

    public async Task<PutawayTaskDto> EvaluateAndExecuteCrossDockAsync(int id, ExecuteCrossDockDto? dto = null, CancellationToken token = default)
    {
        var ownTransaction = context.Database.CurrentTransaction is null;
        var isSqlServer = context.Database.IsRelational() && context.Database.IsSqlServer();
        await using var tx = ownTransaction ? await context.Database.BeginTransactionAsync(token) : null;
        try
        {
            if (isSqlServer)
            {
                var tenantId = context.CurrentTenantId;
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM dbo.PutawayTasks WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND Id = {id}; SELECT 1 FROM dbo.PutawayTaskItems WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND PutawayTaskId = {id};",
                    token);
            }

            var task = await Load(id, token);
            var nowUtc = DateTime.UtcNow;
            var actorId = currentUser.UserId > 0 ? currentUser.UserId : (task.CreatedBy > 0 ? task.CreatedBy : 1);

            if (dto?.Reverse == true)
            {
                if (!task.IsCrossDock || task.CrossDockAllocatedQty <= 0m)
                {
                    throw new BusinessRuleException("Nhiệm vụ chưa phát sinh Cross-Dock để đảo ngược.");
                }

                var reverseProductIds = task.Items.Select(x => x.ProductId).Distinct().OrderBy(x => x).ToList();
                await context.AcquireInventoryAllocationLocksAsync(task.WarehouseId, reverseProductIds, token);

                var alreadyReversedIds = await context.InventoryTransactions
                    .AsNoTracking()
                    .Where(t => t.ReferenceId == task.Id && t.ReversalOfTransactionId != null)
                    .Select(t => t.ReversalOfTransactionId!.Value)
                    .ToListAsync(token);

                var cdRefTypes = new[] { "PutawayOut", "PutawayIn" };
                var cdTxs = await context.InventoryTransactions
                    .AsNoTracking()
                    .Where(t => t.ReferenceId == task.Id &&
                                t.TransactionType != TransactionType.Reversal &&
                                t.ReferenceType != null &&
                                cdRefTypes.Contains(t.ReferenceType) &&
                                !alreadyReversedIds.Contains(t.Id))
                    .OrderBy(t => t.Id)
                    .ToListAsync(token);

                foreach (var orig in cdTxs)
                {
                    var revDelta = -orig.Quantity;
                    var bucket = await context.InventoryStocks
                        .FirstOrDefaultAsync(s =>
                            s.WarehouseId == orig.WarehouseId &&
                            s.ProductId == orig.ProductId &&
                            s.LocationId == orig.LocationId &&
                            s.LotId == orig.LotId &&
                            s.SerialId == orig.SerialId &&
                            s.HandlingUnitId == orig.HandlingUnitId &&
                            s.OwnerId == orig.OwnerId &&
                            s.Status == orig.InventoryStatus,
                            token);

                    if (bucket != null)
                    {
                        if (revDelta < 0m)
                        {
                            var releaseAmt = Math.Min(-revDelta, bucket.ReservedQuantity);
                            bucket.ReservedQuantity = Math.Max(0m, bucket.ReservedQuantity - releaseAmt);
                            bucket.AllocatedQuantity = Math.Max(0m, bucket.AllocatedQuantity - Math.Min(-revDelta, bucket.AllocatedQuantity));
                        }
                        bucket.Quantity = Math.Max(0m, bucket.Quantity + revDelta);
                        bucket.LastUpdated = nowUtc;
                        bucket.RefreshGrainHash();
                    }

                    context.InventoryTransactions.Add(new InventoryTransaction
                    {
                        ProductId = orig.ProductId,
                        WarehouseId = orig.WarehouseId,
                        LocationId = orig.LocationId,
                        FromLocationId = orig.ToLocationId,
                        ToLocationId = orig.FromLocationId,
                        LotId = orig.LotId,
                        SerialId = orig.SerialId,
                        HandlingUnitId = orig.HandlingUnitId,
                        OwnerId = orig.OwnerId,
                        InventoryStatus = orig.InventoryStatus,
                        FromInventoryStatus = orig.ToInventoryStatus,
                        ToInventoryStatus = orig.FromInventoryStatus,
                        TransactionType = TransactionType.Reversal,
                        Quantity = revDelta,
                        UnitCost = orig.UnitCost,
                        ReferenceId = task.Id,
                        ReferenceType = orig.ReferenceType,
                        SourceDocumentLineId = orig.SourceDocumentLineId,
                        ReversalOfTransactionId = orig.Id,
                        ReasonCode = "CROSS_DOCK_REVERSAL",
                        TransactionDate = nowUtc,
                        ActualOccurredAtUtc = nowUtc,
                        PostedAtUtc = nowUtc,
                        CreatedBy = actorId,
                        Note = $"Đảo bút toán Cross-Dock PutawayTask #{task.Id} (OrigTx=#{orig.Id})"
                    });
                }

                var cdAllocations = await context.StockAllocations
                    .Include(a => a.Reservation)
                    .Where(a => a.WarehouseId == task.WarehouseId &&
                                a.SelectionReason == $"CROSS_DOCK_TASK_{task.Id}" &&
                                a.Status == StockAllocationStatus.Active)
                    .ToListAsync(token);

                foreach (var alloc in cdAllocations)
                {
                    alloc.Status = StockAllocationStatus.Released;
                    alloc.ReleasedAt = nowUtc;
                    alloc.ReleasedBy = actorId;
                    alloc.ReleaseReason = dto.Reason ?? "Cross-Dock Reversal";
                    if (alloc.Reservation != null)
                    {
                        if (alloc.Reservation.SourceCode == $"CD-TASK-{task.Id}")
                        {
                            alloc.Reservation.ReleasedQuantity = alloc.Reservation.Quantity;
                            alloc.Reservation.AllocatedQuantity = 0m;
                            alloc.Reservation.Status = StockReservationStatus.Released;
                            alloc.Reservation.ReleasedAt = nowUtc;
                            alloc.Reservation.ReleasedBy = actorId;
                            alloc.Reservation.ReleaseReason = dto.Reason ?? "Cross-Dock Reversal";
                        }
                        else
                        {
                            alloc.Reservation.Quantity = Math.Max(0m, alloc.Reservation.Quantity - alloc.Quantity);
                            alloc.Reservation.AllocatedQuantity = Math.Max(0m, alloc.Reservation.AllocatedQuantity - alloc.Quantity);
                            if (alloc.Reservation.AllocatedQuantity > 0m)
                            {
                                alloc.Reservation.Status = StockReservationStatus.Allocated;
                                alloc.Reservation.ReleasedQuantity = 0m;
                                alloc.Reservation.ReleasedAt = null;
                                alloc.Reservation.ReleasedBy = null;
                                alloc.Reservation.ReleaseReason = null;
                            }
                            else
                            {
                                alloc.Reservation.Status = StockReservationStatus.Released;
                                alloc.Reservation.ReleasedQuantity = alloc.Reservation.Quantity;
                                alloc.Reservation.ReleasedAt = nowUtc;
                                alloc.Reservation.ReleasedBy = actorId;
                                alloc.Reservation.ReleaseReason = dto.Reason ?? "Cross-Dock Reversal";
                            }
                        }
                    }
                }

                if (task.CrossDockTargetBackorderId.HasValue)
                {
                    var bo = await context.Backorders.FirstOrDefaultAsync(b => b.Id == task.CrossDockTargetBackorderId.Value, token);
                    if (bo != null)
                    {
                        bo.RecoveredQuantity = Math.Max(0m, bo.RecoveredQuantity - task.CrossDockAllocatedQty);
                        bo.Status = BackorderStatus.Open;
                        bo.ResolvedAt = null;
                        bo.UpdatedAt = nowUtc;
                    }
                }

                foreach (var it in task.Items)
                {
                    it.MovedBaseQuantity = Math.Max(0m, it.MovedBaseQuantity - task.CrossDockAllocatedQty);
                }

                task.IsCrossDock = false;
                task.CrossDockAllocatedQty = 0m;
                task.Status = PutawayTaskStatus.Open;
                task.CompletedAt = null;
                context.AuditLogs.Add(Audit(actorId, "PutawayTask.CrossDockReversed", task.Id, task.WarehouseId, $"Reason: {dto.Reason ?? "Cross-Dock Reversal"}"));
                await context.SaveChangesAsync(token);
                if (tx is not null) await tx.CommitAsync(token);
                return await GetAsync(id, token);
            }

            if (task.IsCrossDock && task.Status == PutawayTaskStatus.Completed)
            {
                throw ConflictWithCode("CROSS_DOCK_ALREADY_COMPLETED", "Nhiệm vụ Cross-Dock đã được thực thi hoàn tất.");
            }
            if (task.Status is PutawayTaskStatus.Cancelled or PutawayTaskStatus.Completed)
            {
                throw Conflict("Nhiệm vụ không ở trạng thái có thể thực thi Cross-Dock.");
            }

            var item = (dto?.ItemId is > 0
                ? task.Items.FirstOrDefault(x => x.Id == dto.ItemId.Value)
                : task.Items.FirstOrDefault(x => x.InventoryStatus == InventoryStatus.Available && x.RemainingBaseQuantity > 0))
                ?? task.Items.FirstOrDefault();

            if (item is null)
                throw new NotFoundException("Không tìm thấy dòng hàng cất kho.");
            if (item.InventoryStatus != InventoryStatus.Available)
                throw new BusinessRuleException("Chỉ hàng hóa ở trạng thái Khả dụng (Available) mới được phép Cross-Dock trực tiếp sang khu vực xuất hàng.");
            if (item.RemainingBaseQuantity <= 0m)
                throw Conflict("Dòng hàng đã được cất hết số lượng yêu cầu.");

            await context.AcquireInventoryAllocationLocksAsync([(task.WarehouseId, item.ProductId)], token);

            // Match open Backorder or high-priority outbound SalesOrder / ExportReceipt
            var targetBoId = dto?.TargetBackorderId ?? task.CrossDockTargetBackorderId;
            Backorder? matchedBackorder = null;
            if (targetBoId is > 0)
            {
                matchedBackorder = await context.Backorders
                    .Include(b => b.SalesOrderLine)
                    .FirstOrDefaultAsync(b =>
                        b.Id == targetBoId.Value &&
                        b.WarehouseId == task.WarehouseId &&
                        b.Status == BackorderStatus.Open &&
                        b.Quantity - b.RecoveredQuantity - b.CancelledQuantity > 0,
                        token);
            }
            matchedBackorder ??= await context.Backorders
                .Include(b => b.SalesOrderLine)
                    .ThenInclude(l => l.SalesOrder)
                .Where(b => b.WarehouseId == task.WarehouseId &&
                            b.Status == BackorderStatus.Open &&
                            b.Quantity - b.RecoveredQuantity - b.CancelledQuantity > 0 &&
                            b.SalesOrderLine.ProductId == item.ProductId)
                .OrderByDescending(b => b.SalesOrderLine.SalesOrder.Priority)
                .ThenBy(b => b.CreatedAt)
                .FirstOrDefaultAsync(token);

            int? matchedSalesOrderId = dto?.TargetSalesOrderId ?? matchedBackorder?.SalesOrderLine?.SalesOrderId ?? task.CrossDockTargetSalesOrderId;
            decimal? outboundDemandQty = matchedBackorder != null
                ? Math.Max(0m, matchedBackorder.Quantity - matchedBackorder.RecoveredQuantity - matchedBackorder.CancelledQuantity)
                : null;

            if (!matchedSalesOrderId.HasValue)
            {
                var waitingSoLine = await context.SalesOrderLines
                    .Include(l => l.SalesOrder)
                    .Where(l => l.SalesOrder.WarehouseId == task.WarehouseId &&
                                l.ProductId == item.ProductId &&
                                l.OrderedQuantity - l.CancelledQuantity > 0 &&
                                (l.SalesOrder.Status == SalesOrderStatus.Released ||
                                 l.SalesOrder.Status == SalesOrderStatus.PartiallyFulfilled ||
                                 l.SalesOrder.Status == SalesOrderStatus.Draft))
                    .OrderByDescending(l => l.SalesOrder.Priority)
                    .ThenBy(l => l.SalesOrder.CreatedAt)
                    .FirstOrDefaultAsync(token);
                if (waitingSoLine != null)
                {
                    matchedSalesOrderId = waitingSoLine.SalesOrderId;
                    outboundDemandQty ??= Math.Max(0m, waitingSoLine.OrderedQuantity - waitingSoLine.CancelledQuantity);
                }
            }

            int? matchedExportReceiptId = dto?.TargetExportReceiptId ?? task.CrossDockTargetExportReceiptId;
            if (!matchedExportReceiptId.HasValue)
            {
                var waitingExportLine = await context.ExportReceiptDetails
                    .Include(d => d.ExportReceipt)
                    .Where(d => d.ExportReceipt.WarehouseId == task.WarehouseId &&
                                d.ProductId == item.ProductId &&
                                d.Quantity > 0 &&
                                (d.ExportReceipt.Status == ReceiptStatus.Draft || d.ExportReceipt.Status == ReceiptStatus.Approved))
                    .OrderByDescending(d => d.ExportReceipt.Id)
                    .FirstOrDefaultAsync(token);
                if (waitingExportLine != null)
                {
                    matchedExportReceiptId = waitingExportLine.ExportReceiptId;
                    outboundDemandQty ??= waitingExportLine.Quantity;
                }
            }

            // Resolve Outbound Staging location (SHIPPING-STAGE / STAGE-OUT-01 / STAGE-01)
            WarehouseLocation? stagingLoc = null;
            if (dto?.OutboundStagingLocationId is > 0)
            {
                stagingLoc = await LocationForMutation(dto.OutboundStagingLocationId.Value, token);
            }
            if (stagingLoc is null && !string.IsNullOrWhiteSpace(dto?.OutboundStagingLocationCode))
            {
                var code = dto.OutboundStagingLocationCode.Trim().ToUpperInvariant();
                stagingLoc = await LocationByCodeForMutation(task.WarehouseId, code, token);
            }
            if (stagingLoc is null)
            {
                var stagingCodes = new[] { "SHIPPING-STAGE", "STAGE-OUT-01", "STAGE-01" };
                stagingLoc = await context.WarehouseLocations
                    .Where(l => l.WarehouseId == task.WarehouseId && l.IsActive && !l.IsBlocked && stagingCodes.Contains(l.Code))
                    .OrderBy(l => l.Id)
                    .FirstOrDefaultAsync(token);
            }
            if (stagingLoc is null)
            {
                stagingLoc = await context.WarehouseLocations
                    .Where(l => l.WarehouseId == task.WarehouseId &&
                                l.IsActive &&
                                !l.IsBlocked &&
                                l.Id != item.SourceLocationId &&
                                l.LocationType == WarehouseLocationType.Storage &&
                                l.IsPickable)
                    .OrderBy(l => l.Code)
                    .FirstOrDefaultAsync(token);
            }
            if (stagingLoc is null)
            {
                stagingLoc = new WarehouseLocation
                {
                    WarehouseId = task.WarehouseId,
                    Code = "SHIPPING-STAGE",
                    Name = "Bãi tập kết xuất hàng Cross-Dock",
                    LocationType = WarehouseLocationType.Storage,
                    IsPickable = true,
                    IsReceivable = false,
                    IsActive = true,
                    IsBlocked = false,
                    CreatedBy = actorId
                };
                context.WarehouseLocations.Add(stagingLoc);
                await context.SaveChangesAsync(token);
            }

            var sourceBuckets = await context.InventoryStocks
                .Include(x => x.Lot)
                .Include(x => x.Serial)
                .Where(x => x.ProductId == item.ProductId &&
                            x.WarehouseId == task.WarehouseId &&
                            x.Status == InventoryStatus.Available &&
                            x.LocationId == item.SourceLocationId &&
                            x.Quantity > x.ReservedQuantity)
                .OrderBy(x => x.LotId)
                .ThenBy(x => x.SerialId)
                .ThenBy(x => x.Id)
                .ToListAsync(token);

            var totalAvailAtReceiving = sourceBuckets.Sum(x => Math.Max(0m, x.Quantity - x.ReservedQuantity));
            if (totalAvailAtReceiving <= 0m)
            {
                throw Conflict("Không còn tồn khả dụng tại bãi tiếp nhận để thực thi Cross-Dock.");
            }

            var targetQty = dto?.Quantity is > 0m
                ? Math.Min(dto.Quantity.Value, item.RemainingBaseQuantity)
                : Math.Min(outboundDemandQty ?? item.RemainingBaseQuantity, item.RemainingBaseQuantity);
            var crossDockQty = Math.Round(Math.Min(targetQty, totalAvailAtReceiving), 4);
            if (crossDockQty <= 0m)
            {
                throw new BusinessRuleException("Số lượng Cross-Dock phải lớn hơn 0.");
            }

            var rsvSourceType = "CrossDockPutawayTask";
            var rsvSourceId = task.Id;
            var reservation = await context.StockReservations
                .FirstOrDefaultAsync(r =>
                    r.ProductId == item.ProductId &&
                    r.SourceType == rsvSourceType &&
                    r.SourceId == rsvSourceId,
                    token);
            if (reservation is null)
            {
                reservation = new StockReservation
                {
                    ReservationCode = $"RSV-CD-{task.Id}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                    ProductId = item.ProductId,
                    WarehouseId = task.WarehouseId,
                    Quantity = crossDockQty,
                    AllocatedQuantity = crossDockQty,
                    ConsumedQuantity = 0m,
                    ReleasedQuantity = 0m,
                    BaseUomIdSnapshot = item.BaseUnitId > 0 ? item.BaseUnitId : 1,
                    BaseUomCodeSnapshot = !string.IsNullOrWhiteSpace(item.BaseUnitCodeSnapshot) ? item.BaseUnitCodeSnapshot : "THÙNG",
                    BaseUomDecimalPlacesSnapshot = item.BaseUnitDecimalPlaces,
                    AllocationVersion = 1,
                    Status = StockReservationStatus.Allocated,
                    SourceType = rsvSourceType,
                    SourceId = rsvSourceId,
                    SourceCode = $"CD-TASK-{task.Id}",
                    CreatedAt = nowUtc,
                    CreatedBy = actorId,
                    ExpiresAt = nowUtc.AddDays(7)
                };
                context.StockReservations.Add(reservation);
            }
            else
            {
                if (reservation.Status is StockReservationStatus.Released or StockReservationStatus.Cancelled)
                {
                    reservation.Quantity = crossDockQty;
                    reservation.AllocatedQuantity = crossDockQty;
                    reservation.ConsumedQuantity = 0m;
                    reservation.ReleasedQuantity = 0m;
                    reservation.ReleasedAt = null;
                    reservation.ReleasedBy = null;
                    reservation.ReleaseReason = null;
                }
                else
                {
                    reservation.Quantity = Math.Round(reservation.Quantity + crossDockQty, 4);
                    reservation.AllocatedQuantity = Math.Round(reservation.AllocatedQuantity + crossDockQty, 4);
                }
                reservation.Status = StockReservationStatus.Allocated;
                reservation.AllocationVersion += 1;
            }
            await context.SaveChangesAsync(token);

            var remToMove = crossDockQty;
            var sourceCode = item.SourceLocation?.Code?.Trim() ?? "RECEIVING";
            foreach (var srcBucket in sourceBuckets)
            {
                if (remToMove <= 0m) break;
                var avail = Math.Max(0m, srcBucket.Quantity - srcBucket.ReservedQuantity);
                if (avail <= 0m) continue;
                var chunk = Math.Min(remToMove, avail);

                srcBucket.Quantity -= chunk;
                srcBucket.LastUpdated = nowUtc;
                srcBucket.RefreshGrainHash();

                var destBucket = context.InventoryStocks.Local.FirstOrDefault(x =>
                                     x.ProductId == item.ProductId &&
                                     x.WarehouseId == task.WarehouseId &&
                                     x.Status == InventoryStatus.Available &&
                                     x.LocationId == stagingLoc.Id &&
                                     x.LotId == srcBucket.LotId &&
                                     x.SerialId == srcBucket.SerialId &&
                                     x.HandlingUnitId == srcBucket.HandlingUnitId &&
                                     x.OwnerId == srcBucket.OwnerId)
                                 ?? await context.InventoryStocks.FirstOrDefaultAsync(x =>
                                     x.ProductId == item.ProductId &&
                                     x.WarehouseId == task.WarehouseId &&
                                     x.Status == InventoryStatus.Available &&
                                     x.LocationId == stagingLoc.Id &&
                                     x.LotId == srcBucket.LotId &&
                                     x.SerialId == srcBucket.SerialId &&
                                     x.HandlingUnitId == srcBucket.HandlingUnitId &&
                                     x.OwnerId == srcBucket.OwnerId, token);

                if (destBucket is null)
                {
                    destBucket = new InventoryStock
                    {
                        ProductId = item.ProductId,
                        WarehouseId = task.WarehouseId,
                        Status = InventoryStatus.Available,
                        LocationId = stagingLoc.Id,
                        LotId = srcBucket.LotId,
                        SerialId = srcBucket.SerialId,
                        HandlingUnitId = srcBucket.HandlingUnitId,
                        OwnerId = srcBucket.OwnerId,
                        Quantity = chunk,
                        ReservedQuantity = chunk,
                        AllocatedQuantity = chunk,
                        MovingAverageCost = srcBucket.MovingAverageCost,
                        LastUpdated = nowUtc
                    };
                    destBucket.RefreshGrainHash();
                    context.InventoryStocks.Add(destBucket);
                }
                else
                {
                    destBucket.Quantity += chunk;
                    destBucket.ReservedQuantity += chunk;
                    destBucket.AllocatedQuantity += chunk;
                    destBucket.LastUpdated = nowUtc;
                    destBucket.RefreshGrainHash();
                }

                context.StockAllocations.Add(new StockAllocation
                {
                    AllocationCode = $"ALC-CD-{task.Id}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                    ReservationId = reservation.Id,
                    WarehouseId = task.WarehouseId,
                    ProductId = item.ProductId,
                    LocationId = stagingLoc.Id,
                    LotId = srcBucket.LotId,
                    SerialId = srcBucket.SerialId,
                    HandlingUnitId = srcBucket.HandlingUnitId,
                    OwnerId = srcBucket.OwnerId,
                    InventoryStatus = InventoryStatus.Available,
                    Quantity = chunk,
                    Status = StockAllocationStatus.Active,
                    Strategy = AllocationStrategy.LocationOrder,
                    SelectionReason = $"CROSS_DOCK_TASK_{task.Id}",
                    AllocatedAt = nowUtc,
                    AllocatedBy = actorId,
                    Version = 1
                });

                context.InventoryLocationMovements.Add(new InventoryLocationMovement
                {
                    WarehouseId = task.WarehouseId,
                    ProductId = item.ProductId,
                    InventoryStatus = InventoryStatus.Available,
                    LotId = srcBucket.LotId,
                    SerialId = srcBucket.SerialId,
                    FromLocationId = item.SourceLocationId,
                    ToLocationId = stagingLoc.Id,
                    BaseQuantity = chunk,
                    EnteredQuantity = chunk,
                    EnteredUnitCode = !string.IsNullOrWhiteSpace(item.BaseUnitCodeSnapshot) ? item.BaseUnitCodeSnapshot : "THÙNG",
                    ReferenceType = "CrossDock",
                    ReferenceId = task.Id,
                    PutawayTaskId = task.Id,
                    PutawayTaskItemId = item.Id,
                    ReceiptId = task.ReceiptId,
                    ReceiptLineId = item.ReceiptLineId,
                    CreatedBy = actorId
                });

                var noteText = $"CROSS-DOCK từ {sourceCode} thẳng sang bãi xuất {stagingLoc.Code}";
                context.InventoryTransactions.AddRange(
                    new InventoryTransaction
                    {
                        ProductId = item.ProductId,
                        WarehouseId = task.WarehouseId,
                        LocationId = item.SourceLocationId,
                        FromLocationId = item.SourceLocationId,
                        ToLocationId = stagingLoc.Id,
                        InventoryStatus = InventoryStatus.Available,
                        FromInventoryStatus = InventoryStatus.Available,
                        ToInventoryStatus = InventoryStatus.Available,
                        LotId = srcBucket.LotId,
                        SerialId = srcBucket.SerialId,
                        HandlingUnitId = srcBucket.HandlingUnitId,
                        OwnerId = srcBucket.OwnerId,
                        TransactionType = TransactionType.Move,
                        Quantity = -chunk,
                        UnitCost = srcBucket.MovingAverageCost,
                        ReferenceId = task.Id,
                        ReferenceType = "PutawayOut",
                        SourceDocumentLineId = item.ReceiptLineId,
                        TransactionDate = nowUtc,
                        ActualOccurredAtUtc = nowUtc,
                        PostedAtUtc = nowUtc,
                        CreatedBy = actorId,
                        Note = noteText
                    },
                    new InventoryTransaction
                    {
                        ProductId = item.ProductId,
                        WarehouseId = task.WarehouseId,
                        LocationId = stagingLoc.Id,
                        FromLocationId = item.SourceLocationId,
                        ToLocationId = stagingLoc.Id,
                        InventoryStatus = InventoryStatus.Available,
                        FromInventoryStatus = InventoryStatus.Available,
                        ToInventoryStatus = InventoryStatus.Available,
                        LotId = srcBucket.LotId,
                        SerialId = srcBucket.SerialId,
                        HandlingUnitId = srcBucket.HandlingUnitId,
                        OwnerId = srcBucket.OwnerId,
                        TransactionType = TransactionType.Move,
                        Quantity = chunk,
                        UnitCost = srcBucket.MovingAverageCost,
                        ReferenceId = task.Id,
                        ReferenceType = "PutawayIn",
                        SourceDocumentLineId = item.ReceiptLineId,
                        TransactionDate = nowUtc,
                        ActualOccurredAtUtc = nowUtc,
                        PostedAtUtc = nowUtc,
                        CreatedBy = actorId,
                        Note = noteText
                    });

                remToMove -= chunk;
            }

            if (matchedBackorder != null)
            {
                var maxRecoverable = Math.Max(0m, matchedBackorder.Quantity - matchedBackorder.CancelledQuantity);
                matchedBackorder.RecoveredQuantity = Math.Min(maxRecoverable, Math.Round(matchedBackorder.RecoveredQuantity + crossDockQty, 4));
                matchedBackorder.UpdatedAt = nowUtc;
                if (matchedBackorder.RecoveredQuantity + matchedBackorder.CancelledQuantity >= matchedBackorder.Quantity)
                {
                    matchedBackorder.Status = BackorderStatus.Fulfilled;
                    matchedBackorder.ResolvedAt = nowUtc;
                }
            }

            item.MovedBaseQuantity += crossDockQty;
            task.IsCrossDock = true;
            task.CrossDockAllocatedQty = Math.Round(task.CrossDockAllocatedQty + crossDockQty, 4);
            task.CrossDockTargetBackorderId = matchedBackorder?.Id ?? task.CrossDockTargetBackorderId;
            task.CrossDockTargetSalesOrderId = matchedSalesOrderId ?? task.CrossDockTargetSalesOrderId;
            task.CrossDockTargetExportReceiptId = matchedExportReceiptId ?? task.CrossDockTargetExportReceiptId;
            task.AssignedUserId ??= actorId;
            task.StartedAt ??= nowUtc;
            task.Status = task.Items.All(x => x.RemainingBaseQuantity == 0)
                ? PutawayTaskStatus.Completed
                : PutawayTaskStatus.InProgress;
            if (task.Status == PutawayTaskStatus.Completed)
                task.CompletedAt = nowUtc;

            context.AuditLogs.Add(Audit(
                actorId,
                "PutawayTask.CrossDockExecuted",
                task.Id,
                task.WarehouseId,
                $"CrossDockQty: {crossDockQty:0.####}; StagingBin: {stagingLoc.Code}; SalesOrderId: {task.CrossDockTargetSalesOrderId}; BackorderId: {task.CrossDockTargetBackorderId}"));

            await context.SaveChangesAsync(token);
            if (tx is not null) await tx.CommitAsync(token);
            return await GetAsync(id, token);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            if (tx is not null) await tx.RollbackAsync(token);
            throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.", ex);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(token);
            throw;
        }
    }

    public async Task<PutawayTaskDto> MoveAsync(int id, MovePutawayItemDto dto, CancellationToken token = default)
    {
        if(dto.Quantity<=0) throw new BusinessRuleException("Số lượng cất phải lớn hơn 0.");
        var ownTransaction = context.Database.CurrentTransaction is null;
        var isSqlServer = context.Database.IsRelational() && context.Database.IsSqlServer();
        var maxAttempts = ownTransaction && isSqlServer ? 4 : 1;
        for (var attempt = 1; ; attempt++)
        {
        await using var tx=ownTransaction?await context.Database.BeginTransactionAsync(token):null;
        try
        {
            if (isSqlServer)
            {
                var tenantId = context.CurrentTenantId;
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM dbo.PutawayTasks WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND Id = {id}; SELECT 1 FROM dbo.PutawayTaskItems WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND PutawayTaskId = {id};",
                    token);
            }
            var task=await Load(id,token);
            var isPutback = IsPutbackTask(task);
            if(task.Status is not PutawayTaskStatus.Assigned and not PutawayTaskStatus.InProgress && !(isPutback && task.Status == PutawayTaskStatus.Open))
                throw Conflict("Nhiệm vụ không ở trạng thái có thể cất hàng.");
            EnsureActor(task);
            if (task.Status == PutawayTaskStatus.InProgress)
            {
                if (!string.IsNullOrWhiteSpace(dto.RowVersion))
                {
                    try { _ = Convert.FromBase64String(dto.RowVersion); }
                    catch { throw new BusinessRuleException("Phiên bản dữ liệu không hợp lệ."); }
                }
                context.Entry(task).Property(x => x.RowVersion).OriginalValue = task.RowVersion;
            }
            else if (task.RowVersion.Length > 0 && !string.IsNullOrWhiteSpace(dto.RowVersion))
            {
                ApplyVersion(task.RowVersion,dto.RowVersion,context.Entry(task).Property(x=>x.RowVersion));
            }
            var item=(dto.ItemId > 0 ? task.Items.SingleOrDefault(x=>x.Id==dto.ItemId) : task.Items.FirstOrDefault())??throw new NotFoundException("Không tìm thấy dòng hàng.");
            await context.AcquireInventoryAllocationLocksAsync([(task.WarehouseId, item.ProductId)], token);
            WarehouseLocation? destination;
            if (dto.DestinationLocationId > 0)
            {
                destination = await LocationForMutation(dto.DestinationLocationId, token) ?? throw new NotFoundException("Không tìm thấy vị trí đích.");
                if (!string.IsNullOrWhiteSpace(dto.DestinationLocationBarcode) &&
                    !string.Equals(destination.Code, dto.DestinationLocationBarcode.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    throw ConflictWithCode("PUTAWAY_WRONG_BIN", $"Mã vị trí quét ({dto.DestinationLocationBarcode.Trim()}) không khớp với vị trí đích ({destination.Code}).");
                }
            }
            else if (!string.IsNullOrWhiteSpace(dto.DestinationLocationBarcode))
            {
                var scannedBinCode = dto.DestinationLocationBarcode.Trim().ToUpperInvariant();
                destination = await LocationByCodeForMutation(task.WarehouseId, scannedBinCode, token)
                              ?? throw ConflictWithCode("PUTAWAY_WRONG_BIN", $"Không tìm thấy vị trí kệ lưu trữ với mã quét '{scannedBinCode}'.");
            }
            else
            {
                throw new NotFoundException("Không tìm thấy vị trí đích.");
            }
            if(destination.WarehouseId!=task.WarehouseId) throw new NotFoundException("Không tìm thấy vị trí đích.");
            if(!destination.IsActive||destination.IsBlocked||destination.LocationType!=RequiredType(item.InventoryStatus)||(item.InventoryStatus==InventoryStatus.Available&&!destination.IsPickable&&destination.LocationType!=WarehouseLocationType.Storage)) throw new BusinessRuleException("Vị trí đích không phù hợp với hàng hóa này.");
            var baseQty=ToBase(dto.Quantity,dto.UnitCode,item); if(baseQty>item.RemainingBaseQuantity) throw new BusinessRuleException("Số lượng cất vượt quá số lượng còn lại.");
            var product=await ProductForCapacity(item.ProductId,token)??throw new NotFoundException("Không tìm thấy sản phẩm.");
            if (!string.IsNullOrWhiteSpace(dto.ProductBarcode))
            {
                var scannedProductCode = dto.ProductBarcode.Trim();
                var matchesSku = string.Equals(product.Code, scannedProductCode, StringComparison.OrdinalIgnoreCase);
                var matchesBarcode = matchesSku || await context.ProductBarcodes.AsNoTracking()
                    .AnyAsync(b => b.ProductId == item.ProductId && b.Value == scannedProductCode, token);
                if (!matchesBarcode)
                {
                    throw ConflictWithCode("PUTAWAY_WRONG_PRODUCT", $"Mã vạch sản phẩm quét ({scannedProductCode}) không khớp với SKU ({product.Code}).");
                }
            }
            await EnsurePutawayCapacityAsync(destination, product, baseQty, token);
            var sourceBuckets = await context.InventoryStocks
                .Include(x => x.Lot)
                .Include(x => x.Serial)
                .Where(x => x.ProductId == item.ProductId &&
                            x.WarehouseId == task.WarehouseId &&
                            x.Status == item.InventoryStatus &&
                            x.LocationId == item.SourceLocationId &&
                            (isPutback ? x.Quantity > 0 : x.Quantity > x.ReservedQuantity))
                .OrderBy(x => x.LotId)
                .ThenBy(x => x.SerialId)
                .ThenBy(x => x.Id)
                .ToListAsync(token);
            if (!isPutback && item.ReceiptLineId > 0)
            {
                var lineIdentities = await context.ImportReceiptInventoryIdentities.AsNoTracking()
                    .Where(x => x.ImportReceiptDetailId == item.ReceiptLineId)
                    .ToListAsync(token);
                var statusIdentities = lineIdentities.Where(x => x.TargetStatus == item.InventoryStatus).ToList();
                var identities = statusIdentities.Count > 0 ? statusIdentities : lineIdentities;
                if (identities.Count > 0)
                {
                    var allowedLots = identities
                        .Where(x => !string.IsNullOrWhiteSpace(x.LotNumber))
                        .Select(x => x.LotNumber!.Trim())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var allowedSerials = identities
                        .Where(x => !string.IsNullOrWhiteSpace(x.SerialNumber))
                        .Select(x => x.SerialNumber!.Trim())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var allowUntracked = identities.Any(x => string.IsNullOrWhiteSpace(x.LotNumber) && string.IsNullOrWhiteSpace(x.SerialNumber));

                    sourceBuckets = sourceBuckets.Where(b =>
                    {
                        var lotKey = b.Lot?.LotNumber ?? (b.LotId.HasValue ? b.LotId.Value.ToString() : null);
                        var serialKey = b.Serial?.SerialNumber ?? (b.SerialId.HasValue ? b.SerialId.Value.ToString() : null);
                        if (serialKey is not null)
                            return allowedSerials.Contains(serialKey) &&
                                   (lotKey is null || allowedLots.Count == 0 || allowedLots.Contains(lotKey));
                        if (lotKey is not null)
                            return allowedLots.Contains(lotKey);
                        return allowUntracked;
                    }).ToList();
                }
                else if (task.ReceiptId > 0)
                {
                    var receiptTxIdentities = await context.InventoryTransactions.AsNoTracking()
                        .Where(t => t.ReferenceType == "ImportReceipt" &&
                                    t.ReferenceId == task.ReceiptId &&
                                    t.ProductId == item.ProductId &&
                                    t.InventoryStatus == item.InventoryStatus &&
                                    (t.LotId.HasValue || t.SerialId.HasValue))
                        .Select(t => new { t.LotId, t.SerialId })
                        .ToListAsync(token);
                    if (receiptTxIdentities.Count > 0)
                    {
                        var allowedPairs = receiptTxIdentities
                            .Select(t => (t.LotId, t.SerialId))
                            .ToHashSet();
                        sourceBuckets = sourceBuckets
                            .Where(b => allowedPairs.Contains((b.LotId, b.SerialId)))
                            .ToList();
                    }
                }
            }
            if (sourceBuckets.Count == 0 || sourceBuckets.Sum(x => isPutback ? x.Quantity : x.Quantity - x.ReservedQuantity) < baseQty)
                throw Conflict("Tồn tại vị trí nguồn đã thay đổi. Vui lòng tải lại.");
            var nowUtc = DateTime.UtcNow;
            var remainingToMove = baseQty;
            var enteredUnitCode = string.IsNullOrWhiteSpace(dto.UnitCode) ? "THÙNG" : dto.UnitCode.Trim().ToUpperInvariant();
            foreach (var sourceBucket in sourceBuckets)
            {
                if (remainingToMove <= 0) break;
                var availableInBucket = isPutback
                    ? sourceBucket.Quantity
                    : sourceBucket.Quantity - sourceBucket.ReservedQuantity;
                if (availableInBucket <= 0) continue;
                var chunk = Math.Min(remainingToMove, availableInBucket);

                var sourceLocked = await context.InventoryLocks.EffectiveAt(nowUtc).AnyAsync(l =>
                    l.WarehouseId == task.WarehouseId &&
                    (!l.LocationId.HasValue || l.LocationId == item.SourceLocationId) &&
                    (!l.ProductId.HasValue || l.ProductId == item.ProductId) &&
                    (!l.InventoryStatus.HasValue || l.InventoryStatus == item.InventoryStatus) &&
                    (!l.LotId.HasValue || l.LotId == sourceBucket.LotId) &&
                    (!l.SerialId.HasValue || l.SerialId == sourceBucket.SerialId), token);
                var destinationLocked = await context.InventoryLocks.EffectiveAt(nowUtc).AnyAsync(l =>
                    l.WarehouseId == task.WarehouseId &&
                    (!l.LocationId.HasValue || l.LocationId == destination.Id) &&
                    (!l.ProductId.HasValue || l.ProductId == item.ProductId) &&
                    (!l.InventoryStatus.HasValue || l.InventoryStatus == item.InventoryStatus) &&
                    (!l.LotId.HasValue || l.LotId == sourceBucket.LotId) &&
                    (!l.SerialId.HasValue || l.SerialId == sourceBucket.SerialId), token);
                if (sourceLocked || destinationLocked)
                {
                    var locked = new BusinessRuleException("Inventory bucket đang bị khóa bởi Inventory Lock.");
                    locked.Data["HttpStatusCode"] = 409;
                    locked.Data["ErrorCode"] = "INV_STOCK_LOCKED";
                    throw locked;
                }

                sourceBucket.Quantity -= chunk;
                var reservedMoved = 0m;
                var allocatedMoved = 0m;
                if (isPutback)
                {
                    if (sourceBucket.ReservedQuantity > sourceBucket.Quantity)
                    {
                        reservedMoved = sourceBucket.ReservedQuantity - sourceBucket.Quantity;
                        sourceBucket.ReservedQuantity = sourceBucket.Quantity;
                    }
                    if (sourceBucket.AllocatedQuantity > sourceBucket.Quantity)
                    {
                        allocatedMoved = sourceBucket.AllocatedQuantity - sourceBucket.Quantity;
                        sourceBucket.AllocatedQuantity = sourceBucket.Quantity;
                    }
                }
                sourceBucket.LastUpdated = nowUtc;

                var dest = context.InventoryStocks.Local.FirstOrDefault(x =>
                               x.ProductId == item.ProductId &&
                               x.WarehouseId == task.WarehouseId &&
                               x.Status == item.InventoryStatus &&
                               x.LocationId == destination.Id &&
                               x.LotId == sourceBucket.LotId &&
                               x.SerialId == sourceBucket.SerialId &&
                               x.HandlingUnitId == sourceBucket.HandlingUnitId &&
                               x.OwnerId == sourceBucket.OwnerId)
                           ?? await context.InventoryStocks.FirstOrDefaultAsync(x =>
                               x.ProductId == item.ProductId &&
                               x.WarehouseId == task.WarehouseId &&
                               x.Status == item.InventoryStatus &&
                               x.LocationId == destination.Id &&
                               x.LotId == sourceBucket.LotId &&
                               x.SerialId == sourceBucket.SerialId &&
                               x.HandlingUnitId == sourceBucket.HandlingUnitId &&
                               x.OwnerId == sourceBucket.OwnerId, token);
                if (dest is null)
                {
                    context.InventoryStocks.Add(new InventoryStock
                    {
                        ProductId = item.ProductId,
                        WarehouseId = task.WarehouseId,
                        Status = item.InventoryStatus,
                        LocationId = destination.Id,
                        LotId = sourceBucket.LotId,
                        SerialId = sourceBucket.SerialId,
                        HandlingUnitId = sourceBucket.HandlingUnitId,
                        OwnerId = sourceBucket.OwnerId,
                        Quantity = chunk,
                        ReservedQuantity = reservedMoved,
                        AllocatedQuantity = allocatedMoved,
                        MovingAverageCost = sourceBucket.MovingAverageCost,
                        LastUpdated = nowUtc
                    });
                }
                else
                {
                    dest.Quantity += chunk;
                    dest.ReservedQuantity += reservedMoved;
                    dest.AllocatedQuantity += allocatedMoved;
                    dest.LastUpdated = nowUtc;
                }

                context.InventoryLocationMovements.Add(new InventoryLocationMovement
                {
                    WarehouseId = task.WarehouseId,
                    ProductId = item.ProductId,
                    InventoryStatus = item.InventoryStatus,
                    LotId = sourceBucket.LotId,
                    SerialId = sourceBucket.SerialId,
                    FromLocationId = item.SourceLocationId,
                    ToLocationId = destination.Id,
                    BaseQuantity = chunk,
                    EnteredQuantity = chunk == baseQty ? dto.Quantity : decimal.Round(dto.Quantity * chunk / baseQty, 4),
                    EnteredUnitCode = enteredUnitCode,
                    PutawayTaskId = task.Id,
                    PutawayTaskItemId = item.Id,
                    ReceiptId = task.ReceiptId,
                    ReceiptLineId = item.ReceiptLineId,
                    CreatedBy = currentUser.UserId
                });

                var noteText = $"Putaway từ {item.SourceLocation?.Code ?? "RECEIVING"} sang {destination.Code}";
                context.InventoryTransactions.AddRange(
                    new InventoryTransaction
                    {
                        ProductId = item.ProductId,
                        WarehouseId = task.WarehouseId,
                        LocationId = item.SourceLocationId,
                        FromLocationId = item.SourceLocationId,
                        ToLocationId = destination.Id,
                        InventoryStatus = item.InventoryStatus,
                        FromInventoryStatus = item.InventoryStatus,
                        ToInventoryStatus = item.InventoryStatus,
                        LotId = sourceBucket.LotId,
                        SerialId = sourceBucket.SerialId,
                        HandlingUnitId = sourceBucket.HandlingUnitId,
                        OwnerId = sourceBucket.OwnerId,
                        TransactionType = TransactionType.Move,
                        Quantity = -chunk,
                        UnitCost = sourceBucket.MovingAverageCost,
                        ReferenceId = task.Id,
                        ReferenceType = "PutawayTask",
                        SourceDocumentLineId = item.ReceiptLineId,
                        TransactionDate = nowUtc,
                        CreatedBy = currentUser.UserId,
                        Note = noteText
                    },
                    new InventoryTransaction
                    {
                        ProductId = item.ProductId,
                        WarehouseId = task.WarehouseId,
                        LocationId = destination.Id,
                        FromLocationId = item.SourceLocationId,
                        ToLocationId = destination.Id,
                        InventoryStatus = item.InventoryStatus,
                        FromInventoryStatus = item.InventoryStatus,
                        ToInventoryStatus = item.InventoryStatus,
                        LotId = sourceBucket.LotId,
                        SerialId = sourceBucket.SerialId,
                        HandlingUnitId = sourceBucket.HandlingUnitId,
                        OwnerId = sourceBucket.OwnerId,
                        TransactionType = TransactionType.Move,
                        Quantity = chunk,
                        UnitCost = sourceBucket.MovingAverageCost,
                        ReferenceId = task.Id,
                        ReferenceType = "PutawayTask",
                        SourceDocumentLineId = item.ReceiptLineId,
                        TransactionDate = nowUtc,
                        CreatedBy = currentUser.UserId,
                        Note = noteText
                    });

                remainingToMove -= chunk;
            }
            item.MovedBaseQuantity+=baseQty; task.Status=task.Items.All(x=>x.RemainingBaseQuantity==0)?PutawayTaskStatus.Completed:PutawayTaskStatus.InProgress; task.StartedAt??=nowUtc; if(task.Status==PutawayTaskStatus.Completed)task.CompletedAt=nowUtc;
            if (isPutback && task.Status == PutawayTaskStatus.Completed)
            {
                await CompletePutbackOrderCancellationAsync(task, baseQty, nowUtc, token);
            }
            context.AuditLogs.Add(Audit(currentUser.UserId,"PutawayTask.Moved",task.Id,task.WarehouseId,$"ItemId: {item.Id}; BaseQuantity: {baseQty}; DestinationId: {destination.Id}"));
            await context.SaveChangesAsync(token); if(tx is not null)await tx.CommitAsync(token); return await GetAsync(id,token);
        }
        catch (Exception ex) when (ownTransaction && attempt < maxAttempts && IsDeadlock(ex))
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            await Task.Delay(15 * attempt, token);
        }
        catch(DbUpdateConcurrencyException ex){if(tx is not null)await tx.RollbackAsync(token);throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.",ex);}
        catch(Exception ex) when (IsDeadlock(ex)){if(tx is not null)await tx.RollbackAsync(token);throw new DeadlockException("Giao dịch Putaway bị deadlock.",ex);}
        catch{if(tx is not null)await tx.RollbackAsync(token);throw;}
        }
    }

    private static bool IsDeadlock(Exception? ex)
    {
        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is DeadlockException or Microsoft.Data.SqlClient.SqlException { Number: 1205 })
                return true;
        }
        return false;
    }

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
    private static bool IsPutbackTask(PutawayTask task) =>
        !string.IsNullOrWhiteSpace(task.TaskType) &&
        task.TaskType.Contains("Putback", StringComparison.OrdinalIgnoreCase);

    private async Task CompletePutbackOrderCancellationAsync(
        PutawayTask completedTask,
        decimal movedBaseQty,
        DateTime nowUtc,
        CancellationToken token)
    {
        var taskProductIds = completedTask.Items.Select(i => i.ProductId).Distinct().ToList();
        var otherPendingPutbackTasks = await context.PutawayTasks.AsNoTracking()
            .Where(t => t.Id != completedTask.Id &&
                        t.WarehouseId == completedTask.WarehouseId &&
                        t.TaskType != null &&
                        t.TaskType.Contains("Putback") &&
                        t.Status != PutawayTaskStatus.Completed &&
                        t.Status != PutawayTaskStatus.Cancelled &&
                        (completedTask.ReceiptId <= 0 || t.ReceiptId == completedTask.ReceiptId))
            .AnyAsync(token);
        if (otherPendingPutbackTasks) return;

        var exportReceipts = await context.ExportReceipts
            .Include(e => e.Details)
            .Include(e => e.SalesOrder)
            .Where(e => e.WarehouseId == completedTask.WarehouseId &&
                        (e.Status == ReceiptStatus.CancelPendingPutback ||
                         (completedTask.ReceiptId > 0 && (e.Id == completedTask.ReceiptId || e.SalesOrderId == completedTask.ReceiptId))))
            .ToListAsync(token);

        var matchingExportReceipts = exportReceipts
            .Where(e => (completedTask.ReceiptId > 0 && (e.Id == completedTask.ReceiptId || e.SalesOrderId == completedTask.ReceiptId)) ||
                        e.Status == ReceiptStatus.CancelPendingPutback &&
                        (e.Details.Count == 0 || e.Details.Any(d => taskProductIds.Contains(d.ProductId))))
            .ToList();

        var salesOrderIds = matchingExportReceipts
            .Where(e => e.SalesOrderId.HasValue)
            .Select(e => e.SalesOrderId!.Value)
            .ToHashSet();
        if (completedTask.ReceiptId > 0) salesOrderIds.Add(completedTask.ReceiptId);

        var marketplaceOrderIds = matchingExportReceipts
            .Where(e => !string.IsNullOrWhiteSpace(e.MarketplaceOrderId))
            .Select(e => e.MarketplaceOrderId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var salesOrders = await context.SalesOrders
            .Include(o => o.Lines)
            .Where(o => o.Status == SalesOrderStatus.CancelPendingPutback ||
                        salesOrderIds.Contains(o.Id) ||
                        (!string.IsNullOrEmpty(o.ExternalOrderId) && marketplaceOrderIds.Contains(o.ExternalOrderId)) ||
                        (!string.IsNullOrEmpty(o.OrderCode) && marketplaceOrderIds.Contains(o.OrderCode)))
            .ToListAsync(token);

        var matchingSalesOrders = salesOrders
            .Where(o => salesOrderIds.Contains(o.Id) ||
                        (!string.IsNullOrEmpty(o.ExternalOrderId) && marketplaceOrderIds.Contains(o.ExternalOrderId)) ||
                        (!string.IsNullOrEmpty(o.OrderCode) && marketplaceOrderIds.Contains(o.OrderCode)) ||
                        (o.Status == SalesOrderStatus.CancelPendingPutback &&
                         (o.Lines.Count == 0 || o.Lines.Any(l => taskProductIds.Contains(l.ProductId)))))
            .ToList();

        foreach (var so in matchingSalesOrders)
        {
            if (so.Status == SalesOrderStatus.CancelPendingPutback)
            {
                so.Status = SalesOrderStatus.Cancelled;
            }
            salesOrderIds.Add(so.Id);
            if (!string.IsNullOrWhiteSpace(so.ExternalOrderId))
            {
                marketplaceOrderIds.Add(so.ExternalOrderId);
            }
            if (!string.IsNullOrWhiteSpace(so.OrderCode))
            {
                marketplaceOrderIds.Add(so.OrderCode);
            }
        }

        if (matchingExportReceipts.Count == 0 && (salesOrderIds.Count > 0 || marketplaceOrderIds.Count > 0))
        {
            var linkedReceipts = exportReceipts
                .Where(e => (e.SalesOrderId.HasValue && salesOrderIds.Contains(e.SalesOrderId.Value)) ||
                            (!string.IsNullOrWhiteSpace(e.MarketplaceOrderId) && marketplaceOrderIds.Contains(e.MarketplaceOrderId!)))
                .ToList();
            matchingExportReceipts.AddRange(linkedReceipts);
        }

        var exportReceiptIds = new HashSet<int>();
        foreach (var er in matchingExportReceipts)
        {
            if (er.Status is ReceiptStatus.CancelPendingPutback or ReceiptStatus.Approved or ReceiptStatus.Draft)
            {
                er.Status = ReceiptStatus.Cancelled;
            }
            exportReceiptIds.Add(er.Id);
        }

        var activeReservationStatuses = new[]
        {
            StockReservationStatus.Active,
            StockReservationStatus.PartiallyConsumed,
            StockReservationStatus.PartiallyAllocated,
            StockReservationStatus.Allocated
        };

        var reservations = await context.StockReservations
            .Where(r => r.WarehouseId == completedTask.WarehouseId &&
                        activeReservationStatuses.Contains(r.Status) &&
                        ((r.SourceType == "ExportReceipt" && r.SourceId.HasValue && exportReceiptIds.Contains(r.SourceId.Value)) ||
                         (r.SourceType == "SalesOrder" && r.SourceId.HasValue && salesOrderIds.Contains(r.SourceId.Value)) ||
                         taskProductIds.Contains(r.ProductId)))
            .ToListAsync(token);

        var matchingReservations = reservations
            .Where(r => (r.SourceType == "ExportReceipt" && r.SourceId.HasValue && exportReceiptIds.Contains(r.SourceId.Value)) ||
                        (r.SourceType == "SalesOrder" && r.SourceId.HasValue && salesOrderIds.Contains(r.SourceId.Value)))
            .ToList();

        if (matchingReservations.Count == 0)
        {
            matchingReservations = reservations
                .Where(r => taskProductIds.Contains(r.ProductId))
                .ToList();
        }

        var reservationIds = matchingReservations.Select(r => r.Id).ToHashSet();
        var activeAllocations = reservationIds.Count > 0
            ? await context.StockAllocations
                .Where(a => reservationIds.Contains(a.ReservationId) &&
                            (a.Status == StockAllocationStatus.Active ||
                             a.Status == StockAllocationStatus.Picking ||
                             a.Status == StockAllocationStatus.Picked))
                .ToListAsync(token)
            : [];

        foreach (var alloc in activeAllocations)
        {
            alloc.Status = StockAllocationStatus.Released;
            alloc.ReleasedAt = nowUtc;
            alloc.ReleasedBy = currentUser.UserId;
            alloc.ReleaseReason = "Putback completed";
        }

        var releasedByProduct = new Dictionary<int, decimal>();
        foreach (var rsv in matchingReservations)
        {
            var unreleased = Math.Max(0m, rsv.Quantity - rsv.ConsumedQuantity - rsv.ReleasedQuantity);
            rsv.ReleasedQuantity += unreleased;
            rsv.AllocatedQuantity = 0m;
            rsv.Status = StockReservationStatus.Released;
            rsv.ReleasedAt = nowUtc;
            rsv.ReleasedBy = currentUser.UserId;
            rsv.ReleaseReason = "Putback completed";
            releasedByProduct[rsv.ProductId] = releasedByProduct.GetValueOrDefault(rsv.ProductId) + unreleased;
        }

        foreach (var prodId in taskProductIds)
        {
            if (!releasedByProduct.ContainsKey(prodId))
            {
                var taskProdQty = completedTask.Items
                    .Where(i => i.ProductId == prodId)
                    .Sum(i => i.RequiredBaseQuantity > 0 ? i.RequiredBaseQuantity : movedBaseQty);
                releasedByProduct[prodId] = taskProdQty;
            }
        }

        var warehouseStocks = await context.InventoryStocks
            .Where(s => s.WarehouseId == completedTask.WarehouseId && taskProductIds.Contains(s.ProductId))
            .OrderByDescending(s => s.ReservedQuantity)
            .ThenBy(s => s.Id)
            .ToListAsync(token);

        foreach (var (prodId, qtyToRelease) in releasedByProduct)
        {
            var remReserve = qtyToRelease;
            var remAlloc = qtyToRelease;
            foreach (var st in warehouseStocks.Where(s => s.ProductId == prodId))
            {
                if (remReserve > 0m && st.ReservedQuantity > 0m)
                {
                    var dec = Math.Min(remReserve, st.ReservedQuantity);
                    st.ReservedQuantity -= dec;
                    remReserve -= dec;
                    st.LastUpdated = nowUtc;
                }
                if (remAlloc > 0m && st.AllocatedQuantity > 0m)
                {
                    var decAlloc = Math.Min(remAlloc, st.AllocatedQuantity);
                    st.AllocatedQuantity -= decAlloc;
                    remAlloc -= decAlloc;
                    st.LastUpdated = nowUtc;
                }
            }
        }
    }
    // Hold destination eligibility through commit, including a concurrent master deactivation.
    private Task<WarehouseLocation?> LocationForMutation(int id, CancellationToken token) =>
        (context.Database.IsSqlServer()
            ? context.WarehouseLocations.FromSqlInterpolated($"SELECT * FROM dbo.WarehouseLocations WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = {context.CurrentTenantId} AND Id = {id}")
            : context.WarehouseLocations.Where(x => x.Id == id)).SingleOrDefaultAsync(token);
    private Task<WarehouseLocation?> LocationByCodeForMutation(int warehouseId, string code, CancellationToken token) =>
        (context.Database.IsSqlServer()
            ? context.WarehouseLocations.FromSqlInterpolated($"SELECT * FROM dbo.WarehouseLocations WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = {context.CurrentTenantId} AND WarehouseId = {warehouseId} AND Code = {code}")
            : context.WarehouseLocations.Where(x => x.WarehouseId == warehouseId && x.Code == code)).SingleOrDefaultAsync(token);
    private Task<Product?> ProductForCapacity(int id, CancellationToken token) =>
        (context.Database.IsSqlServer()
            ? context.Products.FromSqlInterpolated($"SELECT * FROM dbo.Products WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = {context.CurrentTenantId} AND Id = {id}").AsNoTracking()
            : context.Products.AsNoTracking().Where(x => x.Id == id)).SingleOrDefaultAsync(token);
    private IQueryable<PutawayTask> Query()=>context.PutawayTasks.Include(x=>x.Receipt).Include(x=>x.Warehouse).Include(x=>x.Items).ThenInclude(x=>x.ReceiptLine).ThenInclude(x=>x.Product).ThenInclude(x=>x.Barcodes).Include(x=>x.Items).ThenInclude(x=>x.ReceiptLine).ThenInclude(x=>x.Product).ThenInclude(x=>x.Unit).Include(x=>x.Items).ThenInclude(x=>x.SourceLocation);
    private void EnsureActor(PutawayTask task){if(IsPutbackTask(task)&&(!task.AssignedUserId.HasValue||task.AssignedUserId.Value<=0))return;if(task.AssignedUserId!=currentUser.UserId&&!currentUser.IsGlobalAdmin&&currentUser.Role is not "Admin" and not "Manager")throw new ForbiddenException("Bạn không có quyền thực hiện thao tác này.");}
    private static decimal ToBase(decimal qty, string unit, PutawayTaskItem item)
    {
        if (string.IsNullOrWhiteSpace(unit) || string.IsNullOrWhiteSpace(item.BaseUnitCodeSnapshot))
            return qty;
        var code = unit.Trim().ToUpperInvariant();
        var baseCode = item.BaseUnitCodeSnapshot.Trim().ToUpperInvariant();
        var opCode = (item.OperationUnitCodeSnapshot ?? string.Empty).Trim().ToUpperInvariant();
        var prodUnitCode = item.ReceiptLine?.Product?.Unit?.Code?.Trim().ToUpperInvariant();
        var factor = item.ConversionFactorSnapshot > 0 ? item.ConversionFactorSnapshot : 1m;
        var result = (code == baseCode || (code == prodUnitCode && factor == 1m))
            ? qty
            : (code == opCode || code == prodUnitCode)
                ? qty * factor
                : throw new BusinessRuleException("Đơn vị tính không được hỗ trợ.");
        if (decimal.Round(result, item.BaseUnitDecimalPlaces) != result)
            throw new BusinessRuleException("Số lượng vượt quá độ chính xác cho phép; hệ thống không tự làm tròn.");
        return result;
    }
    private static WarehouseLocationType RequiredType(InventoryStatus s)=>s switch{InventoryStatus.Available=>WarehouseLocationType.Storage,InventoryStatus.Damaged=>WarehouseLocationType.Damaged,InventoryStatus.Rejected=>WarehouseLocationType.Rejected,_=>throw new BusinessRuleException("Trạng thái tồn kho không thuộc luồng cất hàng tự động.")};
    private static void ApplyVersion(byte[] actual,string encoded,Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry<PutawayTask,byte[]> p){byte[] expected;try{expected=Convert.FromBase64String(encoded);}catch{throw new BusinessRuleException("Phiên bản dữ liệu không hợp lệ.");}if(!actual.SequenceEqual(expected))throw Conflict("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");p.OriginalValue=expected;}
    private async Task<PutawayTaskDto> MapAsync(PutawayTask t,CancellationToken token)
    {
        var moves=await context.InventoryLocationMovements.AsNoTracking().Where(x=>x.PutawayTaskId==t.Id&&x.PutawayTaskItemId.HasValue).Join(context.WarehouseLocations,x=>x.FromLocationId,x=>x.Id,(m,f)=>new{m,f}).Join(context.WarehouseLocations,x=>x.m.ToLocationId,x=>x.Id,(x,d)=>new PutawayMovementDto{Id=x.m.Id,ItemId=x.m.PutawayTaskItemId.GetValueOrDefault(),FromLocationId=x.f.Id,FromLocationCode=x.f.Code.Trim(),ToLocationId=d.Id,ToLocationCode=d.Code.Trim(),BaseQuantity=x.m.BaseQuantity,CreatedAt=x.m.CreatedAt}).ToListAsync(token);
        var activeLocations=await context.WarehouseLocations.AsNoTracking().Where(x=>x.WarehouseId==t.WarehouseId&&x.IsActive&&!x.IsBlocked).OrderBy(x=>x.Code).ToListAsync(token);
        var canMutate=currentUser.Role is "Admin" or "Manager" or "WarehouseStaff";
        return new PutawayTaskDto
        {
            Id=t.Id,ReceiptId=t.ReceiptId,ReceiptCode=(t.Receipt?.Code??string.Empty).Trim(),WarehouseId=t.WarehouseId,WarehouseCode=(t.Warehouse?.Code??string.Empty).Trim(),WarehouseName=(t.Warehouse?.Name??string.Empty).Trim(),Status=t.Status.ToString(),AssignedUserId=t.AssignedUserId,RequiredBaseQuantity=t.Items.Sum(x=>x.RequiredBaseQuantity),MovedBaseQuantity=t.Items.Sum(x=>x.MovedBaseQuantity),IsCrossDock=t.IsCrossDock,CrossDockTargetSalesOrderId=t.CrossDockTargetSalesOrderId,CrossDockTargetExportReceiptId=t.CrossDockTargetExportReceiptId,CrossDockTargetBackorderId=t.CrossDockTargetBackorderId,CrossDockAllocatedQty=t.CrossDockAllocatedQty,CreatedAt=t.CreatedAt,RowVersion=canMutate&&t.RowVersion is { Length: > 0 }?Convert.ToBase64String(t.RowVersion):null,ExceptionReason=canMutate?t.ExceptionReason:null,
            Items=t.Items.Select(x=>
            {
                var reqType=RequiredType(x.InventoryStatus);
                var prodStorage=x.ReceiptLine?.Product?.StorageClass;
                var recLoc=activeLocations.FirstOrDefault(l=>l.LocationType==reqType&&(x.InventoryStatus!=InventoryStatus.Available||l.IsPickable)&&(l.StorageClass==null||(prodStorage!=null&&l.StorageClass==prodStorage)));
                var lastMove=moves.Where(m=>m.ItemId==x.Id).OrderByDescending(m=>m.Id).FirstOrDefault();
                var fallbackUom=(x.ReceiptLine?.Product?.Unit?.Code??string.Empty).Trim();
                if(fallbackUom is "QA_UNIT" or "QA-PCS") fallbackUom="THÙNG";
                var baseUom=!string.IsNullOrWhiteSpace(x.BaseUnitCodeSnapshot)?x.BaseUnitCodeSnapshot.Trim():fallbackUom;
                if(baseUom is "" or "QA_UNIT" or "QA-PCS") baseUom=!string.IsNullOrWhiteSpace(fallbackUom)?fallbackUom:"THÙNG";
                var opUom=!string.IsNullOrWhiteSpace(x.OperationUnitCodeSnapshot)?x.OperationUnitCodeSnapshot.Trim():baseUom;
                if(opUom is "" or "QA_UNIT" or "QA-PCS") opUom=baseUom;
                if((x.OperationUnitId==x.BaseUnitId&&x.ConversionFactorSnapshot<=1m)||string.Equals(opUom,baseUom,StringComparison.OrdinalIgnoreCase)) opUom=baseUom;
                return new PutawayTaskItemDto
                {
                    Id=x.Id,ReceiptLineId=x.ReceiptLineId,ProductId=x.ProductId,ProductCode=(x.ReceiptLine?.Product?.Code??string.Empty).Trim(),ProductName=(x.ReceiptLine?.Product?.Name??string.Empty).Trim(),ProductBarcodes=x.ReceiptLine?.Product?.Barcodes.Select(b=>b.Value.Trim()).ToList()??[],InventoryStatus=x.InventoryStatus.ToString(),SourceLocationId=x.SourceLocationId,SourceLocationCode=(x.SourceLocation?.Code??string.Empty).Trim(),
                    RecommendedLocationId=recLoc?.Id,RecommendedLocationCode=recLoc?.Code.Trim(),
                    ConfirmedLocationId=lastMove?.ToLocationId,ConfirmedLocationCode=lastMove?.ToLocationCode.Trim(),
                    OperationUnitCode=opUom,
                    BaseUnitCode=baseUom,
                    RequiredOperationQuantity=x.RequiredOperationQuantity,RequiredBaseQuantity=x.RequiredBaseQuantity,MovedBaseQuantity=x.MovedBaseQuantity,RemainingBaseQuantity=x.RemainingBaseQuantity,RowVersion=canMutate&&x.RowVersion is { Length: > 0 }?Convert.ToBase64String(x.RowVersion):null
                };
            }).ToList(),
            Movements=moves
        };
    }
    private static AuditLog Audit(int user,string action,int entity,int warehouse,string values)=>new(){UserId=user,Action=action,EntityName="PutawayTask",EntityId=entity,WarehouseId=warehouse,NewValues=values,Result="Success",Timestamp=DateTime.UtcNow};
    private static BusinessRuleException Conflict(string message){var e=new BusinessRuleException(message);e.Data["HttpStatusCode"]=409;return e;}
    private static BusinessRuleException ConflictWithCode(string code,string message){var e=new BusinessRuleException(message);e.Data["HttpStatusCode"]=409;e.Data["ErrorCode"]=code;return e;}
    private WarehouseLocationDto Location(WarehouseLocation x)=>new(){Id=x.Id,WarehouseId=x.WarehouseId,Code=x.Code.Trim(),Name=x.Name.Trim(),StructurePath=x.StructurePath?.Trim(),StorageClass=x.StorageClass?.Trim(),MaxWeightKg=x.MaxWeightKg,MaxVolumeM3=x.MaxVolumeM3,MaxPalletEquivalent=x.MaxPalletEquivalent,MapX=x.MapX,MapY=x.MapY,MapWidth=x.MapWidth,MapHeight=x.MapHeight,LocationType=x.LocationType.ToString(),IsActive=x.IsActive,IsBlocked=x.IsBlocked,IsPickable=x.IsPickable,IsReceivable=x.IsReceivable,IsSystemManaged=x.IsSystemManaged,RowVersion=currentUser.Role is "Admin" or "Manager" or "WarehouseStaff"?Convert.ToBase64String(x.RowVersion):null};
    private static decimal? CapacityUtilizationPercent(WarehouseLocationCapacityDto? capacity)
    {
        if (capacity is null) return null;
        var ratios = new List<decimal>();
        if (capacity.MaxWeightKg.HasValue && capacity.UsedWeightKg.HasValue && capacity.MaxWeightKg > 0) ratios.Add(capacity.UsedWeightKg.Value / capacity.MaxWeightKg.Value * 100m);
        if (capacity.MaxVolumeM3.HasValue && capacity.UsedVolumeM3.HasValue && capacity.MaxVolumeM3 > 0) ratios.Add(capacity.UsedVolumeM3.Value / capacity.MaxVolumeM3.Value * 100m);
        if (capacity.MaxPalletEquivalent.HasValue && capacity.UsedPalletEquivalent.HasValue && capacity.MaxPalletEquivalent > 0) ratios.Add(capacity.UsedPalletEquivalent.Value / capacity.MaxPalletEquivalent.Value * 100m);
        return ratios.Count == 0 ? null : decimal.Round(ratios.Max(), 2);
    }
    private static void ValidateMapLayout(decimal? x, decimal? y, decimal? width, decimal? height)
    {
        var values = new[] { x, y, width, height };
        var populated = values.Count(value => value.HasValue);
        if (populated == 0) return;
        if (populated != 4) throw new BusinessRuleException("Layout phải có đủ X, Y, Width và Height hoặc để trống toàn bộ.");
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || x > 100 || y > 100 || width > 100 || height > 100 || x + width > 100 || y + height > 100)
            throw new BusinessRuleException("Layout phải nằm trong canvas 0–100% và không vượt biên.");
    }
    private static string Required(string value,string label)=>string.IsNullOrWhiteSpace(value)?throw new BusinessRuleException($"{label} là bắt buộc."):value.Trim();
    private static string? NormalizeStructurePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToUpperInvariant()).ToArray();
        if (parts.Length != 5) throw new BusinessRuleException("Đường dẫn cấu trúc phải gồm đúng 5 cấp: Zone/Aisle/Rack/Level/Bin.");
        foreach (var part in parts)
        {
            if (part.Length is 0 or > 32 || part.Any(ch => !((ch >= 'A' && ch <= 'Z') || char.IsDigit(ch) || ch is '-' or '_')))
                throw new BusinessRuleException("Mỗi cấp cấu trúc chỉ được dùng chữ A-Z, số, dấu gạch ngang hoặc gạch dưới.");
        }
        var normalized = string.Join("/", parts);
        if (normalized.Length > 160) throw new BusinessRuleException("Đường dẫn cấu trúc vượt quá độ dài cho phép.");
        return normalized;
    }
    private static string? NormalizeStorageClass(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized=value.Trim().ToUpperInvariant();
        if (normalized.Length>32 || normalized.Any(ch=>!((ch>='A'&&ch<='Z')||char.IsDigit(ch)||ch is '-' or '_')))
            throw new BusinessRuleException("Storage Class chỉ được dùng chữ A-Z, số, dấu gạch ngang hoặc gạch dưới.");
        return normalized;
    }
    private static void ValidateCapacityLimits(decimal? weight,decimal? volume,decimal? pallet)
    {
        if(weight.HasValue&&weight<=0)throw new BusinessRuleException("Giới hạn trọng lượng phải lớn hơn 0.");
        if(volume.HasValue&&volume<=0)throw new BusinessRuleException("Giới hạn thể tích phải lớn hơn 0.");
        if(pallet.HasValue&&pallet<=0)throw new BusinessRuleException("Giới hạn pallet-equivalent phải lớn hơn 0.");
    }
    private async Task EnsureExistingStockFitsConstraintsAsync(int locationId,string? storageClass,decimal? maxWeight,decimal? maxVolume,decimal? maxPallet,CancellationToken token)
    {
        var rows=await context.InventoryStocks.AsNoTracking().Where(x=>x.LocationId==locationId&&x.Quantity>0)
            .Join(context.Products.AsNoTracking(),s=>s.ProductId,p=>p.Id,(s,p)=>new{s.Quantity,p.Code,p.StorageClass,p.UnitWeightKg,p.UnitVolumeM3,p.UnitPalletEquivalent})
            .ToListAsync(token);
        if(storageClass is not null&&rows.Any(x=>!string.Equals(x.StorageClass,storageClass,StringComparison.Ordinal)))
            throw Conflict("Không thể áp Storage Class vì vị trí đang chứa sản phẩm không tương thích.");
        if(maxWeight.HasValue)
        {
            if(rows.Any(x=>!x.UnitWeightKg.HasValue))throw Conflict("Không thể áp giới hạn trọng lượng vì có sản phẩm trong vị trí chưa có UnitWeightKg.");
            if(rows.Sum(x=>x.Quantity*x.UnitWeightKg!.Value)>maxWeight.Value)throw Conflict("Giới hạn trọng lượng mới thấp hơn tải hiện tại của vị trí.");
        }
        if(maxVolume.HasValue)
        {
            if(rows.Any(x=>!x.UnitVolumeM3.HasValue))throw Conflict("Không thể áp giới hạn thể tích vì có sản phẩm trong vị trí chưa có UnitVolumeM3.");
            if(rows.Sum(x=>x.Quantity*x.UnitVolumeM3!.Value)>maxVolume.Value)throw Conflict("Giới hạn thể tích mới thấp hơn tải hiện tại của vị trí.");
        }
        if(maxPallet.HasValue)
        {
            if(rows.Any(x=>!x.UnitPalletEquivalent.HasValue))throw Conflict("Không thể áp giới hạn pallet-equivalent vì có sản phẩm trong vị trí chưa có UnitPalletEquivalent.");
            if(rows.Sum(x=>x.Quantity*x.UnitPalletEquivalent!.Value)>maxPallet.Value)throw Conflict("Giới hạn pallet-equivalent mới thấp hơn tải hiện tại của vị trí.");
        }
    }
    private async Task EnsurePutawayCapacityAsync(WarehouseLocation location,Product product,decimal incomingBaseQty,CancellationToken token)
    {
        if(location.StorageClass is not null&&!string.Equals(location.StorageClass,product.StorageClass,StringComparison.Ordinal))
            throw new BusinessRuleException($"Storage Class không tương thích. Vị trí yêu cầu {location.StorageClass}, sản phẩm là {product.StorageClass ?? "CHƯA CẤU HÌNH"}.");
        var rows=await context.InventoryStocks.AsNoTracking().Where(x=>x.LocationId==location.Id&&x.Quantity>0)
            .Join(context.Products.AsNoTracking(),s=>s.ProductId,p=>p.Id,(s,p)=>new{s.Quantity,p.Code,p.StorageClass,p.UnitWeightKg,p.UnitVolumeM3,p.UnitPalletEquivalent})
            .ToListAsync(token);
        if(location.StorageClass is not null&&rows.Any(x=>!string.Equals(x.StorageClass,location.StorageClass,StringComparison.Ordinal)))
            throw Conflict("Vị trí đang có tồn kho không tương thích Storage Class; cần xử lý master data trước khi tiếp tục.");
        if(location.MaxWeightKg.HasValue)
        {
            if(!product.UnitWeightKg.HasValue)throw new BusinessRuleException("Sản phẩm chưa có UnitWeightKg nên không thể xác minh sức chứa.");
            if(rows.Any(x=>!x.UnitWeightKg.HasValue))throw Conflict("Vị trí có tồn kho thiếu UnitWeightKg nên không thể xác minh sức chứa.");
            var projected=rows.Sum(x=>x.Quantity*x.UnitWeightKg!.Value)+incomingBaseQty*product.UnitWeightKg.Value;
            if(projected>location.MaxWeightKg.Value)throw new BusinessRuleException($"Vượt giới hạn trọng lượng vị trí: {projected:0.######}/{location.MaxWeightKg.Value:0.######} kg.");
        }
        if(location.MaxVolumeM3.HasValue)
        {
            if(!product.UnitVolumeM3.HasValue)throw new BusinessRuleException("Sản phẩm chưa có UnitVolumeM3 nên không thể xác minh sức chứa.");
            if(rows.Any(x=>!x.UnitVolumeM3.HasValue))throw Conflict("Vị trí có tồn kho thiếu UnitVolumeM3 nên không thể xác minh sức chứa.");
            var projected=rows.Sum(x=>x.Quantity*x.UnitVolumeM3!.Value)+incomingBaseQty*product.UnitVolumeM3.Value;
            if(projected>location.MaxVolumeM3.Value)throw new BusinessRuleException($"Vượt giới hạn thể tích vị trí: {projected:0.########}/{location.MaxVolumeM3.Value:0.########} m³.");
        }
        if(location.MaxPalletEquivalent.HasValue)
        {
            if(!product.UnitPalletEquivalent.HasValue)throw new BusinessRuleException("Sản phẩm chưa có UnitPalletEquivalent nên không thể xác minh sức chứa.");
            if(rows.Any(x=>!x.UnitPalletEquivalent.HasValue))throw Conflict("Vị trí có tồn kho thiếu UnitPalletEquivalent nên không thể xác minh sức chứa.");
            var projected=rows.Sum(x=>x.Quantity*x.UnitPalletEquivalent!.Value)+incomingBaseQty*product.UnitPalletEquivalent.Value;
            if(projected>location.MaxPalletEquivalent.Value)throw new BusinessRuleException($"Vượt giới hạn pallet-equivalent vị trí: {projected:0.########}/{location.MaxPalletEquivalent.Value:0.########}.");
        }
    }
    private static void ValidateFlags(WarehouseLocationType type,bool pickable,bool receivable){if(type==WarehouseLocationType.Storage&&!pickable)throw new BusinessRuleException("Vị trí lưu trữ phải cho phép lấy hàng.");if((type is WarehouseLocationType.Damaged or WarehouseLocationType.Rejected)&&pickable)throw new BusinessRuleException("Vị trí hư hỏng hoặc từ chối không được cho phép lấy hàng.");if(receivable)throw new BusinessRuleException("Chỉ vị trí nhận hàng do hệ thống quản lý được phép nhận hàng.");}
    private void ApplyLocationVersion(WarehouseLocation x,string encoded){byte[] expected;try{expected=Convert.FromBase64String(encoded);}catch{throw new BusinessRuleException("Phiên bản dữ liệu không hợp lệ.");}if(!x.RowVersion.SequenceEqual(expected))throw Conflict("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");context.Entry(x).Property(y=>y.RowVersion).OriginalValue=expected;}
}
