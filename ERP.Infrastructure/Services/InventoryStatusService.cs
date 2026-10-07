using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class InventoryStatusService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser) : IInventoryStatusService
{
    public async Task<IReadOnlyList<InventoryStatusDefinitionDto>> GetStatusesAsync(
        CancellationToken cancellationToken = default) =>
        (await context.InventoryStatusDefinitions.AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Code)
            .ToListAsync(cancellationToken))
        .Select(MapStatus)
        .ToList();

    public async Task<IReadOnlyList<InventoryStatusBucketDto>> GetBucketsAsync(
        int? warehouseId = null,
        int? productId = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (warehouseId.HasValue)
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            allowed = [warehouseId.Value];
        }

        var query = BucketQuery().AsNoTracking().Where(x => allowed.Contains(x.WarehouseId));
        if (productId.HasValue) query = query.Where(x => x.ProductId == productId.Value);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var definition = await ResolveStatusAsync(status, cancellationToken);
            query = query.Where(x => x.Status == definition.Id);
        }

        var rows = await query
            .OrderBy(x => x.WarehouseId)
            .ThenBy(x => x.Product.Code)
            .ThenBy(x => x.LocationId)
            .ThenBy(x => x.StatusDefinition.SortOrder)
            .Take(1000)
            .ToListAsync(cancellationToken);
        return rows.Select(MapBucket).ToList();
    }

    public async Task<IReadOnlyList<InventoryStatusBucketDto>> GetQuarantineAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (warehouseId.HasValue)
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            allowed = [warehouseId.Value];
        }

        var rows = await BucketQuery().AsNoTracking()
            .Where(x => allowed.Contains(x.WarehouseId) &&
                        x.Status == InventoryStatus.Quarantine &&
                        x.Quantity > 0)
            .OrderBy(x => x.WarehouseId)
            .ThenBy(x => x.Product.Code)
            .ThenBy(x => x.LocationId)
            .Take(500)
            .ToListAsync(cancellationToken);
        return rows.Select(MapBucket).ToList();
    }

    public Task<InventoryStatusChangeResultDto> ChangeStatusAsync(
        InventoryStatusChangeRequestDto request,
        CancellationToken cancellationToken = default) =>
        ChangeCoreAsync(request, cancellationToken);

    public async Task<InventoryStatusChangeResultDto> ReleaseQuarantineAsync(
        int stockId,
        QuarantineReleaseRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0)
            throw Validation("QUANTITY_MUST_BE_POSITIVE", "Số lượng release phải lớn hơn 0.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw Validation("REASON_CODE_REQUIRED", "Lý do release quarantine là bắt buộc.");

        var source = await context.InventoryStocks.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == stockId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy bucket quarantine.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(source.WarehouseId, cancellationToken);
        if (source.Status != InventoryStatus.Quarantine)
            throw Conflict("INV_STATUS_CHANGE_NOT_ALLOWED", "Bucket không còn ở trạng thái QUARANTINE.");

        return await ChangeCoreAsync(new InventoryStatusChangeRequestDto
        {
            WarehouseId = source.WarehouseId,
            ProductId = source.ProductId,
            LocationId = source.LocationId,
            FromStatus = "QUARANTINE",
            ToStatus = "AVAILABLE",
            Quantity = request.Quantity,
            Reason = request.Reason
        }, cancellationToken);
    }

    private async Task<InventoryStatusChangeResultDto> ChangeCoreAsync(
        InventoryStatusChangeRequestDto request,
        CancellationToken cancellationToken)
    {
        ValidateChange(request);
        await warehouseAuthorization.EnsureWarehouseAccessAsync(request.WarehouseId, cancellationToken);

        var from = await ResolveStatusAsync(request.FromStatus, cancellationToken);
        var to = await ResolveStatusAsync(request.ToStatus, cancellationToken);
        if (from.Id == to.Id)
            throw Conflict("INV_STATUS_CHANGE_NOT_ALLOWED", "Trạng thái nguồn và đích phải khác nhau.");
        if (!from.IsActive || !to.IsActive)
            throw Conflict("INV_STATUS_CHANGE_NOT_ALLOWED", "Inventory status không còn hoạt động.");

        var ownTransaction = context.Database.CurrentTransaction is null;
        await using var transaction = ownTransaction
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var source = await context.InventoryStocks
                .SingleOrDefaultAsync(x =>
                    x.ProductId == request.ProductId &&
                    x.WarehouseId == request.WarehouseId &&
                    x.LocationId == request.LocationId &&
                    x.Status == from.Id,
                    cancellationToken)
                ?? throw Conflict("INV_INSUFFICIENT_ON_HAND", "Không tìm thấy bucket nguồn có tồn.");

            var movable = source.Quantity - source.ReservedQuantity;
            if (movable < request.Quantity)
            {
                var code = source.ReservedQuantity > 0
                    ? "INV_STATUS_CHANGE_NOT_ALLOWED"
                    : "INV_INSUFFICIENT_ON_HAND";
                throw Conflict(code, source.ReservedQuantity > 0
                    ? "Không thể chuyển phần quantity đang được reservation giữ."
                    : "Bucket nguồn không đủ OnHand.");
            }

            var destination = await context.InventoryStocks.SingleOrDefaultAsync(x =>
                x.ProductId == request.ProductId &&
                x.WarehouseId == request.WarehouseId &&
                x.LocationId == request.LocationId &&
                x.Status == to.Id,
                cancellationToken);
            if (destination is null)
            {
                destination = new InventoryStock
                {
                    ProductId = request.ProductId,
                    WarehouseId = request.WarehouseId,
                    LocationId = request.LocationId,
                    Status = to.Id,
                    Quantity = 0,
                    ReservedQuantity = 0,
                    LastUpdated = DateTime.UtcNow
                };
                context.InventoryStocks.Add(destination);
            }

            var now = DateTime.UtcNow;
            source.Quantity -= request.Quantity;
            source.LastUpdated = now;
            destination.Quantity += request.Quantity;
            destination.LastUpdated = now;

            var ledger = new InventoryTransaction
            {
                ProductId = request.ProductId,
                WarehouseId = request.WarehouseId,
                LocationId = request.LocationId,
                InventoryStatus = to.Id,
                FromInventoryStatus = from.Id,
                ToInventoryStatus = to.Id,
                TransactionType = TransactionType.StatusChange,
                Quantity = request.Quantity,
                ReferenceId = source.Id,
                ReferenceType = "InventoryStatusChange",
                TransactionDate = now,
                CreatedBy = currentUser.UserId,
                Note = request.Reason.Trim()
            };
            context.InventoryTransactions.Add(ledger);
            context.AuditLogs.Add(new AuditLog
            {
                UserId = currentUser.UserId,
                Action = "Inventory.StatusChanged",
                EntityName = "InventoryStock",
                EntityId = source.Id,
                WarehouseId = request.WarehouseId,
                Timestamp = now,
                OldValues = $"Status: {from.Code}; Quantity: {source.Quantity + request.Quantity}; Reserved: {source.ReservedQuantity}",
                NewValues = $"Status: {to.Code}; QuantityMoved: {request.Quantity}; SourceRemaining: {source.Quantity}",
                Reason = request.Reason.Trim(),
                Result = "Success",
                Severity = "Warning"
            });

            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new InventoryStatusChangeResultDto
            {
                TransactionId = ledger.Id,
                SourceStockId = source.Id,
                DestinationStockId = destination.Id,
                FromStatus = from.Code,
                ToStatus = to.Code,
                Quantity = request.Quantity,
                SourceRemainingQuantity = source.Quantity,
                DestinationQuantity = destination.Quantity,
                PostedAt = now
            };
        }
        catch (DbUpdateConcurrencyException ex)
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new ConcurrencyException("Inventory bucket đã thay đổi. Vui lòng tải lại và thử lại.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new ConcurrencyException("Inventory bucket đích được tạo đồng thời. Vui lòng thử lại.", ex);
        }
        catch (SqlException ex) when (ex.Number == 1205)
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new DeadlockException("Giao dịch đổi trạng thái tồn kho bị deadlock.", ex);
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private IQueryable<InventoryStock> BucketQuery() =>
        context.InventoryStocks
            .Include(x => x.Product)
            .Include(x => x.Warehouse)
            .Include(x => x.Location)
            .Include(x => x.StatusDefinition);

    private async Task<InventoryStatusDefinition> ResolveStatusAsync(
        string raw,
        CancellationToken cancellationToken)
    {
        var code = raw?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
            throw Validation("VALIDATION_FAILED", "Inventory status là bắt buộc.");
        return await context.InventoryStatusDefinitions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Code == code, cancellationToken)
            ?? throw Conflict("INV_STATUS_CHANGE_NOT_ALLOWED", $"Inventory status {code} không hợp lệ.");
    }

    private static InventoryStatusDefinitionDto MapStatus(InventoryStatusDefinition x) => new()
    {
        Id = (int)x.Id,
        Code = x.Code,
        DisplayName = x.DisplayName,
        IsAvailable = x.IsAvailable,
        IsReservable = x.IsReservable,
        IsAllocatable = x.IsAllocatable,
        IsPickable = x.IsPickable,
        IsShippable = x.IsShippable,
        IsActive = x.IsActive,
        SortOrder = x.SortOrder
    };

    private static InventoryStatusBucketDto MapBucket(InventoryStock x)
    {
        var locationEligible = !x.LocationId.HasValue ||
            x.Location is { IsActive: true, IsBlocked: false, IsPickable: true };
        return new InventoryStatusBucketDto
        {
            StockId = x.Id,
            ProductId = x.ProductId,
            ProductCode = x.Product.Code,
            ProductName = x.Product.Name,
            WarehouseId = x.WarehouseId,
            WarehouseName = x.Warehouse.Name,
            LocationId = x.LocationId,
            LocationCode = x.Location?.Code,
            StatusCode = x.StatusDefinition.Code,
            StatusDisplayName = x.StatusDefinition.DisplayName,
            OnHandQuantity = x.Quantity,
            ReservedQuantity = x.ReservedQuantity,
            EligibleAvailableQuantity = x.StatusDefinition.IsAvailable && locationEligible
                ? Math.Max(0m, x.Quantity - x.ReservedQuantity)
                : 0m,
            LastUpdated = x.LastUpdated
        };
    }

    private static void ValidateChange(InventoryStatusChangeRequestDto request)
    {
        if (request.WarehouseId <= 0 || request.ProductId <= 0)
            throw Validation("VALIDATION_FAILED", "Kho và sản phẩm là bắt buộc.");
        if (request.Quantity <= 0)
            throw Validation("QUANTITY_MUST_BE_POSITIVE", "Số lượng đổi trạng thái phải lớn hơn 0.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw Validation("REASON_CODE_REQUIRED", "Lý do đổi trạng thái là bắt buộc.");
    }

    private static BusinessRuleException Conflict(string code, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["HttpStatusCode"] = 409;
        ex.Data["ErrorCode"] = code;
        return ex;
    }

    private static BusinessRuleException Validation(string code, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["HttpStatusCode"] = 422;
        ex.Data["ErrorCode"] = code;
        return ex;
    }
}
