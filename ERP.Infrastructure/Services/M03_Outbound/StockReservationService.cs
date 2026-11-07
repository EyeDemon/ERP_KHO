using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Options;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public partial class StockReservationService(
    ErpKhoDbContext context,
    IInventoryStockRepository stockRepository,
    IUnitOfWork unitOfWork,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser,
    StockReservationOptions options,
    IPickingTaskIntegration? pickingTaskIntegration = null) : IStockReservationService
{
    public async Task<StockReservationDto> CreateAsync(CreateStockReservationDto request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        await (warehouseAuthorization.EnsureWarehouseCommandAccessAsync(request.WarehouseId, cancellationToken)
            ?? warehouseAuthorization.EnsureWarehouseAccessAsync(request.WarehouseId, cancellationToken));
        var exists = await context.Products.AnyAsync(x => x.Id == request.ProductId && x.IsActive, cancellationToken);
        if (!exists) throw new NotFoundException("Không tìm thấy sản phẩm.");
        var expiresAt = request.ExpiresAt ?? DateTime.UtcNow.AddMinutes(options.DefaultExpiryMinutes);
        if (expiresAt <= DateTime.UtcNow) throw new BusinessRuleException("Thời hạn giữ hàng phải ở tương lai.");

        await unitOfWork.BeginTransactionAsync();
        try
        {
            var bundleComponents = await context.ProductBundleComponents.AsNoTracking()
                .Where(x => x.ParentProductId == request.ProductId && x.IsActive)
                .OrderBy(x => x.ComponentProductId)
                .ToListAsync(cancellationToken);

            var hasDirectStock = await context.InventoryStocks.AsNoTracking()
                .AnyAsync(x => x.ProductId == request.ProductId && x.WarehouseId == request.WarehouseId && x.Quantity > 0, cancellationToken);

            if (bundleComponents.Count > 0 && !hasDirectStock)
            {
                StockReservation? primaryReservation = null;
                foreach (var component in bundleComponents)
                {
                    var componentQty = decimal.Round(request.Quantity * component.ComponentQuantity, 4, MidpointRounding.AwayFromZero);
                    var rsv = await ReserveCoreAsync(
                        "Manual",
                        null,
                        $"BUNDLE:{request.ProductId}",
                        request.WarehouseId,
                        component.ComponentProductId,
                        componentQty,
                        currentUser.UserId,
                        expiresAt,
                        cancellationToken);
                    if (primaryReservation is null) primaryReservation = rsv;
                }
                await unitOfWork.CommitTransactionAsync();
                return await GetAsync(primaryReservation!.Id, cancellationToken);
            }

            var reservation = await ReserveCoreAsync("Manual", null, null, request.WarehouseId, request.ProductId, request.Quantity, currentUser.UserId, expiresAt, cancellationToken);
            await unitOfWork.CommitTransactionAsync();
            return await GetAsync(reservation.Id, cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }

    public async Task<StockReservation> ReserveForExportAsync(int exportReceiptId, string exportCode, int warehouseId, int productId, decimal quantity, int userId, CancellationToken cancellationToken = default)
    {
        var existing = await context.StockReservations.FirstOrDefaultAsync(x => x.SourceType == "ExportReceipt" && x.SourceId == exportReceiptId && x.ProductId == productId, cancellationToken);
        if (existing is not null) return existing;
        try
        {
            return await ReserveCoreAsync("ExportReceipt", exportReceiptId, exportCode, warehouseId, productId, quantity, userId, DateTime.UtcNow.AddMinutes(options.DefaultExpiryMinutes), cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyException("Phiếu xuất đã được xử lý hoặc đang được xử lý bởi yêu cầu khác.", exception);
        }
        catch (DbUpdateException exception) when (IsExportReservationSourceConflict(exception))
        {
            throw new ConcurrencyException("Phiếu xuất đã được giữ hàng bởi yêu cầu đồng thời khác.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 })
        {
            throw new DeadlockException("Giao dịch giữ hàng bị deadlock.", exception);
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            throw new DeadlockException("Giao dịch giữ hàng bị deadlock.", exception);
        }
    }

    public async Task<StockReservation> ReserveForSourceAsync(
        string sourceType,
        int sourceId,
        string sourceCode,
        int warehouseId,
        int productId,
        decimal quantity,
        int userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceType) || sourceId <= 0 || string.IsNullOrWhiteSpace(sourceCode) || quantity <= 0)
            throw new BusinessRuleException("Nguồn reservation và số lượng phải hợp lệ.");
        await (warehouseAuthorization.EnsureWarehouseCommandAccessAsync(warehouseId, cancellationToken)
            ?? warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId, cancellationToken));

        var existing = await context.StockReservations.FirstOrDefaultAsync(
            x => x.SourceType == sourceType &&
                 x.SourceId == sourceId &&
                 x.ProductId == productId,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.WarehouseId == warehouseId &&
                existing.Quantity == quantity &&
                existing.Status is StockReservationStatus.Active or
                    StockReservationStatus.PartiallyAllocated or
                    StockReservationStatus.Allocated)
                return existing;
            throw new ConcurrencyException("Nguồn demand đã có reservation khác với yêu cầu hiện tại.");
        }

        try
        {
            return await ReserveCoreAsync(
                sourceType.Trim(),
                sourceId,
                sourceCode.Trim(),
                warehouseId,
                productId,
                quantity,
                userId,
                DateTime.UtcNow.AddMinutes(options.DefaultExpiryMinutes),
                cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConcurrencyException("Nguồn demand đã được giữ hàng bởi yêu cầu đồng thời khác.", exception);
        }
    }

    public async Task<StockReservation> IncreaseSourceReservationAsync(
        string sourceType,
        int sourceId,
        string sourceCode,
        int warehouseId,
        int productId,
        decimal additionalQuantity,
        int userId,
        CancellationToken cancellationToken = default)
    {
        if (additionalQuantity <= 0)
            throw new BusinessRuleException("Số lượng bổ sung reservation phải lớn hơn 0.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);

        var reservation = await context.StockReservations.SingleOrDefaultAsync(
            x => x.SourceType == sourceType &&
                 x.SourceId == sourceId &&
                 x.ProductId == productId,
            cancellationToken);
        if (reservation is null)
            return await ReserveForSourceAsync(
                sourceType,
                sourceId,
                sourceCode,
                warehouseId,
                productId,
                additionalQuantity,
                userId,
                cancellationToken);

        if (reservation.WarehouseId != warehouseId)
            throw new ConcurrencyException("Reservation nguồn không cùng kho.");
        if (string.Equals(reservation.SourceType, "Manual", StringComparison.OrdinalIgnoreCase) && reservation.ExpiresAt <= DateTime.UtcNow)
            throw new ConcurrencyException("Reservation nguồn đã hết hạn.");
        if (reservation.Status is not StockReservationStatus.Active and
            not StockReservationStatus.PartiallyAllocated and
            not StockReservationStatus.Allocated)
            throw new ConcurrencyException("Reservation nguồn không còn có thể tăng số lượng.");

        if (!await stockRepository.TryReserveAsync(productId, warehouseId, additionalQuantity, cancellationToken))
            throw new ConcurrencyException("Không đủ tồn khả dụng để bổ sung reservation.", "INSUFFICIENT_AVAILABLE_STOCK");

        reservation.Quantity += additionalQuantity;
        reservation.ExpiresAt = DateTime.UtcNow.AddMinutes(options.DefaultExpiryMinutes);
        UpdateActiveReservationStatus(reservation);
        context.AuditLogs.Add(Audit(
            userId,
            "StockReservation.Increased",
            reservation,
            $"AdditionalQuantity: {additionalQuantity}; Source: {sourceType}/{sourceId}"));
        await context.SaveChangesAsync(cancellationToken);
        return reservation;
    }

    private static bool IsExportReservationSourceConflict(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 } sql &&
        (sql.Message.Contains("IX_StockReservations_SourceType_SourceId_ProductId", StringComparison.OrdinalIgnoreCase) ||
         sql.Message.Contains("UX_StockReservations_TenantId_SourceType_SourceId_ProductId", StringComparison.OrdinalIgnoreCase));

    public async Task<StockReservation> GetExportReservationAsync(int exportReceiptId, int warehouseId, int productId, CancellationToken cancellationToken = default)
    {
        return await context.StockReservations.FirstOrDefaultAsync(
                   x => x.SourceType == "ExportReceipt" &&
                        x.SourceId == exportReceiptId &&
                        x.WarehouseId == warehouseId &&
                        x.ProductId == productId,
                   cancellationToken)
               ?? throw new ConcurrencyException("Không tìm thấy reservation đã được tạo khi duyệt phiếu xuất.");
    }

    private async Task<StockReservation> ReserveCoreAsync(string sourceType, int? sourceId, string? sourceCode, int warehouseId, int productId, decimal quantity, int userId, DateTime expiresAt, CancellationToken cancellationToken)
    {
        await context.AcquireInventoryAllocationLocksAsync([(warehouseId, productId)], cancellationToken);
        var stale = await context.StockReservations
            .Where(x => x.ProductId == productId &&
                        x.WarehouseId == warehouseId &&
                        x.SourceType == "Manual" &&
                        x.ExpiresAt <= DateTime.UtcNow &&
                        (x.Status == StockReservationStatus.Active ||
                         x.Status == StockReservationStatus.PartiallyConsumed ||
                         x.Status == StockReservationStatus.PartiallyAllocated ||
                         x.Status == StockReservationStatus.Allocated) &&
                        !context.PickingTaskLines.Any(line =>
                            line.Allocation.ReservationId == x.Id &&
                            line.PickingTask.Status != PickingTaskStatus.Cancelled))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        foreach (var item in stale) await ReleaseCoreAsync(item, null, userId, "Expired before availability check", StockReservationStatus.Expired, cancellationToken);
        if (!await stockRepository.TryReserveAsync(productId, warehouseId, quantity, cancellationToken))
            throw new ConcurrencyException("Không đủ tồn khả dụng để giữ hàng.", "INSUFFICIENT_AVAILABLE_STOCK");

        var productUom = await context.Products.AsNoTracking()
            .Where(x => x.Id == productId)
            .Select(x => new
            {
                x.UnitId,
                UnitCode = x.Unit != null ? x.Unit.Code : "THÙNG",
                DecimalPlaces = x.Unit != null ? x.Unit.DecimalPlaces : 0
            })
            .FirstOrDefaultAsync(cancellationToken);

        var reservation = new StockReservation
        {
            ReservationCode = $"RSV-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..34].ToUpperInvariant(),
            ProductId = productId,
            WarehouseId = warehouseId,
            Quantity = quantity,
            BaseUomIdSnapshot = productUom?.UnitId ?? 1,
            BaseUomCodeSnapshot = string.IsNullOrWhiteSpace(productUom?.UnitCode) ? "THÙNG" : productUom.UnitCode.Trim(),
            BaseUomDecimalPlacesSnapshot = productUom?.DecimalPlaces ?? 0,
            Status = StockReservationStatus.Active,
            SourceType = sourceType,
            SourceId = sourceId,
            SourceCode = sourceCode,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId,
            ExpiresAt = expiresAt
        };
        context.StockReservations.Add(reservation);
        await context.SaveChangesAsync(cancellationToken);
        context.AuditLogs.Add(Audit(userId, "StockReservation.Created", reservation, $"Quantity: {quantity}, Source: {sourceType}/{sourceId}"));
        return reservation;
    }
}
