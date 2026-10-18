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
    public async Task<IReadOnlyList<WarehouseDto>> ListLocationWarehousesAsync(CancellationToken token = default)
    {
        var ids = await warehouses.GetAccessibleWarehouseIdsAsync(token);
        return await context.Warehouses.AsNoTracking()
            .Where(x => ids.Contains(x.Id) && x.IsActive)
            .OrderBy(x => x.Code)
            .Select(x => new WarehouseDto { Id=x.Id, Code=x.Code, Name=x.Name, Address=x.Address, IsActive=x.IsActive, CreatedAt=x.CreatedAt })
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
                && rows.Any(x => !string.Equals(x.StorageClass, location.StorageClass, StringComparison.Ordinal));
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
                Code = location.Code,
                Name = location.Name,
                StructurePath = location.StructurePath,
                StorageClass = location.StorageClass,
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
                    Code = location.Code,
                    Name = location.Name,
                    StructurePath = location.StructurePath,
                    StorageClass = location.StorageClass,
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
        context.WarehouseLocations.Add(entity); await context.SaveChangesAsync(token); return Location(entity);
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
                || await context.PutawayTaskItems.AnyAsync(x => x.SourceLocationId == id && x.PutawayTask.Status != PutawayTaskStatus.Completed && x.PutawayTask.Status != PutawayTaskStatus.Cancelled, token);
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
            await context.PutawayTaskItems.AnyAsync(x => x.SourceLocationId == id && x.PutawayTask.Status != PutawayTaskStatus.Completed && x.PutawayTask.Status != PutawayTaskStatus.Cancelled, token)))
            throw Conflict("Không thể ngừng hoạt động vị trí đang có tồn kho, giữ hàng hoặc nhiệm vụ chưa hoàn tất.");
        if ((dto.IsBlocked || !dto.IsPickable) && (await context.InventoryStocks.AnyAsync(x => x.LocationId == id && x.ReservedQuantity > 0, token) ||
            await context.StockAllocations.AnyAsync(x => x.LocationId == id && (x.Status == StockAllocationStatus.Active || x.Status == StockAllocationStatus.Picking || x.Status == StockAllocationStatus.Picked), token)))
            throw Conflict("Không thể khóa hoặc tắt quyền lấy hàng của vị trí đang có cam kết giữ hàng hoặc phân bổ đang mở.");
        entity.Name=Required(dto.Name,"Tên vị trí"); entity.IsActive=dto.IsActive; entity.IsBlocked=dto.IsBlocked; entity.IsPickable=dto.IsPickable; entity.IsReceivable=dto.IsReceivable; entity.UpdatedAt=DateTime.UtcNow; entity.UpdatedBy=currentUser.UserId;
        try { await context.SaveChangesAsync(token); } catch (DbUpdateConcurrencyException ex) { throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.", ex); }
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
            .Select(x => new PutawayTaskListDto { Id=x.Id, ReceiptId=x.ReceiptId, ReceiptCode=x.Receipt.Code, WarehouseId=x.WarehouseId, WarehouseName=x.Warehouse.Name,
                Status=x.Status.ToString(), AssignedUserId=x.AssignedUserId, RequiredBaseQuantity=x.Items.Sum(i=>i.RequiredBaseQuantity), MovedBaseQuantity=x.Items.Sum(i=>i.MovedBaseQuantity), CreatedAt=x.CreatedAt }).ToListAsync(token);
    }

    public async Task<PutawayTaskDto> GetAsync(int id, CancellationToken token = default)
    {
        var task = await Query().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException("Không tìm thấy nhiệm vụ hoặc bạn không có quyền truy cập.");
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
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM dbo.PutawayTasks WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE Id = {id}; SELECT 1 FROM dbo.PutawayTaskItems WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE PutawayTaskId = {id};",
                    token);
            }
            var task=await Load(id,token); if(task.Status is not PutawayTaskStatus.Assigned and not PutawayTaskStatus.InProgress) throw Conflict("Nhiệm vụ không ở trạng thái có thể cất hàng."); EnsureActor(task);
            if (task.Status == PutawayTaskStatus.InProgress)
            {
                try { _ = Convert.FromBase64String(dto.RowVersion); }
                catch { throw new BusinessRuleException("Phiên bản dữ liệu không hợp lệ."); }
                context.Entry(task).Property(x => x.RowVersion).OriginalValue = task.RowVersion;
            }
            else
            {
                ApplyVersion(task.RowVersion,dto.RowVersion,context.Entry(task).Property(x=>x.RowVersion));
            }
            var item=task.Items.SingleOrDefault(x=>x.Id==dto.ItemId)??throw new NotFoundException("Không tìm thấy dòng hàng.");
            await context.AcquireInventoryAllocationLocksAsync([(task.WarehouseId, item.ProductId)], token);
            var destination=await LocationForMutation(dto.DestinationLocationId,token)??throw new NotFoundException("Không tìm thấy vị trí đích.");
            if(destination.WarehouseId!=task.WarehouseId) throw new NotFoundException("Không tìm thấy vị trí đích.");
            if(!destination.IsActive||destination.IsBlocked||destination.LocationType!=RequiredType(item.InventoryStatus)||(item.InventoryStatus==InventoryStatus.Available&&!destination.IsPickable)) throw new BusinessRuleException("Vị trí đích không phù hợp với hàng hóa này.");
            var baseQty=ToBase(dto.Quantity,dto.UnitCode,item); if(baseQty>item.RemainingBaseQuantity) throw new BusinessRuleException("Số lượng cất vượt quá số lượng còn lại.");
            var product=await ProductForCapacity(item.ProductId,token)??throw new NotFoundException("Không tìm thấy sản phẩm.");
            await EnsurePutawayCapacityAsync(destination, product, baseQty, token);
            var sourceBuckets = await context.InventoryStocks
                .Include(x => x.Lot)
                .Include(x => x.Serial)
                .Where(x => x.ProductId == item.ProductId &&
                            x.WarehouseId == task.WarehouseId &&
                            x.Status == item.InventoryStatus &&
                            x.LocationId == item.SourceLocationId &&
                            x.Quantity > x.ReservedQuantity)
                .OrderBy(x => x.LotId)
                .ThenBy(x => x.SerialId)
                .ThenBy(x => x.Id)
                .ToListAsync(token);
            if (item.ReceiptLineId > 0)
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
            if (sourceBuckets.Count == 0 || sourceBuckets.Sum(x => x.Quantity - x.ReservedQuantity) < baseQty)
                throw Conflict("Tồn tại vị trí nguồn đã thay đổi. Vui lòng tải lại.");
            var nowUtc = DateTime.UtcNow;
            var remainingToMove = baseQty;
            var enteredUnitCode = dto.UnitCode.Trim().ToUpperInvariant();
            foreach (var sourceBucket in sourceBuckets)
            {
                if (remainingToMove <= 0) break;
                var availableInBucket = sourceBucket.Quantity - sourceBucket.ReservedQuantity;
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
                sourceBucket.LastUpdated = nowUtc;

                var dest = context.InventoryStocks.Local.FirstOrDefault(x =>
                               x.ProductId == item.ProductId &&
                               x.WarehouseId == task.WarehouseId &&
                               x.Status == item.InventoryStatus &&
                               x.LocationId == destination.Id &&
                               x.LotId == sourceBucket.LotId &&
                               x.SerialId == sourceBucket.SerialId)
                           ?? await context.InventoryStocks.FirstOrDefaultAsync(x =>
                               x.ProductId == item.ProductId &&
                               x.WarehouseId == task.WarehouseId &&
                               x.Status == item.InventoryStatus &&
                               x.LocationId == destination.Id &&
                               x.LotId == sourceBucket.LotId &&
                               x.SerialId == sourceBucket.SerialId, token);
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
                        Quantity = chunk,
                        LastUpdated = nowUtc
                    });
                }
                else
                {
                    dest.Quantity += chunk;
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

                remainingToMove -= chunk;
            }
            item.MovedBaseQuantity+=baseQty; task.Status=task.Items.All(x=>x.RemainingBaseQuantity==0)?PutawayTaskStatus.Completed:PutawayTaskStatus.InProgress; task.StartedAt??=nowUtc; if(task.Status==PutawayTaskStatus.Completed)task.CompletedAt=nowUtc;
            context.AuditLogs.Add(Audit(currentUser.UserId,"PutawayTask.Moved",task.Id,task.WarehouseId,$"ItemId: {item.Id}; BaseQuantity: {baseQty}; DestinationId: {destination.Id}"));
            await context.SaveChangesAsync(token); if(tx is not null)await tx.CommitAsync(token); return await GetAsync(id,token);
        }
        catch (Exception ex) when (ownTransaction && attempt < maxAttempts &&
                                   (ex is DeadlockException ||
                                    ex is Microsoft.Data.SqlClient.SqlException { Number: 1205 } ||
                                    ex is DbUpdateException { InnerException: Microsoft.Data.SqlClient.SqlException { Number: 1205 } }))
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            await Task.Delay(15 * attempt, token);
        }
        catch(DbUpdateConcurrencyException ex){if(tx is not null)await tx.RollbackAsync(token);throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.",ex);} catch{if(tx is not null)await tx.RollbackAsync(token);throw;}
        }
    }

    private async Task<PutawayTaskDto> Mutate(int id,PutawayStateCommandDto dto,PutawayTaskStatus[] allowed,Action<PutawayTask> change,string action,CancellationToken token)
    {
        await using var tx=context.Database.CurrentTransaction is null?await context.Database.BeginTransactionAsync(token):null; try{var task=await Load(id,token);if(!allowed.Contains(task.Status))throw Conflict("Trạng thái nhiệm vụ không còn phù hợp.");ApplyVersion(task.RowVersion,dto.RowVersion,context.Entry(task).Property(x=>x.RowVersion));change(task);context.AuditLogs.Add(Audit(currentUser.UserId,$"PutawayTask.{action}",task.Id,task.WarehouseId,$"Status: {task.Status}"));await context.SaveChangesAsync(token);if(tx is not null)await tx.CommitAsync(token);return await GetAsync(id,token);}catch(DbUpdateConcurrencyException ex){if(tx is not null)await tx.RollbackAsync(token);throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.",ex);}catch{if(tx is not null)await tx.RollbackAsync(token);throw;}
    }
    private async Task<PutawayTask> Load(int id,CancellationToken token){var task=await Query().SingleOrDefaultAsync(x=>x.Id==id,token)??throw new NotFoundException("Không tìm thấy nhiệm vụ hoặc bạn không có quyền truy cập.");await warehouses.EnsureWarehouseAccessAsync(task.WarehouseId,token);return task;}
    // Hold destination eligibility through commit, including a concurrent master deactivation.
    private Task<WarehouseLocation?> LocationForMutation(int id, CancellationToken token) =>
        (context.Database.IsSqlServer()
            ? context.WarehouseLocations.FromSqlInterpolated($"SELECT * FROM dbo.WarehouseLocations WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}")
            : context.WarehouseLocations.Where(x => x.Id == id)).SingleOrDefaultAsync(token);
    private Task<Product?> ProductForCapacity(int id, CancellationToken token) =>
        (context.Database.IsSqlServer()
            ? context.Products.FromSqlInterpolated($"SELECT * FROM dbo.Products WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}").AsNoTracking()
            : context.Products.AsNoTracking().Where(x => x.Id == id)).SingleOrDefaultAsync(token);
    private IQueryable<PutawayTask> Query()=>context.PutawayTasks.Include(x=>x.Receipt).Include(x=>x.Warehouse).Include(x=>x.Items).ThenInclude(x=>x.ReceiptLine).ThenInclude(x=>x.Product).Include(x=>x.Items).ThenInclude(x=>x.SourceLocation);
    private void EnsureActor(PutawayTask task){if(task.AssignedUserId!=currentUser.UserId&&!currentUser.IsGlobalAdmin&&currentUser.Role is not "Admin" and not "Manager")throw new ForbiddenException("Bạn không có quyền thực hiện thao tác này.");}
    private static decimal ToBase(decimal qty,string unit,PutawayTaskItem item){var code=unit.Trim().ToUpperInvariant();var result=code==item.BaseUnitCodeSnapshot.ToUpperInvariant()?qty:code==item.OperationUnitCodeSnapshot.ToUpperInvariant()?qty*item.ConversionFactorSnapshot:throw new BusinessRuleException("Đơn vị tính không được hỗ trợ.");if(decimal.Round(result,item.BaseUnitDecimalPlaces)!=result)throw new BusinessRuleException("Số lượng vượt quá độ chính xác cho phép; hệ thống không tự làm tròn.");return result;}
    private static WarehouseLocationType RequiredType(InventoryStatus s)=>s switch{InventoryStatus.Available=>WarehouseLocationType.Storage,InventoryStatus.Damaged=>WarehouseLocationType.Damaged,InventoryStatus.Rejected=>WarehouseLocationType.Rejected,_=>throw new BusinessRuleException("Trạng thái tồn kho không thuộc luồng cất hàng tự động.")};
    private static void ApplyVersion(byte[] actual,string encoded,Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry<PutawayTask,byte[]> p){byte[] expected;try{expected=Convert.FromBase64String(encoded);}catch{throw new BusinessRuleException("Phiên bản dữ liệu không hợp lệ.");}if(!actual.SequenceEqual(expected))throw Conflict("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");p.OriginalValue=expected;}
    private async Task<PutawayTaskDto> MapAsync(PutawayTask t,CancellationToken token){var moves=await context.InventoryLocationMovements.AsNoTracking().Where(x=>x.PutawayTaskId==t.Id&&x.PutawayTaskItemId.HasValue).Join(context.WarehouseLocations,x=>x.FromLocationId,x=>x.Id,(m,f)=>new{m,f}).Join(context.WarehouseLocations,x=>x.m.ToLocationId,x=>x.Id,(x,d)=>new PutawayMovementDto{Id=x.m.Id,ItemId=x.m.PutawayTaskItemId.GetValueOrDefault(),FromLocationCode=x.f.Code,ToLocationCode=d.Code,BaseQuantity=x.m.BaseQuantity,CreatedAt=x.m.CreatedAt}).ToListAsync(token);var canMutate=currentUser.Role is "Admin" or "Manager" or "WarehouseStaff";return new PutawayTaskDto{Id=t.Id,ReceiptId=t.ReceiptId,ReceiptCode=t.Receipt.Code,WarehouseId=t.WarehouseId,WarehouseName=t.Warehouse.Name,Status=t.Status.ToString(),AssignedUserId=t.AssignedUserId,RequiredBaseQuantity=t.Items.Sum(x=>x.RequiredBaseQuantity),MovedBaseQuantity=t.Items.Sum(x=>x.MovedBaseQuantity),CreatedAt=t.CreatedAt,RowVersion=canMutate?Convert.ToBase64String(t.RowVersion):null,ExceptionReason=canMutate?t.ExceptionReason:null,Items=t.Items.Select(x=>new PutawayTaskItemDto{Id=x.Id,ReceiptLineId=x.ReceiptLineId,ProductId=x.ProductId,ProductCode=x.ReceiptLine.Product.Code,ProductName=x.ReceiptLine.Product.Name,InventoryStatus=x.InventoryStatus.ToString(),SourceLocationId=x.SourceLocationId,SourceLocationCode=x.SourceLocation.Code,OperationUnitCode=x.OperationUnitCodeSnapshot,BaseUnitCode=x.BaseUnitCodeSnapshot,RequiredOperationQuantity=x.RequiredOperationQuantity,RequiredBaseQuantity=x.RequiredBaseQuantity,MovedBaseQuantity=x.MovedBaseQuantity,RemainingBaseQuantity=x.RemainingBaseQuantity,RowVersion=canMutate?Convert.ToBase64String(x.RowVersion):null}).ToList(),Movements=moves};}
    private static AuditLog Audit(int user,string action,int entity,int warehouse,string values)=>new(){UserId=user,Action=action,EntityName="PutawayTask",EntityId=entity,WarehouseId=warehouse,NewValues=values,Result="Success",Timestamp=DateTime.UtcNow};
    private static BusinessRuleException Conflict(string message){var e=new BusinessRuleException(message);e.Data["HttpStatusCode"]=409;return e;}
    private WarehouseLocationDto Location(WarehouseLocation x)=>new(){Id=x.Id,WarehouseId=x.WarehouseId,Code=x.Code,Name=x.Name,StructurePath=x.StructurePath,StorageClass=x.StorageClass,MaxWeightKg=x.MaxWeightKg,MaxVolumeM3=x.MaxVolumeM3,MaxPalletEquivalent=x.MaxPalletEquivalent,MapX=x.MapX,MapY=x.MapY,MapWidth=x.MapWidth,MapHeight=x.MapHeight,LocationType=x.LocationType.ToString(),IsActive=x.IsActive,IsBlocked=x.IsBlocked,IsPickable=x.IsPickable,IsReceivable=x.IsReceivable,IsSystemManaged=x.IsSystemManaged,RowVersion=currentUser.Role is "Admin" or "Manager" or "WarehouseStaff"?Convert.ToBase64String(x.RowVersion):null};
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
