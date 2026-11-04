using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class HandoverManifestService : IHandoverManifestService
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
            receipt = await _db.ExportReceipts.FirstOrDefaultAsync(
                r => r.TrackingNumber == tracking || r.Code == tracking || r.MarketplaceOrderId == tracking,
                cancellationToken);
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
        else if (!string.IsNullOrWhiteSpace(request?.TrackingNumber))
        {
            var cancelledOrder = await _db.SalesOrders
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    o => (o.OrderCode == trackingNumber || o.ExternalOrderId == trackingNumber) &&
                         (o.Status == SalesOrderStatus.Cancelled || o.Status == SalesOrderStatus.CancelPendingPutback),
                    cancellationToken);
            if (cancelledOrder != null)
            {
                return HandoverManifestOperationResult.Fail(409, new
                {
                    code = "ORDER_CANCELLED_PUTBACK_REQUIRED",
                    errorCode = "ORDER_CANCELLED_PUTBACK_REQUIRED",
                    trackingNumber,
                    salesOrderId = cancelledOrder.Id,
                    receiptStatus = cancelledOrder.Status.ToString(),
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
}
