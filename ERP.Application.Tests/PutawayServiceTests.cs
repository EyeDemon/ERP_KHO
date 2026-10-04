using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

public sealed class PutawayServiceTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly DbContextOptions<ErpKhoDbContext> options;
    public PutawayServiceTests()
    {
        connection.Open();
        options = new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlite(connection).Options;
        using var db = Create(); db.Database.EnsureCreated();
    }

    [Fact]
    public async Task Partial_and_split_moves_preserve_status_total_and_complete_once()
    {
        var ids = await SeedAsync();
        await using var db = Create(); var service = Service(db, ids.WarehouseId, ids.UserId);
        var task = await service.GetAsync(ids.TaskId);

        task = await service.MoveAsync(task.Id, new MovePutawayItemDto { ItemId = ids.ItemId, DestinationLocationId = ids.Storage1, Quantity = 2, UnitCode = "BOX", RowVersion = task.RowVersion! });
        task.Status.Should().Be(nameof(PutawayTaskStatus.InProgress)); task.Items.Single().RemainingBaseQuantity.Should().Be(6);
        task = await service.MoveAsync(task.Id, new MovePutawayItemDto { ItemId = ids.ItemId, DestinationLocationId = ids.Storage2, Quantity = 3, UnitCode = "BOX", RowVersion = task.RowVersion! });

        task.Status.Should().Be(nameof(PutawayTaskStatus.Completed));
        (await db.InventoryStocks.Where(x => x.ProductId == ids.ProductId).SumAsync(x => x.Quantity)).Should().Be(10);
        (await db.InventoryStocks.SingleAsync(x => x.LocationId == ids.Receiving)).Quantity.Should().Be(0);
        (await db.InventoryLocationMovements.CountAsync()).Should().Be(2);
        (await db.AuditLogs.CountAsync(x => x.Action == "PutawayTask.Moved")).Should().Be(2);
    }

    [Fact]
    public async Task Move_rejects_status_incompatible_destination_without_effect()
    {
        var ids = await SeedAsync(); await using var db = Create(); var service = Service(db, ids.WarehouseId, ids.UserId);
        var task = await service.GetAsync(ids.TaskId);
        var action = () => service.MoveAsync(task.Id, new MovePutawayItemDto { ItemId = ids.ItemId, DestinationLocationId = ids.Damaged, Quantity = 1, UnitCode = "EA", RowVersion = task.RowVersion! });
        await action.Should().ThrowAsync<ERP.Application.Exceptions.BusinessRuleException>();
        (await db.InventoryLocationMovements.CountAsync()).Should().Be(0);
        (await db.InventoryStocks.SingleAsync(x => x.LocationId == ids.Receiving)).Quantity.Should().Be(10);
    }

    [Fact]
    public async Task Location_master_normalizes_code_and_protects_system_location()
    {
        var ids = await SeedAsync(); await using var db = Create(); var service = Service(db, ids.WarehouseId, ids.UserId);
        var created = await service.CreateLocationAsync(new CreateWarehouseLocationDto { WarehouseId=ids.WarehouseId, Code="  shelf-c  ", Name="Kệ C", StructurePath=" zone-a / a01 / r02 / l03 / b04 ", LocationType="Storage", IsPickable=true });
        created.Code.Should().Be("SHELF-C"); created.LocationType.Should().Be(nameof(WarehouseLocationType.Storage)); created.StructurePath.Should().Be("ZONE-A/A01/R02/L03/B04");
        var receiving = (await service.ListLocationsAsync(ids.WarehouseId)).Single(x => x.Id == ids.Receiving);
        var action = () => service.UpdateLocationAsync(receiving.Id, new UpdateWarehouseLocationDto { Name=receiving.Name, IsActive=false, IsReceivable=true, RowVersion=receiving.RowVersion! });
        await action.Should().ThrowAsync<ERP.Application.Exceptions.BusinessRuleException>();
    }

    [Fact]
    public async Task Location_structure_path_is_write_once_and_validated()
    {
        var ids = await SeedAsync(); await using var db = Create(); var service = Service(db, ids.WarehouseId, ids.UserId);
        var invalid = () => service.CreateLocationAsync(new CreateWarehouseLocationDto { WarehouseId=ids.WarehouseId, Code="BAD-PATH", Name="Sai cấu trúc", StructurePath="ZONE-A/A01/R02/L03", LocationType="Storage", IsPickable=true });
        await invalid.Should().ThrowAsync<ERP.Application.Exceptions.BusinessRuleException>();

        var created = await service.CreateLocationAsync(new CreateWarehouseLocationDto { WarehouseId=ids.WarehouseId, Code="STRUCT-01", Name="Ô cấu trúc", StructurePath="ZONE-A/A01/R02/L03/B10", LocationType="Storage", IsPickable=true });
        var change = () => service.UpdateLocationAsync(created.Id, new UpdateWarehouseLocationDto { Name=created.Name, StructurePath="ZONE-B/A01/R02/L03/B10", IsActive=true, IsPickable=true, RowVersion=created.RowVersion! });
        await change.Should().ThrowAsync<ERP.Application.Exceptions.BusinessRuleException>()
            .Where(x => Equals(x.Data["HttpStatusCode"], 409));
    }

    [Fact]
    public async Task Move_rejects_storage_class_mismatch_without_effect()
    {
        var ids = await SeedAsync(); await using var db = Create();
        var product = await db.Products.SingleAsync(x => x.Id == ids.ProductId);
        var location = await db.WarehouseLocations.SingleAsync(x => x.Id == ids.Storage1);
        product.StorageClass = "CHILLED";
        location.StorageClass = "AMBIENT";
        await db.SaveChangesAsync();
        var service = Service(db, ids.WarehouseId, ids.UserId);
        var task = await service.GetAsync(ids.TaskId);

        var action = () => service.MoveAsync(task.Id, new MovePutawayItemDto { ItemId=ids.ItemId, DestinationLocationId=ids.Storage1, Quantity=1, UnitCode="EA", RowVersion=task.RowVersion! });

        await action.Should().ThrowAsync<ERP.Application.Exceptions.BusinessRuleException>().WithMessage("*Storage Class*");
        (await db.InventoryLocationMovements.CountAsync()).Should().Be(0);
        (await db.InventoryStocks.SingleAsync(x => x.LocationId == ids.Receiving)).Quantity.Should().Be(10);
    }

    [Fact]
    public async Task Move_rejects_projected_capacity_and_dashboard_reports_current_usage()
    {
        var ids = await SeedAsync(); await using var db = Create();
        var product = await db.Products.SingleAsync(x => x.Id == ids.ProductId);
        var location = await db.WarehouseLocations.SingleAsync(x => x.Id == ids.Storage1);
        product.StorageClass = "AMBIENT";
        product.UnitWeightKg = 2m;
        location.StorageClass = "AMBIENT";
        location.MaxWeightKg = 4.5m;
        db.InventoryStocks.Add(new InventoryStock { ProductId=ids.ProductId, WarehouseId=ids.WarehouseId, LocationId=ids.Storage1, Status=InventoryStatus.Available, Quantity=2 });
        await db.SaveChangesAsync();
        var service = Service(db, ids.WarehouseId, ids.UserId);

        var capacity = (await service.ListLocationCapacitiesAsync(ids.WarehouseId)).Single(x => x.LocationId == ids.Storage1);
        capacity.UsedWeightKg.Should().Be(4m);
        capacity.State.Should().Be("NearCapacity");

        var task = await service.GetAsync(ids.TaskId);
        var action = () => service.MoveAsync(task.Id, new MovePutawayItemDto { ItemId=ids.ItemId, DestinationLocationId=ids.Storage1, Quantity=1, UnitCode="EA", RowVersion=task.RowVersion! });
        await action.Should().ThrowAsync<ERP.Application.Exceptions.BusinessRuleException>().WithMessage("*Vượt giới hạn trọng lượng*");
        (await db.InventoryLocationMovements.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Capacity_dashboard_marks_legacy_profile_gap_and_constraint_update_fails_closed()
    {
        var ids = await SeedAsync(); await using var db = Create();
        var location = await db.WarehouseLocations.SingleAsync(x => x.Id == ids.Storage1);
        location.MaxVolumeM3 = 10m;
        db.InventoryStocks.Add(new InventoryStock { ProductId=ids.ProductId, WarehouseId=ids.WarehouseId, LocationId=ids.Storage1, Status=InventoryStatus.Available, Quantity=2 });
        await db.SaveChangesAsync();
        var service = Service(db, ids.WarehouseId, ids.UserId);

        var capacity = (await service.ListLocationCapacitiesAsync(ids.WarehouseId)).Single(x => x.LocationId == ids.Storage1);
        capacity.ProfileIncomplete.Should().BeTrue();
        capacity.State.Should().Be("ProfileIncomplete");

        var dto = (await service.ListLocationsAsync(ids.WarehouseId)).Single(x => x.Id == ids.Storage1);
        var action = () => service.UpdateLocationAsync(ids.Storage1, new UpdateWarehouseLocationDto
        {
            Name = dto.Name,
            IsActive = true,
            IsPickable = true,
            UpdateConstraints = true,
            StorageClass = "AMBIENT",
            MaxVolumeM3 = 5m,
            RowVersion = dto.RowVersion!
        });
        await action.Should().ThrowAsync<ERP.Application.Exceptions.BusinessRuleException>()
            .Where(x => Equals(x.Data["HttpStatusCode"], 409));
    }

    [Fact]
    public async Task Location_layout_requires_complete_bounded_rectangle_and_writes_audit()
    {
        var ids = await SeedAsync(); await using var db = Create(); var service = Service(db, ids.WarehouseId, ids.UserId);
        var location = (await service.ListLocationsAsync(ids.WarehouseId)).Single(x => x.Id == ids.Storage1);

        var invalid = () => service.UpdateLocationLayoutAsync(ids.Storage1, new UpdateWarehouseLocationLayoutDto
        {
            MapX = 10m,
            RowVersion = location.RowVersion!
        });
        await invalid.Should().ThrowAsync<ERP.Application.Exceptions.BusinessRuleException>().WithMessage("*đủ X, Y, Width và Height*");

        var updated = await service.UpdateLocationLayoutAsync(ids.Storage1, new UpdateWarehouseLocationLayoutDto
        {
            MapX = 10m,
            MapY = 20m,
            MapWidth = 30m,
            MapHeight = 40m,
            RowVersion = location.RowVersion!
        });

        updated.MapX.Should().Be(10m);
        updated.MapY.Should().Be(20m);
        updated.MapWidth.Should().Be(30m);
        updated.MapHeight.Should().Be(40m);
        (await db.AuditLogs.CountAsync(x => x.Action == "WarehouseLocation.LayoutUpdated" && x.EntityId == ids.Storage1)).Should().Be(1);
    }

    [Fact]
    public async Task Warehouse_map_uses_real_capacity_and_recent_movement_without_fabricating_missing_layout()
    {
        var ids = await SeedAsync(); await using var db = Create();
        var product = await db.Products.SingleAsync(x => x.Id == ids.ProductId);
        var location = await db.WarehouseLocations.SingleAsync(x => x.Id == ids.Storage1);
        product.UnitWeightKg = 1m;
        location.MaxWeightKg = 10m;
        location.MapX = 5m;
        location.MapY = 10m;
        location.MapWidth = 20m;
        location.MapHeight = 15m;
        db.InventoryStocks.Add(new InventoryStock { ProductId=ids.ProductId, WarehouseId=ids.WarehouseId, LocationId=ids.Storage1, Status=InventoryStatus.Available, Quantity=5 });
        db.InventoryLocationMovements.Add(new InventoryLocationMovement { WarehouseId=ids.WarehouseId, ProductId=ids.ProductId, InventoryStatus=InventoryStatus.Available, FromLocationId=ids.Receiving, ToLocationId=ids.Storage1, BaseQuantity=1, EnteredQuantity=1, EnteredUnitCode="EA", PutawayTaskId=ids.TaskId, PutawayTaskItemId=ids.ItemId, ReceiptId=1, ReceiptLineId=1, CreatedBy=ids.UserId, CreatedAt=DateTime.UtcNow });
        await db.SaveChangesAsync();

        var map = await Service(db, ids.WarehouseId, ids.UserId).GetWarehouseMapAsync(ids.WarehouseId);
        var mapped = map.Items.Single(x => x.LocationId == ids.Storage1);
        var unmapped = map.Items.Single(x => x.LocationId == ids.Storage2);

        mapped.UtilizationPercent.Should().Be(50m);
        mapped.RecentMovementCount.Should().Be(1);
        mapped.MapX.Should().Be(5m);
        unmapped.MapX.Should().BeNull();
        map.GeneratedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Mutation_reuses_ambient_idempotency_transaction()
    {
        var ids = await SeedAsync(); await using var db = Create(); var service = Service(db, ids.WarehouseId, ids.UserId);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var task = await service.GetAsync(ids.TaskId);
        task = await service.StartAsync(task.Id, new PutawayStateCommandDto { RowVersion=task.RowVersion! });
        task.Status.Should().Be(nameof(PutawayTaskStatus.InProgress));
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Location_deactivation_protects_stock_and_unfinished_tasks_but_allows_empty_location()
    {
        var ids = await SeedAsync(); await using var db = Create(); var service = Service(db, ids.WarehouseId, ids.UserId);
        var task = await service.GetAsync(ids.TaskId);
        await service.MoveAsync(task.Id, new MovePutawayItemDto { ItemId=ids.ItemId, DestinationLocationId=ids.Storage1, Quantity=2, UnitCode="EA", RowVersion=task.RowVersion! });
        async Task Deactivate(int id)
        {
            var location = (await service.ListLocationsAsync(ids.WarehouseId)).Single(x => x.Id == id);
            await service.UpdateLocationAsync(id, new UpdateWarehouseLocationDto { Name=location.Name, IsActive=false, IsPickable=true, RowVersion=location.RowVersion! });
        }
        await FluentActions.Awaiting(() => Deactivate(ids.Storage1)).Should().ThrowAsync<ERP.Application.Exceptions.BusinessRuleException>()
            .Where(x => Equals(x.Data["HttpStatusCode"], 409));
        var item = await db.PutawayTaskItems.SingleAsync(x => x.Id == ids.ItemId);
        item.SourceLocationId=ids.Storage2; await db.SaveChangesAsync();
        await FluentActions.Awaiting(() => Deactivate(ids.Storage2)).Should().ThrowAsync<ERP.Application.Exceptions.BusinessRuleException>();
        item.SourceLocationId=ids.Receiving; await db.SaveChangesAsync();
        await Deactivate(ids.Storage2);
        (await db.WarehouseLocations.SingleAsync(x => x.Id == ids.Storage1)).IsActive.Should().BeTrue();
        (await db.WarehouseLocations.SingleAsync(x => x.Id == ids.Storage2)).IsActive.Should().BeFalse();
        (await db.InventoryStocks.SumAsync(x => x.Quantity)).Should().Be(10);
        (await db.InventoryLocationMovements.CountAsync()).Should().Be(1);
    }

    private async Task<(int WarehouseId,int UserId,int ProductId,int TaskId,int ItemId,int Receiving,int Storage1,int Storage2,int Damaged)> SeedAsync()
    {
        await using var db=Create(); var role=new Role{RoleName="Manager"}; var user=new User{Username=Guid.NewGuid().ToString("N"),PasswordHash="x",FullName="QA",Role=role};
        var unit=new Unit{Code="EA",Name="Cái",DecimalPlaces=0}; var warehouse=new Warehouse{Code="W1",Name="Kho"}; var product=new Product{Code="P1",Name="Sản phẩm",Unit=unit}; db.AddRange(role,user,warehouse,product); await db.SaveChangesAsync();
        var receiving=new WarehouseLocation{WarehouseId=warehouse.Id,Code="RECEIVING",Name="Nhận",LocationType=WarehouseLocationType.Receiving,IsActive=true,IsReceivable=true,IsSystemManaged=true};
        var s1=new WarehouseLocation{WarehouseId=warehouse.Id,Code="S1",Name="Kệ 1",LocationType=WarehouseLocationType.Storage,IsActive=true,IsPickable=true}; var s2=new WarehouseLocation{WarehouseId=warehouse.Id,Code="S2",Name="Kệ 2",LocationType=WarehouseLocationType.Storage,IsActive=true,IsPickable=true}; var damaged=new WarehouseLocation{WarehouseId=warehouse.Id,Code="D1",Name="Hư",LocationType=WarehouseLocationType.Damaged,IsActive=true}; db.AddRange(receiving,s1,s2,damaged); await db.SaveChangesAsync();
        var receipt=new ImportReceipt{Code="PN1",WarehouseId=warehouse.Id,Status=ReceiptStatus.Posted,CreatedBy=user.Id}; var line=new ImportReceiptDetail{ImportReceipt=receipt,ProductId=product.Id,Quantity=5,ExpectedQuantity=5,ReceivedQuantity=5,AcceptedQuantity=5,PostedQuantity=5,OperationUnitId=unit.Id,OperationUnitCodeSnapshot="BOX",BaseUnitId=unit.Id,BaseUnitCodeSnapshot="EA",ConversionFactor=2,ConversionVersion=3,BaseExpectedQuantity=10,BaseReceivedQuantity=10,BaseAcceptedQuantity=10,BasePostedQuantity=10}; db.Add(line); await db.SaveChangesAsync();
        var task=new PutawayTask{ReceiptId=receipt.Id,WarehouseId=warehouse.Id,Status=PutawayTaskStatus.Assigned,AssignedUserId=user.Id,CreatedBy=user.Id,RowVersion=Guid.NewGuid().ToByteArray()}; var item=new PutawayTaskItem{PutawayTask=task,ReceiptLineId=line.Id,ProductId=product.Id,InventoryStatus=InventoryStatus.Available,SourceLocationId=receiving.Id,OperationUnitId=unit.Id,OperationUnitCodeSnapshot="BOX",BaseUnitId=unit.Id,BaseUnitCodeSnapshot="EA",ConversionFactorSnapshot=2,ConversionVersionSnapshot=3,BaseUnitDecimalPlaces=0,RequiredOperationQuantity=5,RequiredBaseQuantity=10,RowVersion=Guid.NewGuid().ToByteArray()}; db.AddRange(item,new InventoryStock{ProductId=product.Id,WarehouseId=warehouse.Id,LocationId=receiving.Id,Status=InventoryStatus.Available,Quantity=10}); await db.SaveChangesAsync();
        return(warehouse.Id,user.Id,product.Id,task.Id,item.Id,receiving.Id,s1.Id,s2.Id,damaged.Id);
    }
    private TestContext Create()=>new(options);
    private static PutawayService Service(ErpKhoDbContext db,int warehouse,int user)=>new(db,new Access(warehouse),new Current(user));
    public void Dispose()=>connection.Dispose();
    private sealed record Current(int UserId):ICurrentUser{public bool IsAuthenticated=>true;public bool IsGlobalAdmin=>false;public string Role=>"Manager";}
    private sealed class Access(int id):IWarehouseAuthorizationService{public Task<IReadOnlyList<int>> GetAccessibleWarehouseIdsAsync(CancellationToken c=default)=>Task.FromResult<IReadOnlyList<int>>([id]);public Task<bool> CanAccessWarehouseAsync(int w,CancellationToken c=default)=>Task.FromResult(w==id);public Task EnsureWarehouseAccessAsync(int w,CancellationToken c=default)=>w==id?Task.CompletedTask:throw new UnauthorizedAccessException();}
    private sealed class TestContext(DbContextOptions<ErpKhoDbContext> o):ErpKhoDbContext(o){public override Task<int> SaveChangesAsync(CancellationToken c=default){foreach(var e in ChangeTracker.Entries().Where(x=>x.Entity is PutawayTask or PutawayTaskItem or WarehouseLocation&&x.State is EntityState.Added or EntityState.Modified))e.Property("RowVersion").CurrentValue=Guid.NewGuid().ToByteArray();return base.SaveChangesAsync(c);}protected override void OnModelCreating(ModelBuilder m){base.OnModelCreating(m);foreach(var p in m.Model.GetEntityTypes().SelectMany(x=>x.GetProperties()))if(p.GetColumnType()?.Contains("max",StringComparison.OrdinalIgnoreCase)==true)p.SetColumnType(null);foreach(var t in new[]{typeof(PutawayTask),typeof(PutawayTaskItem),typeof(WarehouseLocation)})m.Entity(t).Property("RowVersion").IsConcurrencyToken().ValueGeneratedNever();}}
}
