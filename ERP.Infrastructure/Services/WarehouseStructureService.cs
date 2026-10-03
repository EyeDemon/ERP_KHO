using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class WarehouseStructureService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouses,
    ICurrentUser currentUser) : IWarehouseStructureService
{
    private static readonly HashSet<string> ZoneTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "RECEIVING", "STORAGE", "PICKING", "QC", "QUARANTINE", "STAGING", "SHIPPING"
    };

    public async Task<WarehouseStructureDto> GetAsync(int warehouseId, CancellationToken token = default)
    {
        await warehouses.EnsureWarehouseAccessAsync(warehouseId, token);
        var warehouse = await context.Warehouses.AsNoTracking()
            .Where(x => x.Id == warehouseId)
            .Select(x => new { x.Id, x.Code, x.Name })
            .SingleOrDefaultAsync(token)
            ?? throw new NotFoundException("Không tìm thấy kho hoặc bạn không có quyền truy cập.");

        var zones = await context.WarehouseZones.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId)
            .Include(x => x.Aisles)
                .ThenInclude(x => x.Racks)
                    .ThenInclude(x => x.Levels)
            .OrderBy(x => x.Code)
            .ToListAsync(token);

        var locations = await LocationQuery()
            .Where(x => x.WarehouseId == warehouseId)
            .OrderBy(x => x.Code)
            .ToListAsync(token);

        var byZone = locations.Where(x => x.ZoneId.HasValue && !x.RackLevelId.HasValue)
            .GroupBy(x => x.ZoneId!.Value).ToDictionary(x => x.Key, x => x.ToList());
        var byLevel = locations.Where(x => x.RackLevelId.HasValue)
            .GroupBy(x => x.RackLevelId!.Value).ToDictionary(x => x.Key, x => x.ToList());

        return new WarehouseStructureDto
        {
            WarehouseId = warehouse.Id,
            WarehouseCode = warehouse.Code,
            WarehouseName = warehouse.Name,
            Zones = zones.Select(zone => Zone(zone, byZone, byLevel)).ToList(),
            SystemLocations = locations.Where(x => !x.ZoneId.HasValue).Select(Location).ToList()
        };
    }

    public async Task<WarehouseZoneDto> CreateZoneAsync(int warehouseId, CreateWarehouseZoneDto dto, CancellationToken token = default)
    {
        await warehouses.EnsureWarehouseAccessAsync(warehouseId, token);
        var code = Code(dto.Code, "Mã khu vực");
        var name = Required(dto.Name, "Tên khu vực");
        var type = ZoneType(dto.ZoneType);
        if (await context.WarehouseZones.AnyAsync(x => x.WarehouseId == warehouseId && x.Code == code, token))
            throw Conflict("Mã khu vực đã tồn tại trong kho.");

        await using var tx = context.Database.CurrentTransaction is null ? await context.Database.BeginTransactionAsync(token) : null;
        var entity = new WarehouseZone
        {
            WarehouseId = warehouseId,
            Code = code,
            Name = name,
            ZoneType = type,
            PickPriority = dto.PickPriority,
            PutawayPriority = dto.PutawayPriority,
            IsActive = true,
            CreatedBy = currentUser.UserId
        };
        context.WarehouseZones.Add(entity);
        await context.SaveChangesAsync(token);
        context.AuditLogs.Add(Audit("WarehouseZone.Created", "WarehouseZone", entity.Id, warehouseId, $"Code: {code}; Type: {type}"));
        await context.SaveChangesAsync(token);
        if (tx is not null) await tx.CommitAsync(token);
        return Zone(entity, [], []);
    }

    public async Task<WarehouseZoneDto> UpdateZoneAsync(int warehouseId, int zoneId, UpdateWarehouseZoneDto dto, CancellationToken token = default)
    {
        await warehouses.EnsureWarehouseAccessAsync(warehouseId, token);
        var entity = await context.WarehouseZones.SingleOrDefaultAsync(x => x.Id == zoneId && x.WarehouseId == warehouseId, token)
            ?? throw new NotFoundException("Không tìm thấy khu vực hoặc bạn không có quyền truy cập.");
        ApplyVersion(entity.RowVersion, dto.RowVersion, context.Entry(entity).Property(x => x.RowVersion));

        if (!dto.IsActive && entity.IsActive &&
            await context.WarehouseLocations.AnyAsync(x => x.ZoneId == entity.Id && x.IsActive, token))
            throw Conflict("Không thể ngừng hoạt động khu vực còn vị trí đang hoạt động.");

        entity.Name = Required(dto.Name, "Tên khu vực");
        entity.ZoneType = ZoneType(dto.ZoneType);
        entity.PickPriority = dto.PickPriority;
        entity.PutawayPriority = dto.PutawayPriority;
        entity.IsActive = dto.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = currentUser.UserId;

        try
        {
            await context.SaveChangesAsync(token);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.", ex);
        }
        context.AuditLogs.Add(Audit("WarehouseZone.Updated", "WarehouseZone", entity.Id, warehouseId, $"Active: {entity.IsActive}; Type: {entity.ZoneType}"));
        await context.SaveChangesAsync(token);
        return Zone(entity, [], []);
    }

    public async Task<WarehouseAisleDto> CreateAisleAsync(int warehouseId, int zoneId, CreateWarehouseAisleDto dto, CancellationToken token = default)
    {
        await warehouses.EnsureWarehouseAccessAsync(warehouseId, token);
        var zone = await context.WarehouseZones.SingleOrDefaultAsync(x => x.Id == zoneId && x.WarehouseId == warehouseId, token)
            ?? throw new NotFoundException("Không tìm thấy khu vực hoặc bạn không có quyền truy cập.");
        if (!zone.IsActive) throw new BusinessRuleException("Không thể thêm dãy kệ vào khu vực ngừng hoạt động.");
        var code = Code(dto.Code, "Mã dãy kệ");
        if (await context.WarehouseAisles.AnyAsync(x => x.ZoneId == zoneId && x.Code == code, token))
            throw Conflict("Mã dãy kệ đã tồn tại trong khu vực.");

        var entity = new WarehouseAisle { ZoneId = zoneId, Code = code, Name = Optional(dto.Name), CreatedBy = currentUser.UserId };
        context.WarehouseAisles.Add(entity);
        await context.SaveChangesAsync(token);
        context.AuditLogs.Add(Audit("WarehouseAisle.Created", "WarehouseAisle", entity.Id, warehouseId, $"ZoneId: {zoneId}; Code: {code}"));
        await context.SaveChangesAsync(token);
        return Aisle(entity, []);
    }

    public async Task<WarehouseAisleDto> UpdateAisleAsync(int warehouseId, int zoneId, int aisleId, UpdateWarehouseAisleDto dto, CancellationToken token = default)
    {
        await warehouses.EnsureWarehouseAccessAsync(warehouseId, token);
        var entity = await context.WarehouseAisles.Include(x => x.Zone)
            .SingleOrDefaultAsync(x => x.Id == aisleId && x.ZoneId == zoneId && x.Zone.WarehouseId == warehouseId, token)
            ?? throw new NotFoundException("Không tìm thấy dãy kệ hoặc bạn không có quyền truy cập.");
        ApplyVersion(entity.RowVersion, dto.RowVersion, context.Entry(entity).Property(x => x.RowVersion));
        entity.Name = Optional(dto.Name);
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = currentUser.UserId;
        await SaveConcurrencyAsync(token);
        context.AuditLogs.Add(Audit("WarehouseAisle.Updated", "WarehouseAisle", entity.Id, warehouseId, $"ZoneId: {zoneId}"));
        await context.SaveChangesAsync(token);
        return Aisle(entity, []);
    }

    public async Task<WarehouseRackDto> CreateRackAsync(int warehouseId, int aisleId, CreateWarehouseRackDto dto, CancellationToken token = default)
    {
        await warehouses.EnsureWarehouseAccessAsync(warehouseId, token);
        var aisle = await context.WarehouseAisles.Include(x => x.Zone)
            .SingleOrDefaultAsync(x => x.Id == aisleId && x.Zone.WarehouseId == warehouseId, token)
            ?? throw new NotFoundException("Không tìm thấy dãy kệ hoặc bạn không có quyền truy cập.");
        if (!aisle.Zone.IsActive) throw new BusinessRuleException("Không thể thêm kệ vào khu vực ngừng hoạt động.");
        var code = Code(dto.Code, "Mã kệ");
        if (await context.WarehouseRacks.AnyAsync(x => x.AisleId == aisleId && x.Code == code, token))
            throw Conflict("Mã kệ đã tồn tại trong dãy.");

        var entity = new WarehouseRack { AisleId = aisleId, Code = code, Name = Optional(dto.Name), CreatedBy = currentUser.UserId };
        context.WarehouseRacks.Add(entity);
        await context.SaveChangesAsync(token);
        context.AuditLogs.Add(Audit("WarehouseRack.Created", "WarehouseRack", entity.Id, warehouseId, $"AisleId: {aisleId}; Code: {code}"));
        await context.SaveChangesAsync(token);
        return Rack(entity, []);
    }

    public async Task<WarehouseRackDto> UpdateRackAsync(int warehouseId, int aisleId, int rackId, UpdateWarehouseRackDto dto, CancellationToken token = default)
    {
        await warehouses.EnsureWarehouseAccessAsync(warehouseId, token);
        var entity = await context.WarehouseRacks.Include(x => x.Aisle).ThenInclude(x => x.Zone)
            .SingleOrDefaultAsync(x => x.Id == rackId && x.AisleId == aisleId && x.Aisle.Zone.WarehouseId == warehouseId, token)
            ?? throw new NotFoundException("Không tìm thấy kệ hoặc bạn không có quyền truy cập.");
        ApplyVersion(entity.RowVersion, dto.RowVersion, context.Entry(entity).Property(x => x.RowVersion));
        entity.Name = Optional(dto.Name);
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = currentUser.UserId;
        await SaveConcurrencyAsync(token);
        context.AuditLogs.Add(Audit("WarehouseRack.Updated", "WarehouseRack", entity.Id, warehouseId, $"AisleId: {aisleId}"));
        await context.SaveChangesAsync(token);
        return Rack(entity, []);
    }

    public async Task<WarehouseRackLevelDto> CreateLevelAsync(int warehouseId, int rackId, CreateWarehouseRackLevelDto dto, CancellationToken token = default)
    {
        await warehouses.EnsureWarehouseAccessAsync(warehouseId, token);
        if (dto.LevelNo <= 0) throw new BusinessRuleException("Số tầng phải lớn hơn 0.");
        var rack = await context.WarehouseRacks.Include(x => x.Aisle).ThenInclude(x => x.Zone)
            .SingleOrDefaultAsync(x => x.Id == rackId && x.Aisle.Zone.WarehouseId == warehouseId, token)
            ?? throw new NotFoundException("Không tìm thấy kệ hoặc bạn không có quyền truy cập.");
        if (!rack.Aisle.Zone.IsActive) throw new BusinessRuleException("Không thể thêm tầng vào khu vực ngừng hoạt động.");
        if (await context.WarehouseRackLevels.AnyAsync(x => x.RackId == rackId && x.LevelNo == dto.LevelNo, token))
            throw Conflict("Tầng đã tồn tại trên kệ.");

        var entity = new WarehouseRackLevel { RackId = rackId, LevelNo = dto.LevelNo, CreatedBy = currentUser.UserId };
        context.WarehouseRackLevels.Add(entity);
        await context.SaveChangesAsync(token);
        context.AuditLogs.Add(Audit("WarehouseRackLevel.Created", "WarehouseRackLevel", entity.Id, warehouseId, $"RackId: {rackId}; LevelNo: {dto.LevelNo}"));
        await context.SaveChangesAsync(token);
        return Level(entity, []);
    }

    private IQueryable<WarehouseLocation> LocationQuery() => context.WarehouseLocations.AsNoTracking()
        .Include(x => x.Zone)
        .Include(x => x.RackLevel).ThenInclude(x => x!.Rack).ThenInclude(x => x.Aisle);

    private WarehouseZoneDto Zone(
        WarehouseZone x,
        IReadOnlyDictionary<int, List<WarehouseLocation>> byZone,
        IReadOnlyDictionary<int, List<WarehouseLocation>> byLevel) => new()
    {
        Id = x.Id,
        WarehouseId = x.WarehouseId,
        Code = x.Code,
        Name = x.Name,
        ZoneType = x.ZoneType,
        PickPriority = x.PickPriority,
        PutawayPriority = x.PutawayPriority,
        IsActive = x.IsActive,
        RowVersion = Version(x.RowVersion),
        Aisles = x.Aisles.OrderBy(a => a.Code).Select(a =>
            Aisle(a, a.Racks.OrderBy(r => r.Code).Select(r =>
                Rack(r, r.Levels.OrderBy(l => l.LevelNo).Select(l =>
                    Level(l, byLevel.TryGetValue(l.Id, out var locations) ? locations : [])).ToList())).ToList())).ToList(),
        Locations = byZone.TryGetValue(x.Id, out var zoneLocations) ? zoneLocations.Select(Location).ToList() : []
    };

    private WarehouseAisleDto Aisle(WarehouseAisle x, IReadOnlyList<WarehouseRackDto> racks) => new()
    {
        Id = x.Id, ZoneId = x.ZoneId, Code = x.Code, Name = x.Name, RowVersion = Version(x.RowVersion), Racks = racks
    };

    private WarehouseRackDto Rack(WarehouseRack x, IReadOnlyList<WarehouseRackLevelDto> levels) => new()
    {
        Id = x.Id, AisleId = x.AisleId, Code = x.Code, Name = x.Name, RowVersion = Version(x.RowVersion), Levels = levels
    };

    private WarehouseRackLevelDto Level(WarehouseRackLevel x, IReadOnlyList<WarehouseLocation> locations) => new()
    {
        Id = x.Id, RackId = x.RackId, LevelNo = x.LevelNo, RowVersion = Version(x.RowVersion), Locations = locations.Select(Location).ToList()
    };

    private WarehouseLocationDto Location(WarehouseLocation x) => new()
    {
        Id = x.Id,
        WarehouseId = x.WarehouseId,
        ZoneId = x.ZoneId,
        ZoneCode = x.Zone?.Code,
        RackLevelId = x.RackLevelId,
        AisleCode = x.RackLevel?.Rack?.Aisle?.Code,
        RackCode = x.RackLevel?.Rack?.Code,
        LevelNo = x.RackLevel?.LevelNo,
        Code = x.Code,
        Name = x.Name,
        Barcode = x.Barcode,
        PickPriority = x.PickPriority,
        PutawayPriority = x.PutawayPriority,
        LocationType = x.LocationType.ToString(),
        IsActive = x.IsActive,
        IsBlocked = x.IsBlocked,
        IsPickable = x.IsPickable,
        IsReceivable = x.IsReceivable,
        IsSystemManaged = x.IsSystemManaged,
        RowVersion = Version(x.RowVersion)
    };

    private string? Version(byte[] value) =>
        currentUser.Role is "Admin" or "Manager" ? Convert.ToBase64String(value) : null;

    private async Task SaveConcurrencyAsync(CancellationToken token)
    {
        try { await context.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException ex) { throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.", ex); }
    }

    private static void ApplyVersion(byte[] actual, string encoded, Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry<byte[]> property)
    {
        byte[] expected;
        try { expected = Convert.FromBase64String(encoded); }
        catch { throw new BusinessRuleException("Phiên bản dữ liệu không hợp lệ."); }
        if (expected.Length != 8 && actual.Length == 8) throw new BusinessRuleException("Phiên bản dữ liệu không hợp lệ.");
        if (!actual.SequenceEqual(expected)) throw Conflict("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
        property.OriginalValue = expected;
    }

    private static string Code(string value, string label)
    {
        var result = Required(value, label).ToUpperInvariant();
        if (result.Length > 30 || result.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_' or '.')))
            throw new BusinessRuleException($"{label} chỉ gồm chữ, số, dấu -, _, . và tối đa 30 ký tự.");
        return result;
    }

    private static string ZoneType(string value)
    {
        var result = Required(value, "Loại khu vực").ToUpperInvariant();
        if (!ZoneTypes.Contains(result)) throw new BusinessRuleException("Loại khu vực không hợp lệ.");
        return result;
    }

    private static string Required(string value, string label) =>
        string.IsNullOrWhiteSpace(value) ? throw new BusinessRuleException($"{label} là bắt buộc.") : value.Trim();

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private AuditLog Audit(string action, string entityName, int entityId, int warehouseId, string values) => new()
    {
        UserId = currentUser.UserId,
        Action = action,
        EntityName = entityName,
        EntityId = entityId,
        WarehouseId = warehouseId,
        NewValues = values,
        Result = "Success",
        Timestamp = DateTime.UtcNow
    };

    private static BusinessRuleException Conflict(string message)
    {
        var error = new BusinessRuleException(message);
        error.Data["HttpStatusCode"] = 409;
        return error;
    }
}
