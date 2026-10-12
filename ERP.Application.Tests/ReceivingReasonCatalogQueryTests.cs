using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ERP.Application.Tests;

public sealed class ReceivingReasonCatalogQueryTests
{
    public static readonly DateTime Now = new(2026, 10, 11, 8, 0, 0, DateTimeKind.Utc);
    public sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }
    private static ErpKhoDbContext Context() => new(new DbContextOptionsBuilder<ErpKhoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public static ReceivingReasonCode[] Rows() => [
        new() { Code="QA_BOUNDARY", Name="Nhận thiếu", Version=1, IsActive=true, EffectiveFromUtc=Now, EffectiveToUtc=Now.AddDays(1), RequiresNote=true },
        new() { Code="QA_END", Name="Kết thúc", Version=1, IsActive=true, EffectiveFromUtc=Now.AddDays(-1), EffectiveToUtc=Now },
        new() { Code="QA_FUTURE", Name="Chờ áp dụng", Version=1, IsActive=true, EffectiveFromUtc=Now.AddDays(1) },
        new() { Code="QA_INACTIVE", Name="Ngừng sử dụng", Version=1, IsActive=false, EffectiveFromUtc=Now.AddDays(-1) },
        new() { Code="QA_BOUNDARY", Name="Nhận thiếu phiên bản hai", Version=2, IsActive=true, EffectiveFromUtc=Now.AddDays(-1), EffectiveToUtc=Now.AddDays(2), RequiresApproval=true }
    ];

    [Theory]
    [InlineData(ReasonEffectivity.Effective, 3)]
    [InlineData(ReasonEffectivity.Expired, 1)]
    [InlineData(ReasonEffectivity.NotYetEffective, 1)]
    public async Task UtcBoundaryAndActivityAreIndependent(ReasonEffectivity filter, int count)
    {
        await using var db=Context(); db.AddRange(Rows()); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var query=new ReceivingReasonCatalogQuery(db,new Clock());
        var result=await query.ListAsync(null,null,filter,1,20,default);
        Assert.Equal(count,result.TotalRecords); Assert.All(result.Items,x=>Assert.Equal(filter,x.Effectivity));
        Assert.All(result.Items,x=>Assert.Equal(TimeSpan.Zero,x.EffectiveFromUtc.Offset));
        Assert.Empty(db.ChangeTracker.Entries());
        var inactive=await query.ListAsync(null,false,ReasonEffectivity.Effective,1,20,default);
        Assert.Single(inactive.Items); Assert.Null(inactive.Items[0].EffectiveToUtc);
    }

    [Fact]
    public async Task SearchCountPagingAndOverlapRetainEveryVersion()
    {
        await using var db=Context(); db.AddRange(Rows()); await db.SaveChangesAsync();
        var query=new ReceivingReasonCatalogQuery(db,new Clock());
        var result=await query.ListAsync(" qa_boundary ",true,ReasonEffectivity.Effective,2,1,default);
        Assert.Equal(2,result.TotalRecords); Assert.Equal(2,result.TotalPages); Assert.Equal(1,Assert.Single(result.Items).Version);
        Assert.Equal(2,(await query.VersionsAsync("qa_boundary",default)).Count);
        Assert.Equal(2,(await query.ListAsync("Nhận thiếu",null,null,1,20,default)).Items.Count);
        Assert.Empty((await query.ListAsync("không có",null,null,1,20,default)).Items);
        await Assert.ThrowsAsync<NotFoundException>(()=>query.DetailAsync(-1,default));
        await Assert.ThrowsAsync<NotFoundException>(()=>query.VersionsAsync("MISSING",default));
    }

    [Fact]
    public async Task ReadDoesNotReinterpretHistoricalSnapshotOrWriteAnything()
    {
        await using var db=Context(); var rows=Rows(); db.AddRange(rows);
        var snapshot=new ReceivingResolutionVersion { ReasonCodeSnapshot="QA_BOUNDARY", ReasonNameSnapshot="Tên lịch sử", ReasonVersionSnapshot=1, ReasonRequiresNoteSnapshot=true };
        db.Add(snapshot); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var before=JsonSerializer.Serialize(await db.ReceivingReasonCodes.OrderBy(x=>x.Id).ToArrayAsync());
        db.ChangeTracker.Clear(); var query=new ReceivingReasonCatalogQuery(db,new Clock());
        await query.ListAsync(null,null,null,1,20,default); await query.DetailAsync(rows[0].Id,default); await query.VersionsAsync(rows[0].Code,default);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(before,JsonSerializer.Serialize(await db.ReceivingReasonCodes.OrderBy(x=>x.Id).ToArrayAsync()));
        var stored=await db.Set<ReceivingResolutionVersion>().SingleAsync();
        Assert.Equal("Tên lịch sử",stored.ReasonNameSnapshot); Assert.Equal(1,stored.ReasonVersionSnapshot); Assert.True(stored.ReasonRequiresNoteSnapshot);
        Assert.Empty(await db.InventoryStocks.ToArrayAsync()); Assert.Empty(await db.InventoryTransactions.ToArrayAsync()); Assert.Empty(await db.AuditLogs.ToArrayAsync()); Assert.Empty(await db.IdempotencyRecords.ToArrayAsync());
    }
}
