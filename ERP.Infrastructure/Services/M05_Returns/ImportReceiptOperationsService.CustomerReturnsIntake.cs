using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ImportReceiptOperationsService
{
    public async Task<WmsEndpointOutcome> IntakeCustomerReturnAsync(
        CustomerReturnIntakeInput? request,
        int? actorUserId = null,
        bool canReadCost = true,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "RMA_INTAKE_REQUIRED",
                errorCode = "RMA_INTAKE_REQUIRED",
                message = "Dữ liệu tiếp nhận hàng hoàn trả không được để trống."
            });
        }

        var now = DateTime.UtcNow;
        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : 1);
        var effectiveAuth = warehouseAuth ?? warehouseAccess;

        var trackingCandidate = (request.TrackingNumber ?? request.ReturnReference)?.Trim();
        var exportCodeCandidate = (request.ExportReceiptCode ?? request.ReturnReference)?.Trim();
        var soCodeCandidate = (request.SalesOrderCode ?? request.ReturnReference)?.Trim();

        ERP.Domain.Entities.Shipment? matchedShipment = null;
        if (request.ShipmentId.HasValue)
        {
            matchedShipment = await db.Shipments.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == request.ShipmentId.Value, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(trackingCandidate))
        {
            matchedShipment = await db.Shipments.AsNoTracking()
                .FirstOrDefaultAsync(
                    s => s.ShipmentCode == trackingCandidate || s.SealNumber == trackingCandidate,
                    cancellationToken);
        }

        ERP.Domain.Entities.ExportReceipt? matchedExport = null;
        var resolvedExportId = request.ExportReceiptId
            ?? (matchedShipment is { SourceType: "ExportReceipt", SourceId: not null } ? matchedShipment.SourceId : null);
        if (resolvedExportId.HasValue)
        {
            matchedExport = await db.ExportReceipts.AsNoTracking()
                .Include(e => e.Details)
                .FirstOrDefaultAsync(e => e.Id == resolvedExportId.Value, cancellationToken);
        }
        else if (matchedShipment != null)
        {
            matchedExport = await db.ExportReceipts.AsNoTracking()
                .Include(e => e.Details)
                .FirstOrDefaultAsync(e => e.ShipmentId == matchedShipment.Id, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(exportCodeCandidate))
        {
            matchedExport = await db.ExportReceipts.AsNoTracking()
                .Include(e => e.Details)
                .FirstOrDefaultAsync(e => e.Code == exportCodeCandidate, cancellationToken);
        }

        var resolvedSalesOrderId = request.SalesOrderId
            ?? matchedExport?.SalesOrderId
            ?? (matchedShipment is { SourceType: "SalesOrder", SourceId: not null } ? matchedShipment.SourceId : null);
        if (!resolvedSalesOrderId.HasValue && !string.IsNullOrWhiteSpace(soCodeCandidate))
        {
            resolvedSalesOrderId = await db.SalesOrders.AsNoTracking()
                .Where(s => s.OrderCode == soCodeCandidate || s.ExternalOrderId == soCodeCandidate)
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var targetWhId = request.WarehouseId
            ?? matchedExport?.WarehouseId
            ?? matchedShipment?.WarehouseId
            ?? await db.Warehouses.AsNoTracking().OrderBy(w => w.Id).Select(w => w.Id).FirstOrDefaultAsync(cancellationToken);
        if (targetWhId <= 0) targetWhId = 1;

        if (effectiveAuth != null)
            await effectiveAuth.EnsureWarehouseAccessAsync(targetWhId, cancellationToken);

        int? resolvedQuarantineLocId = request.QuarantineLocationId;
        if (!resolvedQuarantineLocId.HasValue && !string.IsNullOrWhiteSpace(request.QuarantineLocationCode))
        {
            var qCode = request.QuarantineLocationCode.Trim();
            resolvedQuarantineLocId = await db.WarehouseLocations.AsNoTracking()
                .Where(l => l.WarehouseId == targetWhId && l.Code == qCode)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        if (!resolvedQuarantineLocId.HasValue)
        {
            resolvedQuarantineLocId = await db.WarehouseLocations.AsNoTracking()
                .Where(l => l.WarehouseId == targetWhId && l.IsActive)
                .OrderBy(l => l.Code.Contains("QUAR") || l.Code.Contains("RMA") ? 0 : (l.LocationType == ERP.Domain.Enums.WarehouseLocationType.Receiving ? 1 : 2))
                .ThenBy(l => l.Id)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var rawLines = new List<CustomerReturnIntakeLineInput>();
        if (request.Lines is { Count: > 0 })
        {
            rawLines.AddRange(request.Lines);
        }
        else if (request.ProductId.HasValue || !string.IsNullOrWhiteSpace(request.ProductCode))
        {
            rawLines.Add(new CustomerReturnIntakeLineInput(
                ProductId: request.ProductId,
                ProductCode: request.ProductCode,
                Quantity: request.Quantity ?? 1m,
                UnitCost: request.UnitCost,
                LotId: request.LotId,
                LotNumber: request.LotNumber,
                SerialId: request.SerialId,
                SerialNumber: request.SerialNumber,
                QuarantineLocationId: resolvedQuarantineLocId,
                Condition: request.Condition));
        }
        else if (matchedExport is { Details.Count: > 0 })
        {
            foreach (var d in matchedExport.Details.OrderBy(x => x.Id))
            {
                rawLines.Add(new CustomerReturnIntakeLineInput(
                    ProductId: d.ProductId,
                    Quantity: d.Quantity,
                    UnitCost: d.UnitPrice,
                    QuarantineLocationId: resolvedQuarantineLocId,
                    Condition: request.Condition ?? "GOOD",
                    BaseUomCode: d.BaseUomCodeSnapshot,
                    TransactionUomCode: d.BaseUomCodeSnapshot,
                    ConversionRateSnapshot: 1m));
            }
        }

        if (rawLines.Count == 0)
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "RMA_LINES_REQUIRED",
                errorCode = "RMA_LINES_REQUIRED",
                message = "Cần chỉ định ít nhất 1 dòng sản phẩm hoàn trả hoặc mã vận đơn/phiếu xuất hợp lệ."
            });
        }

        var resolvedLines = new List<(
            int ProductId,
            int? LotId,
            int? SerialId,
            DateTime? ExpiryDate,
            int? BaseUomId,
            string BaseUomCode,
            string TransactionUomCode,
            decimal ConversionRate,
            int? QuarantineLocId,
            decimal Quantity,
            decimal UnitCost,
            string Condition)>();

        foreach (var rl in rawLines)
        {
            if (rl.Quantity <= 0m)
            {
                return WmsEndpointOutcome.UnprocessableEntity(new
                {
                    code = "RMA_INVALID_QUANTITY",
                    errorCode = "RMA_INVALID_QUANTITY",
                    message = "Số lượng tiếp nhận hoàn trả phải lớn hơn 0."
                });
            }

            var prodId = rl.ProductId;
            if (!prodId.HasValue && !string.IsNullOrWhiteSpace(rl.ProductCode))
            {
                var pCode = rl.ProductCode.Trim();
                prodId = await db.Products.AsNoTracking()
                    .Where(p => p.Code == pCode)
                    .Select(p => (int?)p.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            if (!prodId.HasValue)
            {
                return WmsEndpointOutcome.NotFound(new
                {
                    code = "PRODUCT_NOT_FOUND",
                    errorCode = "PRODUCT_NOT_FOUND",
                    message = "Không tìm thấy sản phẩm hoàn trả."
                });
            }

            var product = await db.Products.AsNoTracking()
                .Include(p => p.Unit)
                .FirstOrDefaultAsync(p => p.Id == prodId.Value, cancellationToken);
            if (product == null)
            {
                return WmsEndpointOutcome.NotFound(new
                {
                    code = "PRODUCT_NOT_FOUND",
                    errorCode = "PRODUCT_NOT_FOUND",
                    message = $"Không tìm thấy sản phẩm #{prodId.Value}."
                });
            }

            var matchedDetail = matchedExport?.Details.FirstOrDefault(d => d.ProductId == product.Id);

            var lotId = rl.LotId;
            if (!lotId.HasValue && !string.IsNullOrWhiteSpace(rl.LotNumber))
            {
                var lNum = rl.LotNumber.Trim();
                lotId = await db.InventoryLots.AsNoTracking()
                    .Where(l => l.ProductId == product.Id && l.LotNumber == lNum)
                    .Select(l => (int?)l.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var serialId = rl.SerialId;
            if (!serialId.HasValue && !string.IsNullOrWhiteSpace(rl.SerialNumber))
            {
                var sNum = rl.SerialNumber.Trim();
                serialId = await db.InventorySerials.AsNoTracking()
                    .Where(s => s.ProductId == product.Id && s.SerialNumber == sNum)
                    .Select(s => (int?)s.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var lineQId = rl.QuarantineLocationId ?? resolvedQuarantineLocId;
            if (!rl.QuarantineLocationId.HasValue && !string.IsNullOrWhiteSpace(rl.QuarantineLocationCode))
            {
                var qlCode = rl.QuarantineLocationCode.Trim();
                lineQId = await db.WarehouseLocations.AsNoTracking()
                    .Where(l => l.WarehouseId == targetWhId && l.Code == qlCode)
                    .Select(l => (int?)l.Id)
                    .FirstOrDefaultAsync(cancellationToken) ?? resolvedQuarantineLocId;
            }

            var baseUomCode = !string.IsNullOrWhiteSpace(rl.BaseUomCode)
                ? rl.BaseUomCode.Trim()
                : (!string.IsNullOrWhiteSpace(product.Unit?.Code) ? product.Unit.Code.Trim() : "PCS");
            var txUomCode = !string.IsNullOrWhiteSpace(rl.TransactionUomCode)
                ? rl.TransactionUomCode.Trim()
                : baseUomCode;
            var convRate = rl.ConversionRateSnapshot is > 0m ? rl.ConversionRateSnapshot.Value : 1m;
            var baseQty = decimal.Round(rl.Quantity * convRate, 4);
            var unitCost = rl.UnitCost ?? matchedDetail?.UnitPrice ?? product.MovingAverageCost;
            int? baseUomId = product.UnitId > 0 ? product.UnitId : (int?)null;

            resolvedLines.Add((
                product.Id,
                lotId,
                serialId,
                rl.ExpiryDate,
                baseUomId,
                baseUomCode,
                txUomCode,
                convRate,
                lineQId,
                baseQty,
                unitCost,
                NormalizeConditionCode(rl.Condition ?? request.Condition)));
        }


        return await PersistCustomerReturnIntakeAsync(
            request,
            targetWhId,
            resolvedLines,
            trackingCandidate,
            resolvedSalesOrderId,
            matchedExport,
            matchedShipment,
            resolvedQuarantineLocId,
            effectiveActorId,
            canReadCost,
            effectiveAuth,
            now,
            cancellationToken);
    }
}
