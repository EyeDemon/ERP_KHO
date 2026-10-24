using System.Security.Claims;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/data-imports")]
[Route("api/data-imports")]
public sealed class DataImportsController : ControllerBase
{
    private readonly ErpKhoDbContext? _db;
    private readonly IWarehouseAuthorizationService? _warehouseAccess;
    private readonly ICurrentUser? _currentUser;

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public DataImportsController(
        ErpKhoDbContext? db = null,
        IWarehouseAuthorizationService? warehouseAccess = null,
        ICurrentUser? currentUser = null)
    {
        _db = db;
        _warehouseAccess = warehouseAccess;
        _currentUser = currentUser;
    }

    public DataImportsController(
        IExportReceiptService? exportReceiptService,
        ErpKhoDbContext? db = null,
        IWarehouseAuthorizationService? warehouseAccess = null,
        ICurrentUser? currentUser = null)
        : this(db, warehouseAccess, currentUser)
    {
        _ = exportReceiptService;
    }

    [HttpGet("templates")]
    [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public IActionResult GetDataImportTemplates()
    {
        return Ok(new
        {
            templates = new[]
            {
                new
                {
                    templateCode = "OPENING_BALANCE_EXCEL",
                    templateName = "Mẫu Excel Nhập Tồn Kho Đầu Kỳ & Giá Vốn Bình Quân (MAC)",
                    downloadUrl = "/api/v1/import-templates/opening-balance",
                    requiredColumns = new[] { "ProductCode", "LocationCode", "Quantity", "UnitCost", "LotNumber", "ExpiryDate" }
                },
                new
                {
                    templateCode = "PRODUCT_MASTER_EXCEL",
                    templateName = "Mẫu Excel Danh Mục Sản Phẩm & Quy Đổi ĐVT",
                    downloadUrl = "/api/v1/import-templates/products",
                    requiredColumns = new[] { "ProductCode", "ProductName", "BaseUomCode", "Barcode", "MovingAverageCost" }
                }
            },
            statusLabel = "Đã tải danh mục biểu mẫu nhập liệu Excel đầu kỳ chuẩn"
        });
    }

    [HttpPost("opening-balance")]
    [PermissionAuthorize(AppPermissions.InventoryOpeningBalanceImport, AppPermissions.InventoryWrite)]
    [IdempotentCommand("Inventory.ImportOpeningBalance")]
    public async Task<IActionResult> ImportOpeningBalance(
        [ModelBinder(BinderType = typeof(FlexibleBodyOrFormModelBinder))] ExportReceiptsController.ImportOpeningBalanceRequest? request = null,
        CancellationToken cancellationToken = default,
        [ModelBinder(BinderType = typeof(FlexibleFormFileModelBinder))] Microsoft.AspNetCore.Http.IFormFile? file = null)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        if (_db == null)
        {
            return StatusCode(503, new { code = "DB_UNAVAILABLE", message = "Cơ sở dữ liệu chưa sẵn sàng." });
        }

        var now = DateTime.UtcNow;
        int? validAuditUserId = null;
        var userIdStr = User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User?.FindFirstValue("Id")
            ?? (_currentUser?.UserId > 0 ? _currentUser.UserId.ToString() : null);
        if (int.TryParse(userIdStr, out var parsedUid) &&
            await _db.Users.AsNoTracking().AnyAsync(u => u.Id == parsedUid, cancellationToken))
        {
            validAuditUserId = parsedUid;
        }

        var actorUserId = validAuditUserId ?? (int.TryParse(userIdStr, out var fallbackUid) ? fallbackUid : 1);
        var warehouseId = request?.WarehouseId ?? await _db.Warehouses.AsNoTracking().OrderBy(w => w.Id).Select(w => w.Id).FirstOrDefaultAsync(cancellationToken);
        if (warehouseId <= 0) warehouseId = 1;

        if (warehouseId > 0 && _warehouseAccess != null)
        {
            await _warehouseAccess.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);
        }

        var items = new List<ExportReceiptsController.OpeningBalanceItemRequest>();
        var uploadedFile = file ?? (Request?.HasFormContentType == true && Request.Form.Files.Count > 0 ? Request.Form.Files[0] : null);
        if (uploadedFile != null && uploadedFile.Length > 0)
        {
            using var stream = uploadedFile.OpenReadStream();
            using var wb = new ClosedXML.Excel.XLWorkbook(stream);
            var ws = wb.Worksheets.First();
            foreach (var row in ws.RowsUsed().Skip(1))
            {
                var pCode = row.Cell(1).GetString()?.Trim();
                var locCode = row.Cell(2).GetString()?.Trim();
                var qtyVal = row.Cell(3).TryGetValue<decimal>(out var q)
                    ? q
                    : (decimal.TryParse(row.Cell(3).GetString()?.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var qParsed) ? qParsed : 0m);
                var costVal = row.Cell(4).TryGetValue<decimal>(out var c)
                    ? c
                    : (decimal.TryParse(row.Cell(4).GetString()?.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var cParsed) ? cParsed : 0m);
                var lotNumber = row.Cell(5).GetString()?.Trim();
                DateTime? expiryDate = null;
                if (row.Cell(6).TryGetValue<DateTime>(out var dt))
                {
                    expiryDate = DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);
                }
                else if (DateTime.TryParse(row.Cell(6).GetString()?.Trim(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var dtParsed))
                {
                    expiryDate = DateTime.SpecifyKind(dtParsed.Date, DateTimeKind.Utc);
                }

                if (!string.IsNullOrWhiteSpace(pCode) && qtyVal > 0m)
                {
                    items.Add(new ExportReceiptsController.OpeningBalanceItemRequest(
                        ProductCode: pCode,
                        LocationCode: string.IsNullOrWhiteSpace(locCode) ? null : locCode,
                        Quantity: qtyVal,
                        UnitCost: costVal,
                        LotNumber: string.IsNullOrWhiteSpace(lotNumber) ? null : lotNumber,
                        ExpiryDate: expiryDate));
                }
            }
        }

        if (items.Count == 0 && request?.Items is { Count: > 0 })
        {
            items.AddRange(request.Items);
        }
        else if (items.Count == 0 && (request?.ProductId is > 0 || request?.Quantity is > 0m))
        {
            items.Add(new ExportReceiptsController.OpeningBalanceItemRequest(
                ProductId: request.ProductId,
                LocationId: request.LocationId,
                Quantity: request.Quantity ?? 0m,
                UnitCost: request.UnitCost ?? 0m));
        }

        if (items.Count == 0)
        {
            return BadRequest(new
            {
                code = "OPENING_BALANCE_FILE_REQUIRED",
                errorCode = "OPENING_BALANCE_FILE_REQUIRED",
                message = "Vui lòng tải lên tệp Excel (.xlsx) hoặc danh sách dòng tồn kho đầu kỳ hợp lệ."
            });
        }

        var defaultLocId = request?.LocationId ?? await _db.WarehouseLocations
            .AsNoTracking()
            .Where(l => l.WarehouseId == warehouseId && l.IsActive)
            .OrderBy(l => l.Id)
            .Select(l => l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var resolvedProducts = new List<(ExportReceiptsController.OpeningBalanceItemRequest Item, ERP.Domain.Entities.Product Product)>();
        foreach (var item in items)
        {
            if (item.Quantity <= 0m)
            {
                return BadRequest(new { code = "INVALID_OPENING_QUANTITY", errorCode = "INVALID_OPENING_QUANTITY", message = "Số lượng tồn đầu kỳ phải lớn hơn 0." });
            }
            if (item.UnitCost < 0m)
            {
                return BadRequest(new { code = "INVALID_OPENING_COST", errorCode = "INVALID_OPENING_COST", message = "Đơn giá vốn đầu kỳ không được âm." });
            }

            ERP.Domain.Entities.Product? product = null;
            if (item.ProductId is > 0)
            {
                product = await _db.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId.Value, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(item.ProductCode))
            {
                var pCode = item.ProductCode.Trim();
                product = await _db.Products.FirstOrDefaultAsync(p => p.Code == pCode, cancellationToken);
            }

            if (product == null)
            {
                return BadRequest(new
                {
                    code = "PRODUCT_NOT_FOUND",
                    errorCode = "PRODUCT_NOT_FOUND",
                    productCode = item.ProductCode,
                    productId = item.ProductId,
                    message = $"Không tìm thấy sản phẩm '{item.ProductCode ?? item.ProductId?.ToString()}'."
                });
            }

            resolvedProducts.Add((item, product));
        }

        await using var sqlTx = _db.Database.CurrentTransaction == null && _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var lockProductIds = resolvedProducts.Select(x => x.Product.Id).Distinct().ToList();
        if (lockProductIds.Count > 0)
        {
            await _db.AcquireInventoryAllocationLocksAsync(warehouseId, lockProductIds, cancellationToken);
        }

        var importedItems = new List<object>();

        foreach (var (item, product) in resolvedProducts)
        {
            int? locId = item.LocationId;
            string? resolvedLocationCode = item.LocationCode;
            if ((!locId.HasValue || locId <= 0) && !string.IsNullOrWhiteSpace(item.LocationCode))
            {
                var codeNorm = item.LocationCode.Trim().ToUpperInvariant();
                var locationEntity = await _db.WarehouseLocations
                    .FirstOrDefaultAsync(l => l.WarehouseId == warehouseId && l.Code == codeNorm, cancellationToken);
                if (locationEntity == null)
                {
                    locationEntity = new ERP.Domain.Entities.WarehouseLocation
                    {
                        WarehouseId = warehouseId,
                        Code = codeNorm,
                        Name = $"Kệ {codeNorm}",
                        LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage,
                        IsActive = true,
                        IsPickable = true,
                        IsReceivable = true
                    };
                    _db.WarehouseLocations.Add(locationEntity);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                locId = locationEntity.Id;
                resolvedLocationCode = locationEntity.Code;
            }
            locId ??= (defaultLocId > 0 ? defaultLocId : null);

            int? lotId = null;
            if (!string.IsNullOrWhiteSpace(item.LotNumber))
            {
                var lotNo = item.LotNumber.Trim().ToUpperInvariant();
                var lotEntity = await _db.InventoryLots
                    .FirstOrDefaultAsync(l => l.ProductId == product.Id && l.LotNumber == lotNo, cancellationToken);
                if (lotEntity == null)
                {
                    lotEntity = new ERP.Domain.Entities.InventoryLot
                    {
                        ProductId = product.Id,
                        LotNumber = lotNo,
                        ManufactureDate = now.Date,
                        ExpiryDate = item.ExpiryDate ?? now.Date.AddYears(2),
                        CreatedAt = now
                    };
                    _db.InventoryLots.Add(lotEntity);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                else if (item.ExpiryDate.HasValue)
                {
                    lotEntity.ExpiryDate = item.ExpiryDate.Value;
                }
                lotId = lotEntity.Id;
            }

            var stock = await _db.InventoryStocks.FirstOrDefaultAsync(
                s => s.WarehouseId == warehouseId &&
                     s.ProductId == product.Id &&
                     s.LocationId == locId &&
                     s.LotId == lotId &&
                     s.SerialId == null &&
                     s.HandlingUnitId == null &&
                     s.OwnerId == null &&
                     s.Status == ERP.Domain.Enums.InventoryStatus.Available,
                cancellationToken);

            if (stock == null)
            {
                stock = new ERP.Domain.Entities.InventoryStock
                {
                    WarehouseId = warehouseId,
                    ProductId = product.Id,
                    LocationId = locId > 0 ? locId : null,
                    LotId = lotId,
                    Status = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = item.Quantity,
                    ReservedQuantity = 0m,
                    AllocatedQuantity = 0m,
                    LockedQuantity = 0m,
                    MovingAverageCost = item.UnitCost,
                    LastUpdated = now
                };
                stock.RefreshGrainHash();
                _db.InventoryStocks.Add(stock);
            }
            else
            {
                var oldQty = Math.Max(0m, stock.Quantity);
                var newTotalQty = oldQty + item.Quantity;
                stock.MovingAverageCost = newTotalQty > 0m
                    ? decimal.Round(((oldQty * stock.MovingAverageCost) + (item.Quantity * item.UnitCost)) / newTotalQty, 4)
                    : item.UnitCost;
                stock.Quantity = newTotalQty;
                stock.LastUpdated = now;
                stock.RefreshGrainHash();
            }

            product.MovingAverageCost = stock.MovingAverageCost;

            var tx = new ERP.Domain.Entities.InventoryTransaction
            {
                ProductId = product.Id,
                WarehouseId = warehouseId,
                LocationId = locId > 0 ? locId : null,
                LotId = lotId,
                TransactionType = ERP.Domain.Enums.TransactionType.Import,
                Quantity = item.Quantity,
                UnitCost = item.UnitCost,
                ReferenceId = product.Id,
                ReferenceType = "OPENING_BALANCE",
                CreatedBy = actorUserId,
                TransactionDate = now,
                Note = $"Nhập tồn kho đầu kỳ ({item.Quantity:0.##} x {item.UnitCost:0.##}đ)"
            };
            _db.InventoryTransactions.Add(tx);

            _db.IntegrationOutboxMessages.Add(new ERP.Domain.Entities.IntegrationOutboxMessage
            {
                EventId = $"OPN-VAL-{warehouseId}-{product.Id}-{now.Ticks}",
                EventType = "InventoryValuationPosted",
                AggregateType = "InventoryStock",
                AggregateId = product.Id,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    warehouseId,
                    productId = product.Id,
                    productCode = product.Code,
                    locationId = locId,
                    locationCode = resolvedLocationCode,
                    lotId,
                    lotNumber = item.LotNumber,
                    expiryDate = item.ExpiryDate,
                    openingQuantity = item.Quantity,
                    unitCost = item.UnitCost,
                    movingAverageCost = stock.MovingAverageCost
                }),
                Status = "PENDING",
                CreatedAt = now
            });

            importedItems.Add(new
            {
                productId = product.Id,
                productCode = product.Code,
                locationId = locId,
                locationCode = resolvedLocationCode,
                lotId,
                lotNumber = item.LotNumber,
                expiryDate = item.ExpiryDate,
                importedQuantity = item.Quantity,
                unitCost = item.UnitCost,
                movingAverageCost = stock.MovingAverageCost,
                referenceType = "OPENING_BALANCE"
            });
        }

        _db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
        {
            UserId = validAuditUserId,
            WarehouseId = warehouseId,
            EntityName = "InventoryStock",
            Action = "Inventory.ImportOpeningBalance",
            NewValues = $"ImportedCount={importedItems.Count}",
            Reason = $"Nạp tồn kho đầu kỳ ({importedItems.Count} dòng)",
            Result = "Success",
            Timestamp = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        if (sqlTx != null)
        {
            await sqlTx.CommitAsync(cancellationToken);
        }

        return Ok(new
        {
            warehouseId,
            importedCount = importedItems.Count,
            referenceType = "OPENING_BALANCE",
            items = importedItems,
            importedAtUtc = now,
            statusLabel = $"Đã nạp tồn kho đầu kỳ ({importedItems.Count} dòng) & khởi tạo giá vốn bình quân gia quyền (MAC)"
        });
    }
}
