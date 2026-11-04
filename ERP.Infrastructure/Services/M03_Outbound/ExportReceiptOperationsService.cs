using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ExportReceiptOperationsService(
    ErpKhoDbContext db,
    IWarehouseAuthorizationService? warehouseAccess = null,
    ICurrentUser? currentUser = null,
    IExportReceiptService? exportReceiptService = null,
    IHandoverManifestService? handoverManifestService = null) : IExportReceiptOperationsService
{
    private readonly ErpKhoDbContext _db = db;
    private readonly IWarehouseAuthorizationService? _warehouseAccess = warehouseAccess;
    private readonly ICurrentUser? _currentUser = currentUser;
    private readonly IExportReceiptService? _exportReceiptService = exportReceiptService;
    private readonly IHandoverManifestService? _handoverManifestService = handoverManifestService;

    private IHandoverManifestService ResolveHandoverManifests(
        IExportReceiptService? exportReceiptServiceOverride,
        IWarehouseAuthorizationService? warehouseAccessOverride,
        ICurrentUser? currentUserOverride)
        => _handoverManifestService ?? new HandoverManifestService(
            _db,
            (exportReceiptServiceOverride ?? _exportReceiptService)!,
            warehouseAccessOverride ?? _warehouseAccess,
            currentUserOverride ?? _currentUser);

    private async Task<int?> ResolveValidAuditUserIdAsync(
        int? actorUserId,
        bool isAuthenticated,
        ICurrentUser? currentUserOverride,
        CancellationToken cancellationToken)
    {
        var effectiveUser = currentUserOverride ?? _currentUser;
        var hasClaimUser = actorUserId is > 0;
        var uid = actorUserId ?? 0;
        if (!hasClaimUser && effectiveUser?.UserId is > 0)
        {
            uid = effectiveUser.UserId;
            hasClaimUser = true;
        }

        if (hasClaimUser && uid > 0)
        {
            if (await _db.Users.AsNoTracking().AnyAsync(u => u.Id == uid, cancellationToken))
            {
                return uid;
            }

            if (!_db.Database.IsRelational() && !await _db.Users.AsNoTracking().AnyAsync(cancellationToken))
            {
                return uid;
            }
        }

        if (isAuthenticated && !hasClaimUser)
        {
            throw new UnauthorizedAccessException("Không xác định được người dùng hợp lệ cho nhật ký kiểm toán.");
        }

        return null;
    }

    public async Task<WmsEndpointOutcome> GetPipelineSummaryAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveWhAccess != null)
        {
            if (warehouseId.HasValue)
                await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else if (_currentUser == null || (_currentUser.IsAuthenticated && !_currentUser.IsGlobalAdmin))
                accessibleIds = await effectiveWhAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var query = _db.ExportReceipts.AsNoTracking();
        if (warehouseId.HasValue)
            query = query.Where(r => r.WarehouseId == warehouseId.Value);
        else if (accessibleIds != null)
            query = query.Where(r => accessibleIds.Contains(r.WarehouseId));

        var grouped = await query
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var totalReceipts = grouped.Sum(x => x.Count);
        var draftCount = grouped
            .Where(x => x.Status == ReceiptStatus.Draft)
            .Sum(x => x.Count);
        var approvedReadyCount = grouped
            .Where(x => x.Status == ReceiptStatus.Approved)
            .Sum(x => x.Count);
        var dispatchedCount = grouped
            .Where(x => x.Status is ReceiptStatus.Dispatched or ReceiptStatus.Posted)
            .Sum(x => x.Count);
        var cancelledCount = grouped
            .Where(x => x.Status == ReceiptStatus.Cancelled)
            .Sum(x => x.Count);

        var now = DateTime.UtcNow;
        var twoHoursLater = now.AddHours(2);
        var todayStart = now.Date;
        var tomorrowStart = todayStart.AddDays(1);

        var activePendingQuery = query.Where(r =>
            r.Status != ReceiptStatus.Dispatched &&
            r.Status != ReceiptStatus.Posted &&
            r.Status != ReceiptStatus.Cancelled &&
            r.Status != ReceiptStatus.CancelPendingPutback &&
            r.MarketplaceShipByUtc.HasValue);

        var slaOverdueCount = await activePendingQuery.CountAsync(
            r => r.MarketplaceShipByUtc!.Value < now,
            cancellationToken);
        var slaAtRiskUnder2HoursCount = await activePendingQuery.CountAsync(
            r => r.MarketplaceShipByUtc!.Value >= now && r.MarketplaceShipByUtc.Value <= twoHoursLater,
            cancellationToken);
        var slaDueTodayCount = await activePendingQuery.CountAsync(
            r => r.MarketplaceShipByUtc!.Value > twoHoursLater &&
                 r.MarketplaceShipByUtc.Value >= todayStart &&
                 r.MarketplaceShipByUtc.Value < tomorrowStart,
            cancellationToken);
        var marketplacePrioritizedCount = await query.CountAsync(
            r => (r.MarketplaceOrderId != null && r.MarketplaceOrderId != "") ||
                 (r.TrackingNumber != null && r.TrackingNumber != ""),
            cancellationToken);

        var slaComplianceRatePct = totalReceipts == 0
            ? 100m
            : decimal.Round(((totalReceipts - slaOverdueCount) * 100m) / totalReceipts, 2);

        return WmsEndpointOutcome.Ok(new
        {
            totalReceipts,
            draftCount,
            approvedReadyCount,
            dispatchedCount,
            cancelledCount,
            slaOverdueCount,
            slaAtRiskUnder2HoursCount,
            slaDueTodayCount,
            slaComplianceRatePct,
            marketplacePrioritizedCount,
            status = "Đã tải tổng quan luồng phiếu xuất kho",
            statusLabel = "Đã tải tổng quan luồng phiếu xuất kho & đồng hồ SLA giao hàng sàn TMĐT"
        });
    }

    public async Task<WmsEndpointOutcome> GetSlaQueueAsync(
        int? warehouseId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<int>? accessibleIds = null;
        if (_warehouseAccess != null)
        {
            if (warehouseId.HasValue)
            {
                await _warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else if (_currentUser == null || (_currentUser.IsAuthenticated && !_currentUser.IsGlobalAdmin))
            {
                accessibleIds = await _warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
            }
        }

        var now = DateTime.UtcNow;
        var query = _db.ExportReceipts
            .AsNoTracking()
            .Where(r => r.Status != ReceiptStatus.Cancelled &&
                        r.Status != ReceiptStatus.CancelPendingPutback &&
                        r.Status != ReceiptStatus.Dispatched &&
                        r.MarketplaceShipByUtc.HasValue);

        if (warehouseId.HasValue)
        {
            query = query.Where(r => r.WarehouseId == warehouseId.Value);
        }
        else if (accessibleIds != null)
        {
            query = query.Where(r => accessibleIds.Contains(r.WarehouseId));
        }

        var receipts = await query
            .OrderBy(r => r.MarketplaceShipByUtc)
            .ToListAsync(cancellationToken);

        var queueItems = receipts.Select(r =>
        {
            var shipBy = r.MarketplaceShipByUtc!.Value;
            var remainingMinutes = (int)Math.Round((shipBy - now).TotalMinutes);
            var slaTier = shipBy < now
                ? "BREACHED"
                : (shipBy <= now.AddHours(2) ? "AT_RISK_UNDER_2H" : "ON_TRACK");
            return new
            {
                exportReceiptId = r.Id,
                exportReceiptCode = r.Code,
                warehouseId = r.WarehouseId,
                marketplaceOrderId = r.MarketplaceOrderId,
                carrierCode = r.CarrierCode,
                trackingNumber = r.TrackingNumber,
                marketplaceShipByUtc = shipBy,
                remainingMinutes,
                slaTier,
                status = r.Status.ToString()
            };
        }).ToList();

        return WmsEndpointOutcome.Ok(new
        {
            totalCount = queueItems.Count,
            breachedCount = queueItems.Count(x => x.slaTier == "BREACHED"),
            atRiskUnder2HoursCount = queueItems.Count(x => x.slaTier == "AT_RISK_UNDER_2H"),
            onTrackCount = queueItems.Count(x => x.slaTier == "ON_TRACK"),
            items = queueItems
        });
    }

    private async Task<WmsEndpointOutcome> ReverseAccountingOutboxEntryAsync(
        SyncAccountingAndEInvoiceInput request,
        ExportReceipt? exportReceipt,
        int targetReceiptId,
        int targetWarehouseId,
        string receiptCode,
        string accountingProvider,
        string einvoiceProvider,
        int? validAuditUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var eventIdToReverse = request.OriginalOutboxEventId?.Trim();
        var origOutbox = !string.IsNullOrWhiteSpace(eventIdToReverse)
            ? await _db.IntegrationOutboxMessages.FirstOrDefaultAsync(
                m => m.EventId == eventIdToReverse && m.EventType == "AccountingAndEinvoiceNd70Synced",
                cancellationToken)
            : await _db.IntegrationOutboxMessages
                .Where(m => m.AggregateType == "ExportReceipt" && m.AggregateId == targetReceiptId && m.EventType == "AccountingAndEinvoiceNd70Synced")
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync(cancellationToken);

        if (origOutbox == null || string.Equals(origOutbox.Status, "REVERSED", StringComparison.OrdinalIgnoreCase))
        {
            throw new ERP.Domain.Exceptions.ConflictException(
                "ACCOUNTING_EINVOICE_ALREADY_REVERSED",
                $"Bút toán đồng bộ Kế toán & Hóa đơn điện tử của phiếu xuất #{targetReceiptId} không tồn tại hoặc đã được hủy/đảo trước đó.");
        }

        origOutbox.Status = "REVERSED";
        origOutbox.LastError = "Reversed via Form 04/SS-HDDT cancellation";

        if (exportReceipt != null)
        {
            exportReceipt.EInvoiceStatus = "CANCELLED_04SS";
        }

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = validAuditUserId,
            WarehouseId = targetWarehouseId,
            EntityName = "ExportReceipt",
            EntityId = targetReceiptId,
            Action = "ExportReceipt.ReverseAccountingAndEinvoiceOutbox",
            NewValues = origOutbox.EventId,
            Reason = $"Hủy/đảo đồng bộ Kế toán ({accountingProvider}) & HĐĐT NĐ 70/2025 ({einvoiceProvider}) theo Mẫu 04/SS-HĐĐT ({origOutbox.EventId})",
            Result = "Success",
            Timestamp = now
        });

        await _db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            exportReceiptId = targetReceiptId,
            exportReceiptCode = receiptCode,
            outboxEventId = origOutbox.EventId,
            reversed = true,
            form04SsApplied = true,
            status = "REVERSED",
            statusLabel = $"Đã hủy/đảo bút toán đồng bộ Kế toán & HĐĐT NĐ 70/2025 theo Mẫu 04/SS-HĐĐT ({origOutbox.EventId})",
            reversedAtUtc = now
        });
    }

    private static List<object> BuildCircular200JournalEntries(
        string receiptCode,
        bool canReadCost,
        decimal totalCogsAmount,
        decimal totalRevenueAmount,
        decimal totalVatAmount,
        decimal inboundPurchaseAmount,
        string? importReceiptCode)
    {
        var entries = new List<object>
        {
            new
            {
                entryType = "COGS_ISSUE_TT200",
                descriptionVi = $"Hạch toán giá vốn hàng xuất kho theo bình quân gia quyền di động (MAC) — Phiếu {receiptCode}",
                debitAccount = "632",
                debitAccountNameVi = "Giá vốn hàng bán",
                creditAccount = "1561",
                creditAccountNameVi = "Giá mua hàng hóa kho",
                debitAmount = canReadCost ? (decimal?)totalCogsAmount : null,
                creditAmount = canReadCost ? (decimal?)totalCogsAmount : null
            },
            new
            {
                entryType = "REVENUE_RECEIVABLE_TT200",
                descriptionVi = $"Hạch toán doanh thu bán hàng & công nợ phải thu khách hàng — Phiếu {receiptCode}",
                debitAccount = "131",
                debitAccountNameVi = "Phải thu của khách hàng",
                creditAccount = "5111",
                creditAccountNameVi = "Doanh thu bán hàng hóa",
                debitAmount = (decimal?)totalRevenueAmount,
                creditAmount = (decimal?)totalRevenueAmount
            },
            new
            {
                entryType = "VAT_OUTPUT_TT200",
                descriptionVi = $"Hạch toán thuế GTGT đầu ra (10%) theo Nghị định 70/2025/NĐ-CP — Phiếu {receiptCode}",
                debitAccount = "131",
                debitAccountNameVi = "Phải thu của khách hàng (Thuế GTGT)",
                creditAccount = "33311",
                creditAccountNameVi = "Thuế GTGT đầu ra phải nộp",
                debitAmount = (decimal?)totalVatAmount,
                creditAmount = (decimal?)totalVatAmount
            }
        };

        if (inboundPurchaseAmount > 0m)
        {
            entries.Add(new
            {
                entryType = "INBOUND_PURCHASE_TT200",
                descriptionVi = $"Hạch toán nhập mua hàng hóa nhập kho — Phiếu {importReceiptCode}",
                debitAccount = "1561",
                debitAccountNameVi = "Giá mua hàng hóa kho",
                creditAccount = "331",
                creditAccountNameVi = "Phải trả cho người bán",
                debitAmount = canReadCost ? (decimal?)inboundPurchaseAmount : null,
                creditAmount = canReadCost ? (decimal?)inboundPurchaseAmount : null
            });
        }

        return entries;
    }
}
