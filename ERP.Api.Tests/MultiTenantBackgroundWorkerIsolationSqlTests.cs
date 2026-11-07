using ERP.Api.Hosting;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ERP.Api.Tests;

public sealed class MultiTenantBackgroundWorkerIsolationSqlTests
{
    [ApprovalSqlServerFact]
    public async Task MultiTenantBackgroundWorkerIsolationSqlTest()
    {
        await using var owned = await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await owned.MigrateAndSeedAsync();

        var services = new ServiceCollection();
        services.AddDbContext<ErpKhoDbContext>(opt => opt.UseSqlServer(owned.ConnectionString));
        services.AddScoped<IWarehouseAuthorizationService>(_ => Mock.Of<IWarehouseAuthorizationService>());
        services.AddScoped<IInventoryReconciliationQueryService, InventoryReconciliationQueryService>();
        await using var provider = services.BuildServiceProvider();

        const int tenantOneId = 1;
        const int tenantTwoId = 2;

        // Provision Tenant 1 metadata + Tenant 2 isolated data (with a deliberate 7-unit unposted drift on Tenant 2 only)
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ErpKhoDbContext>();
            setupDb.UseTenant(tenantOneId);
            if (!await setupDb.SaasTenants.IgnoreQueryFilters().AnyAsync(t => t.TenantId == tenantOneId))
            {
                setupDb.SaasTenants.Add(new SaasTenant
                {
                    TenantId = tenantOneId,
                    TenantCode = "TENANT-001",
                    CompanyName = "Tenant One Baseline",
                    LifecycleStatus = "ACTIVE"
                });
            }

            var t1UserId = await setupDb.Users.OrderBy(u => u.Id).Select(u => u.Id).FirstAsync();
            var t1Stock = await setupDb.InventoryStocks.SingleAsync();
            setupDb.InventoryTransactions.Add(new InventoryTransaction
            {
                TenantId = tenantOneId,
                WarehouseId = t1Stock.WarehouseId,
                ProductId = t1Stock.ProductId,
                LocationId = t1Stock.LocationId,
                InventoryStatus = t1Stock.Status,
                TransactionType = TransactionType.Import,
                Quantity = t1Stock.Quantity,
                ReferenceType = "OpeningBalance",
                ReferenceId = t1Stock.Id,
                TransactionDate = DateTime.UtcNow,
                CreatedBy = t1UserId,
                Note = "Đối ứng sổ cái tồn đầu kỳ Tenant 1"
            });
            await setupDb.SaveChangesAsync();
        }

        await using (var tenantTwoScope = provider.CreateAsyncScope())
        {
            var db2 = tenantTwoScope.ServiceProvider.GetRequiredService<ErpKhoDbContext>();
            db2.UseTenant(tenantTwoId);

            db2.SaasTenants.Add(new SaasTenant
            {
                TenantId = tenantTwoId,
                TenantCode = "TENANT-002",
                CompanyName = "Tenant Two Worker Isolation Probe",
                LifecycleStatus = "ACTIVE"
            });
            var unit2 = new Unit { TenantId = tenantTwoId, Code = "CAI-T2", Name = "Cái T2", DecimalPlaces = 0 };
            var cat2 = new ProductCategory { TenantId = tenantTwoId, Code = "CAT-T2", Name = "Nhóm T2" };
            var wh2 = new Warehouse { TenantId = tenantTwoId, Code = "WH-T2-01", Name = "Kho Tenant 2", IsActive = true };
            db2.Units.Add(unit2);
            db2.ProductCategories.Add(cat2);
            db2.Warehouses.Add(wh2);
            await db2.SaveChangesAsync();

            var loc2 = new WarehouseLocation
            {
                TenantId = tenantTwoId,
                WarehouseId = wh2.Id,
                Code = "BIN-T2-01",
                Name = "Kệ T2",
                LocationType = WarehouseLocationType.Storage,
                IsPickable = true,
                IsActive = true
            };
            var prod2 = new Product
            {
                TenantId = tenantTwoId,
                Code = "SKU-T2-ISO",
                Name = "Sản phẩm cô lập Tenant 2",
                UnitId = unit2.Id,
                CategoryId = cat2.Id,
                IsActive = true
            };
            db2.WarehouseLocations.Add(loc2);
            db2.Products.Add(prod2);
            await db2.SaveChangesAsync();

            db2.InventoryStocks.Add(new InventoryStock
            {
                TenantId = tenantTwoId,
                WarehouseId = wh2.Id,
                ProductId = prod2.Id,
                LocationId = loc2.Id,
                Status = InventoryStatus.Available,
                Quantity = 7m,
                ReservedQuantity = 0m
            });
            await db2.SaveChangesAsync();
        }

        var job = new DailyInventoryIntegrityJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DailyInventoryIntegrityJob>.Instance);

        var totalDrift = await job.VerifyIntegrityOnceAsync();
        totalDrift.Should().Be(1, "only Tenant 2 has the deliberate 7-unit unbacked bucket; Tenant 1 has 0 drift");

        // Verify strict per-tenant query & write isolation (0 cross-tenant leakage)
        await using (var verifyT1Scope = provider.CreateAsyncScope())
        {
            var verifyDb1 = verifyT1Scope.ServiceProvider.GetRequiredService<ErpKhoDbContext>();
            verifyDb1.UseTenant(tenantOneId);

            var t1Warehouses = await verifyDb1.Warehouses.AsNoTracking().Select(w => w.Code).ToListAsync();
            t1Warehouses.Should().NotContain("WH-T2-01");

            var t1AuditLogs = await verifyDb1.AuditLogs.AsNoTracking()
                .Where(a => a.Action == "DAILY_INVENTORY_INTEGRITY_CHECK")
                .ToListAsync();
            t1AuditLogs.Should().ContainSingle();
            t1AuditLogs[0].TenantId.Should().Be(tenantOneId);
            t1AuditLogs[0].NewValues.Should().StartWith("DriftCount=0;");
            t1AuditLogs[0].Result.Should().Be("Success");
        }

        await using (var verifyT2Scope = provider.CreateAsyncScope())
        {
            var verifyDb2 = verifyT2Scope.ServiceProvider.GetRequiredService<ErpKhoDbContext>();
            verifyDb2.UseTenant(tenantTwoId);

            var t2Warehouses = await verifyDb2.Warehouses.AsNoTracking().Select(w => w.Code).ToListAsync();
            t2Warehouses.Should().Equal("WH-T2-01");

            var t2AuditLogs = await verifyDb2.AuditLogs.AsNoTracking()
                .Where(a => a.Action == "DAILY_INVENTORY_INTEGRITY_CHECK")
                .ToListAsync();
            t2AuditLogs.Should().ContainSingle();
            t2AuditLogs[0].TenantId.Should().Be(tenantTwoId);
            t2AuditLogs[0].NewValues.Should().Be("DriftCount=1;CheckedBuckets=1");
            t2AuditLogs[0].Result.Should().Be("Warning");
        }
    }
}
