using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ReceiptInventoryIdentityService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization) : IReceiptInventoryIdentityService
{
    public async Task<IReadOnlyList<ImportReceiptInventoryIdentityDto>> SetAsync(
        int receiptId,
        SetImportReceiptInventoryIdentitiesDto request,
        int actorId,
        CancellationToken cancellationToken = default)
    {
        var ownTransaction = context.Database.CurrentTransaction is null;
        await using var tx = ownTransaction
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var receipt = await LoadReceiptAsync(receiptId, cancellationToken);
            await warehouseAuthorization.EnsureWarehouseAccessAsync(receipt.WarehouseId, cancellationToken);
            if (receipt.Status is not ReceiptStatus.Received and not ReceiptStatus.QcCompleted)
                throw Conflict("INVALID_STATE_TRANSITION", "Lot/Serial chỉ được cấu hình sau Receive/QC và trước READY_TO_POST.");

            var normalized = NormalizeAndValidate(receipt, request.Lines);
            await context.ImportReceiptInventoryIdentities
                .Where(x => x.ImportReceiptDetail.ImportReceiptId == receiptId)
                .ExecuteDeleteAsync(cancellationToken);

            foreach (var line in normalized)
            {
                context.ImportReceiptInventoryIdentities.Add(new ImportReceiptInventoryIdentity
                {
                    ImportReceiptDetailId = line.LineId,
                    ProductId = line.ProductId,
                    TargetStatus = line.TargetStatus,
                    BaseQuantity = line.BaseQuantity,
                    LotNumber = line.LotNumber,
                    ManufactureDate = line.ManufactureDate,
                    ExpiryDate = line.ExpiryDate,
                    SerialNumber = line.SerialNumber,
                    CreatedBy = actorId,
                    CreatedAt = DateTime.UtcNow
                });
            }

            context.AuditLogs.Add(new AuditLog
            {
                UserId = actorId,
                Action = "ImportReceipt.InventoryIdentityConfigured",
                EntityName = "ImportReceipt",
                EntityId = receipt.Id,
                WarehouseId = receipt.WarehouseId,
                Timestamp = DateTime.UtcNow,
                NewValues = $"IdentityRows: {normalized.Count}; TrackedLines: {receipt.Details.Count(x => x.Product.TrackingType != ProductTrackingType.None)}",
                Result = "Success",
                Severity = "Information"
            });

            await context.SaveChangesAsync(cancellationToken);
            if (tx is not null) await tx.CommitAsync(cancellationToken);
            return await GetAsync(receiptId, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
            throw Conflict("SERIAL_DUPLICATE", "Serial đã được khai báo ở receipt khác hoặc bị trùng trong request.");
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<ImportReceiptInventoryIdentityDto>> GetAsync(
        int receiptId,
        CancellationToken cancellationToken = default)
    {
        var receipt = await context.ImportReceipts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == receiptId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy phiếu nhập.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(receipt.WarehouseId, cancellationToken);

        return await context.ImportReceiptInventoryIdentities.AsNoTracking()
            .Where(x => x.ImportReceiptDetail.ImportReceiptId == receiptId)
            .OrderBy(x => x.ImportReceiptDetailId)
            .ThenBy(x => x.TargetStatus)
            .ThenBy(x => x.LotNumber)
            .ThenBy(x => x.SerialNumber)
            .Select(x => new ImportReceiptInventoryIdentityDto
            {
                Id = x.Id,
                LineId = x.ImportReceiptDetailId,
                ProductId = x.ProductId,
                TargetStatus = x.TargetStatus.ToString(),
                BaseQuantity = x.BaseQuantity,
                LotNumber = x.LotNumber,
                ManufactureDate = x.ManufactureDate,
                ExpiryDate = x.ExpiryDate,
                SerialNumber = x.SerialNumber
            })
            .ToListAsync(cancellationToken);
    }

    public async Task ValidateReadyToPostAsync(int receiptId, CancellationToken cancellationToken = default)
    {
        var receipt = await LoadReceiptAsync(receiptId, cancellationToken);
        await warehouseAuthorization.EnsureWarehouseAccessAsync(receipt.WarehouseId, cancellationToken);
        var rows = await context.ImportReceiptInventoryIdentities.AsNoTracking()
            .Where(x => x.ImportReceiptDetail.ImportReceiptId == receiptId)
            .ToListAsync(cancellationToken);
        ValidatePersisted(receipt, rows);
    }

    public async Task<bool> TryPostTrackedBucketAsync(
        ImportReceipt receipt,
        ImportReceiptDetail detail,
        InventoryStatus status,
        decimal baseQuantity,
        int actorId,
        int? locationId,
        CancellationToken cancellationToken = default)
    {
        if (baseQuantity <= 0) return true;
        var product = detail.Product ?? await context.Products.SingleAsync(x => x.Id == detail.ProductId, cancellationToken);
        if (product.TrackingType == ProductTrackingType.None) return false;

        var identities = await context.ImportReceiptInventoryIdentities
            .Where(x => x.ImportReceiptDetailId == detail.Id && x.TargetStatus == status)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        if (identities.Sum(x => x.BaseQuantity) != baseQuantity)
            throw Conflict("LOT_REQUIRED", $"Dòng {detail.Id} chưa khai báo đủ Lot/Serial cho bucket {status}.");

        foreach (var identity in identities)
        {
            InventoryLot? lot = null;
            if (!string.IsNullOrWhiteSpace(identity.LotNumber))
            {
                lot = await ResolveLotAsync(product, identity, cancellationToken);
                if (lot is { Id: 0 })
                {
                    await context.SaveChangesAsync(cancellationToken);
                }
            }

            InventorySerial? serial = null;
            if (product.TrackingType == ProductTrackingType.Serial)
            {
                if (string.IsNullOrWhiteSpace(identity.SerialNumber))
                    throw Conflict("SERIAL_REQUIRED", $"Dòng {detail.Id} thiếu serial.");
                if (identity.BaseQuantity != 1m)
                    throw Conflict("SERIAL_QUANTITY_INVALID", "Mỗi serial phải có BaseQuantity = 1.");
                var serialNumber = NormalizeIdentifier(identity.SerialNumber);
                if (context.InventorySerials.Local.Any(
                        x => x.ProductId == product.Id &&
                             string.Equals(x.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase)) ||
                    await context.InventorySerials.AnyAsync(
                        x => x.ProductId == product.Id && x.SerialNumber == serialNumber,
                        cancellationToken))
                    throw Conflict("SERIAL_ALREADY_ON_HAND", $"Serial {serialNumber} đã tồn tại.");
                serial = new InventorySerial
                {
                    ProductId = product.Id,
                    LotId = lot?.Id,
                    Lot = lot,
                    SerialNumber = serialNumber,
                    CreatedAt = DateTime.UtcNow
                };
                context.InventorySerials.Add(serial);
                await context.SaveChangesAsync(cancellationToken);
            }

            var stock = context.InventoryStocks.Local.FirstOrDefault(
                x => x.ProductId == product.Id &&
                     x.WarehouseId == receipt.WarehouseId &&
                     x.LocationId == locationId &&
                     x.Status == status &&
                     (lot == null ? x.LotId == null && x.Lot == null : (lot.Id > 0 ? x.LotId == lot.Id : ReferenceEquals(x.Lot, lot))) &&
                     (serial == null ? x.SerialId == null && x.Serial == null : (serial.Id > 0 ? x.SerialId == serial.Id : ReferenceEquals(x.Serial, serial))));
            if (stock is null && (lot is null || lot.Id > 0) && (serial is null || serial.Id > 0))
            {
                stock = await context.InventoryStocks.SingleOrDefaultAsync(
                    x => x.ProductId == product.Id &&
                         x.WarehouseId == receipt.WarehouseId &&
                         x.LocationId == locationId &&
                         x.Status == status &&
                         x.LotId == (lot == null ? null : lot.Id) &&
                         x.SerialId == (serial == null ? null : serial.Id),
                    cancellationToken);
            }

            var factor = detail.ConversionFactor > 0 ? detail.ConversionFactor : 1m;
            var baseUnitCost = factor > 0 ? decimal.Round(detail.UnitPrice / factor, 4, MidpointRounding.AwayFromZero) : detail.UnitPrice;
            if (stock is null)
            {
                stock = new InventoryStock
                {
                    ProductId = product.Id,
                    WarehouseId = receipt.WarehouseId,
                    LocationId = locationId,
                    Status = status,
                    LotId = lot?.Id,
                    Lot = lot,
                    SerialId = serial?.Id,
                    Serial = serial,
                    Quantity = identity.BaseQuantity,
                    MovingAverageCost = baseUnitCost,
                    LastUpdated = DateTime.UtcNow
                };
                context.InventoryStocks.Add(stock);
            }
            else
            {
                if (product.TrackingType == ProductTrackingType.Serial && stock.Quantity + identity.BaseQuantity > 1m)
                    throw Conflict("SERIAL_ALREADY_ON_HAND", $"Serial {identity.SerialNumber} không thể có tồn kho > 1.");
                var oldQty = Math.Max(0m, stock.Quantity);
                var oldMac = stock.MovingAverageCost > 0m ? stock.MovingAverageCost : baseUnitCost;
                stock.MovingAverageCost = oldQty + identity.BaseQuantity > 0m
                    ? decimal.Round(((oldQty * oldMac) + (identity.BaseQuantity * baseUnitCost)) / (oldQty + identity.BaseQuantity), 4, MidpointRounding.AwayFromZero)
                    : baseUnitCost;
                stock.Quantity += identity.BaseQuantity;
                stock.LastUpdated = DateTime.UtcNow;
            }
            if (baseUnitCost > 0m)
            {
                product.MovingAverageCost = stock.MovingAverageCost > 0m ? stock.MovingAverageCost : baseUnitCost;
            }

            context.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductId = product.Id,
                WarehouseId = receipt.WarehouseId,
                LocationId = locationId,
                Lot = lot,
                Serial = serial,
                InventoryStatus = status,
                TransactionType = TransactionType.Import,
                Quantity = identity.BaseQuantity,
                ReferenceId = receipt.Id,
                ReferenceType = "ImportReceipt",
                TransactionDate = DateTime.UtcNow,
                CreatedBy = actorId,
                Note = detail.Note
            });
        }

        return true;
    }

    private async Task<InventoryLot> ResolveLotAsync(
        Product product,
        ImportReceiptInventoryIdentity identity,
        CancellationToken cancellationToken)
    {
        var number = NormalizeIdentifier(identity.LotNumber!);
        var lot = context.InventoryLots.Local.SingleOrDefault(
                      x => x.ProductId == product.Id &&
                           string.Equals(x.LotNumber, number, StringComparison.OrdinalIgnoreCase))
                  ?? await context.InventoryLots.SingleOrDefaultAsync(
                      x => x.ProductId == product.Id && x.LotNumber == number,
                      cancellationToken);
        if (lot is null)
        {
            lot = new InventoryLot
            {
                ProductId = product.Id,
                LotNumber = number,
                ManufactureDate = identity.ManufactureDate,
                ExpiryDate = identity.ExpiryDate,
                ReceivedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            context.InventoryLots.Add(lot);
            return lot;
        }

        if (lot.ManufactureDate != identity.ManufactureDate || lot.ExpiryDate != identity.ExpiryDate)
            throw Conflict("LOT_ALREADY_EXISTS", $"Lot {number} đã tồn tại với ngày manufacture/expiry khác.");
        return lot;
    }

    private async Task<ImportReceipt> LoadReceiptAsync(int receiptId, CancellationToken cancellationToken) =>
        await context.ImportReceipts
            .AsSplitQuery()
            .Include(x => x.Details).ThenInclude(x => x.Product)
            .SingleOrDefaultAsync(x => x.Id == receiptId, cancellationToken)
        ?? throw new NotFoundException("Không tìm thấy phiếu nhập.");
}
