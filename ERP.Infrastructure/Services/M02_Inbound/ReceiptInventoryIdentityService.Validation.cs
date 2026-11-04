using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Domain.Enums;

namespace ERP.Infrastructure.Services;

public sealed partial class ReceiptInventoryIdentityService
{
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
            if (quantity != 1) throw Conflict("SERIAL_QUANTITY_INVALID", "Mỗi serial phải có BaseQuantity = 1.");
        }
        if (product.ExpiryControl)
        {
            if (lot is null) throw Conflict("LOT_REQUIRED", $"Sản phẩm {product.Code} bật Expiry Control nên cần lot.");
            if (!expiry.HasValue) throw Conflict("EXPIRY_REQUIRED", $"Sản phẩm {product.Code} yêu cầu expiry.");
        }
        if (manufacture.HasValue && expiry.HasValue && expiry.Value.Date < manufacture.Value.Date)
            throw Conflict("EXPIRY_BEFORE_MANUFACTURE", "Expiry không được trước manufacture date.");
        if (expiry.HasValue && expiry.Value.Date < DateTime.UtcNow.Date)
            throw Conflict("LOT_EXPIRED", "Không thể nhận lot đã hết hạn.");
    }

    private static string NormalizeIdentifier(string value) =>
        new string(value.Where(ch => ch >= 32 && ch != 127 && ch != '\u200b' && ch != '\ufeff').ToArray()).Trim();

    private static string? NormalizeNullable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = NormalizeIdentifier(value);
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

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
