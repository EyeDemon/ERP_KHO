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
            actionContext.Result = new ContentResult { StatusCode = existing.ResponseStatusCode, ContentType = "application/json", Content = existing.ResponseBody };
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
        if (statusCode >= 500)
        {
            await transaction.RollbackAsync(CancellationToken.None);
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
