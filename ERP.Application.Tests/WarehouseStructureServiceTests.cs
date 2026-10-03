using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

public sealed class WarehouseStructureServiceTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly DbContextOptions<ErpKhoDbContext> options;

    public WarehouseStructureServiceTests()
    {
        connection.Open();
        options = new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlite(connection).Options;
        using var db = Create();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task Creates_canonical_zone_aisle_rack_level_hierarchy_with_parent_scoped_codes()
    {
        var seed = await SeedAsync();
        await using var db = Create();
        var service = Service(db, seed.WarehouseId, seed.UserId);

        var zone = await service.CreateZoneAsync(seed.WarehouseId, new CreateWarehouseZoneDto
        {
            Code = " zone-a ",
            Name = "Khu A",
            ZoneType = "storage",
            PickPriority = 10,
            PutawayPriority = 20
        });
        var aisle = await service.CreateAisleAsync(seed.WarehouseId, zone.Id, new CreateWarehouseAisleDto { Code = " a01 ", Name = "Dãy 01" });
        var rack = await service.CreateRackAsync(seed.WarehouseId, aisle.Id, new CreateWarehouseRackDto { Code = " r02 ", Name = "Kệ 02" });
        var level = await service.CreateLevelAsync(seed.WarehouseId, rack.Id, new CreateWarehouseRackLevelDto { LevelNo = 3 });

        zone.Code.Should().Be("ZONE-A");
        aisle.Code.Should().Be("A01");
        rack.Code.Should().Be("R02");
        level.LevelNo.Should().Be(3);

        var structure = await service.GetAsync(seed.WarehouseId);
        structure.Zones.Single().Aisles.Single().Racks.Single().Levels.Single().LevelNo.Should().Be(3);

        await FluentActions.Awaiting(() => service.CreateAisleAsync(seed.WarehouseId, zone.Id, new CreateWarehouseAisleDto { Code = "A01" }))
            .Should().ThrowAsync<ERP.Application.Exceptions.BusinessRuleException>()
            .Where(x => Equals(x.Data["HttpStatusCode"], 409));
    }

    [Fact]
    public async Task Rejects_cross_warehouse_parent_ids()
    {
        var seed = await SeedAsync();
        await using var db = Create();
        var other = new Warehouse { Code = "W2", Name = "Kho 2" };
        db.Warehouses.Add(other);
        await db.SaveChangesAsync();
        var zone = new WarehouseZone { WarehouseId = other.Id, Code = "OTHER", Name = "Khu khác", ZoneType = "STORAGE", IsActive = true, CreatedBy = seed.UserId };
        db.WarehouseZones.Add(zone);
        await db.SaveChangesAsync();

        var service = Service(db, seed.WarehouseId, seed.UserId);
        await FluentActions.Awaiting(() => service.CreateAisleAsync(seed.WarehouseId, zone.Id, new CreateWarehouseAisleDto { Code = "A01" }))
            .Should().ThrowAsync<ERP.Application.Exceptions.NotFoundException>();
    }

    [Fact]
    public async Task Cannot_deactivate_zone_while_active_location_remains()
    {
        var seed = await SeedAsync();
        await using var db = Create();
        var service = Service(db, seed.WarehouseId, seed.UserId);
        var zone = await service.CreateZoneAsync(seed.WarehouseId, new CreateWarehouseZoneDto { Code = "STORAGE", Name = "Lưu trữ", ZoneType = "STORAGE" });
        db.WarehouseLocations.Add(new WarehouseLocation
        {
            WarehouseId = seed.WarehouseId,
            ZoneId = zone.Id,
            Code = "BIN-01",
            Name = "Ô 01",
            LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            CreatedBy = seed.UserId
        });
        await db.SaveChangesAsync();

        await FluentActions.Awaiting(() => service.UpdateZoneAsync(seed.WarehouseId, zone.Id, new UpdateWarehouseZoneDto
        {
            Name = zone.Name,
            ZoneType = zone.ZoneType,
            IsActive = false,
            RowVersion = zone.RowVersion!
        }))
        .Should().ThrowAsync<ERP.Application.Exceptions.BusinessRuleException>()
        .Where(x => Equals(x.Data["HttpStatusCode"], 409));
    }

    [Fact]
    public async Task Stale_zone_rowversion_is_rejected()
    {
        var seed = await SeedAsync();
        await using var db = Create();
        var service = Service(db, seed.WarehouseId, seed.UserId);
        var zone = await service.CreateZoneAsync(seed.WarehouseId, new CreateWarehouseZoneDto { Code = "Z1", Name = "Khu 1", ZoneType = "STORAGE" });
        var stale = zone.RowVersion!;

        zone = await service.UpdateZoneAsync(seed.WarehouseId, zone.Id, new UpdateWarehouseZoneDto
        {
            Name = "Khu 1A",
            ZoneType = zone.ZoneType,
            IsActive = true,
            RowVersion = zone.RowVersion!
        });

        await FluentActions.Awaiting(() => service.UpdateZoneAsync(seed.WarehouseId, zone.Id, new UpdateWarehouseZoneDto
        {
            Name = "Khu lỗi",
            ZoneType = zone.ZoneType,
            IsActive = true,
            RowVersion = stale
        })).Should().ThrowAsync<ERP.Application.Exceptions.BusinessRuleException>();
    }

    private async Task<(int WarehouseId, int UserId)> SeedAsync()
    {
        await using var db = Create();
        var role = new Role { RoleName = "Manager" };
        var user = new User { Username = Guid.NewGuid().ToString("N"), PasswordHash = "x", FullName = "QA", Role = role };
        var warehouse = new Warehouse { Code = "W1", Name = "Kho 1" };
        db.AddRange(role, user, warehouse);
        await db.SaveChangesAsync();
        return (warehouse.Id, user.Id);
    }

    private TestContext Create() => new(options);
    private static WarehouseStructureService Service(ErpKhoDbContext db, int warehouseId, int userId) =>
        new(db, new Access(warehouseId), new Current(userId));

    public void Dispose() => connection.Dispose();

    private sealed record Current(int UserId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public bool IsGlobalAdmin => false;
        public string Role => "Manager";
    }

    private sealed class Access(int warehouseId) : IWarehouseAuthorizationService
    {
        public Task<IReadOnlyList<int>> GetAccessibleWarehouseIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<int>>([warehouseId]);
        public Task<bool> CanAccessWarehouseAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(id == warehouseId);
        public Task EnsureWarehouseAccessAsync(int id, CancellationToken cancellationToken = default) =>
            id == warehouseId ? Task.CompletedTask : throw new UnauthorizedAccessException();
    }

    private sealed class TestContext(DbContextOptions<ErpKhoDbContext> options) : ErpKhoDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var entry in ChangeTracker.Entries().Where(x =>
                         x.State is EntityState.Added or EntityState.Modified &&
                         x.Entity is WarehouseZone or WarehouseAisle or WarehouseRack or WarehouseRackLevel or WarehouseLocation))
                entry.Property("RowVersion").CurrentValue = Guid.NewGuid().ToByteArray();
            return base.SaveChangesAsync(cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(x => x.GetProperties()))
                if (property.GetColumnType()?.Contains("max", StringComparison.OrdinalIgnoreCase) == true)
                    property.SetColumnType(null);
            foreach (var type in new[] { typeof(WarehouseZone), typeof(WarehouseAisle), typeof(WarehouseRack), typeof(WarehouseRackLevel), typeof(WarehouseLocation) })
                modelBuilder.Entity(type).Property("RowVersion").IsConcurrencyToken().ValueGeneratedNever();
        }
    }
}
