using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class StockAllocationService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser,
    IPickingTaskIntegration? pickingTaskIntegration = null) : IStockAllocationService
{
    private static readonly StockAllocationStatus[] CapacityStatuses =
    [
        StockAllocationStatus.Active,
        StockAllocationStatus.Picking,
        StockAllocationStatus.Picked
    ];

    private static readonly StockReservationStatus[] AllocatableReservationStatuses =
    [
        StockReservationStatus.Active,
        StockReservationStatus.PartiallyConsumed,
        StockReservationStatus.PartiallyAllocated,
        StockReservationStatus.Allocated
    ];

    public async Task<StockAllocationPageDto> GetPageAsync(
        int page,
        int pageSize,
        int? warehouseId,
        int? reservationId,
        string? status,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var accessible = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (warehouseId.HasValue)
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            accessible = [warehouseId.Value];
        }

        var query = AllocationQuery().AsNoTracking().Where(x => accessible.Contains(x.WarehouseId));
        if (reservationId.HasValue) query = query.Where(x => x.ReservationId == reservationId.Value);
        if (Enum.TryParse<StockAllocationStatus>(status, true, out var parsed))
            query = query.Where(x => x.Status == parsed);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.AllocatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        return new StockAllocationPageDto
        {
            Items = rows.Select(Map).ToList(),
            TotalRecords = total,
            PageIndex = page,
            PageSize = pageSize
        };
    }

    public async Task<StockAllocationDto> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await AllocationQuery().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy Allocation hoặc bạn không có quyền truy cập.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.WarehouseId, cancellationToken);
        return Map(entity);
    }

    public async Task<IReadOnlyList<AllocatableReservationDto>> GetReservationsAsync(
        int? warehouseId,
        CancellationToken cancellationToken = default)
    {
        var accessible = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (warehouseId.HasValue)
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            accessible = [warehouseId.Value];
        }

        var now = DateTime.UtcNow;
        return await context.StockReservations.AsNoTracking()
            .Where(x => accessible.Contains(x.WarehouseId) &&
                        AllocatableReservationStatuses.Contains(x.Status) &&
                        (x.SourceType != "Manual" || x.ExpiresAt > now) &&
                        x.Quantity - x.ConsumedQuantity - x.ReleasedQuantity - x.AllocatedQuantity > 0)
            .OrderBy(x => x.ExpiresAt).ThenBy(x => x.Id)
            .Select(x => new AllocatableReservationDto
            {
                ReservationId = x.Id,
                ReservationCode = x.ReservationCode.Trim(),
                SourceType = x.SourceType.Trim(),
                SourceCode = x.SourceCode != null ? x.SourceCode.Trim() : null,
                WarehouseId = x.WarehouseId,
                WarehouseName = x.Warehouse.Name.Trim(),
                ProductId = x.ProductId,
                ProductCode = x.Product.Code.Trim(),
                ProductName = x.Product.Name.Trim(),
                ReservedQuantity = x.Quantity - x.ConsumedQuantity - x.ReleasedQuantity,
                AllocatedQuantity = x.AllocatedQuantity,
                AllocatableQuantity = x.Quantity - x.ConsumedQuantity - x.ReleasedQuantity - x.AllocatedQuantity,
                ExpiresAt = x.ExpiresAt
            })
            .Take(250)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StockAllocationCandidateDto>> GetCandidatesAsync(
        int reservationId,
        CancellationToken cancellationToken = default)
    {
        var reservation = await GetReservationAsync(reservationId, false, cancellationToken);
        ValidateAllocatableReservation(reservation);
        return await BuildCandidatesAsync(reservation, null, null, cancellationToken);
    }

    public Task<IReadOnlyList<StockAllocationDto>> CreateAsync(
        CreateStockAllocationDto request,
        CancellationToken cancellationToken = default)
    {
        if (!request.LocationId.HasValue)
            throw new BusinessRuleException("Vị trí cố định là bắt buộc cho Allocation thủ công.");
        return AllocateAsync(request, AllocationStrategy.FixedLocation, request.LocationId, null, cancellationToken);
    }

    public Task<IReadOnlyList<StockAllocationDto>> AutoAllocateAsync(
        CreateStockAllocationDto request,
        CancellationToken cancellationToken = default) =>
        AllocateAsync(request, AllocationStrategy.LocationOrder, null, null, cancellationToken);

    public async Task ReleaseAsync(
        int id,
        ReleaseStockAllocationDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BusinessRuleException("Lý do giải phóng Allocation là bắt buộc.");

        await ExecuteWithSerializableRetryAsync(async token =>
        {
            var allocation = await context.StockAllocations
                .Include(x => x.Reservation)
                .SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException("Không tìm thấy Allocation hoặc bạn không có quyền truy cập.");
            await warehouseAuthorization.EnsureWarehouseAccessAsync(allocation.WarehouseId, token);
            await context.AcquireInventoryAllocationLocksAsync([(allocation.WarehouseId, allocation.ProductId)], token);

            if (allocation.Status != StockAllocationStatus.Active)
                throw new ConcurrencyException("Allocation không còn ở trạng thái có thể giải phóng.");
            if (await context.PickingTaskLines.AnyAsync(x => x.AllocationId == allocation.Id, token))
                throw new ConcurrencyException("Allocation đã được đưa vào nhiệm vụ Picking và không thể giải phóng trực tiếp.");

            allocation.Status = StockAllocationStatus.Released;
            allocation.ReleasedAt = DateTime.UtcNow;
            allocation.ReleasedBy = currentUser.UserId;
            allocation.ReleaseReason = request.Reason.Trim();
            allocation.Version++;

            var bucketStock = await context.InventoryStocks.FirstOrDefaultAsync(
                x => x.ProductId == allocation.ProductId &&
                     x.WarehouseId == allocation.WarehouseId &&
                     x.LocationId == allocation.LocationId &&
                     x.Status == allocation.InventoryStatus &&
                     x.LotId == allocation.LotId &&
                     x.SerialId == allocation.SerialId,
                token);
            if (bucketStock is not null)
            {
                bucketStock.AllocatedQuantity = Math.Max(0m, bucketStock.AllocatedQuantity - allocation.Quantity);
            }

            var reservation = allocation.Reservation;
            reservation.AllocatedQuantity -= allocation.Quantity;
            if (reservation.AllocatedQuantity < 0)
                throw new ConcurrencyException("Dữ liệu Allocation của reservation cần được đối soát.");
            reservation.AllocationVersion++;
            UpdateReservationStatus(reservation);

            context.AuditLogs.Add(Audit(
                "StockAllocation.Released",
                allocation.Id,
                allocation.WarehouseId,
                $"Allocation: {allocation.AllocationCode}; Quantity: {allocation.Quantity}; Reason: {allocation.ReleaseReason}"));
            await context.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<StockAllocationDto>> ReallocateAsync(
        int id,
        ReallocateStockAllocationDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BusinessRuleException("Lý do phân bổ lại là bắt buộc.");

        return await ExecuteWithSerializableRetryAsync(async token =>
        {
            var original = await context.StockAllocations
                .Include(x => x.Reservation)
                .SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException("Không tìm thấy Allocation hoặc bạn không có quyền truy cập.");
            await warehouseAuthorization.EnsureWarehouseAccessAsync(original.WarehouseId, token);
            await context.AcquireInventoryAllocationLocksAsync([(original.WarehouseId, original.ProductId)], token);

            if (original.Status != StockAllocationStatus.Active)
                throw new ConcurrencyException("Chỉ Allocation đang hoạt động mới có thể phân bổ lại.");
            if (await context.PickingTaskLines.AnyAsync(x => x.AllocationId == original.Id, token))
                throw new ConcurrencyException("Allocation đã được đưa vào nhiệm vụ Picking và không thể phân bổ lại trực tiếp.");
            if (request.LocationId == original.LocationId)
                throw new BusinessRuleException("Vị trí mới phải khác vị trí Allocation hiện tại.");

            original.Status = StockAllocationStatus.Reallocated;
            original.ReleasedAt = DateTime.UtcNow;
            original.ReleasedBy = currentUser.UserId;
            original.ReleaseReason = request.Reason.Trim();
            original.Version++;

            var oldBucketStock = await context.InventoryStocks.FirstOrDefaultAsync(
                x => x.ProductId == original.ProductId &&
                     x.WarehouseId == original.WarehouseId &&
                     x.LocationId == original.LocationId &&
                     x.Status == original.InventoryStatus &&
                     x.LotId == original.LotId &&
                     x.SerialId == original.SerialId,
                token);
            if (oldBucketStock is not null)
            {
                oldBucketStock.AllocatedQuantity = Math.Max(0m, oldBucketStock.AllocatedQuantity - original.Quantity);
            }

            await context.SaveChangesAsync(token);

            var strategy = request.LocationId.HasValue ? AllocationStrategy.FixedLocation : AllocationStrategy.LocationOrder;
            var candidates = await BuildCandidatesAsync(
                original.Reservation,
                request.LocationId,
                request.LocationId.HasValue ? null : original.LocationId,
                token);
            var created = CreateRows(original.Reservation, original.Quantity, strategy, candidates);
            if (created.Count == 0)
                throw new ConcurrencyException("Không tìm thấy vị trí phù hợp để phân bổ lại.");

            await SynchronizeBucketAllocationAndReservationAsync(original.Reservation, created, token);
            original.Reservation.AllocationVersion++;
            context.StockAllocations.AddRange(created);
            await context.SaveChangesAsync(token);
            AddAllocationAudits(created, "StockAllocation.Reallocated", $"FromAllocation: {original.AllocationCode}; Reason: {request.Reason.Trim()}");
            await context.SaveChangesAsync(token);
            if (pickingTaskIntegration is not null)
                await pickingTaskIntegration.EnsureForReservationAsync(original.ReservationId, currentUser.UserId, token);
            return await LoadDtosAsync(created.Select(x => x.Id).ToArray(), token);
        }, cancellationToken);
    }

    private async Task<IReadOnlyList<StockAllocationDto>> AllocateAsync(
        CreateStockAllocationDto request,
        AllocationStrategy strategy,
        int? fixedLocationId,
        int? excludedLocationId,
        CancellationToken cancellationToken)
    {
        if (request.ReservationId <= 0 || request.Quantity <= 0)
            throw new BusinessRuleException("Reservation và số lượng Allocation phải hợp lệ.");

        return await ExecuteWithSerializableRetryAsync(async token =>
        {
            var reservation = await GetReservationAsync(request.ReservationId, true, token);
            await context.AcquireInventoryAllocationLocksAsync([(reservation.WarehouseId, reservation.ProductId)], token);
            ValidateAllocatableReservation(reservation);
            var activeAllocated = await context.StockAllocations
                .Where(x => x.ReservationId == reservation.Id && CapacityStatuses.Contains(x.Status))
                .SumAsync(x => (decimal?)x.Quantity, token) ?? 0m;
            if (activeAllocated != reservation.AllocatedQuantity)
                throw new ConcurrencyException("Dữ liệu Allocation của reservation cần được đối soát.");

            var allocatable = reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity - activeAllocated;
            if (request.Quantity > allocatable)
                throw new BusinessRuleException("Số lượng Allocation vượt quá phần reservation còn có thể phân bổ.");

            var candidates = await BuildCandidatesAsync(reservation, fixedLocationId, excludedLocationId, token);
            if (candidates.Sum(x => x.AllocatableQuantity) < request.Quantity)
                throw new ConcurrencyException("Không đủ bucket đã giữ hàng để tạo Allocation.", "INSUFFICIENT_AVAILABLE_STOCK");

            var rows = CreateRows(reservation, request.Quantity, strategy, candidates);
            await SynchronizeBucketAllocationAndReservationAsync(reservation, rows, token);
            reservation.AllocatedQuantity = activeAllocated + request.Quantity;
            reservation.AllocationVersion++;
            UpdateReservationStatus(reservation);

            context.StockAllocations.AddRange(rows);
            await context.SaveChangesAsync(token);
            AddAllocationAudits(rows, "StockAllocation.Created", $"Reservation: {reservation.ReservationCode}");
            await context.SaveChangesAsync(token);
            if (pickingTaskIntegration is not null)
                await pickingTaskIntegration.EnsureForReservationAsync(reservation.Id, currentUser.UserId, token);
            return await LoadDtosAsync(rows.Select(x => x.Id).ToArray(), token);
        }, cancellationToken);
    }
}
