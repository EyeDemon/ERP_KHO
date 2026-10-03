using System.Text.RegularExpressions;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Interfaces;

namespace ERP.Application.Services;

public sealed partial class ProductCatalogService(IProductCatalogRepository repository, IProductRepository products) : IProductCatalogService
{
    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex BarcodePattern();

    public Task<Product> AddProductAsync(Product product, CancellationToken ct = default) =>
        repository.AddProductAsync(product, ct);

    public async Task<IReadOnlyList<ProductCategoryDto>> GetCategoriesAsync(CancellationToken ct = default) =>
        (await repository.GetCategoriesAsync(ct)).Select(Map).ToList();

    public async Task<ProductCategoryDto> CreateCategoryAsync(CreateProductCategoryDto dto, CancellationToken ct = default)
    {
        var code = NormalizeCode(dto.Code);
        var name = NormalizeName(dto.Name);
        if (await repository.CategoryCodeExistsAsync(code, ct)) throw Conflict("Mã danh mục đã tồn tại.");
        return Map(await repository.AddCategoryAsync(new ProductCategory { Code = code, Name = name }, ct));
    }

    public async Task UpdateCategoryAsync(int id, UpdateProductCategoryDto dto, CancellationToken ct = default)
    {
        var entity = await repository.GetCategoryAsync(id, ct) ?? throw new NotFoundException($"Không tìm thấy danh mục id {id}");
        entity.Name = NormalizeName(dto.Name);
        entity.IsActive = dto.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        await repository.UpdateCategoryAsync(entity, ct);
    }

    public async Task DeleteCategoryAsync(int id, CancellationToken ct = default)
    {
        var entity = await repository.GetCategoryAsync(id, ct) ?? throw new NotFoundException($"Không tìm thấy danh mục id {id}");
        if (await repository.CategoryHasProductsAsync(id, ct)) throw Conflict("Không thể xóa danh mục đang được sản phẩm sử dụng.");
        await repository.DeleteCategoryAsync(entity, ct);
    }

    public async Task EnsureAssignableCategoryAsync(int? categoryId, CancellationToken ct = default)
    {
        if (!categoryId.HasValue) return;
        var category = await repository.GetCategoryAsync(categoryId.Value, ct) ?? throw new BusinessRuleException("Danh mục sản phẩm không tồn tại.");
        if (!category.IsActive) throw new BusinessRuleException("Không thể gán danh mục đang ngừng hoạt động.");
    }

    public async Task SetProductCategoryAsync(int productId, SetProductCategoryDto dto, CancellationToken ct = default)
    {
        await EnsureAssignableCategoryAsync(dto.CategoryId, ct);
        await repository.SetProductCategoryAsync(productId, dto.CategoryId, ct);
    }

    public async Task<IReadOnlyList<ProductBarcodeDto>> GetBarcodesAsync(int productId, CancellationToken ct = default)
    {
        if (await products.GetByIdAsync(productId, ct) is null) throw new NotFoundException($"Không tìm thấy sản phẩm id {productId}");
        return (await repository.GetBarcodesAsync(productId, ct)).Select(Map).ToList();
    }

    public async Task<ProductBarcodeDto> AddBarcodeAsync(int productId, CreateProductBarcodeDto dto, CancellationToken ct = default)
    {
        var value = NormalizeBarcode(dto.Value);
        if (await products.GetByIdAsync(productId, ct) is null) throw new NotFoundException($"Không tìm thấy sản phẩm id {productId}");
        if (await repository.FindBarcodeAsync(value, ct) is not null) throw Conflict("Barcode đã được sử dụng cho sản phẩm khác.");
        return Map(await repository.AddBarcodeAsync(new ProductBarcode { ProductId = productId, Value = value }, ct));
    }

    public async Task DeleteBarcodeAsync(int productId, int barcodeId, CancellationToken ct = default)
    {
        var barcode = await repository.GetBarcodeAsync(barcodeId, ct) ?? throw new NotFoundException($"Không tìm thấy barcode id {barcodeId}");
        if (barcode.ProductId != productId) throw new NotFoundException($"Barcode không thuộc sản phẩm id {productId}");
        await repository.DeleteBarcodeAsync(barcode, ct);
    }

    public async Task<ProductDto> LookupBarcodeAsync(string value, CancellationToken ct = default)
    {
        var barcode = await repository.FindBarcodeAsync(NormalizeBarcode(value), ct) ?? throw new NotFoundException("Không tìm thấy sản phẩm theo barcode.");
        return ProductService.Map(barcode.Product);
    }

    private static string NormalizeCode(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length is < 1 or > 50 || !Regex.IsMatch(normalized, "^[A-Z0-9._-]+$", RegexOptions.CultureInvariant))
            throw new BusinessRuleException("Mã danh mục chỉ được chứa chữ cái ASCII, chữ số, dấu chấm, gạch dưới hoặc gạch ngang.");
        return normalized;
    }

    private static string NormalizeName(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 1 or > 200) throw new BusinessRuleException("Tên danh mục phải có từ 1 đến 200 ký tự.");
        return normalized;
    }

    private static string NormalizeBarcode(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (!BarcodePattern().IsMatch(normalized)) throw new BusinessRuleException("Barcode phải có từ 1 đến 64 ký tự hợp lệ.");
        return normalized;
    }

    private static ProductCategoryDto Map(ProductCategory x) => new() { Id = x.Id, Code = x.Code, Name = x.Name, IsActive = x.IsActive };
    private static ProductBarcodeDto Map(ProductBarcode x) => new() { Id = x.Id, ProductId = x.ProductId, Value = x.Value };
    private static BusinessRuleException Conflict(string message) { var ex = new BusinessRuleException(message); ex.Data["HttpStatusCode"] = 409; return ex; }
}
