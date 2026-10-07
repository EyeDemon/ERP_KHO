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

public sealed class PickingService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser,
    IPackingSessionIntegration? packingSessionIntegration = null) : IPickingService, IPickingDispatchReadiness
{
    private static readonly StockAllocationStatus[] CapacityStatuses =
    [
        StockAllocationStatus.Active,
        StockAllocationStatus.Picking,
        StockAllocationStatus.Picked
    ];

    public async Task<IReadOnlyList<PickingTaskListDto>> ListAsync(
        int? warehouseId = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var accessible = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (warehouseId.HasValue)
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            accessible = [warehouseId.Value];
        }

        var query = Query().AsNoTracking().Where(x => accessible.Contains(x.WarehouseId));
        if (Enum.TryParse<PickingTaskStatus>(status, true, out var parsed))
            query = query.Where(x => x.Status == parsed);

        var tasks = await query
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Take(250)
            .ToListAsync(cancellationToken);
        return tasks.Select(MapList).ToList();
    }

    public async Task<PickingTaskDto> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var task = await LoadAsync(id, false, cancellationToken);
        return Map(task);
    }

    public Task<PickingTaskDto> AssignAsync(
        int id,
        PickingStateCommandDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request.RowVersion, async (task, token) =>
        {
            if (task.Status != PickingTaskStatus.Open)
                throw Conflict("PICK_INVALID_STATE", "Chỉ nhiệm vụ Picking đang mở mới có thể phân công.");
            var assignee = request.AssignedUserId ?? currentUser.UserId;
            var valid = await context.Users.AsNoTracking().AnyAsync(
                x => x.Id == assignee &&
                     x.IsActive &&
                     (x.LockoutEnd == null || x.LockoutEnd <= DateTime.UtcNow) &&
                     (x.Role.RoleName == "Admin" || x.WarehouseAccesses.Any(w => w.WarehouseId == task.WarehouseId)),
                token);
            if (!valid)
                throw new NotFoundException("Không tìm thấy người dùng phù hợp trong kho.");
            task.AssignedUserId = assignee;
            task.Status = PickingTaskStatus.Assigned;
            AddAudit("PickingTask.Assigned", task, $"AssignedUserId: {assignee}");
        }, cancellationToken);

    public Task<PickingTaskDto> StartAsync(
        int id,
        PickingStateCommandDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request.RowVersion, async (task, token) =>
        {
            if (task.Status != PickingTaskStatus.Assigned)
                throw Conflict("PICK_INVALID_STATE", "Nhiệm vụ Picking không ở trạng thái có thể bắt đầu.");
            EnsureActor(task);
            await EnsureSourceStillFullyAllocatedAsync(task, token);
            task.Status = PickingTaskStatus.InProgress;
            task.StartedAt ??= DateTime.UtcNow;
            foreach (var line in task.Lines)
            {
                if (line.Status == PickingTaskLineStatus.Open)
                    line.Status = PickingTaskLineStatus.InProgress;
                if (line.Allocation.Status != StockAllocationStatus.Active)
                    throw Conflict("PICK_ALLOCATION_CHANGED", "Allocation đã thay đổi trước khi bắt đầu Picking.");
                line.Allocation.Status = StockAllocationStatus.Picking;
                line.Allocation.Version++;
            }
            AddAudit("PickingTask.Started", task, $"Lines: {task.Lines.Count}");
        }, cancellationToken);

    public Task<PickingTaskDto> PickAsync(
        int id,
        PickScanDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request.RowVersion, async (task, token) =>
        {
            if (task.Status != PickingTaskStatus.InProgress)
                throw Conflict("PICK_INVALID_STATE", "Nhiệm vụ Picking không ở trạng thái có thể xác nhận lấy hàng.");
            EnsureActor(task);
            if (request.Quantity <= 0)
                throw new BusinessRuleException("Số lượng Picking phải lớn hơn 0.");

            var line = task.Lines.SingleOrDefault(x => x.Id == request.TaskLineId)
                ?? throw new NotFoundException("Không tìm thấy dòng Picking.");
            if (line.Status is not PickingTaskLineStatus.InProgress and not PickingTaskLineStatus.Open)
                throw Conflict("PICK_LINE_INVALID_STATE", "Dòng Picking không còn ở trạng thái có thể xác nhận.");

            ValidateLocationScan(line, request.LocationBarcode);
            await ValidateProductScanAsync(line, request.ProductBarcode, token);
            ValidateTrackingScan(line, request);
            ValidateQuantityPrecision(line, request.Quantity);

            if (request.Quantity > line.RemainingQuantity)
                throw Conflict("PICK_QUANTITY_EXCEEDS_REMAINING", "Số lượng Picking vượt quá số lượng còn lại.");

            var today = DateTime.UtcNow.Date;
            var stockOk = await context.InventoryStocks.AsNoTracking().AnyAsync(
                x => x.ProductId == line.ProductId &&
                     x.WarehouseId == task.WarehouseId &&
                     x.LocationId == line.SourceLocationId &&
                     x.Status == line.Allocation.InventoryStatus &&
                     x.LotId == line.Allocation.LotId &&
                     x.SerialId == line.Allocation.SerialId &&
                     x.StatusDefinition.IsPickable &&
                     x.Location != null &&
                     x.Location.IsActive &&
                     !x.Location.IsBlocked &&
                     x.Location.IsPickable &&
                     (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today) &&
                     x.Quantity >= line.Allocation.Quantity &&
                     x.ReservedQuantity >= line.Allocation.Quantity,
                token);
            if (!stockOk)
                throw Conflict("INV_STATUS_NOT_PICKABLE", "Tồn tại vị trí nguồn đã thay đổi hoặc không còn pickable.");

            if (line.Allocation.Status != StockAllocationStatus.Picking)
                throw Conflict("PICK_ALLOCATION_CHANGED", "Allocation không còn ở trạng thái Picking.");

            line.PickedQuantity += request.Quantity;
            line.Status = line.RemainingQuantity == 0 ? PickingTaskLineStatus.Picked : PickingTaskLineStatus.InProgress;
            line.Allocation.Status = line.Status == PickingTaskLineStatus.Picked
                ? StockAllocationStatus.Picked
                : StockAllocationStatus.Picking;
            line.Allocation.Version++;
            AddAudit(
                "PickingTask.Picked",
                task,
                $"LineId: {line.Id}; AllocationId: {line.AllocationId}; Quantity: {request.Quantity}; Picked: {line.PickedQuantity}/{line.RequestedQuantity}");
        }, cancellationToken);

    public Task<PickingTaskDto> ReportShortPickAsync(
        int id,
        ReportShortPickDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request.RowVersion, async (task, token) =>
        {
            if (task.Status != PickingTaskStatus.InProgress)
                throw Conflict("PICK_INVALID_STATE", "Nhiệm vụ Picking không ở trạng thái có thể báo thiếu.");
            EnsureActor(task);
            var line = task.Lines.SingleOrDefault(x => x.Id == request.TaskLineId)
                ?? throw new NotFoundException("Không tìm thấy dòng Picking.");
            if (line.Status is not PickingTaskLineStatus.InProgress and not PickingTaskLineStatus.Open)
                throw Conflict("PICK_LINE_INVALID_STATE", "Dòng Picking không còn ở trạng thái có thể báo thiếu.");
            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new BusinessRuleException("Lý do Short Pick là bắt buộc.");
            if (request.ActualPickedQuantity != line.PickedQuantity)
                throw Conflict("PICK_SHORT", "Actual quantity phải khớp số lượng đã xác nhận bằng scan trước khi báo Short Pick.");
            if (line.PickedQuantity >= line.RequestedQuantity)
                throw Conflict("PICK_SHORT", "Dòng đã Picking đủ, không thể báo Short Pick.");
            if (line.ShortPicks.Any(x => x.Status == ShortPickExceptionStatus.Open))
                throw Conflict("PICK_SHORT", "Dòng Picking đã có Short Pick đang chờ xử lý.");

            var shortage = line.RequestedQuantity - line.PickedQuantity;
            line.ShortPicks.Add(new ShortPickException
            {
                ExpectedQuantity = line.RequestedQuantity,
                PickedQuantity = line.PickedQuantity,
                ShortageQuantity = shortage,
                Reason = request.Reason.Trim(),
                Status = ShortPickExceptionStatus.Open,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = currentUser.UserId
            });
            line.Status = PickingTaskLineStatus.ShortPick;
            task.Status = PickingTaskStatus.ShortPick;
            AddAudit("PickingTask.ShortPickReported", task, $"LineId: {line.Id}; Shortage: {shortage}; Reason: {request.Reason.Trim()}");
            await Task.CompletedTask;
        }, cancellationToken);

    public Task<PickingTaskDto> ResolveShortPickAsync(
        int taskId,
        int exceptionId,
        ResolveShortPickDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(taskId, request.RowVersion, async (task, token) =>
        {
            if (task.Status != PickingTaskStatus.ShortPick)
                throw Conflict("PICK_INVALID_STATE", "Nhiệm vụ không có Short Pick đang chờ xử lý.");
            EnsureActor(task);
            var exception = task.Lines.SelectMany(x => x.ShortPicks).SingleOrDefault(x => x.Id == exceptionId)
                ?? throw new NotFoundException("Không tìm thấy Short Pick.");
            if (exception.Status != ShortPickExceptionStatus.Open)
                throw Conflict("PICK_SHORT", "Short Pick đã được xử lý.");

            if (!Enum.TryParse<ShortPickResolutionType>(request.Resolution, true, out var resolution) ||
                resolution == ShortPickResolutionType.SupervisorOverride)
                throw new BusinessRuleException("Phương án xử lý Short Pick không hợp lệ.");

            switch (resolution)
            {
                case ShortPickResolutionType.AlternativeLocation:
                    await ResolveAlternativeLocationAsync(task, exception, request.Note, token);
                    break;
                case ShortPickResolutionType.Backorder:
                case ShortPickResolutionType.CancelRemainder:
                    await ResolveReleasedRemainderAsync(task, exception, resolution, request.Note, token);
                    break;
                case ShortPickResolutionType.AlternativeLot:
                    throw Conflict("PICK_RESOLUTION_NOT_AVAILABLE", "Alternative Lot chưa khả dụng vì Lot/Expiry chưa có canonical model.");
                case ShortPickResolutionType.Replenish:
                    throw Conflict("PICK_RESOLUTION_NOT_AVAILABLE", "Replenishment chưa được triển khai trong foundation hiện tại.");
                default:
                    throw new BusinessRuleException("Phương án xử lý Short Pick không hợp lệ.");
            }
        }, cancellationToken);

    public Task<PickingTaskDto> OverrideShortPickAsync(
        int taskId,
        int exceptionId,
        OverrideShortPickDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(taskId, request.RowVersion, async (task, token) =>
        {
            if (task.Status != PickingTaskStatus.ShortPick)
                throw Conflict("PICK_INVALID_STATE", "Nhiệm vụ không có Short Pick đang chờ xử lý.");
            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new BusinessRuleException("Lý do supervisor override là bắt buộc.");
            var exception = task.Lines.SelectMany(x => x.ShortPicks).SingleOrDefault(x => x.Id == exceptionId)
                ?? throw new NotFoundException("Không tìm thấy Short Pick.");
            if (exception.Status != ShortPickExceptionStatus.Open)
                throw Conflict("PICK_SHORT", "Short Pick đã được xử lý.");
            await ResolveReleasedRemainderAsync(
                task,
                exception,
                ShortPickResolutionType.SupervisorOverride,
                request.Reason.Trim(),
                token);
        }, cancellationToken);

    public Task<PickingTaskDto> CompleteAsync(
        int id,
        PickingStateCommandDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request.RowVersion, async (task, token) =>
        {
            if (task.Status is not PickingTaskStatus.InProgress and not PickingTaskStatus.Resolved)
                throw Conflict("PICK_INVALID_STATE", "Nhiệm vụ Picking không ở trạng thái có thể hoàn tất.");
            EnsureActor(task);
            if (task.Lines.Any(x => x.Status is not PickingTaskLineStatus.Picked and not PickingTaskLineStatus.Resolved))
                throw Conflict("PICK_INCOMPLETE", "Vẫn còn dòng Picking chưa hoàn tất hoặc chưa xử lý Short Pick.");
            if (task.Lines.SelectMany(x => x.ShortPicks).Any(x => x.Status == ShortPickExceptionStatus.Open))
                throw Conflict("PICK_SHORT", "Vẫn còn Short Pick chưa được xử lý.");

            task.Status = PickingTaskStatus.Completed;
            task.CompletedAt = DateTime.UtcNow;
            AddAudit("PickingTask.Completed", task, $"PickedQuantity: {task.Lines.Sum(x => x.PickedQuantity)}");
            if (packingSessionIntegration is not null)
                await packingSessionIntegration.EnsureForPickingTaskAsync(task.Id, currentUser.UserId, token);
        }, cancellationToken);

    public async Task EnsureSourceReadyAsync(
        string sourceType,
        int sourceId,
        CancellationToken cancellationToken = default)
    {
        var task = await Query().AsNoTracking()
            .SingleOrDefaultAsync(x => x.SourceType == sourceType && x.SourceId == sourceId, cancellationToken);
        if (task is null) return;

        if (task.Status != PickingTaskStatus.Completed)
            throw new ConcurrencyException("Nhiệm vụ Picking của chứng từ chưa hoàn tất.");

        if (task.Lines.SelectMany(x => x.ShortPicks).Any(x =>
                x.Status == ShortPickExceptionStatus.Resolved &&
                x.ResolutionType is ShortPickResolutionType.Backorder
                    or ShortPickResolutionType.CancelRemainder
                    or ShortPickResolutionType.SupervisorOverride))
            throw new ConcurrencyException("Chứng từ còn thiếu hàng sau Picking và chưa đủ điều kiện dispatch.");

        var reservations = await context.StockReservations.AsNoTracking()
            .Where(x => x.SourceType == sourceType && x.SourceId == sourceId)
            .ToListAsync(cancellationToken);
        foreach (var reservation in reservations)
        {
            var remaining = reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity;
            var allocations = await context.StockAllocations.AsNoTracking()
                .Where(x => x.ReservationId == reservation.Id && CapacityStatuses.Contains(x.Status))
                .ToListAsync(cancellationToken);
            if (remaining <= 0 ||
                allocations.Sum(x => x.Quantity) != remaining ||
                allocations.Any(x => x.Status != StockAllocationStatus.Picked))
                throw new ConcurrencyException("Allocation chưa được Picking đầy đủ để dispatch.");
        }
    }

    private async Task ResolveAlternativeLocationAsync(
        PickingTask task,
        ShortPickException exception,
        string? note,
        CancellationToken token)
    {
        var line = exception.PickingTaskLine;
        var allocation = line.Allocation;
        if (allocation.Status != StockAllocationStatus.Picking)
            throw Conflict("PICK_ALLOCATION_CHANGED", "Allocation đã thay đổi trước khi xử lý Short Pick.");

        var reservation = await context.StockReservations.SingleAsync(x => x.Id == allocation.ReservationId, token);
        var candidates = await context.InventoryStocks.AsNoTracking()
            .Where(x => x.ProductId == allocation.ProductId &&
                        x.WarehouseId == allocation.WarehouseId &&
                        x.Status == InventoryStatus.Available &&
                        x.LocationId.HasValue &&
                        x.LocationId != allocation.LocationId &&
                        x.Location != null &&
                        x.Location.IsActive &&
                        !x.Location.IsBlocked &&
                        x.Location.IsPickable &&
                        x.ReservedQuantity > 0)
            .OrderBy(x => x.LocationId)
            .Select(x => new
            {
                LocationId = x.LocationId!.Value,
                Available = x.ReservedQuantity -
                    (context.StockAllocations
                        .Where(a => a.WarehouseId == x.WarehouseId &&
                                    a.ProductId == x.ProductId &&
                                    a.LocationId == x.LocationId &&
                                    CapacityStatuses.Contains(a.Status))
                        .Sum(a => (decimal?)a.Quantity) ?? 0m)
            })
            .ToListAsync(token);

        if (candidates.Sum(x => Math.Max(0m, x.Available)) < exception.ShortageQuantity)
            throw Conflict("PICK_SHORT", "Không có đủ reserved capacity ở vị trí khác để reallocate phần Short Pick.");

        var originalQuantity = allocation.Quantity;
        if (line.PickedQuantity > 0)
        {
            allocation.Quantity = line.PickedQuantity;
            allocation.Status = StockAllocationStatus.Picked;
        }
        else
        {
            allocation.Status = StockAllocationStatus.Reallocated;
            allocation.ReleasedAt = DateTime.UtcNow;
            allocation.ReleasedBy = currentUser.UserId;
            allocation.ReleaseReason = "Short Pick alternative location";
        }
        allocation.Version++;

        var remaining = exception.ShortageQuantity;
        var sequence = task.Lines.Max(x => x.Sequence);
        foreach (var candidate in candidates)
        {
            var take = Math.Min(remaining, Math.Max(0m, candidate.Available));
            if (take <= 0) continue;
            var created = new StockAllocation
            {
                AllocationCode = $"ALC-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..34].ToUpperInvariant(),
                ReservationId = allocation.ReservationId,
                WarehouseId = allocation.WarehouseId,
                ProductId = allocation.ProductId,
                LocationId = candidate.LocationId,
                InventoryStatus = allocation.InventoryStatus,
                Quantity = take,
                Status = StockAllocationStatus.Picking,
                Strategy = AllocationStrategy.LocationOrder,
                SelectionReason = $"Short Pick reallocation từ Allocation {allocation.AllocationCode}.",
                AllocatedAt = DateTime.UtcNow,
                AllocatedBy = currentUser.UserId
            };
            context.StockAllocations.Add(created);
            task.Lines.Add(new PickingTaskLine
            {
                Allocation = created,
                ProductId = created.ProductId,
                SourceLocationId = created.LocationId,
                RequestedQuantity = take,
                PickedQuantity = 0,
                Sequence = ++sequence,
                Status = PickingTaskLineStatus.InProgress
            });
            remaining -= take;
            if (remaining == 0) break;
        }
        if (remaining != 0)
            throw new ConcurrencyException("Không thể reallocate đầy đủ phần Short Pick.");

        reservation.AllocationVersion++;
        line.Status = PickingTaskLineStatus.Resolved;
        ResolveException(exception, ShortPickResolutionType.AlternativeLocation, note);
        task.Status = PickingTaskStatus.InProgress;
        AddAudit(
            "PickingTask.ShortPickReallocated",
            task,
            $"LineId: {line.Id}; OriginalAllocationId: {allocation.Id}; OriginalQuantity: {originalQuantity}; Shortage: {exception.ShortageQuantity}");
    }

    private async Task ResolveReleasedRemainderAsync(
        PickingTask task,
        ShortPickException exception,
        ShortPickResolutionType resolution,
        string? note,
        CancellationToken token)
    {
        var line = exception.PickingTaskLine;
        var allocation = line.Allocation;
        if (allocation.Status != StockAllocationStatus.Picking)
            throw Conflict("PICK_ALLOCATION_CHANGED", "Allocation đã thay đổi trước khi xử lý Short Pick.");

        var originalQuantity = allocation.Quantity;
        var shortage = exception.ShortageQuantity;
        var otherCommitted = await context.StockAllocations
            .Where(a => a.Id != allocation.Id &&
                        a.WarehouseId == allocation.WarehouseId &&
                        a.ProductId == allocation.ProductId &&
                        a.LocationId == allocation.LocationId &&
                        CapacityStatuses.Contains(a.Status))
            .SumAsync(a => (decimal?)a.Quantity, token) ?? 0m;

        var affected = await context.InventoryStocks
            .Where(x => x.ProductId == allocation.ProductId &&
                        x.WarehouseId == allocation.WarehouseId &&
                        x.LocationId == allocation.LocationId &&
                        x.Status == InventoryStatus.Available &&
                        x.ReservedQuantity >= otherCommitted + originalQuantity)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.ReservedQuantity, x => x.ReservedQuantity - shortage)
                .SetProperty(x => x.LastUpdated, DateTime.UtcNow), token);
        if (affected != 1)
            throw new ConcurrencyException("Reserved bucket đã thay đổi trước khi xử lý Short Pick.");

        var reservation = await context.StockReservations.SingleAsync(x => x.Id == allocation.ReservationId, token);
        if (line.PickedQuantity > 0)
        {
            allocation.Quantity = line.PickedQuantity;
            allocation.Status = StockAllocationStatus.Picked;
        }
        else
        {
            allocation.Status = StockAllocationStatus.Released;
            allocation.ReleasedAt = DateTime.UtcNow;
            allocation.ReleasedBy = currentUser.UserId;
            allocation.ReleaseReason = $"Short Pick {resolution}";
        }
        allocation.Version++;

        reservation.AllocatedQuantity -= shortage;
        reservation.ReleasedQuantity += shortage;
        reservation.AllocationVersion++;
        reservation.ReleasedAt = DateTime.UtcNow;
        reservation.ReleasedBy = currentUser.UserId;
        reservation.ReleaseReason = $"Short Pick {resolution}";
        UpdateReservationStatus(reservation);

        line.Status = PickingTaskLineStatus.Resolved;
        ResolveException(exception, resolution, note);
        task.Status = task.Lines.All(x => x.Status is PickingTaskLineStatus.Picked or PickingTaskLineStatus.Resolved)
            ? PickingTaskStatus.Resolved
            : PickingTaskStatus.InProgress;
        AddAudit(
            "PickingTask.ShortPickResolved",
            task,
            $"LineId: {line.Id}; Resolution: {resolution}; Shortage: {shortage}");
    }

    private void ResolveException(
        ShortPickException exception,
        ShortPickResolutionType resolution,
        string? note)
    {
        exception.ResolutionType = resolution;
        exception.Status = ShortPickExceptionStatus.Resolved;
        exception.ResolvedAt = DateTime.UtcNow;
        exception.ResolvedBy = currentUser.UserId;
        exception.ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    private static void UpdateReservationStatus(StockReservation reservation)
    {
        var remaining = reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity;
        if (remaining <= 0)
            reservation.Status = StockReservationStatus.Released;
        else if (reservation.AllocatedQuantity <= 0)
            reservation.Status = reservation.ConsumedQuantity > 0 ? StockReservationStatus.PartiallyConsumed : StockReservationStatus.Active;
        else if (reservation.AllocatedQuantity < remaining)
            reservation.Status = StockReservationStatus.PartiallyAllocated;
        else
            reservation.Status = StockReservationStatus.Allocated;
    }

    private async Task EnsureSourceStillFullyAllocatedAsync(PickingTask task, CancellationToken token)
    {
        var allocationIds = task.Lines.Select(x => x.AllocationId).ToArray();
        var count = await context.StockAllocations.CountAsync(
            x => allocationIds.Contains(x.Id) && x.Status == StockAllocationStatus.Active,
            token);
        if (count != allocationIds.Length)
            throw Conflict("PICK_ALLOCATION_CHANGED", "Allocation đã thay đổi sau khi nhiệm vụ Picking được tạo.");
    }

    private static void ValidateLocationScan(PickingTaskLine line, string raw)
    {
        var scanned = raw?.Trim();
        if (string.IsNullOrWhiteSpace(scanned) ||
            !string.Equals(scanned, line.SourceLocation.Code, StringComparison.OrdinalIgnoreCase))
            throw Conflict("PICK_WRONG_LOCATION", $"Sai vị trí lấy hàng. Yêu cầu {line.SourceLocation.Code}.");
    }

    private async Task ValidateProductScanAsync(PickingTaskLine line, string raw, CancellationToken token)
    {
        var scanned = raw?.Trim();
        if (string.IsNullOrWhiteSpace(scanned))
            throw Conflict("PICK_WRONG_PRODUCT", "Barcode sản phẩm là bắt buộc.");
        if (string.Equals(scanned, line.Product.Code, StringComparison.OrdinalIgnoreCase)) return;
        if (!await context.ProductBarcodes.AsNoTracking().AnyAsync(
                x => x.ProductId == line.ProductId && x.Value == scanned,
                token))
            throw Conflict("PICK_WRONG_PRODUCT", "Barcode không khớp sản phẩm được phân công.");
    }

    private static void ValidateTrackingScan(PickingTaskLine line, PickScanDto request)
    {
        var allocation = line.Allocation;
        var scannedLot = string.IsNullOrWhiteSpace(request.LotNumber) ? null : request.LotNumber.Trim();
        var scannedSerial = string.IsNullOrWhiteSpace(request.SerialNumber) ? null : request.SerialNumber.Trim();

        if (allocation.LotId.HasValue)
        {
            if (scannedLot is null)
                throw Conflict("PICK_WRONG_LOT", $"Lot là bắt buộc. Yêu cầu {allocation.Lot?.LotNumber}.");
            if (!string.Equals(scannedLot, allocation.Lot?.LotNumber, StringComparison.OrdinalIgnoreCase))
                throw Conflict("PICK_WRONG_LOT", $"Sai lot. Yêu cầu {allocation.Lot?.LotNumber}.");
        }
        else if (scannedLot is not null)
        {
            throw Conflict("PICK_WRONG_LOT", "Dòng Picking này không được phân bổ theo lot.");
        }

        if (allocation.SerialId.HasValue)
        {
            if (scannedSerial is null)
                throw Conflict("PICK_WRONG_SERIAL", $"Serial là bắt buộc. Yêu cầu {allocation.Serial?.SerialNumber}.");
            if (!string.Equals(scannedSerial, allocation.Serial?.SerialNumber, StringComparison.OrdinalIgnoreCase))
                throw Conflict("PICK_WRONG_SERIAL", $"Sai serial. Yêu cầu {allocation.Serial?.SerialNumber}.");
            if (request.Quantity != 1)
                throw Conflict("PICK_WRONG_SERIAL", "Mỗi serial chỉ được xác nhận 1 Base UOM.");
        }
        else if (scannedSerial is not null)
        {
            throw Conflict("PICK_WRONG_SERIAL", "Dòng Picking này không được phân bổ theo serial.");
        }

        if (!string.IsNullOrWhiteSpace(request.DestinationToteBarcode))
            throw Conflict("PICK_DESTINATION_HU_NOT_AVAILABLE", "Destination Tote/HU chưa được gắn trực tiếp từ Picking task.");
    }

    private static void ValidateQuantityPrecision(PickingTaskLine line, decimal quantity)
    {
        var places = line.Product.Unit.DecimalPlaces;
        if (decimal.Round(quantity, places) != quantity)
            throw new BusinessRuleException($"Số lượng vượt quá độ chính xác Base UOM cho phép ({places} chữ số thập phân).");
    }

    private void EnsureActor(PickingTask task)
    {
        if (!task.AssignedUserId.HasValue || task.AssignedUserId.Value != currentUser.UserId)
            throw new ForbiddenException("Nhiệm vụ Picking đã được phân công cho người dùng khác.");
    }

    private async Task<PickingTaskDto> MutateAsync(
        int id,
        string rowVersion,
        Func<PickingTask, CancellationToken, Task> mutation,
        CancellationToken cancellationToken)
    {
        var ownTransaction = context.Database.CurrentTransaction is null;
        await using var transaction = ownTransaction
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var task = await LoadAsync(id, true, cancellationToken);
            ApplyVersion(task, rowVersion);
            await mutation(task, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return Map(task);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new ConcurrencyException("Dữ liệu Picking đã thay đổi. Vui lòng tải lại và thử lại.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 })
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new DeadlockException("Giao dịch Picking bị deadlock.", ex);
        }
        catch (SqlException ex) when (ex.Number == 1205)
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new DeadlockException("Giao dịch Picking bị deadlock.", ex);
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<PickingTask> LoadAsync(int id, bool tracking, CancellationToken token)
    {
        IQueryable<PickingTask> query = Query();
        if (!tracking) query = query.AsNoTracking();
        var task = await query.SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("Không tìm thấy nhiệm vụ Picking hoặc bạn không có quyền truy cập.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(task.WarehouseId, token);
        return task;
    }

    private IQueryable<PickingTask> Query() =>
        context.PickingTasks
            .Include(x => x.Warehouse)
            .Include(x => x.AssignedUser)
            .Include(x => x.Lines).ThenInclude(x => x.Product).ThenInclude(x => x.Unit)
            .Include(x => x.Lines).ThenInclude(x => x.SourceLocation)
            .Include(x => x.Lines).ThenInclude(x => x.Allocation).ThenInclude(x => x.Reservation)
            .Include(x => x.Lines).ThenInclude(x => x.Allocation).ThenInclude(x => x.Lot)
            .Include(x => x.Lines).ThenInclude(x => x.Allocation).ThenInclude(x => x.Serial)
            .Include(x => x.Lines).ThenInclude(x => x.ShortPicks);

    private void ApplyVersion(PickingTask task, string encoded)
    {
        byte[] expected;
        try { expected = Convert.FromBase64String(encoded); }
        catch { throw new BusinessRuleException("Phiên bản dữ liệu Picking không hợp lệ."); }
        if (!task.RowVersion.SequenceEqual(expected))
            throw Conflict("PICK_CONCURRENCY", "Dữ liệu Picking đã thay đổi. Vui lòng tải lại và thử lại.");
        context.Entry(task).Property(x => x.RowVersion).OriginalValue = expected;
    }

    private void AddAudit(string action, PickingTask task, string values) =>
        context.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = action,
            EntityName = "PickingTask",
            EntityId = task.Id,
            WarehouseId = task.WarehouseId,
            Timestamp = DateTime.UtcNow,
            NewValues = values,
            Result = "Success",
            Severity = "Information"
        });

    private static PickingTaskListDto MapList(PickingTask task)
    {
        var requested = task.Lines.Sum(x => x.Status == PickingTaskLineStatus.Resolved ? x.PickedQuantity : x.RequestedQuantity);
        var picked = task.Lines.Sum(x => x.PickedQuantity);
        return new PickingTaskListDto
        {
            Id = task.Id,
            TaskCode = task.TaskCode,
            SourceType = task.SourceType,
            SourceId = task.SourceId,
            SourceCode = task.SourceCode,
            WarehouseId = task.WarehouseId,
            WarehouseName = task.Warehouse.Name,
            PickingType = task.PickingType,
            Status = task.Status.ToString(),
            Priority = task.Priority,
            AssignedUserId = task.AssignedUserId,
            AssignedUserName = task.AssignedUser?.FullName ?? task.AssignedUser?.Username,
            RequestedQuantity = requested,
            PickedQuantity = picked,
            RemainingQuantity = Math.Max(0, requested - picked),
            CreatedAt = task.CreatedAt,
            StartedAt = task.StartedAt,
            CompletedAt = task.CompletedAt
        };
    }

    private static PickingTaskDto Map(PickingTask task)
    {
        var summary = MapList(task);
        return new PickingTaskDto
        {
            Id = summary.Id,
            TaskCode = summary.TaskCode,
            SourceType = summary.SourceType,
            SourceId = summary.SourceId,
            SourceCode = summary.SourceCode,
            WarehouseId = summary.WarehouseId,
            WarehouseName = summary.WarehouseName,
            PickingType = summary.PickingType,
            Status = summary.Status,
            Priority = summary.Priority,
            AssignedUserId = summary.AssignedUserId,
            AssignedUserName = summary.AssignedUserName,
            RequestedQuantity = summary.RequestedQuantity,
            PickedQuantity = summary.PickedQuantity,
            RemainingQuantity = summary.RemainingQuantity,
            CreatedAt = summary.CreatedAt,
            StartedAt = summary.StartedAt,
            CompletedAt = summary.CompletedAt,
            RowVersion = Convert.ToBase64String(task.RowVersion),
            Lines = task.Lines.OrderBy(x => x.Sequence).Select(x => new PickingTaskLineDto
            {
                Id = x.Id,
                AllocationId = x.AllocationId,
                AllocationCode = x.Allocation.AllocationCode,
                ProductId = x.ProductId,
                ProductCode = x.Product.Code,
                ProductName = x.Product.Name,
                SourceLocationId = x.SourceLocationId,
                SourceLocationCode = x.SourceLocation.Code,
                SourceLocationName = x.SourceLocation.Name,
                InventoryStatus = x.Allocation.InventoryStatus.ToString(),
                LotId = x.Allocation.LotId,
                LotNumber = x.Allocation.Lot?.LotNumber,
                ExpiryDate = x.Allocation.Lot?.ExpiryDate,
                SerialId = x.Allocation.SerialId,
                SerialNumber = x.Allocation.Serial?.SerialNumber,
                RequestedQuantity = x.RequestedQuantity,
                PickedQuantity = x.PickedQuantity,
                RemainingQuantity = x.RemainingQuantity,
                Sequence = x.Sequence,
                Status = x.Status.ToString()
            }).ToList(),
            ShortPicks = task.Lines.SelectMany(x => x.ShortPicks).OrderBy(x => x.Id).Select(x => new ShortPickExceptionDto
            {
                Id = x.Id,
                PickingTaskLineId = x.PickingTaskLineId,
                ExpectedQuantity = x.ExpectedQuantity,
                PickedQuantity = x.PickedQuantity,
                ShortageQuantity = x.ShortageQuantity,
                Reason = x.Reason,
                ResolutionType = x.ResolutionType?.ToString(),
                Status = x.Status.ToString(),
                CreatedAt = x.CreatedAt,
                ResolvedAt = x.ResolvedAt,
                ResolutionNote = x.ResolutionNote
            }).ToList()
        };
    }

    private static BusinessRuleException Conflict(string code, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["HttpStatusCode"] = 409;
        ex.Data["ErrorCode"] = code;
        return ex;
    }
}
