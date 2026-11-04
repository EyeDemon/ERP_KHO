using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class PackingService
{
    public async Task<IReadOnlyList<HandlingUnitDto>> ListHandlingUnitsAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var accessible = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (warehouseId.HasValue)
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            accessible = [warehouseId.Value];
        }

        var rows = await HandlingUnitQuery().AsNoTracking()
            .Where(x => accessible.Contains(x.WarehouseId))
            .OrderByDescending(x => x.CreatedAt)
            .Take(250)
            .ToListAsync(cancellationToken);
        return rows.Select(MapHandlingUnit).ToList();
    }

    public async Task<HandlingUnitDto> GetHandlingUnitAsync(int id, CancellationToken cancellationToken = default)
    {
        var hu = await HandlingUnitQuery().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw HuNotFound();
        await warehouseAuthorization.EnsureWarehouseAccessAsync(hu.WarehouseId, cancellationToken);
        return MapHandlingUnit(hu);
    }

    private async Task<PackingSessionDto> MutateAsync(
        int id,
        string encodedRowVersion,
        Func<PackingSession, CancellationToken, Task> mutation,
        CancellationToken cancellationToken)
    {
        var ownTransaction = context.Database.CurrentTransaction is null;
        await using var transaction = ownTransaction
            ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        try
        {
            var session = await LoadAsync(id, true, cancellationToken);
            ApplyVersion(session, encodedRowVersion);
            await mutation(session, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return Map(session);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new ConcurrencyException("Dữ liệu Packing đã thay đổi. Vui lòng tải lại và thử lại.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw Conflict("HU_CONTENT_MISMATCH", "Mã/Barcode/SSCC Handling Unit đã tồn tại hoặc content bị trùng.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 })
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new DeadlockException("Giao dịch Packing bị deadlock.", ex);
        }
        catch (SqlException ex) when (ex.Number == 1205)
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new DeadlockException("Giao dịch Packing bị deadlock.", ex);
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<PackingSession> LoadAsync(int id, bool tracking, CancellationToken token)
    {
        IQueryable<PackingSession> query = Query();
        if (!tracking) query = query.AsNoTracking();
        var session = await query.SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("Không tìm thấy Packing session hoặc bạn không có quyền truy cập.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(session.WarehouseId, token);
        return session;
    }

    private IQueryable<PackingSession> Query() =>
        context.PackingSessions
            .AsSplitQuery()
            .Include(x => x.Warehouse)
            .Include(x => x.PickingTask).ThenInclude(x => x.Lines).ThenInclude(x => x.Product).ThenInclude(x => x.Unit)
            .Include(x => x.PickingTask).ThenInclude(x => x.Lines).ThenInclude(x => x.Allocation).ThenInclude(x => x.Lot)
            .Include(x => x.PickingTask).ThenInclude(x => x.Lines).ThenInclude(x => x.Allocation).ThenInclude(x => x.Serial)
            .Include(x => x.HandlingUnits).ThenInclude(x => x.ParentHandlingUnit)
            .Include(x => x.HandlingUnits).ThenInclude(x => x.Children)
            .Include(x => x.HandlingUnits).ThenInclude(x => x.Contents).ThenInclude(x => x.Product);

    private IQueryable<HandlingUnit> HandlingUnitQuery() =>
        context.HandlingUnits
            .AsSplitQuery()
            .Include(x => x.ParentHandlingUnit)
            .Include(x => x.Contents).ThenInclude(x => x.Product);

    private void ApplyVersion(PackingSession session, string encoded)
    {
        byte[] expected;
        try { expected = Convert.FromBase64String(encoded); }
        catch { throw new BusinessRuleException("Phiên bản dữ liệu Packing không hợp lệ."); }
        if (!session.RowVersion.SequenceEqual(expected))
            throw Conflict("HU_CONTENT_MISMATCH", "Dữ liệu Packing đã thay đổi. Vui lòng tải lại và thử lại.");
        context.Entry(session).Property(x => x.RowVersion).OriginalValue = expected;
    }

    private static void EnsureSessionMutable(PackingSession session)
    {
        if (session.Status is not PackingSessionStatus.Open and not PackingSessionStatus.InProgress)
            throw Conflict("HU_CONTENT_MISMATCH", "Packing session không còn ở trạng thái cho phép thay đổi content/HU.");
    }

    private static void EnsureNestableChild(HandlingUnit hu)
    {
        if (hu.Status is HandlingUnitStatus.Staged or HandlingUnitStatus.Loaded or HandlingUnitStatus.Shipped or HandlingUnitStatus.Cancelled)
            throw Conflict("HU_CLOSED", "Handling Unit đã đi vào downstream hoặc đã hủy.");
    }

    private async Task ValidateProductScanAsync(PickingTaskLine line, string raw, CancellationToken token)
    {
        var scanned = CleanScan(raw);
        if (string.IsNullOrWhiteSpace(scanned))
            throw Conflict("HU_CONTENT_MISMATCH", "Barcode sản phẩm là bắt buộc.");
        if (string.Equals(scanned, line.Product.Code, StringComparison.OrdinalIgnoreCase)) return;
        if (!await context.ProductBarcodes.AsNoTracking().AnyAsync(
                x => x.ProductId == line.ProductId && x.Value == scanned,
                token))
            throw Conflict("HU_CONTENT_MISMATCH", "Barcode không khớp sản phẩm đã Picking.");
    }

    private static string? CleanScan(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = new string(value.Where(ch => ch >= 32 && ch != 127 && ch != '\u200b' && ch != '\ufeff').ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static void ValidateQuantityPrecision(PickingTaskLine line, decimal quantity)
    {
        var places = line.Product.Unit.DecimalPlaces;
        if (decimal.Round(quantity, places) != quantity)
            throw new BusinessRuleException($"Số lượng vượt quá độ chính xác Base UOM cho phép ({places} chữ số thập phân).");
    }

    private static void ValidateMetrics(decimal? gross, decimal? net, decimal? volume)
    {
        if (gross <= 0 || net <= 0 || volume <= 0)
            throw new BusinessRuleException("Weight/volume nếu khai báo phải lớn hơn 0.");
        if (gross.HasValue && net.HasValue && gross.Value < net.Value)
            throw new BusinessRuleException("Gross weight không được nhỏ hơn net weight.");
    }

    private static string NormalizeIdentity(string? requested, string fallback, int maxLength)
    {
        var cleaned = CleanScan(requested);
        var value = string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned.ToUpperInvariant();
        if (value.Length is < 1 || value.Length > maxLength)
            throw new BusinessRuleException("Mã/Barcode Handling Unit không hợp lệ.");
        return value;
    }

    private void AddAudit(string action, PackingSession session, string values) =>
        context.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = action,
            EntityName = "PackingSession",
            EntityId = session.Id,
            WarehouseId = session.WarehouseId,
            Timestamp = DateTime.UtcNow,
            NewValues = values,
            Result = "Success",
            Severity = "Information"
        });

    private static PackingSessionListDto MapList(PackingSession session)
    {
        var required = session.PickingTask.Lines.Sum(x => x.PickedQuantity);
        var packed = session.HandlingUnits.SelectMany(x => x.Contents).Sum(x => x.Quantity);
        return new PackingSessionListDto
        {
            Id = session.Id,
            SessionCode = (session.SessionCode ?? string.Empty).Trim(),
            PickingTaskId = session.PickingTaskId,
            PickingTaskCode = (session.PickingTask.TaskCode ?? string.Empty).Trim(),
            SourceType = (session.PickingTask.SourceType ?? string.Empty).Trim(),
            SourceId = session.PickingTask.SourceId,
            SourceCode = (session.PickingTask.SourceCode ?? string.Empty).Trim(),
            WarehouseId = session.WarehouseId,
            WarehouseName = (session.Warehouse.Name ?? string.Empty).Trim(),
            Status = session.Status.ToString(),
            RequiredQuantity = required,
            PackedQuantity = packed,
            RemainingQuantity = Math.Max(0m, required - packed),
            HandlingUnitCount = session.HandlingUnits.Count(x => x.Status != HandlingUnitStatus.Cancelled),
            CreatedAt = session.CreatedAt,
            StartedAt = session.StartedAt,
            PackedAt = session.PackedAt,
            ClosedAt = session.ClosedAt
        };
    }

    private static PackingSessionDto Map(PackingSession session)
    {
        var summary = MapList(session);
        return new PackingSessionDto
        {
            Id = summary.Id,
            SessionCode = summary.SessionCode,
            PickingTaskId = summary.PickingTaskId,
            PickingTaskCode = summary.PickingTaskCode,
            SourceType = summary.SourceType,
            SourceId = summary.SourceId,
            SourceCode = summary.SourceCode,
            WarehouseId = summary.WarehouseId,
            WarehouseName = summary.WarehouseName,
            Status = summary.Status,
            RequiredQuantity = summary.RequiredQuantity,
            PackedQuantity = summary.PackedQuantity,
            RemainingQuantity = summary.RemainingQuantity,
            HandlingUnitCount = summary.HandlingUnitCount,
            CreatedAt = summary.CreatedAt,
            StartedAt = summary.StartedAt,
            PackedAt = summary.PackedAt,
            ClosedAt = summary.ClosedAt,
            RowVersion = Convert.ToBase64String(session.RowVersion),
            Lines = session.PickingTask.Lines.OrderBy(x => x.Sequence).Select(line =>
            {
                var packed = session.HandlingUnits.SelectMany(x => x.Contents)
                    .Where(x => x.PickingTaskLineId == line.Id)
                    .Sum(x => x.Quantity);
                return new PackingSourceLineDto
                {
                    PickingTaskLineId = line.Id,
                    ProductId = line.ProductId,
                    ProductCode = (line.Product.Code ?? string.Empty).Trim(),
                    ProductName = (line.Product.Name ?? string.Empty).Trim(),
                    PickedQuantity = line.PickedQuantity,
                    PackedQuantity = packed,
                    RemainingQuantity = Math.Max(0m, line.PickedQuantity - packed)
                };
            }).ToList(),
            HandlingUnits = session.HandlingUnits.OrderBy(x => x.Id).Select(MapHandlingUnit).ToList()
        };
    }

    private static HandlingUnitDto MapHandlingUnit(HandlingUnit hu) => new()
    {
        Id = hu.Id,
        HuCode = (hu.HuCode ?? string.Empty).Trim(),
        Barcode = (hu.Barcode ?? string.Empty).Trim(),
        Sscc = hu.Sscc?.Trim(),
        WarehouseId = hu.WarehouseId,
        PackingSessionId = hu.PackingSessionId,
        ParentHandlingUnitId = hu.ParentHandlingUnitId,
        ParentHandlingUnitCode = hu.ParentHandlingUnit?.HuCode?.Trim(),
        Type = hu.Type.ToString(),
        Status = hu.Status.ToString(),
        GrossWeightKg = hu.GrossWeightKg,
        NetWeightKg = hu.NetWeightKg,
        VolumeM3 = hu.VolumeM3,
        SealedAt = hu.SealedAt,
        CreatedAt = hu.CreatedAt,
        ClosedAt = hu.ClosedAt,
        RowVersion = Convert.ToBase64String(hu.RowVersion),
        Contents = hu.Contents.OrderBy(x => x.Id).Select(x => new HandlingUnitContentDto
        {
            Id = x.Id,
            PickingTaskLineId = x.PickingTaskLineId,
            ProductId = x.ProductId,
            ProductCode = (x.Product.Code ?? string.Empty).Trim(),
            ProductName = (x.Product.Name ?? string.Empty).Trim(),
            Quantity = x.Quantity,
            PackedAt = x.PackedAt
        }).ToList()
    };

    private static BusinessRuleException Conflict(string code, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["HttpStatusCode"] = 409;
        ex.Data["ErrorCode"] = code;
        return ex;
    }

    private static NotFoundException HuNotFound()
    {
        var ex = new NotFoundException("Không tìm thấy Handling Unit.");
        ex.Data["ErrorCode"] = "HU_NOT_FOUND";
        return ex;
    }
}
