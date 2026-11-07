using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Options;
using ERP.Application.Security;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ApprovalWorkflowService(
    ErpKhoDbContext context,
    ICurrentUser currentUser,
    IWarehouseAuthorizationService warehouseAuthorization,
    IRequestMetadata requestMetadata,
    TimeProvider? timeProvider = null,
    ApprovalAgingOptions? agingOptions = null,
    IReceiptInventoryIdentityService? receiptIdentityService = null,
    IReceiptInboundPlanningIntegration? inboundPlanningIntegration = null) : IApprovalWorkflowService
{
    private string? databaseRole;

    private bool IsLegacyAdminOrManager => currentUser.Role is "Admin" or "Manager";

    private bool CanReadStockTransfer(HashSet<string> p) =>
        p.Contains("inventory.read") || p.Contains("inventory_movement.create") || p.Contains("approval.approve") || IsLegacyAdminOrManager;

    private bool CanReadStocktake(HashSet<string> p) =>
        p.Contains("stocktake.read") || p.Contains("stocktake.manage") || p.Contains("inventory.read") || p.Contains("approval.approve") || IsLegacyAdminOrManager;

    private bool CanApproveStockTransfer(HashSet<string> p) =>
        p.Contains("inventory_movement.create") || p.Contains("approval.approve") || IsLegacyAdminOrManager;

    private bool CanRejectStockTransfer(HashSet<string> p) =>
        p.Contains("approval.reject") || p.Contains("inventory_movement.create") || IsLegacyAdminOrManager;

    private bool CanApproveStocktake(HashSet<string> p) =>
        p.Contains("stocktake.manage") || p.Contains("inventory.write") || p.Contains("approval.approve") || IsLegacyAdminOrManager;

    private bool CanRejectStocktake(HashSet<string> p) =>
        p.Contains("approval.reject") || p.Contains("stocktake.manage") || p.Contains("inventory.write") || IsLegacyAdminOrManager;

    private sealed class QueueRow
    {
        public string Type { get; set; } = string.Empty;
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public int CreatorId { get; set; }
        public string CreatorName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public int? DestinationWarehouseId { get; set; }
        public string? DestinationWarehouseName { get; set; }
        public decimal TotalQuantity { get; set; }
        public bool QcCompleted { get; set; }
    }

    public async Task<PagedResult<ApprovalQueueItem>> GetQueueAsync(ApprovalQueueQuery request, CancellationToken cancellationToken = default)
    {
        var permissions = await PermissionsAsync(cancellationToken);
        var inboundRead = permissions.Contains("receipt.read");
        var outboundRead = permissions.Contains("export_receipt.read");
        var transferRead = CanReadStockTransfer(permissions);
        var stocktakeRead = CanReadStocktake(permissions);
        if (!inboundRead && !outboundRead && !transferRead && !stocktakeRead)
            throw new ForbiddenException("Bạn không có quyền thực hiện thao tác này.");

        ValidatePage(request.PageIndex, request.PageSize);
        var nowUtc = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var options = agingOptions ?? new ApprovalAgingOptions();
        options.Validate();
        var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        var imports = context.ImportReceipts.AsNoTracking().Where(x => (x.Status == ReceiptStatus.Received || x.Status == ReceiptStatus.QcCompleted) && allowed.Contains(x.WarehouseId))
            .Select(x => new QueueRow { Type = "ImportReceipt", Id = x.Id, Code = x.Code, CreatorId = x.CreatedBy, CreatorName = x.CreatedByUser.FullName ?? x.CreatedByUser.Username, CreatedAt = x.CreatedAt, WarehouseId = x.WarehouseId, WarehouseName = x.Warehouse.Name, DestinationWarehouseId = null, DestinationWarehouseName = null, TotalQuantity = x.Details.Sum(d => d.ReceivedQuantity), QcCompleted = x.Status == ReceiptStatus.QcCompleted });
        var exports = context.ExportReceipts.AsNoTracking().Where(x => x.Status == ReceiptStatus.Draft && allowed.Contains(x.WarehouseId))
            .Select(x => new QueueRow { Type = "ExportReceipt", Id = x.Id, Code = x.Code, CreatorId = x.CreatedBy, CreatorName = x.CreatedByUser.FullName ?? x.CreatedByUser.Username, CreatedAt = x.CreatedAt, WarehouseId = x.WarehouseId, WarehouseName = x.Warehouse.Name, DestinationWarehouseId = null, DestinationWarehouseName = null, TotalQuantity = x.Details.Sum(d => d.Quantity), QcCompleted = false });
        var transfers = context.StockTransfers.AsNoTracking().Where(x => x.Status == StockTransferStatus.Draft && allowed.Contains(x.SourceWarehouseId) && allowed.Contains(x.DestinationWarehouseId))
            .Select(x => new QueueRow { Type = "StockTransfer", Id = x.Id, Code = x.Code, CreatorId = x.CreatedBy, CreatorName = x.CreatedByUser.FullName ?? x.CreatedByUser.Username, CreatedAt = x.CreatedAt, WarehouseId = x.SourceWarehouseId, WarehouseName = x.SourceWarehouse.Name, DestinationWarehouseId = x.DestinationWarehouseId, DestinationWarehouseName = x.DestinationWarehouse.Name, TotalQuantity = x.Details.Sum(d => d.RequestedQuantity), QcCompleted = false });
        var stocktakes = context.Stocktakes.AsNoTracking().Where(x => x.Status == StocktakeStatus.Draft && allowed.Contains(x.WarehouseId))
            .Select(x => new QueueRow { Type = "Stocktake", Id = x.Id, Code = x.Code, CreatorId = x.CreatedBy, CreatorName = x.CreatedByUser.FullName ?? x.CreatedByUser.Username, CreatedAt = x.CreatedAt, WarehouseId = x.WarehouseId, WarehouseName = x.Warehouse.Name, DestinationWarehouseId = null, DestinationWarehouseName = null, TotalQuantity = x.Note != null && x.Note.Contains("[BLIND]") ? x.Details.Sum(d => d.ActualQuantity ?? 0m) : x.Details.Sum(d => d.ActualQuantity ?? d.SystemQuantity), QcCompleted = false });

        var query = imports.Where(_ => inboundRead)
            .Concat(exports.Where(_ => outboundRead))
            .Concat(transfers.Where(_ => transferRead))
            .Concat(stocktakes.Where(_ => stocktakeRead));

        if (!string.IsNullOrWhiteSpace(request.DocumentType)) query = query.Where(x => x.Type == request.DocumentType.Trim());
        if (request.WarehouseId.HasValue) query = query.Where(x => x.WarehouseId == request.WarehouseId || x.DestinationWarehouseId == request.WarehouseId);
        if (request.CreatorId.HasValue) query = query.Where(x => x.CreatorId == request.CreatorId);
        if (request.FromUtc.HasValue) query = query.Where(x => x.CreatedAt >= request.FromUtc);
        if (request.ToUtc.HasValue) query = query.Where(x => x.CreatedAt <= request.ToUtc);
        if (!string.IsNullOrWhiteSpace(request.Keyword)) { var keyword = request.Keyword.Trim(); query = query.Where(x => x.Code.Contains(keyword)); }
        if (!string.IsNullOrWhiteSpace(request.SlaStatus))
        {
            var warningCutoff = nowUtc.AddHours(-options.WarningAfterHours);
            var overdueCutoff = nowUtc.AddHours(-options.OverdueAfterHours);
            query = request.SlaStatus.Trim() switch
            {
                ApprovalAgingCalculator.Normal => query.Where(x => x.CreatedAt > warningCutoff),
                ApprovalAgingCalculator.Warning => query.Where(x => x.CreatedAt <= warningCutoff && x.CreatedAt > overdueCutoff),
                ApprovalAgingCalculator.Overdue => query.Where(x => x.CreatedAt <= overdueCutoff),
                _ => throw new BusinessRuleException("Trạng thái SLA không hợp lệ.")
            };
        }

        var total = await query.CountAsync(cancellationToken);
        var ordered = request.SortBy switch
        {
            "DocumentCode" => request.SortDescending ? query.OrderByDescending(x => x.Code) : query.OrderBy(x => x.Code),
            "DocumentType" => request.SortDescending ? query.OrderByDescending(x => x.Type) : query.OrderBy(x => x.Type),
            "RequestedAt" => request.SortDescending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt),
            _ => throw new BusinessRuleException("Kiểu sắp xếp không hợp lệ.")
        };
        var rows = await ordered.ThenBy(x => x.Type).ThenBy(x => x.Code).ThenBy(x => x.Id)
            .Skip((request.PageIndex - 1) * request.PageSize).Take(request.PageSize).ToListAsync(cancellationToken);

        return new PagedResult<ApprovalQueueItem>
        {
            TotalRecords = total,
            PageIndex = request.PageIndex,
            PageSize = request.PageSize,
            Items = rows.Select(x =>
            {
                var aging = ApprovalAgingCalculator.Calculate(x.CreatedAt, nowUtc, true, options);
                return new ApprovalQueueItem
                {
                    DocumentType = x.Type.Trim(), DocumentId = x.Id, DocumentCode = x.Code.Trim(),
                    CreatorId = x.CreatorId, CreatorName = NormalizeActorDisplayName(x.CreatorName) ?? string.Empty,
                    RequestedAtUtc = AsUtc(x.CreatedAt), WarehouseId = x.WarehouseId,
                    WaitingMinutes = aging.WaitingMinutes, SlaStatus = aging.SlaStatus,
                    WarehouseName = x.WarehouseName.Trim(), DestinationWarehouseId = x.DestinationWarehouseId,
                    DestinationWarehouseName = x.DestinationWarehouseName?.Trim(), TotalQuantity = x.TotalQuantity,
                    PendingState = x.Type == "ImportReceipt" ? (x.QcCompleted ? "QcCompleted" : "Received") : "Draft",
                    CanApprove = x.CreatorId != currentUser.UserId && (x.Type switch
                    {
                        "ImportReceipt" => permissions.Contains("receipt.complete") && (!x.QcCompleted || permissions.Contains("quality_disposition.approve")),
                        "ExportReceipt" => permissions.Contains("export_receipt.approve"),
                        "StockTransfer" => CanApproveStockTransfer(permissions),
                        "Stocktake" => CanApproveStocktake(permissions),
                        _ => IsLegacyAdminOrManager
                    }),
                    CanReject = x.CreatorId != currentUser.UserId && (x.Type switch
                    {
                        "ImportReceipt" => permissions.Contains("approval.reject") && (!x.QcCompleted || permissions.Contains("quality_disposition.approve")),
                        "ExportReceipt" => permissions.Contains("approval.reject") && permissions.Contains("export_receipt.cancel"),
                        "StockTransfer" => CanRejectStockTransfer(permissions),
                        "Stocktake" => CanRejectStocktake(permissions),
                        _ => IsLegacyAdminOrManager
                    }),
                    DeniedReasonCode = x.CreatorId == currentUser.UserId ? "CREATOR_CANNOT_CHECK" : null
                };
            }).ToList()
        };
    }

    public async Task<PagedResult<ApprovalHistoryItem>> GetHistoryAsync(ApprovalHistoryQuery request, CancellationToken cancellationToken = default)
    {
        var permissions = await PermissionsAsync(cancellationToken);
        var inboundRead = permissions.Contains("receipt.read");
        var outboundRead = permissions.Contains("export_receipt.read");
        var transferRead = CanReadStockTransfer(permissions);
        var stocktakeRead = CanReadStocktake(permissions);
        if (!inboundRead && !outboundRead && !transferRead && !stocktakeRead)
            throw new ForbiddenException("Bạn không có quyền thực hiện thao tác này.");

        ValidatePage(request.PageIndex, request.PageSize);
        if (request.DocumentId.HasValue && request.DocumentType == "ImportReceipt")
        {
            if (!inboundRead) throw new ForbiddenException("Bạn không có quyền thực hiện thao tác này.");
            var receipt = await context.ImportReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.DocumentId.Value, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.");
            await warehouseAuthorization.EnsureWarehouseAccessAsync(receipt.WarehouseId, cancellationToken);
        }

        var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        var names = new[] { inboundRead ? "ImportReceipt" : "", outboundRead ? "ExportReceipt" : "", transferRead ? "StockTransfer" : "", stocktakeRead ? "Stocktake" : "" };
        var query = context.AuditLogs.AsNoTracking().Where(x => names.Contains(x.EntityName) &&
            ((x.WarehouseId.HasValue && allowed.Contains(x.WarehouseId.Value)) ||
             (x.SourceWarehouseId.HasValue && x.DestinationWarehouseId.HasValue && allowed.Contains(x.SourceWarehouseId.Value) && allowed.Contains(x.DestinationWarehouseId.Value))));
        if (!string.IsNullOrWhiteSpace(request.DocumentType)) query = query.Where(x => x.EntityName == request.DocumentType.Trim());
        if (request.DocumentId.HasValue) query = query.Where(x => x.EntityId == request.DocumentId);
        if (request.WarehouseId.HasValue) query = query.Where(x => x.WarehouseId == request.WarehouseId || x.SourceWarehouseId == request.WarehouseId || x.DestinationWarehouseId == request.WarehouseId);
        if (request.ActorId.HasValue) query = query.Where(x => x.UserId == request.ActorId);
        if (!string.IsNullOrWhiteSpace(request.Action)) query = query.Where(x => x.Action == request.Action.Trim());
        if (!string.IsNullOrWhiteSpace(request.Result)) query = query.Where(x => x.Result == request.Result.Trim());
        if (request.FromUtc.HasValue) query = query.Where(x => x.Timestamp >= request.FromUtc);
        if (request.ToUtc.HasValue) query = query.Where(x => x.Timestamp <= request.ToUtc);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.Include(x => x.User).OrderByDescending(x => x.Timestamp).ThenByDescending(x => x.Id)
            .Skip((request.PageIndex - 1) * request.PageSize).Take(request.PageSize).ToListAsync(cancellationToken);
        var codes = await ResolveDocumentCodesAsync(rows, cancellationToken);
        var items = rows.Select(x => MapHistory(x, codes.GetValueOrDefault((x.EntityName, x.EntityId ?? 0), string.Empty))).ToList();
        if (databaseRole == "Viewer")
        {
            foreach (var item in items.Where(i => i.DocumentType is "ImportReceipt" or "ExportReceipt"))
            {
                item.Reason = null;
                item.CorrelationId = null;
            }
        }
        return new PagedResult<ApprovalHistoryItem> { TotalRecords = total, PageIndex = request.PageIndex, PageSize = request.PageSize, Items = items };
    }

    public async Task<ApprovalDetail> GetDetailAsync(string documentType, int id, CancellationToken cancellationToken = default)
    {
        var permissions = await PermissionsAsync(cancellationToken);
        EnsureCapability(documentType, permissions, "receipt.read");
        var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        ApprovalQueueItem summary;
        string? note;
        IReadOnlyList<ApprovalDetailLine> lines;
        if (documentType == "ImportReceipt")
        {
            var x = await context.ImportReceipts.AsNoTracking().Include(x => x.Warehouse).Include(x => x.CreatedByUser).Include(x => x.Details).ThenInclude(x => x.Product).ThenInclude(x => x.Unit)
                .SingleOrDefaultAsync(x => x.Id == id && allowed.Contains(x.WarehouseId), cancellationToken) ?? throw new NotFoundException("Không tìm thấy chứng từ.");
            summary = DetailSummary(documentType, x.Id, x.Code, x.Status.ToString(), x.CreatedBy, x.CreatedByUser.FullName ?? x.CreatedByUser.Username, x.CreatedAt, x.WarehouseId, x.Warehouse.Name, null, null);
            note = x.Note;
            lines = x.Details.Select(d => DetailLine(d.Product, d.ReceivedQuantity)).ToList();
        }
        else if (documentType == "ExportReceipt")
        {
            var x = await context.ExportReceipts.AsNoTracking().Include(x => x.Warehouse).Include(x => x.CreatedByUser).Include(x => x.Details).ThenInclude(x => x.Product).ThenInclude(x => x.Unit)
                .SingleOrDefaultAsync(x => x.Id == id && allowed.Contains(x.WarehouseId), cancellationToken) ?? throw new NotFoundException("Không tìm thấy chứng từ.");
            summary = DetailSummary(documentType, x.Id, x.Code, x.Status.ToString(), x.CreatedBy, x.CreatedByUser.FullName ?? x.CreatedByUser.Username, x.CreatedAt, x.WarehouseId, x.Warehouse.Name, null, null);
            note = x.Note;
            lines = x.Details.Select(d => DetailLine(d.Product, d.Quantity)).ToList();
        }
        else if (documentType == "Stocktake")
        {
            var x = await context.Stocktakes.AsNoTracking().Include(x => x.Warehouse).Include(x => x.CreatedByUser).Include(x => x.Details).ThenInclude(x => x.Product).ThenInclude(x => x.Unit)
                .SingleOrDefaultAsync(x => x.Id == id && allowed.Contains(x.WarehouseId), cancellationToken) ?? throw new NotFoundException("Không tìm thấy chứng từ.");
            summary = DetailSummary(documentType, x.Id, x.Code, x.Status.ToString(), x.CreatedBy, x.CreatedByUser.FullName ?? x.CreatedByUser.Username, x.CreatedAt, x.WarehouseId, x.Warehouse.Name, null, null);
            var isBlindDraft = x.Status == StocktakeStatus.Draft && x.Note != null && x.Note.Contains("[BLIND]", StringComparison.OrdinalIgnoreCase);
            note = x.Note;
            lines = x.Details.Select(d => DetailLine(d.Product, isBlindDraft ? (d.ActualQuantity ?? 0m) : (d.ActualQuantity ?? d.SystemQuantity))).ToList();
        }
        else if (documentType == "StockTransfer")
        {
            var x = await context.StockTransfers.AsNoTracking().Include(x => x.SourceWarehouse).Include(x => x.DestinationWarehouse).Include(x => x.CreatedByUser).Include(x => x.Details).ThenInclude(x => x.Product).ThenInclude(x => x.Unit)
                .SingleOrDefaultAsync(x => x.Id == id && allowed.Contains(x.SourceWarehouseId) && allowed.Contains(x.DestinationWarehouseId), cancellationToken) ?? throw new NotFoundException("Không tìm thấy chứng từ.");
            summary = DetailSummary(documentType, x.Id, x.Code, x.Status.ToString(), x.CreatedBy, x.CreatedByUser.FullName ?? x.CreatedByUser.Username, x.CreatedAt, x.SourceWarehouseId, x.SourceWarehouse.Name, x.DestinationWarehouseId, x.DestinationWarehouse.Name);
            note = x.Note;
            lines = x.Details.Select(d => DetailLine(d.Product, d.RequestedQuantity)).ToList();
        }
        else throw new BusinessRuleException("Loại chứng từ không hợp lệ.");

        if (documentType == "ImportReceipt")
        {
            summary.CanApprove &= permissions.Contains("receipt.complete") && (summary.PendingState != "QcCompleted" || permissions.Contains("quality_disposition.approve"));
            summary.CanReject &= permissions.Contains("approval.reject") && (summary.PendingState != "QcCompleted" || permissions.Contains("quality_disposition.approve"));
            if (databaseRole == "Viewer") note = null;
        }
        else if (documentType == "ExportReceipt")
        {
            summary.CanApprove &= permissions.Contains("export_receipt.approve");
            summary.CanReject &= permissions.Contains("approval.reject") && permissions.Contains("export_receipt.cancel");
            if (databaseRole == "Viewer") note = null;
        }
        else if (documentType == "StockTransfer")
        {
            summary.CanApprove &= CanApproveStockTransfer(permissions);
            summary.CanReject &= CanRejectStockTransfer(permissions);
        }
        else if (documentType == "Stocktake")
        {
            summary.CanApprove &= CanApproveStocktake(permissions);
            summary.CanReject &= CanRejectStocktake(permissions);
        }

        var history = await GetHistoryAsync(new ApprovalHistoryQuery { DocumentType = documentType, DocumentId = id, PageSize = 20 }, cancellationToken);
        return new ApprovalDetail { Summary = summary, Note = note?.Trim(), Lines = lines, History = history.Items };
    }
}
