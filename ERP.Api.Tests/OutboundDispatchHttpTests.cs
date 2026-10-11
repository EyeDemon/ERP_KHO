using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ERP.Api.Infrastructure;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ERP.Api.Tests;

public sealed class OutboundDispatchHttpTests
{
    [ApprovalSqlServerFact]
    public async Task AdditiveMigrationsPreserveLegacyQuantitiesAndLeaveHistoricalUomUnknown()
    {
        await using var f=await Fixture.Create();
        var migrator=f.Db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261001045923_AddAccountSecurityAggregateConcurrency");
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT ExportReceipts(Code,WarehouseId,Status,CreatedBy,CreatedAt) VALUES('LEGACY-OUT',{f.Warehouse},3,{f.Maker},SYSUTCDATETIME());
            DECLARE @receipt int=SCOPE_IDENTITY();
            INSERT ExportReceiptDetails(ExportReceiptId,ProductId,Quantity,UnitPrice) VALUES(@receipt,{f.Product},4,7);
            INSERT InventoryTransactions(ProductId,WarehouseId,InventoryStatus,TransactionType,Quantity,ReferenceType,ReferenceId,TransactionDate,CreatedBy)
                VALUES({f.Product},{f.Warehouse},0,1,4,'ExportReceipt',@receipt,SYSUTCDATETIME(),{f.Maker});
            INSERT ExportReceipts(Code,WarehouseId,Status,DispatchMode,CreatedBy,ApprovedBy,CreatedAt,ApprovedAt)
            VALUES('LEGACY-RESERVED',{f.Warehouse},1,0,{f.Maker},{f.Checker},SYSUTCDATETIME(),SYSUTCDATETIME());
            DECLARE @reserved int=SCOPE_IDENTITY();
            INSERT ExportReceiptDetails(ExportReceiptId,ProductId,Quantity,UnitPrice) VALUES(@reserved,{f.Product},7,7);
            INSERT StockReservations(ReservationCode,ProductId,WarehouseId,Quantity,ConsumedQuantity,ReleasedQuantity,Status,SourceType,SourceId,SourceCode,CreatedAt,CreatedBy,ExpiresAt)
            VALUES('LEGACY-HOLD',{f.Product},{f.Warehouse},7,0,0,0,'ExportReceipt',@reserved,'LEGACY-RESERVED',SYSUTCDATETIME(),{f.Checker},DATEADD(day,1,SYSUTCDATETIME()));
            UPDATE InventoryStocks SET ReservedQuantity=7 WHERE ProductId={f.Product} AND LocationId={f.LocationA};
            INSERT Roles(RoleName,Description) VALUES('UnknownOutboundRole','QA');
            """);
        await migrator.MigrateAsync("20261005143000_AddOutboundReceiptPermissions");
        Assert.Equal((100m,7m,1),await f.Stock());
        await migrator.MigrateAsync();
        f.Db.ChangeTracker.Clear();
        var legacy=await f.Db.ExportReceiptDetails.AsNoTracking().SingleAsync(x=>x.ExportReceipt.Code=="LEGACY-OUT");
        Assert.Null(legacy.BaseUomIdSnapshot);Assert.Null(legacy.BaseUomCodeSnapshot);Assert.Null(legacy.BaseUomPrecisionSnapshot);
        Assert.Equal(4,legacy.Quantity);Assert.Equal((100m,7m,1),await f.Stock());
        Assert.Equal(8,(await f.Db.ExportReceipts.AsNoTracking().FirstAsync()).RowVersion.Length);
        Assert.False(await f.Db.RolePermissions.AnyAsync(x=>x.Role.RoleName=="UnknownOutboundRole"));
        foreach(var (name,count) in new[]{("Admin",6),("Manager",6),("WarehouseStaff",4),("Viewer",1)})
            Assert.Equal(count,await f.Db.RolePermissions.CountAsync(x=>x.Role.RoleName==name&&x.Permission.Code.StartsWith("export_receipt.")));
        await migrator.MigrateAsync("20261001045923_AddAccountSecurityAggregateConcurrency");
        Assert.Equal((100m,7m,1),await f.Stock());
        await migrator.MigrateAsync();Assert.Equal((100m,7m,1),await f.Stock());
        var reservation=await f.Db.StockReservations.AsNoTracking().SingleAsync();
        Assert.Equal(7,reservation.Quantity);Assert.Equal(0,reservation.ConsumedQuantity);Assert.Equal(0,reservation.ReleasedQuantity);
        Assert.Equal(StockReservationStatus.Active,reservation.Status);
        Assert.Equal(ReceiptStatus.Approved,(await f.Db.ExportReceipts.AsNoTracking().SingleAsync(x=>x.Code=="LEGACY-RESERVED")).Status);
    }

    [ApprovalSqlServerFact]
    public async Task RemoteUpgradeRepairsOnlyImplicitStaffGrantAndPreservesAdministeredProvenance()
    {
        await using var f=await Fixture.Create(); var migrator=f.Db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261005143000_AddOutboundReceiptPermissions");
        await f.Db.Database.ExecuteSqlRawAsync("""
            INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
            SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL FROM Roles r CROSS JOIN Permissions p
            WHERE r.RoleName='WarehouseStaff' AND p.Code='export_receipt.cancel';
            INSERT Roles(RoleName,Description) VALUES(' WarehouseStaff ',N'Explicit administration fixture');
            DECLARE @role int=SCOPE_IDENTITY(),@actor int=(SELECT MIN(Id) FROM Users WHERE IsActive=1);
            INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
            SELECT @role,Id,SYSUTCDATETIME(),NULL FROM Permissions WHERE Code='export_receipt.cancel';
            INSERT Roles(RoleName,Description) VALUES('  WarehouseStaff',N'Actor provenance fixture');
            DECLARE @actorRole int=SCOPE_IDENTITY();
            INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
            SELECT @actorRole,Id,SYSUTCDATETIME(),@actor FROM Permissions WHERE Code='export_receipt.cancel';
            INSERT AuditLogs(UserId,Action,EntityName,EntityId,NewValues,Timestamp)
            VALUES(@actor,'Permission.Grant','Role',@role,'{{"permissionCode":"export_receipt.cancel","granted":true}}',SYSUTCDATETIME());
            INSERT AuditLogs(UserId,Action,EntityName,EntityId,NewValues,Timestamp)
            SELECT @actor,'Permission.Grant','Role',Id,'{{"permissionCode":"export_receipt.cancel","granted":true}}',DATEADD(day,-1,SYSUTCDATETIME())
            FROM Roles WHERE RoleName='WarehouseStaff';
            """);
        var before=await f.Stock(); await migrator.MigrateAsync(); f.Db.ChangeTracker.Clear();
        Assert.False(await f.Db.RolePermissions.AnyAsync(x=>x.Role.RoleName=="WarehouseStaff"&&x.Permission.Code=="export_receipt.cancel"));
        Assert.True(await f.Db.RolePermissions.AnyAsync(x=>x.Role.RoleName==" WarehouseStaff "&&x.Permission.Code=="export_receipt.cancel"&&x.GrantedByUserId==null));
        Assert.True(await f.Db.RolePermissions.AnyAsync(x=>x.Role.RoleName=="  WarehouseStaff"&&x.Permission.Code=="export_receipt.cancel"&&x.GrantedByUserId!=null));
        Assert.Equal(2,await f.Db.AuditLogs.CountAsync(x=>x.Action=="Permission.Grant")); Assert.Equal(before,await f.Stock());
        var error=await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(()=>migrator.MigrateAsync("20261005143000_AddOutboundReceiptPermissions"));
        Assert.Equal(51013,error.Number); Assert.Contains("20261007074831_ReconcileOutboundDispatchMvp",await f.Db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(before,await f.Stock());
    }

    [ApprovalSqlServerFact]
    public async Task WarehouseStaffCreateUsesExplicitGrantAndRevocationAppliesOnNextRequest()
    {
        await using var f=await Fixture.Create();
        object Payload()=>new{code="STAFF-"+Guid.NewGuid().ToString("N"),warehouseId=f.Warehouse,
            details=new[]{new{productId=f.Product,quantity=1m,unitPrice=1m}}};
        using var allowed=await f.Send(HttpMethod.Post,"/api/exportreceipts",f.Dispatcher,Payload());
        Assert.Equal(HttpStatusCode.Created,allowed.StatusCode);
        await f.Grant(f.Dispatcher,"export_receipt.create",false);
        var before=await f.Counts();var receipts=await f.Db.ExportReceipts.CountAsync();
        using var denied=await f.Send(HttpMethod.Post,"/api/exportreceipts",f.Dispatcher,Payload());
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        Assert.Equal(before,await f.Counts());Assert.Equal(receipts,await f.Db.ExportReceipts.CountAsync());
        Assert.Equal((100m,0m,0),await f.Stock());
    }

    [ApprovalSqlServerFact]
    public async Task LocationLedgerDowngradeFailsAtomicallyWithoutADataPlan()
    {
        await using var f=await Fixture.Create();var id=await f.CreateReceipt(75);
        Assert.Equal(HttpStatusCode.OK,(await f.Command(id,"approve-and-reserve",f.Checker,await f.Read(id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await f.Command(id,"dispatch",f.Dispatcher,await f.Read(id))).StatusCode);
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(()=>f.Db.GetService<IMigrator>().MigrateAsync("20261005143000_AddOutboundReceiptPermissions"));
        Assert.Contains("20261007074831_ReconcileOutboundDispatchMvp",await f.Db.Database.GetAppliedMigrationsAsync());
        Assert.Equal((25m,0m,2),await f.Stock());
    }

    [ApprovalSqlServerFact]
    public async Task InvalidBaseQuantityAndIneligibleLocationsCannotChangeInventoryOrClaims()
    {
        await using var f = await Fixture.Create();
        foreach (var kind in new[] { "receiving", "damaged", "rejected", "blocked", "inactive", "nonpickable" })
        {
            f.Db.InventoryStocks.Add(new()
            {
                ProductId=f.Product,WarehouseId=f.Warehouse,Quantity=100,
                Status=kind=="damaged"?InventoryStatus.Damaged:kind=="rejected"?InventoryStatus.Rejected:InventoryStatus.Available,
                Location=new(){WarehouseId=f.Warehouse,Code=kind,Name="Vị trí kiểm thử",CreatedBy=f.Maker,
                    LocationType=kind=="receiving"?WarehouseLocationType.Receiving:WarehouseLocationType.Storage,
                    IsActive=kind!="inactive",IsBlocked=kind=="blocked",IsPickable=kind!="nonpickable"}
            });
        }
        await f.Db.SaveChangesAsync();
        var before=await f.Counts();
        foreach (var quantity in new[]{0m,-1m,1.00001m,101m})
        {
            using var result=await f.Send(HttpMethod.Post,"/api/exportreceipts",f.Maker,new{code="BAD-"+Guid.NewGuid().ToString("N"),warehouseId=f.Warehouse,
                details=new[]{new{productId=f.Product,quantity,unitPrice=1}}});
            Assert.Equal(HttpStatusCode.BadRequest,result.StatusCode);
            Assert.Equal(before,await f.Counts());
        }
        var id=await f.CreateReceipt(100);var token=await f.Read(id);
        Assert.Equal(HttpStatusCode.OK,(await f.Command(id,"approve-and-reserve",f.Checker,token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await f.Command(id,"dispatch",f.Dispatcher,await f.Read(id))).StatusCode);
        Assert.Equal((600m,0m,2),await f.Stock());
        Assert.All(await f.Db.InventoryTransactions.AsNoTracking().ToListAsync(),x=>Assert.Equal(InventoryStatus.Available,x.InventoryStatus));
        Assert.All(await f.Db.InventoryStocks.Include(x=>x.Location).Where(x=>x.Location!.Code!="LEGACY"&&x.Location.Code!="B").ToListAsync(),x=>Assert.Equal(100m,x.Quantity));
    }

    [ApprovalSqlServerFact]
    public async Task PartiallyReleasedHoldCannotBeDispatchedAsACompleteReceipt()
    {
        await using var f=await Fixture.Create();var id=await f.CreateReceipt(10);
        Assert.Equal(HttpStatusCode.OK,(await f.Command(id,"approve-and-reserve",f.Checker,await f.Read(id))).StatusCode);
        await f.Db.StockReservations.Where(x=>x.SourceId==id&&x.SourceType=="ExportReceipt")
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ReleasedQuantity,1m).SetProperty(x=>x.Status,StockReservationStatus.PartiallyConsumed));
        await f.Db.InventoryStocks.Where(x=>x.LocationId==f.LocationA).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ReservedQuantity,9m));
        var before=await f.Counts();
        Assert.Equal(HttpStatusCode.Conflict,(await f.Command(id,"dispatch",f.Dispatcher,await f.Read(id))).StatusCode);
        Assert.Equal(before,await f.Counts());Assert.Equal((100m,9m,0),await f.Stock());
        Assert.Equal(ReceiptStatus.Approved,(await f.Db.ExportReceipts.AsNoTracking().SingleAsync(x=>x.Id==id)).Status);
    }

    [ApprovalSqlServerFact]
    public async Task ReserveThenDispatchUsesStableSnapshotAndLocationLedger_ReauthorizesReplay()
    {
        await using var f = await Fixture.Create();
        var receipt = await f.CreateReceipt(75);
        var original = await f.Read(receipt);
        var holdKey = Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.OK, (await f.Command(receipt, "approve-and-reserve", f.Checker, original, holdKey)).StatusCode);
        Assert.Equal((100m, 75m, 0), await f.Stock());
        var held = await f.Read(receipt);
        var unit = await f.Db.Units.SingleAsync(); unit.Code = "CHANGED"; unit.Name = "Đơn vị mới"; unit.DecimalPlaces = 0;
        await f.Db.SaveChangesAsync();
        var detail = await f.ReadJson(receipt, f.Checker);
        Assert.Equal("IDEM_UNIT", detail.GetProperty("details")[0].GetProperty("baseUomCodeSnapshot").GetString());
        Assert.Equal(4, detail.GetProperty("details")[0].GetProperty("baseUomPrecisionSnapshot").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await f.Command(receipt, "approve-and-reserve", f.Checker, original, holdKey)).StatusCode);
        var dispatchKey = Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.OK, (await f.Command(receipt, "dispatch", f.Dispatcher, held, dispatchKey)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.Command(receipt, "dispatch", f.Dispatcher, held, dispatchKey)).StatusCode);
        Assert.Equal((25m, 0m, 2), await f.Stock());
        var ledger = await f.Db.InventoryTransactions.AsNoTracking().Where(x => x.ReferenceType == "ExportReceipt" && x.ReferenceId == receipt).ToListAsync();
        Assert.Equal(75, ledger.Sum(x => x.Quantity)); Assert.All(ledger, x => Assert.NotNull(x.LocationId));
        foreach (var row in ledger) Assert.Equal(row.LocationId == f.LocationA ? 40m : 35m, row.Quantity);
        Assert.Equal(1, await f.Db.AuditLogs.CountAsync(x => x.EntityId == receipt && x.Action == "ExportReceipt.Dispatched"));
        Assert.Equal(HttpStatusCode.Conflict, (await f.Command(receipt, "dispatch", f.Dispatcher, "changed", dispatchKey)).StatusCode);
        var before = await f.Counts();
        await f.Grant(f.Dispatcher, "export_receipt.dispatch", false);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Command(receipt, "dispatch", f.Dispatcher, held, dispatchKey)).StatusCode);
        Assert.Equal(before, await f.Counts());
        await f.Grant(f.Dispatcher, "export_receipt.dispatch", true);
        await f.Db.UserWarehouses.Where(x => x.UserId == f.Dispatcher).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await f.Command(receipt, "dispatch", f.Dispatcher, held, dispatchKey)).StatusCode);
        Assert.Equal(before, await f.Counts());
    }

    [ApprovalSqlServerFact]
    public async Task NamedReserveIgnoresLegacyMode_SeparationAndBlockedCancellationHaveNoPhysicalEffect()
    {
        await using var f = await Fixture.Create(immediateLegacy: true);
        var id = await f.CreateReceipt(30); var version = await f.Read(id);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Command(id, "approve-and-reserve", f.Maker, version)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.Command(id, "approve-and-reserve", f.Checker, version)).StatusCode);
        Assert.Equal((100m,30m,0), await f.Stock());
        var held = await f.Read(id);
        var before = await f.Counts();
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Command(id,"dispatch",f.Checker,held)).StatusCode);
        Assert.Equal(before, await f.Counts());
        await f.Db.WarehouseLocations.Where(x => x.Id == f.LocationA).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsBlocked,true));
        Assert.Equal(HttpStatusCode.OK,(await f.Command(id,"cancel",f.Checker,held)).StatusCode);
        Assert.Equal((100m,0m,0),await f.Stock());
        Assert.Equal(HttpStatusCode.Conflict,(await f.Command(id,"dispatch",f.Dispatcher,held)).StatusCode);
        Assert.Equal(ReceiptStatus.Cancelled, (await f.Db.ExportReceipts.AsNoTracking().SingleAsync(x=>x.Id==id)).Status);
    }

    [ApprovalSqlServerFact]
    public async Task MissingIndependentCapabilitiesAndViewerFilteringCannotBeBypassedByStaleAdminClaim()
    {
        await using var f = await Fixture.Create(); var id=await f.CreateReceipt(10); var version=await f.Read(id);
        await f.Db.Roles.Where(x=>x.RoleName=="Viewer").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RoleName," viewer "));
        var raw=await f.ReadJson(id,f.Viewer);
        Assert.False(raw.TryGetProperty("rowVersion",out _));
        Assert.False(raw.GetProperty("details")[0].TryGetProperty("unitPrice",out _));
        Assert.Equal(JsonValueKind.Null,raw.GetProperty("note").ValueKind);
        var before=await f.Counts();
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Command(id,"approve-and-reserve",f.Dispatcher,version)).StatusCode);
        await f.Grant(f.Checker,"export_receipt.dispatch",false);
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Command(id,"approve-and-dispatch",f.Checker,version)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await f.Command(id,"approve-and-reserve",f.Checker,version)).StatusCode);
        Assert.Equal((100m,10m,0),await f.Stock());
        Assert.Equal(before.Claims+1,(await f.Counts()).Claims);
        await f.Db.UserWarehouses.Where(x=>x.UserId==f.Viewer).ExecuteDeleteAsync();
        using var denied=await f.Send(HttpMethod.Get,$"/api/exportreceipts/{id}",f.Viewer);
        Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
        Assert.DoesNotContain("QA_PRIVATE",await denied.Content.ReadAsStringAsync());
    }

    [ApprovalSqlServerFact]
    public async Task ApprovalCenterFiltersBeforeCount_RejectNeedsBothCapabilitiesAndMakerSeparation()
    {
        await using var f=await Fixture.Create(); var first=await f.CreateReceipt(10); await f.CreateReceipt(10);
        var customer=new BusinessPartner {Code="OUT-CUSTOMER",Name="Khách hàng ban đầu",IsCustomer=true,IsActive=true};
        f.Db.BusinessPartners.Add(customer);await f.Db.SaveChangesAsync();
        using(var assigned=await f.Send(HttpMethod.Put,$"/api/exportreceipts/{first}/customer",f.Maker,new {partnerId=customer.Id,rowVersion=await f.Read(first)}))
            Assert.Equal(HttpStatusCode.NoContent,assigned.StatusCode);
        f.Db.Stocktakes.Add(new Stocktake{Code="MIXED",WarehouseId=f.Warehouse,CreatedBy=f.Maker}); await f.Db.SaveChangesAsync();
        using var all=await f.Send(HttpMethod.Get,"/api/approvals/queue?pageSize=1",f.Checker);
        Assert.Equal(3,(await all.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalRecords").GetInt32());
        await f.Grant(f.Checker,"export_receipt.read",false);
        using var filtered=await f.Send(HttpMethod.Get,"/api/approvals/queue?pageSize=1",f.Checker);
        Assert.Equal(1,(await filtered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalRecords").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Get,$"/api/approvals/ExportReceipt/{first}",f.Checker)).StatusCode);
        await f.Grant(f.Checker,"export_receipt.cancel",false);
        var before=await f.Counts();
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,$"/api/approvals/ExportReceipt/{first}/reject",f.Checker,new{reason="Không tiếp tục xuất hàng"})).StatusCode);
        Assert.Equal(before,await f.Counts());
        await f.Grant(f.Checker,"export_receipt.cancel",true);
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,$"/api/approvals/ExportReceipt/{first}/reject",f.Maker,new{reason="Không tiếp tục xuất hàng"})).StatusCode);
        var key=Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,$"/api/approvals/ExportReceipt/{first}/reject",f.Checker,new{reason="Không tiếp tục xuất hàng"},key)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,$"/api/approvals/ExportReceipt/{first}/reject",f.Checker,new{reason="Không tiếp tục xuất hàng"},key)).StatusCode);
        Assert.Equal(1,await f.Db.AuditLogs.CountAsync(x=>x.EntityId==first&&x.Action=="ApprovalRejected"));
        await f.Db.BusinessPartners.Where(x=>x.Id==customer.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Name,"Khách hàng đã đổi tên"));
        var cancelled=await f.ReadJson(first,f.Maker);
        Assert.Equal("OUT-CUSTOMER",cancelled.GetProperty("customerCode").GetString());
        Assert.Equal("Khách hàng ban đầu",cancelled.GetProperty("customerName").GetString());
        Assert.Equal((100m,0m,0),await f.Stock());
    }

    [ApprovalSqlServerFact]
    public async Task CompetingReservationsThenDispatchCancelRaceHaveOneWinnerAndNoPartialEffect()
    {
        await using var f=await Fixture.Create(); var a=await f.CreateReceipt(80); var b=await f.CreateReceipt(80);
        var av=await f.Read(a);var bv=await f.Read(b);
        await using(var held=f.Context())
        {
            await using var tx=await held.Database.BeginTransactionAsync();
            await held.WarehouseLocations.FromSqlInterpolated($"SELECT * FROM WarehouseLocations WITH (UPDLOCK,HOLDLOCK) WHERE WarehouseId={f.Warehouse}").AsNoTracking().ToListAsync();
            var first=f.Command(a,"approve-and-reserve",f.Checker,av); var second=f.Command(b,"approve-and-reserve",f.Checker,bv);
            await Task.Delay(250); Assert.False(first.IsCompleted);Assert.False(second.IsCompleted);
            await tx.CommitAsync(); var results=await Task.WhenAll(first,second).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Single(results,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(results,x=>x.StatusCode==HttpStatusCode.Conflict);
        }
        Assert.Equal((100m,80m,0),await f.Stock());
        var winner=await f.Db.ExportReceipts.AsNoTracking().SingleAsync(x=>x.Status==ReceiptStatus.Approved);var token=Convert.ToBase64String(winner.RowVersion);
        await using(var held=f.Context())
        {
            await using var tx=await held.Database.BeginTransactionAsync();
            await held.Database.SqlQuery<int>($"SELECT Id AS Value FROM ExportReceipts WITH (UPDLOCK,HOLDLOCK) WHERE Id={winner.Id}").ToListAsync();
            var first=f.Command(winner.Id,"dispatch",f.Dispatcher,token); var second=f.Command(winner.Id,"cancel",f.Checker,token);
            await Task.Delay(250);Assert.False(first.IsCompleted);Assert.False(second.IsCompleted);await tx.CommitAsync();
            var results=await Task.WhenAll(first,second).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Single(results,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(results,x=>x.StatusCode==HttpStatusCode.Conflict);
        }
        var stock=await f.Stock();Assert.Equal(0,stock.Reserved);Assert.True(stock.OnHand==20||stock.OnHand==100);
        Assert.Equal(stock.OnHand==20?80m:0m,await f.Db.InventoryTransactions.SumAsync(x=>x.Quantity));
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        private readonly SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase owned;
        private readonly WebApplicationFactory<Program> factory;
        private readonly HttpClient client;
        public ErpKhoDbContext Db { get; }
        public int Maker,Checker,Dispatcher,Viewer,Warehouse,Product,LocationA;
        private Fixture(SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase owned, bool immediateLegacy)
        {
            this.owned=owned; Db=Context();factory=ApprovalHttpIntegrationTests.Factory(owned.ConnectionString);
            if (immediateLegacy) factory=factory.WithWebHostBuilder(builder=>builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{
                ["ExportReceipt:DefaultDispatchMode"]="DispatchOnApproval",["ExportReceipt:AllowPerReceiptDispatchMode"]="false"})));
            client=factory.CreateClient(new(){BaseAddress=new Uri("https://localhost")});
        }
        public ErpKhoDbContext Context()=>new(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(owned.ConnectionString).Options);
        public static async Task<Fixture> Create(bool immediateLegacy=false)
        {
            var owned=await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
            try
            {
                await owned.MigrateAndSeedAsync();var f=new Fixture(owned, immediateLegacy);
                var users=await f.Db.Users.OrderBy(x=>x.Id).ToArrayAsync(); f.Maker=users[0].Id;f.Checker=users[1].Id;
                var staff=new User{Username="dispatch",PasswordHash="QA_NOT_A_LOGIN",RoleId=(await f.Db.Roles.SingleAsync(x=>x.RoleName=="WarehouseStaff")).Id};
                var viewer=new User{Username="reader",PasswordHash="QA_NOT_A_LOGIN",RoleId=(await f.Db.Roles.SingleAsync(x=>x.RoleName=="Viewer")).Id};
                f.Db.AddRange(staff,viewer);await f.Db.SaveChangesAsync();f.Dispatcher=staff.Id;f.Viewer=viewer.Id;
                f.Warehouse=(await f.Db.Warehouses.SingleAsync()).Id;f.Product=(await f.Db.Products.SingleAsync()).Id;
                foreach(var actor in new[]{f.Checker,f.Dispatcher,f.Viewer}) f.Db.UserWarehouses.Add(new(){UserId=actor,WarehouseId=f.Warehouse,CreatedBy=f.Maker});
                var existing=await f.Db.InventoryStocks.SingleAsync();existing.Quantity=40;f.LocationA=existing.LocationId!.Value;
                f.Db.InventoryStocks.Add(new(){ProductId=f.Product,WarehouseId=f.Warehouse,Quantity=60,Location=new(){WarehouseId=f.Warehouse,Code="B",Name="Vị trí B",LocationType=WarehouseLocationType.Storage,IsPickable=true,CreatedBy=f.Maker}});
                await f.Db.SaveChangesAsync();return f;
            }
            catch{await owned.DisposeAsync();throw;}
        }
        public async Task<HttpResponseMessage> Send(HttpMethod method,string url,int actor,object? body=null,string? key=null)
        {
            using var req=new HttpRequestMessage(method,url);req.Headers.Add("X-Test-Actor",actor.ToString());req.Headers.Add("X-Test-Role","Admin");
            if(body is not null)req.Content=JsonContent.Create(body);if(method!=HttpMethod.Get)req.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString("N"));
            return await client.SendAsync(req);
        }
        public async Task<int> CreateReceipt(decimal quantity)
        {
            using var response=await Send(HttpMethod.Post,"/api/exportreceipts",Maker,new{code="OUT-"+Guid.NewGuid().ToString("N"),warehouseId=Warehouse,note="QA_PRIVATE",details=new[]{new{productId=Product,quantity,unitPrice=7,note="QA_PRIVATE"}}});
            Assert.Equal(HttpStatusCode.Created,response.StatusCode);return(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        }
        public async Task<JsonElement> ReadJson(int id,int actor){using var r=await Send(HttpMethod.Get,$"/api/exportreceipts/{id}",actor);Assert.Equal(HttpStatusCode.OK,r.StatusCode);return await r.Content.ReadFromJsonAsync<JsonElement>();}
        public async Task<string> Read(int id)=>(await ReadJson(id,Checker)).GetProperty("rowVersion").GetString()!;
        public Task<HttpResponseMessage> Command(int id,string action,int actor,string version,string? key=null)=>Send(HttpMethod.Post,$"/api/exportreceipts/{id}/{action}",actor,new{rowVersion=version},key);
        public async Task Grant(int actor,string code,bool grant)
        {
            var role=(await Db.Users.AsNoTracking().SingleAsync(x=>x.Id==actor)).RoleId;var permission=(await Db.Permissions.SingleAsync(x=>x.Code==code)).Id;
            if(grant){Db.RolePermissions.Add(new(){RoleId=role,PermissionId=permission});await Db.SaveChangesAsync();}
            else await Db.RolePermissions.Where(x=>x.RoleId==role&&x.PermissionId==permission).ExecuteDeleteAsync();
        }
        public async Task<(decimal OnHand,decimal Reserved,int Ledger)> Stock()=>(await Db.InventoryStocks.SumAsync(x=>x.Quantity),await Db.InventoryStocks.SumAsync(x=>x.ReservedQuantity),await Db.InventoryTransactions.CountAsync());
        public async Task<(int Audits,int Claims)> Counts()=>(await Db.AuditLogs.CountAsync(),await Db.IdempotencyRecords.CountAsync());
        public async ValueTask DisposeAsync(){client.Dispose();await factory.DisposeAsync();await Db.DisposeAsync();await owned.DisposeAsync();}
    }
}
