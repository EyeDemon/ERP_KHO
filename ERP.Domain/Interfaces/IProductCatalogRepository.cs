using ERP.Domain.Entities;

namespace ERP.Domain.Interfaces;

public interface IProductCatalogRepository
{
    Task<Product> AddProductAsync(Product product, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<ProductCategory?> GetCategoryAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> CategoryCodeExistsAsync(string code, CancellationToken cancellationToken = default);
    Task<ProductCategory> AddCategoryAsync(ProductCategory category, CancellationToken cancellationToken = default);
    Task UpdateCategoryAsync(ProductCategory category, CancellationToken cancellationToken = default);
    Task DeleteCategoryAsync(ProductCategory category, CancellationToken cancellationToken = default);
    Task<bool> CategoryHasProductsAsync(int id, CancellationToken cancellationToken = default);
    Task SetProductCategoryAsync(int productId, int? categoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductBarcode>> GetBarcodesAsync(int productId, CancellationToken cancellationToken = default);
    Task<ProductBarcode?> FindBarcodeAsync(string value, CancellationToken cancellationToken = default);
    Task<ProductBarcode> AddBarcodeAsync(ProductBarcode barcode, CancellationToken cancellationToken = default);
    Task<ProductBarcode?> GetBarcodeAsync(int id, CancellationToken cancellationToken = default);
    Task DeleteBarcodeAsync(ProductBarcode barcode, CancellationToken cancellationToken = default);
}
