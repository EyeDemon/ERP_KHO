using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERP.Application.Tests;

public class CatalogModelMetadataTests
{
    [Fact]
    public void CatalogEntities_ShouldHaveUniqueCodeIndexesWithExactNames()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(nameof(CatalogEntities_ShouldHaveUniqueCodeIndexesWithExactNames))
            .Options;

        using var context = new ErpKhoDbContext(options);
        var model = context.Model;

        // Product (TenantId, Code) -> UX_Products_TenantId_Code (Unique)
        var productEntity = model.FindEntityType(typeof(Product));
        productEntity.Should().NotBeNull();
        var productTenantProp = productEntity!.FindProperty(nameof(Product.TenantId))!;
        var productCodeProp = productEntity.FindProperty(nameof(Product.Code))!;
        var productIndex = productEntity.FindIndex(new[] { productTenantProp, productCodeProp });
        productIndex.Should().NotBeNull();
        productIndex!.IsUnique.Should().BeTrue();
        productIndex.GetDatabaseName().Should().Be("UX_Products_TenantId_Code");

        // Warehouse (TenantId, Code) -> UX_Warehouses_TenantId_Code (Unique)
        var warehouseEntity = model.FindEntityType(typeof(Warehouse));
        warehouseEntity.Should().NotBeNull();
        var warehouseTenantProp = warehouseEntity!.FindProperty(nameof(Warehouse.TenantId))!;
        var warehouseCodeProp = warehouseEntity.FindProperty(nameof(Warehouse.Code))!;
        var warehouseIndex = warehouseEntity.FindIndex(new[] { warehouseTenantProp, warehouseCodeProp });
        warehouseIndex.Should().NotBeNull();
        warehouseIndex!.IsUnique.Should().BeTrue();
        warehouseIndex.GetDatabaseName().Should().Be("UX_Warehouses_TenantId_Code");

        // Unit (TenantId, Code) -> UX_Units_TenantId_Code (Unique)
        var unitEntity = model.FindEntityType(typeof(Unit));
        unitEntity.Should().NotBeNull();
        var unitTenantProp = unitEntity!.FindProperty(nameof(Unit.TenantId))!;
        var unitCodeProp = unitEntity.FindProperty(nameof(Unit.Code))!;
        var unitIndex = unitEntity.FindIndex(new[] { unitTenantProp, unitCodeProp });
        unitIndex.Should().NotBeNull();
        unitIndex!.IsUnique.Should().BeTrue();
        unitIndex.GetDatabaseName().Should().Be("UX_Units_TenantId_Code");

        var category = model.FindEntityType(typeof(ProductCategory))!;
        category.FindIndex(new[]
        {
            category.FindProperty(nameof(ProductCategory.TenantId))!,
            category.FindProperty(nameof(ProductCategory.Code))!
        })!.IsUnique.Should().BeTrue();
        var barcode = model.FindEntityType(typeof(ProductBarcode))!;
        barcode.FindIndex(new[]
        {
            barcode.FindProperty(nameof(ProductBarcode.TenantId))!,
            barcode.FindProperty(nameof(ProductBarcode.Value))!
        })!.IsUnique.Should().BeTrue();
    }
}
