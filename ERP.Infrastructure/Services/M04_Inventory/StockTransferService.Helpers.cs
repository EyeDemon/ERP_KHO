using System.Data;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class StockTransferService
{
    private async Task ValidateDraftAsync(CreateStockTransferDto request, CancellationToken cancellationToken)
    {
        if (request.SourceWarehouseId <= 0 || request.DestinationWarehouseId <= 0 || request.SourceWarehouseId == request.DestinationWarehouseId)
            throw new BusinessRuleException("Kho nguồn và kho đích phải hợp lệ và khác nhau.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(request.SourceWarehouseId, cancellationToken);
        var warehouses = await context.Warehouses.AsNoTracking()
            .Where(x => x.Id == request.SourceWarehouseId || x.Id == request.DestinationWarehouseId)
            .Select(x => new { x.Id, x.IsActive })
            .ToListAsync(cancellationToken);
        var sourceWarehouse = warehouses.SingleOrDefault(x => x.Id == request.SourceWarehouseId)
            ?? throw new BusinessRuleException("Kho nguồn không tồn tại.");
        var destinationWarehouse = warehouses.SingleOrDefault(x => x.Id == request.DestinationWarehouseId)
            ?? throw new BusinessRuleException("Kho đích không tồn tại.");
        if (!sourceWarehouse.IsActive)
            throw new BusinessRuleException("Kho nguồn đang ngừng hoạt động.");
        if (!destinationWarehouse.IsActive)
            throw new BusinessRuleException("Kho đích đang ngừng hoạt động.");
        if (request.Details == null || request.Details.Count == 0 ||
            request.Details.Any(x => x == null || x.ProductId <= 0 ||
                x.Quantity <= 0 || x.Quantity >= 100_000_000_000_000m ||
                decimal.Round(x.Quantity, 4) != x.Quantity))
            throw new BusinessRuleException("Phiếu phải có sản phẩm với số lượng dương và tối đa 4 chữ số thập phân.");
        if (request.Note?.Length > 500 || request.Details.Any(x => x.Note?.Length > 500))
            throw new BusinessRuleException("Ghi chú phiếu hoặc từng sản phẩm không được quá 500 ký tự.");
        if (request.Details.GroupBy(x => x.ProductId).Any(x => x.Count() > 1)) throw new BusinessRuleException("Một sản phẩm không được lặp nhiều dòng.");
        var ids = request.Details.Select(x => x.ProductId).Distinct().ToList();
        var products = await context.Products.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.IsActive, x.TrackingType, DecimalPlaces = x.Unit != null ? (int?)x.Unit.DecimalPlaces : null })
            .ToListAsync(cancellationToken);
        if (products.Count != ids.Count) throw new BusinessRuleException("Có sản phẩm không tồn tại.");
        if (products.Any(x => !x.IsActive))
            throw new BusinessRuleException("Không thể điều chuyển sản phẩm đã ngừng hoạt động.");
        if (products.Any(x => x.TrackingType != ProductTrackingType.None))
            throw new BusinessRuleException("Phiếu điều chuyển kho chưa hỗ trợ sản phẩm quản lý theo Lô/Serial.");
        foreach (var line in request.Details)
        {
            var product = products.Single(x => x.Id == line.ProductId);
            var decimalPlaces = product.DecimalPlaces ?? 4;
            if (decimalPlaces is >= 0 and <= 4 && decimal.Round(line.Quantity, decimalPlaces) != line.Quantity)
                throw new BusinessRuleException($"Số lượng sản phẩm {product.Code} vượt quá độ chính xác đơn vị tính ({decimalPlaces} chữ số thập phân).");
        }
    }

    private static void ValidateReceipt(StockTransfer entity, ReceiveStockTransferDto request)
    {
        if (request.Details is null || request.Details.Any(x => x is null) ||
            request.Details.Count != entity.Details.Count ||
            request.Details.Select(x => x.ProductId).Distinct().Count() != entity.Details.Count)
            throw new BusinessRuleException("Phải khai báo kết quả nhận cho từng sản phẩm đúng một lần.");
        foreach (var input in request.Details)
        {
            var line = entity.Details.SingleOrDefault(x => x.ProductId == input.ProductId) ?? throw new BusinessRuleException("Sản phẩm không thuộc phiếu.");
            if (input.Note?.Length > 500)
                throw new BusinessRuleException("Ghi chú nhận hàng không được quá 500 ký tự.");
            if (input.ReceivedQuantity < 0 || input.MissingQuantity < 0 || input.DamagedQuantity < 0 ||
                decimal.Round(input.ReceivedQuantity, 4) != input.ReceivedQuantity ||
                decimal.Round(input.MissingQuantity, 4) != input.MissingQuantity ||
                decimal.Round(input.DamagedQuantity, 4) != input.DamagedQuantity)
                throw new BusinessRuleException("Số lượng nhận, thiếu và hư hỏng phải không âm và có tối đa 4 chữ số thập phân.");

            // Bound each part before summing to avoid overflow from malformed
            // decimal payloads; the posted quantity uses decimal(18,4).
            if (input.ReceivedQuantity > line.DispatchedQuantity ||
                input.MissingQuantity > line.DispatchedQuantity ||
                input.DamagedQuantity > line.DispatchedQuantity)
                throw new BusinessRuleException("Số lượng thực nhận, thiếu hoặc hỏng không được vượt quá số đã xuất.");

            // Receiving is a terminal movement for each line, not a partial
            // receiving workflow. Every dispatched unit must be accounted for
            // as received, missing or damaged before the document leaves transit.
            if (input.ReceivedQuantity + input.MissingQuantity + input.DamagedQuantity != line.DispatchedQuantity)
                throw new BusinessRuleException("Tổng thực nhận, thiếu và hỏng phải đúng bằng số lượng đã xuất của từng sản phẩm.");
        }
    }

    private async Task<int> IncreaseStockAsync(
        int productId,
        int warehouseId,
        decimal quantity,
        DateTime now,
        CancellationToken cancellationToken,
        InventoryStatus status = InventoryStatus.Available)
    {
        var locationId = status == InventoryStatus.Damaged
            ? await context.WarehouseLocations
                .Where(x => x.WarehouseId == warehouseId && x.IsActive && !x.IsBlocked && !x.IsPickable &&
                            (x.LocationType == WarehouseLocationType.Damaged || x.Code == "DAMAGED" || x.LocationType == WarehouseLocationType.Receiving || x.Code == "RECEIVING"))
                .OrderBy(x => x.LocationType == WarehouseLocationType.Damaged || x.Code == "DAMAGED" ? 0 : 1)
                .ThenBy(x => x.Id)
                .Select(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken)
            : await context.WarehouseLocations
                .Where(x => x.WarehouseId == warehouseId && x.Code == "LEGACY" && x.IsSystemManaged && x.IsActive && !x.IsBlocked && x.IsPickable)
                .Select(x => x.Id)
                .SingleOrDefaultAsync(cancellationToken);

        if (locationId == 0 && status == InventoryStatus.Damaged)
        {
            var damagedLocation = new WarehouseLocation
            {
                WarehouseId = warehouseId,
                Code = "DAMAGED",
                Name = "Vị trí cách ly hàng hỏng",
                LocationType = WarehouseLocationType.Damaged,
                IsActive = true,
                IsPickable = false,
                IsReceivable = false,
                IsSystemManaged = true
            };
            context.WarehouseLocations.Add(damagedLocation);
            await context.SaveChangesAsync(cancellationToken);
            locationId = damagedLocation.Id;
        }
        if (locationId == 0) throw new BusinessRuleException("Kho đích chưa có vị trí tương thích để nhận điều chuyển.");
        if (context.Database.IsRelational() && context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Nhận hoặc hoàn trả điều chuyển phải nằm trong giao dịch SQL.");

        if (!context.Database.IsSqlServer())
        {
            var existing = await context.InventoryStocks.FirstOrDefaultAsync(
                x => x.ProductId == productId &&
                     x.WarehouseId == warehouseId &&
                     x.LocationId == locationId &&
                     x.Status == status &&
                     x.LotId == null &&
                     x.SerialId == null,
                cancellationToken);
            if (existing is null)
            {
                context.InventoryStocks.Add(new InventoryStock
                {
                    ProductId = productId,
                    WarehouseId = warehouseId,
                    LocationId = locationId,
                    Status = status,
                    Quantity = quantity,
                    ReservedQuantity = 0,
                    LastUpdated = now
                });
            }
            else
            {
                existing.Quantity += quantity;
                existing.LastUpdated = now;
            }
            return locationId;
        }

        // Serialize just this canonical stock bucket across different transfer
        // documents. Without an application lock, two SERIALIZABLE transactions
        // can both take compatible range-read locks on a missing destination row
        // and then deadlock when MERGE converts them to write locks (SQL 1205).
        // The lock is held until the caller commits/rolls back ledger + audit.
        await context.AcquireInventoryAllocationLocksAsync([(warehouseId, productId)], cancellationToken);
        var tenantId = context.CurrentTenantId;
        var statusInt = (int)status;
        var bucketLock = $"ERP:TransferStock:{tenantId}:{productId}:{warehouseId}:{locationId}:{statusInt}";
        await context.Database.ExecuteSqlInterpolatedAsync($@"
DECLARE @lockResult int;
EXEC @lockResult = sys.sp_getapplock
    @Resource = {bucketLock},
    @LockMode = 'Exclusive',
    @LockOwner = 'Transaction',
    @LockTimeout = 45000;
IF @lockResult < 0
BEGIN
    -- Preserve the SQL lock return code (-1 timeout, -3 deadlock victim,
    -- etc.) so an operator can diagnose contention without assuming that
    -- every lock failure is a deadlock.
    DECLARE @lockMessage nvarchar(2048) =
        CONCAT(N'Không thể khóa ô tồn kho điều chuyển. Mã khóa SQL: ', @lockResult);
    THROW 51032, @lockMessage, 1;
END;", cancellationToken);

        await context.Database.ExecuteSqlInterpolatedAsync($@"
MERGE INTO InventoryStocks WITH (HOLDLOCK) AS target
USING (SELECT {tenantId} AS TenantId, {productId} AS ProductId, {warehouseId} AS WarehouseId, {locationId} AS LocationId, {quantity} AS Quantity, {now} AS LastUpdated, {statusInt} AS Status) AS source
ON target.TenantId = source.TenantId AND target.ProductId = source.ProductId AND target.WarehouseId = source.WarehouseId AND target.Status = source.Status AND target.LocationId = source.LocationId
   AND target.LotId IS NULL AND target.SerialId IS NULL
WHEN MATCHED THEN
    UPDATE SET Quantity = target.Quantity + source.Quantity, LastUpdated = source.LastUpdated
WHEN NOT MATCHED THEN
    INSERT (TenantId, WarehouseId, ProductId, LocationId, LotId, SerialId, HandlingUnitId, Status, Quantity, ReservedQuantity, AllocatedQuantity, LockedQuantity, LastUpdated, GrainHash)
    VALUES (source.TenantId, source.WarehouseId, source.ProductId, source.LocationId, NULL, NULL, NULL, source.Status, source.Quantity, 0, 0, 0, source.LastUpdated, CAST(HASHBYTES('SHA2_256', CONVERT(varchar(256), CONCAT(source.TenantId, '|', source.WarehouseId, '|', ISNULL(source.LocationId, -1), '|', source.ProductId, '|-1|-1|-1|', source.Status, '|-1'))) AS varbinary(32)));", cancellationToken);
        return locationId;
    }

    private async Task TransitionAsync(StockTransfer entity, StockTransferStatus from, StockTransferStatus to, string action, CancellationToken cancellationToken)
    {
        var id = entity.Id;
        var now = DateTime.UtcNow;
        // Recheck the exact warehouse pair observed during authorization.
        // A parallel draft edit may change either warehouse before this CAS
        // executes; stale scope must never approve/complete/cancel that version.
        if (context.Database.IsSqlServer())
        {
            var target = context.StockTransfers.Where(x =>
                x.Id == id && x.Status == from &&
                x.SourceWarehouseId == entity.SourceWarehouseId &&
                x.DestinationWarehouseId == entity.DestinationWarehouseId);
            var affected = action switch
            {
                "StockTransfer.Approved" => await target.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, to).SetProperty(x => x.ApprovedBy, currentUser.UserId).SetProperty(x => x.ApprovedAt, now), cancellationToken),
                "StockTransfer.Completed" => await target.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, to).SetProperty(x => x.CompletedBy, currentUser.UserId).SetProperty(x => x.CompletedAt, now), cancellationToken),
                "StockTransfer.Cancelled" => await target.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, to).SetProperty(x => x.CancelledBy, currentUser.UserId).SetProperty(x => x.CancelledAt, now).SetProperty(x => x.ReverseOfTransferId, (int?)null), cancellationToken),
                _ => throw new InvalidOperationException("Unsupported stock-transfer transition.")
            };
            if (affected != 1) throw Conflict("Trạng thái phiếu đã thay đổi hoặc thao tác không hợp lệ.");
        }
        else
        {
            if (entity.Status != from)
                throw Conflict("Trạng thái phiếu đã thay đổi hoặc thao tác không hợp lệ.");
            entity.Status = to;
            switch (action)
            {
                case "StockTransfer.Approved":
                    entity.ApprovedBy = currentUser.UserId;
                    entity.ApprovedAt = now;
                    break;
                case "StockTransfer.Completed":
                    entity.CompletedBy = currentUser.UserId;
                    entity.CompletedAt = now;
                    break;
                case "StockTransfer.Cancelled":
                    entity.CancelledBy = currentUser.UserId;
                    entity.CancelledAt = now;
                    entity.ReverseOfTransferId = null;
                    break;
                default:
                    throw new InvalidOperationException("Unsupported stock-transfer transition.");
            }
            await context.SaveChangesAsync(cancellationToken);
        }
        await AuditAsync(entity, action, from, to, cancellationToken, now);
        if (context.Database.CurrentTransaction is null) context.ChangeTracker.Clear();
    }

    // Lock the same header row as Draft edits before reading posting lines.
    // Caller owns a transaction to keep ledger and document version consistent.
    private async Task<StockTransfer> GetLockedScopedAsync(int id, CancellationToken cancellationToken)
    {
        if (context.Database.IsRelational() && context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Stock transfer posting requires a database transaction.");
        var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        var tenantId = context.CurrentTenantId;
        var locked = context.Database.IsSqlServer()
            ? context.StockTransfers.FromSqlInterpolated(
                $"SELECT * FROM dbo.StockTransfers WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND Id = {id}")
            : context.StockTransfers.AsQueryable();
        return await locked.Include(x => x.SourceWarehouse).Include(x => x.DestinationWarehouse)
            .Include(x => x.Details).ThenInclude(x => x.Product)
            .SingleOrDefaultAsync(x => x.Id == id &&
                (allowed.Contains(x.SourceWarehouseId) || allowed.Contains(x.DestinationWarehouseId)),
                cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy phiếu điều chuyển.");
    }

    private async Task<StockTransfer> GetScopedAsync(int id, CancellationToken cancellationToken)
    {
        var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        var entity = await context.StockTransfers.Include(x => x.SourceWarehouse).Include(x => x.DestinationWarehouse)
            .Include(x => x.Details).ThenInclude(x => x.Product)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy phiếu điều chuyển.");
        if (!allowed.Contains(entity.SourceWarehouseId) && !allowed.Contains(entity.DestinationWarehouseId))
            throw new ForbiddenException("Bạn không có quyền truy cập kho được yêu cầu.");
        return entity;
    }

    private void EnsureWriteRole()
    {
        if (!currentUser.IsGlobalAdmin && currentUser.Role is not ("Manager" or "WarehouseStaff")) throw new ForbiddenException("Vai trò hiện tại chỉ được xem.");
    }
    private void EnsureApproveRole()
    {
        if (!currentUser.IsGlobalAdmin && currentUser.Role != "Manager") throw new ForbiddenException("Bạn không có quyền duyệt phiếu.");
    }

    private InventoryTransaction Transaction(
        StockTransfer transfer,
        StockTransferDetail line,
        int warehouseId,
        TransactionType type,
        decimal quantity,
        DateTime now,
        int? locationId = null,
        InventoryStatus inventoryStatus = InventoryStatus.Available) => new()
        {
            ProductId = line.ProductId,
            WarehouseId = warehouseId,
            LocationId = locationId,
            InventoryStatus = inventoryStatus,
            TransactionType = type,
            Quantity = quantity,
            ReferenceId = transfer.Id,
            ReferenceType = "StockTransfer",
            TransactionDate = now,
            CreatedBy = currentUser.UserId,
            Note = line.Note
        };
    private AuditLog Audit(StockTransfer entity, string action, StockTransferStatus from, StockTransferStatus to, DateTime now) => new()
    {
        UserId = currentUser.UserId, Action = action, EntityName = "StockTransfer", EntityId = entity.Id,
        SourceWarehouseId = entity.SourceWarehouseId, DestinationWarehouseId = entity.DestinationWarehouseId,
        OldValues = $"Status: {from}", NewValues = $"Status: {to}", Result = "Success",
        Severity = "Information", Timestamp = now
    };
    private async Task AuditAsync(StockTransfer entity, string action, StockTransferStatus from, StockTransferStatus to, CancellationToken cancellationToken, DateTime? now = null)
    { context.AuditLogs.Add(Audit(entity, action, from, to, now ?? DateTime.UtcNow)); await context.SaveChangesAsync(cancellationToken); }
    private static ConcurrencyException Conflict(string message) => new(message);

    private static StockTransferDto MapSummary(StockTransfer x) => new()
    {
        Id = x.Id,
        Code = x.Code.Trim(),
        SourceWarehouseId = x.SourceWarehouseId,
        SourceWarehouseName = x.SourceWarehouse.Name.Trim(),
        DestinationWarehouseId = x.DestinationWarehouseId,
        DestinationWarehouseName = x.DestinationWarehouse.Name.Trim(),
        Status = x.Status,
        DraftRevision = x.DraftRevision,
        Note = x.Note?.Trim(),
        InternalTransferOrderNo = x.InternalTransferOrderNo?.Trim(),
        TransporterName = x.TransporterName?.Trim(),
        VehiclePlateNumber = x.VehiclePlateNumber?.Trim(),
        ElectronicPxkkvcnbNo = x.ElectronicPxkkvcnbNo?.Trim(),
        CreatedBy = x.CreatedBy,
        CreatedAt = x.CreatedAt,
        ApprovedAt = x.ApprovedAt,
        DispatchedAt = x.DispatchedAt,
        ReceivedAt = x.ReceivedAt,
        CompletedAt = x.CompletedAt,
        CancelledAt = x.CancelledAt
    };
    private static StockTransferDto Map(StockTransfer x) { var dto = MapSummary(x); dto.Details = x.Details.OrderBy(d => d.ProductId).Select(d => new StockTransferDetailDto { ProductId = d.ProductId, ProductCode = d.Product.Code.Trim(), ProductName = d.Product.Name.Trim(), RequestedQuantity = d.RequestedQuantity, DispatchedQuantity = d.DispatchedQuantity, ReceivedQuantity = d.ReceivedQuantity, MissingQuantity = d.MissingQuantity, DamagedQuantity = d.DamagedQuantity, InTransitQuantity = x.Status == StockTransferStatus.InTransit ? d.DispatchedQuantity - d.ReceivedQuantity - d.MissingQuantity - d.DamagedQuantity : 0, Note = d.Note?.Trim() }).ToList(); return dto; }
}
