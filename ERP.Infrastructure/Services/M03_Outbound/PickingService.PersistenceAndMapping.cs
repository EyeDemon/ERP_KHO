using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class PickingService
{
    private static void ValidateLocationScan(PickingTaskLine line, string raw)
    {
        var scanned = CleanScan(raw);
        if (string.IsNullOrWhiteSpace(scanned) ||
            !string.Equals(scanned, line.SourceLocation.Code, StringComparison.OrdinalIgnoreCase))
            throw Conflict("PICK_WRONG_LOCATION", $"Sai vị trí lấy hàng. Yêu cầu {line.SourceLocation.Code}.");
    }

    private async Task ValidateProductScanAsync(PickingTaskLine line, string raw, CancellationToken token)
    {
        var scanned = CleanScan(raw);
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
        var scannedLot = NullIfEmpty(CleanScan(request.LotNumber));
        var scannedSerial = NullIfEmpty(CleanScan(request.SerialNumber));

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

        if (NullIfEmpty(CleanScan(request.DestinationToteBarcode)) is not null)
            throw Conflict("PICK_DESTINATION_HU_NOT_AVAILABLE", "Destination Tote/HU chưa được gắn trực tiếp từ Picking task.");
    }

    private static string CleanScan(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var buffer = new char[value.Length];
        var count = 0;
        foreach (var ch in value)
        {
            if (ch < 32 || ch == 127 || ch == '\u200b' || ch == '\ufeff')
                continue;
            buffer[count++] = ch;
        }
        return new string(buffer, 0, count).Trim();
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

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
        int? lockWarehouseId = null;
        int[] lockProductIds = [];
        if (context.Database.IsSqlServer())
        {
            lockWarehouseId = await context.PickingTasks.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => (int?)x.WarehouseId)
                .FirstOrDefaultAsync(cancellationToken);
            if (lockWarehouseId.HasValue)
            {
                lockProductIds = await context.PickingTaskLines.AsNoTracking()
                    .Where(x => x.PickingTaskId == id)
                    .Select(x => x.ProductId)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToArrayAsync(cancellationToken);
            }
        }

        var isSqlServer = context.Database.IsRelational() && context.Database.IsSqlServer();
        var maxAttempts = ownTransaction && isSqlServer ? 4 : 1;
        for (var attempt = 1; ; attempt++)
        {
            await using var transaction = ownTransaction
                ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                : null;
            try
            {
                if (lockWarehouseId.HasValue && isSqlServer && context.Database.CurrentTransaction is not null)
                {
                    await context.AcquireInventoryAllocationLocksAsync(lockWarehouseId.Value, lockProductIds, cancellationToken);
                }

                var task = await LoadAsync(id, true, cancellationToken);
                ApplyVersion(task, rowVersion);
                await mutation(task, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                var dto = Map(await LoadAsync(id, false, cancellationToken));
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return dto;
            }
            catch (Exception ex) when (ownTransaction && attempt < maxAttempts && IsDeadlock(ex))
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                context.ChangeTracker.Clear();
                await Task.Delay(15 * attempt, cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                throw new ConcurrencyException("Dữ liệu Picking đã thay đổi. Vui lòng tải lại và thử lại.", ex);
            }
            catch (Exception ex) when (IsDeadlock(ex))
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
    }

    private static bool IsDeadlock(Exception? ex)
    {
        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is DeadlockException or SqlException { Number: 1205 })
                return true;
        }
        return false;
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
            TaskCode = (task.TaskCode ?? string.Empty).Trim(),
            SourceType = (task.SourceType ?? string.Empty).Trim(),
            SourceId = task.SourceId,
            SourceCode = (task.SourceCode ?? string.Empty).Trim(),
            WarehouseId = task.WarehouseId,
            WarehouseName = (task.Warehouse.Name ?? string.Empty).Trim(),
            PickingType = (task.PickingType ?? string.Empty).Trim(),
            Status = task.Status.ToString(),
            Priority = task.Priority,
            AssignedUserId = task.AssignedUserId,
            AssignedUserName = (task.AssignedUser?.FullName ?? task.AssignedUser?.Username)?.Trim(),
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
            Lines = task.Lines
                .OrderBy(x => (x.SourceLocation?.Code ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Sequence)
                .ThenBy(x => x.Id)
                .Select(x => new PickingTaskLineDto
                {
                    Id = x.Id,
                    AllocationId = x.AllocationId,
                    AllocationCode = (x.Allocation.AllocationCode ?? string.Empty).Trim(),
                    ProductId = x.ProductId,
                    ProductCode = (x.Product.Code ?? string.Empty).Trim(),
                    ProductName = (x.Product.Name ?? string.Empty).Trim(),
                    SourceLocationId = x.SourceLocationId,
                    SourceLocationCode = (x.SourceLocation.Code ?? string.Empty).Trim(),
                    SourceLocationName = (x.SourceLocation.Name ?? string.Empty).Trim(),
                    InventoryStatus = x.Allocation.InventoryStatus.ToString(),
                    LotId = x.Allocation.LotId,
                    LotNumber = x.Allocation.Lot?.LotNumber?.Trim(),
                    ExpiryDate = x.Allocation.Lot?.ExpiryDate,
                    SerialId = x.Allocation.SerialId,
                    SerialNumber = x.Allocation.Serial?.SerialNumber?.Trim(),
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
                Reason = (x.Reason ?? string.Empty).Trim(),
                ResolutionType = x.ResolutionType?.ToString(),
                Status = x.Status.ToString(),
                CreatedAt = x.CreatedAt,
                ResolvedAt = x.ResolvedAt,
                ResolutionNote = x.ResolutionNote?.Trim()
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
