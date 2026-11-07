using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class SalesOrderBackorderService
{
    private async Task<SalesOrderDto> MutateOrderAsync(
        int id,
        string encodedRowVersion,
        Func<SalesOrder, CancellationToken, Task> mutation,
        CancellationToken cancellationToken)
    {
        int? lockWarehouseId = null;
        int[] lockProductIds = [];
        if (context.Database.IsSqlServer())
        {
            lockWarehouseId = await context.SalesOrders.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => (int?)x.WarehouseId)
                .FirstOrDefaultAsync(cancellationToken);
            if (lockWarehouseId.HasValue)
            {
                lockProductIds = await context.SalesOrderLines.AsNoTracking()
                    .Where(x => x.SalesOrderId == id)
                    .Select(x => x.ProductId)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToArrayAsync(cancellationToken);
            }
        }

        await ExecuteSerializableAsync(async token =>
        {
            if (lockWarehouseId.HasValue && lockProductIds.Length > 0)
            {
                await AcquireAllocationLocksAsync(lockWarehouseId.Value, lockProductIds, token);
            }

            var order = await LoadOrderAsync(id, true, token);
            ApplyVersion(order, encodedRowVersion);
            await mutation(order, token);
            await context.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
        return await GetSalesOrderAsync(id, cancellationToken);
    }

    private Task AcquireAllocationLocksAsync(
        int warehouseId,
        IEnumerable<int> productIds,
        CancellationToken token) =>
        context.AcquireInventoryAllocationLocksAsync(warehouseId, productIds, token);

    private async Task<SalesOrder> LoadOrderAsync(int id, bool tracking, CancellationToken token)
    {
        IQueryable<SalesOrder> query = OrderQuery();
        if (!tracking) query = query.AsNoTracking();
        var order = await query.SingleOrDefaultAsync(x => x.Id == id, token);
        if (order is null)
        {
            var fallbackQuery = context.SalesOrders
                .Include(x => x.Lines)
                    .ThenInclude(x => x.Backorder)
                .AsQueryable();
            if (!tracking) fallbackQuery = fallbackQuery.AsNoTracking();
            order = await fallbackQuery.SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException("Không tìm thấy Sales Order.");
            order.Customer ??= await context.BusinessPartners.AsNoTracking().FirstOrDefaultAsync(x => x.Id == order.CustomerId, token)
                ?? new BusinessPartner { Id = order.CustomerId, Code = $"CUST-{order.CustomerId}", Name = $"Customer {order.CustomerId}", IsCustomer = true, IsActive = true };
            order.Warehouse ??= await context.Warehouses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == order.WarehouseId, token)
                ?? new Warehouse { Id = order.WarehouseId, Code = $"WH-{order.WarehouseId}", Name = $"Warehouse {order.WarehouseId}", IsActive = true };
            if (order.Lines.Count == 0)
            {
                var lines = await context.SalesOrderLines.AsNoTracking().Where(l => l.SalesOrderId == id).ToListAsync(token);
                foreach (var l in lines) order.Lines.Add(l);
            }
            var lineProductIds = order.Lines.Select(l => l.ProductId).Distinct().ToArray();
            var lineProducts = await context.Products.AsNoTracking().Where(p => lineProductIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, token);
            foreach (var line in order.Lines)
            {
                line.Product ??= lineProducts.GetValueOrDefault(line.ProductId)
                    ?? new Product { Id = line.ProductId, Code = $"SKU-{line.ProductId}", Name = $"Product {line.ProductId}", IsActive = true };
            }
        }
        await warehouseAuthorization.EnsureWarehouseAccessAsync(order.WarehouseId, token);
        return order;
    }

    private async Task<Backorder> LoadBackorderAsync(int id, bool tracking, CancellationToken token)
    {
        IQueryable<Backorder> query = BackorderQuery();
        if (!tracking) query = query.AsNoTracking();
        var row = await query.SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("Không tìm thấy Backorder.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(row.WarehouseId, token);
        return row;
    }

    private static void ValidateCreate(CreateSalesOrderDto request)
    {
        if (string.IsNullOrWhiteSpace(request.ExternalOrderId))
            request.ExternalOrderId = $"EXT-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        if (request.ExternalOrderId.Trim().Length > 100)
            throw new BusinessRuleException("ExternalOrderId là bắt buộc và tối đa 100 ký tự.");
        if (request.CustomerId <= 0 || request.WarehouseId <= 0)
            throw new BusinessRuleException("Khách hàng và kho là bắt buộc.");
        if (request.Priority < 0)
            throw new BusinessRuleException("Priority không được âm.");
        if (request.Lines.Count == 0)
            throw new BusinessRuleException("Sales Order phải có ít nhất một dòng.");
        for (var i = 0; i < request.Lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(request.Lines[i].ExternalLineId))
                request.Lines[i].ExternalLineId = $"LINE-{i + 1}-{request.Lines[i].ProductId}";
        }
        if (request.Lines.Any(x => string.IsNullOrWhiteSpace(x.ExternalLineId) || x.ExternalLineId.Trim().Length > 100 || x.ProductId <= 0 || x.OrderedQuantity <= 0 || x.UnitPrice < 0m || x.TaxPercent < 0m || x.TaxPercent > 100m))
            throw new BusinessRuleException("Dòng Sales Order không hợp lệ.");
        if (request.Lines.GroupBy(x => x.ExternalLineId.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new BusinessRuleException("ExternalLineId không được trùng trong cùng Sales Order.");
        if (request.Lines.GroupBy(x => x.ProductId).Any(g => g.Count() > 1))
            throw new BusinessRuleException("Foundation hiện yêu cầu mỗi SKU chỉ xuất hiện một dòng trong Sales Order.");
    }

    private static void ValidateQuantityPrecision(int decimalPlaces, decimal quantity)
    {
        if (decimal.Round(quantity, decimalPlaces) != quantity)
            throw new BusinessRuleException($"Số lượng vượt quá độ chính xác Base UOM cho phép ({decimalPlaces} chữ số thập phân).");
    }

    private void ApplyVersion(SalesOrder order, string encoded)
    {
        var expected = DecodeVersion(encoded);
        if (!order.RowVersion.SequenceEqual(expected))
            throw Conflict("SALES_ORDER_VERSION_CONFLICT", "Sales Order đã thay đổi. Vui lòng tải lại.");
        context.Entry(order).Property(x => x.RowVersion).OriginalValue = expected;
    }

    private void ApplyVersion(Backorder row, string encoded)
    {
        var expected = DecodeVersion(encoded);
        if (!row.RowVersion.SequenceEqual(expected))
            throw Conflict("BACKORDER_VERSION_CONFLICT", "Backorder đã thay đổi. Vui lòng tải lại.");
        context.Entry(row).Property(x => x.RowVersion).OriginalValue = expected;
    }

    private static byte[] DecodeVersion(string encoded)
    {
        try { return Convert.FromBase64String(encoded); }
        catch { throw new BusinessRuleException("RowVersion không hợp lệ."); }
    }

    private async Task<T> ExecuteSerializableAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken token)
    {
        var own = context.Database.CurrentTransaction is null;
        var maxAttempts = own && context.Database.IsSqlServer() ? 4 : 1;
        for (var attempt = 1; ; attempt++)
        {
            await using var transaction = own
                ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token)
                : null;
            try
            {
                var result = await action(token);
                if (transaction is not null) await transaction.CommitAsync(token);
                return result;
            }
            catch (Exception ex) when (own && attempt < maxAttempts && IsSqlDeadlock(ex))
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                context.ChangeTracker.Clear();
                await Task.Delay(15 * attempt, token);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                throw new ConcurrencyException("Demand đã thay đổi đồng thời. Vui lòng tải lại.", ex);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                throw Conflict("DEMAND_DUPLICATE", "Demand/Backorder bị trùng do yêu cầu đồng thời.");
            }
            catch
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }

    private static bool IsSqlDeadlock(Exception ex) =>
        ex is DeadlockException ||
        ex is SqlException { Number: 1205 or 51033 } ||
        ex is DbUpdateException { InnerException: SqlException { Number: 1205 or 51033 } };

    private void AddAudit(string action, SalesOrder order, string values) =>
        context.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = action,
            EntityName = "SalesOrder",
            EntityId = order.Id,
            WarehouseId = order.WarehouseId,
            Timestamp = DateTime.UtcNow,
            NewValues = values,
            Result = "Success",
            Severity = "Information"
        });

    private static BusinessRuleException Conflict(string code, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["HttpStatusCode"] = 409;
        ex.Data["ErrorCode"] = code;
        return ex;
    }
}
