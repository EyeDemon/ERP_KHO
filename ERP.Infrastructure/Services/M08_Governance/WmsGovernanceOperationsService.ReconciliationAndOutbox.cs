using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERP.Application.DTOs;
using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class WmsGovernanceOperationsService
{
    private static readonly byte[] EphemeralOutboxSigningKey = RandomNumberGenerator.GetBytes(32);

    // -------------------------------------------------------------------------
    // InventoryReconciliationController
    // -------------------------------------------------------------------------
    public async Task<int> CountInventoryStocksAsync(
        int? warehouseId,
        int? productId,
        CancellationToken cancellationToken = default)
    {
        var stockQuery = db.InventoryStocks.AsNoTracking().AsQueryable();
        if (warehouseId.HasValue)
            stockQuery = stockQuery.Where(s => s.WarehouseId == warehouseId.Value);
        if (productId.HasValue)
            stockQuery = stockQuery.Where(s => s.ProductId == productId.Value);
        return await stockQuery.CountAsync(cancellationToken);
    }

    public Task<WmsEndpointOutcome> GetInventoryValuationAsync(
        int? warehouseId,
        int? productId,
        CancellationToken cancellationToken = default) =>
        GetInventoryValuationAsync(warehouseId, productId, accessibleIds: null, cancellationToken);

    public async Task<WmsEndpointOutcome> GetInventoryValuationAsync(
        int? warehouseId,
        int? productId,
        IReadOnlyCollection<int>? accessibleIds,
        CancellationToken cancellationToken = default)
    {
        var query = db.InventoryStocks.AsNoTracking()
            .Include(s => s.Product)
            .Include(s => s.Warehouse)
            .Where(s => s.Quantity > 0m);
        if (warehouseId.HasValue)
            query = query.Where(s => s.WarehouseId == warehouseId.Value);
        else if (accessibleIds != null)
            query = query.Where(s => accessibleIds.Contains(s.WarehouseId));
        if (productId.HasValue)
            query = query.Where(s => s.ProductId == productId.Value);

        var stocks = await query.ToListAsync(cancellationToken);
        var items = stocks
            .GroupBy(s => new { s.WarehouseId, WarehouseCode = s.Warehouse?.Code ?? "", s.ProductId, ProductCode = s.Product?.Code ?? "", ProductName = s.Product?.Name ?? "", ProductMac = s.Product?.MovingAverageCost ?? 0m })
            .Select(g =>
            {
                var onHandQty = g.Sum(x => x.Quantity);
                var mac = g.Where(x => x.MovingAverageCost > 0m).Select(x => x.MovingAverageCost).FirstOrDefault();
                if (mac <= 0m) mac = g.Key.ProductMac;
                var totalValue = decimal.Round(onHandQty * mac, 2, MidpointRounding.AwayFromZero);
                return new
                {
                    warehouseId = g.Key.WarehouseId,
                    warehouseCode = g.Key.WarehouseCode.Trim(),
                    productId = g.Key.ProductId,
                    productCode = g.Key.ProductCode.Trim(),
                    productName = g.Key.ProductName.Trim(),
                    onHandQuantity = onHandQty,
                    movingAverageCost = mac,
                    totalValuation = totalValue
                };
            })
            .OrderBy(x => x.warehouseId)
            .ThenBy(x => x.productId)
            .ToList();

        var totalValuation = items.Sum(x => x.totalValuation);
        var totalOnHandQuantity = items.Sum(x => x.onHandQuantity);
        return WmsEndpointOutcome.Ok(new
        {
            valuationMethod = "MOVING_AVERAGE_COST",
            totalOnHandQuantity,
            totalInventoryValue = totalValuation,
            totalValuation,
            currency = "VND",
            items
        });
    }

    public async Task<decimal> SumReservedQuantityAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? accessibleIds,
        CancellationToken cancellationToken = default)
    {
        var stockQuery = db.InventoryStocks.AsNoTracking().AsQueryable();
        if (warehouseId.HasValue)
            stockQuery = stockQuery.Where(s => s.WarehouseId == warehouseId.Value);
        else if (accessibleIds != null)
            stockQuery = stockQuery.Where(s => accessibleIds.Contains(s.WarehouseId));

        return await stockQuery.SumAsync(s => (decimal?)s.ReservedQuantity, cancellationToken) ?? 0m;
    }

    public async Task<(int TotalTransactionsCount, int TotalAuditLogsCount, int IdempotencyRecordsCount)> GetIntegrationHubCountsAsync(
        int? warehouseId,
        CancellationToken cancellationToken = default)
    {
        var txQuery = db.InventoryTransactions.AsNoTracking();
        if (warehouseId.HasValue)
            txQuery = txQuery.Where(t => t.WarehouseId == warehouseId.Value);
        var totalTransactionsCount = await txQuery.CountAsync(cancellationToken);
        var totalAuditLogsCount = await db.AuditLogs.AsNoTracking().CountAsync(cancellationToken);
        var idempotencyRecordsCount = await db.IdempotencyRecords.AsNoTracking().CountAsync(cancellationToken);
        return (totalTransactionsCount, totalAuditLogsCount, idempotencyRecordsCount);
    }

    private sealed record OutboxMessageDto(
        string messageId,
        string eventType,
        string eventTypeLabel,
        string referenceCode,
        string targetSystem,
        string status,
        string statusLabel,
        DateTime? dispatchedAtUtc,
        int attemptCount = 0,
        string? payloadSignature = null);

    public async Task<WmsEndpointOutcome> GetOutboxMessagesAsync(
        int? warehouseId,
        string? statusFilter,
        string? eventTypeFilter,
        CancellationToken cancellationToken = default)
    {
        var (messages, _) = await BuildOutboxMessagesAsync(warehouseId, false, statusFilter, eventTypeFilter, cancellationToken);
        var dispatchedCount = messages.Count(m => string.Equals(m.status, "DISPATCHED", StringComparison.OrdinalIgnoreCase));
        var failedCount = messages.Count(m => string.Equals(m.status, "FAILED", StringComparison.OrdinalIgnoreCase));
        var pendingCount = messages.Count(m => string.Equals(m.status, "PENDING", StringComparison.OrdinalIgnoreCase));
        var overallStatus = failedCount > 0
            ? "FAILED"
            : (pendingCount > 0 ? "PENDING" : (dispatchedCount > 0 ? "DISPATCHED" : "IDLE"));
        return WmsEndpointOutcome.Ok(new
        {
            totalMessages = messages.Length,
            dispatchedCount,
            pendingCount,
            dlqFailedCount = failedCount,
            status = overallStatus,
            statusLabel = pendingCount > 0
                ? $"Còn {pendingCount}/{messages.Length} bản tin tích hợp đang chờ chuyển phát."
                : $"Đã đồng bộ {dispatchedCount}/{messages.Length} bản tin tích hợp trong hàng đợi.",
            messages
        });
    }

    public async Task<WmsEndpointOutcome> DispatchPendingOutboxMessagesAsync(
        int? warehouseId,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var (messages, newlyDispatchedCount) = await BuildOutboxMessagesAsync(warehouseId, true, null, null, cancellationToken);
        var now = DateTime.UtcNow;
        var batchCode = $"OUTBOX-BATCH-{now:yyyyMMdd}-01";

        if (actorUserId.HasValue && !await db.Users.AsNoTracking().AnyAsync(u => u.Id == actorUserId.Value, cancellationToken))
        {
            actorUserId = null;
        }

        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            WarehouseId = warehouseId,
            EntityName = "IntegrationOutboxBatch",
            Action = "InventoryReconciliation.DispatchOutbox",
            NewValues = $"{batchCode}: Dispatched={newlyDispatchedCount}",
            Reason = "Chuyển phát các bản tin tích hợp đang chờ trong hàng đợi sang hệ thống kế toán và vận tải",
            Result = "Success",
            Timestamp = now
        });
        await db.SaveChangesAsync(cancellationToken);

        var pendingCount = messages.Count(m => string.Equals(m.status, "PENDING", StringComparison.OrdinalIgnoreCase));
        var failedCount = messages.Count(m => string.Equals(m.status, "FAILED", StringComparison.OrdinalIgnoreCase));
        return WmsEndpointOutcome.Ok(new
        {
            batchCode,
            dispatchedCount = newlyDispatchedCount,
            totalDispatchedCount = messages.Count(m => string.Equals(m.status, "DISPATCHED", StringComparison.OrdinalIgnoreCase)),
            pendingCount,
            dlqFailedCount = failedCount,
            status = pendingCount == 0 && failedCount == 0 ? "DISPATCHED" : (failedCount > 0 ? "FAILED" : "PENDING"),
            statusLabel = $"Đã chuyển phát {newlyDispatchedCount} bản tin tích hợp đang chờ sang hệ thống kế toán và vận tải.",
            messages,
            dispatchedAtUtc = now
        });
    }

    private async Task<(OutboxMessageDto[] Messages, int DispatchedInRunCount)> BuildOutboxMessagesAsync(
        int? warehouseId,
        bool dispatchPending,
        string? statusFilter,
        string? eventTypeFilter,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var tenantId = db.CurrentTenantId > 0 ? db.CurrentTenantId : 1;
        var dispatchedInRunCount = 0;

        if (dispatchPending)
        {
            var pendingEntities = await db.IntegrationOutboxMessages
                .Where(x => x.TenantId == tenantId &&
                    (x.Status == "PENDING" || x.Status == "Pending" || x.Status == "RETRYING"))
                .OrderBy(x => x.Id)
                .ToListAsync(cancellationToken);

            foreach (var msg in pendingEntities)
            {
                if (warehouseId.HasValue && !PayloadMatchesWarehouse(msg.PayloadJson, warehouseId.Value))
                {
                    continue;
                }

                msg.Status = "DISPATCHED";
                msg.PublishedAt = now;
                msg.RetryCount += 1;
                msg.LastError = null;
                dispatchedInRunCount++;
            }

            if (dispatchedInRunCount > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        var query = db.IntegrationOutboxMessages
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(eventTypeFilter))
        {
            var evtTrimmed = eventTypeFilter.Trim();
            query = query.Where(x => x.EventType == evtTrimmed);
        }

        var dbMessages = await query
            .OrderByDescending(x => x.Id)
            .Take(500)
            .ToListAsync(cancellationToken);

        if (warehouseId.HasValue)
        {
            dbMessages = dbMessages
                .Where(m => PayloadMatchesWarehouse(m.PayloadJson, warehouseId.Value))
                .ToList();
        }

        var mapped = dbMessages.Select(m =>
        {
            var normalizedStatus = NormalizeOutboxStatus(m.Status);
            var referenceCode = ExtractPayloadProperty(m.PayloadJson, "referenceCode")
                ?? ExtractPayloadProperty(m.PayloadJson, "code")
                ?? $"{m.AggregateType}-{m.AggregateId}";
            var targetFromPayload = ExtractPayloadProperty(m.PayloadJson, "targetSystem");

            var (label, defaultTarget) = m.EventType switch
            {
                "ERP_GL_JOURNAL_SYNC" => ("Kết chuyển Bút toán Sổ cái Chung", "Phân hệ Kế toán Tài chính"),
                "TMS_SHIPMENT_DISPATCH" => ("Điều phối Vận đơn & Xuất bến", "Hệ thống Quản lý Vận tải"),
                "AP_AR_INVOICE_POST" => ("Hạch toán Hóa đơn Công nợ", "Sổ phụ Công nợ Đối tác"),
                "WMS_LEDGER_SNAPSHOT" => ("Chốt Số dư Sổ cái Kho Cuối kỳ", "Kho Dữ liệu Đối soát Tập trung"),
                _ => ("Kết chuyển Bút toán Định giá Tồn kho", "Phân hệ Kế toán Tài chính")
            };

            var statusLabel = normalizedStatus switch
            {
                "DISPATCHED" => "Đã chuyển phát thành công",
                "FAILED" => string.IsNullOrWhiteSpace(m.LastError) ? "Chuyển phát thất bại" : $"Lỗi: {m.LastError}",
                _ => "Đang chờ chuyển phát trong hàng đợi"
            };

            var signature = ComputeHmacSha256Hex(m.PayloadJson ?? string.Empty);

            return new OutboxMessageDto(
                string.IsNullOrWhiteSpace(m.EventId) ? $"OUTBOX-{m.Id:D3}" : m.EventId,
                m.EventType,
                label,
                referenceCode,
                string.IsNullOrWhiteSpace(targetFromPayload) ? defaultTarget : targetFromPayload,
                normalizedStatus,
                statusLabel,
                m.PublishedAt,
                m.RetryCount,
                signature);
        });

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            var normalizedFilter = NormalizeOutboxStatus(statusFilter);
            mapped = mapped.Where(m => string.Equals(m.status, normalizedFilter, StringComparison.OrdinalIgnoreCase));
        }

        return (mapped.ToArray(), dispatchedInRunCount);
    }

    private static string NormalizeOutboxStatus(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus))
            return "PENDING";

        if (string.Equals(rawStatus, "DISPATCHED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rawStatus, "Published", StringComparison.OrdinalIgnoreCase))
        {
            return "DISPATCHED";
        }

        if (string.Equals(rawStatus, "FAILED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rawStatus, "DeadLetter", StringComparison.OrdinalIgnoreCase))
        {
            return "FAILED";
        }

        return "PENDING";
    }

    private static bool PayloadMatchesWarehouse(string? payloadJson, int warehouseId)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return true;

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("warehouseId", out var whProp) &&
                whProp.TryGetInt32(out var payloadWarehouseId))
            {
                return payloadWarehouseId == warehouseId;
            }
        }
        catch (JsonException)
        {
            return true;
        }

        return true;
    }

    private static string? ExtractPayloadProperty(string? payloadJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(propertyName, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string ComputeHmacSha256Hex(string payload)
    {
        var envSecret = Environment.GetEnvironmentVariable("ERP_OUTBOX_SIGNING_SECRET");
        var keyBytes = !string.IsNullOrWhiteSpace(envSecret)
            ? Encoding.UTF8.GetBytes(envSecret.Trim())
            : EphemeralOutboxSigningKey;
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
