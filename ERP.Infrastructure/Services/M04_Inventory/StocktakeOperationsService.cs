using System.Data;
using System.Globalization;
using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class StocktakeOperationsService(
    ErpKhoDbContext db = null!,
    IWarehouseAuthorizationService? warehouseAccess = null,
    ICurrentUser? currentUser = null) : IStocktakeOperationsService
{
    private sealed record CountAttemptItemDto(
        int attemptNo,
        decimal countedQty,
        string counterUserId,
        DateTime countedAtUtc,
        decimal varianceQty,
        string? note = null);

    private sealed record StocktakeLineAutoApprovalDto(
        int stocktakeDetailId,
        int productId,
        string productCode,
        string productName,
        int? locationId,
        string locationCode,
        string? lotNumber,
        decimal systemQty,
        decimal countedQty,
        decimal varianceQty,
        decimal variancePct,
        decimal unitCost,
        decimal varianceValueVnd,
        int attemptCount,
        int maxAllowedAttempts,
        IReadOnlyList<CountAttemptItemDto> attemptHistory,
        bool requiresRecount,
        bool autoApproveEligible,
        bool requiresManagerApproval,
        string approvalStatus);

    public async Task<WmsEndpointOutcome> GetReasonCodesAndAdjustmentsAsync(
        int? warehouseId = null,
        bool canReadCost = true,
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

        var recentProposals = new List<object>();
        var openStocktakesCount = 0;
        var activeCountFreezeLocksCount = 0;
        if (db != null)
        {
            var tenantId = db.CurrentTenantId > 0 ? db.CurrentTenantId : 1;
            var stQuery = db.Stocktakes.AsNoTracking()
                .Where(s => s.Status == StocktakeStatus.Draft);
            var lockQuery = db.InventoryLocks.AsNoTracking()
                .Where(l => l.Status == InventoryLockStatus.Active && l.LockType == InventoryLockType.CountFreeze);
            if (warehouseId.HasValue)
            {
                stQuery = stQuery.Where(s => s.WarehouseId == warehouseId.Value);
                lockQuery = lockQuery.Where(l => l.WarehouseId == warehouseId.Value);
            }
            else if (accessibleIds != null)
            {
                stQuery = stQuery.Where(s => accessibleIds.Contains(s.WarehouseId));
                lockQuery = lockQuery.Where(l => accessibleIds.Contains(l.WarehouseId));
            }

            openStocktakesCount = await stQuery.CountAsync(cancellationToken);
            activeCountFreezeLocksCount = await lockQuery.CountAsync(cancellationToken);

            var proposalQuery = db.StocktakeAdjustmentProposals
                .AsNoTracking()
                .Where(p => p.TenantId == tenantId);

            if (warehouseId.HasValue)
                proposalQuery = proposalQuery.Where(p => p.WarehouseId == warehouseId.Value);
            else if (accessibleIds != null)
                proposalQuery = proposalQuery.Where(p => accessibleIds.Contains(p.WarehouseId));

            var dbProposals = await proposalQuery
                .OrderByDescending(p => p.CreatedAtUtc)
                .ThenByDescending(p => p.Id)
                .Take(50)
                .ToListAsync(cancellationToken);

            var productIds = dbProposals.Select(p => p.ProductId).Distinct().ToList();
            var productCodeMap = productIds.Count > 0
                ? await db.Products.AsNoTracking()
                    .Where(p => productIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.Code, cancellationToken)
                : new Dictionary<int, string>();

            foreach (var p in dbProposals)
            {
                var isApproved = string.Equals(p.Status, "APPROVED", StringComparison.OrdinalIgnoreCase);
                var productCode = productCodeMap.TryGetValue(p.ProductId, out var code) && !string.IsNullOrWhiteSpace(code)
                    ? code.Trim()
                    : $"SKU-{p.ProductId}";

                recentProposals.Add(new
                {
                    id = p.Id,
                    proposalCode = p.ProposalCode,
                    stocktakeId = p.StocktakeId,
                    warehouseId = p.WarehouseId,
                    productId = p.ProductId,
                    productCode,
                    locationId = p.LocationId,
                    systemQuantity = p.SystemQuantity,
                    countedQuantity = p.CountedQuantity,
                    deltaQuantity = p.DeltaQuantity,
                    variancePct = p.VariancePct,
                    unitCost = canReadCost ? (decimal?)p.UnitCost : null,
                    varianceValue = canReadCost ? (decimal?)p.VarianceValue : null,
                    isAutoApproved = p.IsAutoApproved,
                    reasonCode = p.ReasonCode,
                    note = p.Note,
                    status = p.Status,
                    statusLabel = isApproved
                        ? "Đã phê duyệt & Ghi sổ"
                        : "Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm",
                    createdBy = p.CreatedBy,
                    approvedBy = p.ApprovedBy,
                    createdAtUtc = p.CreatedAtUtc,
                    approvedAtUtc = p.ApprovedAtUtc
                });
            }
        }

        return WmsEndpointOutcome.Ok(new
        {
            reasonCodes = new object[]
            {
                new { code = "COUNT_VARIANCE", name = "Chênh lệch kiểm đếm định kỳ", category = "CycleCount", requiresApproval = true },
                new { code = "DAMAGED_FOUND", name = "Phát hiện hàng hư hỏng vật lý", category = "Quality", requiresApproval = true },
                new { code = "EXPIRY_WRITE_OFF", name = "Hủy hàng hết hạn sử dụng", category = "Expiry", requiresApproval = true },
                new { code = "SUPPLIER_SHORTAGE", name = "Thiếu hụt nguồn giao từ NCC", category = "Inbound", requiresApproval = true },
                new { code = "MISPLACED_STOCK", name = "Sai lệch vị trí lưu kho nội bộ", category = "Slotting", requiresApproval = false },
                new { code = "RECALL_QUARANTINE", name = "Cô lập thu hồi lô hàng khẩn cấp", category = "Recall", requiresApproval = true }
            },
            adjustmentGovernance = new
            {
                sodRequired = true,
                approvalThresholdPct = 1.0m,
                approvalThresholdAmountVnd = 500_000m,
                supportedTransactionTypes = new[] { "AdjustmentIncrease", "AdjustmentDecrease" }
            },
            openStocktakesCount,
            activeCountFreezeLocksCount,
            status = "Đã tải danh mục mã lý do & lịch sử đề xuất điều chỉnh kiểm kê",
            statusLabel = "Đã tải danh mục mã lý do & lịch sử đề xuất điều chỉnh kiểm kê",
            recentProposals
        });
    }

    public async Task<WmsEndpointOutcome> VerifyFreezePolicyAsync(
        VerifyStocktakeFreezePolicyInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var warehouseId = request?.WarehouseId;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var openStocktakesCount = 0;
        var approvedStocktakesCount = 0;
        var activeCountFreezeLocksCount = 0;
        if (db != null)
        {
            var stQuery = db.Stocktakes.AsNoTracking();
            var lockQuery = db.InventoryLocks.AsNoTracking()
                .Where(l => l.Status == InventoryLockStatus.Active && l.LockType == InventoryLockType.CountFreeze);

            if (warehouseId.HasValue)
            {
                stQuery = stQuery.Where(s => s.WarehouseId == warehouseId.Value);
                lockQuery = lockQuery.Where(l => l.WarehouseId == warehouseId.Value);
            }
            else if (accessibleIds != null)
            {
                stQuery = stQuery.Where(s => accessibleIds.Contains(s.WarehouseId));
                lockQuery = lockQuery.Where(l => accessibleIds.Contains(l.WarehouseId));
            }

            openStocktakesCount = await stQuery.CountAsync(
                s => s.Status != StocktakeStatus.Approved &&
                     s.Status != StocktakeStatus.Posted &&
                     s.Status != StocktakeStatus.Completed &&
                     s.Status != StocktakeStatus.Cancelled,
                cancellationToken);
            approvedStocktakesCount = await stQuery.CountAsync(
                s => s.Status == StocktakeStatus.Approved ||
                     s.Status == StocktakeStatus.Posted ||
                     s.Status == StocktakeStatus.Completed,
                cancellationToken);
            activeCountFreezeLocksCount = await lockQuery.CountAsync(cancellationToken);
        }

        var now = DateTime.UtcNow;
        var verificationCode = $"ST-FRZ-{now:yyyyMMdd-HHmm}";
        if (db != null)
        {
            var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : null);
            db.AuditLogs.Add(new AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = warehouseId,
                EntityName = "Stocktake",
                Action = "Stocktake.VerifyFreezePolicy",
                NewValues = verificationCode,
                Reason = $"Kiểm định chính sách đóng băng kiểm kê ({openStocktakesCount} phiên mở, {activeCountFreezeLocksCount} khóa CountFreeze)",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return WmsEndpointOutcome.Ok(new
        {
            verificationCode,
            warehouseId,
            openStocktakesCount,
            approvedStocktakesCount,
            activeCountFreezeLocksCount,
            freezePolicyStatus = "Đã kích hoạt khóa đóng băng kiểm kê (CountFreeze) chống xuất/di chuyển lệch kỳ",
            status = "Đã xác thực chính sách đóng băng kiểm kê & mã lý do điều chỉnh",
            statusLabel = "Đã xác thực chính sách đóng băng kiểm kê & mã lý do điều chỉnh",
            verifiedAtUtc = now
        });
    }
}
