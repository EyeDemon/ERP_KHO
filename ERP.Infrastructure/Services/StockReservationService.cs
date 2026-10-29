using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Options;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public class StockReservationService(
    ErpKhoDbContext context,
    IInventoryStockRepository stockRepository,
    IUnitOfWork unitOfWork,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser,
    StockReservationOptions options,
    IPickingTaskIntegration? pickingTaskIntegration = null) : IStockReservationService
{
    public async Task<StockReservationDto> CreateAsync(CreateStockReservationDto request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        await (warehouseAuthorization.EnsureWarehouseCommandAccessAsync(request.WarehouseId, cancellationToken)
            ?? warehouseAuthorization.EnsureWarehouseAccessAsync(request.WarehouseId, cancellationToken));
        var exists = await context.Products.AnyAsync(x => x.Id == request.ProductId && x.IsActive, cancellationToken);
        if (!exists) throw new NotFoundException("Không tìm thấy sản phẩm.");
        var expiresAt = request.ExpiresAt ?? DateTime.UtcNow.AddMinutes(options.DefaultExpiryMinutes);
        if (expiresAt <= DateTime.UtcNow) throw new BusinessRuleException("Thời hạn giữ hàng phải ở tương lai.");

        await unitOfWork.BeginTransactionAsync();
        try
        {
            var bundleComponents = await context.ProductBundleComponents.AsNoTracking()
                .Where(x => x.ParentProductId == request.ProductId && x.IsActive)
                .OrderBy(x => x.ComponentProductId)
                .ToListAsync(cancellationToken);

            var hasDirectStock = await context.InventoryStocks.AsNoTracking()
                .AnyAsync(x => x.ProductId == request.ProductId && x.WarehouseId == request.WarehouseId && x.Quantity > 0, cancellationToken);

            if (bundleComponents.Count > 0 && !hasDirectStock)
            {
                StockReservation? primaryReservation = null;
                foreach (var component in bundleComponents)
                {
                    var componentQty = decimal.Round(request.Quantity * component.ComponentQuantity, 4, MidpointRounding.AwayFromZero);
                    var rsv = await ReserveCoreAsync(
                        "Manual",
                        null,
                        $"BUNDLE:{request.ProductId}",
                        request.WarehouseId,
                        component.ComponentProductId,
                        componentQty,
                        currentUser.UserId,
                        expiresAt,
                        cancellationToken);
                    if (primaryReservation is null) primaryReservation = rsv;
                }
                await unitOfWork.CommitTransactionAsync();
                return await GetAsync(primaryReservation!.Id, cancellationToken);
            }

            var reservation = await ReserveCoreAsync("Manual", null, null, request.WarehouseId, request.ProductId, request.Quantity, currentUser.UserId, expiresAt, cancellationToken);
            await unitOfWork.CommitTransactionAsync();
            return await GetAsync(reservation.Id, cancellationToken);
        }
        catch { await unitOfWork.RollbackTransactionAsync(); throw; }
    }

    public async Task<StockReservation> ReserveForExportAsync(int exportReceiptId, string exportCode, int warehouseId, int productId, decimal quantity, int userId, CancellationToken cancellationToken = default)
    {
        var existing = await context.StockReservations.FirstOrDefaultAsync(x => x.SourceType == "ExportReceipt" && x.SourceId == exportReceiptId && x.ProductId == productId, cancellationToken);
        if (existing is not null) return existing;
        try
        {
            return await ReserveCoreAsync("ExportReceipt", exportReceiptId, exportCode, warehouseId, productId, quantity, userId, DateTime.UtcNow.AddMinutes(options.DefaultExpiryMinutes), cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyException("Phiếu xuất đã được xử lý hoặc đang được xử lý bởi yêu cầu khác.", exception);
        }
        catch (DbUpdateException exception) when (IsExportReservationSourceConflict(exception))
        {
            throw new ConcurrencyException("Phiếu xuất đã được giữ hàng bởi yêu cầu đồng thời khác.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 })
        {
            throw new DeadlockException("Giao dịch giữ hàng bị deadlock.", exception);
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            throw new DeadlockException("Giao dịch giữ hàng bị deadlock.", exception);
        }
    }

    public async Task<StockReservation> ReserveForSourceAsync(
        string sourceType,
        int sourceId,
        string sourceCode,
        int warehouseId,
        int productId,
        decimal quantity,
        int userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceType) || sourceId <= 0 || string.IsNullOrWhiteSpace(sourceCode) || quantity <= 0)
            throw new BusinessRuleException("Nguồn reservation và số lượng phải hợp lệ.");
        await (warehouseAuthorization.EnsureWarehouseCommandAccessAsync(warehouseId, cancellationToken)
            ?? warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId, cancellationToken));

        var existing = await context.StockReservations.FirstOrDefaultAsync(
            x => x.SourceType == sourceType &&
                 x.SourceId == sourceId &&
                 x.ProductId == productId,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.WarehouseId == warehouseId &&
                existing.Quantity == quantity &&
                existing.Status is StockReservationStatus.Active or
                    StockReservationStatus.PartiallyAllocated or
                    StockReservationStatus.Allocated)
                return existing;
            throw new ConcurrencyException("Nguồn demand đã có reservation khác với yêu cầu hiện tại.");
        }

        try
        {
            return await ReserveCoreAsync(
                sourceType.Trim(),
                sourceId,
                sourceCode.Trim(),
                warehouseId,
                productId,
                quantity,
                userId,
                DateTime.UtcNow.AddMinutes(options.DefaultExpiryMinutes),
                cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConcurrencyException("Nguồn demand đã được giữ hàng bởi yêu cầu đồng thời khác.", exception);
        }
    }

    public async Task<StockReservation> IncreaseSourceReservationAsync(
        string sourceType,
        int sourceId,
        string sourceCode,
        int warehouseId,
        int productId,
        decimal additionalQuantity,
        int userId,
        CancellationToken cancellationToken = default)
    {
        if (additionalQuantity <= 0)
            throw new BusinessRuleException("Số lượng bổ sung reservation phải lớn hơn 0.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);

        var reservation = await context.StockReservations.SingleOrDefaultAsync(
            x => x.SourceType == sourceType &&
                 x.SourceId == sourceId &&
                 x.ProductId == productId,
            cancellationToken);
        if (reservation is null)
            return await ReserveForSourceAsync(
                sourceType,
                sourceId,
                sourceCode,
                warehouseId,
                productId,
                additionalQuantity,
                userId,
                cancellationToken);

        if (reservation.WarehouseId != warehouseId)
            throw new ConcurrencyException("Reservation nguồn không cùng kho.");
        if (string.Equals(reservation.SourceType, "Manual", StringComparison.OrdinalIgnoreCase) && reservation.ExpiresAt <= DateTime.UtcNow)
            throw new ConcurrencyException("Reservation nguồn đã hết hạn.");
        if (reservation.Status is not StockReservationStatus.Active and
            not StockReservationStatus.PartiallyAllocated and
            not StockReservationStatus.Allocated)
            throw new ConcurrencyException("Reservation nguồn không còn có thể tăng số lượng.");

        if (!await stockRepository.TryReserveAsync(productId, warehouseId, additionalQuantity, cancellationToken))
            throw new ConcurrencyException("Không đủ tồn khả dụng để bổ sung reservation.", "INSUFFICIENT_AVAILABLE_STOCK");

        reservation.Quantity += additionalQuantity;
        reservation.ExpiresAt = DateTime.UtcNow.AddMinutes(options.DefaultExpiryMinutes);
        UpdateActiveReservationStatus(reservation);
        context.AuditLogs.Add(Audit(
            userId,
            "StockReservation.Increased",
            reservation,
            $"AdditionalQuantity: {additionalQuantity}; Source: {sourceType}/{sourceId}"));
        await context.SaveChangesAsync(cancellationToken);
        return reservation;
    }

    private static bool IsExportReservationSourceConflict(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 } sql &&
        (sql.Message.Contains("IX_StockReservations_SourceType_SourceId_ProductId", StringComparison.OrdinalIgnoreCase) ||
         sql.Message.Contains("UX_StockReservations_TenantId_SourceType_SourceId_ProductId", StringComparison.OrdinalIgnoreCase));

    public async Task<StockReservation> GetExportReservationAsync(int exportReceiptId, int warehouseId, int productId, CancellationToken cancellationToken = default)
    {
        return await context.StockReservations.FirstOrDefaultAsync(
                   x => x.SourceType == "ExportReceipt" &&
                        x.SourceId == exportReceiptId &&
                        x.WarehouseId == warehouseId &&
                        x.ProductId == productId,
                   cancellationToken)
               ?? throw new ConcurrencyException("Không tìm thấy reservation đã được tạo khi duyệt phiếu xuất.");
    }

    private async Task<StockReservation> ReserveCoreAsync(string sourceType, int? sourceId, string? sourceCode, int warehouseId, int productId, decimal quantity, int userId, DateTime expiresAt, CancellationToken cancellationToken)
    {
        await context.AcquireInventoryAllocationLocksAsync([(warehouseId, productId)], cancellationToken);
        var stale = await context.StockReservations
            .Where(x => x.ProductId == productId &&
                        x.WarehouseId == warehouseId &&
                        x.SourceType == "Manual" &&
                        x.ExpiresAt <= DateTime.UtcNow &&
                        (x.Status == StockReservationStatus.Active ||
                         x.Status == StockReservationStatus.PartiallyConsumed ||
                         x.Status == StockReservationStatus.PartiallyAllocated ||
                         x.Status == StockReservationStatus.Allocated) &&
                        !context.PickingTaskLines.Any(line =>
                            line.Allocation.ReservationId == x.Id &&
                            line.PickingTask.Status != PickingTaskStatus.Cancelled))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        foreach (var item in stale) await ReleaseCoreAsync(item, null, userId, "Expired before availability check", StockReservationStatus.Expired, cancellationToken);
        if (!await stockRepository.TryReserveAsync(productId, warehouseId, quantity, cancellationToken))
            throw new ConcurrencyException("Không đủ tồn khả dụng để giữ hàng.", "INSUFFICIENT_AVAILABLE_STOCK");

        var productUom = await context.Products.AsNoTracking()
            .Where(x => x.Id == productId)
            .Select(x => new
            {
                x.UnitId,
                UnitCode = x.Unit != null ? x.Unit.Code : "THÙNG",
                DecimalPlaces = x.Unit != null ? x.Unit.DecimalPlaces : 0
            })
            .FirstOrDefaultAsync(cancellationToken);

        var reservation = new StockReservation
        {
            ReservationCode = $"RSV-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..34].ToUpperInvariant(),
            ProductId = productId,
            WarehouseId = warehouseId,
            Quantity = quantity,
            BaseUomIdSnapshot = productUom?.UnitId ?? 1,
            BaseUomCodeSnapshot = string.IsNullOrWhiteSpace(productUom?.UnitCode) ? "THÙNG" : productUom.UnitCode.Trim(),
            BaseUomDecimalPlacesSnapshot = productUom?.DecimalPlaces ?? 0,
            Status = StockReservationStatus.Active,
            SourceType = sourceType,
            SourceId = sourceId,
            SourceCode = sourceCode,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId,
            ExpiresAt = expiresAt
        };
        context.StockReservations.Add(reservation);
        await context.SaveChangesAsync(cancellationToken);
        context.AuditLogs.Add(Audit(userId, "StockReservation.Created", reservation, $"Quantity: {quantity}, Source: {sourceType}/{sourceId}"));
        return reservation;
    }

    public async Task<IReadOnlyList<InventoryStockConsumption>> ConsumeAsync(StockReservation reservation, int userId, CancellationToken cancellationToken = default)
    {
        await context.AcquireInventoryAllocationLocksAsync([(reservation.WarehouseId, reservation.ProductId)], cancellationToken);
        var now = DateTime.UtcNow;
        var expired = string.Equals(reservation.SourceType, "Manual", StringComparison.OrdinalIgnoreCase) && reservation.ExpiresAt <= now;
        var completedPickingProtectsExpiry = expired && await context.PickingTaskLines.AsNoTracking().AnyAsync(
            line => line.Allocation.ReservationId == reservation.Id &&
                    line.PickingTask.Status == PickingTaskStatus.Completed,
            cancellationToken);
        if ((expired && !completedPickingProtectsExpiry) ||
            reservation.Status is not StockReservationStatus.Active and
                not StockReservationStatus.PartiallyConsumed and
                not StockReservationStatus.PartiallyAllocated and
                not StockReservationStatus.Allocated)
            throw new ConcurrencyException("Reservation đã hết hạn hoặc không còn hiệu lực.");
        var remaining = reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity;
        if (remaining <= 0)
            throw new ConcurrencyException("Reservation không còn số lượng để tiêu thụ.");

        var allocations = await context.StockAllocations
            .Where(x => x.ReservationId == reservation.Id &&
                        (x.Status == StockAllocationStatus.Active || x.Status == StockAllocationStatus.Picking || x.Status == StockAllocationStatus.Picked))
            .OrderBy(x => x.LocationId).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        IReadOnlyList<InventoryStockConsumption> consumptions;
        if (allocations.Count > 0)
        {
            if (allocations.Sum(x => x.Quantity) != remaining || reservation.AllocatedQuantity != remaining)
                throw new ConcurrencyException("Reservation đang được Allocation một phần. Hãy hoàn tất hoặc giải phóng Allocation trước khi tiêu thụ.");

            var consumed = new List<InventoryStockConsumption>();
            foreach (var allocation in allocations)
            {
                if (!await stockRepository.TryConsumeReservationAtBucketAsync(
                        reservation.ProductId,
                        reservation.WarehouseId,
                        allocation.LocationId,
                        allocation.InventoryStatus,
                        allocation.LotId,
                        allocation.SerialId,
                        allocation.Quantity,
                        cancellationToken))
                    throw new ConcurrencyException("Bucket Allocation Lot/Serial/Status đã thay đổi trước khi tiêu thụ.");

                if (!context.Database.IsRelational())
                {
                    var bucketStock = await context.InventoryStocks.FirstOrDefaultAsync(
                        x => x.ProductId == reservation.ProductId &&
                             x.WarehouseId == reservation.WarehouseId &&
                             x.LocationId == allocation.LocationId &&
                             x.Status == allocation.InventoryStatus &&
                             x.LotId == allocation.LotId &&
                             x.SerialId == allocation.SerialId,
                        cancellationToken);
                    if (bucketStock is not null)
                    {
                        bucketStock.AllocatedQuantity = Math.Max(0m, bucketStock.AllocatedQuantity - allocation.Quantity);
                    }
                }

                consumed.Add(new InventoryStockConsumption(
                    allocation.LocationId,
                    allocation.Quantity,
                    allocation.InventoryStatus,
                    allocation.LotId,
                    allocation.SerialId));
                allocation.Status = StockAllocationStatus.Consumed;
                allocation.Version++;
                context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "StockAllocation.Consumed",
                    EntityName = "StockAllocation",
                    EntityId = allocation.Id,
                    WarehouseId = reservation.WarehouseId,
                    Timestamp = now,
                    NewValues = $"Allocation: {allocation.AllocationCode}; Quantity: {allocation.Quantity}",
                    Result = "Success",
                    Severity = "Information"
                });
            }
            reservation.AllocatedQuantity = 0;
            reservation.AllocationVersion++;
            consumptions = consumed;
        }
        else
        {
            consumptions = await stockRepository.ConsumeReservationWithBreakdownAsync(
                reservation.ProductId, reservation.WarehouseId, remaining, cancellationToken);
            if (consumptions.Count == 0 || consumptions.Sum(x => x.Quantity) != remaining)
                throw new ConcurrencyException("Reservation không thể được tiêu thụ do dữ liệu tồn kho đã thay đổi.");
        }

        reservation.ConsumedQuantity += remaining;
        reservation.ConsumedAt = now;
        reservation.Status = StockReservationStatus.Consumed;
        context.AuditLogs.Add(Audit(userId, "StockReservation.Consumed", reservation, $"Quantity: {remaining}"));
        return consumptions;
    }

    public async Task ReleaseSourceAsync(string sourceType, int sourceId, int userId, string reason, CancellationToken cancellationToken = default)
    {
        var reservations = await context.StockReservations
            .Where(x => x.SourceType == sourceType &&
                        x.SourceId == sourceId &&
                        (x.Status == StockReservationStatus.Active ||
                         x.Status == StockReservationStatus.PartiallyConsumed ||
                         x.Status == StockReservationStatus.PartiallyAllocated ||
                         x.Status == StockReservationStatus.Allocated))
            .OrderBy(x => x.WarehouseId)
            .ThenBy(x => x.ProductId)
            .ToListAsync(cancellationToken);
        await context.AcquireInventoryAllocationLocksAsync(
            reservations.Select(x => (x.WarehouseId, x.ProductId)),
            cancellationToken);
        foreach (var reservation in reservations)
            await ReleaseCoreAsync(
                reservation,
                null,
                userId,
                reason,
                StockReservationStatus.Cancelled,
                cancellationToken,
                sourceWide: true);
    }

    public async Task ReleaseAsync(int id, ReleaseStockReservationDto request, CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            await unitOfWork.BeginTransactionAsync();
            try
            {
                var reservation = await ScopedQuery().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy reservation.");
                if (!string.Equals(reservation.SourceType, "Manual", StringComparison.OrdinalIgnoreCase))
                    throw new ConcurrencyException(
                        "Chỉ được giải phóng thủ công reservation Manual; reservation thuộc chứng từ xuất phải được xử lý theo vòng đời chứng từ.",
                        "DOCUMENT_OWNED_RESERVATION");
                await context.AcquireInventoryAllocationLocksAsync([(reservation.WarehouseId, reservation.ProductId)], cancellationToken);
                await ReleaseCoreAsync(reservation, request.Quantity, currentUser.UserId, request.Reason, StockReservationStatus.Released, cancellationToken);
                await unitOfWork.CommitTransactionAsync();
                return;
            }
            catch (DeadlockException) when (attempt < maxAttempts)
            {
                await unitOfWork.RollbackTransactionAsync();
                await Task.Delay(15 * attempt, cancellationToken);
            }
            catch
            {
                await unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }
    }

    private async Task ReleaseCoreAsync(
        StockReservation reservation,
        decimal? requested,
        int userId,
        string reason,
        StockReservationStatus finalStatus,
        CancellationToken cancellationToken,
        bool sourceWide = false)
    {
        await context.AcquireInventoryAllocationLocksAsync([(reservation.WarehouseId, reservation.ProductId)], cancellationToken);
        if (reservation.Status is not StockReservationStatus.Active and
            not StockReservationStatus.PartiallyConsumed and
            not StockReservationStatus.PartiallyAllocated and
            not StockReservationStatus.Allocated)
            throw new ConcurrencyException("Reservation không còn có thể giải phóng.");

        var remaining = reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity;
        var quantity = requested ?? remaining;
        if (quantity <= 0 || quantity > remaining)
            throw new BusinessRuleException("Số lượng giải phóng không hợp lệ.");

        var releaseAll = quantity == remaining;
        if (releaseAll)
        {
            if (pickingTaskIntegration is not null)
                await pickingTaskIntegration.PrepareReservationReleaseAsync(
                    reservation.Id,
                    userId,
                    reason,
                    sourceWide,
                    cancellationToken);
            await ReleaseActiveAllocationsAsync(reservation, userId, reason, cancellationToken);
        }
        else
        {
            var unallocated = remaining - reservation.AllocatedQuantity;
            if (quantity > unallocated)
                throw new BusinessRuleException("Số lượng giải phóng vượt phần reservation chưa Allocation. Hãy giải phóng Allocation trước.");
        }

        if (!await stockRepository.TryReleaseReservationAsync(
                reservation.ProductId,
                reservation.WarehouseId,
                quantity,
                releaseAll ? reservation.Id : null,
                cancellationToken))
            throw new ConcurrencyException("Dữ liệu giữ hàng đã thay đổi.");

        reservation.ReleasedQuantity += quantity;
        reservation.ReleasedAt = DateTime.UtcNow;
        reservation.ReleasedBy = userId;
        reservation.ReleaseReason = reason;

        if (reservation.ConsumedQuantity + reservation.ReleasedQuantity == reservation.Quantity)
            reservation.Status = finalStatus;
        else
            UpdateActiveReservationStatus(reservation);

        context.AuditLogs.Add(Audit(userId, "StockReservation.Released", reservation, $"Quantity: {quantity}, Reason: {reason}"));
    }

    private async Task ReleaseActiveAllocationsAsync(StockReservation reservation, int userId, string reason, CancellationToken cancellationToken)
    {
        var allocations = await context.StockAllocations
            .Where(x => x.ReservationId == reservation.Id &&
                        (x.Status == StockAllocationStatus.Active ||
                         x.Status == StockAllocationStatus.Picking ||
                         x.Status == StockAllocationStatus.Picked))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        if (allocations.Count == 0)
        {
            if (reservation.AllocatedQuantity != 0)
                throw new ConcurrencyException("Dữ liệu Allocation của reservation cần được đối soát.");
            return;
        }

        var activeQuantity = allocations.Sum(x => x.Quantity);
        if (activeQuantity != reservation.AllocatedQuantity)
            throw new ConcurrencyException("Dữ liệu Allocation của reservation cần được đối soát.");

        foreach (var allocation in allocations)
        {
            allocation.Status = StockAllocationStatus.Released;
            allocation.ReleasedAt = DateTime.UtcNow;
            allocation.ReleasedBy = userId;
            allocation.ReleaseReason = reason;
            allocation.Version++;

            if (context.Database.IsRelational())
            {
                await context.InventoryStocks
                    .Where(x => x.ProductId == reservation.ProductId &&
                                x.WarehouseId == reservation.WarehouseId &&
                                x.LocationId == allocation.LocationId &&
                                x.Status == allocation.InventoryStatus &&
                                x.LotId == allocation.LotId &&
                                x.SerialId == allocation.SerialId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.AllocatedQuantity, x => x.AllocatedQuantity >= allocation.Quantity ? x.AllocatedQuantity - allocation.Quantity : 0m),
                        cancellationToken);
            }
            else
            {
                var bucketStock = await context.InventoryStocks.FirstOrDefaultAsync(
                    x => x.ProductId == reservation.ProductId &&
                         x.WarehouseId == reservation.WarehouseId &&
                         x.LocationId == allocation.LocationId &&
                         x.Status == allocation.InventoryStatus &&
                         x.LotId == allocation.LotId &&
                         x.SerialId == allocation.SerialId,
                    cancellationToken);
                if (bucketStock is not null)
                {
                    bucketStock.AllocatedQuantity = Math.Max(0m, bucketStock.AllocatedQuantity - allocation.Quantity);
                }
            }

            context.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "StockAllocation.ReleasedWithReservation",
                EntityName = "StockAllocation",
                EntityId = allocation.Id,
                WarehouseId = reservation.WarehouseId,
                Timestamp = DateTime.UtcNow,
                NewValues = $"Allocation: {allocation.AllocationCode}; Quantity: {allocation.Quantity}; Reason: {reason}",
                Result = "Success",
                Severity = "Information"
            });
        }

        reservation.AllocatedQuantity = 0;
        reservation.AllocationVersion++;
    }

    private static void UpdateActiveReservationStatus(StockReservation reservation)
    {
        var remaining = reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity;
        if (reservation.AllocatedQuantity <= 0)
            reservation.Status = reservation.ConsumedQuantity > 0
                ? StockReservationStatus.PartiallyConsumed
                : StockReservationStatus.Active;
        else if (reservation.AllocatedQuantity < remaining)
            reservation.Status = StockReservationStatus.PartiallyAllocated;
        else
            reservation.Status = StockReservationStatus.Allocated;
    }

    public async Task<int> ExpireAsync(CancellationToken cancellationToken = default)
    {
        await unitOfWork.BeginTransactionAsync();
        try
        {
            var expired = await ScopedQuery()
                .Where(x => x.SourceType == "Manual" &&
                            x.ExpiresAt <= DateTime.UtcNow &&
                            (x.Status == StockReservationStatus.Active ||
                             x.Status == StockReservationStatus.PartiallyConsumed ||
                             x.Status == StockReservationStatus.PartiallyAllocated ||
                             x.Status == StockReservationStatus.Allocated) &&
                            !context.PickingTaskLines.Any(line =>
                                line.Allocation.ReservationId == x.Id &&
                                line.PickingTask.Status != PickingTaskStatus.Cancelled))
                .OrderBy(x => x.WarehouseId)
                .ThenBy(x => x.ProductId)
                .ToListAsync(cancellationToken);
            await context.AcquireInventoryAllocationLocksAsync(
                expired.Select(x => (x.WarehouseId, x.ProductId)),
                cancellationToken);
            foreach (var item in expired) await ReleaseCoreAsync(item, null, currentUser.UserId, "Expired", StockReservationStatus.Expired, cancellationToken);
            await unitOfWork.CommitTransactionAsync();
            return expired.Count;
        }
        catch { await unitOfWork.RollbackTransactionAsync(); throw; }
    }

    public async Task<StockReservationPageDto> GetPageAsync(int page, int pageSize, int? warehouseId, int? productId, string? status, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        if (warehouseId.HasValue)
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
        var query = ScopedQuery().AsNoTracking();
        if (warehouseId.HasValue) query = query.Where(x => x.WarehouseId == warehouseId);
        if (productId.HasValue) query = query.Where(x => x.ProductId == productId);
        if (Enum.TryParse<StockReservationStatus>(status, true, out var parsed)) query = query.Where(x => x.Status == parsed);
        var total = await query.CountAsync(cancellationToken);
        var entities = await query.Include(x => x.Product).Include(x => x.Warehouse).OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var items = entities.Select(Map).ToList();
        return new() { Items = items, TotalRecords = total, PageIndex = page, PageSize = pageSize };
    }

    public async Task<StockReservationDto> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await context.StockReservations.AsNoTracking().Include(x => x.Product).Include(x => x.Warehouse).FirstOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new NotFoundException("Không tìm thấy reservation.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.WarehouseId, cancellationToken);
        return Map(entity);
    }

    public async Task<IReadOnlyList<ReservationReconciliationIssueDto>> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        var issues = new List<ReservationReconciliationIssueDto>();
        var ledger = await context.StockReservations.AsNoTracking()
            .Where(x => allowed.Contains(x.WarehouseId) &&
                (x.Status == StockReservationStatus.Active ||
                 x.Status == StockReservationStatus.PartiallyConsumed ||
                 x.Status == StockReservationStatus.PartiallyAllocated ||
                 x.Status == StockReservationStatus.Allocated))
            .GroupBy(x => new { x.ProductId, x.WarehouseId })
            .Select(g => new { g.Key.ProductId, g.Key.WarehouseId, Reserved = g.Sum(x => x.Quantity - x.ConsumedQuantity - x.ReleasedQuantity) })
            .ToListAsync(cancellationToken);
        var stocks = await context.InventoryStocks.AsNoTracking()
            .Where(x => allowed.Contains(x.WarehouseId) &&
                (x.Status == ERP.Domain.Enums.InventoryStatus.Available || x.ReservedQuantity != 0))
            .GroupBy(x => new { x.ProductId, x.WarehouseId })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.WarehouseId,
                BucketCount = g.Count(),
                Reserved = g.Sum(x => x.ReservedQuantity),
                HasNegativeAvailable = g.Any(x => x.Quantity - x.ReservedQuantity < 0)
            })
            .ToListAsync(cancellationToken);
        foreach (var key in ledger.Select(x => (x.ProductId, x.WarehouseId)).Union(stocks.Select(x => (x.ProductId, x.WarehouseId))))
        {
            var l = ledger.FirstOrDefault(x => x.ProductId == key.ProductId && x.WarehouseId == key.WarehouseId)?.Reserved ?? 0;
            var stockGroup = stocks.FirstOrDefault(x => x.ProductId == key.ProductId && x.WarehouseId == key.WarehouseId);
            var reserved = stockGroup?.Reserved ?? 0;
            if (stockGroup is null || stockGroup.BucketCount == 0 || l != reserved || stockGroup.HasNegativeAvailable)
            {
                issues.Add(new()
                {
                    Issue = stockGroup is null || stockGroup.BucketCount == 0
                        ? "MissingStock"
                        : l != reserved
                            ? "LedgerMismatch"
                            : "NegativeAvailable",
                    ProductId = key.ProductId,
                    WarehouseId = key.WarehouseId,
                    LedgerReserved = l,
                    StockReserved = stockGroup is null || stockGroup.BucketCount == 0 ? null : reserved
                });
            }
        }
        var invalidReservations = await context.StockReservations.AsNoTracking()
            .Where(x => allowed.Contains(x.WarehouseId) &&
                ((x.Status == StockReservationStatus.Active || x.Status == StockReservationStatus.PartiallyConsumed || x.Status == StockReservationStatus.PartiallyAllocated || x.Status == StockReservationStatus.Allocated) &&
                 (x.Quantity - x.ConsumedQuantity - x.ReleasedQuantity <= 0 ||
                  (x.SourceType == "Manual" && x.ExpiresAt <= DateTime.UtcNow &&
                   !context.PickingTaskLines.Any(line =>
                       line.Allocation.ReservationId == x.Id &&
                       line.PickingTask.Status != PickingTaskStatus.Cancelled)))))
            .Select(x => new ReservationReconciliationIssueDto { Issue = x.SourceType == "Manual" && x.ExpiresAt <= DateTime.UtcNow ? "ExpiredStillActive" : "ActiveWithoutRemaining", ReservationId = x.Id, ProductId = x.ProductId, WarehouseId = x.WarehouseId })
            .ToListAsync(cancellationToken);
        issues.AddRange(invalidReservations);
        var invalidExportSources = await context.StockReservations.AsNoTracking()
            .Where(x => allowed.Contains(x.WarehouseId) && x.SourceType == "ExportReceipt" &&
                (!x.SourceId.HasValue || !context.ExportReceipts.Any(e => e.Id == x.SourceId && e.WarehouseId == x.WarehouseId) ||
                 !context.ExportReceiptDetails.Any(d => d.ExportReceiptId == x.SourceId && d.ProductId == x.ProductId)))
            .Select(x => new ReservationReconciliationIssueDto { Issue = "InvalidSourceReference", ReservationId = x.Id, ProductId = x.ProductId, WarehouseId = x.WarehouseId })
            .ToListAsync(cancellationToken);
        issues.AddRange(invalidExportSources);
        return issues;
    }

    public async Task<int> RebuildBalancesAsync(int? warehouseId = null, int? productId = null, CancellationToken cancellationToken = default)
    {
        var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (warehouseId.HasValue)
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            allowed = [warehouseId.Value];
        }

        var stocksQuery = context.InventoryStocks
            .Include(x => x.Location)
            .Where(x => allowed.Contains(x.WarehouseId));
        if (productId.HasValue)
            stocksQuery = stocksQuery.Where(x => x.ProductId == productId.Value);

        var stocks = await stocksQuery.OrderBy(x => x.WarehouseId).ThenBy(x => x.ProductId).ThenBy(x => x.Id).ToListAsync(cancellationToken);

        var activeAllocations = await context.StockAllocations.AsNoTracking()
            .Where(x => allowed.Contains(x.WarehouseId) &&
                        (!productId.HasValue || x.ProductId == productId.Value) &&
                        (x.Status == StockAllocationStatus.Active ||
                         x.Status == StockAllocationStatus.Picking ||
                         x.Status == StockAllocationStatus.Picked))
            .ToListAsync(cancellationToken);

        var activeReservations = await context.StockReservations.AsNoTracking()
            .Where(x => allowed.Contains(x.WarehouseId) &&
                        (!productId.HasValue || x.ProductId == productId.Value) &&
                        (x.Status == StockReservationStatus.Active ||
                         x.Status == StockReservationStatus.PartiallyConsumed ||
                         x.Status == StockReservationStatus.PartiallyAllocated ||
                         x.Status == StockReservationStatus.Allocated))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var activeLocks = await context.InventoryLocks.AsNoTracking()
            .EffectiveAt(now)
            .Where(x => allowed.Contains(x.WarehouseId) &&
                        (!productId.HasValue || !x.ProductId.HasValue || x.ProductId == productId.Value))
            .ToListAsync(cancellationToken);

        var ledgerTransactions = await context.InventoryTransactions.AsNoTracking()
            .Where(x => allowed.Contains(x.WarehouseId) &&
                        (!productId.HasValue || x.ProductId == productId.Value))
            .ToListAsync(cancellationToken);

        foreach (var txGroup in ledgerTransactions.GroupBy(t => (t.WarehouseId, t.ProductId)))
        {
            var existingGroupStocks = stocks
                .Where(s => s.WarehouseId == txGroup.Key.WarehouseId && s.ProductId == txGroup.Key.ProductId)
                .ToList();
            var enforceHu = txGroup.Any(t => t.HandlingUnitId.HasValue) &&
                            existingGroupStocks.Select(b => b.HandlingUnitId).Distinct().Count() > 1;
            var enforceOwner = txGroup.Any(t => t.OwnerId.HasValue) &&
                               existingGroupStocks.Select(b => b.OwnerId).Distinct().Count() > 1;

            var candidateGrains = new List<(int TenantId, int WarehouseId, int? LocationId, int ProductId, int? LotId, int? SerialId, int? HandlingUnitId, InventoryStatus Status, int? OwnerId)>();
            foreach (var tx in txGroup)
            {
                if (tx.TransactionType == TransactionType.TransferAdjustment || tx.TransactionType == TransactionType.Reversal)
                    continue;

                var huId = enforceHu ? tx.HandlingUnitId : null;
                var ownId = enforceOwner ? tx.OwnerId : null;

                if (tx.TransactionType == TransactionType.Move)
                {
                    if (tx.LocationId.HasValue)
                        candidateGrains.Add((tx.TenantId, tx.WarehouseId, tx.LocationId, tx.ProductId, tx.LotId, tx.SerialId, huId, tx.InventoryStatus, ownId));
                    if (tx.FromLocationId.HasValue)
                        candidateGrains.Add((tx.TenantId, tx.WarehouseId, tx.FromLocationId, tx.ProductId, tx.LotId, tx.SerialId, huId, tx.FromInventoryStatus ?? tx.InventoryStatus, ownId));
                    if (tx.ToLocationId.HasValue)
                        candidateGrains.Add((tx.TenantId, tx.WarehouseId, tx.ToLocationId, tx.ProductId, tx.LotId, tx.SerialId, huId, tx.ToInventoryStatus ?? tx.InventoryStatus, ownId));
                }
                else if (tx.TransactionType == TransactionType.StatusChange)
                {
                    if (tx.LocationId.HasValue && tx.FromInventoryStatus.HasValue)
                        candidateGrains.Add((tx.TenantId, tx.WarehouseId, tx.LocationId, tx.ProductId, tx.LotId, tx.SerialId, huId, tx.FromInventoryStatus.Value, ownId));
                    if (tx.LocationId.HasValue && tx.ToInventoryStatus.HasValue)
                        candidateGrains.Add((tx.TenantId, tx.WarehouseId, tx.LocationId, tx.ProductId, tx.LotId, tx.SerialId, huId, tx.ToInventoryStatus.Value, ownId));
                }
                else if (tx.LocationId.HasValue)
                {
                    candidateGrains.Add((tx.TenantId, tx.WarehouseId, tx.LocationId, tx.ProductId, tx.LotId, tx.SerialId, huId, tx.InventoryStatus, ownId));
                }
            }

            foreach (var candidate in candidateGrains.Distinct())
            {
                var exists = stocks.Any(s =>
                    s.TenantId == candidate.TenantId &&
                    s.WarehouseId == candidate.WarehouseId &&
                    s.LocationId == candidate.LocationId &&
                    s.ProductId == candidate.ProductId &&
                    s.LotId == candidate.LotId &&
                    s.SerialId == candidate.SerialId &&
                    (!enforceHu || s.HandlingUnitId == candidate.HandlingUnitId) &&
                    s.Status == candidate.Status &&
                    (!enforceOwner || s.OwnerId == candidate.OwnerId));

                if (!exists)
                {
                    var newStock = new InventoryStock
                    {
                        TenantId = candidate.TenantId,
                        WarehouseId = candidate.WarehouseId,
                        LocationId = candidate.LocationId,
                        ProductId = candidate.ProductId,
                        LotId = candidate.LotId,
                        SerialId = candidate.SerialId,
                        HandlingUnitId = candidate.HandlingUnitId,
                        Status = candidate.Status,
                        OwnerId = candidate.OwnerId,
                        Quantity = 0m,
                        ReservedQuantity = 0m,
                        AllocatedQuantity = 0m,
                        LockedQuantity = 0m,
                        LastUpdated = now
                    };
                    newStock.RefreshGrainHash();
                    context.InventoryStocks.Add(newStock);
                    stocks.Add(newStock);
                }
            }
        }

        if (stocks.Count == 0) return 0;

        foreach (var group in stocks.GroupBy(x => (x.WarehouseId, x.ProductId)))
        {
            var groupTxs = ledgerTransactions
                .Where(t => t.WarehouseId == group.Key.WarehouseId && t.ProductId == group.Key.ProductId)
                .ToList();

            var totalReservedTarget = activeReservations
                .Where(r => r.WarehouseId == group.Key.WarehouseId && r.ProductId == group.Key.ProductId)
                .Sum(r => Math.Max(0m, r.Quantity - r.ConsumedQuantity - r.ReleasedQuantity));

            var enforceHuDimension = groupTxs.Any(t => t.HandlingUnitId.HasValue) &&
                                     group.Select(b => b.HandlingUnitId).Distinct().Count() > 1;
            var enforceOwnerDimension = groupTxs.Any(t => t.OwnerId.HasValue) &&
                                        group.Select(b => b.OwnerId).Distinct().Count() > 1;

            decimal allocatedSum = 0m;
            foreach (var bucket in group)
            {
                var bucketTxs = groupTxs.Where(t =>
                    t.TenantId == bucket.TenantId &&
                    t.WarehouseId == bucket.WarehouseId &&
                    t.ProductId == bucket.ProductId &&
                    t.LotId == bucket.LotId &&
                    t.SerialId == bucket.SerialId &&
                    (!enforceHuDimension || t.HandlingUnitId == bucket.HandlingUnitId) &&
                    (!enforceOwnerDimension || t.OwnerId == bucket.OwnerId) &&
                    (t.LocationId == bucket.LocationId ||
                     (t.TransactionType == TransactionType.Move &&
                      (t.FromLocationId == bucket.LocationId || t.ToLocationId == bucket.LocationId))) &&
                    (t.InventoryStatus == bucket.Status ||
                     t.FromInventoryStatus == bucket.Status ||
                     t.ToInventoryStatus == bucket.Status)).ToList();

                var hasExplicitMoveLegsForBucket = bucketTxs.Any(t =>
                    t.TransactionType == TransactionType.Move &&
                    t.LocationId == bucket.LocationId);
                var hasMatchingLedgerTx = bucketTxs.Count > 0;

                if (hasMatchingLedgerTx)
                {
                    decimal ledgerQty = 0m;
                    foreach (var tx in bucketTxs)
                    {
                        if (tx.TransactionType == TransactionType.TransferAdjustment || tx.TransactionType == TransactionType.Reversal)
                            continue;
                        if (tx.TransactionType == TransactionType.StatusChange)
                        {
                            if (tx.LocationId != bucket.LocationId) continue;
                            if (tx.ToInventoryStatus == bucket.Status && tx.Quantity > 0)
                                ledgerQty += tx.Quantity;
                            else if (tx.FromInventoryStatus == bucket.Status)
                                ledgerQty -= Math.Abs(tx.Quantity);
                        }
                        else if (tx.TransactionType == TransactionType.Move)
                        {
                            if (tx.InventoryStatus != bucket.Status &&
                                tx.FromInventoryStatus != bucket.Status &&
                                tx.ToInventoryStatus != bucket.Status)
                                continue;

                            if (tx.LocationId == bucket.LocationId)
                            {
                                if (tx.Quantity < 0m)
                                {
                                    ledgerQty += tx.Quantity;
                                }
                                else if (tx.FromLocationId == bucket.LocationId &&
                                         tx.ToLocationId.HasValue &&
                                         tx.ToLocationId != bucket.LocationId &&
                                         !groupTxs.Any(other =>
                                             other.Id != tx.Id &&
                                             other.TransactionType == TransactionType.Move &&
                                             other.LocationId == tx.ToLocationId &&
                                             other.FromLocationId == tx.FromLocationId &&
                                             other.ToLocationId == tx.ToLocationId &&
                                             Math.Abs(other.Quantity) == Math.Abs(tx.Quantity)))
                                {
                                    ledgerQty -= tx.Quantity;
                                }
                                else
                                {
                                    ledgerQty += tx.Quantity;
                                }
                            }
                            else if (!hasExplicitMoveLegsForBucket)
                            {
                                if (tx.FromLocationId == bucket.LocationId)
                                    ledgerQty -= Math.Abs(tx.Quantity);
                                else if (tx.ToLocationId == bucket.LocationId)
                                    ledgerQty += Math.Abs(tx.Quantity);
                            }
                        }
                        else if (tx.LocationId == bucket.LocationId && tx.InventoryStatus == bucket.Status)
                        {
                            if (tx.Quantity < 0m)
                                ledgerQty += tx.Quantity;
                            else
                                ledgerQty += tx.TransactionType.ApplySign(tx.Quantity);
                        }
                    }

                    if (hasMatchingLedgerTx && ledgerQty >= 0m)
                    {
                        bucket.Quantity = ledgerQty;
                    }
                    else if (ledgerQty < 0m)
                    {
                        throw new ERP.Domain.Exceptions.UnprocessableDomainException(
                            $"Phát hiện số dư sổ cái 9 chiều bị âm ({ledgerQty:0.0000}) tại Kho #{bucket.WarehouseId}, Sản phẩm #{bucket.ProductId}, Vị trí #{bucket.LocationId}, Lô #{bucket.LotId?.ToString() ?? "NULL"}.",
                            "INV_NEGATIVE_LEDGER_BUCKET_REBUILD");
                    }
                }
                else if (groupTxs.Count > 0 || bucket.Quantity < 0m)
                {
                    bucket.Quantity = 0m;
                }

                var bucketAllocated = activeAllocations
                    .Where(a => a.WarehouseId == bucket.WarehouseId &&
                                a.ProductId == bucket.ProductId &&
                                a.LocationId == bucket.LocationId &&
                                a.InventoryStatus == bucket.Status &&
                                a.LotId == bucket.LotId &&
                                a.SerialId == bucket.SerialId &&
                                (!a.HandlingUnitId.HasValue || a.HandlingUnitId == bucket.HandlingUnitId) &&
                                (!a.OwnerId.HasValue || a.OwnerId == bucket.OwnerId))
                    .Sum(a => a.Quantity);

                var isLocked = activeLocks.Any(l =>
                    l.WarehouseId == bucket.WarehouseId &&
                    (!l.LocationId.HasValue || l.LocationId == bucket.LocationId) &&
                    (!l.ProductId.HasValue || l.ProductId == bucket.ProductId) &&
                    (!l.InventoryStatus.HasValue || l.InventoryStatus == bucket.Status) &&
                    (!l.LotId.HasValue || l.LotId == bucket.LotId) &&
                    (!l.SerialId.HasValue || l.SerialId == bucket.SerialId));

                bucket.AllocatedQuantity = Math.Min(bucket.Quantity, bucketAllocated);
                bucket.LockedQuantity = isLocked ? bucket.Quantity : 0m;
                bucket.ReservedQuantity = bucket.AllocatedQuantity;
                allocatedSum += bucket.AllocatedQuantity;
            }

            var remainingUnallocatedReserve = Math.Max(0m, totalReservedTarget - allocatedSum);
            foreach (var bucket in group
                .Where(b => b.Status == InventoryStatus.Available && b.LockedQuantity == 0)
                .OrderBy(b => b.Location?.LocationType == WarehouseLocationType.Receiving ? 1 : 0)
                .ThenBy(b => b.LocationId)
                .ThenBy(b => b.Id))
            {
                if (remainingUnallocatedReserve <= 0) break;
                var freeCapacity = Math.Max(0m, bucket.Quantity - bucket.ReservedQuantity);
                var add = Math.Min(remainingUnallocatedReserve, freeCapacity);
                if (add > 0)
                {
                    bucket.ReservedQuantity += add;
                    remainingUnallocatedReserve -= add;
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        return stocks.Count;
    }

    private IQueryable<StockReservation> ScopedQuery()
    {
        if (currentUser.IsGlobalAdmin) return context.StockReservations;
        return context.StockReservations.Where(x => context.UserWarehouses.Any(a => a.UserId == currentUser.UserId && a.WarehouseId == x.WarehouseId));
    }
    private static void Validate(CreateStockReservationDto x) { if (x.ProductId <= 0 || x.WarehouseId <= 0 || x.Quantity <= 0) throw new BusinessRuleException("Sản phẩm, kho và số lượng giữ phải hợp lệ."); }
    private static StockReservationDto Map(StockReservation x) => new() { Id = x.Id, ReservationCode = x.ReservationCode.Trim(), ProductId = x.ProductId, ProductCode = (x.Product?.Code ?? string.Empty).Trim(), ProductName = (x.Product?.Name ?? string.Empty).Trim(), WarehouseId = x.WarehouseId, WarehouseName = (x.Warehouse?.Name ?? string.Empty).Trim(), Quantity = x.Quantity, ConsumedQuantity = x.ConsumedQuantity, ReleasedQuantity = x.ReleasedQuantity, AllocatedQuantity = x.AllocatedQuantity, Status = x.Status.ToString(), SourceType = x.SourceType.Trim(), SourceId = x.SourceId, SourceCode = x.SourceCode?.Trim(), CreatedAt = x.CreatedAt, ExpiresAt = x.ExpiresAt };
    private static AuditLog Audit(int userId, string action, StockReservation x, string values) => new() { UserId = userId, Action = action, EntityName = "StockReservation", EntityId = x.Id, Timestamp = DateTime.UtcNow, NewValues = values };
}
