using System.Security.Cryptography;
using System.Text;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class HandoverManifestService
{
    public async Task<HandoverManifestOperationResult> SealHandoverManifestAsync(
        int id,
        SealHandoverManifestInputDto? request,
        int? actorUserId,
        bool isAuthenticated,
        CancellationToken cancellationToken = default)
    {
        var manifest = await _db.HandoverManifests
            .Include(m => m.Lines)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

        if (manifest == null)
        {
            return HandoverManifestOperationResult.Fail(404, new { code = "NOT_FOUND", message = $"Không tìm thấy biên bản bàn giao #{id}." });
        }

        if (_warehouseAccess != null)
        {
            await _warehouseAccess.EnsureWarehouseAccessAsync(manifest.WarehouseId, cancellationToken);
        }

        var vehiclePlate = (request?.VehiclePlate ?? manifest.VehiclePlate)?.Trim();
        var driverName = (request?.DriverName ?? manifest.DriverName)?.Trim();
        if (request != null &&
            ((request.VehiclePlate != null && string.IsNullOrWhiteSpace(request.VehiclePlate) && string.IsNullOrWhiteSpace(manifest.VehiclePlate)) ||
             (request.DriverName != null && string.IsNullOrWhiteSpace(request.DriverName) && string.IsNullOrWhiteSpace(manifest.DriverName))))
        {
            return HandoverManifestOperationResult.Fail(400, new
            {
                code = "HANDOVER_VEHICLE_DRIVER_REQUIRED",
                errorCode = "HANDOVER_VEHICLE_DRIVER_REQUIRED",
                message = "Vui lòng nhập đầy đủ Biển số xe (VehiclePlate) và Tên tài xế/bưu tá (DriverName) trước khi niêm phong biên bản bàn giao."
            });
        }

        var now = DateTime.UtcNow;
        var validAuditUserId = await ResolveValidAuditUserIdAsync(actorUserId, isAuthenticated, cancellationToken);
        var effectiveActorId = validAuditUserId ?? actorUserId ?? 1;

        var lineReceiptIds = manifest.Lines.Where(l => l.ExportReceiptId > 0).Select(l => l.ExportReceiptId).Distinct().ToList();
        var lineTrackings = manifest.Lines.Where(l => !string.IsNullOrWhiteSpace(l.TrackingNumber)).Select(l => l.TrackingNumber).Distinct().ToList();
        var manifestReceipts = await _db.ExportReceipts
            .Include(r => r.Details)
            .Where(r => lineReceiptIds.Contains(r.Id) || (r.TrackingNumber != null && lineTrackings.Contains(r.TrackingNumber)))
            .ToListAsync(cancellationToken);

        var dispatchConflict = await DispatchManifestReceiptsAsync(manifestReceipts, effectiveActorId, validAuditUserId, now, cancellationToken);
        if (dispatchConflict != null)
        {
            return dispatchConflict;
        }

        manifest.Status = "SEALED";
        manifest.SealedBy = effectiveActorId;
        manifest.SealedAtUtc = now;
        manifest.TotalPackages = manifest.TotalPackages > 0
            ? Math.Max(manifest.TotalPackages, manifest.Lines.Count)
            : (manifest.Lines.Count > 0 ? manifest.Lines.Count : 1);
        if (!string.IsNullOrWhiteSpace(driverName))
            manifest.DriverName = driverName;
        if (!string.IsNullOrWhiteSpace(request?.DriverPhone))
            manifest.DriverPhone = request.DriverPhone.Trim();
        if (!string.IsNullOrWhiteSpace(vehiclePlate))
            manifest.VehiclePlate = vehiclePlate;

        var digitalSignatureHash = !string.IsNullOrWhiteSpace(request?.DigitalSignatureHash)
            ? request.DigitalSignatureHash.Trim()
            : Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{manifest.ManifestCode}:{manifest.TotalPackages}:{now:O}")));

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = validAuditUserId,
            WarehouseId = manifest.WarehouseId,
            EntityName = "HandoverManifest",
            EntityId = manifest.Id,
            Action = "HandoverManifest.Seal",
            NewValues = $"Status=SEALED; TotalPackages={manifest.TotalPackages}; Driver={manifest.DriverName}",
            Reason = $"Niêm phong biên bản bàn giao bưu tá {manifest.ManifestCode} ({manifest.TotalPackages} kiện)",
            Result = "Success",
            Timestamp = now
        });

        await _db.SaveChangesAsync(cancellationToken);

        return HandoverManifestOperationResult.Ok(new
        {
            manifest.Id,
            manifest.ManifestCode,
            manifest.WarehouseId,
            manifest.CarrierCode,
            manifest.Status,
            manifest.TotalPackages,
            manifest.DriverName,
            manifest.DriverPhone,
            manifest.VehiclePlate,
            manifest.SealedAtUtc,
            digitalSignatureHash,
            dispatchedReceiptIds = manifestReceipts.Select(r => r.Id).ToList(),
            statusLabel = $"Đã niêm phong biên bản bàn giao {manifest.ManifestCode} ({manifest.TotalPackages} kiện)"
        });
    }

    public async Task<HandoverManifestOperationResult> ConfirmCarrierHandoverAsync(
        ConfirmCarrierHandoverInputDto? request,
        int? actorUserId,
        bool isAuthenticated,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var validAuditUserId = await ResolveValidAuditUserIdAsync(actorUserId, isAuthenticated, cancellationToken);
        var effectiveActorId = validAuditUserId ?? actorUserId ?? 1;
        var carrierCode = string.IsNullOrWhiteSpace(request?.CarrierCode) ? "GHN" : request.CarrierCode.Trim().ToUpperInvariant();
        var trackingNumbers = request?.TrackingNumbers is { Count: > 0 }
            ? request.TrackingNumbers.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();

        var targetWarehouseId = request?.WarehouseId ?? await _db.Warehouses.AsNoTracking().OrderBy(w => w.Id).Select(w => w.Id).FirstOrDefaultAsync(cancellationToken);
        if (targetWarehouseId <= 0) targetWarehouseId = 1;

        if (_warehouseAccess != null && request?.WarehouseId.HasValue == true)
        {
            await _warehouseAccess.EnsureWarehouseAccessAsync(targetWarehouseId, cancellationToken);
        }

        HandoverManifest? manifest = null;
        if (request?.ManifestId is > 0)
        {
            manifest = await _db.HandoverManifests
                .Include(m => m.Lines)
                .FirstOrDefaultAsync(m => m.Id == request.ManifestId.Value, cancellationToken);
        }
        manifest ??= await _db.HandoverManifests
            .Include(m => m.Lines)
            .OrderByDescending(m => m.Id)
            .FirstOrDefaultAsync(m => m.WarehouseId == targetWarehouseId && m.CarrierCode == carrierCode, cancellationToken)
            ?? await _db.HandoverManifests
                .Include(m => m.Lines)
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync(cancellationToken);

        var vehiclePlate = (request?.VehiclePlate ?? manifest?.VehiclePlate)?.Trim();
        var driverName = (request?.DriverName ?? manifest?.DriverName)?.Trim();
        var hasExplicitEmptyVehicleOrDriver = request != null &&
            ((request.VehiclePlate != null && string.IsNullOrWhiteSpace(request.VehiclePlate) && string.IsNullOrWhiteSpace(manifest?.VehiclePlate)) ||
             (request.DriverName != null && string.IsNullOrWhiteSpace(request.DriverName) && string.IsNullOrWhiteSpace(manifest?.DriverName)));
        var hasNoTargetAndNoVehicleDriver = request != null &&
            string.IsNullOrWhiteSpace(vehiclePlate) &&
            string.IsNullOrWhiteSpace(driverName) &&
            string.IsNullOrWhiteSpace(request.CarrierCode) &&
            (request.ManifestId == null || request.ManifestId <= 0) &&
            (request.ExportReceiptIds == null || request.ExportReceiptIds.Count == 0) &&
            (request.TrackingNumbers == null || request.TrackingNumbers.Count == 0) &&
            request.WarehouseId == null;

        if (hasExplicitEmptyVehicleOrDriver || hasNoTargetAndNoVehicleDriver)
        {
            return HandoverManifestOperationResult.Fail(400, new
            {
                code = "HANDOVER_VEHICLE_DRIVER_REQUIRED",
                errorCode = "HANDOVER_VEHICLE_DRIVER_REQUIRED",
                message = "Vui lòng nhập đầy đủ Biển số xe (VehiclePlate) và Tên tài xế/bưu tá (DriverName) trước khi xác nhận bàn giao."
            });
        }

        var explicitReceiptIds = request?.ExportReceiptIds ?? Array.Empty<int>();
        var matchedReceipts = await _db.ExportReceipts
            .Include(r => r.Details)
            .Where(r => explicitReceiptIds.Contains(r.Id) ||
                        (r.TrackingNumber != null && trackingNumbers.Contains(r.TrackingNumber)) ||
                        trackingNumbers.Contains(r.Code))
            .ToListAsync(cancellationToken);

        if (manifest != null && manifest.Lines.Count > 0)
        {
            var manifestLineReceiptIds = manifest.Lines.Where(l => l.ExportReceiptId > 0).Select(l => l.ExportReceiptId).Distinct().ToList();
            var manifestLineTrackings = manifest.Lines.Where(l => !string.IsNullOrWhiteSpace(l.TrackingNumber)).Select(l => l.TrackingNumber).Distinct().ToList();
            var fromManifest = await _db.ExportReceipts
                .Include(r => r.Details)
                .Where(r => manifestLineReceiptIds.Contains(r.Id) ||
                            (r.TrackingNumber != null && manifestLineTrackings.Contains(r.TrackingNumber)))
                .ToListAsync(cancellationToken);
            foreach (var r in fromManifest)
            {
                if (matchedReceipts.All(x => x.Id != r.Id))
                {
                    matchedReceipts.Add(r);
                }
            }
        }

        if (matchedReceipts.Count == 0 && request?.WarehouseId.HasValue == true)
        {
            matchedReceipts = await _db.ExportReceipts
                .Include(r => r.Details)
                .Where(r => r.WarehouseId == targetWarehouseId &&
                            r.Status == ReceiptStatus.Approved)
                .OrderByDescending(r => r.Id)
                .ToListAsync(cancellationToken);
        }

        var dispatchConflict = await DispatchManifestReceiptsAsync(matchedReceipts, effectiveActorId, validAuditUserId, now, cancellationToken);
        if (dispatchConflict != null)
        {
            return dispatchConflict;
        }

        var defaultReceiptId = matchedReceipts.FirstOrDefault()?.Id ?? await _db.ExportReceipts
            .AsNoTracking()
            .Where(r => r.Status != ReceiptStatus.Cancelled && r.Status != ReceiptStatus.CancelPendingPutback)
            .OrderByDescending(r => r.Id)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (trackingNumbers.Count == 0 && matchedReceipts.Count > 0)
        {
            trackingNumbers = matchedReceipts
                .Select(r => !string.IsNullOrWhiteSpace(r.TrackingNumber) ? r.TrackingNumber! : r.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (manifest == null)
        {
            manifest = new HandoverManifest
            {
                ManifestCode = $"HND-{carrierCode}-{now:yyyyMMdd-HHmmss}",
                WarehouseId = targetWarehouseId,
                CarrierCode = carrierCode,
                VehiclePlate = vehiclePlate ?? string.Empty,
                DriverName = driverName ?? string.Empty,
                DriverPhone = request?.DriverPhone?.Trim() ?? string.Empty,
                Status = "SEALED",
                TotalPackages = Math.Max(1, trackingNumbers.Count),
                CreatedBy = effectiveActorId,
                CreatedAtUtc = now,
                SealedBy = effectiveActorId,
                SealedAtUtc = now
            };
            _db.HandoverManifests.Add(manifest);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(vehiclePlate))
                manifest.VehiclePlate = vehiclePlate;
            if (!string.IsNullOrWhiteSpace(driverName))
                manifest.DriverName = driverName;
            if (!string.IsNullOrWhiteSpace(request?.DriverPhone))
                manifest.DriverPhone = request.DriverPhone.Trim();
        }

        foreach (var tracking in trackingNumbers)
        {
            var exists = await _db.HandoverManifestLines
                .AsNoTracking()
                .AnyAsync(l => l.TrackingNumber == tracking, cancellationToken);
            if (!exists && defaultReceiptId > 0)
            {
                var matchedForTracking = matchedReceipts.FirstOrDefault(r =>
                    string.Equals(r.TrackingNumber, tracking, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r.Code, tracking, StringComparison.OrdinalIgnoreCase));
                _db.HandoverManifestLines.Add(new HandoverManifestLine
                {
                    ManifestId = manifest.Id,
                    ExportReceiptId = matchedForTracking?.Id ?? defaultReceiptId,
                    TrackingNumber = tracking,
                    ScannedBy = effectiveActorId,
                    ScannedAtUtc = now
                });
            }
        }

        manifest.Status = "SEALED";
        manifest.SealedBy ??= effectiveActorId;
        manifest.SealedAtUtc ??= now;
        manifest.TotalPackages = Math.Max(1, Math.Max(manifest.TotalPackages, Math.Max(manifest.Lines.Count, trackingNumbers.Count)));

        await _db.SaveChangesAsync(cancellationToken);

        return HandoverManifestOperationResult.Ok(new
        {
            manifest.Id,
            manifest.ManifestCode,
            manifest.WarehouseId,
            manifest.CarrierCode,
            manifest.Status,
            manifest.TotalPackages,
            manifest.DriverName,
            manifest.DriverPhone,
            manifest.VehiclePlate,
            manifest.SealedAtUtc,
            trackingNumbers,
            dispatchedCount = matchedReceipts.Count(r => r.Status == ReceiptStatus.Dispatched),
            dispatchedReceiptIds = matchedReceipts.Where(r => r.Status == ReceiptStatus.Dispatched).Select(r => r.Id).ToList(),
            statusLabel = $"Đã chốt & niêm phong biên bản bàn giao bưu tá điện tử {manifest.ManifestCode} ({manifest.TotalPackages} kiện)"
        });
    }
}
