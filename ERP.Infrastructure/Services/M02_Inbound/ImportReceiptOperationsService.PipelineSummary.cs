using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ImportReceiptOperationsService
{
    public async Task<WmsEndpointOutcome> GetPipelineSummaryAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        if (db == null)
        {
            return WmsEndpointOutcome.Ok(new
            {
                totalReceipts = 0,
                draftOrReceivingCount = 0,
                qcOrApprovalPendingCount = 0,
                postedCount = 0,
                cancelledCount = 0,
                status = "Đã tải tổng quan luồng phiếu nhập kho",
                statusLabel = "Đã tải tổng quan luồng phiếu nhập kho"
            });
        }

        var query = db.ImportReceipts.AsNoTracking();
        if (warehouseId.HasValue)
            query = query.Where(r => r.WarehouseId == warehouseId.Value);
        else if (accessibleIds is { Count: > 0 })
            query = query.Where(r => accessibleIds.Contains(r.WarehouseId));

        var grouped = await query
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var totalReceipts = grouped.Sum(x => x.Count);
        var draftOrReceivingCount = grouped
            .Where(x => x.Status is ERP.Domain.Enums.ReceiptStatus.Draft
                or ERP.Domain.Enums.ReceiptStatus.Received
                or ERP.Domain.Enums.ReceiptStatus.Dispatched
                or ERP.Domain.Enums.ReceiptStatus.DiscrepancyPending
                or ERP.Domain.Enums.ReceiptStatus.DiscrepancySubmitted
                or ERP.Domain.Enums.ReceiptStatus.DiscrepancyPendingApproval
                or ERP.Domain.Enums.ReceiptStatus.DiscrepancyResolved
                or ERP.Domain.Enums.ReceiptStatus.DiscrepancyRejected)
            .Sum(x => x.Count);
        var qcOrApprovalPendingCount = grouped
            .Where(x => x.Status is ERP.Domain.Enums.ReceiptStatus.QcPending
                or ERP.Domain.Enums.ReceiptStatus.QcCompleted
                or ERP.Domain.Enums.ReceiptStatus.ReadyToPost)
            .Sum(x => x.Count);
        var postedCount = grouped
            .Where(x => x.Status is ERP.Domain.Enums.ReceiptStatus.Posted
                or ERP.Domain.Enums.ReceiptStatus.Approved)
            .Sum(x => x.Count);
        var cancelledCount = grouped
            .Where(x => x.Status is ERP.Domain.Enums.ReceiptStatus.Cancelled
                or ERP.Domain.Enums.ReceiptStatus.CancelPendingPutback)
            .Sum(x => x.Count);

        return WmsEndpointOutcome.Ok(new
        {
            totalReceipts,
            draftOrReceivingCount,
            qcOrApprovalPendingCount,
            postedCount,
            cancelledCount,
            status = "Đã tải tổng quan luồng phiếu nhập kho",
            statusLabel = "Đã tải tổng quan luồng phiếu nhập kho"
        });
    }

    public async Task<WmsEndpointOutcome> GetQcAndRmaSummaryAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        if (db == null)
        {
            return WmsEndpointOutcome.Ok(new
            {
                totalReceiptsInspected = 0,
                pendingQcOrPostingCount = 0,
                postedWithLotOrQcCount = 0,
                rmaEligibleReceiptsCount = 0,
                qcWorkCenterModes = new[] { "Kiểm định chất lượng đầu vào", "Kiểm định hàng trả về", "Rà soát hàng biệt trữ & hư hỏng" },
                status = "Đã tải tổng quan trạm kiểm định chất lượng & tiếp nhận hàng trả về",
                statusLabel = "Đã tải tổng quan trạm kiểm định chất lượng & tiếp nhận hàng trả về"
            });
        }

        var query = db.ImportReceipts.AsNoTracking();
        if (warehouseId.HasValue)
            query = query.Where(r => r.WarehouseId == warehouseId.Value);
        else if (accessibleIds is { Count: > 0 })
            query = query.Where(r => accessibleIds.Contains(r.WarehouseId));

        var grouped = await query
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var rmaEligibleReceiptsCount = await query.CountAsync(
            r => (r.Note != null && (r.Note.Contains("RMA") || r.Note.Contains("RETURN") || r.Note.Contains("trả") || r.Note.Contains("Trả"))) ||
                 r.Status == ERP.Domain.Enums.ReceiptStatus.DiscrepancyPending ||
                 r.Status == ERP.Domain.Enums.ReceiptStatus.DiscrepancySubmitted ||
                 r.Status == ERP.Domain.Enums.ReceiptStatus.DiscrepancyPendingApproval ||
                 r.Status == ERP.Domain.Enums.ReceiptStatus.DiscrepancyResolved ||
                 r.Status == ERP.Domain.Enums.ReceiptStatus.DiscrepancyRejected,
            cancellationToken);

        var totalReceiptsInspected = grouped.Sum(x => x.Count);
        var pendingQcOrPostingCount = grouped
            .Where(x => x.Status is ERP.Domain.Enums.ReceiptStatus.Draft
                or ERP.Domain.Enums.ReceiptStatus.Approved
                or ERP.Domain.Enums.ReceiptStatus.Received
                or ERP.Domain.Enums.ReceiptStatus.Dispatched
                or ERP.Domain.Enums.ReceiptStatus.QcPending
                or ERP.Domain.Enums.ReceiptStatus.QcCompleted
                or ERP.Domain.Enums.ReceiptStatus.ReadyToPost
                or ERP.Domain.Enums.ReceiptStatus.DiscrepancyPending
                or ERP.Domain.Enums.ReceiptStatus.DiscrepancySubmitted
                or ERP.Domain.Enums.ReceiptStatus.DiscrepancyPendingApproval
                or ERP.Domain.Enums.ReceiptStatus.DiscrepancyResolved
                or ERP.Domain.Enums.ReceiptStatus.DiscrepancyRejected)
            .Sum(x => x.Count);
        var postedWithLotOrQcCount = grouped
            .Where(x => x.Status is ERP.Domain.Enums.ReceiptStatus.Posted
                or ERP.Domain.Enums.ReceiptStatus.Approved)
            .Sum(x => x.Count);

        return WmsEndpointOutcome.Ok(new
        {
            totalReceiptsInspected,
            pendingQcOrPostingCount,
            postedWithLotOrQcCount,
            rmaEligibleReceiptsCount,
            qcWorkCenterModes = new[] { "Kiểm định chất lượng đầu vào", "Kiểm định hàng trả về", "Rà soát hàng biệt trữ & hư hỏng" },
            status = "Đã tải tổng quan trạm kiểm định chất lượng & tiếp nhận hàng trả về",
            statusLabel = "Đã tải tổng quan trạm kiểm định chất lượng & tiếp nhận hàng trả về"
        });
    }
}
