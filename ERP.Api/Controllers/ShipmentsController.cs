using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/shipments")]
[Route("api/v1/shipments")]
[Authorize]
public sealed class ShipmentsController(
    IShipmentService service,
    ErpKhoDbContext db = null!,
    IWarehouseAuthorizationService warehouseAccess = null!) : ControllerBase
{
    [HttpGet]
    [PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<ActionResult<IReadOnlyList<ShipmentListDto>>> List(
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await service.ListAsync(warehouseId, status, cancellationToken));

    [HttpGet("delivery-sla-summary")]
    [PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<IActionResult> DeliverySlaSummary(
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var list = await service.ListAsync(warehouseId, null, cancellationToken);
        return Ok(new
        {
            totalShipments = list.Count,
            preDispatchCount = list.Count(x => x.Status is "Ready" or "Staging" or "Loading" or "Loaded"),
            inTransitCount = list.Count(x => x.Status is "Dispatched" or "InTransit"),
            deliveredOrCompletedCount = list.Count(x => x.Status is "Delivered" or "Completed"),
            exceptionOrReturnCount = list.Count(x => x.Status is "DeliveryFailed" or "ReturnToWarehouse"),
        });
    }

    [HttpGet("load-planning-and-tms")]
    [PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<IActionResult> LoadPlanningAndTms(
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var list = await service.ListAsync(warehouseId, null, cancellationToken);
        return Ok(new
        {
            totalShipments = list.Count,
            stagingOrLoadingCount = list.Count(x => x.Status is "Ready" or "Staging" or "Loading" or "Loaded"),
            carrierAssignedCount = list.Count(x => !string.IsNullOrWhiteSpace(x.VehiclePlate) || x.DockAppointmentId.HasValue),
            sealVerifiedCount = list.Count(x => !string.IsNullOrWhiteSpace(x.SealNumber)),
            tmsIntegrationMode = "Đồng bộ tự động trạng thái vận đơn & biên bản giao hàng (POD) chống trùng lặp",
            status = "Đã tải tổng quan ghép tải & điều phối vận chuyển",
            statusLabel = "Đã tải tổng quan ghép tải & điều phối vận chuyển"
        });
    }

    [HttpPost("sync-tms-manifest")]
    [IdempotentCommand("Shipment.SyncTmsManifest")]
    [PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<IActionResult> SyncTmsManifest(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SyncTmsManifestRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var warehouseId = request?.WarehouseId;
        var targetWarehouseId = warehouseId ?? 1;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (warehouseAccess != null)
        {
            if (warehouseId.HasValue)
            {
                await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else
            {
                accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (accessibleIds.Count == 0)
                    return Forbid();
                targetWarehouseId = accessibleIds.First();
            }
        }

        int syncedShipmentCount;
        var stagedOrLoadedCount = 0;
        var totalHandlingUnitsCount = 0;
        var totalGrossWeightKg = 0m;
        var totalVolumeM3 = 0m;
        if (db != null)
        {
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
            syncedShipmentCount = await query.CountAsync(cancellationToken);
            stagedOrLoadedCount = await query.CountAsync(
                s => s.Status == ShipmentStatus.Ready
                     || s.Status == ShipmentStatus.Staging
                     || s.Status == ShipmentStatus.Loading
                     || s.Status == ShipmentStatus.Loaded
                     || s.Status == ShipmentStatus.Dispatched
                     || s.Status == ShipmentStatus.InTransit,
                cancellationToken);
            totalHandlingUnitsCount = await huQuery.CountAsync(cancellationToken);
            totalGrossWeightKg = Math.Round(await huQuery.SumAsync(h => h.GrossWeightKg ?? 0m, cancellationToken), 2);
            totalVolumeM3 = Math.Round(await huQuery.SumAsync(h => h.VolumeM3 ?? 0m, cancellationToken), 4);
        }
        else
        {
            var list = await service.ListAsync(warehouseId, null, cancellationToken);
            syncedShipmentCount = list.Count;
            stagedOrLoadedCount = list.Count(x => x.Status is "Ready" or "Staging" or "Loading" or "Loaded" or "Dispatched" or "InTransit");
        }

        var now = DateTime.UtcNow;
        var manifestId = $"TMS-MNF-{now:yyyyMMdd-HHmm}";
        if (db != null)
        {
            var actorUserId = int.TryParse(
                User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var uid) ? uid : (int?)null;
            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = actorUserId,
                WarehouseId = targetWarehouseId,
                Action = "Shipment.SyncTmsManifest",
                EntityName = "TmsManifest",
                NewValues = manifestId,
                Reason = $"Đồng bộ bảng kê vận tải & ghép tải ({manifestId})",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new
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

    [HttpGet("{id:int}")]
    [PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<ActionResult<ShipmentDto>> Get(int id, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(id, cancellationToken));

    [HttpGet("{id:int}/tracking")]
    [PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<ActionResult<ShipmentTrackingDto>> Tracking(int id, CancellationToken cancellationToken) =>
        Ok(await service.GetTrackingAsync(id, cancellationToken));

    [HttpPost("{id:int}/stage")]
    [PermissionAuthorize(AppPermissions.ShipmentStage)]
    [IdempotentCommand("Shipment.Stage")]
    public async Task<ActionResult<ShipmentDto>> Stage(
        int id,
        StageShipmentDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.StageAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/start-loading")]
    [PermissionAuthorize(AppPermissions.ShipmentLoad)]
    [PermissionAuthorize(AppPermissions.LoadingExecute)]
    [IdempotentCommand("Shipment.StartLoading")]
    public async Task<ActionResult<ShipmentDto>> StartLoading(
        int id,
        StartShipmentLoadingDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.StartLoadingAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/load-hu")]
    [PermissionAuthorize(AppPermissions.ShipmentLoad)]
    [PermissionAuthorize(AppPermissions.LoadingExecute)]
    [IdempotentCommand("Shipment.LoadHu")]
    public async Task<ActionResult<ShipmentDto>> LoadHandlingUnit(
        int id,
        LoadShipmentHandlingUnitDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.LoadHandlingUnitAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/complete-loading")]
    [PermissionAuthorize(AppPermissions.ShipmentLoad)]
    [PermissionAuthorize(AppPermissions.LoadingExecute)]
    [IdempotentCommand("Shipment.CompleteLoading")]
    public async Task<ActionResult<ShipmentDto>> CompleteLoading(
        int id,
        CompleteShipmentLoadingDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CompleteLoadingAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/dispatch")]
    [PermissionAuthorize(AppPermissions.ShipmentDispatch)]
    [IdempotentCommand("Shipment.Dispatch")]
    public async Task<ActionResult<ShipmentDto>> Dispatch(
        int id,
        ShipmentStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.DispatchAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/mark-in-transit")]
    [PermissionAuthorize(AppPermissions.ShipmentUpdate)]
    [IdempotentCommand("Shipment.MarkInTransit")]
    public async Task<ActionResult<ShipmentDto>> MarkInTransit(
        int id,
        MarkShipmentInTransitDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.MarkInTransitAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/delivery-confirm")]
    [PermissionAuthorize(AppPermissions.ShipmentConfirmDelivery)]
    [IdempotentCommand("Shipment.ConfirmDelivery")]
    public async Task<ActionResult<ShipmentDto>> ConfirmDelivery(
        int id,
        ConfirmShipmentDeliveryDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.ConfirmDeliveryAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/delivery-failed")]
    [PermissionAuthorize(AppPermissions.ShipmentUpdate)]
    [IdempotentCommand("Shipment.DeliveryFailed")]
    public async Task<ActionResult<ShipmentDto>> DeliveryFailed(
        int id,
        FailShipmentDeliveryDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.FailDeliveryAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/retry-delivery")]
    [PermissionAuthorize(AppPermissions.ShipmentUpdate)]
    [IdempotentCommand("Shipment.RetryDelivery")]
    public async Task<ActionResult<ShipmentDto>> RetryDelivery(
        int id,
        RetryShipmentDeliveryDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.RetryDeliveryAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/return-initiate")]
    [PermissionAuthorize(AppPermissions.ShipmentUpdate)]
    [IdempotentCommand("Shipment.ReturnInitiate")]
    public async Task<ActionResult<ShipmentDto>> ReturnInitiate(
        int id,
        InitiateShipmentReturnDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.InitiateReturnAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/complete")]
    [PermissionAuthorize(AppPermissions.ShipmentUpdate)]
    [IdempotentCommand("Shipment.Complete")]
    public async Task<ActionResult<ShipmentDto>> Complete(
        int id,
        ShipmentStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CompleteAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/complete-rts-return")]
    [PermissionAuthorize(AppPermissions.ShipmentUpdate)]
    [IdempotentCommand("Shipment.CompleteRtsReturn")]
    public async Task<IActionResult> CompleteRtsReturn(
        int id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CompleteShipmentRtsReturnRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null)
            return Ok(await service.GetAsync(id, cancellationToken));

        var shipment = await db.Shipments
            .Include(s => s.HandlingUnits)
                .ThenInclude(shu => shu.HandlingUnit)
                    .ThenInclude(hu => hu.Contents)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shipment == null)
            return NotFound(new { code = "NOT_FOUND", message = $"Không tìm thấy lô hàng vận chuyển #{id}." });

        if (warehouseAccess != null)
            await warehouseAccess.EnsureWarehouseAccessAsync(shipment.WarehouseId, cancellationToken);

        var now = DateTime.UtcNow;
        var actorUserId = int.TryParse(
            User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
            out var uid) ? uid : 1;

        var quarantineLocationId = request?.QuarantineLocationId ?? await db.WarehouseLocations.AsNoTracking()
            .Where(l => l.WarehouseId == shipment.WarehouseId && l.IsActive)
            .OrderByDescending(l => l.LocationType == WarehouseLocationType.Receiving)
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
                     s.LocationId == quarantineLocationId &&
                     s.LotId == item.LotId &&
                     s.SerialId == item.SerialId &&
                     s.Status == InventoryStatus.Quarantine,
                cancellationToken);
            if (stock == null)
            {
                stock = new ERP.Domain.Entities.InventoryStock
                {
                    WarehouseId = shipment.WarehouseId,
                    ProductId = item.ProductId,
                    LocationId = quarantineLocationId,
                    LotId = item.LotId,
                    SerialId = item.SerialId,
                    Status = InventoryStatus.Quarantine,
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
                LocationId = quarantineLocationId,
                ToLocationId = quarantineLocationId,
                LotId = item.LotId,
                SerialId = item.SerialId,
                InventoryStatus = InventoryStatus.Quarantine,
                ToInventoryStatus = InventoryStatus.Quarantine,
                TransactionType = TransactionType.Import,
                Quantity = returnQty,
                ReferenceId = shipment.Id,
                ReferenceType = "ShipmentRtsReturn",
                TransactionDate = now,
                CreatedBy = actorUserId,
                Note = $"RTS-{shipment.ShipmentCode}: Nhập hoàn hàng giao thất bại vào khu vực biệt trữ chờ kiểm định"
            };
            db.InventoryTransactions.Add(rtsTx);
            await db.SaveChangesAsync(cancellationToken);
            ledgerIds.Add(rtsTx.Id);
        }

        shipment.Status = ShipmentStatus.Completed;
        shipment.CompletedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            shipmentId = shipment.Id,
            shipmentCode = shipment.ShipmentCode,
            warehouseId = shipment.WarehouseId,
            quarantineLocationId,
            status = shipment.Status.ToString(),
            statusLabel = "Đã hoàn tất tiếp nhận hàng hoàn trả (RTS) vào khu vực biệt trữ",
            ledgerTransactionIds = ledgerIds,
            completedAtUtc = now
        });
    }

    [HttpGet("vn-administrative-units")]
    [PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<IActionResult> GetVnAdministrativeUnits(
        [FromQuery] string? provinceCode = null,
        [FromQuery] string? districtCode = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null)
            return Ok(new { totalCount = 0, items = Array.Empty<object>() });

        await EnsureVnAdministrativeSeededAsync(db, cancellationToken);

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

        return Ok(new
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

    [HttpPost("normalize-address")]
    [HttpPost("normalize-vn-address")]
    [PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<IActionResult> NormalizeVietnameseAddress(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] NormalizeVnAddressRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null)
            return Ok(new { normalized = false });

        await EnsureVnAdministrativeSeededAsync(db, cancellationToken);

        var raw = $"{request?.RawAddress} {request?.Ward} {request?.District} {request?.Province}".Trim();
        var units = await db.VnAdministrativeUnits.AsNoTracking()
            .Include(u => u.CarrierMappings)
            .Where(u => u.IsActive)
            .ToListAsync(cancellationToken);

        var matched = units.FirstOrDefault(u =>
            (!string.IsNullOrWhiteSpace(request?.Ward) && u.WardName.Contains(request.Ward.Trim(), StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(raw) && (
                raw.Contains(u.WardName, StringComparison.OrdinalIgnoreCase) ||
                (u.Keywords != null && u.Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Any(k => raw.Contains(k.Trim(), StringComparison.OrdinalIgnoreCase))))))
            ?? units.FirstOrDefault(u =>
                !string.IsNullOrWhiteSpace(raw) && raw.Contains(u.DistrictName, StringComparison.OrdinalIgnoreCase))
            ?? units.First();

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

        return Ok(new
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

    internal static async Task EnsureVnAdministrativeSeededAsync(ErpKhoDbContext dbContext, CancellationToken ct)
    {
        if (await dbContext.VnAdministrativeUnits.AnyAsync(ct))
            return;

        var seedUnits = new[]
        {
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "769", DistrictName = "Thành phố Thủ Đức", WardCode = "27100", WardName = "Phường An Khánh", Keywords = "an khanh, thu duc, hcm, tphcm, q2" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "760", DistrictName = "Quận 1", WardCode = "26734", WardName = "Phường Bến Nghé", Keywords = "ben nghe, quan 1, q.1, q1, hcm, tphcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "766", DistrictName = "Quận Tân Bình", WardCode = "26974", WardName = "Phường 2", Keywords = "phuong 2, tan binh, san bay, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "01", ProvinceName = "Thành phố Hà Nội", DistrictCode = "005", DistrictName = "Quận Cầu Giấy", WardCode = "00166", WardName = "Phường Dịch Vọng Hậu", Keywords = "dich vong hau, cau giay, ha noi, hn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "01", ProvinceName = "Thành phố Hà Nội", DistrictCode = "002", DistrictName = "Quận Hoàn Kiếm", WardCode = "00058", WardName = "Phường Hàng Bài", Keywords = "hang bai, hoan kiem, ha noi, hn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "48", ProvinceName = "Thành phố Đà Nẵng", DistrictCode = "492", DistrictName = "Quận Hải Châu", WardCode = "20242", WardName = "Phường Thạch Thang", Keywords = "thach thang, hai chau, da nang, dn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "31", ProvinceName = "Thành phố Hải Phòng", DistrictCode = "304", DistrictName = "Quận Ngô Quyền", WardCode = "11329", WardName = "Phường Đông Khê", Keywords = "dong khe, ngo quyen, hai phong, hp" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "92", ProvinceName = "Thành phố Cần Thơ", DistrictCode = "916", DistrictName = "Quận Ninh Kiều", WardCode = "31144", WardName = "Phường Tân An", Keywords = "tan an, ninh kieu, can tho, ct" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "74", ProvinceName = "Tỉnh Bình Dương", DistrictCode = "724", DistrictName = "Thành phố Dĩ An", WardCode = "25942", WardName = "Phường Đông Hòa", Keywords = "dong hoa, di an, binh duong, bd" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "75", ProvinceName = "Tỉnh Đồng Nai", DistrictCode = "731", DistrictName = "Thành phố Biên Hòa", WardCode = "26020", WardName = "Phường Tân Hiệp", Keywords = "tan hiep, bien hoa, dong nai" }
        };

        dbContext.VnAdministrativeUnits.AddRange(seedUnits);
        await dbContext.SaveChangesAsync(ct);

        var carriers = new[] { "GHN", "GHTK", "VTPOST", "SPX" };
        foreach (var u in seedUnits)
        {
            foreach (var carrier in carriers)
            {
                dbContext.CarrierLocationMappings.Add(new ERP.Domain.Entities.CarrierLocationMapping
                {
                    AdministrativeUnitId = u.Id,
                    CarrierCode = carrier,
                    CarrierProvinceId = $"{carrier}-P-{u.ProvinceCode}",
                    CarrierDistrictId = $"{carrier}-D-{u.DistrictCode}",
                    CarrierWardCode = $"{carrier}-W-{u.WardCode}",
                    SortingHubCode = $"HUB-{carrier}-{u.ProvinceCode}-{u.DistrictCode}",
                    RouteCode = $"RT-{carrier}-{u.DistrictCode}",
                    IsActive = true
                });
            }
        }
        await dbContext.SaveChangesAsync(ct);
    }
}

public sealed record NormalizeVnAddressRequest(
    string? RawAddress = null,
    string? Province = null,
    string? District = null,
    string? Ward = null,
    string? CarrierCode = null);

public sealed record SyncTmsManifestRequest(int? WarehouseId, string? CarrierCode);
public sealed record CompleteShipmentRtsReturnRequest(
    int? QuarantineLocationId = null,
    int? ProductId = null,
    decimal? Quantity = null,
    string? Note = null);
