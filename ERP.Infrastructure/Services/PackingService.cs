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

public sealed class PackingService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser,
    IPackingSessionIntegration packingSessionIntegration) : IPackingService
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
            var code = NormalizeIdentity(request.HuCode, generated);
            var barcode = NormalizeIdentity(request.Barcode, code);
            var sscc = string.IsNullOrWhiteSpace(request.Sscc) ? null : request.Sscc.Trim();

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
            if (!string.IsNullOrWhiteSpace(request.LotNumber) || !string.IsNullOrWhiteSpace(request.SerialNumber))
                throw Conflict("HU_CONTENT_MISMATCH", "Lot/Serial canonical chưa được triển khai nên chưa thể xác nhận content theo Lot/Serial.");

            var hu = session.HandlingUnits.SingleOrDefault(x => x.Id == request.HandlingUnitId)
                ?? throw HuNotFound();
            if (hu.Status is HandlingUnitStatus.Closed or HandlingUnitStatus.Staged or HandlingUnitStatus.Loaded or HandlingUnitStatus.Shipped or HandlingUnitStatus.Cancelled)
                throw Conflict("HU_CLOSED", "Handling Unit không còn ở trạng thái cho phép thêm content.");

            var line = session.PickingTask.Lines.SingleOrDefault(x => x.Id == request.PickingTaskLineId)
                ?? throw Conflict("HU_CONTENT_MISMATCH", "Dòng Picking không thuộc Packing session này.");
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
            if (hu.Contents.Count == 0)
                throw Conflict("HU_CONTENT_MISMATCH", "Handling Unit rỗng không thể đóng.");

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
            if (activeHus.Count == 0 || activeHus.Any(x => x.Contents.Count == 0 && x.Children.Count == 0))
                throw Conflict("HU_CONTENT_MISMATCH", "Packing session còn Handling Unit rỗng.");
            if (activeHus.Any(x => x.Status != HandlingUnitStatus.Closed))
                throw Conflict("HU_CLOSED", "Tất cả Handling Unit đang sử dụng phải được đóng trước khi xác nhận PACKED.");

            session.Status = PackingSessionStatus.Packed;
            session.PackedAt = DateTime.UtcNow;
            AddAudit("PackingSession.Packed", session, $"PackedQuantity: {allContents.Sum(x => x.Quantity)}; HandlingUnits: {activeHus.Count}");
            await Task.CompletedTask;
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
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
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
        var scanned = raw?.Trim();
        if (string.IsNullOrWhiteSpace(scanned))
            throw Conflict("HU_CONTENT_MISMATCH", "Barcode sản phẩm là bắt buộc.");
        if (string.Equals(scanned, line.Product.Code, StringComparison.OrdinalIgnoreCase)) return;
        if (!await context.ProductBarcodes.AsNoTracking().AnyAsync(
                x => x.ProductId == line.ProductId && x.Value == scanned,
                token))
            throw Conflict("HU_CONTENT_MISMATCH", "Barcode không khớp sản phẩm đã Picking.");
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

    private static string NormalizeIdentity(string? requested, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(requested) ? fallback : requested.Trim().ToUpperInvariant();
        if (value.Length is < 1 or > 64)
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
            SessionCode = session.SessionCode,
            PickingTaskId = session.PickingTaskId,
            PickingTaskCode = session.PickingTask.TaskCode,
            SourceType = session.PickingTask.SourceType,
            SourceId = session.PickingTask.SourceId,
            SourceCode = session.PickingTask.SourceCode,
            WarehouseId = session.WarehouseId,
            WarehouseName = session.Warehouse.Name,
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
                    ProductCode = line.Product.Code,
                    ProductName = line.Product.Name,
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
        HuCode = hu.HuCode,
        Barcode = hu.Barcode,
        Sscc = hu.Sscc,
        WarehouseId = hu.WarehouseId,
        PackingSessionId = hu.PackingSessionId,
        ParentHandlingUnitId = hu.ParentHandlingUnitId,
        ParentHandlingUnitCode = hu.ParentHandlingUnit?.HuCode,
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
            ProductCode = x.Product.Code,
            ProductName = x.Product.Name,
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
