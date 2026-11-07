using ERP.Application.DTOs;
using ERP.Domain.Entities;

namespace ERP.Application.Interfaces;

public interface IProductCatalogService
{
    Task<Product> AddProductAsync(Product product, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<ProductCategoryDto> CreateCategoryAsync(CreateProductCategoryDto dto, CancellationToken cancellationToken = default);
    Task UpdateCategoryAsync(int id, UpdateProductCategoryDto dto, CancellationToken cancellationToken = default);
    Task DeleteCategoryAsync(int id, CancellationToken cancellationToken = default);
    Task SetProductCategoryAsync(int productId, SetProductCategoryDto dto, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductBarcodeDto>> GetBarcodesAsync(int productId, CancellationToken cancellationToken = default);
    Task<ProductBarcodeDto> AddBarcodeAsync(int productId, CreateProductBarcodeDto dto, CancellationToken cancellationToken = default);
    Task DeleteBarcodeAsync(int productId, int barcodeId, CancellationToken cancellationToken = default);
    Task<ProductDto> LookupBarcodeAsync(string value, CancellationToken cancellationToken = default);
    Task EnsureAssignableCategoryAsync(int? categoryId, CancellationToken cancellationToken = default);
}
