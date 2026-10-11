using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ERP.Application.Interfaces;
using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Infrastructure;

public sealed class IdempotentCommandFilter(
    string commandScope,
    ErpKhoDbContext context,
    IRequestMetadata metadata,
    IWarehouseAuthorizationService warehouseAuthorization,
    ERP.Application.Options.ExportReceiptOptions? exportOptions = null) : IAsyncActionFilter
{
    public const string HeaderName = "Idempotency-Key";
    private const int MaxStoredResponseLength = 65_536;

    public async Task OnActionExecutionAsync(ActionExecutingContext actionContext, ActionExecutionDelegate next)
    {
        if (!int.TryParse(actionContext.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            actionContext.Result = new UnauthorizedResult();
            return;
        }

        // Recheck target existence before both initial claims and successful terminal-state replay.
        if (commandScope is "User.Unlock" or "User.RevokeSessions" &&
            actionContext.ActionArguments.TryGetValue("userId", out var target) && target is int targetId &&
            !await context.Users.AsNoTracking().AnyAsync(u => u.Id == targetId, actionContext.HttpContext.RequestAborted))
        {
            actionContext.Result = new NotFoundObjectResult(new { message = "Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập." });
            return;
        }

        var rawKey = actionContext.HttpContext.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(rawKey) || rawKey.Length > 128)
        {
            actionContext.Result = new BadRequestObjectResult(new { message = "Idempotency-Key là bắt buộc và không được vượt quá 128 ký tự." });
            return;
        }

        var keyHash = Hash(rawKey);
        var fingerprint = Fingerprint(commandScope, actionContext.ActionArguments);
        metadata.IdempotencyKeyHash = keyHash;
        metadata.RequestFingerprint = fingerprint;
        await using var transaction = await context.Database.BeginTransactionAsync(actionContext.HttpContext.RequestAborted);
        await AuthorizeExportAsync(actionContext, userId, keyHash, fingerprint);
        await AuthorizeReservationAsync(actionContext, userId, keyHash, fingerprint);
        var record = new IdempotencyRecord
        {
            UserId = userId, CommandScope = commandScope, KeyHash = keyHash,
            RequestFingerprint = fingerprint, Status = IdempotencyStatus.Processing,
            CorrelationId = metadata.CorrelationId, CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(30)
        };
        context.IdempotencyRecords.Add(record);
        try
        {
            await context.SaveChangesAsync(actionContext.HttpContext.RequestAborted);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            var existing = await context.IdempotencyRecords.AsNoTracking().SingleAsync(
                x => x.UserId == userId && x.CommandScope == commandScope && x.KeyHash == keyHash,
                actionContext.HttpContext.RequestAborted);
            if (existing.RequestFingerprint != fingerprint || existing.Status != IdempotencyStatus.Completed)
            {
                actionContext.Result = new ConflictObjectResult(new { message = "Idempotency-Key đang được xử lý hoặc đã được dùng cho yêu cầu khác." });
                return;
            }
            await ReauthorizeWarehouses(existing, actionContext.HttpContext.RequestAborted);
            // Original audited transition, not today's receipt state, determines conditional QC replay rights.
            if (commandScope is "ImportReceipt.QcDisposition" or "ImportReceipt.Approve")
            {
                var auditAction = commandScope == "ImportReceipt.QcDisposition" ? "ImportReceipt.QcDispositionRecorded" : "ImportReceipt.Approved";
                var audit = await context.AuditLogs.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.CorrelationId == existing.CorrelationId && x.Action == auditAction && x.Result == "Success",
                    actionContext.HttpContext.RequestAborted);
                if (audit is null) throw new ForbiddenException("Bạn không có quyền thực hiện thao tác này.");
                var qcTransition = commandScope == "ImportReceipt.QcDisposition" ? audit.NewValues : audit.OldValues;
                if (qcTransition == "Status: QcCompleted")
                {
                    var permission = commandScope == "ImportReceipt.QcDisposition" ? "quality_inspection.complete" : "quality_disposition.approve";
                    if (!await context.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive &&
                        (u.LockoutEnd == null || u.LockoutEnd <= DateTime.UtcNow) && u.Role.Permissions.Any(p => p.Permission.Code == permission),
                        actionContext.HttpContext.RequestAborted))
                        throw new ForbiddenException("Bạn không có quyền thực hiện thao tác này.");
                }
            }
            var responseBody = existing.ResponseBody;
            // Replay preserves the effect, but response tokens follow the actor's current database classification/grants.
            if (commandScope == "StockReservation.Create" && !await context.Users.AsNoTracking().AnyAsync(u =>
                u.Id == userId && u.IsActive && (u.LockoutEnd == null || u.LockoutEnd <= DateTime.UtcNow) &&
                u.Role.RoleName != "Viewer" && u.Role.Permissions.Any(p => p.Permission.Code == "reservation.release"),
                actionContext.HttpContext.RequestAborted))
            {
                var body = JsonNode.Parse(responseBody!)!.AsObject();
                body.Remove("rowVersion");
                responseBody = body.ToJsonString();
            }
            actionContext.Result = new ContentResult { StatusCode = existing.ResponseStatusCode, ContentType = "application/json", Content = responseBody };
            return;
        }

        var executed = await next();
        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            var rejectedAudit = executed.Exception is ForbiddenException
                ? RejectionAudit(userId, actionContext.ActionArguments, executed.Exception.Message, TrackedWarehouses(actionContext.ActionArguments))
                : null;
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            if (rejectedAudit is not null)
            {
                context.AuditLogs.Add(rejectedAudit);
                await context.SaveChangesAsync(CancellationToken.None);
            }
            return;
        }

        var statusCode = ResultStatus(executed.Result);
        if (statusCode >= 500 || statusCode is 401 or 403 or 404 ||
            ((commandScope.StartsWith("ExportReceipt.", StringComparison.Ordinal) || commandScope.StartsWith("StockReservation.", StringComparison.Ordinal)) && statusCode >= 400))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            return;
        }

        var audits = context.AuditLogs.Local.Where(x => x.CorrelationId == metadata.CorrelationId).ToList();
        record.WarehouseId = audits.Select(x => x.WarehouseId).FirstOrDefault(x => x.HasValue);
        record.SourceWarehouseId = audits.Select(x => x.SourceWarehouseId).FirstOrDefault(x => x.HasValue);
        record.DestinationWarehouseId = audits.Select(x => x.DestinationWarehouseId).FirstOrDefault(x => x.HasValue);
        record.Status = IdempotencyStatus.Completed;
        record.ResponseStatusCode = statusCode;
        record.ResponseBody = SerializeResult(executed.Result);
        if (record.ResponseBody?.Length > MaxStoredResponseLength)
            throw new InvalidOperationException("Idempotent response exceeds the safe persistence limit.");
        record.CompletedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(actionContext.HttpContext.RequestAborted);
        await transaction.CommitAsync(actionContext.HttpContext.RequestAborted);
        context.ChangeTracker.Clear();
    }

    private async Task ReauthorizeWarehouses(IdempotencyRecord record, CancellationToken token)
    {
        foreach (var warehouseId in new[] { record.WarehouseId, record.SourceWarehouseId, record.DestinationWarehouseId }.Where(x => x.HasValue).Select(x => x!.Value).Distinct())
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId, token);
        // Bulk expiry can affect more warehouses than the three single-resource scope columns.
        if (record.CommandScope == "StockReservation.Expire")
        {
            var warehouses = await context.AuditLogs.AsNoTracking().Where(x => x.CorrelationId == record.CorrelationId && x.Action == "StockReservation.Released" && x.WarehouseId != null)
                .Select(x => x.WarehouseId!.Value).Distinct().ToListAsync(token);
            foreach (var id in warehouses) await warehouseAuthorization.EnsureWarehouseAccessAsync(id, token);
        }
    }

    private async Task AuthorizeReservationAsync(ActionExecutingContext action, int actor, string keyHash, string fingerprint)
    {
        if (!commandScope.StartsWith("StockReservation.", StringComparison.Ordinal)) return;
        var token = action.HttpContext.RequestAborted;
        if (commandScope == "StockReservation.Create")
        {
            var request = (ERP.Application.DTOs.CreateStockReservationDto)action.ActionArguments["request"]!;
            await warehouseAuthorization.EnsureWarehouseAccessAsync(request.WarehouseId, token);
            return;
        }
        if (commandScope != "StockReservation.Release") return;
        var id = (int)action.ActionArguments["id"]!;
        if (context.Database.IsSqlServer())
            await context.Database.SqlQuery<int>($"SELECT Id AS Value FROM StockReservations WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}").ToListAsync(token);
        var reservation = await context.StockReservations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(reservation.WarehouseId, token);
        if (reservation.SourceType != "Manual") throw new ERP.Domain.Exceptions.ConcurrencyException("Giữ hàng của phiếu xuất chỉ được xử lý qua phiếu xuất.");
        var existing = await context.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == actor && x.CommandScope == commandScope && x.KeyHash == keyHash, token);
        if (existing is not null)
        {
            if (existing.RequestFingerprint != fingerprint || existing.Status != IdempotencyStatus.Completed)
                throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
            return; // The original token may now be stale; successful replay still reauthorizes scope and capability.
        }
        var dto = (ERP.Application.DTOs.ReleaseStockReservationDto)action.ActionArguments["request"]!;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(dto.RowVersion ?? ""); }
        catch (FormatException) { throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại."); }
        if (bytes.Length != 8 || !bytes.SequenceEqual(reservation.RowVersion) || reservation.Status is not ERP.Domain.Enums.StockReservationStatus.Active and not ERP.Domain.Enums.StockReservationStatus.PartiallyConsumed)
            throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
    }

    private async Task AuthorizeExportAsync(ActionExecutingContext action, int actor, string keyHash, string fingerprint)
    {
        var exportReject = commandScope == "Approval.Reject" && action.ActionArguments.TryGetValue("documentType", out var documentType) && documentType as string == "ExportReceipt";
        if (!commandScope.StartsWith("ExportReceipt.", StringComparison.Ordinal) && !exportReject) return;
        var token = action.HttpContext.RequestAborted;
        var permission = commandScope switch
        {
            "ExportReceipt.Create" => "export_receipt.create",
            "ExportReceipt.CustomerChanged" => "export_receipt.update",
            "ExportReceipt.Dispatch" => "export_receipt.dispatch",
            "ExportReceipt.Cancel" => "export_receipt.cancel",
            _ => exportReject ? "export_receipt.cancel" : "export_receipt.approve"
        };
        async Task Require(string code)
        {
            if (!await context.Users.AsNoTracking().AnyAsync(u => u.Id == actor && u.IsActive &&
                (u.LockoutEnd == null || u.LockoutEnd <= DateTime.UtcNow) && u.Role.Permissions.Any(g => g.Permission.Code == code), token))
                throw new ForbiddenException("Bạn không có quyền thực hiện thao tác này.");
        }
        await Require(permission);
        if (exportReject) await Require("approval.reject");
        if (commandScope == "ExportReceipt.Create")
        {
            var dto = (ERP.Application.DTOs.CreateExportReceiptDto)action.ActionArguments["dto"]!;
            await warehouseAuthorization.EnsureWarehouseAccessAsync(dto.WarehouseId, token);
            return;
        }
        var id = (int)action.ActionArguments["id"]!;
        // Lock before reading the original claim: a concurrent same-key retry sees the committed winner.
        if (context.Database.IsSqlServer())
            await context.Database.SqlQuery<int>($"SELECT Id AS Value FROM ExportReceipts WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}").ToListAsync(token);
        var receipt = await context.ExportReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(receipt.WarehouseId, token);
        if (commandScope.Contains("Approve", StringComparison.Ordinal) || exportReject)
            ERP.Application.Security.ApprovalSafetyGuard.EnsureDifferentChecker(receipt.CreatedBy, actor);
        if (commandScope == "ExportReceipt.Dispatch" && receipt.ApprovedBy == actor)
            throw new ForbiddenException("Người duyệt phải khác người xác nhận xuất.");
        var existing = await context.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == actor && x.CommandScope == commandScope && x.KeyHash == keyHash, token);
        if (existing is not null && (existing.RequestFingerprint != fingerprint || existing.Status != IdempotencyStatus.Completed))
            throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
        var immediate = commandScope == "ExportReceipt.ApproveAndDispatch";
        if (commandScope == "ExportReceipt.Approve")
        {
            if (existing is null) immediate = (exportOptions ?? new()).GetDefaultMode() == ERP.Domain.Enums.ExportDispatchMode.DispatchOnApproval;
            else
            {
                var original = await context.AuditLogs.AsNoTracking().SingleOrDefaultAsync(x => x.CorrelationId == existing.CorrelationId && x.EntityName == "ExportReceipt" && x.EntityId == id &&
                    (x.Action == "ExportReceipt.ApprovedAndReserved" || x.Action == "ExportReceipt.ApprovedAndDispatched") && x.Result == "Success", token)
                    ?? throw new ForbiddenException("Bạn không có quyền thực hiện thao tác này.");
                immediate = original.Action == "ExportReceipt.ApprovedAndDispatched";
            }
        }
        if (immediate) await Require("export_receipt.dispatch");
        if (existing is not null) return; // Reauthorize original semantics, then permit terminal-state replay.
        var expectedState = commandScope == "ExportReceipt.Dispatch" ? ERP.Domain.Enums.ReceiptStatus.Approved : ERP.Domain.Enums.ReceiptStatus.Draft;
        if (commandScope == "ExportReceipt.Cancel" ? receipt.Status is not ERP.Domain.Enums.ReceiptStatus.Draft and not ERP.Domain.Enums.ReceiptStatus.Approved : receipt.Status != expectedState)
            throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
        if (!exportReject)
        {
            var dto = action.ActionArguments.Values.FirstOrDefault(x => x is ERP.Application.DTOs.ExportReceiptCommandDto or ERP.Application.DTOs.SetReceiptPartnerDto);
            var value = dto?.GetType().GetProperty("RowVersion")?.GetValue(dto) as string;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(value ?? ""); }
            catch (FormatException) { throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại."); }
            if (bytes.Length != 8 || !bytes.SequenceEqual(receipt.RowVersion))
                throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
        }
    }

    private (int? Warehouse, int? Source, int? Destination) TrackedWarehouses(IDictionary<string, object?> arguments)
    {
        foreach (var entity in context.ChangeTracker.Entries().Select(x => x.Entity).Concat(arguments.Values.OfType<object>()))
        {
            var type = entity.GetType();
            int? Read(string name) => type.GetProperty(name)?.GetValue(entity) is int value ? value : null;
            var warehouse = Read("WarehouseId");
            var source = Read("SourceWarehouseId");
            var destination = Read("DestinationWarehouseId");
            if (warehouse.HasValue || source.HasValue || destination.HasValue) return (warehouse, source, destination);
        }
        return (null, null, null);
    }

    private AuditLog RejectionAudit(int userId, IDictionary<string, object?> arguments, string reason, (int? Warehouse, int? Source, int? Destination) warehouses)
    {
        var entityId = arguments.TryGetValue("id", out var id) && id is int value ? value : (int?)null;
        return new AuditLog
        {
            UserId = userId, Action = commandScope + ".Rejected", EntityName = commandScope.Split('.')[0],
            EntityId = entityId, WarehouseId = warehouses.Warehouse, SourceWarehouseId = warehouses.Source,
            DestinationWarehouseId = warehouses.Destination, Result = "Rejected", Reason = reason, Severity = "Warning",
            Timestamp = DateTime.UtcNow
        };
    }

    public static string Fingerprint(string scope, IDictionary<string, object?> arguments)
    {
        var canonical = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var argument in arguments.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (argument.Value is CancellationToken) continue;
            canonical[argument.Key] = JsonSerializer.SerializeToElement(argument.Value, argument.Value?.GetType() ?? typeof(object));
        }
        return Hash(scope + "\n" + JsonSerializer.Serialize(canonical));
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        if (ex.InnerException is SqlException { Number: 2601 or 2627 }) return true;
        var inner = ex.InnerException;
        return inner?.GetType().Name == "SqliteException"
            && inner.GetType().GetProperty("SqliteErrorCode")?.GetValue(inner) is 19;
    }
    private static int ResultStatus(IActionResult? result) => result switch { ObjectResult x => x.StatusCode ?? 200, StatusCodeResult x => x.StatusCode, _ => 200 };
    private static string? SerializeResult(IActionResult? result) => result switch
    {
        ObjectResult x => JsonSerializer.Serialize(x.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        _ => null
    };
}
