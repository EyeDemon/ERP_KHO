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

public sealed class StockAllocationService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser) : IStockAllocationService
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
                        x.ExpiresAt > now &&
                        x.Quantity - x.ConsumedQuantity - x.ReleasedQuantity - x.AllocatedQuantity > 0)
            .OrderBy(x => x.ExpiresAt).ThenBy(x => x.Id)
            .Select(x => new AllocatableReservationDto
            {
                ReservationId = x.Id,
                ReservationCode = x.ReservationCode,
                SourceType = x.SourceType,
                SourceCode = x.SourceCode,
                WarehouseId = x.WarehouseId,
                WarehouseName = x.Warehouse.Name,
                ProductId = x.ProductId,
                ProductCode = x.Product.Code,
                ProductName = x.Product.Name,
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

            if (allocation.Status != StockAllocationStatus.Active)
                throw new ConcurrencyException("Allocation không còn ở trạng thái có thể giải phóng.");

            allocation.Status = StockAllocationStatus.Released;
            allocation.ReleasedAt = DateTime.UtcNow;
            allocation.ReleasedBy = currentUser.UserId;
            allocation.ReleaseReason = request.Reason.Trim();
            allocation.Version++;

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

            if (original.Status != StockAllocationStatus.Active)
                throw new ConcurrencyException("Chỉ Allocation đang hoạt động mới có thể phân bổ lại.");
            if (request.LocationId == original.LocationId)
                throw new BusinessRuleException("Vị trí mới phải khác vị trí Allocation hiện tại.");

            original.Status = StockAllocationStatus.Reallocated;
            original.ReleasedAt = DateTime.UtcNow;
            original.ReleasedBy = currentUser.UserId;
            original.ReleaseReason = request.Reason.Trim();
            original.Version++;
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

            original.Reservation.AllocationVersion++;
            context.StockAllocations.AddRange(created);
            await context.SaveChangesAsync(token);
            AddAllocationAudits(created, "StockAllocation.Reallocated", $"FromAllocation: {original.AllocationCode}; Reason: {request.Reason.Trim()}");
            await context.SaveChangesAsync(token);
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
                throw new ConcurrencyException("Không đủ bucket đã giữ hàng để tạo Allocation.");

            var rows = CreateRows(reservation, request.Quantity, strategy, candidates);
            reservation.AllocatedQuantity = activeAllocated + request.Quantity;
            reservation.AllocationVersion++;
            UpdateReservationStatus(reservation);

            context.StockAllocations.AddRange(rows);
            await context.SaveChangesAsync(token);
            AddAllocationAudits(rows, "StockAllocation.Created", $"Reservation: {reservation.ReservationCode}");
            await context.SaveChangesAsync(token);
            return await LoadDtosAsync(rows.Select(x => x.Id).ToArray(), token);
        }, cancellationToken);
    }

    private async Task<StockReservation> GetReservationAsync(
        int reservationId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        IQueryable<StockReservation> query = context.StockReservations;
        if (!tracking) query = query.AsNoTracking();
        var reservation = await query
            .Include(x => x.Product)
            .Include(x => x.Warehouse)
            .SingleOrDefaultAsync(x => x.Id == reservationId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy reservation hoặc bạn không có quyền truy cập.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(reservation.WarehouseId, cancellationToken);
        return reservation;
    }

    private static void ValidateAllocatableReservation(StockReservation reservation)
    {
        if (!AllocatableReservationStatuses.Contains(reservation.Status))
            throw new ConcurrencyException("Reservation không còn ở trạng thái có thể Allocation.");
        if (reservation.ExpiresAt <= DateTime.UtcNow)
            throw new ConcurrencyException("Reservation đã hết hạn.");
        var remaining = reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity;
        if (remaining <= 0)
            throw new ConcurrencyException("Reservation không còn số lượng để Allocation.");
    }

    private async Task<List<StockAllocationCandidateDto>> BuildCandidatesAsync(
        StockReservation reservation,
        int? fixedLocationId,
        int? excludedLocationId,
        CancellationToken cancellationToken)
    {
        var query = context.InventoryStocks.AsNoTracking()
            .Where(x => x.ProductId == reservation.ProductId &&
                        x.WarehouseId == reservation.WarehouseId &&
                        x.Status == InventoryStatus.Available &&
                        x.LocationId.HasValue &&
                        x.Location != null &&
                        x.Location.IsActive &&
                        !x.Location.IsBlocked &&
                        x.Location.IsPickable &&
                        x.ReservedQuantity > 0);

        if (fixedLocationId.HasValue) query = query.Where(x => x.LocationId == fixedLocationId.Value);
        if (excludedLocationId.HasValue) query = query.Where(x => x.LocationId != excludedLocationId.Value);

        var raw = await query
            .OrderBy(x => x.LocationId)
            .Select(x => new
            {
                LocationId = x.LocationId!.Value,
                LocationCode = x.Location!.Code,
                LocationName = x.Location.Name,
                x.ReservedQuantity,
                AllocatedQuantity = context.StockAllocations
                    .Where(a => a.WarehouseId == x.WarehouseId &&
                                a.ProductId == x.ProductId &&
                                a.LocationId == x.LocationId &&
                                CapacityStatuses.Contains(a.Status))
                    .Sum(a => (decimal?)a.Quantity) ?? 0m
            })
            .Take(500)
            .ToListAsync(cancellationToken);

        return raw
            .Select((x, index) => new StockAllocationCandidateDto
            {
                LocationId = x.LocationId,
                LocationCode = x.LocationCode,
                LocationName = x.LocationName,
                ReservedQuantity = x.ReservedQuantity,
                AllocatedQuantity = x.AllocatedQuantity,
                AllocatableQuantity = Math.Max(0, x.ReservedQuantity - x.AllocatedQuantity),
                Rank = index + 1,
                Reason = fixedLocationId.HasValue
                    ? "Vị trí cố định do người dùng chọn."
                    : "Tự động theo thứ tự vị trí ổn định (LocationId tăng dần). FEFO/FIFO chưa bật vì chưa có Lot/Expiry/ReceiptDate canonical."
            })
            .Where(x => x.AllocatableQuantity > 0)
            .ToList();
    }

    private List<StockAllocation> CreateRows(
        StockReservation reservation,
        decimal quantity,
        AllocationStrategy strategy,
        IReadOnlyList<StockAllocationCandidateDto> candidates)
    {
        var remaining = quantity;
        var rows = new List<StockAllocation>();
        foreach (var candidate in candidates)
        {
            var take = Math.Min(remaining, candidate.AllocatableQuantity);
            if (take <= 0) continue;
            rows.Add(new StockAllocation
            {
                AllocationCode = $"ALC-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..34].ToUpperInvariant(),
                ReservationId = reservation.Id,
                WarehouseId = reservation.WarehouseId,
                ProductId = reservation.ProductId,
                LocationId = candidate.LocationId,
                InventoryStatus = InventoryStatus.Available,
                Quantity = take,
                Status = StockAllocationStatus.Active,
                Strategy = strategy,
                SelectionReason = candidate.Reason,
                AllocatedAt = DateTime.UtcNow,
                AllocatedBy = currentUser.UserId
            });
            remaining -= take;
            if (remaining == 0) break;
        }

        if (remaining != 0)
            throw new ConcurrencyException("Không đủ candidate để hoàn tất Allocation.");
        return rows;
    }

    private void AddAllocationAudits(IEnumerable<StockAllocation> rows, string action, string extra)
    {
        foreach (var row in rows)
        {
            context.AuditLogs.Add(Audit(
                action,
                row.Id,
                row.WarehouseId,
                $"Allocation: {row.AllocationCode}; ReservationId: {row.ReservationId}; LocationId: {row.LocationId}; Quantity: {row.Quantity}; Strategy: {row.Strategy}; {extra}"));
        }
    }

    private AuditLog Audit(string action, int entityId, int warehouseId, string values) => new()
    {
        UserId = currentUser.UserId,
        Action = action,
        EntityName = "StockAllocation",
        EntityId = entityId,
        WarehouseId = warehouseId,
        Timestamp = DateTime.UtcNow,
        NewValues = values,
        Result = "Success",
        Severity = "Information"
    };

    private static void UpdateReservationStatus(StockReservation reservation)
    {
        var remaining = reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity;
        if (reservation.AllocatedQuantity <= 0)
            reservation.Status = reservation.ConsumedQuantity > 0 ? StockReservationStatus.PartiallyConsumed : StockReservationStatus.Active;
        else if (reservation.AllocatedQuantity < remaining)
            reservation.Status = StockReservationStatus.PartiallyAllocated;
        else
            reservation.Status = StockReservationStatus.Allocated;
    }

    private async Task<T> ExecuteWithSerializableRetryAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var ownTransaction = context.Database.CurrentTransaction is null;
        var attempts = ownTransaction ? 2 : 1;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            await using var transaction = ownTransaction
                ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;
            try
            {
                var result = await action(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (DbUpdateConcurrencyException exception) when (attempt < attempts)
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                context.ChangeTracker.Clear();
                _ = exception;
            }
            catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 } && attempt < attempts)
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                context.ChangeTracker.Clear();
            }
            catch (SqlException exception) when (exception.Number == 1205 && attempt < attempts)
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                context.ChangeTracker.Clear();
            }
            catch (DbUpdateConcurrencyException exception)
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                throw new ConcurrencyException("Dữ liệu Allocation đã thay đổi. Vui lòng tải lại và thử lại.", exception);
            }
            catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 })
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                throw new DeadlockException("Giao dịch Allocation bị deadlock.", exception);
            }
            catch (SqlException exception) when (exception.Number == 1205)
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                throw new DeadlockException("Giao dịch Allocation bị deadlock.", exception);
            }
            catch
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        throw new ConcurrencyException("Không thể hoàn tất Allocation do dữ liệu thay đổi đồng thời.");
    }

    private IQueryable<StockAllocation> AllocationQuery() =>
        context.StockAllocations
            .Include(x => x.Reservation)
            .Include(x => x.Warehouse)
            .Include(x => x.Product)
            .Include(x => x.Location);

    private static StockAllocationDto Map(StockAllocation x) => new()
    {
        Id = x.Id,
        AllocationCode = x.AllocationCode,
        ReservationId = x.ReservationId,
        ReservationCode = x.Reservation.ReservationCode,
        SourceType = x.Reservation.SourceType,
        SourceId = x.Reservation.SourceId,
        SourceCode = x.Reservation.SourceCode,
        WarehouseId = x.WarehouseId,
        WarehouseName = x.Warehouse.Name,
        ProductId = x.ProductId,
        ProductCode = x.Product.Code,
        ProductName = x.Product.Name,
        LocationId = x.LocationId,
        LocationCode = x.Location.Code,
        LocationName = x.Location.Name,
        InventoryStatus = x.InventoryStatus.ToString(),
        Quantity = x.Quantity,
        Status = x.Status.ToString(),
        Strategy = x.Strategy.ToString(),
        SelectionReason = x.SelectionReason,
        AllocatedAt = x.AllocatedAt,
        ReleasedAt = x.ReleasedAt,
        ReleaseReason = x.ReleaseReason
    };

    private async Task<IReadOnlyList<StockAllocationDto>> LoadDtosAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return [];
        var rows = await AllocationQuery().AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }
}
