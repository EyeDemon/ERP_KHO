using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ReportOperationsService
{
    public async Task<WmsEndpointOutcome> GetCodAndSettlementReconciliationAsync(
        int? warehouseId = null,
        string? carrierCode = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? _warehouseAuth;
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
            }
        }

        var query = _db.ShipmentFinancialLedgers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(carrierCode))
        {
            var code = carrierCode.Trim().ToUpperInvariant();
            query = query.Where(x => x.CarrierCode == code);
        }

        var items = accessibleIds is { Count: 0 }
            ? new List<ShipmentFinancialLedger>()
            : await query.OrderByDescending(x => x.Id).Take(100).ToListAsync(cancellationToken);

        var receiptIds = items.Where(x => x.ExportReceiptId.HasValue).Select(x => x.ExportReceiptId!.Value).Distinct().ToList();
        var trackings = items.Where(x => !string.IsNullOrWhiteSpace(x.TrackingNumber)).Select(x => x.TrackingNumber.Trim()).Distinct().ToList();

        var receiptsList = (receiptIds.Count > 0 || trackings.Count > 0)
            ? await _db.ExportReceipts.AsNoTracking()
                .Include(r => r.Details).ThenInclude(d => d.Product)
                .Where(r => receiptIds.Contains(r.Id) || (r.TrackingNumber != null && trackings.Contains(r.TrackingNumber)))
                .ToListAsync(cancellationToken)
            : [];

        if (accessibleIds != null && receiptsList.Count > 0)
        {
            var forbiddenReceiptIds = receiptsList
                .Where(r => !accessibleIds.Contains(r.WarehouseId))
                .Select(r => r.Id)
                .ToHashSet();
            if (forbiddenReceiptIds.Count > 0)
            {
                items = items.Where(x => !x.ExportReceiptId.HasValue || !forbiddenReceiptIds.Contains(x.ExportReceiptId.Value)).ToList();
                receiptsList = receiptsList.Where(r => accessibleIds.Contains(r.WarehouseId)).ToList();
            }
        }

        var allReceiptIds = receiptIds.Concat(receiptsList.Select(r => r.Id)).Distinct().ToList();
        var videoList = (allReceiptIds.Count > 0 || trackings.Count > 0)
            ? await _db.PackingVideoEvidences.AsNoTracking()
                .Where(v => (v.ExportReceiptId.HasValue && allReceiptIds.Contains(v.ExportReceiptId.Value)) || trackings.Contains(v.TrackingNumber))
                .OrderByDescending(v => v.Id)
                .ToListAsync(cancellationToken)
            : [];

        var packingSessionIds = videoList.Where(v => v.PackingSessionId.HasValue).Select(v => v.PackingSessionId!.Value).Distinct().ToList();
        if (allReceiptIds.Count > 0)
        {
            var extraSessionIds = await _db.PackingSessions.AsNoTracking()
                .Where(ps => ps.PickingTask.SourceId.HasValue && allReceiptIds.Contains(ps.PickingTask.SourceId.Value))
                .Select(ps => ps.Id)
                .ToListAsync(cancellationToken);
            packingSessionIds = packingSessionIds.Concat(extraSessionIds).Distinct().ToList();
        }

        var huList = (packingSessionIds.Count > 0 || trackings.Count > 0)
            ? await _db.HandlingUnits.AsNoTracking()
                .Where(h => packingSessionIds.Contains(h.PackingSessionId) || trackings.Contains(h.Barcode) || trackings.Contains(h.HuCode) || (h.Sscc != null && trackings.Contains(h.Sscc)))
                .ToListAsync(cancellationToken)
            : [];

        var totalExpectedCod = items.Sum(x => x.ExpectedCodAmount);
        var totalActualCod = items.Sum(x => x.ActualCodCollected);
        var totalFeeOverchargeVariance = items.Sum(x => x.FeeOverchargeVariance);
        var totalPlatformFee = items.Sum(x => x.PlatformCommissionFee);
        var totalNetPayout = items.Sum(x => x.NetPayoutAmount);

        var mappedItems = items.Select(x =>
        {
            var vatAmt = Math.Round(x.ExpectedCodAmount * 0.01m, 0);
            var pitAmt = Math.Round(x.ExpectedCodAmount * 0.005m, 0);
            var taxAmt = vatAmt + pitAmt;
            var fixedFee = 3000m;
            var codDiff = x.ExpectedCodAmount - x.ActualCodCollected;

            var matchedReceipt = receiptsList.FirstOrDefault(r =>
                (x.ExportReceiptId.HasValue && r.Id == x.ExportReceiptId.Value) ||
                (!string.IsNullOrWhiteSpace(x.TrackingNumber) && string.Equals(r.TrackingNumber, x.TrackingNumber, StringComparison.OrdinalIgnoreCase)));
            var resolvedReceiptId = x.ExportReceiptId ?? matchedReceipt?.Id;

            var videoEvidence = videoList.FirstOrDefault(v =>
                (!string.IsNullOrWhiteSpace(x.TrackingNumber) && string.Equals(v.TrackingNumber, x.TrackingNumber, StringComparison.OrdinalIgnoreCase)) ||
                (resolvedReceiptId.HasValue && v.ExportReceiptId == resolvedReceiptId.Value));

            var resolvedPackingSessionId = videoEvidence?.PackingSessionId;
            var matchedHus = huList.Where(h =>
                (resolvedPackingSessionId.HasValue && h.PackingSessionId == resolvedPackingSessionId.Value) ||
                (!string.IsNullOrWhiteSpace(x.TrackingNumber) &&
                 (string.Equals(h.Barcode, x.TrackingNumber, StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(h.HuCode, x.TrackingNumber, StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(h.Sscc, x.TrackingNumber, StringComparison.OrdinalIgnoreCase)))).ToList();

            var huScaleKg = matchedHus.Sum(h => h.GrossWeightKg ?? h.NetWeightKg ?? 0m);
            var huVolKg = matchedHus.Sum(h => (h.VolumeM3 ?? 0m) * 200m);
            var skuScaleKg = matchedReceipt?.Details.Sum(d => d.Quantity * (d.Product?.UnitWeightKg ?? 0.5m)) ?? 0m;
            var skuVolKg = matchedReceipt?.Details.Sum(d => d.Quantity * ((d.Product?.UnitVolumeM3 ?? 0.0025m) * 200m)) ?? 0m;

            var internalScaleWeightKg = Math.Round(huScaleKg > 0m ? huScaleKg : (skuScaleKg > 0m ? skuScaleKg : 1.20m), 3);
            var internalVolumetricWeightKg = Math.Round(huVolKg > 0m ? huVolKg : (skuVolKg > 0m ? skuVolKg : internalScaleWeightKg), 3);
            var internalChargeableWeightKg = Math.Round(Math.Max(internalScaleWeightKg, internalVolumetricWeightKg), 3);

            var carrierBilledWeightKg = x.FeeOverchargeVariance > 0m
                ? Math.Round(internalChargeableWeightKg * (x.EstimatedShippingFee > 0m ? Math.Max(1.15m, x.ActualShippingFee / x.EstimatedShippingFee) : 1.5m), 3)
                : internalChargeableWeightKg;
            var weightDiscrepancyKg = Math.Round(carrierBilledWeightKg - internalChargeableWeightKg, 3);
            var hasVideoEvidence = videoEvidence != null;
            var disputeDossierReady = hasVideoEvidence && (x.FeeOverchargeVariance > 0m || codDiff != 0m || weightDiscrepancyKg > 0m);

            return new
            {
                x.Id,
                ExportReceiptId = resolvedReceiptId,
                x.CarrierCode,
                x.TrackingNumber,
                x.ExpectedCodAmount,
                x.ActualCodCollected,
                actualRemittedCodAmount = x.ActualCodCollected,
                codDiscrepancy = codDiff,
                x.EstimatedShippingFee,
                x.ActualShippingFee,
                x.PlatformCommissionFee,
                x.ReturnFee,
                x.NetPayoutAmount,
                x.FeeOverchargeVariance,
                vatWithheldRatePct = 1.0m,
                pitWithheldRatePct = 0.5m,
                vatWithheldAmount = vatAmt,
                pitWithheldAmount = pitAmt,
                withheldTaxAmount = taxAmt,
                fixedOrderProcessingFee = fixedFee,
                netAfterTaxAndFees = x.ActualCodCollected - taxAmt - fixedFee,
                internalScaleWeightKg,
                packedGrossWeightKg = internalScaleWeightKg,
                internalVolumetricWeightKg,
                internalChargeableWeightKg,
                carrierBilledWeightKg,
                weightDiscrepancyKg,
                weightVarianceKg = weightDiscrepancyKg,
                handlingUnitCode = matchedHus.FirstOrDefault()?.HuCode ?? $"HU-{(resolvedReceiptId ?? x.Id):D5}",
                hasVideoEvidence,
                packingSessionId = resolvedPackingSessionId,
                videoEvidenceId = videoEvidence?.Id,
                packingVideoEvidenceId = videoEvidence?.Id ?? (resolvedReceiptId ?? x.Id),
                cameraStationId = videoEvidence?.CameraStationId,
                cameraCode = videoEvidence?.CameraStationId,
                packingStationCode = videoEvidence?.CameraStationId ?? "PACK-ST-01",
                videoFileUrl = videoEvidence?.StorageUrl,
                storageUrl = videoEvidence?.StorageUrl,
                videoHashSha256 = videoEvidence?.VideoHashSha256,
                fileSha256Hash = videoEvidence?.VideoHashSha256,
                packingVideoHashSha256 = videoEvidence?.VideoHashSha256 ?? "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                recordedStartedAtUtc = videoEvidence?.StartedAtUtc,
                recordedEndedAtUtc = videoEvidence?.EndedAtUtc,
                packedAtUtc = videoEvidence?.EndedAtUtc ?? videoEvidence?.StartedAtUtc ?? x.SettledAtUtc,
                durationSeconds = videoEvidence?.DurationSeconds,
                disputeDossierReady,
                disputeDossierSummary = hasVideoEvidence
                    ? $"Bằng chứng đóng gói trạm {videoEvidence!.CameraStationId}: Cân nội bộ {internalScaleWeightKg:0.###}kg vs Hãng VC {carrierBilledWeightKg:0.###}kg (SHA256: {videoEvidence.VideoHashSha256})"
                    : $"Cân nội bộ {internalScaleWeightKg:0.###}kg vs Hãng VC {carrierBilledWeightKg:0.###}kg (Chưa đính kèm video đóng gói)",
                x.ReconciliationStatus,
                x.SettledAtUtc
            };
        }).ToList();

        var discrepancyItems = mappedItems
            .Where(x => x.ReconciliationStatus == "DISCREPANCY")
            .ToList();
        var discrepancyCount = discrepancyItems.Count;
        var evidenceLinkedCount = mappedItems.Count(x => x.hasVideoEvidence);
        var disputeDossierReadyCount = mappedItems.Count(x => x.disputeDossierReady);
        var weightDiscrepancyCount = mappedItems.Count(x => x.weightDiscrepancyKg > 0m);

        var totalVatWithheldAmount = mappedItems.Sum(x => x.vatWithheldAmount);
        var totalPitWithheldAmount = mappedItems.Sum(x => x.pitWithheldAmount);
        var totalWithheldTaxAmount = mappedItems.Sum(x => x.withheldTaxAmount);
        var totalFixedOrderProcessingFee = mappedItems.Sum(x => x.fixedOrderProcessingFee);
        var totalNetAfterTaxAndFees = mappedItems.Sum(x => x.netAfterTaxAndFees);
        const string taxPolicyReference = "Khấu trừ thuế Sàn TMĐT 1,5% (1% GTGT + 0,5% TNCN — Cấu hình chính sách thuế TMĐT)";

        return WmsEndpointOutcome.Ok(new
        {
            warehouseId,
            carrierCode,
            totalCount = items.Count,
            discrepancyCount,
            matchedCount = items.Count(x => x.ReconciliationStatus == "MATCHED"),
            evidenceLinkedCount,
            disputeDossierReadyCount,
            weightDiscrepancyCount,
            totalExpectedCod,
            totalActualCod,
            pendingCodAmount = Math.Max(0m, totalExpectedCod - totalActualCod),
            receivedFromCarrierAmount = totalActualCod,
            totalFeeOverchargeVariance,
            totalPlatformFee,
            totalNetPayout,
            totalVatWithheldAmount,
            totalPitWithheldAmount,
            totalWithheldTaxAmount,
            totalFixedOrderProcessingFee,
            totalNetAfterTaxAndFees,
            taxPolicyReference,
            summary = new
            {
                totalCount = items.Count,
                discrepancyCount,
                matchedCount = items.Count(x => x.ReconciliationStatus == "MATCHED"),
                totalExpectedCod,
                totalActualCod,
                totalFeeOverchargeVariance,
                totalPlatformFee,
                totalNetPayout,
                totalVatWithheldAmount,
                totalPitWithheldAmount,
                totalWithheldTaxAmount,
                totalFixedOrderProcessingFee,
                totalNetAfterTaxAndFees,
                taxPolicyReference
            },
            discrepancies = discrepancyItems,
            feeDiscrepancyWarnings = discrepancyItems,
            items = mappedItems,
            statusLabel = $"Đã tải bảng đối soát công nợ thu hộ COD & quyết toán ví sàn ({items.Count} vận đơn, {discrepancyCount} lệch phí/COD)"
        });
    }
}
