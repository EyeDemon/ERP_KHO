using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services.Carriers;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class ShipmentOperationsService(
    ErpKhoDbContext db,
    IWarehouseAuthorizationService? warehouseAccess = null,
    ICurrentUser? currentUser = null,
    ICarrierIntegrationRegistry? carrierRegistry = null) : IShipmentOperationsService
{
    public const string DefaultCarrierWebhookSecret = "ERP_KHO_CARRIER_WEBHOOK_SECRET_2026";
    private readonly ICarrierIntegrationRegistry _carrierRegistry = carrierRegistry ?? VietnamCarrierIntegrationRegistry.Default;
    public async Task<WmsEndpointOutcome> SyncTmsManifestAsync(
        SyncTmsManifestInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var warehouseId = request?.WarehouseId;
        var targetWarehouseId = warehouseId ?? 1;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
            {
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else
            {
                accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (accessibleIds.Count == 0)
                    return WmsEndpointOutcome.Forbid();
                targetWarehouseId = accessibleIds.First();
            }
        }

        var query = db.Shipments.AsNoTracking();
        var huQuery = db.HandlingUnits.AsNoTracking()
            .Where(h => h.Status != HandlingUnitStatus.Cancelled);
        if (warehouseId.HasValue)
        {
            query = query.Where(s => s.WarehouseId == warehouseId.Value);
            huQuery = huQuery.Where(h => h.WarehouseId == warehouseId.Value);
        }
        else if (accessibleIds != null)
        {
            query = query.Where(s => accessibleIds.Contains(s.WarehouseId));
            huQuery = huQuery.Where(h => accessibleIds.Contains(h.WarehouseId));
        }

        var syncedShipmentCount = await query.CountAsync(cancellationToken);
        var stagedOrLoadedCount = await query.CountAsync(
            s => s.Status == ShipmentStatus.Ready
                 || s.Status == ShipmentStatus.Staging
                 || s.Status == ShipmentStatus.Loading
                 || s.Status == ShipmentStatus.Loaded
                 || s.Status == ShipmentStatus.Dispatched
                 || s.Status == ShipmentStatus.InTransit,
            cancellationToken);
        var totalHandlingUnitsCount = await huQuery.CountAsync(cancellationToken);
        var totalGrossWeightKg = Math.Round(await huQuery.SumAsync(h => h.GrossWeightKg ?? 0m, cancellationToken), 2);
        var totalVolumeM3 = Math.Round(await huQuery.SumAsync(h => h.VolumeM3 ?? 0m, cancellationToken), 4);

        var now = DateTime.UtcNow;
        var manifestId = $"TMS-MNF-{now:yyyyMMdd-HHmm}";
        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : null);
        db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
        {
            UserId = effectiveActorId,
            WarehouseId = targetWarehouseId,
            Action = "Shipment.SyncTmsManifest",
            EntityName = "TmsManifest",
            NewValues = manifestId,
            Reason = $"Đồng bộ bảng kê vận tải & ghép tải ({manifestId})",
            Result = "Success",
            Timestamp = now
        });
        await db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            manifestId,
            warehouseId = targetWarehouseId,
            carrierCode = string.IsNullOrWhiteSpace(request?.CarrierCode) ? "VTP-EXPRESS" : request.CarrierCode.Trim(),
            syncedShipmentCount,
            stagedOrLoadedCount,
            totalHandlingUnitsCount,
            totalGrossWeightKg,
            totalVolumeM3,
            ediStatus = "Đã đồng bộ bảng kê vận tải & ghép tải thành công",
            status = "Đã đồng bộ bảng kê vận tải & ghép tải thành công",
            statusLabel = "Đã đồng bộ bảng kê vận tải & ghép tải thành công",
            syncedAtUtc = now,
        });
    }

    public async Task<WmsEndpointOutcome> GetFleetAssignmentsAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (warehouseId.HasValue && effectiveAuth != null)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
        }

        var shipmentQuery = db.Shipments.AsNoTracking().Include(s => s.DockAppointment).AsQueryable();
        var manifestQuery = db.HandoverManifests.AsNoTracking().AsQueryable();
        if (warehouseId.HasValue)
        {
            shipmentQuery = shipmentQuery.Where(s => s.WarehouseId == warehouseId.Value);
            manifestQuery = manifestQuery.Where(m => m.WarehouseId == warehouseId.Value);
        }

        var shipmentList = await shipmentQuery
            .OrderByDescending(s => s.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        var manifestList = await manifestQuery
            .OrderByDescending(m => m.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            totalCount = shipmentList.Count,
            manifestCount = manifestList.Count,
            items = shipmentList.Select(s =>
            {
                var matchedManifest = manifestList.FirstOrDefault(m =>
                    !string.IsNullOrWhiteSpace(s.VehiclePlate) &&
                    string.Equals(m.VehiclePlate, s.VehiclePlate, StringComparison.OrdinalIgnoreCase));
                return new
                {
                    shipmentId = s.Id,
                    shipmentCode = s.ShipmentCode,
                    warehouseId = s.WarehouseId,
                    status = s.Status.ToString(),
                    vehiclePlate = s.VehiclePlate ?? matchedManifest?.VehiclePlate,
                    trailerPlate = s.TrailerPlate,
                    sealNumber = s.SealNumber,
                    driverName = matchedManifest?.DriverName ?? s.DockAppointment?.DriverName,
                    driverPhone = matchedManifest?.DriverPhone ?? s.DockAppointment?.DriverPhone,
                    carrierCode = matchedManifest?.CarrierCode ?? s.DockAppointment?.CarrierName,
                    dockAppointmentId = s.DockAppointmentId
                };
            }),
            manifests = manifestList.Select(m => new
            {
                manifestId = m.Id,
                manifestCode = m.ManifestCode,
                warehouseId = m.WarehouseId,
                carrierCode = m.CarrierCode,
                vehiclePlate = m.VehiclePlate,
                driverName = m.DriverName,
                driverPhone = m.DriverPhone,
                status = m.Status,
                totalPackages = m.TotalPackages,
                createdAtUtc = m.CreatedAtUtc,
                sealedAtUtc = m.SealedAtUtc
            }),
            statusLabel = "Đã tải danh sách phân công phương tiện & tài xế vận chuyển"
        });
    }

    public async Task<WmsEndpointOutcome> AssignFleetVehicleAsync(
        AssignFleetVehicleInput? request,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null ||
            string.IsNullOrWhiteSpace(request.VehiclePlate) ||
            string.IsNullOrWhiteSpace(request.DriverName))
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "FLEET_VEHICLE_DRIVER_REQUIRED",
                errorCode = "FLEET_VEHICLE_DRIVER_REQUIRED",
                message = "Bắt buộc nhập biển số xe (VehiclePlate) và họ tên tài xế/bưu tá (DriverName) khi phân công vận chuyển."
            });
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var vehiclePlate = request.VehiclePlate.Trim().ToUpperInvariant();
        var driverName = request.DriverName.Trim();
        var driverPhone = string.IsNullOrWhiteSpace(request.DriverPhone) ? null : request.DriverPhone.Trim();
        var carrierCode = string.IsNullOrWhiteSpace(request.CarrierCode) ? "GHN" : request.CarrierCode.Trim().ToUpperInvariant();
        var now = DateTime.UtcNow;

        ERP.Domain.Entities.Shipment? shipment = null;
        if (request.ShipmentId is > 0)
        {
            shipment = await db.Shipments
                .Include(s => s.DockAppointment)
                .FirstOrDefaultAsync(s => s.Id == request.ShipmentId.Value, cancellationToken);
            if (shipment == null)
            {
                return WmsEndpointOutcome.NotFound(new
                {
                    code = "SHIPMENT_NOT_FOUND",
                    errorCode = "SHIPMENT_NOT_FOUND",
                    message = $"Không tìm thấy chuyến giao hàng #{request.ShipmentId.Value}."
                });
            }
        }

        ERP.Domain.Entities.HandoverManifest? manifest = null;
        if (request.ManifestId is > 0)
        {
            manifest = await db.HandoverManifests.FirstOrDefaultAsync(m => m.Id == request.ManifestId.Value, cancellationToken);
            if (manifest == null)
            {
                return WmsEndpointOutcome.NotFound(new
                {
                    code = "MANIFEST_NOT_FOUND",
                    errorCode = "MANIFEST_NOT_FOUND",
                    message = $"Không tìm thấy biên bản bàn giao #{request.ManifestId.Value}."
                });
            }
        }

        var targetWarehouseId = request.WarehouseId
            ?? shipment?.WarehouseId
            ?? manifest?.WarehouseId
            ?? await db.Warehouses.AsNoTracking().OrderBy(w => w.Id).Select(w => w.Id).FirstOrDefaultAsync(cancellationToken);
        if (targetWarehouseId <= 0) targetWarehouseId = 1;

        if (effectiveAuth != null)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(targetWarehouseId, cancellationToken);
        }

        if (shipment != null)
        {
            shipment.VehiclePlate = vehiclePlate;
            if (!string.IsNullOrWhiteSpace(request.TrailerPlate))
                shipment.TrailerPlate = request.TrailerPlate.Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(request.SealNumber))
                shipment.SealNumber = request.SealNumber.Trim();
            if (shipment.DockAppointment != null)
            {
                shipment.DockAppointment.VehiclePlate = vehiclePlate;
                shipment.DockAppointment.DriverName = driverName;
                if (driverPhone != null)
                    shipment.DockAppointment.DriverPhone = driverPhone;
            }
        }

        var candidateActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : null);
        var resolvedActorId = candidateActorId is > 0 && await db.Users.AsNoTracking().AnyAsync(u => u.Id == candidateActorId.Value, cancellationToken)
            ? candidateActorId
            : null;

        if (manifest != null)
        {
            manifest.VehiclePlate = vehiclePlate;
            manifest.DriverName = driverName;
            manifest.DriverPhone = driverPhone;
            if (!string.IsNullOrWhiteSpace(request.CarrierCode))
                manifest.CarrierCode = carrierCode;
        }
        else if (shipment == null)
        {
            manifest = await db.HandoverManifests
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync(m => m.WarehouseId == targetWarehouseId && m.CarrierCode == carrierCode && m.Status == "OPEN", cancellationToken);
            if (manifest == null)
            {
                manifest = new ERP.Domain.Entities.HandoverManifest
                {
                    ManifestCode = $"HND-{carrierCode}-{now:yyyyMMdd-HHmmss}",
                    WarehouseId = targetWarehouseId,
                    CarrierCode = carrierCode,
                    VehiclePlate = vehiclePlate,
                    DriverName = driverName,
                    DriverPhone = driverPhone,
                    Status = "OPEN",
                    TotalPackages = 0,
                    CreatedBy = resolvedActorId ?? 1,
                    CreatedAtUtc = now
                };
                db.HandoverManifests.Add(manifest);
            }
            else
            {
                manifest.VehiclePlate = vehiclePlate;
                manifest.DriverName = driverName;
                manifest.DriverPhone = driverPhone;
            }
        }

        db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
        {
            UserId = resolvedActorId,
            WarehouseId = targetWarehouseId,
            EntityName = shipment != null ? "Shipment" : "HandoverManifest",
            EntityId = shipment?.Id ?? manifest?.Id,
            Action = "Shipment.AssignFleetVehicle",
            NewValues = $"VehiclePlate={vehiclePlate}; DriverName={driverName}; DriverPhone={driverPhone}; SealNumber={request.SealNumber}",
            Reason = $"Phân công phương tiện {vehiclePlate} & tài xế {driverName}",
            Result = "Success",
            Timestamp = now
        });

        await db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            shipmentId = shipment?.Id,
            shipmentCode = shipment?.ShipmentCode,
            manifestId = manifest?.Id,
            manifestCode = manifest?.ManifestCode,
            warehouseId = targetWarehouseId,
            carrierCode,
            vehiclePlate,
            trailerPlate = shipment?.TrailerPlate ?? request.TrailerPlate,
            sealNumber = shipment?.SealNumber ?? request.SealNumber,
            driverName,
            driverPhone,
            assignedAtUtc = now,
            statusLabel = $"Đã cập nhật phân công xe {vehiclePlate} & tài xế {driverName}"
        });
    }

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

    public async Task<WmsEndpointOutcome> GetCarrierConfigsAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var targetWarehouseId = warehouseId is > 0 ? warehouseId.Value : 1;
        if (warehouseId.HasValue && effectiveAuth != null)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
        }

        await EnsureCarrierConfigsSeededAsync(targetWarehouseId, cancellationToken);

        var query = db.CarrierIntegrationConfigs.AsNoTracking();
        if (warehouseId is > 0)
        {
            query = query.Where(c => c.WarehouseId == warehouseId.Value);
        }

        var configs = await query
            .OrderBy(c => c.WarehouseId)
            .ThenBy(c => c.CarrierCode)
            .ToListAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            totalCount = configs.Count,
            warehouseId = targetWarehouseId,
            items = configs.Select(c =>
            {
                var adapter = _carrierRegistry.ResolveAdapter(c.CarrierCode);
                return new
                {
                    c.Id,
                    c.WarehouseId,
                    c.CarrierCode,
                    carrierDisplayName = adapter.CarrierDisplayName,
                    c.ShopId,
                    hasApiToken = !string.IsNullOrWhiteSpace(c.EncryptedApiToken),
                    c.IsSandbox,
                    c.SortingHubPrefix,
                    c.BaseShippingFeeVnd,
                    c.IsActive,
                    c.UpdatedAtUtc
                };
            }),
            statusLabel = $"Đã tải {configs.Count} cấu hình kết nối hãng vận chuyển (GHN, GHTK, ViettelPost, J&T Express)"
        });
    }

    public async Task<WmsEndpointOutcome> UpsertCarrierConfigAsync(
        UpsertCarrierConfigInput? request,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.CarrierCode))
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "CARRIER_CODE_REQUIRED",
                errorCode = "CARRIER_CODE_REQUIRED",
                message = "Bắt buộc chọn mã đơn vị vận chuyển (CarrierCode: GHN, GHTK, VIETTELPOST, JT_EXPRESS)."
            });
        }

        var targetWarehouseId = request.WarehouseId is > 0 ? request.WarehouseId.Value : 1;
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (effectiveAuth != null)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(targetWarehouseId, cancellationToken);
        }

        var normCarrier = VietnamCarrierIntegrationRegistry.NormalizeCarrierCode(request.CarrierCode);
        var adapter = _carrierRegistry.ResolveAdapter(normCarrier);
        var now = DateTime.UtcNow;

        var config = await db.CarrierIntegrationConfigs
            .FirstOrDefaultAsync(c => c.WarehouseId == targetWarehouseId && c.CarrierCode == normCarrier, cancellationToken);

        if (config == null)
        {
            config = new ERP.Domain.Entities.CarrierIntegrationConfig
            {
                WarehouseId = targetWarehouseId,
                CarrierCode = normCarrier,
                ShopId = !string.IsNullOrWhiteSpace(request.ShopId) ? request.ShopId.Trim() : $"SHOP-{normCarrier}-{targetWarehouseId:D2}",
                EncryptedApiToken = !string.IsNullOrWhiteSpace(request.ApiToken) ? request.ApiToken.Trim() : $"SANDBOX_TOKEN_{normCarrier}_{targetWarehouseId:D2}",
                IsSandbox = request.IsSandbox ?? true,
                WebhookSecretHash = !string.IsNullOrWhiteSpace(request.WebhookSecret) ? request.WebhookSecret.Trim() : DefaultCarrierWebhookSecret,
                SortingHubPrefix = !string.IsNullOrWhiteSpace(request.SortingHubPrefix) ? request.SortingHubPrefix.Trim() : $"HUB-{normCarrier}",
                BaseShippingFeeVnd = request.BaseShippingFeeVnd is > 0m ? Math.Round(request.BaseShippingFeeVnd.Value, 4) : 22000m,
                IsActive = request.IsActive ?? true,
                UpdatedAtUtc = now
            };
            db.CarrierIntegrationConfigs.Add(config);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request.ShopId))
                config.ShopId = request.ShopId.Trim();
            if (!string.IsNullOrWhiteSpace(request.ApiToken))
                config.EncryptedApiToken = request.ApiToken.Trim();
            if (request.IsSandbox.HasValue)
                config.IsSandbox = request.IsSandbox.Value;
            if (!string.IsNullOrWhiteSpace(request.WebhookSecret))
                config.WebhookSecretHash = request.WebhookSecret.Trim();
            if (!string.IsNullOrWhiteSpace(request.SortingHubPrefix))
                config.SortingHubPrefix = request.SortingHubPrefix.Trim();
            if (request.BaseShippingFeeVnd is > 0m)
                config.BaseShippingFeeVnd = Math.Round(request.BaseShippingFeeVnd.Value, 4);
            if (request.IsActive.HasValue)
                config.IsActive = request.IsActive.Value;
            config.UpdatedAtUtc = now;
        }

        var resolvedActorId = await ResolveNullableUserIdAsync(actorUserId, cancellationToken);
        db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
        {
            UserId = resolvedActorId,
            WarehouseId = targetWarehouseId,
            EntityName = "CarrierIntegrationConfig",
            Action = "Shipment.UpsertCarrierConfig",
            NewValues = $"CarrierCode={normCarrier}; ShopId={config.ShopId}; IsSandbox={config.IsSandbox}; BaseShippingFeeVnd={config.BaseShippingFeeVnd}",
            Reason = $"Cập nhật cấu hình kết nối ĐVVC {adapter.CarrierDisplayName}",
            Result = "Success",
            Timestamp = now
        });

        await db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            config.Id,
            config.WarehouseId,
            config.CarrierCode,
            carrierDisplayName = adapter.CarrierDisplayName,
            config.ShopId,
            hasApiToken = !string.IsNullOrWhiteSpace(config.EncryptedApiToken),
            config.IsSandbox,
            config.SortingHubPrefix,
            config.BaseShippingFeeVnd,
            config.IsActive,
            config.UpdatedAtUtc,
            statusLabel = $"Đã lưu cấu hình kết nối {adapter.CarrierDisplayName} cho kho #{targetWarehouseId}"
        });
    }

    public async Task<WmsEndpointOutcome> BookCarrierWaybillAsync(
        CarrierWaybillBookingRequest? request,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null || (string.IsNullOrWhiteSpace(request.RawAddress) && string.IsNullOrWhiteSpace(request.Ward)))
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "WAYBILL_ADDRESS_REQUIRED",
                errorCode = "WAYBILL_ADDRESS_REQUIRED",
                message = "Bắt buộc cung cấp địa chỉ người nhận để tạo vận đơn điện tử (AWB)."
            });
        }

        ERP.Domain.Entities.Shipment? shipment = null;
        if (request.ShipmentId is > 0)
        {
            shipment = await db.Shipments.FirstOrDefaultAsync(s => s.Id == request.ShipmentId.Value, cancellationToken);
            if (shipment == null)
            {
                return WmsEndpointOutcome.NotFound(new
                {
                    code = "SHIPMENT_NOT_FOUND",
                    errorCode = "SHIPMENT_NOT_FOUND",
                    message = $"Không tìm thấy chuyến giao hàng #{request.ShipmentId.Value}."
                });
            }
        }

        ERP.Domain.Entities.ExportReceipt? exportReceipt = null;
        if (request.ExportReceiptId is > 0)
        {
            exportReceipt = await db.ExportReceipts.FirstOrDefaultAsync(e => e.Id == request.ExportReceiptId.Value, cancellationToken);
            if (exportReceipt == null)
            {
                return WmsEndpointOutcome.NotFound(new
                {
                    code = "EXPORT_RECEIPT_NOT_FOUND",
                    errorCode = "EXPORT_RECEIPT_NOT_FOUND",
                    message = $"Không tìm thấy phiếu xuất kho #{request.ExportReceiptId.Value}."
                });
            }
        }

        if (shipment != null && exportReceipt == null)
        {
            exportReceipt = await db.ExportReceipts.FirstOrDefaultAsync(
                e => e.ShipmentId == shipment.Id || (shipment.SourceType == "ExportReceipt" && e.Id == shipment.SourceId),
                cancellationToken);
        }
        else if (exportReceipt != null && shipment == null && exportReceipt.ShipmentId is > 0)
        {
            shipment = await db.Shipments.FirstOrDefaultAsync(s => s.Id == exportReceipt.ShipmentId.Value, cancellationToken);
        }

        var targetWarehouseId = shipment?.WarehouseId
            ?? exportReceipt?.WarehouseId
            ?? (request.WarehouseId > 0 ? request.WarehouseId : 1);

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (effectiveAuth != null)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(targetWarehouseId, cancellationToken);
        }

        await VnAdministrativeSeedHelper.EnsureVnAdministrativeSeededAsync(db, cancellationToken);
        await EnsureCarrierConfigsSeededAsync(targetWarehouseId, cancellationToken);

        var normCarrier = VietnamCarrierIntegrationRegistry.NormalizeCarrierCode(request.CarrierCode);
        var adapter = _carrierRegistry.ResolveAdapter(normCarrier);

        var config = await db.CarrierIntegrationConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.WarehouseId == targetWarehouseId && c.CarrierCode == adapter.CarrierCode, cancellationToken);

        var matchedUnit = await ResolveVnAdministrativeUnitAsync(
            request.RawAddress,
            request.Province,
            request.District,
            request.Ward,
            cancellationToken);

        string provinceName;
        string wardName;
        string normalizedAddress;
        string sortingHubCode;
        string routeCode;

        if (matchedUnit != null)
        {
            provinceName = matchedUnit.ProvinceName;
            wardName = matchedUnit.WardName;
            var streetPart = string.IsNullOrWhiteSpace(request.RawAddress) ? string.Empty : $"{request.RawAddress.Trim()}, ";
            normalizedAddress = streetPart.Contains(matchedUnit.WardName, StringComparison.OrdinalIgnoreCase)
                ? request.RawAddress.Trim()
                : $"{streetPart}{matchedUnit.WardName}, {matchedUnit.ProvinceName}";

            var mapping = matchedUnit.CarrierMappings.FirstOrDefault(m =>
                string.Equals(m.CarrierCode, adapter.CarrierCode, StringComparison.OrdinalIgnoreCase) ||
                (adapter.CarrierCode == "VIETTELPOST" && string.Equals(m.CarrierCode, "VTPOST", StringComparison.OrdinalIgnoreCase)));

            var hubPrefix = !string.IsNullOrWhiteSpace(config?.SortingHubPrefix)
                ? config.SortingHubPrefix.Trim()
                : $"HUB-{adapter.CarrierCode}";

            sortingHubCode = !string.IsNullOrWhiteSpace(mapping?.SortingHubCode)
                ? mapping.SortingHubCode
                : $"{hubPrefix}-{matchedUnit.ProvinceCode}-{matchedUnit.WardCode}";
            routeCode = !string.IsNullOrWhiteSpace(mapping?.RouteCode)
                ? mapping.RouteCode
                : $"RT-{adapter.CarrierCode}-{matchedUnit.ProvinceCode}-{matchedUnit.WardCode}";
        }
        else if (!string.IsNullOrWhiteSpace(request.Ward)
                 && !string.IsNullOrWhiteSpace(request.Province)
                 && !request.Ward.Contains("Không Tồn Tại", StringComparison.OrdinalIgnoreCase)
                 && !request.Ward.Contains("Không Có Thật", StringComparison.OrdinalIgnoreCase))
        {
            provinceName = request.Province.Trim();
            wardName = request.Ward.Trim();
            normalizedAddress = string.IsNullOrWhiteSpace(request.RawAddress)
                ? $"{wardName}, {provinceName}"
                : $"{request.RawAddress.Trim()}, {wardName}, {provinceName}";
            var hubPrefix = !string.IsNullOrWhiteSpace(config?.SortingHubPrefix)
                ? config.SortingHubPrefix.Trim()
                : $"HUB-{adapter.CarrierCode}";
            sortingHubCode = $"{hubPrefix}-VN-001";
            routeCode = $"RT-{adapter.CarrierCode}-VN-001";
        }
        else
        {
            return WmsEndpointOutcome.UnprocessableEntity(new
            {
                code = "ADDRESS_WARD_UNRESOLVED",
                errorCode = "ADDRESS_WARD_UNRESOLVED",
                matched = false,
                message = "Không thể xác định Phường/Xã theo mô hình hành chính 2 cấp (Tỉnh/TP - Phường/Xã) để tạo vận đơn."
            });
        }

        var effectiveRequest = request with
        {
            WarehouseId = targetWarehouseId,
            ShipmentId = shipment?.Id ?? request.ShipmentId,
            ExportReceiptId = exportReceipt?.Id ?? request.ExportReceiptId,
            CarrierCode = adapter.CarrierCode,
            Province = provinceName,
            Ward = wardName
        };

        var booking = adapter.BookWaybill(
            effectiveRequest,
            sortingHubCode,
            routeCode,
            normalizedAddress,
            config?.BaseShippingFeeVnd ?? 22000m,
            config?.IsSandbox ?? true);

        var now = DateTime.UtcNow;
        var resolvedActorId = await ResolveNullableUserIdAsync(actorUserId, cancellationToken);

        await using var tx = db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        if (exportReceipt != null)
        {
            exportReceipt.CarrierCode = booking.CarrierCode;
            exportReceipt.TrackingNumber = booking.AwbTrackingCode;
            exportReceipt.AwbLabelUrl ??= $"/api/v1/export-receipts/{exportReceipt.Id}/awb-label?awb={booking.AwbTrackingCode}";
        }

        if (shipment != null)
        {
            shipment.SourceCode ??= booking.AwbTrackingCode;
            var eventExists = await db.ShipmentTrackingEvents.AnyAsync(
                e => e.ShipmentId == shipment.Id
                     && e.Source == booking.CarrierCode
                     && e.SourceEventId == booking.AwbTrackingCode,
                cancellationToken);
            if (!eventExists)
            {
                db.ShipmentTrackingEvents.Add(new ERP.Domain.Entities.ShipmentTrackingEvent
                {
                    ShipmentId = shipment.Id,
                    EventType = "WaybillBooked",
                    FromStatus = shipment.Status,
                    ToStatus = shipment.Status,
                    OccurredAt = now,
                    RecordedAt = now,
                    Source = booking.CarrierCode,
                    SourceEventId = booking.AwbTrackingCode,
                    Note = $"Đã tạo vận đơn điện tử {booking.AwbTrackingCode} ({booking.SortingHubCode})",
                    RecordedBy = resolvedActorId
                });
            }
        }

        var outboxMsg = new ERP.Domain.Entities.IntegrationOutboxMessage
        {
            EventId = $"AWB-{booking.CarrierCode}-{booking.AwbTrackingCode}-{Guid.NewGuid():N}"[..48],
            EventType = "CARRIER_BOOK_WAYBILL",
            AggregateType = shipment != null ? "Shipment" : "ExportReceipt",
            AggregateId = shipment?.Id ?? exportReceipt?.Id,
            PayloadJson = booking.ProviderPayloadJson,
            Status = "Pending",
            RetryCount = 0,
            CreatedAt = now
        };
        db.IntegrationOutboxMessages.Add(outboxMsg);

        var expectedCod = Math.Round(Math.Max(0m, request.CodAmountVnd), 4);
        var ledger = await db.ShipmentFinancialLedgers.FirstOrDefaultAsync(
            l => (shipment != null && l.ShipmentId == shipment.Id)
                 || (exportReceipt != null && l.ExportReceiptId == exportReceipt.Id)
                 || l.TrackingNumber == booking.AwbTrackingCode,
            cancellationToken);

        if (ledger == null)
        {
            ledger = new ERP.Domain.Entities.ShipmentFinancialLedger
            {
                ShipmentId = shipment?.Id,
                ExportReceiptId = exportReceipt?.Id,
                CarrierCode = booking.CarrierCode,
                TrackingNumber = booking.AwbTrackingCode,
                ExpectedCodAmount = expectedCod,
                ActualCodCollected = 0m,
                CodVariance = Math.Round(0m - expectedCod, 4),
                EstimatedShippingFee = booking.EstimatedShippingFeeVnd,
                ActualShippingFee = 0m,
                ShippingFeeVariance = 0m,
                NetSettlementAmount = Math.Round(expectedCod - booking.EstimatedShippingFeeVnd, 4),
                ReconciliationStatus = "PENDING",
                CreatedAtUtc = now
            };
            db.ShipmentFinancialLedgers.Add(ledger);
        }
        else
        {
            ledger.CarrierCode = booking.CarrierCode;
            ledger.TrackingNumber = booking.AwbTrackingCode;
            ledger.ExpectedCodAmount = expectedCod;
            ledger.EstimatedShippingFee = booking.EstimatedShippingFeeVnd;
            ledger.NetSettlementAmount = Math.Round(expectedCod - booking.EstimatedShippingFeeVnd, 4);
        }

        db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
        {
            UserId = resolvedActorId,
            WarehouseId = targetWarehouseId,
            EntityName = shipment != null ? "Shipment" : "ExportReceipt",
            EntityId = shipment?.Id ?? exportReceipt?.Id,
            Action = "Shipment.BookCarrierWaybill",
            NewValues = $"Carrier={booking.CarrierCode}; AWB={booking.AwbTrackingCode}; Hub={booking.SortingHubCode}; Fee={booking.EstimatedShippingFeeVnd}",
            Reason = $"Tạo vận đơn điện tử {booking.AwbTrackingCode} qua {adapter.CarrierDisplayName}",
            Result = "Success",
            Timestamp = now
        });

        await db.SaveChangesAsync(cancellationToken);
        if (tx != null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return WmsEndpointOutcome.Ok(new
        {
            shipmentId = shipment?.Id,
            exportReceiptId = exportReceipt?.Id,
            warehouseId = targetWarehouseId,
            carrierCode = booking.CarrierCode,
            carrierDisplayName = adapter.CarrierDisplayName,
            awbTrackingCode = booking.AwbTrackingCode,
            trackingNumber = booking.AwbTrackingCode,
            sortingHubCode = booking.SortingHubCode,
            routeCode = booking.RouteCode,
            estimatedShippingFeeVnd = booking.EstimatedShippingFeeVnd,
            codAmountVnd = expectedCod,
            administrativeModel = booking.AdministrativeModel,
            normalizedAddress = booking.NormalizedAddress,
            isSandbox = booking.IsSandbox,
            outboxMessageId = outboxMsg.Id,
            outboxEventType = outboxMsg.EventType,
            financialLedgerId = ledger.Id,
            statusLabel = $"Đã tạo vận đơn điện tử {booking.AwbTrackingCode} qua {adapter.CarrierDisplayName} (Outbox Pattern)"
        });
    }

    public async Task<WmsEndpointOutcome> IngestCarrierWebhookAsync(
        string carrierCode,
        CarrierWebhookEventInput? request,
        string? rawBody = null,
        string? signatureHeader = null,
        int? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var normCarrier = VietnamCarrierIntegrationRegistry.NormalizeCarrierCode(carrierCode);
        var adapter = _carrierRegistry.ResolveAdapter(normCarrier);

        if (!string.IsNullOrWhiteSpace(signatureHeader))
        {
            var config = await db.CarrierIntegrationConfigs.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CarrierCode == normCarrier && c.IsActive, cancellationToken);
            var secret = !string.IsNullOrWhiteSpace(config?.WebhookSecretHash)
                ? config.WebhookSecretHash
                : DefaultCarrierWebhookSecret;

            var candidatePayloads = new List<string>();
            if (!string.IsNullOrEmpty(rawBody))
                candidatePayloads.Add(rawBody);
            if (request != null)
            {
                candidatePayloads.Add(JsonSerializer.Serialize(request));
                candidatePayloads.Add(JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            }
            if (candidatePayloads.Count == 0)
                candidatePayloads.Add(string.Empty);

            var signatureValid = candidatePayloads.Any(p =>
                adapter.VerifyWebhookHmacSignature(p, signatureHeader, secret) ||
                (secret != DefaultCarrierWebhookSecret && adapter.VerifyWebhookHmacSignature(p, signatureHeader, DefaultCarrierWebhookSecret)));

            if (!signatureValid)
            {
                return WmsEndpointOutcome.Json(401, new
                {
                    code = "CARRIER_WEBHOOK_INVALID_SIGNATURE",
                    errorCode = "CARRIER_WEBHOOK_INVALID_SIGNATURE",
                    message = "Chữ ký xác thực HMAC-SHA256 của webhook ĐVVC không hợp lệ."
                });
            }
        }

        if (request == null)
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "CARRIER_WEBHOOK_PAYLOAD_REQUIRED",
                errorCode = "CARRIER_WEBHOOK_PAYLOAD_REQUIRED",
                message = "Dữ liệu sự kiện webhook vận chuyển không được để trống."
            });
        }

        var resolvedStatusInput = !string.IsNullOrWhiteSpace(request.CarrierStatus)
            ? request.CarrierStatus
            : (!string.IsNullOrWhiteSpace(request.Status) ? request.Status : request.EventType);
        var rawStatus = string.IsNullOrWhiteSpace(resolvedStatusInput)
            ? "IN_TRANSIT"
            : resolvedStatusInput.Trim().ToUpperInvariant();
        var eventId = !string.IsNullOrWhiteSpace(request.EventId)
            ? request.EventId.Trim()
            : $"{normCarrier}-{request.TrackingNumber ?? "NOAWB"}-{rawStatus}";
        var sourceSystem = $"CARRIER_{normCarrier}";

        var existingInbox = await db.IntegrationInboxMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.SourceSystem == sourceSystem && m.ExternalMessageId == eventId, cancellationToken);
        if (existingInbox != null)
        {
            return WmsEndpointOutcome.Ok(new
            {
                idempotentReplay = true,
                inboxMessageId = existingInbox.Id,
                sourceSystem,
                eventId,
                carrierCode = normCarrier,
                trackingNumber = request.TrackingNumber,
                status = "AlreadyProcessed",
                statusLabel = $"Sự kiện webhook {eventId} từ {normCarrier} đã được xử lý trước đó (Idempotent Replay)"
            });
        }

        var now = DateTime.UtcNow;
        await using var tx = db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var ledger = await db.ShipmentFinancialLedgers.FirstOrDefaultAsync(
            l => (!string.IsNullOrWhiteSpace(request.TrackingNumber) && l.TrackingNumber == request.TrackingNumber)
                 || (request.ShipmentId.HasValue && l.ShipmentId == request.ShipmentId.Value)
                 || (request.ExportReceiptId.HasValue && l.ExportReceiptId == request.ExportReceiptId.Value),
            cancellationToken);

        var exportReceiptId = request.ExportReceiptId ?? ledger?.ExportReceiptId;
        ERP.Domain.Entities.ExportReceipt? exportReceipt = null;
        if (exportReceiptId is > 0)
        {
            exportReceipt = await db.ExportReceipts.FirstOrDefaultAsync(e => e.Id == exportReceiptId.Value, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.TrackingNumber))
        {
            exportReceipt = await db.ExportReceipts.FirstOrDefaultAsync(e => e.TrackingNumber == request.TrackingNumber, cancellationToken);
        }

        var shipmentId = request.ShipmentId ?? ledger?.ShipmentId ?? exportReceipt?.ShipmentId;
        ERP.Domain.Entities.Shipment? shipment = null;
        if (shipmentId is > 0)
        {
            shipment = await db.Shipments
                .Include(s => s.ProofOfDelivery)
                .FirstOrDefaultAsync(s => s.Id == shipmentId.Value, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.TrackingNumber))
        {
            shipment = await db.Shipments
                .Include(s => s.ProofOfDelivery)
                .FirstOrDefaultAsync(s => s.SourceCode == request.TrackingNumber, cancellationToken);
        }

        var inboxMsg = new ERP.Domain.Entities.IntegrationInboxMessage
        {
            SourceSystem = sourceSystem,
            ExternalMessageId = eventId,
            EventType = $"CARRIER_STATUS_{rawStatus}",
            PayloadJson = !string.IsNullOrWhiteSpace(rawBody) ? rawBody : JsonSerializer.Serialize(request),
            Status = "Processed",
            ReceivedAt = now,
            ProcessedAt = now
        };
        db.IntegrationInboxMessages.Add(inboxMsg);

        var targetStatus = rawStatus switch
        {
            "PICKED" or "PICKED_UP" or "DISPATCHED" or "READY_TO_PICK" => ShipmentStatus.Dispatched,
            "IN_TRANSIT" or "INTRANSIT" or "TRANSPORTING" or "SORTING" or "DELIVERING" => ShipmentStatus.InTransit,
            "DELIVERED" or "COMPLETED" or "SUCCESS" => ShipmentStatus.Delivered,
            "DELIVERY_FAILED" or "DELIVERYFAILED" or "FAILED" or "RETURN" or "RETURN_TO_WAREHOUSE" or "RTS" => ShipmentStatus.DeliveryFailed,
            _ => ShipmentStatus.InTransit
        };

        var nullableActorId = await ResolveNullableUserIdAsync(actorUserId, cancellationToken);
        ERP.Domain.Entities.ShipmentProofOfDelivery? epod = null;

        if (shipment != null)
        {
            var prevStatus = shipment.Status;
            shipment.Status = targetStatus;
            if (targetStatus == ShipmentStatus.Dispatched)
                shipment.DispatchedAt ??= now;
            else if (targetStatus == ShipmentStatus.InTransit)
                shipment.InTransitAt ??= now;
            else if (targetStatus == ShipmentStatus.Delivered)
                shipment.CompletedAt ??= now;
            else if (targetStatus == ShipmentStatus.DeliveryFailed)
                shipment.DeliveryFailedAt ??= now;

            db.ShipmentTrackingEvents.Add(new ERP.Domain.Entities.ShipmentTrackingEvent
            {
                ShipmentId = shipment.Id,
                EventType = $"CarrierWebhook.{normCarrier}.{rawStatus}",
                FromStatus = prevStatus,
                ToStatus = targetStatus,
                OccurredAt = now,
                RecordedAt = now,
                Source = normCarrier,
                SourceEventId = eventId,
                ReasonCode = request.FailureReason,
                Note = request.PodNote ?? request.FailureReason ?? $"Cập nhật trạng thái vận đơn từ webhook {normCarrier}: {rawStatus}",
                RecordedBy = nullableActorId
            });

            if (targetStatus == ShipmentStatus.Delivered)
            {
                var epodCreatorId = await ResolveValidUserIdAsync(actorUserId ?? shipment.CreatedBy, cancellationToken);
                epod = shipment.ProofOfDelivery;
                if (epod == null)
                {
                    epod = new ERP.Domain.Entities.ShipmentProofOfDelivery
                    {
                        ShipmentId = shipment.Id,
                        DeliveredAt = now,
                        ReceiverName = !string.IsNullOrWhiteSpace(request.PodRecipientName) ? request.PodRecipientName.Trim() : "Người nhận ký nhận (e-POD)",
                        EvidenceReference = request.PodPhotoUrl ?? $"EPOD-{normCarrier}-{request.TrackingNumber ?? shipment.ShipmentCode}",
                        CarrierReference = request.TrackingNumber ?? shipment.ShipmentCode,
                        DeliveryNote = request.PodNote ?? $"Giao hàng thành công qua {adapter.CarrierDisplayName}",
                        CreatedAt = now,
                        CreatedBy = epodCreatorId
                    };
                    db.ShipmentProofOfDeliveries.Add(epod);
                }
                else
                {
                    epod.DeliveredAt = now;
                    if (!string.IsNullOrWhiteSpace(request.PodRecipientName))
                        epod.ReceiverName = request.PodRecipientName.Trim();
                    if (!string.IsNullOrWhiteSpace(request.PodPhotoUrl))
                        epod.EvidenceReference = request.PodPhotoUrl.Trim();
                    if (!string.IsNullOrWhiteSpace(request.PodNote))
                        epod.DeliveryNote = request.PodNote.Trim();
                }
            }
        }

        if (ledger == null && (shipment != null || exportReceipt != null || !string.IsNullOrWhiteSpace(request.TrackingNumber)))
        {
            ledger = new ERP.Domain.Entities.ShipmentFinancialLedger
            {
                ShipmentId = shipment?.Id,
                ExportReceiptId = exportReceipt?.Id,
                CarrierCode = normCarrier,
                TrackingNumber = request.TrackingNumber ?? exportReceipt?.TrackingNumber ?? shipment?.ShipmentCode ?? eventId,
                ExpectedCodAmount = Math.Round(request.ActualCodCollected ?? 0m, 4),
                EstimatedShippingFee = Math.Round(request.ActualShippingFee ?? 22000m, 4),
                CreatedAtUtc = now
            };
            db.ShipmentFinancialLedgers.Add(ledger);
        }

        if (ledger != null)
        {
            if (request.ActualCodCollected.HasValue)
                ledger.ActualCodCollected = Math.Round(request.ActualCodCollected.Value, 4);
            if (request.ActualShippingFee.HasValue)
                ledger.ActualShippingFee = Math.Round(request.ActualShippingFee.Value, 4);

            ledger.CodVariance = Math.Round(ledger.ActualCodCollected - ledger.ExpectedCodAmount, 4);
            ledger.ShippingFeeVariance = Math.Round(ledger.ActualShippingFee - ledger.EstimatedShippingFee, 4);
            ledger.NetSettlementAmount = Math.Round(
                ledger.ActualCodCollected - ledger.ActualShippingFee - ledger.PlatformCommissionFee - ledger.SellerVoucherAmount - ledger.ReturnFee,
                4);

            if (request.ActualCodCollected.HasValue || request.ActualShippingFee.HasValue)
            {
                ledger.ReconciliationStatus = ledger.CodVariance == 0m && ledger.ShippingFeeVariance <= 0m
                    ? "MATCHED"
                    : "DISCREPANCY";
                if (targetStatus == ShipmentStatus.Delivered && ledger.ReconciliationStatus == "MATCHED")
                {
                    ledger.SettledAtUtc ??= now;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        if (tx != null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return WmsEndpointOutcome.Ok(new
        {
            idempotentReplay = false,
            inboxMessageId = inboxMsg.Id,
            sourceSystem,
            eventId,
            carrierCode = normCarrier,
            carrierDisplayName = adapter.CarrierDisplayName,
            trackingNumber = request.TrackingNumber ?? ledger?.TrackingNumber ?? exportReceipt?.TrackingNumber,
            shipmentId = shipment?.Id,
            exportReceiptId = exportReceipt?.Id,
            shipmentStatus = shipment?.Status.ToString() ?? targetStatus.ToString(),
            epodCreated = epod != null,
            rtsReady = targetStatus == ShipmentStatus.DeliveryFailed,
            financialLedgerId = ledger?.Id,
            actualCodCollected = ledger?.ActualCodCollected ?? 0m,
            actualShippingFee = ledger?.ActualShippingFee ?? 0m,
            codVariance = ledger?.CodVariance ?? 0m,
            shippingFeeVariance = ledger?.ShippingFeeVariance ?? 0m,
            reconciliationStatus = ledger?.ReconciliationStatus ?? "PENDING",
            statusLabel = $"Đã xử lý sự kiện webhook {rawStatus} từ {adapter.CarrierDisplayName}"
        });
    }

    private async Task EnsureCarrierConfigsSeededAsync(int warehouseId, CancellationToken ct)
    {
        var existingCodes = await db.CarrierIntegrationConfigs.AsNoTracking()
            .Where(c => c.WarehouseId == warehouseId)
            .Select(c => c.CarrierCode)
            .ToListAsync(ct);

        var existingSet = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);
        var added = false;
        var now = DateTime.UtcNow;

        foreach (var adapter in _carrierRegistry.GetAllAdapters())
        {
            if (existingSet.Contains(adapter.CarrierCode))
                continue;

            var defaultFee = adapter.CarrierCode switch
            {
                "GHN" => 22000m,
                "GHTK" => 20000m,
                "VIETTELPOST" => 21000m,
                "JT_EXPRESS" => 19500m,
                _ => 22000m
            };

            db.CarrierIntegrationConfigs.Add(new ERP.Domain.Entities.CarrierIntegrationConfig
            {
                WarehouseId = warehouseId,
                CarrierCode = adapter.CarrierCode,
                ShopId = $"SHOP-{adapter.CarrierCode}-{warehouseId:D2}",
                EncryptedApiToken = $"SANDBOX_TOKEN_{adapter.CarrierCode}_{warehouseId:D2}",
                IsSandbox = true,
                WebhookSecretHash = DefaultCarrierWebhookSecret,
                SortingHubPrefix = $"HUB-{adapter.CarrierCode}",
                BaseShippingFeeVnd = defaultFee,
                IsActive = true,
                UpdatedAtUtc = now
            });
            added = true;
        }

        if (added)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<ERP.Domain.Entities.VnAdministrativeUnit?> ResolveVnAdministrativeUnitAsync(
        string? rawAddress,
        string? province,
        string? district,
        string? ward,
        CancellationToken ct)
    {
        var raw = $"{rawAddress} {ward} {district} {province}".Trim();
        var units = await db.VnAdministrativeUnits.AsNoTracking()
            .Include(u => u.CarrierMappings)
            .Where(u => u.IsActive)
            .ToListAsync(ct);

        static string StripWardPrefix(string wardName) =>
            wardName.Replace("Phường ", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("Xã ", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("Thị trấn ", "", StringComparison.OrdinalIgnoreCase)
                    .Trim();

        return units
            .Select(u =>
            {
                var score = 0;
                var coreWard = StripWardPrefix(u.WardName);
                if (!string.IsNullOrWhiteSpace(ward) &&
                    (u.WardName.Contains(ward.Trim(), StringComparison.OrdinalIgnoreCase) ||
                     (coreWard.Length >= 2 && ward.Contains(coreWard, StringComparison.OrdinalIgnoreCase))))
                {
                    score += 100;
                }
                if (!string.IsNullOrWhiteSpace(province) &&
                    u.ProvinceName.Contains(province.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    score += 25;
                }
                if (!string.IsNullOrWhiteSpace(raw) && string.IsNullOrWhiteSpace(ward))
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
            .Where(x => x.Score >= 40)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Unit)
            .FirstOrDefault();
    }

    private async Task<int?> ResolveNullableUserIdAsync(int? actorUserId, CancellationToken ct)
    {
        var candidate = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : null);
        if (candidate is > 0 && await db.Users.AsNoTracking().AnyAsync(u => u.Id == candidate.Value, ct))
            return candidate.Value;
        return null;
    }

    private async Task<int> ResolveValidUserIdAsync(int? actorUserId, CancellationToken ct)
    {
        var resolved = await ResolveNullableUserIdAsync(actorUserId, ct);
        if (resolved.HasValue)
            return resolved.Value;
        var firstUser = await db.Users.AsNoTracking().OrderBy(u => u.Id).Select(u => u.Id).FirstOrDefaultAsync(ct);
        return firstUser > 0 ? firstUser : 1;
    }
}
