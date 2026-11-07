using ERP.Application.Common;
using ERP.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ExportReceiptOperationsService
{
    public Task<HandoverManifestOperationResult> GetHandoverManifestsAsync(
        int? warehouseId,
        string? carrierCode,
        IExportReceiptService? exportReceiptServiceOverride = null,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default)
        => ResolveHandoverManifests(exportReceiptServiceOverride, warehouseAccessOverride, currentUserOverride)
            .GetHandoverManifestsAsync(warehouseId, carrierCode, cancellationToken);

    public Task<HandoverManifestOperationResult> ScanHandoverAwbAsync(
        int? manifestIdRoute,
        ScanHandoverAwbInputDto? request,
        int? actorUserId,
        bool isAuthenticated,
        IExportReceiptService? exportReceiptServiceOverride = null,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default)
        => ResolveHandoverManifests(exportReceiptServiceOverride, warehouseAccessOverride, currentUserOverride)
            .ScanHandoverAwbAsync(manifestIdRoute, request, actorUserId, isAuthenticated, cancellationToken);

    public Task<HandoverManifestOperationResult> SealHandoverManifestAsync(
        int id,
        SealHandoverManifestInputDto? request,
        int? actorUserId,
        bool isAuthenticated,
        IExportReceiptService? exportReceiptServiceOverride = null,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default)
        => ResolveHandoverManifests(exportReceiptServiceOverride, warehouseAccessOverride, currentUserOverride)
            .SealHandoverManifestAsync(id, request, actorUserId, isAuthenticated, cancellationToken);

    public Task<HandoverManifestOperationResult> ConfirmCarrierHandoverAsync(
        ConfirmCarrierHandoverInputDto? request,
        int? actorUserId,
        bool isAuthenticated,
        IExportReceiptService? exportReceiptServiceOverride = null,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default)
        => ResolveHandoverManifests(exportReceiptServiceOverride, warehouseAccessOverride, currentUserOverride)
            .ConfirmCarrierHandoverAsync(request, actorUserId, isAuthenticated, cancellationToken);

    public async Task<WmsEndpointOutcome> GetCarrierAddressMappingsAsync(
        string? province,
        string? district,
        string? ward,
        CancellationToken cancellationToken = default)
    {
        await VnAdministrativeSeedHelper.EnsureVnAdministrativeSeededAsync(_db, cancellationToken);

        var allUnits = await _db.VnAdministrativeUnits
            .AsNoTracking()
            .Include(u => u.CarrierMappings)
            .Where(u => u.IsActive)
            .ToListAsync(cancellationToken);

        var filteredUnits = allUnits.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(province))
        {
            var p = province.Trim();
            filteredUnits = filteredUnits.Where(u =>
                u.ProvinceName.Contains(p, StringComparison.OrdinalIgnoreCase) ||
                p.Contains(u.ProvinceName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(u.ProvinceCode, p, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(ward))
        {
            var w = ward.Trim();
            filteredUnits = filteredUnits.Where(u =>
                u.WardName.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                w.Contains(u.WardName, StringComparison.OrdinalIgnoreCase) ||
                (u.Keywords != null && u.Keywords.Contains(w, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(u.WardCode, w, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(district))
        {
            var d = district.Trim();
            var byDistrict = filteredUnits.Where(u =>
                (!string.IsNullOrWhiteSpace(u.DistrictName) && (u.DistrictName.Contains(d, StringComparison.OrdinalIgnoreCase) || d.Contains(u.DistrictName, StringComparison.OrdinalIgnoreCase))) ||
                (!string.IsNullOrWhiteSpace(u.DistrictCode) && string.Equals(u.DistrictCode, d, StringComparison.OrdinalIgnoreCase))).ToList();
            if (byDistrict.Count > 0)
            {
                filteredUnits = byDistrict;
            }
        }

        var matchedUnits = filteredUnits.ToList();
        var matched = matchedUnits.Count > 0;
        var primaryUnit = matchedUnits.FirstOrDefault() ?? allUnits.FirstOrDefault();
        var unitIds = matchedUnits.Select(u => u.Id).ToHashSet();
        var dbMappings = await _db.CarrierLocationMappings
            .AsNoTracking()
            .Where(m => unitIds.Count == 0 || unitIds.Contains(m.AdministrativeUnitId))
            .ToListAsync(cancellationToken);

        var ghnMap = primaryUnit?.CarrierMappings.FirstOrDefault(m => m.CarrierCode == "GHN");
        var ghtkMap = primaryUnit?.CarrierMappings.FirstOrDefault(m => m.CarrierCode == "GHTK");
        var vtpMap = primaryUnit?.CarrierMappings.FirstOrDefault(m => m.CarrierCode == "VTP" || m.CarrierCode == "VIETTELPOST" || m.CarrierCode == "VTPOST");
        var jtMap = primaryUnit?.CarrierMappings.FirstOrDefault(m => m.CarrierCode == "JT" || m.CarrierCode == "J&T" || m.CarrierCode == "SPX");

        return WmsEndpointOutcome.Ok(new
        {
            administrativeModel = "VN_2_TIER_2025_2026",
            matched,
            input = new { province, district, ward },
            persistedUnitsCount = allUnits.Count,
            matchedUnitsCount = matchedUnits.Count,
            units = matchedUnits.Select(u => new
            {
                u.Id,
                u.ProvinceCode,
                u.ProvinceName,
                u.WardCode,
                u.WardName,
                legacyDistrictCode = u.DistrictCode,
                legacyDistrictName = u.DistrictName,
                legacyWardAliases = u.Keywords,
                carrierMappings = u.CarrierMappings.Select(m => new
                {
                    m.Id,
                    m.CarrierCode,
                    m.CarrierProvinceId,
                    m.CarrierDistrictId,
                    m.CarrierWardCode,
                    sortingRouteCode = m.RouteCode
                })
            }),
            carrierMappings = dbMappings.Select(m => new
            {
                m.Id,
                m.AdministrativeUnitId,
                m.CarrierCode,
                m.CarrierProvinceId,
                m.CarrierDistrictId,
                m.CarrierWardCode,
                sortingRouteCode = m.RouteCode
            }),
            normalized = primaryUnit == null ? null : new
            {
                provinceCode = primaryUnit.ProvinceCode,
                provinceName = primaryUnit.ProvinceName,
                districtName = primaryUnit.DistrictName,
                wardCode = primaryUnit.WardCode,
                wardName = primaryUnit.WardName,
                ghn = ghnMap == null ? null : new { provinceId = ghnMap.CarrierProvinceId, districtId = ghnMap.CarrierDistrictId, wardCode = ghnMap.CarrierWardCode, routeCode = ghnMap.RouteCode },
                ghtk = ghtkMap == null ? null : new { provinceCode = ghtkMap.CarrierProvinceId, districtCode = ghtkMap.CarrierDistrictId, wardCode = ghtkMap.CarrierWardCode, sortingHub = ghtkMap.RouteCode },
                viettelPost = vtpMap == null ? null : new { provinceId = vtpMap.CarrierProvinceId, districtId = vtpMap.CarrierDistrictId, wardId = vtpMap.CarrierWardCode, postOfficeCode = vtpMap.RouteCode },
                jtExpress = jtMap == null ? null : new { areaCode = jtMap.CarrierDistrictId, sortingCode = jtMap.RouteCode }
            },
            statusLabel = "Đã truy vấn & chuẩn hóa địa giới hành chính Việt Nam sang mã định tuyến 3PL từ cơ sở dữ liệu"
        });
    }
}
