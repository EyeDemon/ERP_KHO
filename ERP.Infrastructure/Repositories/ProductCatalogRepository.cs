using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Repositories;

public sealed class ProductCatalogRepository(ErpKhoDbContext context) : IProductCatalogRepository
{
    public async Task<Product> AddProductAsync(Product product, CancellationToken ct = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            if (product.CategoryId.HasValue &&
                !await context.ProductCategories.AnyAsync(x => x.Id == product.CategoryId.Value && x.IsActive, ct))
                throw new BusinessRuleException("Danh mục không tồn tại hoặc đang ngừng hoạt động.");

            context.Products.Add(product);
            await SaveAsync("Mã sản phẩm đã tồn tại hoặc danh mục không còn khả dụng.", ct);
            await transaction.CommitAsync(ct);
            return product;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<ProductCategory>> GetCategoriesAsync(CancellationToken ct = default) =>
        await context.ProductCategories.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct);

    public Task<ProductCategory?> GetCategoryAsync(int id, CancellationToken ct = default) =>
        context.ProductCategories.SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> CategoryCodeExistsAsync(string code, CancellationToken ct = default) =>
        context.ProductCategories.AnyAsync(x => x.Code == code, ct);

    public async Task<ProductCategory> AddCategoryAsync(ProductCategory category, CancellationToken ct = default)
    {
        context.ProductCategories.Add(category);
        await SaveAsync("Mã danh mục đã tồn tại.", ct);
        return category;
    }

    public async Task UpdateCategoryAsync(ProductCategory category, CancellationToken ct = default)
    {
        await SaveAsync("Danh mục đã được thay đổi đồng thời.", ct);
    }

    public async Task DeleteCategoryAsync(ProductCategory category, CancellationToken ct = default)
    {
        context.ProductCategories.Remove(category);
        await SaveAsync("Không thể xóa danh mục đang được sản phẩm sử dụng.", ct);
    }

    public Task<bool> CategoryHasProductsAsync(int id, CancellationToken ct = default) =>
        context.Products.AnyAsync(x => x.CategoryId == id, ct);

    public async Task SetProductCategoryAsync(int productId, int? categoryId, CancellationToken ct = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            if (categoryId.HasValue && !await context.ProductCategories.AnyAsync(x => x.Id == categoryId.Value && x.IsActive, ct))
                throw new BusinessRuleException("Danh mục không tồn tại hoặc đang ngừng hoạt động.");
            var product = await context.Products.SingleOrDefaultAsync(x => x.Id == productId, ct)
                ?? throw new ERP.Application.Exceptions.NotFoundException($"Không tìm thấy sản phẩm id {productId}");
            product.CategoryId = categoryId;
            product.UpdatedAt = DateTime.UtcNow;
            await SaveAsync("Danh mục không còn khả dụng hoặc dữ liệu đã thay đổi.", ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<ProductBarcode>> GetBarcodesAsync(int productId, CancellationToken ct = default) =>
        await context.ProductBarcodes.AsNoTracking().Where(x => x.ProductId == productId).OrderBy(x => x.Id).ToListAsync(ct);

    public Task<ProductBarcode?> FindBarcodeAsync(string value, CancellationToken ct = default) =>
        context.ProductBarcodes.AsNoTracking().Include(x => x.Product).ThenInclude(x => x.Unit)
            .Include(x => x.Product).ThenInclude(x => x.Category).Include(x => x.Product).ThenInclude(x => x.Barcodes)
            .SingleOrDefaultAsync(x => x.Value == value, ct);

    public async Task<ProductBarcode> AddBarcodeAsync(ProductBarcode barcode, CancellationToken ct = default)
    {
        context.ProductBarcodes.Add(barcode);
        await SaveAsync("Barcode đã được sử dụng cho sản phẩm khác.", ct);
        return barcode;
    }

    public Task<ProductBarcode?> GetBarcodeAsync(int id, CancellationToken ct = default) =>
        context.ProductBarcodes.SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task DeleteBarcodeAsync(ProductBarcode barcode, CancellationToken ct = default)
    {
        context.ProductBarcodes.Remove(barcode);
        await SaveAsync("Barcode đã được thay đổi đồng thời.", ct);
    }

    private async Task SaveAsync(string conflictMessage, CancellationToken ct)
    {
        try { await context.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 or 547 })
        {
            var error = new BusinessRuleException(conflictMessage, ex);
            error.Data["HttpStatusCode"] = 409;
            throw error;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu đã được thay đổi bởi người khác.", ex);
        }
    }
}
