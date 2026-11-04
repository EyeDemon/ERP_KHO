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

public sealed partial class PutawayService(
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
}
