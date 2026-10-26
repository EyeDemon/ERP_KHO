using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    IWarehouseAuthorizationService warehouseAuthorization) : IAsyncActionFilter
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
            actionContext.Result = new NotFoundObjectResult(new
            {
                code = "RESOURCE_NOT_FOUND",
                message = "Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.",
                error = new
                {
                    code = "RESOURCE_NOT_FOUND",
                    message = "Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập."
                }
            });
            return;
        }

        var rawKey = actionContext.HttpContext.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            if (IsOptionalHeaderScope(commandScope))
            {
                await next();
                return;
            }

            actionContext.Result = new BadRequestObjectResult(new { message = "Idempotency-Key là bắt buộc và không được vượt quá 128 ký tự." });
            return;
        }

        if (rawKey.Length > 128)
        {
            actionContext.Result = new BadRequestObjectResult(new { message = "Idempotency-Key là bắt buộc và không được vượt quá 128 ký tự." });
            return;
        }

        var keyHash = Hash(rawKey);
        var fingerprint = Fingerprint(
            commandScope,
            actionContext.ActionArguments,
            actionContext.HttpContext.Request.QueryString.HasValue ? actionContext.HttpContext.Request.QueryString.Value : null);
        metadata.IdempotencyKeyHash = keyHash;
        metadata.RequestFingerprint = fingerprint;
        await using var transaction = !context.Database.IsRelational()
            ? null
            : RequiresSerializableIsolation(commandScope)
                ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, actionContext.HttpContext.RequestAborted)
                : await context.Database.BeginTransactionAsync(actionContext.HttpContext.RequestAborted);
        var tenantId = context.CurrentTenantId;
        var record = new IdempotencyRecord
        {
            TenantId = tenantId, UserId = userId, CommandScope = commandScope, KeyHash = keyHash,
            RequestFingerprint = fingerprint, Status = IdempotencyStatus.Processing,
            CorrelationId = metadata.CorrelationId, CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(30)
        };
        context.IdempotencyRecords.Add(record);
        try
        {
            await context.SaveChangesAsync(actionContext.HttpContext.RequestAborted);
        }
        catch (Exception ex) when (IsUniqueViolation(ex) || IsDeadlock(ex))
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            var existing = await context.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.UserId == userId && x.CommandScope == commandScope && x.KeyHash == keyHash,
                actionContext.HttpContext.RequestAborted);
            if (existing is null || existing.RequestFingerprint != fingerprint || existing.Status != IdempotencyStatus.Completed)
            {
                actionContext.Result = new ConflictObjectResult(new
                {
                    code = "IDEMPOTENCY_CONFLICT",
                    message = "Idempotency-Key đang được xử lý hoặc đã được dùng cho yêu cầu khác.",
                    error = new
                    {
                        code = "IDEMPOTENCY_CONFLICT",
                        message = "Idempotency-Key đang được xử lý hoặc đã được dùng cho yêu cầu khác."
                    }
                });
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
            actionContext.Result = new ContentResult { StatusCode = existing.ResponseStatusCode, ContentType = "application/json", Content = existing.ResponseBody };
            return;
        }

        var executed = await next();
        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            var rejectedAudit = executed.Exception is ForbiddenException
                ? RejectionAudit(userId, actionContext.ActionArguments, executed.Exception.Message, TrackedWarehouses(actionContext.ActionArguments))
                : null;
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            if (!context.Database.IsRelational())
            {
                context.ChangeTracker.Clear();
                var orphan = await context.IdempotencyRecords.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == record.Id, CancellationToken.None);
                if (orphan is not null)
                {
                    context.IdempotencyRecords.Remove(orphan);
                    await context.SaveChangesAsync(CancellationToken.None);
                }
            }
            context.ChangeTracker.Clear();
            if (rejectedAudit is not null)
            {
                context.AuditLogs.Add(rejectedAudit);
                await context.SaveChangesAsync(CancellationToken.None);
            }
            return;
        }

        var statusCode = ResultStatus(executed.Result);
        if (statusCode >= 400)
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            if (!context.Database.IsRelational())
            {
                context.ChangeTracker.Clear();
                var orphan = await context.IdempotencyRecords.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == record.Id, CancellationToken.None);
                if (orphan is not null)
                {
                    context.IdempotencyRecords.Remove(orphan);
                    await context.SaveChangesAsync(CancellationToken.None);
                }
            }
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
            record.ResponseBody = BuildTruncatedReplayEnvelope(record.ResponseBody, statusCode);
        record.CompletedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(actionContext.HttpContext.RequestAborted);
        if (transaction is not null) await transaction.CommitAsync(actionContext.HttpContext.RequestAborted);
        context.ChangeTracker.Clear();
    }

    private static string BuildTruncatedReplayEnvelope(string originalJson, int statusCode)
    {
        var envelope = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["idempotentReplay"] = true,
            ["truncated"] = true,
            ["statusCode"] = statusCode
        };

        try
        {
            using var doc = JsonDocument.Parse(originalJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (envelope.ContainsKey(prop.Name))
                        continue;

                    switch (prop.Value.ValueKind)
                    {
                        case JsonValueKind.Number:
                        case JsonValueKind.True:
                        case JsonValueKind.False:
                        case JsonValueKind.Null:
                            envelope[prop.Name] = prop.Value.Clone();
                            break;
                        case JsonValueKind.String when (prop.Value.GetString()?.Length ?? 0) <= 512:
                            envelope[prop.Name] = prop.Value.GetString();
                            break;
                        case JsonValueKind.Array:
                            envelope[prop.Name] = Array.Empty<object>();
                            break;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Fallback to minimal valid JSON envelope if payload was not a JSON object.
        }

        return JsonSerializer.Serialize(envelope);
    }

    private async Task ReauthorizeWarehouses(IdempotencyRecord record, CancellationToken token)
    {
        foreach (var warehouseId in new[] { record.WarehouseId, record.SourceWarehouseId, record.DestinationWarehouseId }.Where(x => x.HasValue).Select(x => x!.Value).Distinct())
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId, token);
    }

    private (int? Warehouse, int? Source, int? Destination) TrackedWarehouses(IDictionary<string, object?> arguments)
    {
        foreach (var entity in context.ChangeTracker.Entries().Select(x => x.Entity).Concat(arguments.Values.Where(v => !IsNonPayloadArgument(v)).OfType<object>()))
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

    private static bool IsNonPayloadArgument(object? value)
    {
        if (value is null) return false;
        if (value is CancellationToken
            or ErpKhoDbContext
            or DbContext
            or IWarehouseAuthorizationService
            or IRequestMetadata
            or IBusinessPartnerService
            or IProductCatalogService
            or Microsoft.AspNetCore.Http.IFormFile
            or Microsoft.AspNetCore.Http.IFormCollection
            or System.IO.Stream)
            return true;

        var ns = value.GetType().Namespace;
        return ns is not null && (
            ns.StartsWith("ERP.Infrastructure", StringComparison.Ordinal) ||
            ns.StartsWith("ERP.Application.Services", StringComparison.Ordinal) ||
            ns.StartsWith("ERP.Application.Interfaces", StringComparison.Ordinal) ||
            ns.StartsWith("ERP.Api.Security", StringComparison.Ordinal) ||
            ns.StartsWith("ERP.Api.Infrastructure", StringComparison.Ordinal) ||
            ns.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
            ns.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    public static string Fingerprint(string scope, IDictionary<string, object?> arguments) =>
        Fingerprint(scope, arguments, null);

    public static string Fingerprint(string scope, IDictionary<string, object?> arguments, string? queryString)
    {
        var canonical = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var argument in arguments.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (IsNonPayloadArgument(argument.Value)) continue;
            canonical[argument.Key] = JsonSerializer.SerializeToElement(argument.Value, argument.Value?.GetType() ?? typeof(object));
        }
        if (!string.IsNullOrWhiteSpace(queryString))
            canonical["__queryString"] = JsonSerializer.SerializeToElement(queryString);
        return Hash(scope + "\n" + JsonSerializer.Serialize(canonical));
    }

    public static string ComputeRequestHash(HttpRequest request, string? body = null)
    {
        var raw = $"{request.Method}\n{request.Path}\n{request.QueryString.Value ?? string.Empty}\n{body ?? string.Empty}";
        return Hash(raw);
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static bool IsOptionalHeaderScope(string scope) =>
        scope.StartsWith("BusinessPartner.", StringComparison.Ordinal)
        || scope.StartsWith("Product.", StringComparison.Ordinal)
        || scope.StartsWith("Warehouse.", StringComparison.Ordinal)
        || scope.StartsWith("Unit.", StringComparison.Ordinal)
        || scope.StartsWith("ProductCategory.", StringComparison.Ordinal)
        || scope.StartsWith("ProductBarcode.", StringComparison.Ordinal)
        || scope.StartsWith("PutawayLocation.", StringComparison.Ordinal)
        || scope.StartsWith("WarehouseCalendar.", StringComparison.Ordinal)
        || scope is "DockYard.Dock.Update" or "DockYard.YardSlot.Update"
            or "ImportReceipt.SetSupplier" or "ImportReceipt.RmaIntakeCheck" or "ImportReceipt.ExecuteRtvRefurbish"
            or "ExportReceipt.SetCustomer"
            or "Approval.EscalateOverdue"
            or "PackingSession.ExecuteKittingVas"
            or "PickingTask.SyncAutomationTelemetry"
            or "PurchaseOrder.ExpediteOverdue" or "PurchaseOrder.GenerateProcurementRecommendations"
            or "SalesOrder.ExpediteBackorder"
            or "Shipment.SyncTmsManifest"
            or "Stocktake.CreateAdjustmentProposal"
            or "OperationalException.Acknowledge"
            or "InventoryReservation.Release" or "InventoryReservation.Expire";

    public static bool RequiresSerializableIsolation(string scope) =>
        scope is not ("DockYard.Appointment.Confirm" or "StockTransfer.Approve") &&
        (scope.StartsWith("ImportReceipt.", StringComparison.Ordinal)
        || scope.StartsWith("ExportReceipt.", StringComparison.Ordinal)
        || scope.StartsWith("Inbound.", StringComparison.Ordinal)
        || scope.StartsWith("Outbound.", StringComparison.Ordinal)
        || scope.StartsWith("SalesOrder.", StringComparison.Ordinal)
        || scope.StartsWith("Backorder.", StringComparison.Ordinal)
        || scope.StartsWith("StockReservation.", StringComparison.Ordinal)
        || scope.StartsWith("InventoryReservation.", StringComparison.Ordinal)
        || scope.StartsWith("StockAllocation.", StringComparison.Ordinal)
        || scope.StartsWith("InventoryAllocation.", StringComparison.Ordinal)
        || scope.StartsWith("Picking.", StringComparison.Ordinal)
        || scope.StartsWith("PickingTask.", StringComparison.Ordinal)
        || scope.StartsWith("Packing.", StringComparison.Ordinal)
        || scope.StartsWith("PackingSession.", StringComparison.Ordinal)
        || scope.StartsWith("HandlingUnit.", StringComparison.Ordinal)
        || scope.StartsWith("Shipment.", StringComparison.Ordinal)
        || scope.StartsWith("Putaway.", StringComparison.Ordinal)
        || scope.StartsWith("PutawayTask.", StringComparison.Ordinal)
        || scope.StartsWith("Stocktake.", StringComparison.Ordinal)
        || scope.StartsWith("StockTransfer.", StringComparison.Ordinal)
        || scope.StartsWith("InventoryMovement.", StringComparison.Ordinal)
        || scope.StartsWith("InventoryReversal.", StringComparison.Ordinal)
        || scope.StartsWith("InventoryLock.", StringComparison.Ordinal)
        || scope.StartsWith("InventoryStatus.", StringComparison.Ordinal)
        || scope.StartsWith("ReceivingDiscrepancy.", StringComparison.Ordinal)
        || scope.StartsWith("DockAppointment.", StringComparison.Ordinal)
        || scope.StartsWith("DockYard.", StringComparison.Ordinal)
        || scope.StartsWith("Approval.", StringComparison.Ordinal));
    private static bool IsUniqueViolation(Exception ex)
    {
        if (ex is not DbUpdateException dbEx) return false;
        if (dbEx.InnerException is SqlException { Number: 2601 or 2627 }) return true;
        var inner = dbEx.InnerException;
        return inner?.GetType().Name == "SqliteException"
            && inner.GetType().GetProperty("SqliteErrorCode")?.GetValue(inner) is 19;
    }

    private static bool IsDeadlock(Exception ex) =>
        ex is SqlException { Number: 1205 } ||
        ex is DbUpdateException { InnerException: SqlException { Number: 1205 } };
    internal static int ResultStatus(IActionResult? result) => result switch
    {
        ForbidResult => StatusCodes.Status403Forbidden,
        ChallengeResult => StatusCodes.Status401Unauthorized,
        UnauthorizedResult => StatusCodes.Status401Unauthorized,
        ObjectResult x => x.StatusCode ?? StatusCodes.Status200OK,
        StatusCodeResult x => x.StatusCode,
        _ => StatusCodes.Status200OK
    };
    private static string? SerializeResult(IActionResult? result) => result switch
    {
        ObjectResult x => JsonSerializer.Serialize(x.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        _ => null
    };
}
