using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerProductCatalogTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task Migration_PreservesExistingProductsAndAddsCatalogSchema()
    {
        await using var context = CreateContext();
        (await context.Products.CountAsync(x => x.CategoryId != null)).Should().Be(0);
        (await context.ProductBarcodes.CountAsync()).Should().Be(0);
        var migration = await context.Database.GetAppliedMigrationsAsync();
        migration.Should().Contain(x => x.EndsWith("_AddProductCategoriesAndBarcodes"));
    }

    [SqlServerFact]
    public async Task BarcodeUniqueIndex_IsCaseSensitiveAndRejectsConcurrentDuplicate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        int product1;
        int product2;
        await using (var seed = CreateContext())
        {
            var unit = new Unit { Code = $"U{suffix}", Name = "Unit" };
            seed.Units.Add(unit); await seed.SaveChangesAsync();
            var products = new[] { new Product { Code = $"P{suffix}A", Name = "A", UnitId = unit.Id }, new Product { Code = $"P{suffix}B", Name = "B", UnitId = unit.Id } };
            seed.Products.AddRange(products); await seed.SaveChangesAsync(); product1 = products[0].Id; product2 = products[1].Id;
            seed.ProductBarcodes.AddRange(new ProductBarcode { ProductId = product1, Value = "001Ab" }, new ProductBarcode { ProductId = product1, Value = "001ab" });
            await seed.SaveChangesAsync();
        }
        await using var first = CreateContext(); await using var second = CreateContext();
        first.ProductBarcodes.Add(new ProductBarcode { ProductId = product1, Value = $"DUP{suffix}" });
        second.ProductBarcodes.Add(new ProductBarcode { ProductId = product2, Value = $"DUP{suffix}" });
        using var ready = new CountdownEvent(2);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var results = await Task.WhenAll(
            SaveAtBarrier(first, ready, timeout.Token),
            SaveAtBarrier(second, ready, timeout.Token)).WaitAsync(timeout.Token);
        results.Count(x => x).Should().Be(1);
        await using var verify = CreateContext();
        (await verify.ProductBarcodes.CountAsync(x => x.Value == $"DUP{suffix}")).Should().Be(1);
    }

    [SqlServerFact]
    public async Task CategoryForeignKey_RejectsDeleteWhileReferenced()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        int categoryId;
        await using (var seed = CreateContext())
        {
            var unit = new Unit { Code = $"V{suffix}", Name = "Unit" };
            var category = new ProductCategory { Code = $"C{suffix}", Name = "Category" };
            seed.AddRange(unit, category); await seed.SaveChangesAsync();
            seed.Products.Add(new Product { Code = $"Q{suffix}", Name = "Product", UnitId = unit.Id, CategoryId = category.Id });
            await seed.SaveChangesAsync();
            categoryId = category.Id;
        }

        await using var deleting = CreateContext();
        var categoryToDelete = await deleting.ProductCategories.SingleAsync(x => x.Id == categoryId);
        deleting.ProductCategories.Remove(categoryToDelete);
        await FluentActions.Awaiting(() => deleting.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }

    private static ErpKhoDbContext CreateContext() => new(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(ConnectionString).Options);
    private static async Task<bool> SaveResult(ErpKhoDbContext context) { try { await context.SaveChangesAsync(); return true; } catch (DbUpdateException) { return false; } }
    private static async Task<bool> SaveAtBarrier(ErpKhoDbContext context, CountdownEvent ready, CancellationToken ct)
    {
        ready.Signal();
        var synchronized = await Task.Run(() => ready.Wait(TimeSpan.FromSeconds(5), ct), ct);
        if (!synchronized) throw new TimeoutException("Concurrent barcode writers did not reach the synchronization barrier.");
        try { await context.SaveChangesAsync(ct); return true; }
        catch (DbUpdateException) { return false; }
    }
}
