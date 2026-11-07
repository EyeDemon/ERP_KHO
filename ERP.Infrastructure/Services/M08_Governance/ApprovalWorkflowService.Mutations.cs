using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Options;
using ERP.Application.Security;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ApprovalWorkflowService
{
    public async Task<ApprovalActionResult> RejectAsync(string documentType, int id, string reason, CancellationToken cancellationToken = default)
    {
        documentType = NormalizeDocumentType(documentType);
        var permissions = await PermissionsAsync(cancellationToken);
        EnsureCapability(documentType, permissions, "approval.reject");
        reason = ApprovalRejectReasonPolicy.Normalize(reason);
        var ownsTransaction = context.Database.IsRelational() && context.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        try
        {
            var result = documentType switch
            {
                "ImportReceipt" => await RejectReceiptAsync(documentType, id, reason, context.ImportReceipts,
                    [ReceiptStatus.Received, ReceiptStatus.QcCompleted, ReceiptStatus.ReadyToPost], cancellationToken),
                "ExportReceipt" => await RejectReceiptAsync(documentType, id, reason, context.ExportReceipts, [ReceiptStatus.Draft], cancellationToken),
                "Stocktake" => await RejectStocktakeAsync(id, reason, cancellationToken),
                "StockTransfer" => await RejectTransferAsync(id, reason, cancellationToken),
                _ => throw new BusinessRuleException("Loại chứng từ không hợp lệ.")
            };
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static string NormalizeDocumentType(string? documentType) =>
        string.Equals(documentType?.Trim(), "IMPORT_RECEIPT", StringComparison.OrdinalIgnoreCase)
            ? "ImportReceipt"
            : documentType?.Trim() ?? string.Empty;

    private async Task<ApprovalActionResult> RejectReceiptAsync<TEntity>(string type, int id, string reason, DbSet<TEntity> set, ReceiptStatus[] pendingStatuses, CancellationToken token) where TEntity : class
    {
        var entity = await set.AsNoTracking().SingleOrDefaultAsync(x => EF.Property<int>(x, "Id") == id, token) ?? throw new NotFoundException("Không tìm thấy chứng từ.");
        var warehouseId = (int)entity.GetType().GetProperty("WarehouseId")!.GetValue(entity)!;
        var creatorId = (int)entity.GetType().GetProperty("CreatedBy")!.GetValue(entity)!;
        var code = (string)entity.GetType().GetProperty("Code")!.GetValue(entity)!;
        var oldStatus = (ReceiptStatus)entity.GetType().GetProperty("Status")!.GetValue(entity)!;
        await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId, token);
        ApprovalSafetyGuard.EnsureDifferentChecker(creatorId, currentUser.UserId);
        var affected = 0;
        if (context.Database.IsRelational())
        {
            affected = await set.Where(x => EF.Property<int>(x, "Id") == id && pendingStatuses.Contains(EF.Property<ReceiptStatus>(x, "Status")))
                .ExecuteUpdateAsync(s => s.SetProperty(x => EF.Property<ReceiptStatus>(x, "Status"), ReceiptStatus.Cancelled), token);
        }
        else
        {
            var tracked = await set.SingleOrDefaultAsync(x => EF.Property<int>(x, "Id") == id, token);
            var statusProp = typeof(TEntity).GetProperty("Status")!;
            var currentStatus = tracked is not null ? (ReceiptStatus)statusProp.GetValue(tracked)! : (ReceiptStatus)(-1);
            if (tracked is not null && pendingStatuses.Contains(currentStatus))
            {
                statusProp.SetValue(tracked, ReceiptStatus.Cancelled);
                oldStatus = currentStatus;
                affected = 1;
            }
        }
        if (affected != 1) throw new ConcurrencyException("Chứng từ không còn ở trạng thái chờ duyệt.");
        if (type == "ImportReceipt")
        {
            await VoidReceiptSerialsAsync(id, token);
            await SyncReceiptAsync(id, token);
        }
        context.AuditLogs.Add(CreateAudit(type, id, code, creatorId, warehouseId, null, reason, oldStatus));
        await context.SaveChangesAsync(token);
        return Result(type, id, code);
    }

    public async Task VoidReceiptSerialsAsync(int receiptId, CancellationToken token = default)
    {
        if (receiptIdentityService is not null) await receiptIdentityService.GetAsync(receiptId, token);
        var detailIds = await context.ImportReceiptDetails.Where(d => d.ImportReceiptId == receiptId).Select(d => d.Id).ToListAsync(token);
        var stagedIdentities = await context.ImportReceiptInventoryIdentities
            .Where(x => detailIds.Contains(x.ImportReceiptDetailId) || (x.ImportReceiptDetail != null && x.ImportReceiptDetail.ImportReceiptId == receiptId))
            .ToListAsync(token);
        if (stagedIdentities.Count > 0) context.ImportReceiptInventoryIdentities.RemoveRange(stagedIdentities);
    }

    public async Task SyncReceiptAsync(int receiptId, CancellationToken token = default)
    {
        var receipt = await context.ImportReceipts.SingleOrDefaultAsync(x => x.Id == receiptId, token);
        if (receipt is null) return;
        if (inboundPlanningIntegration is not null && inboundPlanningIntegration is not ReceiptInboundPlanningIntegration)
            await inboundPlanningIntegration.MarkReceivingAsync(receipt, token);
        if (receipt.AsnId.HasValue)
        {
            var asn = await context.Asns.SingleOrDefaultAsync(x => x.Id == receipt.AsnId.Value && x.WarehouseId == receipt.WarehouseId, token);
            if (asn is not null && asn.Status == AsnStatus.Receiving)
            {
                var hasOtherActiveReceipt = await context.ImportReceipts
                    .AnyAsync(x => x.AsnId == asn.Id && x.Id != receiptId && x.Status != ReceiptStatus.Cancelled, token);
                if (!hasOtherActiveReceipt)
                {
                    asn.Status = AsnStatus.Arrived;
                    asn.UpdatedAtUtc = DateTime.UtcNow;
                }
            }
        }
    }

    private async Task<ApprovalActionResult> RejectStocktakeAsync(int id, string reason, CancellationToken token)
    {
        var entity = await context.Stocktakes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException("Không tìm thấy chứng từ.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.WarehouseId, token);
        ApprovalSafetyGuard.EnsureDifferentChecker(entity.CreatedBy, currentUser.UserId);
        var affected = 0;
        if (context.Database.IsRelational())
        {
            affected = await context.Stocktakes.Where(x => x.Id == id && x.Status == StocktakeStatus.Draft)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, StocktakeStatus.Cancelled), token);
        }
        else
        {
            var tracked = await context.Stocktakes.SingleOrDefaultAsync(x => x.Id == id && x.Status == StocktakeStatus.Draft, token);
            if (tracked is not null) { tracked.Status = StocktakeStatus.Cancelled; affected = 1; }
        }
        if (affected != 1) throw new ConcurrencyException("Chứng từ không còn ở trạng thái chờ duyệt.");
        context.AuditLogs.Add(CreateAudit("Stocktake", id, entity.Code, entity.CreatedBy, entity.WarehouseId, null, reason, ReceiptStatus.Draft));
        await context.SaveChangesAsync(token);
        return Result("Stocktake", id, entity.Code);
    }

    private async Task<ApprovalActionResult> RejectTransferAsync(int id, string reason, CancellationToken token)
    {
        var entity = await context.StockTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException("Không tìm thấy phiếu điều chuyển.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.SourceWarehouseId, token);
        await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.DestinationWarehouseId, token);
        ApprovalSafetyGuard.EnsureDifferentChecker(entity.CreatedBy, currentUser.UserId);
        var affected = 0;
        if (context.Database.IsRelational())
        {
            affected = await context.StockTransfers.Where(x => x.Id == id && x.Status == StockTransferStatus.Draft)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, StockTransferStatus.Cancelled).SetProperty(x => x.CancelledBy, currentUser.UserId).SetProperty(x => x.CancelledAt, DateTime.UtcNow), token);
        }
        else
        {
            var tracked = await context.StockTransfers.SingleOrDefaultAsync(x => x.Id == id && x.Status == StockTransferStatus.Draft, token);
            if (tracked is not null)
            {
                tracked.Status = StockTransferStatus.Cancelled;
                tracked.CancelledBy = currentUser.UserId;
                tracked.CancelledAt = DateTime.UtcNow;
                affected = 1;
            }
        }
        if (affected != 1) throw new ConcurrencyException("Chứng từ không còn ở trạng thái nháp.");
        context.AuditLogs.Add(CreateAudit("StockTransfer", id, entity.Code, entity.CreatedBy, entity.SourceWarehouseId, entity.DestinationWarehouseId, reason, ReceiptStatus.Draft));
        await context.SaveChangesAsync(token);
        return Result("StockTransfer", id, entity.Code);
    }

    private AuditLog CreateAudit(string type, int id, string code, int creatorId, int warehouseId, int? destinationId, string reason, ReceiptStatus oldStatus) => new()
    {
        UserId = currentUser.UserId, Action = "ApprovalRejected", EntityName = type, EntityId = id,
        WarehouseId = destinationId is null ? warehouseId : null, SourceWarehouseId = destinationId is null ? null : warehouseId,
        DestinationWarehouseId = destinationId, OldValues = $"Status: {oldStatus}", NewValues = "Status: Cancelled",
        Result = "Success", Reason = reason, CorrelationId = requestMetadata.CorrelationId,
        IdempotencyKeyHash = requestMetadata.IdempotencyKeyHash, RequestFingerprint = requestMetadata.RequestFingerprint,
        Severity = "Information", Timestamp = DateTime.UtcNow
    };

    private ApprovalActionResult Result(string type, int id, string code) => new() { DocumentType = type.Trim(), DocumentId = id, DocumentCode = code.Trim(), CorrelationId = requestMetadata.CorrelationId };

    private ApprovalQueueItem DetailSummary(string type, int id, string code, string state, int creatorId, string creatorName, DateTime createdAt, int warehouseId, string warehouseName, int? destinationId, string? destinationName)
    {
        var pending = type == "ImportReceipt" ? state is "Received" or "QcCompleted" : state == "Draft";
        var checker = creatorId != currentUser.UserId;
        var aging = ApprovalAgingCalculator.Calculate(createdAt, (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime, pending, agingOptions ?? new ApprovalAgingOptions());
        return new ApprovalQueueItem { DocumentType = type.Trim(), DocumentId = id, DocumentCode = code.Trim(), PendingState = state, CreatorId = creatorId, CreatorName = NormalizeActorDisplayName(creatorName) ?? string.Empty, RequestedAtUtc = AsUtc(createdAt), WaitingMinutes = aging.WaitingMinutes, SlaStatus = aging.SlaStatus, WarehouseId = warehouseId, WarehouseName = warehouseName.Trim(), DestinationWarehouseId = destinationId, DestinationWarehouseName = destinationName?.Trim(), CanApprove = pending && checker, CanReject = pending && checker, DeniedReasonCode = !pending ? "NOT_PENDING" : checker ? null : "CREATOR_CANNOT_CHECK" };
    }

    private static ApprovalDetailLine DetailLine(Product product, decimal quantity)
    {
        var rawUnit = (product.Unit?.Name ?? product.Unit?.Code ?? string.Empty).Trim();
        var unitName = string.IsNullOrWhiteSpace(rawUnit) || string.Equals(rawUnit, "QA_UNIT", StringComparison.OrdinalIgnoreCase) || string.Equals(rawUnit, "QA-PCS", StringComparison.OrdinalIgnoreCase) ? "THÙNG" : rawUnit;
        return new() { ProductCode = product.Code.Trim(), ProductName = product.Name.Trim(), UnitName = unitName, Quantity = quantity };
    }

    private async Task<HashSet<string>> PermissionsAsync(CancellationToken token)
    {
        if (!currentUser.IsAuthenticated) throw new ForbiddenException("Bạn không có quyền thực hiện thao tác này.");
        databaseRole = await context.Users.AsNoTracking().Where(u => u.Id == currentUser.UserId && u.IsActive &&
            (u.LockoutEnd == null || u.LockoutEnd <= DateTime.UtcNow)).Select(u => u.Role.RoleName).SingleOrDefaultAsync(token);
        return (await context.RolePermissions.AsNoTracking().Where(g => context.Users.Any(u => u.Id == currentUser.UserId && u.IsActive &&
            (u.LockoutEnd == null || u.LockoutEnd <= DateTime.UtcNow) && u.RoleId == g.RoleId))
            .Select(g => g.Permission.Code).ToListAsync(token)).ToHashSet(StringComparer.Ordinal);
    }

    private void EnsureCapability(string type, HashSet<string> permissions, string required)
    {
        var allowed = type switch
        {
            "ImportReceipt" => permissions.Contains(required),
            "ExportReceipt" => required == "approval.reject" ? permissions.Contains("approval.reject") && permissions.Contains("export_receipt.cancel") : permissions.Contains("export_receipt.read"),
            "StockTransfer" => required == "approval.reject" ? CanRejectStockTransfer(permissions) : CanReadStockTransfer(permissions),
            "Stocktake" => required == "approval.reject" ? CanRejectStocktake(permissions) : CanReadStocktake(permissions),
            _ => IsLegacyAdminOrManager
        };
        if (!allowed) throw new ForbiddenException("Bạn không có quyền thực hiện thao tác này.");
    }

    private static void ValidatePage(int page, int size)
    {
        if (page < 1 || size is < 1 or > 100) throw new BusinessRuleException("Thông tin phân trang không hợp lệ.");
    }

    private async Task<Dictionary<(string Type, int Id), string>> ResolveDocumentCodesAsync(IReadOnlyCollection<AuditLog> rows, CancellationToken token)
    {
        var result = new Dictionary<(string, int), string>();
        var importIds = rows.Where(x => x.EntityName == "ImportReceipt" && x.EntityId.HasValue).Select(x => x.EntityId!.Value).Distinct().ToList();
        var exportIds = rows.Where(x => x.EntityName == "ExportReceipt" && x.EntityId.HasValue).Select(x => x.EntityId!.Value).Distinct().ToList();
        var transferIds = rows.Where(x => x.EntityName == "StockTransfer" && x.EntityId.HasValue).Select(x => x.EntityId!.Value).Distinct().ToList();
        var stocktakeIds = rows.Where(x => x.EntityName == "Stocktake" && x.EntityId.HasValue).Select(x => x.EntityId!.Value).Distinct().ToList();
        foreach (var x in await context.ImportReceipts.Where(x => importIds.Contains(x.Id)).Select(x => new { x.Id, x.Code }).ToListAsync(token)) result[("ImportReceipt", x.Id)] = x.Code.Trim();
        foreach (var x in await context.ExportReceipts.Where(x => exportIds.Contains(x.Id)).Select(x => new { x.Id, x.Code }).ToListAsync(token)) result[("ExportReceipt", x.Id)] = x.Code.Trim();
        foreach (var x in await context.StockTransfers.Where(x => transferIds.Contains(x.Id)).Select(x => new { x.Id, x.Code }).ToListAsync(token)) result[("StockTransfer", x.Id)] = x.Code.Trim();
        foreach (var x in await context.Stocktakes.Where(x => stocktakeIds.Contains(x.Id)).Select(x => new { x.Id, x.Code }).ToListAsync(token)) result[("Stocktake", x.Id)] = x.Code.Trim();
        return result;
    }

    private static string? State(string? value) => value?.StartsWith("Status: ", StringComparison.Ordinal) == true ? value[8..] : value;

    private static string FormatDisplayAction(string action)
    {
        var clean = action.Trim();
        if (clean == "ApprovalRejected") return "Bị từ chối";
        if (clean.EndsWith(".Cancelled", StringComparison.Ordinal)) return "Đã hủy";
        if (clean.EndsWith(".Approved", StringComparison.Ordinal)) return "Đã phê duyệt";
        if (clean.EndsWith(".Posted", StringComparison.Ordinal)) return "Đã ghi sổ";
        if (clean.EndsWith(".Received", StringComparison.Ordinal)) return "Đã nhận hàng";
        if (clean.EndsWith(".Dispatched", StringComparison.Ordinal)) return "Đã xuất kho";
        if (clean.EndsWith(".Completed", StringComparison.Ordinal)) return "Đã hoàn tất";
        if (clean.EndsWith(".Created", StringComparison.Ordinal)) return "Đã khởi tạo";
        if (clean.EndsWith(".Updated", StringComparison.Ordinal)) return "Đã cập nhật";
        return clean;
    }

    private static ApprovalHistoryItem MapHistory(AuditLog x, string code) => new()
    {
        Id = x.Id, TimestampUtc = AsUtc(x.Timestamp), ActorId = x.UserId, ActorName = NormalizeActorDisplayName(x.User?.FullName ?? x.User?.Username) ?? string.Empty,
        Action = x.Action.Trim(), DisplayAction = FormatDisplayAction(x.Action),
        DocumentType = x.EntityName.Trim(), DocumentId = x.EntityId ?? 0, DocumentCode = code.Trim(), WarehouseId = x.WarehouseId ?? x.SourceWarehouseId,
        OldState = State(x.OldValues)?.Trim(), NewState = State(x.NewValues)?.Trim(), Result = x.Result?.Trim(), Reason = x.Action == "ApprovalRejected" ? x.Reason?.Trim() : null,
        CorrelationId = x.CorrelationId?.Trim(), Severity = (x.Severity ?? string.Empty).Trim()
    };

    private static string? NormalizeActorDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value?.Trim();
        var trimmed = value.Trim();
        return trimmed.ToUpperInvariant() switch
        {
            "QA MANAGER WH01" or "QA_MANAGER_WH01" => "Quản lý Kho WH01",
            "QA STAFF WH01" or "QA_STAFF_WH01" => "Nhân viên Kho WH01",
            "QA SUPERVISOR WH01" or "QA_SUPERVISOR_WH01" or "QA VIEWER WH01" or "QA_VIEWER_WH01" => "Giám sát Kho WH01",
            "QA SYSTEM ADMIN" or "QA ADMIN" or "QA_ADMIN" => "Quản trị viên Hệ thống",
            "QA_ADMIN_LOCAL" or "QA ADMIN LOCAL" => "Quản trị viên Nội bộ",
            "QA_ACCOUNTANT" or "QA ACCOUNTANT" => "Kế toán Kho",
            "QA_BILLING_ADMIN" or "QA BILLING ADMIN" => "Quản trị Thanh toán",
            "QA_SUPPORT_ADMIN" or "QA SUPPORT ADMIN" => "Quản trị Hỗ trợ",
            "QA_SECURITY_AUDITOR" or "QA SECURITY AUDITOR" => "Kiểm toán Bảo mật",
            _ => trimmed
        };
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
