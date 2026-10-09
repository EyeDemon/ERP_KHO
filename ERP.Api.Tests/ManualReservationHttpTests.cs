using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Api.Controllers;
using ERP.Api.Infrastructure;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Fixture = ERP.Api.Tests.OutboundDispatchHttpTests.Fixture;

namespace ERP.Api.Tests;

public sealed class ManualReservationHttpTests
{
    [Fact]
    public void EveryExistingActionHasExactPermissionAndMutationIdempotency()
    {
        var expected = new Dictionary<string,string> { ["GetPage"]="reservation.read",["Get"]="reservation.read",["Create"]="reservation.create",["Release"]="reservation.release",["Expire"]="reservation.release",["Reconciliation"]="reservation.read" };
        foreach (var (name, code) in expected)
        {
            var action = typeof(StockReservationsController).GetMethod(name)!;
            Assert.Equal(code, Assert.Single(action.GetCustomAttributes(typeof(PermissionAuthorizeAttribute),true).Cast<PermissionAuthorizeAttribute>()).Arguments![0]);
            Assert.Empty(action.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute),true));
            if (name is "Create" or "Release" or "Expire") Assert.Single(action.GetCustomAttributes(typeof(IdempotentCommandAttribute),true));
        }
    }

    [ApprovalSqlServerFact]
    public async Task DeterministicBundlesUnknownRoleAndLegacySnapshots()
    {
        await using var f=await Fixture.Create();
        foreach(var (role, count) in new[]{("Admin",3),("Manager",3),("WarehouseStaff",2),("Viewer",1)})
            Assert.Equal(count,await f.Db.RolePermissions.CountAsync(x=>x.Role.RoleName==role&&x.Permission.Code.StartsWith("reservation.")));
        var unknown=new Role{RoleName="ManualUnknown"};f.Db.Roles.Add(unknown);await f.Db.SaveChangesAsync();
        Assert.False(await f.Db.RolePermissions.AnyAsync(x=>x.RoleId==unknown.Id));
        var migrator=f.Db.GetService<IMigrator>();await migrator.MigrateAsync("20261007074831_ReconcileOutboundDispatchMvp");
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT StockReservations(ReservationCode,ProductId,WarehouseId,Quantity,ConsumedQuantity,ReleasedQuantity,Status,SourceType,CreatedAt,CreatedBy,ExpiresAt)
            VALUES('LEGACY-MANUAL',{f.Product},{f.Warehouse},2,0,0,0,'Manual',SYSUTCDATETIME(),{f.Maker},DATEADD(day,1,SYSUTCDATETIME()));
            """);
        var before=await f.Stock();await migrator.MigrateAsync();f.Db.ChangeTracker.Clear();
        var legacy=await f.Db.StockReservations.AsNoTracking().SingleAsync();Assert.Null(legacy.BaseUomIdSnapshot);Assert.Null(legacy.BaseUomCodeSnapshot);Assert.Equal(8,legacy.RowVersion.Length);Assert.Equal(before,await f.Stock());
        await migrator.MigrateAsync("20261007074831_ReconcileOutboundDispatchMvp");await migrator.MigrateAsync();Assert.Equal(before,await f.Stock());
        await Create(f,f.Checker,1m);
        var error=await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(()=>migrator.MigrateAsync("20261007074831_ReconcileOutboundDispatchMvp"));
        Assert.Equal(51014,error.Number);Assert.Contains("20261009015806_AddManualReservationMvp",await f.Db.Database.GetAppliedMigrationsAsync());
    }

    [ApprovalSqlServerFact]
    public async Task StaffCreateReadViewerFilteringAndNextRequestRevocation()
    {
        await using var f=await Fixture.Create();var created=await Create(f,f.Dispatcher,5m);var id=created.GetProperty("id").GetInt32();
        Assert.False(created.TryGetProperty("rowVersion",out _));
        using(var viewer=await f.Send(HttpMethod.Get,"/api/stock-reservations/"+id,f.Viewer))
        {
            Assert.Equal(HttpStatusCode.OK,viewer.StatusCode);var raw=await viewer.Content.ReadAsStringAsync();
            using var json=JsonDocument.Parse(raw);
            foreach(var property in new[]{"rowVersion","unitPrice","cost","value","releaseReason","idempotencyKeyHash","requestFingerprint","passwordHash","stackTrace"}) Assert.False(json.RootElement.TryGetProperty(property,out _));
        }
        var before=await f.Counts();
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/stock-reservations/"+id+"/release",f.Dispatcher,new{reason="Giải phóng"})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/stock-reservations/expire",f.Dispatcher,new{})).StatusCode);
        await f.Grant(f.Dispatcher,"reservation.create",false);
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/stock-reservations",f.Dispatcher,new{productId=f.Product,warehouseId=f.Warehouse,quantity=1})).StatusCode);
        Assert.Equal(before,await f.Counts());Assert.Equal((100m,5m,0),await f.Stock());
    }

    [ApprovalSqlServerFact]
    public async Task CreationReplayFiltersTokenUsingCurrentGrantsAndViewerClassification()
    {
        await using var f=await Fixture.Create();var key=Guid.NewGuid().ToString("N");
        var payload=new{productId=f.Product,warehouseId=f.Warehouse,quantity=1m};
        using var first=await f.Send(HttpMethod.Post,"/api/stock-reservations",f.Checker,payload,key);
        Assert.Equal(HttpStatusCode.Created,first.StatusCode);
        var original=await first.Content.ReadFromJsonAsync<JsonElement>();Assert.True(original.TryGetProperty("rowVersion",out _));
        var before=await f.Counts();var stock=await f.Stock();
        await f.Grant(f.Checker,"reservation.release",false);
        using var noRelease=await f.Send(HttpMethod.Post,"/api/stock-reservations",f.Checker,payload,key);
        Assert.Equal(HttpStatusCode.Created,noRelease.StatusCode);
        Assert.False((await noRelease.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("rowVersion",out _));
        await f.Grant(f.Checker,"reservation.release",true);
        using var regranted=await f.Send(HttpMethod.Post,"/api/stock-reservations",f.Checker,payload,key);
        Assert.Equal(HttpStatusCode.Created,regranted.StatusCode);
        Assert.Equal(original.GetProperty("rowVersion").GetString(),(await regranted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rowVersion").GetString());
        await f.Grant(f.Viewer,"reservation.create",true);await f.Grant(f.Viewer,"reservation.release",true);
        var maker=await f.Db.Users.SingleAsync(x=>x.Id==f.Checker);maker.RoleId=(await f.Db.Users.AsNoTracking().SingleAsync(x=>x.Id==f.Viewer)).RoleId;await f.Db.SaveChangesAsync();
        using var viewer=await f.Send(HttpMethod.Post,"/api/stock-reservations",f.Checker,payload,key);
        Assert.Equal(HttpStatusCode.Created,viewer.StatusCode);
        var filtered=await viewer.Content.ReadFromJsonAsync<JsonElement>();Assert.False(filtered.TryGetProperty("rowVersion",out _));Assert.Equal(original.GetProperty("id").GetInt32(),filtered.GetProperty("id").GetInt32());
        Assert.Equal(before,await f.Counts());Assert.Equal(stock,await f.Stock());
        Assert.Equal(1,await f.Db.StockReservations.CountAsync());
        using var stored=JsonDocument.Parse((await f.Db.IdempotencyRecords.AsNoTracking().SingleAsync()).ResponseBody!);
        Assert.True(stored.RootElement.TryGetProperty("rowVersion",out _));
    }

    [ApprovalSqlServerFact]
    public async Task SnapshotSurvivesMasterChangeAndPrecisionNeverRounds()
    {
        await using var f=await Fixture.Create();var item=await Create(f,f.Checker,2.5m);var id=item.GetProperty("id").GetInt32();
        Assert.Equal("IDEM_UNIT",item.GetProperty("baseUomCodeSnapshot").GetString());Assert.Equal(4,item.GetProperty("baseUomPrecisionSnapshot").GetInt32());
        var unit=await f.Db.Units.SingleAsync();unit.Code="NEW";unit.Name="Đơn vị đã đổi";unit.DecimalPlaces=0;await f.Db.SaveChangesAsync();
        var before=await f.Counts();
        foreach(var qty in new[]{0m,-1m,1.00001m,0.5m})
        {
            using var invalid=await f.Send(HttpMethod.Post,"/api/stock-reservations",f.Checker,new{productId=f.Product,warehouseId=f.Warehouse,quantity=qty});
            Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);Assert.Equal(before,await f.Counts());
        }
        var payload=new{quantity=0.125m,reason="Giải phóng một phần",rowVersion=item.GetProperty("rowVersion").GetString()};
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,"/api/stock-reservations/"+id+"/release",f.Checker,payload)).StatusCode);
        var saved=await Read(f,f.Checker,id);Assert.Equal("IDEM_UNIT",saved.GetProperty("baseUomCodeSnapshot").GetString());Assert.Equal(2.375m,saved.GetProperty("remainingQuantity").GetDecimal());Assert.Equal((100m,2.375m,0),await f.Stock());
    }

    [ApprovalSqlServerFact]
    public async Task ReleaseStaleTokenReplayFingerprintAndRevokedAccessHaveNoDuplicateEffects()
    {
        await using var f=await Fixture.Create();var item=await Create(f,f.Checker,10m);var id=item.GetProperty("id").GetInt32();
        var url="/api/stock-reservations/"+id+"/release";var key=Guid.NewGuid().ToString("N");
        var dto=new{quantity=2m,reason="Giải phóng một phần",rowVersion=item.GetProperty("rowVersion").GetString()};
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,url,f.Checker,dto,key)).StatusCode);
        var before=await f.Counts();Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,url,f.Checker,dto,key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Post,url,f.Checker,new{quantity=3m,reason=dto.reason,rowVersion=dto.rowVersion},key)).StatusCode);
        foreach(var token in new[]{dto.rowVersion,null,"","broken","AAAAAAAAAAE="})
            Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Post,url,f.Checker,new{quantity=1m,reason=dto.reason,rowVersion=token})).StatusCode);
        await f.Grant(f.Checker,"reservation.release",false);
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,url,f.Checker,dto,key)).StatusCode);
        await f.Grant(f.Checker,"reservation.release",true);await f.Db.UserWarehouses.Where(x=>x.UserId==f.Checker).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.NotFound,(await f.Send(HttpMethod.Post,url,f.Checker,dto,key)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await f.Send(HttpMethod.Get,"/api/stock-reservations/"+id,f.Checker)).StatusCode);
        Assert.Equal(before,await f.Counts());Assert.Equal((100m,8m,0),await f.Stock());
        Assert.Equal(1,await f.Db.AuditLogs.CountAsync(x=>x.EntityId==id&&x.Action=="StockReservation.Released"));
    }

    [ApprovalSqlServerFact]
    public async Task ConcurrentReleaseSameTokenHasOneWinnerOneAuditAndNoPartialStockEffect()
    {
        await using var f=await Fixture.Create();var item=await Create(f,f.Checker,10m);var id=item.GetProperty("id").GetInt32();
        var dto=new{quantity=3m,reason="Giải phóng",rowVersion=item.GetProperty("rowVersion").GetString()};
        await using var held=f.Context();await using var tx=await held.Database.BeginTransactionAsync();
        await held.Database.SqlQuery<int>($"SELECT Id AS Value FROM StockReservations WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}").ToListAsync();
        var first=f.Send(HttpMethod.Post,"/api/stock-reservations/"+id+"/release",f.Checker,dto);var second=f.Send(HttpMethod.Post,"/api/stock-reservations/"+id+"/release",f.Checker,dto);
        await Task.Delay(250);Assert.False(first.IsCompleted);Assert.False(second.IsCompleted);await tx.CommitAsync();
        var outcomes=await Task.WhenAll(first,second).WaitAsync(TimeSpan.FromSeconds(30));Assert.Single(outcomes,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(outcomes,x=>x.StatusCode==HttpStatusCode.Conflict);
        Assert.Equal((100m,7m,0),await f.Stock());Assert.Equal(1,await f.Db.AuditLogs.CountAsync(x=>x.EntityId==id&&x.Action=="StockReservation.Released"));
        Assert.Equal(1,await f.Db.IdempotencyRecords.CountAsync(x=>x.CommandScope=="StockReservation.Release"));
    }

    [ApprovalSqlServerFact]
    public async Task CompetingManualCreateCannotOversell()
    {
        await using var f=await Fixture.Create();await using var held=f.Context();await using var tx=await held.Database.BeginTransactionAsync();
        await held.WarehouseLocations.FromSqlInterpolated($"SELECT * FROM WarehouseLocations WITH (UPDLOCK,HOLDLOCK) WHERE WarehouseId={f.Warehouse}").AsNoTracking().ToListAsync();
        var payload=new{productId=f.Product,warehouseId=f.Warehouse,quantity=80m};
        var first=f.Send(HttpMethod.Post,"/api/stock-reservations",f.Checker,payload);var second=f.Send(HttpMethod.Post,"/api/stock-reservations",f.Checker,payload);
        await Task.Delay(250);Assert.False(first.IsCompleted);Assert.False(second.IsCompleted);await tx.CommitAsync();
        var outcomes=await Task.WhenAll(first,second).WaitAsync(TimeSpan.FromSeconds(30));Assert.Single(outcomes,x=>x.StatusCode==HttpStatusCode.Created);Assert.Single(outcomes,x=>x.StatusCode==HttpStatusCode.Conflict);
        Assert.Equal((100m,80m,0),await f.Stock());Assert.Equal(1,await f.Db.StockReservations.CountAsync());Assert.Equal(1,await f.Db.AuditLogs.CountAsync(x=>x.Action=="StockReservation.Created"));
        Assert.Equal(1,await f.Db.IdempotencyRecords.CountAsync(x=>x.CommandScope=="StockReservation.Create"));
    }

    [ApprovalSqlServerFact]
    public async Task ExportHoldsAreReadOnlyAndManualExpiryDoesNotTouchThem()
    {
        await using var f=await Fixture.Create();var receipt=await f.CreateReceipt(20m);
        Assert.Equal(HttpStatusCode.OK,(await f.Command(receipt,"approve-and-reserve",f.Checker,await f.Read(receipt))).StatusCode);
        var export=await f.Db.StockReservations.AsNoTracking().SingleAsync();var before=await f.Counts();
        var url="/api/stock-reservations/"+export.Id+"/release";var key=Guid.NewGuid().ToString("N");var payload=new{reason="Không được phép",rowVersion=Convert.ToBase64String(export.RowVersion)};
        Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Post,url,f.Checker,payload,key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Post,url,f.Checker,payload,key)).StatusCode);Assert.Equal(before,await f.Counts());
        await f.Db.StockReservations.Where(x=>x.Id==export.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,DateTime.UtcNow.AddHours(-2)).SetProperty(x=>x.ExpiresAt,DateTime.UtcNow.AddHours(-1)));
        var manual=await Create(f,f.Checker,5m);var manualId=manual.GetProperty("id").GetInt32();
        Assert.Equal((100m,25m,0),await f.Stock()); // Creating Manual must not expire an ExportReceipt hold.
        await f.Db.StockReservations.Where(x=>x.Id==manualId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,DateTime.UtcNow.AddHours(-2)).SetProperty(x=>x.ExpiresAt,DateTime.UtcNow.AddHours(-1)));
        var expireKey=Guid.NewGuid().ToString("N");
        using var expired=await f.Send(HttpMethod.Post,"/api/stock-reservations/expire",f.Checker,new{},expireKey);Assert.Equal(HttpStatusCode.OK,expired.StatusCode);Assert.Equal(1,(await expired.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expired").GetInt32());
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,"/api/stock-reservations/expire",f.Checker,new{},expireKey)).StatusCode);
        Assert.Equal((100m,20m,0),await f.Stock());Assert.Equal(StockReservationStatus.Active,(await f.Db.StockReservations.AsNoTracking().SingleAsync(x=>x.Id==export.Id)).Status);
        var counts=await f.Counts();using var reconcile=await f.Send(HttpMethod.Get,"/api/stock-reservations/reconciliation",f.Viewer);Assert.Equal(HttpStatusCode.OK,reconcile.StatusCode);Assert.Equal(counts,await f.Counts());Assert.Equal((100m,20m,0),await f.Stock());
    }

    private static async Task<JsonElement> Create(Fixture f,int actor,decimal quantity)
    {
        using var result=await f.Send(HttpMethod.Post,"/api/stock-reservations",actor,new{productId=f.Product,warehouseId=f.Warehouse,quantity});
        Assert.Equal(HttpStatusCode.Created,result.StatusCode);return await result.Content.ReadFromJsonAsync<JsonElement>();
    }

    [ApprovalSqlServerFact]
    public async Task BulkExpiryScopesManualOnlyAndReauthorizesEveryOriginalWarehouse()
    {
        await using var f=await Fixture.Create();var warehouses=new List<int>{f.Warehouse};
        for(var i=0;i<4;i++)
        {
            var warehouse=new Warehouse{Code="MANUAL-W"+i,Name="Kho giữ hàng "+i};
            f.Db.InventoryStocks.Add(new(){ProductId=f.Product,Warehouse=warehouse,Quantity=100m,Location=new(){Warehouse=warehouse,Code="STORAGE",Name="Ô lưu trữ",LocationType=WarehouseLocationType.Storage,IsPickable=true,CreatedBy=f.Maker}});
            await f.Db.SaveChangesAsync();
            if(i<3){warehouses.Add(warehouse.Id);f.Db.UserWarehouses.Add(new(){UserId=f.Checker,WarehouseId=warehouse.Id,CreatedBy=f.Maker});await f.Db.SaveChangesAsync();}
            using var created=await f.Send(HttpMethod.Post,"/api/stock-reservations",f.Maker,new{productId=f.Product,warehouseId=warehouse.Id,quantity=1m});Assert.Equal(HttpStatusCode.Created,created.StatusCode);
        }
        await Create(f,f.Checker,1m);
        await f.Db.StockReservations.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,DateTime.UtcNow.AddHours(-2)).SetProperty(x=>x.ExpiresAt,DateTime.UtcNow.AddHours(-1)));
        var key=Guid.NewGuid().ToString("N");
        using var expired=await f.Send(HttpMethod.Post,"/api/stock-reservations/expire",f.Checker,new{},key);Assert.Equal(HttpStatusCode.OK,expired.StatusCode);
        Assert.Equal(4,(await expired.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expired").GetInt32());
        Assert.Equal((500m,1m,0),await f.Stock());
        Assert.Single(await f.Db.StockReservations.AsNoTracking().Where(x=>x.Status==StockReservationStatus.Active).ToListAsync());
        var counts=await f.Counts();Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,"/api/stock-reservations/expire",f.Checker,new{},key)).StatusCode);Assert.Equal(counts,await f.Counts());
        await f.Db.UserWarehouses.Where(x=>x.UserId==f.Checker&&x.WarehouseId==warehouses.Last()).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.NotFound,(await f.Send(HttpMethod.Post,"/api/stock-reservations/expire",f.Checker,new{},key)).StatusCode);
        Assert.Equal(counts,await f.Counts());Assert.Equal((500m,1m,0),await f.Stock());
        Assert.Equal(HttpStatusCode.NotFound,(await f.Send(HttpMethod.Post,"/api/stock-reservations/999999/release",f.Checker,new{rowVersion="AAAAAAAAAAE=",reason="Không tồn tại"})).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await f.Send(HttpMethod.Post,"/api/stock-reservations",999999,new{productId=f.Product,warehouseId=f.Warehouse,quantity=1})).StatusCode);Assert.Equal(counts,await f.Counts());
    }
    private static async Task<JsonElement> Read(Fixture f,int actor,int id)
    {
        using var result=await f.Send(HttpMethod.Get,"/api/stock-reservations/"+id,actor);Assert.Equal(HttpStatusCode.OK,result.StatusCode);return await result.Content.ReadFromJsonAsync<JsonElement>();
    }
}
