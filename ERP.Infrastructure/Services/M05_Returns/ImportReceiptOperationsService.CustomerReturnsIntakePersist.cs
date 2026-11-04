using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ImportReceiptOperationsService
{
    private async Task<WmsEndpointOutcome> PersistCustomerReturnIntakeAsync(
        CustomerReturnIntakeInput request,
        int targetWhId,
        List<(int ProductId, int? LotId, int? SerialId, DateTime? ExpiryDate, int? BaseUomId, string BaseUomCode, string TransactionUomCode, decimal ConversionRate, int? QuarantineLocId, decimal Quantity, decimal UnitCost, string Condition)> resolvedLines,
        string? trackingCandidate,
        int? resolvedSalesOrderId,
        ERP.Domain.Entities.ExportReceipt? matchedExport,
        ERP.Domain.Entities.Shipment? matchedShipment,
        int? resolvedQuarantineLocId,
        int effectiveActorId,
        bool canReadCost,
        IWarehouseAuthorizationService? effectiveAuth,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var isRelational = db.Database.IsRelational();
        await using var tx = isRelational && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        try
        {
            await db.AcquireInventoryAllocationLocksAsync(
                targetWhId,
                resolvedLines.Select(x => x.ProductId).Distinct().OrderBy(x => x),
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(request.ReturnCode) || !string.IsNullOrWhiteSpace(trackingCandidate))
            {
                var rCode = request.ReturnCode?.Trim();
                var existingReturn = await db.CustomerReturns.AsNoTracking()
                    .FirstOrDefaultAsync(r =>
                        r.WarehouseId == targetWhId &&
                        ((!string.IsNullOrWhiteSpace(rCode) && r.ReturnCode == rCode) ||
                         (!string.IsNullOrWhiteSpace(trackingCandidate) && r.TrackingNumber == trackingCandidate)),
                        cancellationToken);
                if (existingReturn != null)
                {
                    if (tx != null) await tx.RollbackAsync(cancellationToken);
                    return await GetCustomerReturnByIdAsync(existingReturn.Id, canReadCost, effectiveAuth, cancellationToken);
                }
            }

            var returnCode = !string.IsNullOrWhiteSpace(request.ReturnCode)
                ? request.ReturnCode.Trim()
                : $"RMA-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

            var customerReturn = new ERP.Domain.Entities.CustomerReturn
            {
                ReturnCode = returnCode,
                WarehouseId = targetWhId,
                SalesOrderId = resolvedSalesOrderId,
                ExportReceiptId = matchedExport?.Id ?? request.ExportReceiptId,
                ShipmentId = matchedShipment?.Id ?? request.ShipmentId,
                QuarantineLocationId = resolvedQuarantineLocId,
                TrackingNumber = trackingCandidate ?? matchedShipment?.ShipmentCode,
                OrderNumber = request.SalesOrderCode ?? matchedExport?.Code ?? matchedShipment?.SourceCode,
                ReturnReason = NormalizeVietnameseDiacritics(request.ReturnReason ?? request.ReasonCode ?? "Khách hoàn hàng (RMA)"),
                Status = "QUARANTINED",
                CreatedBy = effectiveActorId,
                CreatedAtUtc = now
            };
            db.CustomerReturns.Add(customerReturn);
            await db.SaveChangesAsync(cancellationToken);

            foreach (var line in resolvedLines)
            {
                var qStock = await db.InventoryStocks.FirstOrDefaultAsync(
                    s => s.WarehouseId == targetWhId &&
                         s.ProductId == line.ProductId &&
                         s.LocationId == line.QuarantineLocId &&
                         s.LotId == line.LotId &&
                         s.SerialId == line.SerialId &&
                         s.Status == ERP.Domain.Enums.InventoryStatus.Quarantine,
                    cancellationToken);

                if (qStock == null)
                {
                    qStock = new ERP.Domain.Entities.InventoryStock
                    {
                        WarehouseId = targetWhId,
                        ProductId = line.ProductId,
                        LocationId = line.QuarantineLocId,
                        LotId = line.LotId,
                        SerialId = line.SerialId,
                        Status = ERP.Domain.Enums.InventoryStatus.Quarantine,
                        Quantity = line.Quantity,
                        ReservedQuantity = 0m,
                        MovingAverageCost = line.UnitCost,
                        LastUpdated = now
                    };
                    db.InventoryStocks.Add(qStock);
                }
                else
                {
                    qStock.Quantity += line.Quantity;
                    qStock.LastUpdated = now;
                }

                var intakeTx = new ERP.Domain.Entities.InventoryTransaction
                {
                    ProductId = line.ProductId,
                    WarehouseId = targetWhId,
                    LocationId = line.QuarantineLocId,
                    ToLocationId = line.QuarantineLocId,
                    LotId = line.LotId,
                    SerialId = line.SerialId,
                    InventoryStatus = ERP.Domain.Enums.InventoryStatus.Quarantine,
                    ToInventoryStatus = ERP.Domain.Enums.InventoryStatus.Quarantine,
                    TransactionType = ERP.Domain.Enums.TransactionType.Import,
                    Quantity = line.Quantity,
                    UnitCost = line.UnitCost,
                    ReferenceId = customerReturn.Id,
                    ReferenceType = "CustomerReturn",
                    TransactionDate = now,
                    CreatedBy = effectiveActorId,
                    Note = $"{customerReturn.ReturnCode}: Tiếp nhận hàng hoàn trả vào khu biệt trữ"
                };
                db.InventoryTransactions.Add(intakeTx);

                customerReturn.Lines.Add(new ERP.Domain.Entities.CustomerReturnLine
                {
                    CustomerReturnId = customerReturn.Id,
                    ProductId = line.ProductId,
                    LotId = line.LotId,
                    SerialId = line.SerialId,
                    ExpiryDate = line.ExpiryDate,
                    BaseUomId = line.BaseUomId,
                    BaseUomCode = line.BaseUomCode,
                    TransactionUomCode = line.TransactionUomCode,
                    ConversionRateSnapshot = line.ConversionRate,
                    QuarantineLocationId = line.QuarantineLocId,
                    TargetLocationId = line.QuarantineLocId,
                    Quantity = line.Quantity,
                    UnitCost = line.UnitCost,
                    Condition = line.Condition,
                    Disposition = "PENDING"
                });
            }

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = targetWhId,
                EntityName = "CustomerReturn",
                EntityId = customerReturn.Id,
                Action = "CustomerReturn.Intake",
                OldValues = "NONE",
                NewValues = $"Status: QUARANTINED; ReturnCode: {customerReturn.ReturnCode}",
                Reason = customerReturn.ReturnReason,
                Result = "Success",
                Timestamp = now
            });

            await db.SaveChangesAsync(cancellationToken);
            if (tx != null) await tx.CommitAsync(cancellationToken);

            return await GetCustomerReturnByIdAsync(customerReturn.Id, canReadCost, effectiveAuth, cancellationToken);
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
