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

public sealed partial class StocktakeOperationsService
{
    public async Task<WmsEndpointOutcome> GetCountAttemptsAndAutoApprovalAsync(
        string? id = null,
        decimal thresholdPct = 1.0m,
        decimal thresholdValueVnd = 500000m,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null)
        {
            return WmsEndpointOutcome.Status(503, new { code = "DB_UNAVAILABLE", message = "Cơ sở dữ liệu chưa sẵn sàng." });
        }

        var stocktake = await ResolveStocktakeEntityAsync(id, asNoTracking: true, cancellationToken);
        if (stocktake == null)
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "STOCKTAKE_NOT_FOUND",
                errorCode = "STOCKTAKE_NOT_FOUND",
                stocktakeId = id,
                message = $"Không tìm thấy phiếu kiểm kê '{id}'."
            });
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (effectiveAuth != null && stocktake.WarehouseId > 0)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(stocktake.WarehouseId, cancellationToken);
        }

        var resolvedThresholdPct = thresholdPct > 0m ? thresholdPct : 1.0m;
        var resolvedThresholdValueVnd = thresholdValueVnd > 0m ? thresholdValueVnd : 500000m;

        var lines = await BuildStocktakeAutoApprovalLinesAsync(
            stocktake,
            resolvedThresholdPct,
            resolvedThresholdValueVnd,
            cancellationToken);

        var matchedCount = lines.Count(x => x.approvalStatus == "MATCHED_NO_VARIANCE");
        var autoApprovedCount = lines.Count(x => x.approvalStatus == "AUTO_APPROVED_WITHIN_THRESHOLD");
        var requiresManagerApprovalCount = lines.Count(x => x.requiresManagerApproval);
        var requiresRecountCount = lines.Count(x => x.requiresRecount);
        var totalAbsoluteVarianceValueVnd = lines.Sum(x => x.varianceValueVnd);
        var canAutoCompleteSession = requiresManagerApprovalCount == 0;
        var autoApprovalDecision = canAutoCompleteSession
            ? "AUTO_APPROVED_ALL_WITHIN_THRESHOLD"
            : "ESCALATED_TO_MANAGER_APPROVAL";

        return WmsEndpointOutcome.Ok(new
        {
            stocktakeId = stocktake.Id,
            stocktakeCode = stocktake.Code,
            status = (int)stocktake.Status,
            statusName = stocktake.Status.ToString(),
            autoApprovalThresholdPct = resolvedThresholdPct,
            autoApprovalThresholdValueVnd = resolvedThresholdValueVnd,
            autoApprovalDecision,
            lines,
            summary = new
            {
                totalLines = lines.Count,
                matchedCount,
                autoApprovedCount,
                requiresManagerApprovalCount,
                requiresRecountCount,
                totalAbsoluteVarianceValueVnd,
                canAutoCompleteSession
            }
        });
    }

    public async Task<WmsEndpointOutcome> EvaluateAutoApprovalAsync(
        string? id = null,
        decimal? thresholdPct = 1.0m,
        decimal? thresholdValueVnd = 500000m,
        IReadOnlyList<RecountSubmissionInput>? recountSubmissions = null,
        int? actorUserId = null,
        string? counterUserLabel = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null)
        {
            return WmsEndpointOutcome.Status(503, new { code = "DB_UNAVAILABLE", message = "Cơ sở dữ liệu chưa sẵn sàng." });
        }

        var stocktake = await ResolveStocktakeEntityAsync(id, asNoTracking: false, cancellationToken);
        if (stocktake == null)
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "STOCKTAKE_NOT_FOUND",
                errorCode = "STOCKTAKE_NOT_FOUND",
                stocktakeId = id,
                message = $"Không tìm thấy phiếu kiểm kê '{id}'."
            });
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (effectiveAuth != null && stocktake.WarehouseId > 0)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(stocktake.WarehouseId, cancellationToken);
        }

        var now = DateTime.UtcNow;
        int? validAuditUserId = null;
        var candidateUid = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : null);
        if (candidateUid is > 0 &&
            await db.Users.AsNoTracking().AnyAsync(u => u.Id == candidateUid.Value, cancellationToken))
        {
            validAuditUserId = candidateUid;
        }

        var resolvedCounterLabel = validAuditUserId?.ToString()
            ?? counterUserLabel
            ?? "system";

        if (recountSubmissions is { Count: > 0 })
        {
            foreach (var sub in recountSubmissions)
            {
                if (!sub.CountedQuantity.HasValue)
                    continue;

                if (sub.CountedQuantity.Value < 0m)
                {
                    return WmsEndpointOutcome.BadRequest(new
                    {
                        code = "NEGATIVE_COUNTED_QUANTITY",
                        errorCode = "NEGATIVE_COUNTED_QUANTITY",
                        message = "Số lượng thực đếm không được âm."
                    });
                }

                var parsedDetailId = ParseDetailIdFromJsonElement(sub.StocktakeDetailId) ?? sub.DetailId;
                StocktakeDetail? targetDetail = null;

                if (parsedDetailId.HasValue && parsedDetailId.Value > 0)
                {
                    targetDetail = stocktake.Details.FirstOrDefault(d => d.Id == parsedDetailId.Value);
                }

                if (targetDetail == null && sub.ProductId.HasValue && sub.ProductId.Value > 0)
                {
                    targetDetail = stocktake.Details.FirstOrDefault(d => d.ProductId == sub.ProductId.Value);
                }

                if (targetDetail == null && stocktake.Details.Count > 0)
                {
                    targetDetail = stocktake.Details.OrderBy(d => d.Id).First();
                }

                if (targetDetail == null)
                    continue;

                if (targetDetail.CountAttemptCount <= 0 && targetDetail.ActualQuantity.HasValue && !targetDetail.CountAttempt1Qty.HasValue)
                {
                    targetDetail.CountAttempt1Qty = targetDetail.ActualQuantity.Value;
                    targetDetail.CountAttemptCount = 1;
                }

                var nextAttemptNo = Math.Min(3, Math.Max(0, targetDetail.CountAttemptCount) + 1);
                if (nextAttemptNo == 1)
                    targetDetail.CountAttempt1Qty = sub.CountedQuantity.Value;
                else if (nextAttemptNo == 2)
                    targetDetail.CountAttempt2Qty = sub.CountedQuantity.Value;
                else
                    targetDetail.CountAttempt3Qty = sub.CountedQuantity.Value;

                targetDetail.CountAttemptCount = nextAttemptNo;
                targetDetail.LastCountedByUserId = validAuditUserId;
                targetDetail.ActualQuantity = sub.CountedQuantity.Value;
                targetDetail.DifferenceQuantity = sub.CountedQuantity.Value - targetDetail.SystemQuantity;
                if (!string.IsNullOrWhiteSpace(sub.Note))
                {
                    targetDetail.RecountReason = sub.Note.Trim();
                    targetDetail.Note = sub.Note.Trim();
                }

                var attemptPayload = JsonSerializer.Serialize(new
                {
                    stocktakeId = stocktake.Id,
                    stocktakeDetailId = targetDetail.Id,
                    attemptNo = nextAttemptNo,
                    countedQty = targetDetail.ActualQuantity.Value,
                    varianceQty = targetDetail.DifferenceQuantity,
                    counterUserId = resolvedCounterLabel,
                    countedAtUtc = now,
                    note = sub.Note ?? $"Đếm lại lần #{nextAttemptNo}"
                });

                db.AuditLogs.Add(new AuditLog
                {
                    UserId = validAuditUserId,
                    WarehouseId = stocktake.WarehouseId,
                    EntityName = "StocktakeDetail",
                    EntityId = targetDetail.Id,
                    Action = "STOCKTAKE_RECOUNT_ATTEMPT",
                    NewValues = attemptPayload,
                    Reason = sub.Note ?? $"Nộp kết quả đếm lại lần #{nextAttemptNo}",
                    Result = "Success",
                    Timestamp = now
                });
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        var resolvedThresholdPct = thresholdPct is > 0m ? thresholdPct.Value : 1.0m;
        var resolvedThresholdValueVnd = thresholdValueVnd is > 0m ? thresholdValueVnd.Value : 500000m;

        var lines = await BuildStocktakeAutoApprovalLinesAsync(
            stocktake,
            resolvedThresholdPct,
            resolvedThresholdValueVnd,
            cancellationToken);

        var matchedCount = lines.Count(x => x.approvalStatus == "MATCHED_NO_VARIANCE");
        var autoApprovedCount = lines.Count(x => x.approvalStatus == "AUTO_APPROVED_WITHIN_THRESHOLD");
        var requiresManagerApprovalCount = lines.Count(x => x.requiresManagerApproval);
        var requiresRecountCount = lines.Count(x => x.requiresRecount);
        var totalAbsoluteVarianceValueVnd = lines.Sum(x => x.varianceValueVnd);
        var canAutoCompleteSession = requiresManagerApprovalCount == 0;
        var autoApprovalDecision = canAutoCompleteSession
            ? "AUTO_APPROVED_ALL_WITHIN_THRESHOLD"
            : "ESCALATED_TO_MANAGER_APPROVAL";

        if (requiresManagerApprovalCount > 0 &&
            (stocktake.Status == StocktakeStatus.InProgress ||
             stocktake.Status == StocktakeStatus.Draft))
        {
            stocktake.Status = StocktakeStatus.PendingApproval;
        }

        db.AuditLogs.Add(new AuditLog
        {
            UserId = validAuditUserId,
            WarehouseId = stocktake.WarehouseId,
            EntityName = "Stocktake",
            EntityId = stocktake.Id,
            Action = "STOCKTAKE_AUTO_APPROVAL_EVALUATED",
            NewValues = JsonSerializer.Serialize(new
            {
                stocktakeId = stocktake.Id,
                stocktakeCode = stocktake.Code,
                thresholdPct = resolvedThresholdPct,
                thresholdValueVnd = resolvedThresholdValueVnd,
                matchedCount,
                autoApprovedCount,
                requiresManagerApprovalCount,
                requiresRecountCount,
                totalAbsoluteVarianceValueVnd,
                autoApprovalDecision
            }),
            Reason = canAutoCompleteSession
                ? $"Tự động duyệt chênh lệch trong ngưỡng (<= {resolvedThresholdPct:0.##}% và <= {resolvedThresholdValueVnd:N0}đ)"
                : $"Chuyển cấp Quản lý kho phê duyệt ({requiresManagerApprovalCount} dòng vượt ngưỡng)",
            Result = "Success",
            Timestamp = now
        });

        await db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            stocktakeId = stocktake.Id,
            stocktakeCode = stocktake.Code,
            status = (int)stocktake.Status,
            statusName = stocktake.Status.ToString(),
            autoApprovalThresholdPct = resolvedThresholdPct,
            autoApprovalThresholdValueVnd = resolvedThresholdValueVnd,
            autoApprovalDecision,
            lines,
            summary = new
            {
                totalLines = lines.Count,
                matchedCount,
                autoApprovedCount,
                requiresManagerApprovalCount,
                requiresRecountCount,
                totalAbsoluteVarianceValueVnd,
                canAutoCompleteSession
            }
        });
    }
}
