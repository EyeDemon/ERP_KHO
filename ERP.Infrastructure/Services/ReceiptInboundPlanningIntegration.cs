using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class ReceiptInboundPlanningIntegration(ErpKhoDbContext context) : IReceiptInboundPlanningIntegration
{
    public async Task AttachSourceAsync(ImportReceipt receipt, CreateImportReceiptDto dto, CancellationToken token = default)
    {
        if (dto.AsnId.HasValue)
        {
            var asn = await context.Asns.AsNoTracking()
                .Include(x => x.PurchaseOrder)
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == dto.AsnId.Value
                    && x.WarehouseId == receipt.WarehouseId
                    && (!receipt.SupplierId.HasValue || x.SupplierId == receipt.SupplierId.Value), token)
                ?? throw new NotFoundException("Không tìm thấy ASN phù hợp với kho/nhà cung cấp của phiếu nhập.");

            if (asn.Status is not AsnStatus.Arrived and not AsnStatus.Receiving)
                throw Conflict("Chỉ ASN đã đến hoặc đang tiếp nhận mới được tạo phiếu nhập.");

            if (await context.ImportReceipts.AsNoTracking().AnyAsync(x => x.AsnId == asn.Id && x.Status != ReceiptStatus.Cancelled, token))
                throw Conflict("ASN đã được liên kết với một phiếu nhập đang hoạt động.");

            receipt.AsnId = asn.Id;
            receipt.PurchaseOrderId = asn.PurchaseOrderId;
            receipt.SupplierId = asn.SupplierId;

            if (dto.Details.Count != asn.Lines.Count)
                throw new BusinessRuleException("Phiếu nhập từ ASN phải bao phủ chính xác tất cả dòng ASN.");

            var used = new HashSet<int>();
            for (var index = 0; index < dto.Details.Count; index++)
            {
                var sourceDto = dto.Details[index];
                if (!sourceDto.AsnLineId.HasValue || !used.Add(sourceDto.AsnLineId.Value))
                    throw new BusinessRuleException("Mỗi dòng phiếu nhập từ ASN phải tham chiếu một dòng ASN duy nhất.");

                var source = asn.Lines.SingleOrDefault(x => x.Id == sourceDto.AsnLineId.Value)
                    ?? throw new NotFoundException("Không tìm thấy dòng ASN thuộc nguồn đã chọn.");
                if (source.ProductId != sourceDto.ProductId)
                    throw new BusinessRuleException("Sản phẩm phiếu nhập không khớp dòng ASN.");

                var target = receipt.Details.ElementAt(index);
                target.AsnLineId = source.Id;
                target.PurchaseOrderLineId = source.PurchaseOrderLineId;
                target.ProductId = source.ProductId;
                target.Quantity = source.ExpectedQuantity;
                target.ExpectedQuantity = source.ExpectedQuantity;
                target.OperationUnitId = source.OperationUnitId;
                target.OperationUnitCodeSnapshot = source.OperationUnitCodeSnapshot;
                target.OperationUnitDecimalPlaces = source.OperationUnitDecimalPlaces;
                target.BaseUnitId = source.BaseUnitId;
                target.BaseUnitCodeSnapshot = source.BaseUnitCodeSnapshot;
                target.BaseUnitDecimalPlaces = source.BaseUnitDecimalPlaces;
                target.ConversionFactor = source.ConversionFactorSnapshot;
                target.ConversionVersion = source.ConversionVersionSnapshot;
                target.BaseExpectedQuantity = source.BaseExpectedQuantity;
            }
            return;
        }

        if (dto.PurchaseOrderId.HasValue)
        {
            var po = await context.PurchaseOrders.AsNoTracking()
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == dto.PurchaseOrderId.Value
                    && x.WarehouseId == receipt.WarehouseId
                    && (!receipt.SupplierId.HasValue || x.SupplierId == receipt.SupplierId.Value), token)
                ?? throw new NotFoundException("Không tìm thấy đơn mua phù hợp với kho/nhà cung cấp của phiếu nhập.");

            if (po.Status is not PurchaseOrderStatus.Open and not PurchaseOrderStatus.PartiallyReceived)
                throw Conflict("Đơn mua chưa ở trạng thái cho phép nhận hàng.");

            receipt.PurchaseOrderId = po.Id;
            receipt.SupplierId = po.SupplierId;

            var used = new HashSet<int>();
            for (var index = 0; index < dto.Details.Count; index++)
            {
                var sourceDto = dto.Details[index];
                if (!sourceDto.PurchaseOrderLineId.HasValue || !used.Add(sourceDto.PurchaseOrderLineId.Value))
                    throw new BusinessRuleException("Mỗi dòng phiếu nhập từ PO phải tham chiếu một dòng PO duy nhất.");

                var source = po.Lines.SingleOrDefault(x => x.Id == sourceDto.PurchaseOrderLineId.Value)
                    ?? throw new NotFoundException("Không tìm thấy dòng PO thuộc nguồn đã chọn.");
                if (source.ProductId != sourceDto.ProductId)
                    throw new BusinessRuleException("Sản phẩm phiếu nhập không khớp dòng PO.");
                if (sourceDto.ExpectedQuantity <= 0)
                    throw new BusinessRuleException("Số lượng dự kiến phải lớn hơn 0.");

                EnsurePrecision(sourceDto.ExpectedQuantity, source.OperationUnitDecimalPlaces, "Số lượng dự kiến theo PO");
                var baseExpected = sourceDto.ExpectedQuantity * source.ConversionFactorSnapshot;
                EnsurePrecision(baseExpected, source.BaseUnitDecimalPlaces, "Số lượng Base UOM theo PO");
                var max = source.BaseOrderedQuantity * (1m + source.AllowedOverReceiptPct / 100m);
                if (baseExpected > max)
                    throw Conflict("Số lượng dự kiến của phiếu nhập vượt giới hạn nhận của dòng PO.");

                var target = receipt.Details.ElementAt(index);
                target.PurchaseOrderLineId = source.Id;
                target.OperationUnitId = source.OperationUnitId;
                target.OperationUnitCodeSnapshot = source.OperationUnitCodeSnapshot;
                target.OperationUnitDecimalPlaces = source.OperationUnitDecimalPlaces;
                target.BaseUnitId = source.BaseUnitId;
                target.BaseUnitCodeSnapshot = source.BaseUnitCodeSnapshot;
                target.BaseUnitDecimalPlaces = source.BaseUnitDecimalPlaces;
                target.ConversionFactor = source.ConversionFactorSnapshot;
                target.ConversionVersion = source.ConversionVersionSnapshot;
                target.BaseExpectedQuantity = baseExpected;
            }
            return;
        }

        if (dto.Details.Any(x => x.PurchaseOrderLineId.HasValue || x.AsnLineId.HasValue))
            throw new BusinessRuleException("Dòng phiếu nhập có tham chiếu nguồn nhưng header chưa chọn PO/ASN.");
    }

    public async Task MarkReceivingAsync(ImportReceipt receipt, CancellationToken token = default)
    {
        if (!receipt.AsnId.HasValue) return;

        var asn = await context.Asns.SingleOrDefaultAsync(x => x.Id == receipt.AsnId.Value && x.WarehouseId == receipt.WarehouseId, token)
            ?? throw new NotFoundException("Không tìm thấy ASN nguồn của phiếu nhập.");
        if (asn.Status == AsnStatus.Arrived)
        {
            asn.Status = AsnStatus.Receiving;
            asn.UpdatedAtUtc = DateTime.UtcNow;
            context.AuditLogs.Add(Audit("Asn.ReceivingStartedFromReceipt", "Asn", asn.Id, asn.WarehouseId, "Status: Receiving", receipt.CreatedBy));
        }
        else if (asn.Status != AsnStatus.Receiving)
        {
            throw Conflict("ASN nguồn không còn ở trạng thái cho phép tiếp nhận.");
        }
    }

    public async Task ValidatePostAsync(ImportReceipt receipt, CancellationToken token = default)
    {
        if (!receipt.PurchaseOrderId.HasValue) return;

        var po = await context.PurchaseOrders.AsNoTracking().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == receipt.PurchaseOrderId.Value && x.WarehouseId == receipt.WarehouseId, token)
            ?? throw new NotFoundException("Không tìm thấy PO nguồn của phiếu nhập.");
        if (po.Status is PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Closed)
            throw Conflict("PO nguồn đã đóng hoặc hủy.");

        foreach (var detail in receipt.Details.Where(x => x.PurchaseOrderLineId.HasValue))
        {
            var line = po.Lines.SingleOrDefault(x => x.Id == detail.PurchaseOrderLineId)
                ?? throw new BusinessRuleException("Dòng phiếu nhập không còn khớp PO nguồn.");
            var previous = await context.ImportReceiptDetails.AsNoTracking()
                .Where(x => x.PurchaseOrderLineId == line.Id
                    && x.ImportReceiptId != receipt.Id
                    && x.ImportReceipt.Status == ReceiptStatus.Posted)
                .SumAsync(x => (decimal?)x.BasePostedQuantity, token) ?? 0m;
            var maximum = line.BaseOrderedQuantity * (1m + line.AllowedOverReceiptPct / 100m);
            if (previous + detail.BaseAcceptedQuantity + detail.BaseDamagedQuantity + detail.BaseRejectedQuantity > maximum)
                throw Conflict($"Tổng số lượng POST vượt dung sai nhận của dòng PO {line.ExternalLineId}.");
        }

        if (receipt.AsnId.HasValue)
        {
            var status = await context.Asns.AsNoTracking().Where(x => x.Id == receipt.AsnId.Value).Select(x => x.Status).SingleOrDefaultAsync(token);
            if (status != AsnStatus.Receiving)
                throw Conflict("ASN nguồn phải ở trạng thái đang tiếp nhận trước khi POST phiếu nhập.");
        }
    }

    public async Task ApplyPostedStateAsync(ImportReceipt receipt, int actorUserId, CancellationToken token = default)
    {
        if (receipt.AsnId.HasValue)
        {
            var asn = await context.Asns.SingleAsync(x => x.Id == receipt.AsnId.Value && x.WarehouseId == receipt.WarehouseId, token);
            if (asn.Status != AsnStatus.Receiving) throw Conflict("ASN nguồn không còn ở trạng thái đang tiếp nhận.");
            asn.Status = AsnStatus.Completed;
            asn.UpdatedAtUtc = DateTime.UtcNow;
            asn.UpdatedBy = actorUserId;
            context.AuditLogs.Add(Audit("Asn.CompletedFromReceiptPost", "Asn", asn.Id, asn.WarehouseId, "Status: Completed", actorUserId));
        }

        if (!receipt.PurchaseOrderId.HasValue) return;

        var po = await context.PurchaseOrders.Include(x => x.Lines)
            .SingleAsync(x => x.Id == receipt.PurchaseOrderId.Value && x.WarehouseId == receipt.WarehouseId, token);
        var anyReceived = false;
        var allReceived = true;

        foreach (var line in po.Lines)
        {
            var previous = await context.ImportReceiptDetails.AsNoTracking()
                .Where(x => x.PurchaseOrderLineId == line.Id
                    && x.ImportReceiptId != receipt.Id
                    && x.ImportReceipt.Status == ReceiptStatus.Posted)
                .SumAsync(x => (decimal?)x.BasePostedQuantity, token) ?? 0m;
            var current = receipt.Details.Where(x => x.PurchaseOrderLineId == line.Id).Sum(x => x.BasePostedQuantity);
            var total = previous + current;
            if (total > 0) anyReceived = true;
            if (total < line.BaseOrderedQuantity) allReceived = false;
        }

        var target = allReceived ? PurchaseOrderStatus.Received : anyReceived ? PurchaseOrderStatus.PartiallyReceived : PurchaseOrderStatus.Open;
        if (po.Status != target)
        {
            po.Status = target;
            po.UpdatedAtUtc = DateTime.UtcNow;
            po.UpdatedBy = actorUserId;
            context.AuditLogs.Add(Audit("PurchaseOrder.ReconciledFromReceiptPost", "PurchaseOrder", po.Id, po.WarehouseId, $"Status: {target}", actorUserId));
        }
    }

    private static AuditLog Audit(string action, string entityName, int entityId, int warehouseId, string values, int actor) => new()
    {
        UserId = actor,
        Action = action,
        EntityName = entityName,
        EntityId = entityId,
        WarehouseId = warehouseId,
        NewValues = values,
        Result = "Success",
        Severity = "Information",
        Timestamp = DateTime.UtcNow
    };

    private static void EnsurePrecision(decimal value, int decimals, string field)
    {
        if (decimals is < 0 or > 4 || decimal.Round(value, decimals) != value)
            throw new BusinessRuleException($"{field} vượt quá {decimals} chữ số thập phân; hệ thống không tự làm tròn.");
    }

    private static BusinessRuleException Conflict(string message)
    {
        var exception = new BusinessRuleException(message);
        exception.Data["HttpStatusCode"] = 409;
        return exception;
    }
}
