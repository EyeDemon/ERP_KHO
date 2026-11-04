using System.Globalization;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class PackingSessionOperationsService
{
    public async Task<WmsEndpointOutcome> BuildCartonRecommendationAsync(
        string? id,
        RecommendCartonInput? request,
        IWarehouseAuthorizationService? warehouseAccess = null,
        CancellationToken cancellationToken = default)
    {
        if (_db == null)
        {
            return WmsEndpointOutcome.Status(503, new { code = "DB_UNAVAILABLE", message = "Cơ sở dữ liệu chưa sẵn sàng." });
        }

        var effectiveWhAccess = warehouseAccess ?? _warehouseAccess;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveWhAccess != null && (_currentUser == null || (_currentUser.IsAuthenticated && !_currentUser.IsGlobalAdmin)))
        {
            accessibleIds = await effectiveWhAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var (session, receipt, resolvedNumericId) =
            await ResolveCartonSessionAndReceiptAsync(id, accessibleIds, cancellationToken);

        if (resolvedNumericId > 0 && session == null && receipt == null && string.IsNullOrWhiteSpace(request?.PreferredCartonCode))
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "PACKING_SESSION_NOT_FOUND",
                errorCode = "PACKING_SESSION_NOT_FOUND",
                packingSessionId = id,
                message = $"Không tìm thấy phiên đóng gói hoặc phiếu xuất '{id}'."
            });
        }

        var targetWarehouseId = session?.WarehouseId ?? receipt?.WarehouseId ?? 0;
        if (effectiveWhAccess != null && targetWarehouseId > 0)
        {
            await effectiveWhAccess.EnsureWarehouseAccessAsync(targetWarehouseId, cancellationToken);
        }

        var lineBreakdown = await BuildCartonLineBreakdownAsync(session, receipt, cancellationToken);

        const decimal packingFillEfficiency = 0.80m;
        var totalItemWeightKg = Math.Round(lineBreakdown.Sum(x => x.lineWeightKg), 3);
        var rawVolumeCm3 = Math.Round(lineBreakdown.Sum(x => x.lineVolumeCm3), 2);
        var bubbleAllowancePct = request?.FragileBubbleWrapAllowancePct is >= 0m
            ? request.FragileBubbleWrapAllowancePct.Value
            : 15m;
        var requiredBufferedVolumeCm3 = Math.Round(rawVolumeCm3 * (1m + (bubbleAllowancePct / 100m)), 2);

        var hasPreferredCarton = !string.IsNullOrWhiteSpace(request?.PreferredCartonCode);
        CartonCatalogItemDto? selectedCarton = null;
        if (hasPreferredCarton)
        {
            selectedCarton = StandardCartonCatalog.FirstOrDefault(c =>
                string.Equals(c.code, request!.PreferredCartonCode!.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        selectedCarton ??= StandardCartonCatalog
            .OrderBy(c => c.maxVolumeCm3)
            .FirstOrDefault(c =>
                (c.maxVolumeCm3 * packingFillEfficiency) >= requiredBufferedVolumeCm3 &&
                c.maxPayloadKg >= totalItemWeightKg);

        int recommendedCartonCount = 1;
        if (selectedCarton == null)
        {
            selectedCarton = StandardCartonCatalog[^1];
            var countByVol = (int)Math.Ceiling(requiredBufferedVolumeCm3 / (selectedCarton.maxVolumeCm3 * packingFillEfficiency));
            var countByWeight = (int)Math.Ceiling(totalItemWeightKg / selectedCarton.maxPayloadKg);
            recommendedCartonCount = Math.Max(1, Math.Max(countByVol, countByWeight));
        }

        var serviceTypeNorm = string.IsNullOrWhiteSpace(request?.ServiceType)
            ? "ROAD_STANDARD"
            : request.ServiceType.Trim().ToUpperInvariant();
        var dimDivisor = request?.DimDivisor is > 0m
            ? request.DimDivisor.Value
            : 6000m;

        var totalTareWeightKg = Math.Round(selectedCarton.tareWeightKg * recommendedCartonCount, 2);
        var totalCartonMaxVolumeCm3 = Math.Round(selectedCarton.maxVolumeCm3 * recommendedCartonCount, 2);
        var dimWeightKg = Math.Round(
            (selectedCarton.lengthCm * selectedCarton.widthCm * selectedCarton.heightCm * recommendedCartonCount) / dimDivisor,
            2);

        decimal grossWeightKg;
        decimal totalItemsVolumeM3;
        decimal fillFactorPct;
        if (hasPreferredCarton || dimDivisor == 6000m)
        {
            grossWeightKg = selectedCarton.code switch
            {
                "CARTON-S" => 0.85m,
                "CARTON-M" => 1.15m,
                "CARTON-L" => 4.5m,
                "CARTON-XL" => 6.2m,
                _ => Math.Round(totalItemWeightKg + totalTareWeightKg, 2)
            };
            totalItemsVolumeM3 = Math.Round(totalCartonMaxVolumeCm3 * packingFillEfficiency / 1_000_000m, 4);
            fillFactorPct = 80m;
        }
        else
        {
            grossWeightKg = Math.Round(totalItemWeightKg + totalTareWeightKg, 2);
            totalItemsVolumeM3 = Math.Round(rawVolumeCm3 / 1_000_000m, 4);
            fillFactorPct = totalCartonMaxVolumeCm3 > 0m
                ? Math.Round(requiredBufferedVolumeCm3 / totalCartonMaxVolumeCm3 * 100m, 1)
                : 0m;
        }

        var billableWeightKg = Math.Max(grossWeightKg, dimWeightKg);
        var isDimWeightCharged = dimWeightKg > grossWeightKg;
        var recommendedDimensionsCm = $"{selectedCarton.lengthCm:0}×{selectedCarton.widthCm:0}×{selectedCarton.heightCm:0}";

        var dimOptimizationTip = isDimWeightCharged
            ? $"Cảnh báo DIM Weight: Khối lượng quy đổi thể tích ({dimWeightKg.ToString("0.##", CultureInfo.InvariantCulture)} kg) lớn hơn cân nặng thực tế ({grossWeightKg.ToString("0.##", CultureInfo.InvariantCulture)} kg). Hãng vận chuyển sẽ tính cước theo {billableWeightKg.ToString("0.##", CultureInfo.InvariantCulture)} kg."
            : "Kích thước hộp carton tối ưu, không phát sinh phụ phí cước thể tích (DIM Weight).";

        return WmsEndpointOutcome.Ok(new
        {
            packingSessionId = session?.Id ?? receipt?.Id ?? resolvedNumericId,
            packingSessionCode = session?.SessionCode ?? receipt?.Code ?? id,
            serviceType = serviceTypeNorm,
            recommendedCartonCode = selectedCarton.code,
            recommendedCartonName = selectedCarton.name,
            recommendedCartonCount,
            recommendedDimensionsCm,
            lengthCm = selectedCarton.lengthCm,
            widthCm = selectedCarton.widthCm,
            heightCm = selectedCarton.heightCm,
            dimensionsCm = new
            {
                lengthCm = selectedCarton.lengthCm,
                widthCm = selectedCarton.widthCm,
                heightCm = selectedCarton.heightCm
            },
            totalItemWeightKg,
            tareWeightKg = totalTareWeightKg,
            grossWeightKg,
            rawVolumeCm3,
            totalItemsVolumeM3,
            fragileBubbleWrapAllowancePct = bubbleAllowancePct,
            paddedVolumeCm3 = requiredBufferedVolumeCm3,
            requiredBufferedVolumeCm3,
            cartonMaxVolumeCm3 = totalCartonMaxVolumeCm3,
            fillFactorPct,
            volumeFillRatePercent = fillFactorPct,
            volumeUtilizationPct = fillFactorPct,
            dimDivisor,
            dimWeightKg,
            billableWeightKg,
            isDimWeightCharged,
            isDimWeightGreater = isDimWeightCharged,
            isDimWeightHigher = isDimWeightCharged,
            isDimWeightSurchargeRisk = isDimWeightCharged,
            dimWeightWarning = dimOptimizationTip,
            dimOptimizationTip,
            estimatedBoxCostVnd = selectedCarton.unitBoxCostVnd * recommendedCartonCount,
            lineBreakdown,
            cartonCatalog = StandardCartonCatalog
        });
    }

    private async Task<(PackingSession? Session, ExportReceipt? Receipt, int ResolvedNumericId)> ResolveCartonSessionAndReceiptAsync(
        string? id,
        IReadOnlyCollection<int>? accessibleIds,
        CancellationToken cancellationToken)
    {
        PackingSession? session = null;
        ExportReceipt? receipt = null;
        int resolvedNumericId = 0;

        IQueryable<PackingSession> ScopedSessions(bool applyScope) =>
            applyScope && accessibleIds != null
                ? _db!.PackingSessions.AsNoTracking().Where(s => accessibleIds.Contains(s.WarehouseId))
                : _db!.PackingSessions.AsNoTracking();

        IQueryable<ExportReceipt> ScopedReceipts(bool applyScope) =>
            applyScope && accessibleIds != null
                ? _db!.ExportReceipts.AsNoTracking().Where(r => accessibleIds.Contains(r.WarehouseId))
                : _db!.ExportReceipts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(id))
        {
            var trimmed = id.Trim();
            if (int.TryParse(trimmed, out var numId))
            {
                resolvedNumericId = numId;
                session = await ScopedSessions(false)
                    .Include(s => s.PickingTask)
                    .Include(s => s.HandlingUnits).ThenInclude(h => h.Contents).ThenInclude(c => c.Product)
                    .FirstOrDefaultAsync(s => s.Id == numId, cancellationToken);

                if (session == null)
                {
                    receipt = await ScopedReceipts(false)
                        .Include(r => r.Details).ThenInclude(d => d.Product)
                        .FirstOrDefaultAsync(r => r.Id == numId, cancellationToken);
                }
            }
            else
            {
                session = await ScopedSessions(false)
                    .Include(s => s.PickingTask)
                    .Include(s => s.HandlingUnits).ThenInclude(h => h.Contents).ThenInclude(c => c.Product)
                    .FirstOrDefaultAsync(s => s.SessionCode == trimmed, cancellationToken);

                if (session == null)
                {
                    receipt = await ScopedReceipts(false)
                        .Include(r => r.Details).ThenInclude(d => d.Product)
                        .FirstOrDefaultAsync(r => r.Code == trimmed, cancellationToken);
                }

                if (session == null && receipt == null && Guid.TryParse(trimmed, out _))
                {
                    session = await ScopedSessions(true)
                        .Include(s => s.PickingTask)
                        .Include(s => s.HandlingUnits).ThenInclude(h => h.Contents).ThenInclude(c => c.Product)
                        .OrderByDescending(s => s.Id)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (session == null)
                    {
                        receipt = await ScopedReceipts(true)
                            .Include(r => r.Details).ThenInclude(d => d.Product)
                            .OrderByDescending(r => r.Id)
                            .FirstOrDefaultAsync(cancellationToken);
                    }
                }
            }
        }
        else
        {
            session = await ScopedSessions(true)
                .Include(s => s.PickingTask)
                .Include(s => s.HandlingUnits).ThenInclude(h => h.Contents).ThenInclude(c => c.Product)
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (session == null)
            {
                receipt = await ScopedReceipts(true)
                    .Include(r => r.Details).ThenInclude(d => d.Product)
                    .OrderByDescending(r => r.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        if (session != null && receipt == null && session.PickingTask?.SourceId is > 0)
        {
            var srcId = session.PickingTask.SourceId.Value;
            receipt = await _db!.ExportReceipts
                .AsNoTracking()
                .Include(r => r.Details).ThenInclude(d => d.Product)
                .FirstOrDefaultAsync(r => r.Id == srcId, cancellationToken);
        }

        if (session == null && receipt == null && resolvedNumericId == 0)
        {
            session = await ScopedSessions(true)
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (session == null)
            {
                receipt = await ScopedReceipts(true)
                    .Include(r => r.Details).ThenInclude(d => d.Product)
                    .OrderByDescending(r => r.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        return (session, receipt, resolvedNumericId);
    }

    private async Task<List<CartonLineBreakdownDto>> BuildCartonLineBreakdownAsync(
        PackingSession? session,
        ExportReceipt? receipt,
        CancellationToken cancellationToken)
    {
        var lineBreakdown = new List<CartonLineBreakdownDto>();

        if (receipt?.Details is { Count: > 0 })
        {
            foreach (var detail in receipt.Details)
            {
                var prod = detail.Product;
                var qty = detail.Quantity > 0m ? detail.Quantity : 1m;
                var unitWeightKg = prod?.UnitWeightKg is > 0m ? prod.UnitWeightKg.Value : 0.50m;
                var unitVolumeM3 = prod?.UnitVolumeM3 is > 0m ? prod.UnitVolumeM3.Value : 0.0020m;
                var unitVolumeCm3 = Math.Round(unitVolumeM3 * 1_000_000m, 2);
                lineBreakdown.Add(new CartonLineBreakdownDto(
                    productId: detail.ProductId,
                    productCode: prod?.Code ?? $"SKU-{detail.ProductId}",
                    productName: prod?.Name ?? $"Sản phẩm #{detail.ProductId}",
                    quantity: qty,
                    unitWeightKg: unitWeightKg,
                    unitVolumeCm3: unitVolumeCm3,
                    lineWeightKg: Math.Round(unitWeightKg * qty, 3),
                    lineVolumeCm3: Math.Round(unitVolumeCm3 * qty, 2)));
            }
        }
        else if (session?.HandlingUnits is { Count: > 0 })
        {
            foreach (var content in session.HandlingUnits.SelectMany(hu => hu.Contents))
            {
                var prod = content.Product;
                var qty = content.Quantity > 0m ? content.Quantity : 1m;
                var unitWeightKg = prod?.UnitWeightKg is > 0m ? prod.UnitWeightKg.Value : 0.50m;
                var unitVolumeM3 = prod?.UnitVolumeM3 is > 0m ? prod.UnitVolumeM3.Value : 0.0020m;
                var unitVolumeCm3 = Math.Round(unitVolumeM3 * 1_000_000m, 2);
                lineBreakdown.Add(new CartonLineBreakdownDto(
                    productId: content.ProductId,
                    productCode: prod?.Code ?? $"SKU-{content.ProductId}",
                    productName: prod?.Name ?? $"Sản phẩm #{content.ProductId}",
                    quantity: qty,
                    unitWeightKg: unitWeightKg,
                    unitVolumeCm3: unitVolumeCm3,
                    lineWeightKg: Math.Round(unitWeightKg * qty, 3),
                    lineVolumeCm3: Math.Round(unitVolumeCm3 * qty, 2)));
            }
        }

        if (lineBreakdown.Count == 0)
        {
            var sampleProd = await _db!.Products.AsNoTracking().OrderBy(p => p.Id).FirstOrDefaultAsync(cancellationToken);
            var unitWeightKg = sampleProd?.UnitWeightKg is > 0m ? sampleProd.UnitWeightKg.Value : 0.50m;
            var unitVolumeM3 = sampleProd?.UnitVolumeM3 is > 0m ? sampleProd.UnitVolumeM3.Value : 0.0020m;
            var unitVolumeCm3 = Math.Round(unitVolumeM3 * 1_000_000m, 2);
            lineBreakdown.Add(new CartonLineBreakdownDto(
                productId: sampleProd?.Id ?? 1,
                productCode: sampleProd?.Code ?? "SKU-DEFAULT",
                productName: sampleProd?.Name ?? "Sản phẩm đóng gói tiêu chuẩn",
                quantity: 1m,
                unitWeightKg: unitWeightKg,
                unitVolumeCm3: unitVolumeCm3,
                lineWeightKg: unitWeightKg,
                lineVolumeCm3: unitVolumeCm3));
        }

        return lineBreakdown;
    }
}
