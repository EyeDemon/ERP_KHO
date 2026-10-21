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
        [FromQuery] string? code = null,
        [FromQuery] string? barcode = null,
        [FromQuery] int? warehouseId = null,
        CancellationToken ct = default)
    {
        var scan = (!string.IsNullOrWhiteSpace(code) ? code : barcode)?.Trim();
        if (string.IsNullOrWhiteSpace(scan))
            throw new BusinessRuleException("Mã vạch quét không được để trống.");

        if (TryParseGs1128(scan, out var gs1Parsed))
        {
            var gs1Resolved = await ResolveGs1128Async(scan, gs1Parsed, warehouseId, ct);
            return Ok(gs1Resolved);
        }

        // Verified non-empty scan
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

    public sealed record ParseGs1Request(
        string? Barcode = null,
        string? RawBarcode = null,
        string? Gs1Code = null,
        int? WarehouseId = null);

    public sealed record Gs1ParsedResult(
        bool IsGs1128,
        string? Gtin,
        string? NormalizedBarcode,
        string? LotNumber,
        string? ExpiryDateRaw,
        string? ExpiryDate,
        string? ExpiryDateDisplay,
        string? SerialNumber,
        string? Sscc);

    [HttpPost("parse-gs1")]
    public async Task<IActionResult> ParseGs1(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ParseGs1Request? request = null,
        [FromQuery] string? barcode = null,
        [FromQuery] int? warehouseId = null,
        CancellationToken ct = default)
    {
        var raw = (request?.Barcode ?? request?.RawBarcode ?? request?.Gs1Code ?? barcode)?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
            throw new BusinessRuleException("Chuỗi mã vạch GS1-128 không được để trống.");

        var effectiveWarehouseId = request?.WarehouseId ?? warehouseId;
        if (TryParseGs1128(raw, out var parsed))
        {
            var resolved = await ResolveGs1128Async(raw, parsed, effectiveWarehouseId, ct);
            return Ok(resolved);
        }

        var fallbackDto = new BarcodeResolutionDto
        {
            EntityType = "Product",
            Barcode = raw,
            Code = raw,
            Name = raw,
            DisplayName = raw,
            IsGs1128 = false,
            IsGs1Composite = false,
            Gtin = raw.Length is 13 or 14 ? raw : null,
            Gs1Gtin = raw.Length is 13 or 14 ? raw : null,
            NormalizedBarcode = raw,
            Status = "Mã vạch tiêu chuẩn (EAN/SKU)",
            StatusLabel = "Mã vạch tiêu chuẩn (EAN/SKU)"
        };

        if (db is not null)
        {
            var tenantId = db.CurrentTenantId;
            var matchedBarcode = await db.ProductBarcodes.AsNoTracking()
                .Include(b => b.Product)
                .Where(b => b.TenantId == tenantId && b.Value == raw)
                .Select(b => new { b.ProductId, b.Product.Code, b.Product.Name })
                .FirstOrDefaultAsync(ct);
            if (matchedBarcode is not null)
            {
                fallbackDto.EntityId = matchedBarcode.ProductId;
                fallbackDto.ProductId = matchedBarcode.ProductId;
                fallbackDto.Code = matchedBarcode.Code.Trim();
                fallbackDto.ProductCode = matchedBarcode.Code.Trim();
                fallbackDto.Name = matchedBarcode.Name.Trim();
                fallbackDto.ProductName = matchedBarcode.Name.Trim();
                fallbackDto.DisplayName = $"{matchedBarcode.Code.Trim()} - {matchedBarcode.Name.Trim()}";
            }
        }

        return Ok(fallbackDto);
    }

    private async Task<BarcodeResolutionDto> ResolveGs1128Async(
        string rawScan,
        Gs1ParsedResult parsed,
        int? warehouseId,
        CancellationToken ct)
    {
        int? productId = null;
        string? productCode = null;
        string? productName = null;
        int? lotId = null;
        int? serialId = null;
        int? handlingUnitId = null;

        if (db is not null)
        {
            var tenantId = db.CurrentTenantId;
            if (!string.IsNullOrWhiteSpace(parsed.Gtin))
            {
                var gtin = parsed.Gtin.Trim();
                var shortEan = parsed.NormalizedBarcode ?? gtin;

                var pb = await db.ProductBarcodes.AsNoTracking()
                    .Include(x => x.Product)
                    .Where(x => x.TenantId == tenantId && (x.Value == gtin || x.Value == shortEan))
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
                        .Where(x => x.TenantId == tenantId && x.IsActive && (x.Code == gtin || x.Code == shortEan))
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

            if (!string.IsNullOrWhiteSpace(parsed.LotNumber))
            {
                var lotCode = parsed.LotNumber.Trim();
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

            if (!string.IsNullOrWhiteSpace(parsed.SerialNumber))
            {
                var sn = parsed.SerialNumber.Trim();
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

            if (!string.IsNullOrWhiteSpace(parsed.Sscc))
            {
                var sscc = parsed.Sscc.Trim();
                var hu = await db.HandlingUnits.AsNoTracking()
                    .Where(x => x.TenantId == tenantId && (x.Sscc == sscc || x.Barcode == sscc || x.HuCode == sscc))
                    .Select(x => new { x.Id })
                    .FirstOrDefaultAsync(ct);
                if (hu is not null)
                    handlingUnitId = hu.Id;
            }
        }

        if (!productId.HasValue && service is not null && !string.IsNullOrWhiteSpace(parsed.NormalizedBarcode))
        {
            try
            {
                var lookedUp = await service.LookupBarcodeAsync(parsed.NormalizedBarcode, ct);
                if (lookedUp is not null)
                {
                    productId = lookedUp.Id;
                    productCode = lookedUp.Code.Trim();
                    productName = lookedUp.Name.Trim();
                }
            }
            catch
            {
                // Ignore fallback lookup exception when barcode is not registered in mock service
            }
        }

        var primaryCode = productCode ?? parsed.NormalizedBarcode ?? parsed.Gtin ?? parsed.Sscc ?? rawScan;
        var primaryName = productName ?? "Sản phẩm GS1-128";
        var entityType = !string.IsNullOrWhiteSpace(parsed.Sscc) && string.IsNullOrWhiteSpace(parsed.Gtin)
            ? "HandlingUnit"
            : "Product";
        var entityId = productId ?? lotId ?? serialId ?? handlingUnitId ?? 0;

        return new BarcodeResolutionDto
        {
            EntityType = entityType,
            EntityId = entityId,
            Code = primaryCode,
            Name = primaryName,
            Barcode = rawScan,
            DisplayName = $"{primaryCode} - {primaryName}" + (!string.IsNullOrWhiteSpace(parsed.LotNumber) ? $" (Lô: {parsed.LotNumber})" : string.Empty),
            SubType = "GS1-128",
            WarehouseId = warehouseId,
            ProductId = productId,
            ProductCode = productCode ?? primaryCode,
            ProductName = productName ?? primaryName,
            IsGs1128 = true,
            IsGs1Composite = true,
            Gtin = parsed.Gtin,
            Gs1Gtin = parsed.Gtin,
            NormalizedBarcode = parsed.NormalizedBarcode,
            LotId = lotId,
            LotNumber = parsed.LotNumber,
            Gs1LotNumber = parsed.LotNumber,
            ExpiryDate = parsed.ExpiryDate,
            Gs1ExpiryDate = parsed.ExpiryDate,
            ExpiryDateDisplay = parsed.ExpiryDateDisplay,
            SerialId = serialId,
            SerialNumber = parsed.SerialNumber,
            Gs1SerialNumber = parsed.SerialNumber,
            HandlingUnitId = handlingUnitId,
            Sscc = parsed.Sscc,
            Gs1Sscc = parsed.Sscc,
            Status = "Đã giải mã thành công chuỗi mã vạch chuẩn GS1-128",
            StatusLabel = "Đã giải mã thành công chuỗi mã vạch chuẩn GS1-128"
        };
    }

    public static bool TryParseGs1128(string? rawInput, out Gs1ParsedResult result)
    {
        result = new Gs1ParsedResult(false, null, null, null, null, null, null, null, null);
        if (string.IsNullOrWhiteSpace(rawInput))
            return false;

        var s = rawInput.Trim();
        if (s.StartsWith("]C1", StringComparison.OrdinalIgnoreCase))
            s = s[3..].Trim();

        string? gtin = null;
        string? lotNumber = null;
        string? expiryRaw = null;
        string? serialNumber = null;
        string? sscc = null;

        if (s.Contains('(') && s.Contains(')'))
        {
            var idx = 0;
            while (idx < s.Length)
            {
                var openIdx = s.IndexOf('(', idx);
                if (openIdx < 0) break;
                var closeIdx = s.IndexOf(')', openIdx + 1);
                if (closeIdx < 0) break;

                var ai = s[(openIdx + 1)..closeIdx].Trim();
                var nextOpen = s.IndexOf('(', closeIdx + 1);
                var val = (nextOpen >= 0 ? s[(closeIdx + 1)..nextOpen] : s[(closeIdx + 1)..])
                    .Trim()
                    .Trim('\u001d');

                switch (ai)
                {
                    case "00":
                        sscc = val;
                        break;
                    case "01":
                    case "02":
                        gtin = val;
                        break;
                    case "10":
                        lotNumber = val;
                        break;
                    case "17":
                    case "15":
                        expiryRaw = val;
                        break;
                    case "21":
                        serialNumber = val;
                        break;
                }

                idx = nextOpen >= 0 ? nextOpen : s.Length;
            }
        }
        else if (s.Length >= 16 && (s.StartsWith("01", StringComparison.Ordinal) || s.StartsWith("00", StringComparison.Ordinal)))
        {
            var pos = 0;
            while (pos + 2 <= s.Length)
            {
                if (s[pos] == '\u001d')
                {
                    pos++;
                    continue;
                }

                var ai2 = s.Substring(pos, 2);
                if (ai2 == "00" && pos + 20 <= s.Length)
                {
                    sscc = s.Substring(pos + 2, 18);
                    pos += 20;
                }
                else if ((ai2 == "01" || ai2 == "02") && pos + 16 <= s.Length)
                {
                    gtin = s.Substring(pos + 2, 14);
                    pos += 16;
                }
                else if ((ai2 == "17" || ai2 == "15") && pos + 8 <= s.Length)
                {
                    expiryRaw = s.Substring(pos + 2, 6);
                    pos += 8;
                }
                else if (ai2 == "10")
                {
                    pos += 2;
                    var gsIdx = s.IndexOf('\u001d', pos);
                    if (gsIdx >= 0)
                    {
                        lotNumber = s[pos..gsIdx];
                        pos = gsIdx + 1;
                    }
                    else
                    {
                        var remaining = s[pos..];
                        var next17 = remaining.IndexOf("17", StringComparison.Ordinal);
                        if (next17 > 0 && next17 + 8 == remaining.Length && remaining[(next17 + 2)..].All(char.IsDigit))
                        {
                            lotNumber = remaining[..next17];
                            expiryRaw = remaining.Substring(next17 + 2, 6);
                            pos = s.Length;
                        }
                        else
                        {
                            lotNumber = remaining;
                            pos = s.Length;
                        }
                    }
                }
                else if (ai2 == "21")
                {
                    pos += 2;
                    var gsIdx = s.IndexOf('\u001d', pos);
                    if (gsIdx >= 0)
                    {
                        serialNumber = s[pos..gsIdx];
                        pos = gsIdx + 1;
                    }
                    else
                    {
                        serialNumber = s[pos..];
                        pos = s.Length;
                    }
                }
                else
                {
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(gtin) && string.IsNullOrWhiteSpace(sscc) && string.IsNullOrWhiteSpace(lotNumber) && string.IsNullOrWhiteSpace(serialNumber))
            return false;

        var normalizedBarcode = gtin;
        if (!string.IsNullOrWhiteSpace(gtin) && gtin.Length == 14 && gtin.StartsWith('0'))
            normalizedBarcode = gtin[1..];

        string? expiryIso = null;
        string? expiryDisplay = null;
        if (!string.IsNullOrWhiteSpace(expiryRaw) && expiryRaw.Length == 6 && expiryRaw.All(char.IsDigit))
        {
            var yy = int.Parse(expiryRaw[..2], System.Globalization.CultureInfo.InvariantCulture);
            var mm = int.Parse(expiryRaw.Substring(2, 2), System.Globalization.CultureInfo.InvariantCulture);
            var dd = int.Parse(expiryRaw.Substring(4, 2), System.Globalization.CultureInfo.InvariantCulture);
            var year = 2000 + yy;
            if (mm is >= 1 and <= 12)
            {
                var maxDay = DateTime.DaysInMonth(year, mm);
                var day = dd == 0 ? maxDay : Math.Min(dd, maxDay);
                expiryIso = $"{year:D4}-{mm:D2}-{day:D2}";
                expiryDisplay = $"{day:D2}/{mm:D2}/{year:D4}";
            }
        }

        result = new Gs1ParsedResult(
            true,
            gtin,
            normalizedBarcode,
            lotNumber,
            expiryRaw,
            expiryIso,
            expiryDisplay,
            serialNumber,
            sscc);
        return true;
    }

    private IActionResult UnusedMarker()
    {
        return Ok(new BarcodeResolutionDto
        {
            ProductName = string.Empty
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
    public bool IsGs1128 { get; set; }
    public bool IsGs1Composite { get; set; }
    public string? Gtin { get; set; }
    public string? Gs1Gtin { get; set; }
    public string? NormalizedBarcode { get; set; }
    public int? LotId { get; set; }
    public string? LotNumber { get; set; }
    public string? Gs1LotNumber { get; set; }
    public string? ExpiryDate { get; set; }
    public string? Gs1ExpiryDate { get; set; }
    public string? ExpiryDateDisplay { get; set; }
    public int? SerialId { get; set; }
    public string? SerialNumber { get; set; }
    public string? Gs1SerialNumber { get; set; }
    public int? HandlingUnitId { get; set; }
    public string? Sscc { get; set; }
    public string? Gs1Sscc { get; set; }
    public string? Status { get; set; }
    public string? StatusLabel { get; set; }
}
