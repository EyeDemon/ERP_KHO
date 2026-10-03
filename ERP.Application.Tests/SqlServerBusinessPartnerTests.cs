using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerBusinessPartnerTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task MigrationAddsNullableLinksWithoutChangingExistingReceipts()
    {
        await using var db = CreateContext();
        (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(x => x.EndsWith("_AddBusinessPartnersAndReceiptAssociations"));
        db.Model.FindEntityType(typeof(ImportReceipt))!.FindProperty(nameof(ImportReceipt.SupplierId))!.IsNullable.Should().BeTrue();
        db.Model.FindEntityType(typeof(ExportReceipt))!.FindProperty(nameof(ExportReceipt.CustomerId))!.IsNullable.Should().BeTrue();
    }

    [SqlServerFact]
    public async Task DatabaseEnforcesRoleUniqueCodeAndReferencedDelete()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = CreateContext();
        db.BusinessPartners.Add(new BusinessPartner { Code = $"NONE{suffix}", Name = "Invalid", IsActive = true });
        await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        db.ChangeTracker.Clear();
        var partner = new BusinessPartner { Code = $"BP{suffix}", Name = "Both", IsSupplier = true, IsCustomer = true };
        db.BusinessPartners.Add(partner); await db.SaveChangesAsync();
        db.BusinessPartners.Add(new BusinessPartner { Code = partner.Code.ToLowerInvariant(), Name = "Duplicate", IsSupplier = true });
        await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        db.ChangeTracker.Clear();
        var fixture = await ReceiptFixtureAsync(db, suffix, partner.Id);
        await using var deleting = CreateContext();
        deleting.BusinessPartners.Remove(await deleting.BusinessPartners.SingleAsync(x => x.Id == partner.Id));
        await FluentActions.Awaiting(() => deleting.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        (await db.ImportReceipts.AnyAsync(x => x.Id == fixture.ImportId && x.SupplierId == partner.Id)).Should().BeTrue();
        (await db.ExportReceipts.AnyAsync(x => x.Id == fixture.ExportId && x.CustomerId == partner.Id)).Should().BeTrue();
    }

    [SqlServerFact]
    public async Task ConcurrentDuplicateCodeAllowsExactlyOneWriter()
    {
        var code = $"RACE{Guid.NewGuid():N}"[..20];
        await using var first = CreateContext(); await using var second = CreateContext();
        first.BusinessPartners.Add(new BusinessPartner { Code = code, Name = "One", IsSupplier = true });
        second.BusinessPartners.Add(new BusinessPartner { Code = code.ToLowerInvariant(), Name = "Two", IsCustomer = true });
        using var barrier = new CountdownEvent(2); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var results = await Task.WhenAll(SaveAtBarrier(first, barrier, timeout.Token), SaveAtBarrier(second, barrier, timeout.Token)).WaitAsync(timeout.Token);
        results.Count(x => x).Should().Be(1);
        await using var verify = CreateContext(); (await verify.BusinessPartners.CountAsync(x => x.Code == code)).Should().Be(1);
    }

    [SqlServerFact]
    public async Task AssignmentRequiresDraftActiveRoleAndWarehouseAccess()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = CreateContext();
        var valid = new BusinessPartner { Code = $"S{suffix}", Name = "Supplier", IsSupplier = true };
        var inactive = new BusinessPartner { Code = $"I{suffix}", Name = "Inactive", IsSupplier = true, IsActive = false };
        var wrong = new BusinessPartner { Code = $"C{suffix}", Name = "Customer", IsCustomer = true };
        db.AddRange(valid, inactive, wrong); await db.SaveChangesAsync();
        var fixture = await ReceiptFixtureAsync(db, suffix, null);
        var user = new Current(fixture.UserId); var service = new BusinessPartnerService(db, new WarehouseAuthorizationService(db, user), user);
        await service.SetImportSupplierAsync(fixture.ImportId, valid.Id);
        (await db.ImportReceipts.FindAsync(fixture.ImportId))!.SupplierId.Should().Be(valid.Id);
        await FluentActions.Awaiting(() => service.SetImportSupplierAsync(fixture.ImportId, inactive.Id)).Should().ThrowAsync<BusinessRuleException>();
        await FluentActions.Awaiting(() => service.SetImportSupplierAsync(fixture.ImportId, wrong.Id)).Should().ThrowAsync<BusinessRuleException>();
        (await db.ImportReceipts.FindAsync(fixture.ImportId))!.Status = ReceiptStatus.Approved; await db.SaveChangesAsync();
        await FluentActions.Awaiting(() => service.SetImportSupplierAsync(fixture.ImportId, null)).Should().ThrowAsync<BusinessRuleException>();
    }

    [SqlServerFact]
    public async Task UpdateRequiresValidCurrentRowVersion()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        int id; string original;
        await using (var seed = CreateContext())
        {
            var current = new Current(0, true);
            var created = await new BusinessPartnerService(seed, new WarehouseAuthorizationService(seed, current), current)
                .CreateAsync(new SaveBusinessPartnerDto { Code = $"V{suffix}", Name = "Original", IsSupplier = true });
            id = created.Id; original = created.RowVersion;
        }

        async Task Update(string? token, string name)
        {
            await using var db = CreateContext(); var current = new Current(0, true);
            await new BusinessPartnerService(db, new WarehouseAuthorizationService(db, current), current)
                .UpdateAsync(id, new SaveBusinessPartnerDto { Code = $"V{suffix}", Name = name, IsSupplier = true, RowVersion = token });
        }

        await FluentActions.Awaiting(() => Update(null, "Missing")).Should().ThrowAsync<BusinessRuleException>().Where(x => Equals(x.Data["HttpStatusCode"], 409));
        await FluentActions.Awaiting(() => Update("not-base64", "Malformed")).Should().ThrowAsync<BusinessRuleException>().Where(x => Equals(x.Data["HttpStatusCode"], 409));
        await Update(original, "First writer");
        await FluentActions.Awaiting(() => Update(original, "Stale writer")).Should().ThrowAsync<BusinessRuleException>().Where(x => Equals(x.Data["HttpStatusCode"], 409));
        await using var verify = CreateContext(); (await verify.BusinessPartners.FindAsync(id))!.Name.Should().Be("First writer");
    }

    private static async Task<(int ImportId,int ExportId,int UserId)> ReceiptFixtureAsync(ErpKhoDbContext db,string suffix,int? partnerId)
    {
        var role=new Role{RoleName=$"R{suffix}"}; var user=new User{Username=$"U{suffix}",PasswordHash="x",FullName="Synthetic",Role=role};
        var warehouse=new Warehouse{Code=$"W{suffix}",Name="Synthetic"}; db.AddRange(user,warehouse); await db.SaveChangesAsync();
        db.UserWarehouses.Add(new UserWarehouse{UserId=user.Id,WarehouseId=warehouse.Id,CreatedBy=user.Id});
        var import=new ImportReceipt{Code=$"IM{suffix}",WarehouseId=warehouse.Id,CreatedBy=user.Id,SupplierId=partnerId};
        var export=new ExportReceipt{Code=$"EX{suffix}",WarehouseId=warehouse.Id,CreatedBy=user.Id,CustomerId=partnerId}; db.AddRange(import,export); await db.SaveChangesAsync();
        return(import.Id,export.Id,user.Id);
    }
    private static async Task<bool> SaveAtBarrier(ErpKhoDbContext db,CountdownEvent barrier,CancellationToken ct){barrier.Signal();if(!await Task.Run(()=>barrier.Wait(TimeSpan.FromSeconds(5),ct),ct))throw new TimeoutException();try{await db.SaveChangesAsync(ct);return true;}catch(DbUpdateException){return false;}}
    private static ErpKhoDbContext CreateContext()=>new(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(ConnectionString).Options);
    private sealed record Current(int UserId,bool IsGlobalAdmin=false):ICurrentUser{public bool IsAuthenticated=>true;public string Role=>"Manager";}
}
