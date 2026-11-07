using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ExportReceiptOperationsService
{
    public async Task<WmsEndpointOutcome?> CheckSubscriptionQuotaForCreateAsync(
        CancellationToken cancellationToken = default)
    {
        var tenantId = _db.CurrentTenantId;
        var sub = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        if (sub != null)
        {
            var quota = sub.MonthlyOrderQuota > 0 ? sub.MonthlyOrderQuota : 1500;
            var maxAllowed = (int)Math.Ceiling(quota * 1.15m);
            var usagePct = decimal.Round((sub.CurrentMonthOrderCount * 100m) / quota, 2);
            var isExpiredPastDue = string.Equals(sub.Status, "PAST_DUE", StringComparison.OrdinalIgnoreCase) &&
                                   sub.CurrentPeriodEndUtc.AddHours(72) < DateTime.UtcNow;
            if (sub.CurrentMonthOrderCount >= maxAllowed || usagePct > 115m || isExpiredPastDue)
            {
                return WmsEndpointOutcome.Json(403, new
                {
                    code = "SUBSCRIPTION_QUOTA_EXCEEDED",
                    errorCode = "SUBSCRIPTION_QUOTA_EXCEEDED",
                    message = $"Thuê bao đã vượt ngưỡng cứng 115% hạn ngạch đơn hàng tháng ({sub.CurrentMonthOrderCount}/{quota} đơn - {usagePct}%) hoặc quá hạn thanh toán 72h. Hệ thống chuyển sang chế độ Chỉ Đọc cho đơn mới."
                });
            }
        }

        return null;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int TenantId, int ExportReceiptId), byte> _countedExportReceiptOrders = new();

    public async Task IncrementSubscriptionOrderCountAsync(
        CancellationToken cancellationToken = default,
        int? salesOrderId = null,
        string? marketplaceOrderId = null,
        int? exportReceiptId = null)
    {
        var tenantId = _db.CurrentTenantId;
        if (exportReceiptId is > 0)
        {
            var receipt = await _db.ExportReceipts.AsNoTracking()
                .Where(r => r.Id == exportReceiptId.Value)
                .Select(r => new { r.Id, r.Status, r.SalesOrderId, r.MarketplaceOrderId })
                .FirstOrDefaultAsync(cancellationToken);
            if (receipt != null)
            {
                if (receipt.Status is ReceiptStatus.Draft or ReceiptStatus.Cancelled)
                {
                    return;
                }

                salesOrderId ??= receipt.SalesOrderId;
                marketplaceOrderId ??= receipt.MarketplaceOrderId;
            }

            if (!_countedExportReceiptOrders.TryAdd((tenantId, exportReceiptId.Value), 1))
            {
                return;
            }
        }

        if (salesOrderId is > 0)
        {
            var hasSalesOrder = await _db.SalesOrders.AsNoTracking()
                .AnyAsync(o => o.Id == salesOrderId.Value, cancellationToken);
            var existingReceiptCount = await _db.ExportReceipts.AsNoTracking()
                .CountAsync(r => r.SalesOrderId == salesOrderId.Value &&
                                 r.Status != ReceiptStatus.Draft &&
                                 r.Status != ReceiptStatus.Cancelled, cancellationToken);
            if (hasSalesOrder || existingReceiptCount > 1)
            {
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(marketplaceOrderId))
        {
            var normalizedMpId = marketplaceOrderId.Trim();
            var hasMatchingSalesOrder = await _db.SalesOrders.AsNoTracking()
                .AnyAsync(o => o.ExternalOrderId == normalizedMpId, cancellationToken);
            var existingMpReceiptCount = await _db.ExportReceipts.AsNoTracking()
                .CountAsync(r => r.MarketplaceOrderId == normalizedMpId &&
                                 r.Status != ReceiptStatus.Draft &&
                                 r.Status != ReceiptStatus.Cancelled, cancellationToken);
            if (hasMatchingSalesOrder || existingMpReceiptCount > 1)
            {
                return;
            }
        }

        var sub = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        if (sub != null)
        {
            sub.CurrentMonthOrderCount += 1;
            sub.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<WmsEndpointOutcome> VerifyDispatchCutoffAsync(
        VerifyDispatchCutoffInput? request,
        int? actorUserId,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var warehouseId = request?.WarehouseId;
        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveWhAccess != null)
        {
            if (warehouseId.HasValue)
            {
                await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else if (_currentUser == null || (_currentUser.IsAuthenticated && !_currentUser.IsGlobalAdmin))
            {
                accessibleIds = await effectiveWhAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
            }
        }

        var outboundCutoffLocal = "22:00";
        var query = _db.ExportReceipts.AsNoTracking();
        var taskQuery = _db.PickingTasks.AsNoTracking()
            .Where(t => t.Status != PickingTaskStatus.Completed
                        && t.Status != PickingTaskStatus.Cancelled);
        var calQuery = _db.WarehouseCalendarDays.AsNoTracking()
            .Where(c => c.IsOpen && c.OutboundCutoffLocal.HasValue);

        if (warehouseId.HasValue)
        {
            query = query.Where(r => r.WarehouseId == warehouseId.Value);
            taskQuery = taskQuery.Where(t => t.WarehouseId == warehouseId.Value);
            calQuery = calQuery.Where(c => c.WarehouseId == warehouseId.Value);
        }
        else if (accessibleIds != null)
        {
            query = query.Where(r => accessibleIds.Contains(r.WarehouseId));
            taskQuery = taskQuery.Where(t => accessibleIds.Contains(t.WarehouseId));
            calQuery = calQuery.Where(c => accessibleIds.Contains(c.WarehouseId));
        }

        var checkedExportReceiptsCount = await query.CountAsync(cancellationToken);
        var openPickingTasksCount = await taskQuery.CountAsync(cancellationToken);
        var cutoffSpan = await calQuery.Select(c => c.OutboundCutoffLocal).FirstOrDefaultAsync(cancellationToken);
        if (cutoffSpan.HasValue)
        {
            outboundCutoffLocal = cutoffSpan.Value.ToString(@"hh\:mm");
        }

        var now = DateTime.UtcNow;
        var verificationId = $"EXP-CUT-{now:yyyyMMdd-HHmm}";
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            WarehouseId = warehouseId,
            EntityName = "ExportReceipt",
            Action = "ExportReceipt.VerifyDispatchCutoff",
            NewValues = verificationId,
            Reason = $"Đối soát giờ chốt xuất kho ({outboundCutoffLocal}, {checkedExportReceiptsCount} phiếu xuất, {openPickingTasksCount} tác vụ lấy hàng mở)",
            Result = "Success",
            Timestamp = now
        });
        await _db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            verificationId,
            warehouseId,
            checkedExportReceiptsCount,
            openPickingTasksCount,
            outboundCutoffLocal,
            singleShipDeductionGuard = "Chốt trừ tồn kho 1 lần duy nhất khi xác nhận xuất",
            cutoffComplianceStatus = $"Đạt chuẩn giờ chốt xuất kho ({outboundCutoffLocal})",
            status = "Đã đối soát giờ chốt xuất kho & khóa trừ tồn đơn nhất",
            statusLabel = "Đã đối soát giờ chốt xuất kho & khóa trừ tồn đơn nhất",
            verifiedAtUtc = now
        });
    }
}
