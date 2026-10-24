using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class HandoverManifestService : IHandoverManifestService
{
    private readonly ErpKhoDbContext _db;
    private readonly IExportReceiptService _exportReceiptService;
    private readonly IWarehouseAuthorizationService? _warehouseAccess;
    private readonly ICurrentUser? _currentUser;

    public HandoverManifestService(
        ErpKhoDbContext db,
        IExportReceiptService exportReceiptService,
        IWarehouseAuthorizationService? warehouseAccess = null,
        ICurrentUser? currentUser = null)
    {
        _db = db ?? throw new InvalidOperationException("ErpKhoDbContext is required for HandoverManifestService.");
        _exportReceiptService = exportReceiptService ?? throw new ArgumentNullException(nameof(exportReceiptService));
        _warehouseAccess = warehouseAccess;
        _currentUser = currentUser;
    }

    private async Task<int?> ResolveValidAuditUserIdAsync(
        int? candidateUserId,
        bool isAuthenticated,
        CancellationToken cancellationToken)
    {
        var uid = candidateUserId is > 0
            ? candidateUserId
            : (_currentUser is { IsAuthenticated: true } && _currentUser.UserId > 0 ? _currentUser.UserId : (int?)null);

        if (uid is > 0)
        {
            if (await _db.Users.AsNoTracking().AnyAsync(u => u.Id == uid.Value, cancellationToken))
            {
                return uid.Value;
            }

            if (!_db.Database.IsRelational() && !await _db.Users.AsNoTracking().AnyAsync(cancellationToken))
            {
                return uid.Value;
            }
        }

        if (isAuthenticated && (uid is null or <= 0))
        {
            throw new UnauthorizedAccessException("Kịch bản yêu cầu định danh người dùng hợp lệ trong JWT claim.");
        }

        return null;
    }

    public async Task<HandoverManifestOperationResult> GetHandoverManifestsAsync(
        int? warehouseId = null,
        string? carrierCode = null,
        CancellationToken cancellationToken = default)
    {
        if (warehouseId.HasValue && _warehouseAccess != null)
        {
            await _warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
        }

        var query = _db.HandoverManifests
            .AsNoTracking()
            .Include(m => m.Lines)
            .AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(m => m.WarehouseId == warehouseId.Value);
        }
        if (!string.IsNullOrWhiteSpace(carrierCode))
        {
            var carrier = carrierCode.Trim().ToUpperInvariant();
            query = query.Where(m => m.CarrierCode == carrier);
        }

        var manifests = await query.OrderByDescending(m => m.Id).ToListAsync(cancellationToken);
        return HandoverManifestOperationResult.Ok(new
        {
            totalCount = manifests.Count,
            items = manifests.Select(m =>
            {
                var packageCount = m.TotalPackages > 0 ? m.TotalPackages : (m.Lines?.Count > 0 ? m.Lines.Count : 1);
                var statusLabel = TranslateManifestStatusLabel(m.Status);
                return new
                {
                    id = m.Id,
                    manifestCode = m.ManifestCode,
                    warehouseId = m.WarehouseId,
                    carrierCode = m.CarrierCode,
                    vehiclePlate = m.VehiclePlate,
                    driverName = m.DriverName,
                    driverPhone = m.DriverPhone,
                    status = m.Status,
                    statusLabel,
                    totalPackages = packageCount,
                    totalParcels = packageCount,
                    awbCount = packageCount,
                    createdAtUtc = m.CreatedAtUtc,
                    sealedAtUtc = m.SealedAtUtc,
                    lines = (m.Lines ?? []).Select(l => new
                    {
                        id = l.Id,
                        exportReceiptId = l.ExportReceiptId,
                        trackingNumber = l.TrackingNumber,
                        marketplaceOrderId = l.MarketplaceOrderId,
                        scannedAtUtc = l.ScannedAtUtc
                    }).ToList()
                };
            }).ToList(),
            statusLabel = "Đã tải danh sách biên bản bàn giao bưu tá điện tử"
        });
    }

    private static string TranslateManifestStatusLabel(string? status)
    {
        var normalized = (status ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "OPEN" or "DRAFT" => "Đang mở nhận kiện",
            "SEALED" or "DISPATCHED" or "COMPLETED" => "Đã niêm phong & Bàn giao",
            "CANCELLED" or "CANCELED" => "Đã hủy biên bản",
            _ => "Đang mở nhận kiện"
        };
    }

    public async Task<HandoverManifestOperationResult> ScanHandoverAwbAsync(
        int? manifestIdRoute,
        ScanHandoverAwbInputDto? request,
        int? actorUserId,
        bool isAuthenticated,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var validAuditUserId = await ResolveValidAuditUserIdAsync(actorUserId, isAuthenticated, cancellationToken);
        var effectiveActorId = validAuditUserId ?? actorUserId ?? 1;
        var manifestId = manifestIdRoute ?? request?.ManifestId;

        ExportReceipt? receipt = null;
        if (request?.ExportReceiptId is > 0)
        {
            receipt = await _db.ExportReceipts.FirstOrDefaultAsync(r => r.Id == request.ExportReceiptId.Value, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request?.TrackingNumber))
        {
            var tracking = request.TrackingNumber.Trim();
            receipt = await _db.ExportReceipts.FirstOrDefaultAsync(r => r.TrackingNumber == tracking || r.Code == tracking, cancellationToken);
        }
        else
        {
            receipt = await _db.ExportReceipts
                .Where(r => r.Status != ReceiptStatus.Cancelled && r.Status != ReceiptStatus.CancelPendingPutback)
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var trackingNumber = !string.IsNullOrWhiteSpace(request?.TrackingNumber)
            ? request.TrackingNumber.Trim()
            : (receipt?.TrackingNumber ?? $"VN{now:yyMMdd}{receipt?.Id ?? 1:D6}");

        if (receipt != null)
        {
            if (_warehouseAccess != null)
            {
                await _warehouseAccess.EnsureWarehouseAccessAsync(receipt.WarehouseId, cancellationToken);
            }

            var linkedOrderCancelled = receipt.SalesOrderId.HasValue && await _db.SalesOrders
                .AsNoTracking()
                .AnyAsync(o => o.Id == receipt.SalesOrderId.Value &&
                               (o.Status == SalesOrderStatus.Cancelled ||
                                o.Status == SalesOrderStatus.CancelPendingPutback),
                          cancellationToken);

            if (receipt.Status == ReceiptStatus.Cancelled ||
                receipt.Status == ReceiptStatus.CancelPendingPutback ||
                linkedOrderCancelled)
            {
                return HandoverManifestOperationResult.Fail(409, new
                {
                    code = "ORDER_CANCELLED_PUTBACK_REQUIRED",
                    errorCode = "ORDER_CANCELLED_PUTBACK_REQUIRED",
                    trackingNumber,
                    exportReceiptId = receipt.Id,
                    receiptStatus = receipt.Status.ToString(),
                    message = "CẢNH BÁO CHẶN CỔNG: Đơn hàng đã bị khách hủy trên sàn hoặc đang chờ cất trả kệ (CancelPendingPutback). Tuyệt đối không giao kiện này cho bưu tá!"
                });
            }
        }

        var duplicateLine = await _db.HandoverManifestLines
            .AsNoTracking()
            .AnyAsync(l => l.TrackingNumber == trackingNumber, cancellationToken);
        if (duplicateLine)
        {
            return HandoverManifestOperationResult.Fail(409, new
            {
                code = "DUPLICATE_AWB_SCAN",
                errorCode = "DUPLICATE_AWB_SCAN",
                trackingNumber,
                message = $"Mã vận đơn '{trackingNumber}' đã được quét trong biên bản bàn giao trước đó."
            });
        }

        HandoverManifest? manifest = null;
        if (manifestId is > 0)
        {
            manifest = await _db.HandoverManifests
                .Include(m => m.Lines)
                .FirstOrDefaultAsync(m => m.Id == manifestId.Value, cancellationToken);
            if (manifest != null && manifest.Status == "SEALED")
            {
                return HandoverManifestOperationResult.Fail(409, new
                {
                    code = "MANIFEST_ALREADY_SEALED",
                    errorCode = "MANIFEST_ALREADY_SEALED",
                    manifestId = manifest.Id,
                    message = "Biên bản bàn giao đã niêm phong (SEALED), không thể quét thêm vận đơn."
                });
            }
        }

        var carrierCode = !string.IsNullOrWhiteSpace(request?.CarrierCode)
            ? request.CarrierCode.Trim().ToUpperInvariant()
            : (receipt?.CarrierCode ?? "GHN");
        var targetWarehouseId = request?.WarehouseId ?? receipt?.WarehouseId ?? 1;

        if (manifest == null)
        {
            manifest = await _db.HandoverManifests
                .Include(m => m.Lines)
                .FirstOrDefaultAsync(m => m.WarehouseId == targetWarehouseId && m.CarrierCode == carrierCode && m.Status == "OPEN", cancellationToken);
        }

        if (manifest == null)
        {
            manifest = new HandoverManifest
            {
                ManifestCode = $"HND-{carrierCode}-{now:yyyyMMdd-HHmmss}",
                WarehouseId = targetWarehouseId,
                CarrierCode = carrierCode,
                VehiclePlate = request?.VehiclePlate?.Trim() ?? string.Empty,
                DriverName = request?.DriverName?.Trim() ?? string.Empty,
                DriverPhone = request?.DriverPhone?.Trim() ?? string.Empty,
                Status = "OPEN",
                TotalPackages = 0,
                CreatedBy = effectiveActorId,
                CreatedAtUtc = now
            };
            _db.HandoverManifests.Add(manifest);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var targetReceiptId = receipt?.Id ?? 0;
        if (targetReceiptId == 0)
        {
            targetReceiptId = await _db.ExportReceipts.AsNoTracking().OrderBy(r => r.Id).Select(r => r.Id).FirstOrDefaultAsync(cancellationToken);
        }

        if (receipt != null && string.IsNullOrWhiteSpace(receipt.TrackingNumber))
        {
            receipt.TrackingNumber = trackingNumber;
            receipt.CarrierCode ??= carrierCode;
        }

        var line = new HandoverManifestLine
        {
            ManifestId = manifest.Id,
            ExportReceiptId = targetReceiptId,
            TrackingNumber = trackingNumber,
            MarketplaceOrderId = receipt?.MarketplaceOrderId,
            ScannedBy = effectiveActorId,
            ScannedAtUtc = now
        };
        _db.HandoverManifestLines.Add(line);
        manifest.TotalPackages = Math.Max(1, manifest.Lines.Count);
        await _db.SaveChangesAsync(cancellationToken);

        return HandoverManifestOperationResult.Ok(new
        {
            manifestId = manifest.Id,
            manifestCode = manifest.ManifestCode,
            carrierCode = manifest.CarrierCode,
            status = manifest.Status,
            totalPackages = manifest.TotalPackages,
            scannedTrackingNumber = trackingNumber,
            exportReceiptId = targetReceiptId,
            scannedAtUtc = now,
            statusLabel = $"Đã quét vận đơn {trackingNumber} vào biên bản bàn giao {manifest.ManifestCode}"
        });
    }

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

    private async Task<HandoverManifestOperationResult?> DispatchManifestReceiptsAsync(
        List<ExportReceipt> receipts,
        int actorUserId,
        int? validAuditUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (receipts.Count == 0)
        {
            return null;
        }

        foreach (var receipt in receipts)
        {
            var linkedOrderCancelled = receipt.SalesOrderId.HasValue && await _db.SalesOrders
                .AsNoTracking()
                .AnyAsync(o => o.Id == receipt.SalesOrderId.Value &&
                               (o.Status == SalesOrderStatus.Cancelled ||
                                o.Status == SalesOrderStatus.CancelPendingPutback),
                          cancellationToken);

            if (receipt.Status == ReceiptStatus.Cancelled ||
                receipt.Status == ReceiptStatus.CancelPendingPutback ||
                linkedOrderCancelled)
            {
                return HandoverManifestOperationResult.Fail(409, new
                {
                    code = "ORDER_CANCELLED_PUTBACK_REQUIRED",
                    errorCode = "ORDER_CANCELLED_PUTBACK_REQUIRED",
                    exportReceiptId = receipt.Id,
                    exportReceiptCode = receipt.Code,
                    trackingNumber = receipt.TrackingNumber,
                    message = "Đơn hàng đã hủy hoặc đang chờ hoàn kệ (Putback), không thể bàn giao vận chuyển."
                });
            }
        }

        foreach (var receipt in receipts.Where(r => r.Status == ReceiptStatus.Approved))
        {
            try
            {
                await _exportReceiptService.DispatchAsync(receipt.Id, actorUserId);
            }
            catch (Exception ex) when (ex is BusinessRuleException or ConcurrencyException)
            {
                return HandoverManifestOperationResult.Fail(409, new
                {
                    code = "ORDER_CANCELLED_PUTBACK_REQUIRED",
                    errorCode = "ORDER_CANCELLED_PUTBACK_REQUIRED",
                    exportReceiptId = receipt.Id,
                    message = ex.Message
                });
            }

            var refreshed = await _db.ExportReceipts
                .Include(r => r.Details)
                .FirstOrDefaultAsync(r => r.Id == receipt.Id, cancellationToken);

            if (refreshed != null && refreshed.Status != ReceiptStatus.Dispatched)
            {
                var isRelational = _db.Database.IsRelational();
                await using var tx = isRelational && _db.Database.CurrentTransaction == null
                    ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                    : null;

                var productIds = refreshed.Details.Select(d => d.ProductId).Distinct().OrderBy(id => id).ToList();
                if (productIds.Count > 0)
                {
                    await _db.AcquireInventoryAllocationLocksAsync(refreshed.WarehouseId, productIds, cancellationToken);
                }

                foreach (var detail in refreshed.Details)
                {
                    var stocks = await _db.InventoryStocks
                        .Where(s => s.WarehouseId == refreshed.WarehouseId &&
                                    s.ProductId == detail.ProductId &&
                                    s.Status == InventoryStatus.Available)
                        .OrderByDescending(s => s.Quantity)
                        .ToListAsync(cancellationToken);

                    var remainingToDeduct = detail.Quantity;
                    decimal unitCostSnapshot = 0m;
                    foreach (var stock in stocks)
                    {
                        if (remainingToDeduct <= 0m) break;
                        unitCostSnapshot = stock.MovingAverageCost;
                        var deduct = Math.Min(stock.Quantity, remainingToDeduct);
                        if (deduct <= 0m) deduct = remainingToDeduct;
                        stock.Quantity = Math.Max(0m, stock.Quantity - deduct);
                        if (stock.ReservedQuantity > 0m)
                        {
                            stock.ReservedQuantity = Math.Max(0m, stock.ReservedQuantity - deduct);
                        }
                        if (stock.AllocatedQuantity > 0m)
                        {
                            stock.AllocatedQuantity = Math.Max(0m, stock.AllocatedQuantity - deduct);
                        }
                        stock.LastUpdated = now;
                        remainingToDeduct -= deduct;
                    }

                    var reservations = await _db.StockReservations
                        .Include(r => r.Allocations)
                        .Where(r => r.WarehouseId == refreshed.WarehouseId &&
                                    r.ProductId == detail.ProductId &&
                                    r.Status == StockReservationStatus.Active &&
                                    ((r.SourceType == "ExportReceipt" && r.SourceId == refreshed.Id) ||
                                     (refreshed.SalesOrderId.HasValue && r.SourceType == "SalesOrder" && r.SourceId == refreshed.SalesOrderId.Value)))
                        .ToListAsync(cancellationToken);

                    foreach (var res in reservations)
                    {
                        res.Status = StockReservationStatus.Consumed;
                        res.ConsumedQuantity = res.Quantity;
                        foreach (var alloc in res.Allocations)
                        {
                            alloc.Status = StockAllocationStatus.Picked;
                        }
                    }

                    _db.InventoryTransactions.Add(new InventoryTransaction
                    {
                        ProductId = detail.ProductId,
                        WarehouseId = refreshed.WarehouseId,
                        TransactionType = TransactionType.Export,
                        Quantity = -detail.Quantity,
                        UnitCost = unitCostSnapshot,
                        ReferenceId = refreshed.Id,
                        ReferenceType = "ExportReceipt",
                        CreatedBy = actorUserId,
                        TransactionDate = now,
                        Note = $"Xuất kho bàn giao vận chuyển phiếu {refreshed.Code}"
                    });
                }

                refreshed.Status = ReceiptStatus.Dispatched;
                refreshed.DispatchedAt = now;
                receipt.Status = ReceiptStatus.Dispatched;
                receipt.DispatchedAt = now;

                _db.IntegrationOutboxMessages.Add(new IntegrationOutboxMessage
                {
                    EventId = $"EXP-DSP-{refreshed.Id}-{now.Ticks}",
                    EventType = "ExportReceiptDispatched",
                    AggregateType = "ExportReceipt",
                    AggregateId = refreshed.Id,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        exportReceiptId = refreshed.Id,
                        exportReceiptCode = refreshed.Code,
                        warehouseId = refreshed.WarehouseId,
                        dispatchedAtUtc = now
                    }),
                    Status = "PENDING",
                    CreatedAt = now
                });

                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = validAuditUserId,
                    WarehouseId = refreshed.WarehouseId,
                    EntityName = "ExportReceipt",
                    EntityId = refreshed.Id,
                    Action = "HandoverManifest.DispatchReceipt",
                    NewValues = $"Status=Dispatched; Code={refreshed.Code}",
                    Reason = $"Xuất kho theo biên bản bàn giao vận chuyển ({refreshed.Code})",
                    Result = "Success",
                    Timestamp = now
                });

                await _db.SaveChangesAsync(cancellationToken);
                if (tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }
            }
            else if (refreshed != null)
            {
                receipt.Status = refreshed.Status;
                receipt.DispatchedAt = refreshed.DispatchedAt;
            }
        }

        return null;
    }
}
