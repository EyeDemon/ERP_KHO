using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class WmsGovernanceOperationsService
{
    // -------------------------------------------------------------------------
    // ProductBarcodeLookupController
    // -------------------------------------------------------------------------
    public async Task<WmsBarcodeResolutionResult?> ResolveNonGs1BarcodeAsync(
        string scan,
        int? warehouseId,
        CancellationToken ct = default)
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
            return new WmsBarcodeResolutionResult(
                EntityType: "Location",
                EntityId: location.Id,
                Code: locCode,
                Name: locName,
                Barcode: locCode,
                DisplayName: $"{locCode} - {locName}",
                SubType: location.LocationType,
                WarehouseId: location.WarehouseId);
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
            return new WmsBarcodeResolutionResult(
                EntityType: "HandlingUnit",
                EntityId: hu.Id,
                Code: huCode,
                Name: $"{hu.Type} ({hu.Status})",
                Barcode: huBarcode,
                DisplayName: $"{huCode} ({hu.Type} - {hu.Status})",
                SubType: hu.Type,
                WarehouseId: hu.WarehouseId);
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
            return new WmsBarcodeResolutionResult(
                EntityType: "Serial",
                EntityId: serial.Id,
                Code: sn,
                Name: pName,
                Barcode: sn,
                DisplayName: $"SN: {sn} ({pCode} - {pName})",
                ProductId: serial.ProductId,
                ProductCode: pCode,
                ProductName: pName);
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
            return new WmsBarcodeResolutionResult(
                EntityType: "Lot",
                EntityId: lot.Id,
                Code: lotNo,
                Name: pName,
                Barcode: lotNo,
                DisplayName: $"LOT: {lotNo} ({pCode} - {pName})",
                ProductId: lot.ProductId,
                ProductCode: pCode,
                ProductName: pName);
        }

        var productByCode = await db.Products.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive && x.Code == upper)
            .Select(x => new { x.Id, x.Code, x.Name })
            .FirstOrDefaultAsync(ct);

        if (productByCode is not null)
        {
            var pCode = productByCode.Code.Trim();
            var pName = productByCode.Name.Trim();
            return new WmsBarcodeResolutionResult(
                EntityType: "Product",
                EntityId: productByCode.Id,
                Code: pCode,
                Name: pName,
                Barcode: pCode,
                DisplayName: $"{pCode} - {pName}",
                ProductId: productByCode.Id,
                ProductCode: pCode,
                ProductName: pName);
        }

        return null;
    }

    public async Task<(int ProductId, string Code, string Name)?> FindProductByExactBarcodeAsync(
        string rawBarcode,
        CancellationToken ct = default)
    {
        var tenantId = db.CurrentTenantId;
        var matchedBarcode = await db.ProductBarcodes.AsNoTracking()
            .Include(b => b.Product)
            .Where(b => b.TenantId == tenantId && b.Value == rawBarcode)
            .Select(b => new { b.ProductId, b.Product.Code, b.Product.Name })
            .FirstOrDefaultAsync(ct);

        if (matchedBarcode is null)
            return null;

        return (matchedBarcode.ProductId, matchedBarcode.Code.Trim(), matchedBarcode.Name.Trim());
    }

    public async Task<WmsGs1EntityLookupResult> ResolveGs1EntitiesAsync(
        string? gtin,
        string? normalizedBarcode,
        string? lotNumber,
        string? serialNumber,
        string? sscc,
        CancellationToken ct = default)
    {
        int? productId = null;
        string? productCode = null;
        string? productName = null;
        int? lotId = null;
        int? serialId = null;
        int? handlingUnitId = null;

        var tenantId = db.CurrentTenantId;
        if (!string.IsNullOrWhiteSpace(gtin))
        {
            var trimmedGtin = gtin.Trim();
            var shortEan = normalizedBarcode ?? trimmedGtin;

            var pb = await db.ProductBarcodes.AsNoTracking()
                .Include(x => x.Product)
                .Where(x => x.TenantId == tenantId && (x.Value == trimmedGtin || x.Value == shortEan))
                .Select(x => new { x.ProductId, x.Product.Code, x.Product.Name })
                .FirstOrDefaultAsync(ct);

            if (pb is not null)
            {
                productId = pb.ProductId;
                productCode = pb.Code.Trim();
                productName = pb.Name.Trim();
            }
            else
            {
                var prod = await db.Products.AsNoTracking()
                    .Where(x => x.TenantId == tenantId && x.IsActive && (x.Code == trimmedGtin || x.Code == shortEan))
                    .Select(x => new { x.Id, x.Code, x.Name })
                    .FirstOrDefaultAsync(ct);
                if (prod is not null)
                {
                    productId = prod.Id;
                    productCode = prod.Code.Trim();
                    productName = prod.Name.Trim();
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(lotNumber))
        {
            var lotCode = lotNumber.Trim();
            var lotQuery = db.InventoryLots.AsNoTracking()
                .Include(x => x.Product)
                .Where(x => x.TenantId == tenantId && x.LotNumber == lotCode);
            if (productId.HasValue)
                lotQuery = lotQuery.Where(x => x.ProductId == productId.Value);

            var lot = await lotQuery
                .Select(x => new { x.Id, x.ProductId, x.Product.Code, x.Product.Name })
                .FirstOrDefaultAsync(ct);
            if (lot is not null)
            {
                lotId = lot.Id;
                productId ??= lot.ProductId;
                productCode ??= lot.Code.Trim();
                productName ??= lot.Name.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            var sn = serialNumber.Trim();
            var serial = await db.InventorySerials.AsNoTracking()
                .Include(x => x.Product)
                .Where(x => x.TenantId == tenantId && x.SerialNumber == sn)
                .Select(x => new { x.Id, x.ProductId, x.Product.Code, x.Product.Name })
                .FirstOrDefaultAsync(ct);
            if (serial is not null)
            {
                serialId = serial.Id;
                productId ??= serial.ProductId;
                productCode ??= serial.Code.Trim();
                productName ??= serial.Name.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(sscc))
        {
            var trimmedSscc = sscc.Trim();
            var hu = await db.HandlingUnits.AsNoTracking()
                .Where(x => x.TenantId == tenantId && (x.Sscc == trimmedSscc || x.Barcode == trimmedSscc || x.HuCode == trimmedSscc))
                .Select(x => new { x.Id })
                .FirstOrDefaultAsync(ct);
            if (hu is not null)
                handlingUnitId = hu.Id;
        }

        return new WmsGs1EntityLookupResult(productId, productCode, productName, lotId, serialId, handlingUnitId);
    }
}
