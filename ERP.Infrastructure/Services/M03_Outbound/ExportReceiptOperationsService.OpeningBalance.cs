using System.Data;
using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ExportReceiptOperationsService
{
    public async Task<WmsEndpointOutcome> ImportOpeningBalanceAsync(
        ImportOpeningBalanceInput? request,
        IReadOnlyList<OpeningBalanceItemInput> parsedFileItems,
        int? actorUserId,
        bool isAuthenticated,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var validAuditUserId = await ResolveValidAuditUserIdAsync(actorUserId, isAuthenticated, currentUserOverride, cancellationToken);
        var resolvedActorId = validAuditUserId ?? (actorUserId is > 0 ? actorUserId.Value : 1);
        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        var effectiveUser = currentUserOverride ?? _currentUser;

        int warehouseId;
        if (request?.WarehouseId.HasValue == true)
        {
            warehouseId = request.WarehouseId.Value;
            if (warehouseId > 0 && effectiveWhAccess != null)
            {
                await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);
            }
        }
        else if (effectiveWhAccess != null && (effectiveUser == null || (effectiveUser.IsAuthenticated && !effectiveUser.IsGlobalAdmin)))
        {
            var accessibleIds = await effectiveWhAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
            if (accessibleIds.Count == 0)
            {
                return WmsEndpointOutcome.Forbid();
            }
            warehouseId = await _db.Warehouses.AsNoTracking()
                .Where(w => accessibleIds.Contains(w.Id))
                .OrderBy(w => w.Id)
                .Select(w => w.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (warehouseId <= 0)
            {
                warehouseId = accessibleIds.First();
            }
        }
        else
        {
            warehouseId = await _db.Warehouses.AsNoTracking()
                .OrderBy(w => w.Id)
                .Select(w => w.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (warehouseId <= 0) warehouseId = 1;
        }

        var items = new List<OpeningBalanceItemInput>();
        if (parsedFileItems is { Count: > 0 })
        {
            items.AddRange(parsedFileItems);
        }
        else if (request?.Items is { Count: > 0 })
        {
            items.AddRange(request.Items);
        }
        else if (request?.ProductId is > 0 || request?.Quantity is > 0m)
        {
            items.Add(new OpeningBalanceItemInput(
                ProductId: request.ProductId,
                LocationId: request.LocationId,
                Quantity: request.Quantity ?? 0m,
                UnitCost: request.UnitCost ?? 0m));
        }

        if (items.Count == 0)
        {
            return WmsEndpointOutcome.BadRequest(new
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

        var resolvedProducts = new List<(OpeningBalanceItemInput Item, Product Product)>();
        foreach (var item in items)
        {
            if (item.Quantity <= 0m)
            {
                return WmsEndpointOutcome.BadRequest(new { code = "INVALID_OPENING_QUANTITY", errorCode = "INVALID_OPENING_QUANTITY", message = "Số lượng tồn đầu kỳ phải lớn hơn 0." });
            }
            if (item.UnitCost < 0m)
            {
                return WmsEndpointOutcome.BadRequest(new { code = "INVALID_OPENING_COST", errorCode = "INVALID_OPENING_COST", message = "Đơn giá vốn đầu kỳ không được âm." });
            }

            Product? product = null;
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
                return WmsEndpointOutcome.BadRequest(new
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
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        var lockProductIds = resolvedProducts.Select(x => x.Product.Id).Distinct().ToList();
        if (lockProductIds.Count > 0)
        {
            await _db.AcquireInventoryAllocationLocksAsync(warehouseId, lockProductIds, cancellationToken);
        }

        var importedItems = new List<object>();

        foreach (var (item, product) in resolvedProducts)
        {
            var (locId, resolvedLocationCode) = await ResolveOpeningLocationAsync(item, warehouseId, defaultLocId, cancellationToken);
            var lotId = await ResolveOpeningLotAsync(item, product.Id, now, cancellationToken);

            var stock = await _db.InventoryStocks.FirstOrDefaultAsync(
                s => s.WarehouseId == warehouseId &&
                     s.ProductId == product.Id &&
                     s.LocationId == locId &&
                     s.LotId == lotId &&
                     s.SerialId == null &&
                     s.HandlingUnitId == null &&
                     s.OwnerId == null &&
                     s.Status == InventoryStatus.Available,
                cancellationToken);

            if (stock == null)
            {
                stock = new InventoryStock
                {
                    WarehouseId = warehouseId,
                    ProductId = product.Id,
                    LocationId = locId > 0 ? locId : null,
                    LotId = lotId,
                    Status = InventoryStatus.Available,
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

            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductId = product.Id,
                WarehouseId = warehouseId,
                LocationId = locId > 0 ? locId : null,
                LotId = lotId,
                TransactionType = TransactionType.Import,
                Quantity = item.Quantity,
                UnitCost = item.UnitCost,
                ReferenceId = product.Id,
                ReferenceType = "OPENING_BALANCE",
                CreatedBy = resolvedActorId,
                TransactionDate = now,
                Note = $"Nhập tồn kho đầu kỳ ({item.Quantity:0.##} x {item.UnitCost:0.##}đ)"
            });

            _db.IntegrationOutboxMessages.Add(new IntegrationOutboxMessage
            {
                EventId = $"OPN-VAL-{warehouseId}-{product.Id}-{now.Ticks}",
                EventType = "InventoryValuationPosted",
                AggregateType = "InventoryStock",
                AggregateId = product.Id,
                PayloadJson = JsonSerializer.Serialize(new
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

        _db.AuditLogs.Add(new AuditLog
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

        return WmsEndpointOutcome.Ok(new
        {
            warehouseId,
            importedCount = importedItems.Count,
            referenceType = "OPENING_BALANCE",
            items = importedItems,
            importedAtUtc = now,
            statusLabel = $"Đã nạp tồn kho đầu kỳ ({importedItems.Count} dòng) & khởi tạo giá vốn bình quân gia quyền (MAC)"
        });
    }

    private async Task<(int? LocationId, string? LocationCode)> ResolveOpeningLocationAsync(
        OpeningBalanceItemInput item,
        int warehouseId,
        int defaultLocId,
        CancellationToken cancellationToken)
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
                locationEntity = new WarehouseLocation
                {
                    WarehouseId = warehouseId,
                    Code = codeNorm,
                    Name = $"Kệ {codeNorm}",
                    LocationType = WarehouseLocationType.Storage,
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
        return (locId, resolvedLocationCode);
    }

    private async Task<int?> ResolveOpeningLotAsync(
        OpeningBalanceItemInput item,
        int productId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.LotNumber))
            return null;

        var lotNo = item.LotNumber.Trim().ToUpperInvariant();
        var lotEntity = await _db.InventoryLots
            .FirstOrDefaultAsync(l => l.ProductId == productId && l.LotNumber == lotNo, cancellationToken);
        if (lotEntity == null)
        {
            lotEntity = new InventoryLot
            {
                ProductId = productId,
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
        return lotEntity.Id;
    }
}
