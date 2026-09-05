using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Security;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class ApprovalWorkflowService(
    ErpKhoDbContext context,
    ICurrentUser currentUser,
    IWarehouseAuthorizationService warehouseAuthorization,
    IRequestMetadata requestMetadata) : IApprovalWorkflowService
{
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
    }

    public async Task<PagedResult<ApprovalQueueItem>> GetQueueAsync(ApprovalQueueQuery request, CancellationToken cancellationToken = default)
    {
        EnsureChecker();
        ValidatePage(request.PageIndex, request.PageSize);
        var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        var imports = context.ImportReceipts.AsNoTracking().Where(x => x.Status == ReceiptStatus.Draft && allowed.Contains(x.WarehouseId))
            .Select(x => new QueueRow { Type = "ImportReceipt", Id = x.Id, Code = x.Code, CreatorId = x.CreatedBy, CreatorName = x.CreatedByUser.FullName ?? x.CreatedByUser.Username, CreatedAt = x.CreatedAt, WarehouseId = x.WarehouseId, WarehouseName = x.Warehouse.Name, DestinationWarehouseId = null, DestinationWarehouseName = null, TotalQuantity = x.Details.Sum(d => d.Quantity) });
        var exports = context.ExportReceipts.AsNoTracking().Where(x => x.Status == ReceiptStatus.Draft && allowed.Contains(x.WarehouseId))
            .Select(x => new QueueRow { Type = "ExportReceipt", Id = x.Id, Code = x.Code, CreatorId = x.CreatedBy, CreatorName = x.CreatedByUser.FullName ?? x.CreatedByUser.Username, CreatedAt = x.CreatedAt, WarehouseId = x.WarehouseId, WarehouseName = x.Warehouse.Name, DestinationWarehouseId = null, DestinationWarehouseName = null, TotalQuantity = x.Details.Sum(d => d.Quantity) });
        var transfers = context.StockTransfers.AsNoTracking().Where(x => x.Status == StockTransferStatus.Draft && allowed.Contains(x.SourceWarehouseId) && allowed.Contains(x.DestinationWarehouseId))
            .Select(x => new QueueRow { Type = "StockTransfer", Id = x.Id, Code = x.Code, CreatorId = x.CreatedBy, CreatorName = x.CreatedByUser.FullName ?? x.CreatedByUser.Username, CreatedAt = x.CreatedAt, WarehouseId = x.SourceWarehouseId, WarehouseName = x.SourceWarehouse.Name, DestinationWarehouseId = x.DestinationWarehouseId, DestinationWarehouseName = x.DestinationWarehouse.Name, TotalQuantity = x.Details.Sum(d => d.RequestedQuantity) });
        var stocktakes = context.Stocktakes.AsNoTracking().Where(x => x.Status == ReceiptStatus.Draft && allowed.Contains(x.WarehouseId))
            .Select(x => new QueueRow { Type = "Stocktake", Id = x.Id, Code = x.Code, CreatorId = x.CreatedBy, CreatorName = x.CreatedByUser.FullName ?? x.CreatedByUser.Username, CreatedAt = x.CreatedAt, WarehouseId = x.WarehouseId, WarehouseName = x.Warehouse.Name, DestinationWarehouseId = null, DestinationWarehouseName = null, TotalQuantity = x.Details.Sum(d => d.ActualQuantity ?? d.SystemQuantity) });
        var query = imports.Concat(exports).Concat(transfers).Concat(stocktakes);
        if (!string.IsNullOrWhiteSpace(request.DocumentType)) query = query.Where(x => x.Type == request.DocumentType.Trim());
        if (request.WarehouseId.HasValue) query = query.Where(x => x.WarehouseId == request.WarehouseId || x.DestinationWarehouseId == request.WarehouseId);
        if (request.CreatorId.HasValue) query = query.Where(x => x.CreatorId == request.CreatorId);
        if (request.FromUtc.HasValue) query = query.Where(x => x.CreatedAt >= request.FromUtc);
        if (request.ToUtc.HasValue) query = query.Where(x => x.CreatedAt <= request.ToUtc);
        if (!string.IsNullOrWhiteSpace(request.Keyword)) { var keyword = request.Keyword.Trim(); query = query.Where(x => x.Code.Contains(keyword)); }
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
            TotalRecords = total, PageIndex = request.PageIndex, PageSize = request.PageSize,
            Items = rows.Select(x => new ApprovalQueueItem
            {
                DocumentType = x.Type, DocumentId = x.Id, DocumentCode = x.Code, CreatorId = x.CreatorId,
                CreatorName = x.CreatorName, RequestedAtUtc = AsUtc(x.CreatedAt), WarehouseId = x.WarehouseId,
                WarehouseName = x.WarehouseName, DestinationWarehouseId = x.DestinationWarehouseId,
                DestinationWarehouseName = x.DestinationWarehouseName, TotalQuantity = x.TotalQuantity,
                CanApprove = x.CreatorId != currentUser.UserId, CanReject = x.CreatorId != currentUser.UserId,
                DeniedReasonCode = x.CreatorId == currentUser.UserId ? "CREATOR_CANNOT_CHECK" : null
            }).ToList()
        };
    }

    public async Task<PagedResult<ApprovalHistoryItem>> GetHistoryAsync(ApprovalHistoryQuery request, CancellationToken cancellationToken = default)
    {
        EnsureChecker(); ValidatePage(request.PageIndex, request.PageSize);
        var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        var names = new[] { "ImportReceipt", "ExportReceipt", "StockTransfer", "Stocktake" };
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
        return new PagedResult<ApprovalHistoryItem> { TotalRecords = total, PageIndex = request.PageIndex, PageSize = request.PageSize, Items = rows.Select(x => MapHistory(x, codes.GetValueOrDefault((x.EntityName, x.EntityId ?? 0), string.Empty))).ToList() };
    }

    public async Task<ApprovalDetail> GetDetailAsync(string documentType, int id, CancellationToken cancellationToken = default)
    {
        EnsureChecker();
        var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        ApprovalQueueItem summary;
        string? note;
        IReadOnlyList<ApprovalDetailLine> lines;
        if (documentType == "ImportReceipt")
        {
            var x = await context.ImportReceipts.AsNoTracking().Include(x => x.Warehouse).Include(x => x.CreatedByUser).Include(x => x.Details).ThenInclude(x => x.Product).ThenInclude(x => x.Unit)
                .SingleOrDefaultAsync(x => x.Id == id && allowed.Contains(x.WarehouseId), cancellationToken) ?? throw new NotFoundException("Không tìm thấy chứng từ.");
            summary = DetailSummary(documentType, x.Id, x.Code, x.Status.ToString(), x.CreatedBy, x.CreatedByUser.FullName ?? x.CreatedByUser.Username, x.CreatedAt, x.WarehouseId, x.Warehouse.Name, null, null);
            note = x.Note; lines = x.Details.Select(d => DetailLine(d.Product, d.Quantity)).ToList();
        }
        else if (documentType == "ExportReceipt")
        {
            var x = await context.ExportReceipts.AsNoTracking().Include(x => x.Warehouse).Include(x => x.CreatedByUser).Include(x => x.Details).ThenInclude(x => x.Product).ThenInclude(x => x.Unit)
                .SingleOrDefaultAsync(x => x.Id == id && allowed.Contains(x.WarehouseId), cancellationToken) ?? throw new NotFoundException("Không tìm thấy chứng từ.");
            summary = DetailSummary(documentType, x.Id, x.Code, x.Status.ToString(), x.CreatedBy, x.CreatedByUser.FullName ?? x.CreatedByUser.Username, x.CreatedAt, x.WarehouseId, x.Warehouse.Name, null, null);
            note = x.Note; lines = x.Details.Select(d => DetailLine(d.Product, d.Quantity)).ToList();
        }
        else if (documentType == "Stocktake")
        {
            var x = await context.Stocktakes.AsNoTracking().Include(x => x.Warehouse).Include(x => x.CreatedByUser).Include(x => x.Details).ThenInclude(x => x.Product).ThenInclude(x => x.Unit)
                .SingleOrDefaultAsync(x => x.Id == id && allowed.Contains(x.WarehouseId), cancellationToken) ?? throw new NotFoundException("Không tìm thấy chứng từ.");
            summary = DetailSummary(documentType, x.Id, x.Code, x.Status.ToString(), x.CreatedBy, x.CreatedByUser.FullName ?? x.CreatedByUser.Username, x.CreatedAt, x.WarehouseId, x.Warehouse.Name, null, null);
            note = x.Note; lines = x.Details.Select(d => DetailLine(d.Product, d.ActualQuantity ?? d.SystemQuantity)).ToList();
        }
        else if (documentType == "StockTransfer")
        {
            var x = await context.StockTransfers.AsNoTracking().Include(x => x.SourceWarehouse).Include(x => x.DestinationWarehouse).Include(x => x.CreatedByUser).Include(x => x.Details).ThenInclude(x => x.Product).ThenInclude(x => x.Unit)
                .SingleOrDefaultAsync(x => x.Id == id && allowed.Contains(x.SourceWarehouseId) && allowed.Contains(x.DestinationWarehouseId), cancellationToken) ?? throw new NotFoundException("Không tìm thấy chứng từ.");
            summary = DetailSummary(documentType, x.Id, x.Code, x.Status.ToString(), x.CreatedBy, x.CreatedByUser.FullName ?? x.CreatedByUser.Username, x.CreatedAt, x.SourceWarehouseId, x.SourceWarehouse.Name, x.DestinationWarehouseId, x.DestinationWarehouse.Name);
            note = x.Note; lines = x.Details.Select(d => DetailLine(d.Product, d.RequestedQuantity)).ToList();
        }
        else throw new BusinessRuleException("Loại chứng từ không hợp lệ.");
        var history = await GetHistoryAsync(new ApprovalHistoryQuery { DocumentType = documentType, DocumentId = id, PageSize = 20 }, cancellationToken);
        return new ApprovalDetail { Summary = summary, Note = note, Lines = lines, History = history.Items };
    }

    public async Task<ApprovalActionResult> RejectAsync(string documentType, int id, string reason, CancellationToken cancellationToken = default)
    {
        EnsureChecker(); reason = ApprovalRejectReasonPolicy.Normalize(reason);
        var ownsTransaction = context.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var result = documentType switch
            {
                "ImportReceipt" => await RejectReceiptAsync(documentType, id, reason, context.ImportReceipts, cancellationToken),
                "ExportReceipt" => await RejectReceiptAsync(documentType, id, reason, context.ExportReceipts, cancellationToken),
                "Stocktake" => await RejectReceiptAsync(documentType, id, reason, context.Stocktakes, cancellationToken),
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

    private async Task<ApprovalActionResult> RejectReceiptAsync<TEntity>(string type, int id, string reason, DbSet<TEntity> set, CancellationToken token) where TEntity : class
    {
        var entity = await set.SingleOrDefaultAsync(x => EF.Property<int>(x, "Id") == id, token) ?? throw new NotFoundException("Không tìm thấy chứng từ.");
        var warehouseId = (int)entity.GetType().GetProperty("WarehouseId")!.GetValue(entity)!;
        var creatorId = (int)entity.GetType().GetProperty("CreatedBy")!.GetValue(entity)!;
        var code = (string)entity.GetType().GetProperty("Code")!.GetValue(entity)!;
        await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId, token);
        ApprovalSafetyGuard.EnsureDifferentChecker(creatorId, currentUser.UserId);
        var affected = await set.Where(x => EF.Property<int>(x, "Id") == id && EF.Property<ReceiptStatus>(x, "Status") == ReceiptStatus.Draft)
            .ExecuteUpdateAsync(s => s.SetProperty(x => EF.Property<ReceiptStatus>(x, "Status"), ReceiptStatus.Cancelled), token);
        if (affected != 1) throw new ConcurrencyException("Chứng từ không còn ở trạng thái nháp.");
        context.AuditLogs.Add(CreateAudit(type, id, code, creatorId, warehouseId, null, reason));
        await context.SaveChangesAsync(token);
        return Result(type, id, code);
    }

    private async Task<ApprovalActionResult> RejectTransferAsync(int id, string reason, CancellationToken token)
    {
        var entity = await context.StockTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException("Không tìm thấy phiếu điều chuyển.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.SourceWarehouseId, token);
        await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.DestinationWarehouseId, token);
        ApprovalSafetyGuard.EnsureDifferentChecker(entity.CreatedBy, currentUser.UserId);
        var affected = await context.StockTransfers.Where(x => x.Id == id && x.Status == StockTransferStatus.Draft)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, StockTransferStatus.Cancelled).SetProperty(x => x.CancelledBy, currentUser.UserId).SetProperty(x => x.CancelledAt, DateTime.UtcNow), token);
        if (affected != 1) throw new ConcurrencyException("Chứng từ không còn ở trạng thái nháp.");
        context.AuditLogs.Add(CreateAudit("StockTransfer", id, entity.Code, entity.CreatedBy, entity.SourceWarehouseId, entity.DestinationWarehouseId, reason));
        await context.SaveChangesAsync(token);
        return Result("StockTransfer", id, entity.Code);
    }

    private AuditLog CreateAudit(string type, int id, string code, int creatorId, int warehouseId, int? destinationId, string reason) => new()
    {
        UserId = currentUser.UserId, Action = "ApprovalRejected", EntityName = type, EntityId = id,
        WarehouseId = destinationId is null ? warehouseId : null, SourceWarehouseId = destinationId is null ? null : warehouseId,
        DestinationWarehouseId = destinationId, OldValues = "Status: Draft", NewValues = "Status: Cancelled",
        Result = "Success", Reason = reason, CorrelationId = requestMetadata.CorrelationId,
        IdempotencyKeyHash = requestMetadata.IdempotencyKeyHash, RequestFingerprint = requestMetadata.RequestFingerprint,
        Severity = "Information", Timestamp = DateTime.UtcNow
    };
    private ApprovalActionResult Result(string type, int id, string code) => new() { DocumentType = type, DocumentId = id, DocumentCode = code, CorrelationId = requestMetadata.CorrelationId };
    private ApprovalQueueItem DetailSummary(string type, int id, string code, string state, int creatorId, string creatorName, DateTime createdAt, int warehouseId, string warehouseName, int? destinationId, string? destinationName)
    {
        var pending = state == "Draft"; var checker = creatorId != currentUser.UserId;
        return new ApprovalQueueItem { DocumentType = type, DocumentId = id, DocumentCode = code, PendingState = state, CreatorId = creatorId, CreatorName = creatorName, RequestedAtUtc = AsUtc(createdAt), WarehouseId = warehouseId, WarehouseName = warehouseName, DestinationWarehouseId = destinationId, DestinationWarehouseName = destinationName, CanApprove = pending && checker, CanReject = pending && checker, DeniedReasonCode = !pending ? "NOT_PENDING" : checker ? null : "CREATOR_CANNOT_CHECK" };
    }
    private static ApprovalDetailLine DetailLine(Product product, decimal quantity) => new() { ProductCode = product.Code, ProductName = product.Name, UnitName = product.Unit.Name, Quantity = quantity };
    private void EnsureChecker() { if (!currentUser.IsAuthenticated || (!currentUser.IsGlobalAdmin && currentUser.Role != "Manager")) throw new ForbiddenException("Bạn không có quyền phê duyệt chứng từ."); }
    private static void ValidatePage(int page, int size) { if (page < 1 || size is < 1 or > 100) throw new BusinessRuleException("Thông tin phân trang không hợp lệ."); }
    private async Task<Dictionary<(string Type, int Id), string>> ResolveDocumentCodesAsync(IReadOnlyCollection<AuditLog> rows, CancellationToken token)
    {
        var result = new Dictionary<(string, int), string>();
        var importIds = rows.Where(x => x.EntityName == "ImportReceipt" && x.EntityId.HasValue).Select(x => x.EntityId!.Value).Distinct().ToList();
        var exportIds = rows.Where(x => x.EntityName == "ExportReceipt" && x.EntityId.HasValue).Select(x => x.EntityId!.Value).Distinct().ToList();
        var transferIds = rows.Where(x => x.EntityName == "StockTransfer" && x.EntityId.HasValue).Select(x => x.EntityId!.Value).Distinct().ToList();
        var stocktakeIds = rows.Where(x => x.EntityName == "Stocktake" && x.EntityId.HasValue).Select(x => x.EntityId!.Value).Distinct().ToList();
        foreach (var x in await context.ImportReceipts.Where(x => importIds.Contains(x.Id)).Select(x => new { x.Id, x.Code }).ToListAsync(token)) result[("ImportReceipt", x.Id)] = x.Code;
        foreach (var x in await context.ExportReceipts.Where(x => exportIds.Contains(x.Id)).Select(x => new { x.Id, x.Code }).ToListAsync(token)) result[("ExportReceipt", x.Id)] = x.Code;
        foreach (var x in await context.StockTransfers.Where(x => transferIds.Contains(x.Id)).Select(x => new { x.Id, x.Code }).ToListAsync(token)) result[("StockTransfer", x.Id)] = x.Code;
        foreach (var x in await context.Stocktakes.Where(x => stocktakeIds.Contains(x.Id)).Select(x => new { x.Id, x.Code }).ToListAsync(token)) result[("Stocktake", x.Id)] = x.Code;
        return result;
    }
    private static string? State(string? value) => value?.StartsWith("Status: ", StringComparison.Ordinal) == true ? value[8..] : value;
    private static ApprovalHistoryItem MapHistory(AuditLog x, string code) => new()
    {
        Id = x.Id, TimestampUtc = AsUtc(x.Timestamp), ActorId = x.UserId, ActorName = x.User?.FullName ?? x.User?.Username ?? string.Empty,
        Action = x.Action, DisplayAction = x.Action == "ApprovalRejected" ? "Bị từ chối" : x.Action.EndsWith(".Cancelled") ? "Đã hủy" : x.Action,
        DocumentType = x.EntityName, DocumentId = x.EntityId ?? 0, DocumentCode = code, WarehouseId = x.WarehouseId ?? x.SourceWarehouseId,
        OldState = State(x.OldValues), NewState = State(x.NewValues), Result = x.Result, Reason = x.Action == "ApprovalRejected" ? x.Reason : null,
        CorrelationId = x.CorrelationId, Severity = x.Severity
    };

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
