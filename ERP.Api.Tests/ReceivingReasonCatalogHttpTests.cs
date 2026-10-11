using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ERP.Api.Tests;

public sealed class ReceivingReasonCatalogHttpTests
{
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026,10,11,8,0,0,TimeSpan.Zero); }

    [ApprovalSqlServerFact]
    public async Task GlobalCatalogUsesDatabaseGrantAndReadsAreSideEffectFree()
    {
        await using var owned=await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await owned.MigrateAndSeedAsync();
        await using var db=new ErpKhoDbContext(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(owned.ConnectionString).Options);
        var role=await db.Roles.SingleAsync(x=>x.RoleName=="Viewer");
        var actor=new User { Username="qa_reason_reader", FullName="Người tra cứu", PasswordHash="TEST_HASH_ONLY", RoleId=role.Id, IsActive=true };
        var now=new DateTime(2026,10,11,8,0,0,DateTimeKind.Utc);
        var reasons=new ReceivingReasonCode[] {
            new() { Code="QA_CATALOG", Name="Bắt đầu",Version=1,IsActive=true,EffectiveFromUtc=now,EffectiveToUtc=now.AddDays(1),RequiresNote=true },
            new() { Code="QA_CATALOG", Name="Chồng hiệu lực",Version=2,IsActive=true,EffectiveFromUtc=now.AddDays(-1),EffectiveToUtc=now.AddDays(2) },
            new() { Code="QA_END", Name="Đã kết thúc",Version=1,IsActive=true,EffectiveFromUtc=now.AddDays(-1),EffectiveToUtc=now },
            new() { Code="QA_FUTURE", Name="Chưa hiệu lực",Version=1,IsActive=true,EffectiveFromUtc=now.AddDays(1) },
            new() { Code="QA_INACTIVE", Name="Ngừng hoạt động",Version=1,IsActive=false,EffectiveFromUtc=now.AddDays(-1) }
        };
        db.Add(actor); db.AddRange(reasons); await db.SaveChangesAsync();
        var before=await Snapshot(db);
        using var factory=ApprovalHttpIntegrationTests.Factory(owned.ConnectionString).WithWebHostBuilder(builder=>builder.ConfigureTestServices(services=> { services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(new Clock()); }));
        using var client=factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress=new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/receiving-reason-codes")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Actor",actor.Id.ToString()); client.DefaultRequestHeaders.Add("X-Test-Role","Admin");
        foreach(var path in new[]{"/api/receiving-reason-codes?keyword=QA_",$"/api/receiving-reason-codes/{reasons[0].Id}","/api/receiving-reason-codes/versions/QA_CATALOG"})
        {
            var response=await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            var raw=await response.Content.ReadAsStringAsync();
            foreach(var forbidden in new[]{"rowVersion","password","idempotency","evidenceReference","unitPrice","cost","stackTrace"}) Assert.DoesNotContain(forbidden,raw,StringComparison.OrdinalIgnoreCase);
        }
        using(var result=await client.GetFromJsonAsync<JsonDocument>("/api/receiving-reason-codes?keyword=QA_CATALOG&effectivity=Effective&page=2&pageSize=1"))
        { Assert.Equal(2,result!.RootElement.GetProperty("totalRecords").GetInt32()); Assert.Equal(1,result.RootElement.GetProperty("items")[0].GetProperty("version").GetInt32()); }
        foreach(var filter in new[]{"effectivity=Expired","effectivity=NotYetEffective","active=false&effectivity=Effective"})
        { using var result=await client.GetFromJsonAsync<JsonDocument>("/api/receiving-reason-codes?keyword=QA_&"+filter); Assert.Equal(1,result!.RootElement.GetProperty("totalRecords").GetInt32()); }
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/receiving-reason-codes/2147483647")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/receiving-reason-codes/versions/MISSING")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("/api/receiving-reason-codes?effectivity=999")).StatusCode);
        var grant=await db.RolePermissions.SingleAsync(x=>x.RoleId==role.Id && x.Permission.Code==AppPermissions.ReasonRead);
        db.Remove(grant); await db.SaveChangesAsync();
        foreach(var path in new[]{"/api/receiving-reason-codes",$"/api/receiving-reason-codes/{reasons[0].Id}","/api/receiving-reason-codes/versions/QA_CATALOG"})
            Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(path)).StatusCode);
        db.RolePermissions.Add(new RolePermission { RoleId=role.Id,PermissionId=grant.PermissionId }); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/receiving-reason-codes")).StatusCode);
        actor.IsActive=false; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/receiving-reason-codes")).StatusCode);
        Assert.Equal(before,await Snapshot(db));
    }

    private static async Task<string> Snapshot(ErpKhoDbContext db) => JsonSerializer.Serialize(new {
        reasons=await db.ReceivingReasonCodes.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync(),
        stock=await db.InventoryStocks.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new {x.Id,x.Quantity,x.ReservedQuantity}).ToArrayAsync(),
        ledger=await db.InventoryTransactions.CountAsync(), audits=await db.AuditLogs.CountAsync(), claims=await db.IdempotencyRecords.CountAsync(),
        receipts=await db.ImportReceipts.CountAsync(), exports=await db.ExportReceipts.CountAsync(), reservations=await db.StockReservations.CountAsync()
    });
}
