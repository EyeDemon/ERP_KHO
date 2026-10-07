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

public sealed class ReceiptInventoryIdentityService(
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
                lot = await ResolveLotAsync(product, identity, cancellationToken);

            InventorySerial? serial = null;
            if (product.TrackingType == ProductTrackingType.Serial)
            {
                if (string.IsNullOrWhiteSpace(identity.SerialNumber))
                    throw Conflict("SERIAL_REQUIRED", $"Dòng {detail.Id} thiếu serial.");
                if (await context.InventorySerials.AnyAsync(
                        x => x.ProductId == product.Id && x.SerialNumber == identity.SerialNumber,
                        cancellationToken))
                    throw Conflict("SERIAL_ALREADY_ON_HAND", $"Serial {identity.SerialNumber} đã tồn tại.");
                serial = new InventorySerial
                {
                    ProductId = product.Id,
                    Lot = lot,
                    SerialNumber = identity.SerialNumber!,
                    CreatedAt = DateTime.UtcNow
                };
                context.InventorySerials.Add(serial);
            }

            var stock = await context.InventoryStocks.SingleOrDefaultAsync(
                x => x.ProductId == product.Id &&
                     x.WarehouseId == receipt.WarehouseId &&
                     x.LocationId == locationId &&
                     x.Status == status &&
                     x.LotId == (lot == null ? null : lot.Id) &&
                     x.SerialId == (serial == null ? null : serial.Id),
                cancellationToken);
            if (stock is null)
            {
                stock = new InventoryStock
                {
                    ProductId = product.Id,
                    WarehouseId = receipt.WarehouseId,
                    LocationId = locationId,
                    Status = status,
                    Lot = lot,
                    Serial = serial,
                    Quantity = identity.BaseQuantity,
                    LastUpdated = DateTime.UtcNow
                };
                context.InventoryStocks.Add(stock);
            }
            else
            {
                stock.Quantity += identity.BaseQuantity;
                stock.LastUpdated = DateTime.UtcNow;
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
                      x => x.ProductId == product.Id && x.LotNumber == number)
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

    private List<NormalizedIdentity> NormalizeAndValidate(
        ImportReceipt receipt,
        IReadOnlyList<SetImportReceiptInventoryIdentityLineDto> requested)
    {
        var result = new List<NormalizedIdentity>();
        if (requested.Select(x => new { x.LineId, Serial = NormalizeNullable(x.SerialNumber) })
            .Where(x => x.Serial is not null)
            .GroupBy(x => x.Serial!, StringComparer.OrdinalIgnoreCase)
            .Any(g => g.Count() > 1))
            throw Conflict("SERIAL_DUPLICATE", "Serial bị trùng trong request.");

        foreach (var dto in requested)
        {
            var detail = receipt.Details.SingleOrDefault(x => x.Id == dto.LineId)
                ?? throw new BusinessRuleException($"Dòng receipt {dto.LineId} không thuộc phiếu này.");
            if (!Enum.TryParse<InventoryStatus>(dto.TargetStatus, true, out var status) ||
                status is not InventoryStatus.Available and not InventoryStatus.Damaged and not InventoryStatus.Rejected)
                throw new BusinessRuleException("Receipt identity chỉ hỗ trợ AVAILABLE, DAMAGED hoặc REJECTED.");
            if (dto.BaseQuantity <= 0)
                throw new BusinessRuleException("Số lượng identity phải lớn hơn 0.");

            var product = detail.Product;
            var lot = NormalizeNullable(dto.LotNumber);
            var serial = NormalizeNullable(dto.SerialNumber);
            ValidateTrackingIdentity(product, dto.BaseQuantity, lot, serial, dto.ManufactureDate, dto.ExpiryDate);
            result.Add(new NormalizedIdentity(
                detail.Id, product.Id, status, dto.BaseQuantity, lot, dto.ManufactureDate, dto.ExpiryDate, serial));
        }

        ValidateNormalized(receipt, result);
        return result;
    }

    private void ValidatePersisted(ImportReceipt receipt, IReadOnlyList<ImportReceiptInventoryIdentity> rows)
    {
        var normalized = rows.Select(x => new NormalizedIdentity(
            x.ImportReceiptDetailId, x.ProductId, x.TargetStatus, x.BaseQuantity, x.LotNumber,
            x.ManufactureDate, x.ExpiryDate, x.SerialNumber)).ToList();
        foreach (var row in normalized)
        {
            var detail = receipt.Details.Single(x => x.Id == row.LineId);
            ValidateTrackingIdentity(detail.Product, row.BaseQuantity, row.LotNumber, row.SerialNumber, row.ManufactureDate, row.ExpiryDate);
        }
        ValidateNormalized(receipt, normalized);
    }

    private static void ValidateNormalized(ImportReceipt receipt, IReadOnlyList<NormalizedIdentity> rows)
    {
        foreach (var detail in receipt.Details)
        {
            var product = detail.Product;
            var lineRows = rows.Where(x => x.LineId == detail.Id).ToList();
            if (product.TrackingType == ProductTrackingType.None)
            {
                if (lineRows.Count != 0)
                    throw new BusinessRuleException($"Sản phẩm {product.Code} không theo dõi Lot/Serial.");
                continue;
            }

            ValidateBucket(detail, lineRows, InventoryStatus.Available, detail.BaseAcceptedQuantity);
            ValidateBucket(detail, lineRows, InventoryStatus.Damaged, detail.BaseDamagedQuantity);
            ValidateBucket(detail, lineRows, InventoryStatus.Rejected, detail.BaseRejectedQuantity);

            if (product.TrackingType == ProductTrackingType.Serial)
            {
                var physical = detail.BaseAcceptedQuantity + detail.BaseDamagedQuantity + detail.BaseRejectedQuantity;
                if (physical != decimal.Truncate(physical))
                    throw Conflict("SERIAL_REQUIRED", $"Sản phẩm serial {product.Code} yêu cầu Base UOM nguyên đơn vị.");
                if (lineRows.Count != (int)physical)
                    throw Conflict("SERIAL_REQUIRED", $"Sản phẩm serial {product.Code} yêu cầu đúng {physical} serial.");
            }
        }
    }

    private static void ValidateBucket(
        ImportReceiptDetail detail,
        IReadOnlyList<NormalizedIdentity> rows,
        InventoryStatus status,
        decimal expected)
    {
        var actual = rows.Where(x => x.TargetStatus == status).Sum(x => x.BaseQuantity);
        if (actual != expected)
        {
            var code = detail.Product.TrackingType == ProductTrackingType.Serial ? "SERIAL_REQUIRED" : "LOT_REQUIRED";
            throw Conflict(code, $"Dòng {detail.Id} bucket {status} cần {expected} Base UOM nhưng identity đang là {actual}.");
        }
    }

    private static void ValidateTrackingIdentity(
        Product product,
        decimal quantity,
        string? lot,
        string? serial,
        DateTime? manufacture,
        DateTime? expiry)
    {
        if (product.TrackingType == ProductTrackingType.Lot && lot is null)
            throw Conflict("LOT_REQUIRED", $"Sản phẩm {product.Code} yêu cầu lot.");
        if (product.TrackingType == ProductTrackingType.Lot && serial is not null)
            throw new BusinessRuleException($"Sản phẩm lot {product.Code} không nhận serial.");
        if (product.TrackingType == ProductTrackingType.Serial)
        {
            if (serial is null) throw Conflict("SERIAL_REQUIRED", $"Sản phẩm {product.Code} yêu cầu serial.");
            if (quantity != 1) throw Conflict("SERIAL_REQUIRED", "Mỗi serial phải có BaseQuantity = 1.");
        }
        if (product.ExpiryControl)
        {
            if (lot is null) throw Conflict("LOT_REQUIRED", $"Sản phẩm {product.Code} bật Expiry Control nên cần lot.");
            if (!expiry.HasValue) throw Conflict("EXPIRY_REQUIRED", $"Sản phẩm {product.Code} yêu cầu expiry.");
        }
        if (manufacture.HasValue && expiry.HasValue && expiry.Value.Date < manufacture.Value.Date)
            throw new BusinessRuleException("Expiry không được trước manufacture date.");
        if (expiry.HasValue && expiry.Value.Date < DateTime.UtcNow.Date)
            throw Conflict("LOT_EXPIRED", "Không thể nhận lot đã hết hạn.");
    }

    private static string NormalizeIdentifier(string value) => value.Trim();

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static BusinessRuleException Conflict(string code, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["HttpStatusCode"] = 409;
        ex.Data["ErrorCode"] = code;
        return ex;
    }

    private sealed record NormalizedIdentity(
        int LineId,
        int ProductId,
        InventoryStatus TargetStatus,
        decimal BaseQuantity,
        string? LotNumber,
        DateTime? ManufactureDate,
        DateTime? ExpiryDate,
        string? SerialNumber);
}
