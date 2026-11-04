using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ShipmentService
{
    private async Task<ShipmentDto> MutateAsync(
        int id,
        string encodedRowVersion,
        Func<Shipment, CancellationToken, Task> mutation,
        CancellationToken cancellationToken)
    {
        var ownTransaction = context.Database.CurrentTransaction is null;
        var maxAttempts = ownTransaction && context.Database.IsSqlServer() ? 4 : 1;
        for (var attempt = 1; ; attempt++)
        {
            await using var transaction = ownTransaction
                ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                : null;
            try
            {
                var shipment = await LoadAsync(id, true, cancellationToken);
                ApplyVersion(shipment, encodedRowVersion);
                await mutation(shipment, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                var dto = Map(shipment);
                var linkedEr = await context.ExportReceipts.AsNoTracking()
                    .Where(x => x.ShipmentId == shipment.Id)
                    .Select(x => new { x.Id, x.Code })
                    .FirstOrDefaultAsync(cancellationToken);
                if (linkedEr is not null)
                {
                    dto.ExportReceiptId = linkedEr.Id;
                    dto.ExportReceiptCode = linkedEr.Code.Trim();
                }
                else if (shipment.SourceType == "ExportReceipt" && shipment.SourceId.HasValue)
                {
                    dto.ExportReceiptId = shipment.SourceId;
                    dto.ExportReceiptCode = shipment.SourceCode?.Trim();
                }
                return dto;
            }
            catch (Exception ex) when (ownTransaction && attempt < maxAttempts &&
                                       (ex is DeadlockException ||
                                        ex is SqlException { Number: 1205 } ||
                                        ex is DbUpdateException { InnerException: SqlException { Number: 1205 } }))
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                context.ChangeTracker.Clear();
                await Task.Delay(15 * attempt, cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                throw new ConcurrencyException("Dữ liệu Shipment đã thay đổi. Vui lòng tải lại và thử lại.", ex);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql &&
                                               sql.Message.Contains("IX_InventoryTransactions_ShipmentReference", StringComparison.OrdinalIgnoreCase))
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                throw new ConcurrencyException("Shipment đã được dispatch bởi yêu cầu đồng thời khác.", ex);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 })
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                throw new DeadlockException("Giao dịch Shipment bị deadlock.", ex);
            }
            catch (SqlException ex) when (ex.Number == 1205)
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                throw new DeadlockException("Giao dịch Shipment bị deadlock.", ex);
            }
            catch
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }

    private async Task<Shipment> LoadAsync(int id, bool tracking, CancellationToken token)
    {
        IQueryable<Shipment> query = Query();
        if (!tracking) query = query.AsNoTracking();
        var shipment = await query.SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("Không tìm thấy Shipment hoặc bạn không có quyền truy cập.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(shipment.WarehouseId, token);
        return shipment;
    }

    private IQueryable<Shipment> Query() =>
        context.Shipments
            .AsSplitQuery()
            .Include(x => x.Warehouse)
            .Include(x => x.PackingSession).ThenInclude(x => x.HandlingUnits).ThenInclude(x => x.Contents)
            .Include(x => x.PackingSession).ThenInclude(x => x.PickingTask).ThenInclude(x => x.Lines).ThenInclude(x => x.Allocation)
            .Include(x => x.PackingSession).ThenInclude(x => x.PickingTask).ThenInclude(x => x.Lines).ThenInclude(x => x.ShortPicks)
            .Include(x => x.StagingLocation)
            .Include(x => x.DockAppointment)
            .Include(x => x.Dock)
            .Include(x => x.DispatchedByUser)
            .Include(x => x.ProofOfDelivery)
            .Include(x => x.TrackingEvents)
            .Include(x => x.HandlingUnits).ThenInclude(x => x.HandlingUnit);

    private static List<HandlingUnit> ActivePackingHus(Shipment shipment) =>
        shipment.PackingSession.HandlingUnits
            .Where(x => x.Status != HandlingUnitStatus.Cancelled)
            .OrderBy(x => x.Id)
            .ToList();

    private static void ValidateAssignedRoots(Shipment shipment, List<HandlingUnit> active)
    {
        var roots = active.Where(x => x.ParentHandlingUnitId is null).Select(x => x.Id).Order().ToArray();
        var assigned = shipment.HandlingUnits.Select(x => x.HandlingUnitId).Order().ToArray();
        if (roots.Length == 0 || !roots.SequenceEqual(assigned))
            throw Conflict("SHIPMENT_HU_MISMATCH", "Danh sách Handling Unit gốc không còn khớp Packing session.");
    }

    private static List<HandlingUnit> Hierarchy(List<HandlingUnit> all, int rootId)
    {
        var result = new List<HandlingUnit>();
        var queue = new Queue<int>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            var current = all.SingleOrDefault(x => x.Id == id)
                ?? throw Conflict("SHIPMENT_HU_MISMATCH", "Handling Unit hierarchy không hợp lệ.");
            result.Add(current);
            foreach (var child in all.Where(x => x.ParentHandlingUnitId == id))
                queue.Enqueue(child.Id);
        }
        return result;
    }

    private void ApplyVersion(Shipment shipment, string encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            context.Entry(shipment).Property(x => x.RowVersion).OriginalValue = shipment.RowVersion;
            return;
        }
        byte[] expected;
        try { expected = Convert.FromBase64String(encoded); }
        catch { throw new BusinessRuleException("Phiên bản dữ liệu Shipment không hợp lệ."); }
        if (!shipment.RowVersion.SequenceEqual(expected))
            throw Conflict("SHIPMENT_VERSION_CONFLICT", "Dữ liệu Shipment đã thay đổi. Vui lòng tải lại.");
        context.Entry(shipment).Property(x => x.RowVersion).OriginalValue = expected;
    }

    private static void RequireStatus(Shipment shipment, ShipmentStatus expected, string message)
    {
        if (shipment.Status != expected)
            throw Conflict("SHIPMENT_STATE_INVALID", message);
    }

    private void AddAudit(string action, Shipment shipment, string? values) =>
        context.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = action,
            EntityName = "Shipment",
            EntityId = shipment.Id,
            WarehouseId = shipment.WarehouseId,
            Timestamp = DateTime.UtcNow,
            NewValues = values,
            Result = "Success",
            Severity = "Information"
        });

    private static ShipmentTrackingEventDto MapTrackingEvent(ShipmentTrackingEvent item)
    {
        var rawNote = item.Note?.Trim();
        var normalizedNote = string.Equals(rawNote, "Inventory SHIP boundary committed.", StringComparison.OrdinalIgnoreCase)
            ? "Đã xác nhận xuất kho và trừ tồn thực tế."
            : rawNote;
        return new ShipmentTrackingEventDto
        {
            Id = item.Id,
            EventType = item.EventType.Trim(),
            FromStatus = item.FromStatus.ToString(),
            ToStatus = item.ToStatus.ToString(),
            OccurredAt = item.OccurredAt,
            RecordedAt = item.RecordedAt,
            Source = item.Source.Trim(),
            SourceEventId = item.SourceEventId?.Trim(),
            ReasonCode = item.ReasonCode?.Trim(),
            Note = normalizedNote
        };
    }

    private static ShipmentProofOfDeliveryDto? MapProofOfDelivery(ShipmentProofOfDelivery? pod) =>
        pod is null ? null : new ShipmentProofOfDeliveryDto
        {
            DeliveredAt = pod.DeliveredAt,
            ReceiverName = pod.ReceiverName.Trim(),
            EvidenceReference = pod.EvidenceReference?.Trim(),
            Latitude = pod.Latitude,
            Longitude = pod.Longitude,
            CarrierReference = pod.CarrierReference?.Trim(),
            DeliveryNote = pod.DeliveryNote?.Trim(),
            CreatedAt = pod.CreatedAt
        };

    private static ShipmentTrackingDto MapTracking(Shipment shipment) => new()
    {
        ShipmentId = shipment.Id,
        ShipmentCode = shipment.ShipmentCode.Trim(),
        Status = shipment.Status.ToString(),
        DispatchedAt = shipment.DispatchedAt,
        InTransitAt = shipment.InTransitAt,
        DeliveryFailedAt = shipment.DeliveryFailedAt,
        ReturnInitiatedAt = shipment.ReturnInitiatedAt,
        DeliveredAt = shipment.ProofOfDelivery?.DeliveredAt,
        CompletedAt = shipment.CompletedAt,
        ProofOfDelivery = MapProofOfDelivery(shipment.ProofOfDelivery),
        Events = shipment.TrackingEvents.OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).Select(MapTrackingEvent).ToList()
    };

    private static string? CleanScan(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = new string(value.Where(ch => ch >= 32 && ch != 127 && ch != '\u200b' && ch != '\ufeff').ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static string? NormalizeSeal(string? value)
    {
        var normalized = CleanScan(value)?.ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        if (normalized.Length > 80)
            throw new BusinessRuleException("Seal number không được vượt quá 80 ký tự.");
        return normalized;
    }

    private static ShipmentListDto MapList(Shipment shipment) => new()
    {
        Id = shipment.Id,
        ShipmentCode = shipment.ShipmentCode.Trim(),
        PackingSessionId = shipment.PackingSessionId,
        PackingSessionCode = shipment.PackingSession.SessionCode.Trim(),
        WarehouseId = shipment.WarehouseId,
        WarehouseName = shipment.Warehouse.Name.Trim(),
        SourceType = shipment.SourceType.Trim(),
        SourceId = shipment.SourceId,
        SourceCode = shipment.SourceCode?.Trim(),
        Status = shipment.Status.ToString(),
        StagingLocationId = shipment.StagingLocationId,
        StagingLocationCode = shipment.StagingLocation?.Code?.Trim(),
        HandlingUnitCount = shipment.HandlingUnits.Count,
        LoadedHandlingUnitCount = shipment.HandlingUnits.Count(x => x.LoadedAt.HasValue),
        DockAppointmentId = shipment.DockAppointmentId,
        DockAppointmentCode = shipment.DockAppointment?.Code?.Trim(),
        DockId = shipment.DockId,
        DockCode = shipment.Dock?.Code?.Trim(),
        VehiclePlate = shipment.VehiclePlate?.Trim(),
        TrailerPlate = shipment.TrailerPlate?.Trim(),
        SealNumber = shipment.SealNumber?.Trim(),
        CreatedAt = shipment.CreatedAt,
        StagedAt = shipment.StagedAt,
        LoadingStartedAt = shipment.LoadingStartedAt,
        LoadedAt = shipment.LoadedAt,
        DispatchedAt = shipment.DispatchedAt,
        DispatchedBy = shipment.DispatchedBy,
        DispatchedByName = shipment.DispatchedByUser?.FullName?.Trim(),
        InTransitAt = shipment.InTransitAt,
        DeliveryFailedAt = shipment.DeliveryFailedAt,
        ReturnInitiatedAt = shipment.ReturnInitiatedAt,
        DeliveredAt = shipment.ProofOfDelivery?.DeliveredAt,
        CompletedAt = shipment.CompletedAt
    };

    private static ShipmentDto Map(Shipment shipment)
    {
        var summary = MapList(shipment);
        var all = shipment.PackingSession.HandlingUnits.ToList();
        return new ShipmentDto
        {
            Id = summary.Id,
            ShipmentCode = summary.ShipmentCode,
            PackingSessionId = summary.PackingSessionId,
            PackingSessionCode = summary.PackingSessionCode,
            WarehouseId = summary.WarehouseId,
            WarehouseName = summary.WarehouseName,
            SourceType = summary.SourceType,
            SourceId = summary.SourceId,
            SourceCode = summary.SourceCode,
            Status = summary.Status,
            StagingLocationId = summary.StagingLocationId,
            StagingLocationCode = summary.StagingLocationCode,
            HandlingUnitCount = summary.HandlingUnitCount,
            LoadedHandlingUnitCount = summary.LoadedHandlingUnitCount,
            DockAppointmentId = summary.DockAppointmentId,
            DockAppointmentCode = summary.DockAppointmentCode,
            DockId = summary.DockId,
            DockCode = summary.DockCode,
            VehiclePlate = summary.VehiclePlate,
            TrailerPlate = summary.TrailerPlate,
            SealNumber = summary.SealNumber,
            CreatedAt = summary.CreatedAt,
            StagedAt = summary.StagedAt,
            LoadingStartedAt = summary.LoadingStartedAt,
            LoadedAt = summary.LoadedAt,
            DispatchedAt = summary.DispatchedAt,
            DispatchedBy = summary.DispatchedBy,
            DispatchedByName = summary.DispatchedByName,
            InTransitAt = summary.InTransitAt,
            DeliveryFailedAt = summary.DeliveryFailedAt,
            ReturnInitiatedAt = summary.ReturnInitiatedAt,
            DeliveredAt = summary.DeliveredAt,
            CompletedAt = summary.CompletedAt,
            RowVersion = Convert.ToBase64String(shipment.RowVersion),
            ProofOfDelivery = MapProofOfDelivery(shipment.ProofOfDelivery),
            TrackingEvents = shipment.TrackingEvents.OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).Select(MapTrackingEvent).ToList(),
            HandlingUnits = shipment.HandlingUnits.OrderBy(x => x.Sequence).Select(link =>
            {
                var tree = Hierarchy(all, link.HandlingUnitId);
                return new ShipmentHandlingUnitDto
                {
                    Id = link.Id,
                    HandlingUnitId = link.HandlingUnitId,
                    HuCode = link.HandlingUnit.HuCode.Trim(),
                    Barcode = link.HandlingUnit.Barcode.Trim(),
                    Sscc = link.HandlingUnit.Sscc?.Trim(),
                    Type = link.HandlingUnit.Type.ToString(),
                    Status = link.HandlingUnit.Status.ToString(),
                    Sequence = link.Sequence,
                    ContentQuantity = tree.SelectMany(x => x.Contents).Sum(x => x.Quantity),
                    AssignedAt = link.AssignedAt,
                    StagedAt = link.StagedAt,
                    LoadedAt = link.LoadedAt
                };
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
