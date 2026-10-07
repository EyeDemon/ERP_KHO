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

public sealed class InventoryMovementService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser,
    IInventoryLockEvaluator locks) : IInventoryMovementService
{
    public async Task<InventoryMoveResultDto> MoveAsync(
        CreateInventoryMoveDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.InventoryStockId <= 0 || request.DestinationLocationId <= 0 || request.Quantity <= 0)
            throw new BusinessRuleException("InventoryStockId, vị trí đích và số lượng phải hợp lệ.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BusinessRuleException("Lý do di chuyển tồn kho là bắt buộc.");

        var own = context.Database.CurrentTransaction is null;
        await using var tx = own
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var source = await context.InventoryStocks
                .Include(x => x.Product).ThenInclude(x => x.Unit)
                .Include(x => x.Location)
                .SingleOrDefaultAsync(x => x.Id == request.InventoryStockId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy inventory bucket nguồn.");
            await warehouseAuthorization.EnsureWarehouseAccessAsync(source.WarehouseId, cancellationToken);

            if (!source.LocationId.HasValue)
                throw Conflict("INV_BUCKET_CONFLICT", "Inventory bucket nguồn chưa có Location hợp lệ.");
            if (source.LocationId.Value == request.DestinationLocationId)
                throw Validation("TRANSFER_SOURCE_EQUALS_DESTINATION", "Vị trí nguồn và đích phải khác nhau.");

            var destinationLocation = await context.WarehouseLocations
                .SingleOrDefaultAsync(x => x.Id == request.DestinationLocationId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy vị trí đích.");
            if (destinationLocation.WarehouseId != source.WarehouseId)
                throw new NotFoundException("Không tìm thấy vị trí đích.");
            if (!destinationLocation.IsActive || destinationLocation.IsBlocked || destinationLocation.IsSystemManaged)
                throw Conflict("INV_BUCKET_CONFLICT", "Vị trí đích không cho phép internal movement.");

            ValidateQuantityPrecision(source, request.Quantity);
            if (source.SerialId.HasValue && request.Quantity != 1m)
                throw Validation("SERIAL_QUANTITY_INVALID", "Inventory theo Serial chỉ được di chuyển đúng 1 base unit.");
            if (source.Quantity - source.ReservedQuantity < request.Quantity)
                throw Conflict("INV_INSUFFICIENT_AVAILABLE", "Không thể di chuyển phần tồn đang reserved/allocation.");

            await locks.EnsureBucketUnlockedAsync(
                source.WarehouseId,
                source.LocationId,
                source.ProductId,
                source.Status,
                source.LotId,
                source.SerialId,
                cancellationToken);
            await locks.EnsureBucketUnlockedAsync(
                source.WarehouseId,
                destinationLocation.Id,
                source.ProductId,
                source.Status,
                source.LotId,
                source.SerialId,
                cancellationToken);

            await EnsureDestinationCapacityAsync(destinationLocation, source.Product, request.Quantity, cancellationToken);

            var destination = await context.InventoryStocks.SingleOrDefaultAsync(
                x => x.ProductId == source.ProductId &&
                     x.WarehouseId == source.WarehouseId &&
                     x.LocationId == destinationLocation.Id &&
                     x.Status == source.Status &&
                     x.LotId == source.LotId &&
                     x.SerialId == source.SerialId,
                cancellationToken);

            source.Quantity -= request.Quantity;
            source.LastUpdated = DateTime.UtcNow;
            if (destination is null)
            {
                destination = new InventoryStock
                {
                    ProductId = source.ProductId,
                    WarehouseId = source.WarehouseId,
                    LocationId = destinationLocation.Id,
                    LotId = source.LotId,
                    SerialId = source.SerialId,
                    Status = source.Status,
                    Quantity = request.Quantity,
                    ReservedQuantity = 0,
                    LastUpdated = DateTime.UtcNow
                };
                context.InventoryStocks.Add(destination);
            }
            else
            {
                destination.Quantity += request.Quantity;
                destination.LastUpdated = DateTime.UtcNow;
            }

            var movement = new InventoryLocationMovement
            {
                WarehouseId = source.WarehouseId,
                ProductId = source.ProductId,
                InventoryStatus = source.Status,
                LotId = source.LotId,
                SerialId = source.SerialId,
                FromLocationId = source.LocationId.Value,
                ToLocationId = destinationLocation.Id,
                BaseQuantity = request.Quantity,
                EnteredQuantity = request.Quantity,
                EnteredUnitCode = source.Product.Unit?.Code ?? string.Empty,
                ReferenceType = "InventoryMove",
                CreatedBy = currentUser.UserId,
                CreatedAt = DateTime.UtcNow
            };
            context.InventoryLocationMovements.Add(movement);
            await context.SaveChangesAsync(cancellationToken);

            movement.ReferenceId = movement.Id;
            var ledger = new InventoryTransaction
            {
                ProductId = source.ProductId,
                WarehouseId = source.WarehouseId,
                LocationId = source.LocationId,
                FromLocationId = source.LocationId,
                ToLocationId = destinationLocation.Id,
                LotId = source.LotId,
                SerialId = source.SerialId,
                InventoryStatus = source.Status,
                TransactionType = TransactionType.Move,
                Quantity = request.Quantity,
                ReferenceId = movement.Id,
                ReferenceType = "InventoryMove",
                TransactionDate = DateTime.UtcNow,
                CreatedBy = currentUser.UserId,
                Note = request.Reason.Trim()
            };
            context.InventoryTransactions.Add(ledger);
            context.AuditLogs.Add(new AuditLog
            {
                UserId = currentUser.UserId,
                Action = "Inventory.Move",
                EntityName = "InventoryLocationMovement",
                EntityId = movement.Id,
                WarehouseId = source.WarehouseId,
                Timestamp = DateTime.UtcNow,
                OldValues = $"StockId: {source.Id}; LocationId: {source.LocationId}; QuantityBefore: {source.Quantity + request.Quantity}",
                NewValues = $"DestinationStockId: {destination.Id}; ToLocationId: {destinationLocation.Id}; Quantity: {request.Quantity}; Status: {source.Status}; LotId: {source.LotId}; SerialId: {source.SerialId}",
                Reason = request.Reason.Trim(),
                Result = "Success",
                Severity = "Information"
            });
            await context.SaveChangesAsync(cancellationToken);
            if (tx is not null) await tx.CommitAsync(cancellationToken);

            return new InventoryMoveResultDto
            {
                MovementId = movement.Id,
                TransactionId = ledger.Id,
                SourceStockId = source.Id,
                DestinationStockId = destination.Id,
                WarehouseId = source.WarehouseId,
                ProductId = source.ProductId,
                FromLocationId = source.LocationId.Value,
                ToLocationId = destinationLocation.Id,
                InventoryStatus = source.Status.ToString(),
                LotId = source.LotId,
                SerialId = source.SerialId,
                Quantity = request.Quantity
            };
        }
        catch (DbUpdateConcurrencyException ex)
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
            throw new ConcurrencyException("Inventory bucket đã thay đổi trong lúc di chuyển.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
            throw Conflict("INV_BUCKET_CONFLICT", "Inventory bucket đích vừa thay đổi đồng thời.");
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task EnsureDestinationCapacityAsync(
        WarehouseLocation location,
        Product product,
        decimal quantity,
        CancellationToken token)
    {
        if (location.StorageClass is not null &&
            !string.Equals(location.StorageClass, product.StorageClass, StringComparison.Ordinal))
            throw Conflict("INV_BUCKET_CONFLICT", "Storage Class của vị trí đích không phù hợp Product.");

        var rows = await context.InventoryStocks.AsNoTracking()
            .Where(x => x.LocationId == location.Id && x.Quantity > 0)
            .Join(context.Products.AsNoTracking(), s => s.ProductId, p => p.Id,
                (s, p) => new { s.Quantity, p.UnitWeightKg, p.UnitVolumeM3, p.UnitPalletEquivalent })
            .ToListAsync(token);

        if (location.MaxWeightKg.HasValue)
        {
            if (!product.UnitWeightKg.HasValue || rows.Any(x => !x.UnitWeightKg.HasValue))
                throw Conflict("INV_BUCKET_CONFLICT", "Không đủ dữ liệu UnitWeightKg để xác minh capacity.");
            if (rows.Sum(x => x.Quantity * x.UnitWeightKg!.Value) + quantity * product.UnitWeightKg.Value > location.MaxWeightKg.Value)
                throw Conflict("INV_BUCKET_CONFLICT", "Internal move vượt capacity trọng lượng của vị trí đích.");
        }
        if (location.MaxVolumeM3.HasValue)
        {
            if (!product.UnitVolumeM3.HasValue || rows.Any(x => !x.UnitVolumeM3.HasValue))
                throw Conflict("INV_BUCKET_CONFLICT", "Không đủ dữ liệu UnitVolumeM3 để xác minh capacity.");
            if (rows.Sum(x => x.Quantity * x.UnitVolumeM3!.Value) + quantity * product.UnitVolumeM3.Value > location.MaxVolumeM3.Value)
                throw Conflict("INV_BUCKET_CONFLICT", "Internal move vượt capacity thể tích của vị trí đích.");
        }
        if (location.MaxPalletEquivalent.HasValue)
        {
            if (!product.UnitPalletEquivalent.HasValue || rows.Any(x => !x.UnitPalletEquivalent.HasValue))
                throw Conflict("INV_BUCKET_CONFLICT", "Không đủ dữ liệu pallet-equivalent để xác minh capacity.");
            if (rows.Sum(x => x.Quantity * x.UnitPalletEquivalent!.Value) + quantity * product.UnitPalletEquivalent.Value > location.MaxPalletEquivalent.Value)
                throw Conflict("INV_BUCKET_CONFLICT", "Internal move vượt capacity pallet-equivalent của vị trí đích.");
        }
    }

    private static void ValidateQuantityPrecision(InventoryStock source, decimal quantity)
    {
        var places = source.Product.Unit?.DecimalPlaces ?? 4;
        if (decimal.Round(quantity, places) != quantity)
            throw new BusinessRuleException($"Số lượng vượt quá độ chính xác Base UOM cho phép ({places} chữ số thập phân).");
    }

    private static BusinessRuleException Conflict(string code, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["HttpStatusCode"] = 409;
        ex.Data["ErrorCode"] = code;
        return ex;
    }

    private static BusinessRuleException Validation(string code, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["HttpStatusCode"] = 422;
        ex.Data["ErrorCode"] = code;
        return ex;
    }
}
