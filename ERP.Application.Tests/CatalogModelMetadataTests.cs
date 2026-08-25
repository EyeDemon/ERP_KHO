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

        // Product.Code -> IX_ProductCode (Unique)
        var productEntity = model.FindEntityType(typeof(Product));
        productEntity.Should().NotBeNull();
        var productCodeProp = productEntity!.FindProperty(nameof(Product.Code));
        productCodeProp.Should().NotBeNull();
        var productIndex = productEntity.FindIndex(productCodeProp!);
        productIndex.Should().NotBeNull();
        productIndex!.IsUnique.Should().BeTrue();
        productIndex.GetDatabaseName().Should().Be("IX_ProductCode");

        // Warehouse.Code -> IX_WarehouseCode (Unique)
        var warehouseEntity = model.FindEntityType(typeof(Warehouse));
        warehouseEntity.Should().NotBeNull();
        var warehouseCodeProp = warehouseEntity!.FindProperty(nameof(Warehouse.Code));
        warehouseCodeProp.Should().NotBeNull();
        var warehouseIndex = warehouseEntity.FindIndex(warehouseCodeProp!);
        warehouseIndex.Should().NotBeNull();
        warehouseIndex!.IsUnique.Should().BeTrue();
        warehouseIndex.GetDatabaseName().Should().Be("IX_WarehouseCode");

        // Unit.Code -> IX_UnitCode (Unique)
        var unitEntity = model.FindEntityType(typeof(Unit));
        unitEntity.Should().NotBeNull();
        var unitCodeProp = unitEntity!.FindProperty(nameof(Unit.Code));
        unitCodeProp.Should().NotBeNull();
        var unitIndex = unitEntity.FindIndex(unitCodeProp!);
        unitIndex.Should().NotBeNull();
        unitIndex!.IsUnique.Should().BeTrue();
        unitIndex.GetDatabaseName().Should().Be("IX_UnitCode");
    }
}
