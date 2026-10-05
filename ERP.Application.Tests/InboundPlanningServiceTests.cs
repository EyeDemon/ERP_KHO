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

public sealed class InboundPlanningServiceTests : IDisposable
{
    private readonly SqliteConnection connection=new("Data Source=:memory:");
    private readonly DbContextOptions<ErpKhoDbContext> options;

    public InboundPlanningServiceTests()
    {
        connection.Open();
        options=new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlite(connection).Options;
        using var db=Create(); db.Database.EnsureCreated();
    }

    [Fact]
    public async Task Purchase_order_and_asn_state_chain_is_inventory_neutral()
    {
        var ids=await SeedAsync();
        await using var db=Create();
        var service=new InboundPlanningService(db,new Access(ids.WarehouseId),new Current(ids.UserId));

        var po=await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderDto
        {
            ExternalPoId="ERP-PO-001",SourceSystem="ERP",Code="PO-001",SupplierId=ids.SupplierId,WarehouseId=ids.WarehouseId,
            OrderDate=DateTime.UtcNow.Date,ExpectedDate=DateTime.UtcNow.Date.AddDays(2),
            Lines=[new CreatePurchaseOrderLineDto{ExternalLineId="1",ProductId=ids.ProductId,OperationUnitId=ids.UnitId,OrderedQuantity=10,AllowedOverReceiptPct=5}]
        });
        po.Status.Should().Be(nameof(PurchaseOrderStatus.Draft));
        (await db.InventoryStocks.CountAsync()).Should().Be(0);
        (await db.InventoryTransactions.CountAsync()).Should().Be(0);

        po=await service.OpenPurchaseOrderAsync(po.Id,new InboundStateCommandDto{RowVersion=po.RowVersion!});
        var poLine=po.Lines.Single();

        var asn=await service.CreateAsnAsync(new CreateAsnDto
        {
            Code="ASN-001",PurchaseOrderId=po.Id,SupplierId=ids.SupplierId,WarehouseId=ids.WarehouseId,ExpectedArrivalAtUtc=DateTime.UtcNow.AddDays(1),
            Lines=[new CreateAsnLineDto{PurchaseOrderLineId=poLine.Id,ProductId=ids.ProductId,OperationUnitId=ids.UnitId,ExpectedQuantity=10}]
        });
        asn=await service.ConfirmAsnAsync(asn.Id,new InboundStateCommandDto{RowVersion=asn.RowVersion!});
        asn=await service.MarkAsnInTransitAsync(asn.Id,new InboundStateCommandDto{RowVersion=asn.RowVersion!});
        asn=await service.ArriveAsnAsync(asn.Id,new InboundStateCommandDto{RowVersion=asn.RowVersion!});
        asn=await service.StartReceivingAsnAsync(asn.Id,new InboundStateCommandDto{RowVersion=asn.RowVersion!});
        asn=await service.CompleteAsnAsync(asn.Id,new InboundStateCommandDto{RowVersion=asn.RowVersion!});

        asn.Status.Should().Be(nameof(AsnStatus.Completed));
        (await db.InventoryStocks.CountAsync()).Should().Be(0);
        (await db.InventoryTransactions.CountAsync()).Should().Be(0);
        var audits=await db.AuditLogs.Where(x=>x.EntityName=="PurchaseOrder"||x.EntityName=="Asn").ToListAsync();
        audits.Should().HaveCountGreaterThanOrEqualTo(8);
        audits.Should().OnlyContain(x=>x.EntityId>0, "mọi audit PO/ASN phải trỏ tới entity đã được persist");
    }

    private async Task<(int WarehouseId,int SupplierId,int ProductId,int UnitId,int UserId)> SeedAsync()
    {
        await using var db=Create();
        var role=new Role{RoleName="Manager"};
        var user=new User{Username="inbound-planning-test",PasswordHash="x",FullName="Inbound QA",Role=role};
        var unit=new Unit{Code="EA",Name="Cái",DecimalPlaces=0};
        var warehouse=new Warehouse{Code="W-INB",Name="Kho Inbound",IsActive=true};
        var supplier=new BusinessPartner{Code="SUP-INB",Name="Nhà cung cấp",IsSupplier=true,IsActive=true};
        var product=new Product{Code="P-INB",Name="Sản phẩm",Unit=unit,IsActive=true};
        db.AddRange(role,user,unit,warehouse,supplier,product);
        await db.SaveChangesAsync();
        return(warehouse.Id,supplier.Id,product.Id,unit.Id,user.Id);
    }

    private TestContext Create()=>new(options);
    public void Dispose()=>connection.Dispose();

    private sealed record Current(int UserId):ICurrentUser
    {
        public bool IsAuthenticated=>true;
        public bool IsGlobalAdmin=>false;
        public string Role=>"Manager";
    }

    private sealed class Access(int warehouseId):IWarehouseAuthorizationService
    {
        public Task<IReadOnlyList<int>> GetAccessibleWarehouseIdsAsync(CancellationToken cancellationToken=default)=>Task.FromResult<IReadOnlyList<int>>([warehouseId]);
        public Task<bool> CanAccessWarehouseAsync(int id,CancellationToken cancellationToken=default)=>Task.FromResult(id==warehouseId);
        public Task EnsureWarehouseAccessAsync(int id,CancellationToken cancellationToken=default)=>id==warehouseId?Task.CompletedTask:throw new UnauthorizedAccessException();
    }

    private sealed class TestContext(DbContextOptions<ErpKhoDbContext> options):ErpKhoDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken=default)
        {
            foreach(var entry in ChangeTracker.Entries().Where(x=>(x.Entity is PurchaseOrder or Asn) && (x.State is EntityState.Added or EntityState.Modified)))
                entry.Property("RowVersion").CurrentValue=Guid.NewGuid().ToByteArray();
            return base.SaveChangesAsync(cancellationToken);
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach(var property in modelBuilder.Model.GetEntityTypes().SelectMany(x=>x.GetProperties()))
                if(property.GetColumnType()?.Contains("max",StringComparison.OrdinalIgnoreCase)==true) property.SetColumnType(null);
            modelBuilder.Entity<PurchaseOrder>().Property(x=>x.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
            modelBuilder.Entity<Asn>().Property(x=>x.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
        }
    }
}
