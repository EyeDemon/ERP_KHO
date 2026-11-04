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

public sealed partial class PickingService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser,
    IPackingSessionIntegration? packingSessionIntegration = null,
    IInventoryLockEvaluator? inventoryLocks = null) : IPickingService, IPickingDispatchReadiness
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
                if (inventoryLocks is not null)
                    await inventoryLocks.EnsureBucketUnlockedAsync(
                        task.WarehouseId,
                        line.SourceLocationId,
                        line.ProductId,
                        line.Allocation.InventoryStatus,
                        line.Allocation.LotId,
                        line.Allocation.SerialId,
                        token);
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

            if (inventoryLocks is not null)
                await inventoryLocks.EnsureBucketUnlockedAsync(
                    task.WarehouseId,
                    line.SourceLocationId,
                    line.ProductId,
                    line.Allocation.InventoryStatus,
                    line.Allocation.LotId,
                    line.Allocation.SerialId,
                    token);

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
            var totalPicked = task.Lines.Sum(x => x.PickedQuantity);
            AddAudit("PickingTask.Completed", task, $"PickedQuantity: {totalPicked}");
            if (packingSessionIntegration is not null && totalPicked > 0)
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
                x.Status == ShortPickExceptionStatus.Open ||
                (x.Status == ShortPickExceptionStatus.Resolved &&
                 x.ResolutionType == ShortPickResolutionType.Backorder)))
            throw new ConcurrencyException("Chứng từ còn thiếu hàng sau Picking và chưa đủ điều kiện dispatch.");

        if (task.Lines.Sum(x => x.PickedQuantity) <= 0)
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
            if (remaining < 0 ||
                allocations.Sum(x => x.Quantity) != remaining ||
                allocations.Any(x => x.Status != StockAllocationStatus.Picked))
                throw new ConcurrencyException("Allocation chưa được Picking đầy đủ để dispatch.");
        }
    }
}
