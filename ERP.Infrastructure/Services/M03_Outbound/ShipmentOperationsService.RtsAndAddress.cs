using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ShipmentOperationsService
{
    public async Task<WmsEndpointOutcome> CompleteRtsReturnAsync(
        int id,
        CompleteShipmentRtsReturnInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var shipment = await db.Shipments
            .Include(s => s.HandlingUnits)
                .ThenInclude(shu => shu.HandlingUnit)
                    .ThenInclude(hu => hu.Contents)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shipment == null)
            return WmsEndpointOutcome.NotFound(new { code = "NOT_FOUND", message = $"Không tìm thấy lô hàng vận chuyển #{id}." });

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (effectiveAuth != null)
            await effectiveAuth.EnsureWarehouseAccessAsync(shipment.WarehouseId, cancellationToken);

        var now = DateTime.UtcNow;
        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : 1);

        var putToQuarantine = request?.QuarantineLocationId.HasValue == true && request?.IsSealedIntact != true;
        var targetStatus = putToQuarantine ? InventoryStatus.Quarantine : InventoryStatus.Available;
        var targetLocationId = request?.TargetLocationId ?? request?.QuarantineLocationId ?? await db.WarehouseLocations.AsNoTracking()
            .Where(l => l.WarehouseId == shipment.WarehouseId && l.IsActive)
            .OrderByDescending(l => putToQuarantine
                ? l.LocationType == WarehouseLocationType.Receiving
                : (l.IsPickable && l.LocationType == WarehouseLocationType.Storage))
            .ThenBy(l => l.Id)
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var rtsItems = shipment.HandlingUnits
            .SelectMany(shu => shu.HandlingUnit.Contents)
            .Select(c => (c.ProductId, LotId: (int?)null, SerialId: (int?)null, Quantity: c.Quantity))
            .ToList();
        if (rtsItems.Count == 0 && request is { ProductId: not null, Quantity: > 0m })
        {
            rtsItems.Add((request.ProductId.Value, null, null, request.Quantity.Value));
        }

        var ledgerIds = new List<int>();
        foreach (var item in rtsItems)
        {
            var returnQty = item.Quantity;
            if (returnQty <= 0m) continue;

            var stock = await db.InventoryStocks.FirstOrDefaultAsync(
                s => s.WarehouseId == shipment.WarehouseId &&
                     s.ProductId == item.ProductId &&
                     s.LocationId == targetLocationId &&
                     s.LotId == item.LotId &&
                     s.SerialId == item.SerialId &&
                     s.Status == targetStatus,
                cancellationToken);
            if (stock == null)
            {
                stock = new ERP.Domain.Entities.InventoryStock
                {
                    WarehouseId = shipment.WarehouseId,
                    ProductId = item.ProductId,
                    LocationId = targetLocationId,
                    LotId = item.LotId,
                    SerialId = item.SerialId,
                    Status = targetStatus,
                    Quantity = returnQty,
                    ReservedQuantity = 0m,
                    LastUpdated = now
                };
                db.InventoryStocks.Add(stock);
            }
            else
            {
                stock.Quantity += returnQty;
                stock.LastUpdated = now;
            }

            var rtsTx = new ERP.Domain.Entities.InventoryTransaction
            {
                ProductId = item.ProductId,
                WarehouseId = shipment.WarehouseId,
                LocationId = targetLocationId,
                ToLocationId = targetLocationId,
                LotId = item.LotId,
                SerialId = item.SerialId,
                InventoryStatus = targetStatus,
                ToInventoryStatus = targetStatus,
                TransactionType = TransactionType.Import,
                Quantity = returnQty,
                ReferenceId = shipment.Id,
                ReferenceType = "ShipmentRtsReturn",
                TransactionDate = now,
                CreatedBy = effectiveActorId,
                Note = putToQuarantine
                    ? $"RTS-{shipment.ShipmentCode}: Nhập hoàn hàng giao thất bại vào khu vực biệt trữ chờ kiểm định"
                    : $"RTS-{shipment.ShipmentCode}: Nhập hoàn nhanh đơn hoàn nguyên seal về tồn kho khả dụng (Available)"
            };
            db.InventoryTransactions.Add(rtsTx);
            await db.SaveChangesAsync(cancellationToken);
            ledgerIds.Add(rtsTx.Id);
        }

        shipment.Status = ShipmentStatus.Completed;
        shipment.CompletedAt = now;

        var trackingNumber = !string.IsNullOrWhiteSpace(shipment.SealNumber)
            ? shipment.SealNumber.Trim()
            : shipment.ShipmentCode.Trim();
        int? exportReceiptId = string.Equals(shipment.SourceType, "ExportReceipt", StringComparison.OrdinalIgnoreCase)
            ? shipment.SourceId
            : null;
        int? salesOrderId = string.Equals(shipment.SourceType, "SalesOrder", StringComparison.OrdinalIgnoreCase)
            ? shipment.SourceId
            : null;

        var linkedExport = await db.ExportReceipts
            .AsNoTracking()
            .Include(x => x.Details)
            .FirstOrDefaultAsync(
                x => x.ShipmentId == shipment.Id ||
                     (exportReceiptId.HasValue && x.Id == exportReceiptId.Value),
                cancellationToken);
        if (linkedExport != null)
        {
            exportReceiptId ??= linkedExport.Id;
            salesOrderId ??= linkedExport.SalesOrderId;
        }

        var existingReturn = await db.CustomerReturns
            .FirstOrDefaultAsync(
                r => r.ShipmentId == shipment.Id ||
                     (!string.IsNullOrWhiteSpace(trackingNumber) && r.TrackingNumber == trackingNumber),
                cancellationToken);
        if (existingReturn == null)
        {
            var autoReturn = new ERP.Domain.Entities.CustomerReturn
            {
                ReturnCode = $"RMA-RTS-{shipment.Id}-{now:yyyyMMddHHmmss}",
                WarehouseId = shipment.WarehouseId,
                ShipmentId = shipment.Id,
                ExportReceiptId = exportReceiptId,
                SalesOrderId = salesOrderId,
                QuarantineLocationId = targetLocationId,
                TrackingNumber = trackingNumber,
                OrderNumber = shipment.SourceCode?.Trim() ?? linkedExport?.Code?.Trim(),
                ReturnReason = string.IsNullOrWhiteSpace(request?.Note) ? "DELIVERY_FAILED_RTS" : request.Note.Trim(),
                Status = putToQuarantine ? "QUARANTINED" : "COMPLETED",
                CreatedBy = effectiveActorId,
                CreatedAtUtc = now,
                CompletedAtUtc = putToQuarantine ? null : now
            };
            foreach (var item in rtsItems)
            {
                autoReturn.Lines.Add(new ERP.Domain.Entities.CustomerReturnLine
                {
                    ProductId = item.ProductId,
                    LotId = item.LotId,
                    SerialId = item.SerialId,
                    QuarantineLocationId = targetLocationId,
                    TargetLocationId = targetLocationId,
                    RestockLocationId = putToQuarantine ? null : targetLocationId,
                    Quantity = item.Quantity,
                    Condition = "GOOD",
                    Disposition = putToQuarantine ? "PENDING" : "RESTOCK_AVAILABLE"
                });
            }
            db.CustomerReturns.Add(autoReturn);
        }

        await db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            shipmentId = shipment.Id,
            shipmentCode = shipment.ShipmentCode,
            warehouseId = shipment.WarehouseId,
            quarantineLocationId = targetLocationId,
            targetLocationId,
            resultingInventoryStatus = targetStatus.ToString(),
            status = shipment.Status.ToString(),
            statusLabel = putToQuarantine
                ? "Đã hoàn tất tiếp nhận hàng hoàn trả (RTS) vào khu vực biệt trữ"
                : "Đã hoàn tất nhập hoàn nhanh kiện RTS nguyên seal vào tồn kho khả dụng (Available)",
            ledgerTransactionIds = ledgerIds,
            completedAtUtc = now
        });
    }

    public async Task<WmsEndpointOutcome> GetVnAdministrativeUnitsAsync(
        string? provinceCode = null,
        string? districtCode = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        await VnAdministrativeSeedHelper.EnsureVnAdministrativeSeededAsync(db, cancellationToken);

        var query = db.VnAdministrativeUnits.AsNoTracking()
            .Include(u => u.CarrierMappings)
            .Where(u => u.IsActive);

        if (!string.IsNullOrWhiteSpace(provinceCode))
        {
            var p = provinceCode.Trim();
            query = query.Where(u => u.ProvinceCode == p || u.ProvinceName.Contains(p));
        }
        if (!string.IsNullOrWhiteSpace(districtCode))
        {
            var d = districtCode.Trim();
            query = query.Where(u => u.DistrictCode == d || u.DistrictName.Contains(d));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(u =>
                u.ProvinceName.Contains(s) ||
                u.DistrictName.Contains(s) ||
                u.WardName.Contains(s) ||
                (u.Keywords != null && u.Keywords.Contains(s)));
        }

        var units = await query
            .OrderBy(u => u.ProvinceCode)
            .ThenBy(u => u.DistrictCode)
            .ThenBy(u => u.WardCode)
            .ToListAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            totalCount = units.Count,
            items = units.Select(u => new
            {
                u.Id,
                u.ProvinceCode,
                u.ProvinceName,
                u.DistrictCode,
                u.DistrictName,
                u.WardCode,
                u.WardName,
                searchKeywords = u.Keywords,
                ghnDistrictId = u.CarrierMappings.FirstOrDefault(m => m.CarrierCode == "GHN")?.CarrierDistrictId ?? $"GHN-D-{u.DistrictCode}",
                ghtkRouteCode = u.CarrierMappings.FirstOrDefault(m => m.CarrierCode == "GHTK")?.RouteCode ?? $"RT-GHTK-{u.DistrictCode}",
                carrierMappings = u.CarrierMappings.Select(m => new
                {
                    m.CarrierCode,
                    carrierProvinceCode = m.CarrierProvinceId,
                    carrierDistrictCode = m.CarrierDistrictId,
                    m.CarrierWardCode,
                    hubRoutingCode = m.SortingHubCode,
                    m.RouteCode,
                    isDeliverable = m.IsActive
                })
            }),
            statusLabel = $"Đã tải {units.Count} đơn vị hành chính 3 cấp Việt Nam & bảng ánh xạ hãng vận chuyển"
        });
    }

    public async Task<WmsEndpointOutcome> NormalizeVietnameseAddressAsync(
        NormalizeVnAddressInput? request = null,
        CancellationToken cancellationToken = default)
    {
        await VnAdministrativeSeedHelper.EnsureVnAdministrativeSeededAsync(db, cancellationToken);

        var raw = $"{request?.RawAddress} {request?.Ward} {request?.District} {request?.Province}".Trim();
        var units = await db.VnAdministrativeUnits.AsNoTracking()
            .Include(u => u.CarrierMappings)
            .Where(u => u.IsActive)
            .ToListAsync(cancellationToken);

        static string StripWardPrefix(string wardName) =>
            wardName.Replace("Phường ", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("Xã ", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("Thị trấn ", "", StringComparison.OrdinalIgnoreCase)
                    .Trim();

        var matched = units
            .Select(u =>
            {
                var score = 0;
                var coreWard = StripWardPrefix(u.WardName);
                if (!string.IsNullOrWhiteSpace(request?.Ward) &&
                    (u.WardName.Contains(request.Ward.Trim(), StringComparison.OrdinalIgnoreCase) ||
                     (coreWard.Length >= 2 && request.Ward.Contains(coreWard, StringComparison.OrdinalIgnoreCase))))
                {
                    score += 100;
                }
                if (!string.IsNullOrWhiteSpace(raw) && string.IsNullOrWhiteSpace(request?.Ward))
                {
                    if (raw.Contains(u.WardName, StringComparison.OrdinalIgnoreCase))
                        score += 80;
                    else if (coreWard.Length >= 3 && raw.Contains(coreWard, StringComparison.OrdinalIgnoreCase))
                        score += 60;

                    if (raw.Contains(u.DistrictName, StringComparison.OrdinalIgnoreCase))
                        score += 30;

                    if (!string.IsNullOrWhiteSpace(u.Keywords))
                    {
                        var matchedKeywords = u.Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(k => k.Trim())
                            .Where(k => k.Length > 0 && raw.Contains(k, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        score += matchedKeywords.Sum(k => k is "hcm" or "tphcm" or "hn" or "ha noi" or "dn" or "hp" or "ct" or "bd" ? 2 : 20);
                    }
                }
                return (Unit: u, Score: score);
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Unit)
            .FirstOrDefault();

        if (matched == null)
        {
            return WmsEndpointOutcome.UnprocessableEntity(new
            {
                code = "ADDRESS_WARD_UNRESOLVED",
                errorCode = "ADDRESS_WARD_UNRESOLVED",
                matched = false,
                normalized = false,
                rawAddress = raw,
                message = "Không thể xác định Phường/Xã từ địa chỉ cung cấp; vui lòng kiểm tra lại địa chỉ.",
                statusLabel = "Không tìm thấy đơn vị hành chính 3 cấp phù hợp với địa chỉ đầu vào"
            });
        }

        var mappings = matched.CarrierMappings.ToDictionary(
            m => m.CarrierCode.ToUpperInvariant(),
            m => new
            {
                m.CarrierCode,
                carrierProvinceCode = m.CarrierProvinceId,
                carrierDistrictCode = m.CarrierDistrictId,
                m.CarrierWardCode,
                hubRoutingCode = m.SortingHubCode,
                m.RouteCode,
                isDeliverable = m.IsActive
            });

        return WmsEndpointOutcome.Ok(new
        {
            matched = true,
            matchedUnitId = matched.Id,
            provinceCode = matched.ProvinceCode,
            provinceName = matched.ProvinceName,
            districtCode = matched.DistrictCode,
            districtName = matched.DistrictName,
            wardCode = matched.WardCode,
            wardName = matched.WardName,
            normalizedFullAddress = $"{matched.WardName}, {matched.DistrictName}, {matched.ProvinceName}",
            carrierMappings = mappings,
            statusLabel = $"Đã chuẩn hóa địa chỉ sang {matched.WardName}, {matched.DistrictName}, {matched.ProvinceName}"
        });
    }
}
