using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class PackingService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser,
    IPackingSessionIntegration packingSessionIntegration,
    IShipmentIntegration? shipmentIntegration = null) : IPackingService, IPackingDispatchReadiness
{
    public async Task<IReadOnlyList<PackingSessionListDto>> ListAsync(
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
        if (Enum.TryParse<PackingSessionStatus>(status, true, out var parsed))
            query = query.Where(x => x.Status == parsed);

        var sessions = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(250)
            .ToListAsync(cancellationToken);
        return sessions.Select(MapList).ToList();
    }

    public async Task<PackingSessionDto> GetAsync(int id, CancellationToken cancellationToken = default) =>
        Map(await LoadAsync(id, false, cancellationToken));

    public async Task<PackingSessionDto> CreateAsync(
        CreatePackingSessionDto request,
        CancellationToken cancellationToken = default)
    {
        var task = await context.PickingTasks.AsNoTracking()
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.PickingTaskId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy Picking task.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(task.WarehouseId, cancellationToken);
        if (task.Status != PickingTaskStatus.Completed)
            throw Conflict("HU_CONTENT_MISMATCH", "Chỉ Picking task đã hoàn tất mới có thể mở Packing session.");
        if (task.Lines.Sum(x => x.PickedQuantity) <= 0)
            throw Conflict("HU_CONTENT_MISMATCH", "Picking task không có số lượng thực tế để đóng gói.");

        await packingSessionIntegration.EnsureForPickingTaskAsync(task.Id, currentUser.UserId, cancellationToken);
        var id = await context.PackingSessions.AsNoTracking()
            .Where(x => x.PickingTaskId == task.Id)
            .Select(x => x.Id)
            .SingleAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public Task<PackingSessionDto> CreateHandlingUnitAsync(
        int sessionId,
        CreateHandlingUnitDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(sessionId, request.RowVersion, async (session, token) =>
        {
            EnsureSessionMutable(session);
            if (!Enum.TryParse<HandlingUnitType>(request.Type, true, out var type))
                throw new BusinessRuleException("Loại Handling Unit không hợp lệ.");
            ValidateMetrics(request.GrossWeightKg, request.NetWeightKg, request.VolumeM3);

            var now = DateTime.UtcNow;
            var generated = $"HU-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..33].ToUpperInvariant();
            var code = NormalizeIdentity(request.HuCode, generated, 50);
            var barcode = NormalizeIdentity(request.Barcode, code, 64);
            var sscc = CleanScan(request.Sscc);
            if (sscc is { Length: > 32 })
                throw new BusinessRuleException("SSCC không được vượt quá 32 ký tự.");

            session.HandlingUnits.Add(new HandlingUnit
            {
                HuCode = code,
                Barcode = barcode,
                Sscc = sscc,
                WarehouseId = session.WarehouseId,
                Type = type,
                Status = HandlingUnitStatus.Open,
                GrossWeightKg = request.GrossWeightKg,
                NetWeightKg = request.NetWeightKg,
                VolumeM3 = request.VolumeM3,
                CreatedAt = now,
                CreatedBy = currentUser.UserId
            });
            AddAudit("HandlingUnit.Created", session, $"Type: {type}; HuCode: {code}; Barcode: {barcode}");
            await Task.CompletedTask;
        }, cancellationToken);

    public Task<PackingSessionDto> PackAsync(
        int sessionId,
        PackIntoHandlingUnitDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(sessionId, request.RowVersion, async (session, token) =>
        {
            EnsureSessionMutable(session);
            if (request.Quantity <= 0)
                throw new BusinessRuleException("Số lượng đóng gói phải lớn hơn 0.");

            var hu = session.HandlingUnits.SingleOrDefault(x => x.Id == request.HandlingUnitId)
                ?? throw HuNotFound();
            if (hu.Status is HandlingUnitStatus.Closed or HandlingUnitStatus.Staged or HandlingUnitStatus.Loaded or HandlingUnitStatus.Shipped or HandlingUnitStatus.Cancelled)
                throw Conflict("HU_CLOSED", "Handling Unit không còn ở trạng thái cho phép thêm content.");

            var line = session.PickingTask.Lines.SingleOrDefault(x => x.Id == request.PickingTaskLineId)
                ?? throw Conflict("HU_CONTENT_MISMATCH", "Dòng Picking không thuộc Packing session này.");
            if (line.Allocation?.Lot?.ExpiryDate?.Date < DateTime.UtcNow.Date)
                throw Conflict("HU_CONTENT_MISMATCH", "Lô hàng đã hết hạn sử dụng và không thể đóng gói.");

            var expectedLot = line.Allocation?.Lot?.LotNumber;
            var scannedLot = CleanScan(request.LotNumber);
            var requiresLotScan = !string.IsNullOrWhiteSpace(expectedLot) &&
                                  (line.Product.TrackingType != ProductTrackingType.None || line.Product.ExpiryControl);
            if (requiresLotScan || !string.IsNullOrWhiteSpace(scannedLot))
            {
                if (string.IsNullOrWhiteSpace(expectedLot) ||
                    string.IsNullOrWhiteSpace(scannedLot) ||
                    !string.Equals(expectedLot, scannedLot, StringComparison.OrdinalIgnoreCase))
                    throw Conflict("HU_CONTENT_MISMATCH", "LotNumber không khớp với dòng Picking đã phân bổ.");
            }

            var expectedSerial = line.Allocation?.Serial?.SerialNumber;
            var scannedSerial = CleanScan(request.SerialNumber);
            if (!string.IsNullOrWhiteSpace(expectedSerial) || !string.IsNullOrWhiteSpace(scannedSerial))
            {
                if (string.IsNullOrWhiteSpace(expectedSerial) ||
                    string.IsNullOrWhiteSpace(scannedSerial) ||
                    !string.Equals(expectedSerial, scannedSerial, StringComparison.OrdinalIgnoreCase))
                    throw Conflict("HU_CONTENT_MISMATCH", "SerialNumber không khớp với dòng Picking đã phân bổ.");
            }
            if (line.PickedQuantity <= 0)
                throw Conflict("PACK_QUANTITY_EXCEEDS_PICKED", "Dòng Picking chưa có số lượng thực tế để đóng gói.");
            ValidateQuantityPrecision(line, request.Quantity);
            await ValidateProductScanAsync(line, request.ProductBarcode, token);

            var packed = session.HandlingUnits.SelectMany(x => x.Contents)
                .Where(x => x.PickingTaskLineId == line.Id)
                .Sum(x => x.Quantity);
            if (packed + request.Quantity > line.PickedQuantity)
                throw Conflict("PACK_QUANTITY_EXCEEDS_PICKED", "Số lượng đóng gói vượt số lượng đã Picking.");

            var existing = hu.Contents.SingleOrDefault(x => x.PickingTaskLineId == line.Id);
            if (existing is null)
            {
                hu.Contents.Add(new HandlingUnitContent
                {
                    PickingTaskLineId = line.Id,
                    ProductId = line.ProductId,
                    Quantity = request.Quantity,
                    PackedAt = DateTime.UtcNow,
                    PackedBy = currentUser.UserId
                });
            }
            else
            {
                existing.Quantity += request.Quantity;
                existing.PackedAt = DateTime.UtcNow;
                existing.PackedBy = currentUser.UserId;
            }

            hu.Status = HandlingUnitStatus.InUse;
            session.Status = PackingSessionStatus.InProgress;
            session.StartedAt ??= DateTime.UtcNow;
            AddAudit(
                "PackingSession.PackedItem",
                session,
                $"HandlingUnitId: {hu.Id}; PickingTaskLineId: {line.Id}; ProductId: {line.ProductId}; Quantity: {request.Quantity}");
        }, cancellationToken);

    public Task<PackingSessionDto> CloseHandlingUnitAsync(
        int sessionId,
        int handlingUnitId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(sessionId, request.RowVersion, async (session, token) =>
        {
            EnsureSessionMutable(session);
            var hu = session.HandlingUnits.SingleOrDefault(x => x.Id == handlingUnitId) ?? throw HuNotFound();
            if (hu.Status == HandlingUnitStatus.Closed)
                throw Conflict("HU_CLOSED", "Handling Unit đã đóng.");
            if (hu.Status is HandlingUnitStatus.Staged or HandlingUnitStatus.Loaded or HandlingUnitStatus.Shipped or HandlingUnitStatus.Cancelled)
                throw Conflict("HU_CLOSED", "Handling Unit không còn ở trạng thái cho phép đóng.");
            var activeChildren = hu.Children.Where(x => x.Status != HandlingUnitStatus.Cancelled).ToList();
            if (hu.Contents.Count == 0 && activeChildren.Count == 0)
                throw Conflict("HU_CONTENT_MISMATCH", "Handling Unit rỗng không thể đóng.");
            if (activeChildren.Any(x => x.Status != HandlingUnitStatus.Closed))
                throw Conflict("HU_CLOSED", "Tất cả Handling Unit con phải ở trạng thái CLOSED trước khi đóng HU cha.");

            hu.Status = HandlingUnitStatus.Closed;
            hu.SealedAt = DateTime.UtcNow;
            hu.ClosedAt = DateTime.UtcNow;
            AddAudit("HandlingUnit.Closed", session, $"HandlingUnitId: {hu.Id}; HuCode: {hu.HuCode}");
            await Task.CompletedTask;
        }, cancellationToken);

    public Task<PackingSessionDto> CancelHandlingUnitAsync(
        int sessionId,
        int handlingUnitId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(sessionId, request.RowVersion, async (session, token) =>
        {
            EnsureSessionMutable(session);
            var hu = session.HandlingUnits.SingleOrDefault(x => x.Id == handlingUnitId) ?? throw HuNotFound();
            if (hu.Status == HandlingUnitStatus.Cancelled) return;
            if (hu.Contents.Count != 0 || hu.Children.Count != 0)
                throw Conflict("HU_CONTENT_MISMATCH", "Chỉ Handling Unit rỗng, không chứa HU con mới có thể hủy.");
            if (hu.Status is HandlingUnitStatus.Staged or HandlingUnitStatus.Loaded or HandlingUnitStatus.Shipped)
                throw Conflict("HU_CLOSED", "Handling Unit đã đi vào downstream và không thể hủy.");

            hu.ParentHandlingUnitId = null;
            hu.Status = HandlingUnitStatus.Cancelled;
            AddAudit("HandlingUnit.Cancelled", session, $"HandlingUnitId: {hu.Id}; HuCode: {hu.HuCode}");
            await Task.CompletedTask;
        }, cancellationToken);

    public Task<PackingSessionDto> NestHandlingUnitAsync(
        int sessionId,
        int handlingUnitId,
        NestHandlingUnitDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(sessionId, request.RowVersion, async (session, token) =>
        {
            EnsureSessionMutable(session);
            var child = session.HandlingUnits.SingleOrDefault(x => x.Id == handlingUnitId) ?? throw HuNotFound();
            var parent = session.HandlingUnits.SingleOrDefault(x => x.Id == request.ParentHandlingUnitId) ?? throw HuNotFound();
            if (child.Id == parent.Id)
                throw Conflict("HU_CIRCULAR_NESTING", "Handling Unit không thể chứa chính nó.");
            EnsureNestableChild(child);
            if (parent.Status is not HandlingUnitStatus.Open and not HandlingUnitStatus.InUse)
                throw Conflict("HU_CLOSED", "Handling Unit cha không còn mở để nhận HU con.");

            var cursor = parent;
            while (true)
            {
                if (cursor.Id == child.Id)
                    throw Conflict("HU_CIRCULAR_NESTING", "Không thể tạo vòng lặp trong cấu trúc Handling Unit.");
                if (!cursor.ParentHandlingUnitId.HasValue) break;
                cursor = session.HandlingUnits.SingleOrDefault(x => x.Id == cursor.ParentHandlingUnitId.Value)
                    ?? throw Conflict("HU_CONTENT_MISMATCH", "Cấu trúc Handling Unit hiện tại không hợp lệ.");
            }

            child.ParentHandlingUnitId = parent.Id;
            if (parent.Status == HandlingUnitStatus.Open)
                parent.Status = HandlingUnitStatus.InUse;
            AddAudit("HandlingUnit.Nested", session, $"ChildId: {child.Id}; ParentId: {parent.Id}");
            await Task.CompletedTask;
        }, cancellationToken);

    public Task<PackingSessionDto> UnnestHandlingUnitAsync(
        int sessionId,
        int handlingUnitId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(sessionId, request.RowVersion, async (session, token) =>
        {
            EnsureSessionMutable(session);
            var child = session.HandlingUnits.SingleOrDefault(x => x.Id == handlingUnitId) ?? throw HuNotFound();
            EnsureNestableChild(child);
            if (!child.ParentHandlingUnitId.HasValue)
                throw Conflict("HU_CONTENT_MISMATCH", "Handling Unit hiện không nằm trong HU cha.");
            var parent = session.HandlingUnits.SingleOrDefault(x => x.Id == child.ParentHandlingUnitId.Value)
                ?? throw Conflict("HU_CONTENT_MISMATCH", "Cấu trúc Handling Unit hiện tại không hợp lệ.");
            if (parent.Status is not HandlingUnitStatus.Open and not HandlingUnitStatus.InUse)
                throw Conflict("HU_CLOSED", "Handling Unit cha đã đóng và không thể tách HU con.");
            var oldParent = child.ParentHandlingUnitId.Value;
            child.ParentHandlingUnitId = null;
            AddAudit("HandlingUnit.Unnested", session, $"ChildId: {child.Id}; OldParentId: {oldParent}");
            await Task.CompletedTask;
        }, cancellationToken);

    public Task<PackingSessionDto> CompleteAsync(
        int sessionId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(sessionId, request.RowVersion, async (session, token) =>
        {
            if (session.Status != PackingSessionStatus.InProgress)
                throw Conflict("HU_CONTENT_MISMATCH", "Packing session không ở trạng thái có thể xác nhận PACKED.");

            var allContents = session.HandlingUnits.SelectMany(x => x.Contents).ToList();
            foreach (var line in session.PickingTask.Lines)
            {
                var packed = allContents.Where(x => x.PickingTaskLineId == line.Id).Sum(x => x.Quantity);
                if (packed != line.PickedQuantity)
                    throw Conflict("HU_CONTENT_MISMATCH", $"Dòng {line.Product.Code} chưa đóng gói đủ số lượng đã Picking.");
            }

            var activeHus = session.HandlingUnits.Where(x => x.Status != HandlingUnitStatus.Cancelled).ToList();
            if (activeHus.Count == 0 || activeHus.Any(x => x.Contents.Count == 0 && x.Children.All(c => c.Status == HandlingUnitStatus.Cancelled)))
                throw Conflict("HU_CONTENT_MISMATCH", "Packing session còn Handling Unit rỗng.");
            if (activeHus.Any(x => x.Status != HandlingUnitStatus.Closed))
                throw Conflict("HU_CLOSED", "Tất cả Handling Unit đang sử dụng phải được đóng trước khi xác nhận PACKED.");

            session.Status = PackingSessionStatus.Packed;
            session.PackedAt = DateTime.UtcNow;
            AddAudit("PackingSession.Packed", session, $"PackedQuantity: {allContents.Sum(x => x.Quantity)}; HandlingUnits: {activeHus.Count}");
            if (shipmentIntegration is not null)
                await shipmentIntegration.EnsureForPackingSessionAsync(session.Id, currentUser.UserId, token);
        }, cancellationToken);

    public Task<PackingSessionDto> CloseAsync(
        int sessionId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(sessionId, request.RowVersion, async (session, token) =>
        {
            if (session.Status != PackingSessionStatus.Packed)
                throw Conflict("HU_CONTENT_MISMATCH", "Chỉ Packing session PACKED mới có thể đóng.");
            session.Status = PackingSessionStatus.Closed;
            session.ClosedAt = DateTime.UtcNow;
            AddAudit("PackingSession.Closed", session, $"PackedAt: {session.PackedAt:O}");
            await Task.CompletedTask;
        }, cancellationToken);

    public Task<PackingSessionDto> CancelAsync(
        int sessionId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(sessionId, request.RowVersion, async (session, token) =>
        {
            if (session.Status is not PackingSessionStatus.Open and not PackingSessionStatus.InProgress)
                throw Conflict("HU_CONTENT_MISMATCH", "Packing session không còn ở trạng thái cho phép hủy.");
            if (session.HandlingUnits.SelectMany(x => x.Contents).Any())
                throw Conflict("HU_CONTENT_MISMATCH", "Packing session đã có content. Remove/Repack chưa có trong foundation nên không thể hủy an toàn.");
            foreach (var hu in session.HandlingUnits.Where(x => x.Status != HandlingUnitStatus.Cancelled))
                hu.Status = HandlingUnitStatus.Cancelled;
            session.Status = PackingSessionStatus.Cancelled;
            AddAudit("PackingSession.Cancelled", session, $"HandlingUnits: {session.HandlingUnits.Count}");
            await Task.CompletedTask;
        }, cancellationToken);

    public async Task EnsureSourceReadyAsync(
        string sourceType,
        int sourceId,
        CancellationToken cancellationToken = default)
    {
        var session = await Query().AsNoTracking().SingleOrDefaultAsync(
            x => x.PickingTask.SourceType == sourceType && x.PickingTask.SourceId == sourceId,
            cancellationToken);
        if (session is null) return;

        if (session.Status is not PackingSessionStatus.Packed and not PackingSessionStatus.Closed)
            throw new ConcurrencyException("Packing session của chứng từ chưa hoàn tất.");

        var contents = session.HandlingUnits.SelectMany(x => x.Contents).ToList();
        foreach (var line in session.PickingTask.Lines)
        {
            var packed = contents.Where(x => x.PickingTaskLineId == line.Id).Sum(x => x.Quantity);
            if (packed != line.PickedQuantity)
                throw new ConcurrencyException("Packing content không còn khớp số lượng đã Picking.");
        }
        var shipmentExists = await context.Shipments.AsNoTracking()
            .AnyAsync(x => x.PackingSessionId == session.Id, cancellationToken);
        if (!shipmentExists && session.HandlingUnits
            .Where(x => x.Status != HandlingUnitStatus.Cancelled)
            .Any(x => x.Status != HandlingUnitStatus.Closed))
            throw new ConcurrencyException("Handling Unit chưa được đóng đầy đủ.");
    }
}
