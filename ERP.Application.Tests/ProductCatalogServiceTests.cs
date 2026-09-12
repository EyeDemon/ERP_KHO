using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace ERP.Application.Tests;

public sealed class ProductCatalogServiceTests
{
    private readonly Mock<IProductCatalogRepository> _catalog = new();
    private readonly Mock<IProductRepository> _products = new();
    private ProductCatalogService Service => new(_catalog.Object, _products.Object);

    [Fact]
    public async Task CategoryCode_IsTrimmedUppercase()
    {
        ProductCategory? captured = null;
        _catalog.Setup(x => x.AddCategoryAsync(It.IsAny<ProductCategory>(), It.IsAny<CancellationToken>())).Callback<ProductCategory, CancellationToken>((x, _) => captured = x).ReturnsAsync((ProductCategory x, CancellationToken _) => x);
        await Service.CreateCategoryAsync(new CreateProductCategoryDto { Code = " raw-1 ", Name = " Hàng thô " });
        captured!.Code.Should().Be("RAW-1"); captured.Name.Should().Be("Hàng thô");
    }

    [Fact]
    public async Task InactiveCategory_CannotBeAssigned()
    {
        _catalog.Setup(x => x.GetCategoryAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(new ProductCategory { Id = 2, IsActive = false });
        await FluentActions.Awaiting(() => Service.SetProductCategoryAsync(1, new SetProductCategoryDto { CategoryId = 2 })).Should().ThrowAsync<BusinessRuleException>();
        _catalog.Verify(x => x.SetProductCategoryAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReferencedCategory_CannotBeDeleted()
    {
        _catalog.Setup(x => x.GetCategoryAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(new ProductCategory { Id = 2 });
        _catalog.Setup(x => x.CategoryHasProductsAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await FluentActions.Awaiting(() => Service.DeleteCategoryAsync(2)).Should().ThrowAsync<BusinessRuleException>();
    }

    [Theory]
    [InlineData(" 00123 ", "00123")]
    [InlineData("AbC", "AbC")]
    [InlineData("abc", "abc")]
    public async Task Barcode_PreservesLeadingZeroAndCase(string input, string expected)
    {
        _products.Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Product { Id = 1 });
        ProductBarcode? captured = null;
        _catalog.Setup(x => x.AddBarcodeAsync(It.IsAny<ProductBarcode>(), It.IsAny<CancellationToken>())).Callback<ProductBarcode, CancellationToken>((x, _) => captured = x).ReturnsAsync((ProductBarcode x, CancellationToken _) => x);
        await Service.AddBarcodeAsync(1, new CreateProductBarcodeDto { Value = input });
        captured!.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("mã-vạch")]
    public async Task InvalidBarcode_IsRejected(string value) =>
        await FluentActions.Awaiting(() => Service.AddBarcodeAsync(1, new CreateProductBarcodeDto { Value = value })).Should().ThrowAsync<BusinessRuleException>();

    [Fact]
    public async Task MultipleBarcodes_AreReturnedForOneProduct()
    {
        _products.Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Product { Id = 1 });
        _catalog.Setup(x => x.GetBarcodesAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync([new() { ProductId = 1, Value = "001" }, new() { ProductId = 1, Value = "ABC" }]);
        (await Service.GetBarcodesAsync(1)).Select(x => x.Value).Should().Equal("001", "ABC");
    }

    [Fact]
    public async Task BarcodeLookup_DoesNotUseProductCode()
    {
        _catalog.Setup(x => x.FindBarcodeAsync("CODE-OF-OTHER-PRODUCT", It.IsAny<CancellationToken>())).ReturnsAsync((ProductBarcode?)null);
        await FluentActions.Awaiting(() => Service.LookupBarcodeAsync("CODE-OF-OTHER-PRODUCT")).Should().ThrowAsync<NotFoundException>();
    }
}
