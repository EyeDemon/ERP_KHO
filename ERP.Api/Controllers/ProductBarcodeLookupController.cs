using ERP.Api.Authorization;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[Authorize, PermissionAuthorize(AppPermissions.ProductRead)]
[ApiController]
[Route("api/product-barcodes")]
[Route("api/barcode-lookup")]
public sealed class ProductBarcodeLookupController(
    IProductCatalogService service,
    ErpKhoDbContext? db = null) : ControllerBase
{
    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery] string value, CancellationToken ct) =>
        Ok(await service.LookupBarcodeAsync(value, ct));

    [HttpGet("resolve")]
    public async Task<IActionResult> Resolve(
        [FromQuery] string code,
        [FromQuery] int? warehouseId = null,
        CancellationToken ct = default)
    {
        var scan = code?.Trim();
        if (string.IsNullOrWhiteSpace(scan))
            throw new BusinessRuleException("Mã vạch quét không được để trống.");

        if (db is not null)
        {
            var tenantId = db.CurrentTenantId;
            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                var whExistsInTenant = await db.Warehouses.AsNoTracking()
                    .AnyAsync(w => w.Id == warehouseId.Value && w.TenantId == tenantId, ct);
                if (!whExistsInTenant)
                    throw new NotFoundException("Không tìm thấy kho hoặc mã vạch trong phạm vi Tenant hiện tại.");
            }

            var upper = scan.ToUpperInvariant();

            var locQuery = db.WarehouseLocations.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.IsActive);
            if (warehouseId.HasValue && warehouseId.Value > 0)
                locQuery = locQuery.Where(x => x.WarehouseId == warehouseId.Value);

            var location = await locQuery
                .Where(x => x.Code == upper)
                .Select(x => new
                {
                    x.Id,
                    x.Code,
                    x.Name,
                    x.WarehouseId,
                    LocationType = x.LocationType.ToString()
                })
                .FirstOrDefaultAsync(ct);

            if (location is not null)
            {
                var locCode = location.Code.Trim();
                var locName = location.Name.Trim();
                return Ok(new BarcodeResolutionDto
                {
                    EntityType = "Location",
                    EntityId = location.Id,
                    Code = locCode,
                    Name = locName,
                    Barcode = locCode,
                    DisplayName = $"{locCode} - {locName}",
                    SubType = location.LocationType,
                    WarehouseId = location.WarehouseId
                });
            }

            var huQuery = db.HandlingUnits.AsNoTracking()
                .Where(x => x.TenantId == tenantId);
            if (warehouseId.HasValue && warehouseId.Value > 0)
                huQuery = huQuery.Where(x => x.WarehouseId == warehouseId.Value);

            var hu = await huQuery
                .Where(x => x.Barcode == upper || x.HuCode == upper || x.Sscc == upper)
                .Select(x => new
                {
                    x.Id,
                    x.HuCode,
                    x.Barcode,
                    x.WarehouseId,
                    Type = x.Type.ToString(),
                    Status = x.Status.ToString()
                })
                .FirstOrDefaultAsync(ct);

            if (hu is not null)
            {
                var huCode = hu.HuCode.Trim();
                var huBarcode = hu.Barcode.Trim();
                return Ok(new BarcodeResolutionDto
                {
                    EntityType = "HandlingUnit",
                    EntityId = hu.Id,
                    Code = huCode,
                    Name = $"{hu.Type} ({hu.Status})",
                    Barcode = huBarcode,
                    DisplayName = $"{huCode} ({hu.Type} - {hu.Status})",
                    SubType = hu.Type,
                    WarehouseId = hu.WarehouseId
                });
            }

            var serial = await db.InventorySerials.AsNoTracking()
                .Include(x => x.Product)
                .Where(x => x.TenantId == tenantId && x.SerialNumber == upper)
                .Select(x => new
                {
                    x.Id,
                    x.SerialNumber,
                    x.ProductId,
                    ProductCode = x.Product.Code,
                    ProductName = x.Product.Name
                })
                .FirstOrDefaultAsync(ct);

            if (serial is not null)
            {
                var sn = serial.SerialNumber.Trim();
                var pCode = serial.ProductCode.Trim();
                var pName = serial.ProductName.Trim();
                return Ok(new BarcodeResolutionDto
                {
                    EntityType = "Serial",
                    EntityId = serial.Id,
                    Code = sn,
                    Name = pName,
                    Barcode = sn,
                    DisplayName = $"SN: {sn} ({pCode} - {pName})",
                    ProductId = serial.ProductId,
                    ProductCode = pCode,
                    ProductName = pName
                });
            }

            var lot = await db.InventoryLots.AsNoTracking()
                .Include(x => x.Product)
                .Where(x => x.TenantId == tenantId && x.LotNumber == upper)
                .Select(x => new
                {
                    x.Id,
                    x.LotNumber,
                    x.ProductId,
                    ProductCode = x.Product.Code,
                    ProductName = x.Product.Name
                })
                .FirstOrDefaultAsync(ct);

            if (lot is not null)
            {
                var lotNo = lot.LotNumber.Trim();
                var pCode = lot.ProductCode.Trim();
                var pName = lot.ProductName.Trim();
                return Ok(new BarcodeResolutionDto
                {
                    EntityType = "Lot",
                    EntityId = lot.Id,
                    Code = lotNo,
                    Name = pName,
                    Barcode = lotNo,
                    DisplayName = $"LOT: {lotNo} ({pCode} - {pName})",
                    ProductId = lot.ProductId,
                    ProductCode = pCode,
                    ProductName = pName
                });
            }

            var productByCode = await db.Products.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.IsActive && x.Code == upper)
                .Select(x => new { x.Id, x.Code, x.Name })
                .FirstOrDefaultAsync(ct);

            if (productByCode is not null)
            {
                var pCode = productByCode.Code.Trim();
                var pName = productByCode.Name.Trim();
                return Ok(new BarcodeResolutionDto
                {
                    EntityType = "Product",
                    EntityId = productByCode.Id,
                    Code = pCode,
                    Name = pName,
                    Barcode = pCode,
                    DisplayName = $"{pCode} - {pName}",
                    ProductId = productByCode.Id,
                    ProductCode = pCode,
                    ProductName = pName
                });
            }
        }

        var product = await service.LookupBarcodeAsync(scan, ct);
        return Ok(new BarcodeResolutionDto
        {
            EntityType = "Product",
            EntityId = product.Id,
            Code = product.Code.Trim(),
            Name = product.Name.Trim(),
            Barcode = scan,
            DisplayName = $"{product.Code.Trim()} - {product.Name.Trim()}",
            ProductId = product.Id,
            ProductCode = product.Code.Trim(),
            ProductName = product.Name.Trim()
        });
    }
}

public sealed class BarcodeResolutionDto
{
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? SubType { get; set; }
    public int? WarehouseId { get; set; }
    public int? ProductId { get; set; }
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
}
