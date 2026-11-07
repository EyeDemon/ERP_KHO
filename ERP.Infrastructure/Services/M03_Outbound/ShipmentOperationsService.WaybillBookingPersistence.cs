using ERP.Application.Common;
using ERP.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ShipmentOperationsService
{
    private async Task<WmsEndpointOutcome> PersistBookedWaybillTransactionAsync(
        ERP.Domain.Entities.Shipment? shipment,
        ERP.Domain.Entities.ExportReceipt? exportReceipt,
        int targetWarehouseId,
        ICarrierIntegrationAdapter adapter,
        CarrierWaybillBookingResult booking,
        string deterministicClientOrderCode,
        string effectiveShopId,
        string? maskedApiToken,
        string gatewayMode,
        string? expectedDeliveryTime,
        decimal codAmountVnd,
        int? actorUserId,
        CancellationToken cancellationToken)
    {
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
            shipment.SourceCode = booking.AwbTrackingCode;
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

        var rawEventId = $"AWB-{booking.CarrierCode}-{booking.AwbTrackingCode}-{Guid.NewGuid():N}";
        var outboxMsg = new ERP.Domain.Entities.IntegrationOutboxMessage
        {
            EventId = rawEventId.Length > 48 ? rawEventId[..48] : rawEventId,
            EventType = "CARRIER_BOOK_WAYBILL",
            AggregateType = shipment != null ? "Shipment" : "ExportReceipt",
            AggregateId = shipment?.Id ?? exportReceipt?.Id,
            PayloadJson = booking.ProviderPayloadJson,
            Status = "Pending",
            RetryCount = 0,
            CreatedAt = now
        };
        db.IntegrationOutboxMessages.Add(outboxMsg);

        var expectedCod = Math.Round(Math.Max(0m, codAmountVnd), 4);
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
            ledger.ReconciliationStatus = "PENDING";
        }

        db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
        {
            UserId = resolvedActorId,
            WarehouseId = targetWarehouseId,
            EntityName = shipment != null ? "Shipment" : "ExportReceipt",
            EntityId = shipment?.Id ?? exportReceipt?.Id,
            Action = "Shipment.BookCarrierWaybill",
            NewValues = $"Carrier={booking.CarrierCode}; AWB={booking.AwbTrackingCode}; ClientOrderCode={deterministicClientOrderCode}; Hub={booking.SortingHubCode}; Fee={booking.EstimatedShippingFeeVnd}; Mode={gatewayMode}",
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
            shopId = effectiveShopId,
            maskedApiToken,
            gatewayMode,
            awbTrackingCode = booking.AwbTrackingCode,
            orderCode = booking.AwbTrackingCode,
            trackingNumber = booking.AwbTrackingCode,
            clientOrderCode = deterministicClientOrderCode,
            sortingHubCode = booking.SortingHubCode,
            routeCode = booking.RouteCode,
            estimatedShippingFeeVnd = booking.EstimatedShippingFeeVnd,
            totalFee = booking.EstimatedShippingFeeVnd,
            expectedDeliveryTime,
            codAmountVnd = expectedCod,
            administrativeModel = booking.AdministrativeModel,
            normalizedAddress = booking.NormalizedAddress,
            isSandbox = booking.IsSandbox,
            outboxMessageId = outboxMsg.Id,
            outboxEventType = outboxMsg.EventType,
            financialLedgerId = ledger.Id,
            statusLabel = $"Đã tạo vận đơn điện tử {booking.AwbTrackingCode} qua {adapter.CarrierDisplayName} ({gatewayMode})"
        });
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
