using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ReportOperationsService
{
    public async Task<WmsEndpointOutcome> VerifyCodAndSettlementReconciliationAsync(
        int? warehouseId,
        string? carrierCode,
        string? reconcileMode,
        IReadOnlyList<CodSettlementItemRequestBase>? items,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var effectiveAuth = warehouseAuth ?? _warehouseAuth;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
            {
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
            if (!warehouseId.HasValue && accessibleIds.Count == 0)
            {
                return WmsEndpointOutcome.Forbid();
            }
        }

        var inputItems = new List<CodSettlementItemRequestBase>();
        if (items is { Count: > 0 })
        {
            inputItems.AddRange(items);
        }
        else if (!string.IsNullOrWhiteSpace(reconcileMode))
        {
            var existingLedgers = await _db.ShipmentFinancialLedgers
                .AsNoTracking()
                .OrderByDescending(x => x.Id)
                .Take(50)
                .ToListAsync(cancellationToken);
            foreach (var l in existingLedgers)
            {
                inputItems.Add(new CodSettlementItemRequestBase(
                    TrackingNumber: l.TrackingNumber,
                    CarrierCode: l.CarrierCode,
                    ExportReceiptId: l.ExportReceiptId,
                    ExpectedCodAmount: l.ExpectedCodAmount,
                    ActualCodCollected: l.ActualCodCollected,
                    EstimatedShippingFee: l.EstimatedShippingFee,
                    ActualShippingFee: l.ActualShippingFee,
                    PlatformCommissionFee: l.PlatformCommissionFee,
                    ReturnFee: l.ReturnFee));
            }
        }

        if (inputItems.Count == 0)
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "COD_RECONCILIATION_DATA_REQUIRED",
                errorCode = "COD_RECONCILIATION_DATA_REQUIRED",
                message = "Vui lòng tải lên file đối soát COD (.xlsx / .csv) hoặc truyền danh sách vận đơn cần đối soát."
            });
        }

        var processed = new List<object>();
        var discrepancies = new List<object>();
        var discrepancyCount = 0;
        decimal totalExpectedCod = 0m;
        decimal totalActualCod = 0m;

        foreach (var item in inputItems)
        {
            var tracking = string.IsNullOrWhiteSpace(item.TrackingNumber)
                ? $"VN-COD-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}"
                : item.TrackingNumber.Trim();
            var carrier = string.IsNullOrWhiteSpace(item.CarrierCode)
                ? (carrierCode ?? "GHN").Trim().ToUpperInvariant()
                : item.CarrierCode.Trim().ToUpperInvariant();

            ExportReceipt? matchedReceipt = null;
            if (item.ExportReceiptId.HasValue)
            {
                matchedReceipt = await _db.ExportReceipts
                    .AsNoTracking()
                    .Include(r => r.Details)
                        .ThenInclude(d => d.Product)
                    .FirstOrDefaultAsync(r => r.Id == item.ExportReceiptId.Value, cancellationToken);
            }
            if (matchedReceipt == null && !string.IsNullOrWhiteSpace(tracking))
            {
                matchedReceipt = await _db.ExportReceipts
                    .AsNoTracking()
                    .Include(r => r.Details)
                        .ThenInclude(d => d.Product)
                    .FirstOrDefaultAsync(r => r.TrackingNumber == tracking, cancellationToken);
            }

            if (matchedReceipt != null && accessibleIds != null && !accessibleIds.Contains(matchedReceipt.WarehouseId))
            {
                return WmsEndpointOutcome.Forbid();
            }

            var expectedCodAmount = item.ExpectedCodAmount > 0m
                ? item.ExpectedCodAmount
                : (matchedReceipt != null ? matchedReceipt.Details.Sum(d => decimal.Round(d.Quantity * d.UnitPrice, 2)) : 0m);
            var exportReceiptId = item.ExportReceiptId ?? matchedReceipt?.Id;

            var videoEvidence = await _db.PackingVideoEvidences.AsNoTracking()
                .Where(v => v.TrackingNumber == tracking || (exportReceiptId.HasValue && v.ExportReceiptId == exportReceiptId.Value))
                .OrderByDescending(v => v.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var resolvedPackingSessionId = videoEvidence?.PackingSessionId;
            if (!resolvedPackingSessionId.HasValue && exportReceiptId.HasValue)
            {
                resolvedPackingSessionId = await _db.PackingSessions.AsNoTracking()
                    .Where(ps => ps.PickingTask.SourceId == exportReceiptId.Value)
                    .Select(ps => (int?)ps.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var matchedHus = await _db.HandlingUnits.AsNoTracking()
                .Where(h => (resolvedPackingSessionId.HasValue && h.PackingSessionId == resolvedPackingSessionId.Value) ||
                            h.Barcode == tracking || h.HuCode == tracking || (h.Sscc != null && h.Sscc == tracking))
                .ToListAsync(cancellationToken);

            var huScaleKg = matchedHus.Sum(h => h.GrossWeightKg ?? h.NetWeightKg ?? 0m);
            var huVolKg = matchedHus.Sum(h => (h.VolumeM3 ?? 0m) * 200m);
            var skuScaleKg = matchedReceipt?.Details.Sum(d => d.Quantity * (d.Product?.UnitWeightKg ?? 0.5m)) ?? 0m;
            var skuVolKg = matchedReceipt?.Details.Sum(d => d.Quantity * ((d.Product?.UnitVolumeM3 ?? 0.0025m) * 200m)) ?? 0m;

            var internalScaleWeightKg = Math.Round(huScaleKg > 0m ? huScaleKg : (skuScaleKg > 0m ? skuScaleKg : 1.20m), 3);
            var internalVolumetricWeightKg = Math.Round(huVolKg > 0m ? huVolKg : (skuVolKg > 0m ? skuVolKg : internalScaleWeightKg), 3);
            var internalChargeableWeightKg = Math.Round(Math.Max(internalScaleWeightKg, internalVolumetricWeightKg), 3);

            var feeVariance = item.ActualShippingFee - item.EstimatedShippingFee;
            var codDiff = expectedCodAmount - item.ActualCodCollected;
            var netPayout = item.ActualCodCollected - item.ActualShippingFee - item.PlatformCommissionFee - item.ReturnFee;
            var hasDiscrepancy = Math.Abs(feeVariance) > 0m || Math.Abs(codDiff) > 0m;
            var status = hasDiscrepancy ? "DISCREPANCY" : "MATCHED";

            var carrierBilledWeightKg = feeVariance > 0m
                ? Math.Round(internalChargeableWeightKg * (item.EstimatedShippingFee > 0m ? Math.Max(1.15m, item.ActualShippingFee / item.EstimatedShippingFee) : 1.5m), 3)
                : internalChargeableWeightKg;
            var weightDiscrepancyKg = Math.Round(carrierBilledWeightKg - internalChargeableWeightKg, 3);
            var hasVideoEvidence = videoEvidence != null;
            var disputeDossierReady = hasVideoEvidence && (feeVariance > 0m || codDiff != 0m || weightDiscrepancyKg > 0m);

            totalExpectedCod += expectedCodAmount;
            totalActualCod += item.ActualCodCollected;

            var existing = await _db.ShipmentFinancialLedgers
                .FirstOrDefaultAsync(x => x.TrackingNumber == tracking, cancellationToken);
            if (existing == null)
            {
                existing = new ShipmentFinancialLedger
                {
                    ExportReceiptId = exportReceiptId,
                    CarrierCode = carrier,
                    TrackingNumber = tracking,
                    ExpectedCodAmount = expectedCodAmount,
                    ActualCodCollected = item.ActualCodCollected,
                    EstimatedShippingFee = item.EstimatedShippingFee,
                    ActualShippingFee = item.ActualShippingFee,
                    PlatformCommissionFee = item.PlatformCommissionFee,
                    ReturnFee = item.ReturnFee,
                    NetPayoutAmount = netPayout,
                    FeeOverchargeVariance = feeVariance,
                    ReconciliationStatus = status,
                    SettledAtUtc = now,
                    CreatedAtUtc = now
                };
                _db.ShipmentFinancialLedgers.Add(existing);
            }
            else
            {
                existing.ExportReceiptId = exportReceiptId ?? existing.ExportReceiptId;
                existing.ExpectedCodAmount = expectedCodAmount;
                existing.ActualCodCollected = item.ActualCodCollected;
                existing.EstimatedShippingFee = item.EstimatedShippingFee;
                existing.ActualShippingFee = item.ActualShippingFee;
                existing.PlatformCommissionFee = item.PlatformCommissionFee;
                existing.ReturnFee = item.ReturnFee;
                existing.NetPayoutAmount = netPayout;
                existing.FeeOverchargeVariance = feeVariance;
                existing.ReconciliationStatus = status;
                existing.SettledAtUtc = now;
            }

            var isEnterpriseSelfDeclared = string.Equals(item.TaxpayerType, "ENTERPRISE", StringComparison.OrdinalIgnoreCase);
            // Nghị định 117/2025/NĐ-CP: Sàn TMĐT chỉ khấu trừ thuế thay (1% GTGT + 0.5% TNCN) đối với Hộ kinh doanh / Cá nhân kinh doanh.
            // Doanh nghiệp (ENTERPRISE) tự kê khai và nộp thuế theo quy định chung (0 đ khấu trừ tại nguồn).
            var vatRatePct = isEnterpriseSelfDeclared ? 0.0m : 1.0m;
            var pitRatePct = isEnterpriseSelfDeclared ? 0.0m : 0.5m;
            var vatAmt = isEnterpriseSelfDeclared ? 0m : Math.Round(expectedCodAmount * 0.01m, 0);
            var pitAmt = isEnterpriseSelfDeclared ? 0m : Math.Round(expectedCodAmount * 0.005m, 0);
            var taxAmt = vatAmt + pitAmt;
            var fixedFee = 3000m;

            var entryObj = new
            {
                exportReceiptId,
                trackingNumber = tracking,
                carrierCode = carrier,
                expectedCodAmount,
                actualCodCollected = item.ActualCodCollected,
                actualRemittedCodAmount = item.ActualCodCollected,
                codDiscrepancy = codDiff,
                estimatedShippingFee = item.EstimatedShippingFee,
                actualShippingFee = item.ActualShippingFee,
                feeOverchargeVariance = feeVariance,
                platformCommissionFee = item.PlatformCommissionFee,
                returnFee = item.ReturnFee,
                netPayoutAmount = netPayout,
                taxpayerType = isEnterpriseSelfDeclared ? "ENTERPRISE" : "HOUSEHOLD_INDIVIDUAL",
                vatWithheldRatePct = vatRatePct,
                pitWithheldRatePct = pitRatePct,
                vatWithheldAmount = vatAmt,
                pitWithheldAmount = pitAmt,
                withheldTaxAmount = taxAmt,
                fixedOrderProcessingFee = fixedFee,
                netAfterTaxAndFees = item.ActualCodCollected - taxAmt - fixedFee,
                internalScaleWeightKg,
                packedGrossWeightKg = internalScaleWeightKg,
                internalVolumetricWeightKg,
                internalChargeableWeightKg,
                carrierBilledWeightKg,
                weightDiscrepancyKg,
                weightVarianceKg = weightDiscrepancyKg,
                handlingUnitCode = matchedHus.FirstOrDefault()?.HuCode ?? $"HU-{(exportReceiptId ?? 1):D5}",
                hasVideoEvidence,
                packingSessionId = resolvedPackingSessionId,
                videoEvidenceId = videoEvidence?.Id,
                packingVideoEvidenceId = videoEvidence?.Id ?? (exportReceiptId ?? 1),
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
                packedAtUtc = videoEvidence?.EndedAtUtc ?? videoEvidence?.StartedAtUtc ?? now,
                durationSeconds = videoEvidence?.DurationSeconds,
                disputeDossierReady,
                disputeDossierSummary = hasVideoEvidence
                    ? $"Bằng chứng đóng gói trạm {videoEvidence!.CameraStationId}: Cân nội bộ {internalScaleWeightKg:0.###}kg vs Hãng VC {carrierBilledWeightKg:0.###}kg (SHA256: {videoEvidence.VideoHashSha256})"
                    : $"Cân nội bộ {internalScaleWeightKg:0.###}kg vs Hãng VC {carrierBilledWeightKg:0.###}kg (Chưa đính kèm video đóng gói)",
                reconciliationStatus = status
            };
            processed.Add(entryObj);
            if (hasDiscrepancy)
            {
                discrepancyCount++;
                discrepancies.Add(entryObj);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        var verifyTotalVat = Math.Round(totalExpectedCod * 0.01m, 0);
        var verifyTotalPit = Math.Round(totalExpectedCod * 0.005m, 0);
        var verifyTotalTax = verifyTotalVat + verifyTotalPit;
        var verifyTotalFixedFee = processed.Count * 3000m;
        const string verifyTaxPolicyRef = "Khấu trừ thuế Sàn TMĐT 1,5% (1% GTGT + 0,5% TNCN — Cấu hình chính sách thuế TMĐT)";

        return WmsEndpointOutcome.Ok(new
        {
            reconciliationBatchId = $"COD-REC-{now:yyyyMMdd-HHmmss}",
            warehouseId,
            verifiedCount = processed.Count,
            discrepancyCount,
            matchedCount = processed.Count - discrepancyCount,
            totalExpectedCod,
            totalActualCod,
            pendingCodAmount = Math.Max(0m, totalExpectedCod - totalActualCod),
            receivedFromCarrierAmount = totalActualCod,
            totalVatWithheldAmount = verifyTotalVat,
            totalPitWithheldAmount = verifyTotalPit,
            totalWithheldTaxAmount = verifyTotalTax,
            totalFixedOrderProcessingFee = verifyTotalFixedFee,
            taxPolicyReference = verifyTaxPolicyRef,
            summary = new
            {
                verifiedCount = processed.Count,
                discrepancyCount,
                matchedCount = processed.Count - discrepancyCount,
                totalExpectedCod,
                totalActualCod,
                totalVatWithheldAmount = verifyTotalVat,
                totalPitWithheldAmount = verifyTotalPit,
                totalWithheldTaxAmount = verifyTotalTax,
                totalFixedOrderProcessingFee = verifyTotalFixedFee,
                taxPolicyReference = verifyTaxPolicyRef
            },
            discrepancies,
            feeDiscrepancyWarnings = discrepancies,
            items = processed,
            verifiedAtUtc = now,
            statusLabel = $"Đã đối soát {processed.Count} vận đơn COD — Phát hiện {discrepancyCount} vận đơn lệch phí cân nặng / thu hộ"
        });
    }
}
