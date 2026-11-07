using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ImportReceiptOperationsService
{
    private async Task<WmsEndpointOutcome?> ExecuteCustomerReturnLineDispositionsAsync(
        ERP.Domain.Entities.CustomerReturn customerReturn,
        CustomerReturnDisposeInput? request,
        int? topLevelRestockLocId,
        DateTime now,
        int effectiveActorId,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx,
        CancellationToken cancellationToken)
    {
                foreach (var line in customerReturn.Lines.ToList())
                {
                    var qLocId = line.QuarantineLocationId ?? customerReturn.QuarantineLocationId;
                    var quarantineStock = await db.InventoryStocks.FirstOrDefaultAsync(
                        s => s.WarehouseId == customerReturn.WarehouseId &&
                             s.ProductId == line.ProductId &&
                             (!qLocId.HasValue || s.LocationId == qLocId.Value) &&
                             s.LotId == line.LotId &&
                             s.SerialId == line.SerialId &&
                             s.Status == ERP.Domain.Enums.InventoryStatus.Quarantine,
                        cancellationToken)
                        ?? await db.InventoryStocks.FirstOrDefaultAsync(
                            s => s.WarehouseId == customerReturn.WarehouseId &&
                                 s.ProductId == line.ProductId &&
                                 s.Status == ERP.Domain.Enums.InventoryStatus.Quarantine &&
                                 s.Quantity >= line.Quantity,
                            cancellationToken);

                    if (quarantineStock == null || quarantineStock.Quantity < line.Quantity)
                    {
                        if (tx != null) await tx.RollbackAsync(cancellationToken);
                        return WmsEndpointOutcome.UnprocessableEntity(new
                        {
                            code = "INSUFFICIENT_QUARANTINE_STOCK",
                            errorCode = "INSUFFICIENT_QUARANTINE_STOCK",
                            message = $"Không đủ tồn kho biệt trữ (Quarantine) cho sản phẩm #{line.ProductId} (Cần: {line.Quantity}, Hiện có: {quarantineStock?.Quantity ?? 0m})."
                        });
                    }

                    quarantineStock.Quantity -= line.Quantity;
                    quarantineStock.LastUpdated = now;

                    var disp = NormalizeDispositionCode(line.Disposition);
                    line.Disposition = disp;

                    if (disp is "RESTOCK_AVAILABLE" or "REFURBISH_REPACK")
                    {
                        var pkgId = line.PackagingProductId ?? request?.PackagingProductId;
                        var pkgQty = line.PackagingQuantity > 0m ? line.PackagingQuantity : (request?.PackagingQuantity ?? 0m);
                        if (disp == "REFURBISH_REPACK" && pkgId.HasValue && pkgQty > 0m)
                        {
                            var pkgStock = await db.InventoryStocks
                                .Where(s => s.WarehouseId == customerReturn.WarehouseId &&
                                            s.ProductId == pkgId.Value &&
                                            s.Status == ERP.Domain.Enums.InventoryStatus.Available &&
                                            s.Quantity - s.ReservedQuantity >= pkgQty)
                                .OrderByDescending(s => s.Quantity - s.ReservedQuantity)
                                .FirstOrDefaultAsync(cancellationToken);

                            if (pkgStock == null)
                            {
                                if (tx != null) await tx.RollbackAsync(cancellationToken);
                                return WmsEndpointOutcome.UnprocessableEntity(new
                                {
                                    code = "INSUFFICIENT_PACKAGING_STOCK",
                                    errorCode = "INSUFFICIENT_PACKAGING_STOCK",
                                    message = $"Không đủ tồn kho bao bì (#{pkgId.Value}) để đóng gói tân trang."
                                });
                            }

                            pkgStock.Quantity -= pkgQty;
                            pkgStock.LastUpdated = now;
                            line.PackagingConsumedQty = pkgQty;

                            db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
                            {
                                ProductId = pkgId.Value,
                                WarehouseId = customerReturn.WarehouseId,
                                LocationId = pkgStock.LocationId,
                                FromLocationId = pkgStock.LocationId,
                                InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                                FromInventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                                TransactionType = ERP.Domain.Enums.TransactionType.AdjustmentDecrease,
                                Quantity = pkgQty,
                                ReferenceId = customerReturn.Id,
                                ReferenceType = "CustomerReturnRefurbishPackaging",
                                TransactionDate = now,
                                CreatedBy = effectiveActorId,
                                Note = $"{customerReturn.ReturnCode}: Tiêu hao vật tư bao bì tân trang"
                            });
                        }

                        var restockLocId = line.RestockLocationId
                            ?? line.TargetLocationId
                            ?? topLevelRestockLocId;
                        if (!restockLocId.HasValue || restockLocId == quarantineStock.LocationId)
                        {
                            var preferredStorageLocId = await db.WarehouseLocations.AsNoTracking()
                                .Where(l => l.WarehouseId == customerReturn.WarehouseId &&
                                            l.IsActive &&
                                            !l.IsBlocked &&
                                            l.Id != quarantineStock.LocationId)
                                .OrderBy(l => l.LocationType == ERP.Domain.Enums.WarehouseLocationType.Storage && l.IsPickable ? 0 : 1)
                                .ThenBy(l => l.Id)
                                .Select(l => (int?)l.Id)
                                .FirstOrDefaultAsync(cancellationToken);
                            restockLocId ??= preferredStorageLocId ?? quarantineStock.LocationId;
                        }

                        line.RestockLocationId = restockLocId;
                        line.TargetLocationId = restockLocId;

                        var availableStock = await db.InventoryStocks.FirstOrDefaultAsync(
                            s => s.WarehouseId == customerReturn.WarehouseId &&
                                 s.ProductId == line.ProductId &&
                                 s.LocationId == restockLocId &&
                                 s.LotId == line.LotId &&
                                 s.SerialId == line.SerialId &&
                                 s.Status == ERP.Domain.Enums.InventoryStatus.Available,
                            cancellationToken);

                        if (availableStock == null)
                        {
                            availableStock = new ERP.Domain.Entities.InventoryStock
                            {
                                WarehouseId = customerReturn.WarehouseId,
                                ProductId = line.ProductId,
                                LocationId = restockLocId,
                                LotId = line.LotId,
                                SerialId = line.SerialId,
                                Status = ERP.Domain.Enums.InventoryStatus.Available,
                                Quantity = line.Quantity,
                                ReservedQuantity = 0m,
                                MovingAverageCost = line.UnitCost,
                                LastUpdated = now
                            };
                            db.InventoryStocks.Add(availableStock);
                        }
                        else
                        {
                            availableStock.Quantity += line.Quantity;
                            availableStock.LastUpdated = now;
                        }

                        var txOut = new ERP.Domain.Entities.InventoryTransaction
                        {
                            ProductId = line.ProductId,
                            WarehouseId = customerReturn.WarehouseId,
                            LocationId = quarantineStock.LocationId,
                            FromLocationId = quarantineStock.LocationId,
                            ToLocationId = restockLocId,
                            LotId = line.LotId,
                            SerialId = line.SerialId,
                            InventoryStatus = ERP.Domain.Enums.InventoryStatus.Quarantine,
                            FromInventoryStatus = ERP.Domain.Enums.InventoryStatus.Quarantine,
                            ToInventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                            TransactionType = ERP.Domain.Enums.TransactionType.StatusChange,
                            Quantity = -line.Quantity,
                            UnitCost = line.UnitCost,
                            ReferenceId = customerReturn.Id,
                            ReferenceType = "CustomerReturn",
                            TransactionDate = now,
                            CreatedBy = effectiveActorId,
                            Note = $"{customerReturn.ReturnCode}: Xuất khỏi khu biệt trữ chuyển về tồn khả dụng ({disp})"
                        };
                        var txIn = new ERP.Domain.Entities.InventoryTransaction
                        {
                            ProductId = line.ProductId,
                            WarehouseId = customerReturn.WarehouseId,
                            LocationId = restockLocId,
                            FromLocationId = quarantineStock.LocationId,
                            ToLocationId = restockLocId,
                            LotId = line.LotId,
                            SerialId = line.SerialId,
                            InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                            FromInventoryStatus = ERP.Domain.Enums.InventoryStatus.Quarantine,
                            ToInventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                            TransactionType = ERP.Domain.Enums.TransactionType.StatusChange,
                            Quantity = line.Quantity,
                            UnitCost = line.UnitCost,
                            ReferenceId = customerReturn.Id,
                            ReferenceType = "CustomerReturn",
                            TransactionDate = now,
                            CreatedBy = effectiveActorId,
                            Note = $"{customerReturn.ReturnCode}: Nhập lại kệ hàng khả dụng ({disp})"
                        };
                        db.InventoryTransactions.Add(txOut);
                        db.InventoryTransactions.Add(txIn);
                        await db.SaveChangesAsync(cancellationToken);
                        line.PostedLedgerTransactionId = txIn.Id;
                    }
                    else
                    {
                        var writeOffTx = new ERP.Domain.Entities.InventoryTransaction
                        {
                            ProductId = line.ProductId,
                            WarehouseId = customerReturn.WarehouseId,
                            LocationId = quarantineStock.LocationId,
                            FromLocationId = quarantineStock.LocationId,
                            LotId = line.LotId,
                            SerialId = line.SerialId,
                            InventoryStatus = ERP.Domain.Enums.InventoryStatus.Quarantine,
                            FromInventoryStatus = ERP.Domain.Enums.InventoryStatus.Quarantine,
                            TransactionType = ERP.Domain.Enums.TransactionType.AdjustmentDecrease,
                            Quantity = line.Quantity,
                            UnitCost = line.UnitCost,
                            ReferenceId = customerReturn.Id,
                            ReferenceType = disp == "RTV_VENDOR" ? "QualityDispositionRtv" : "QualityDispositionScrap",
                            TransactionDate = now,
                            CreatedBy = effectiveActorId,
                            Note = $"{customerReturn.ReturnCode}: Xuất toán khỏi khu biệt trữ ({disp})"
                        };
                        db.InventoryTransactions.Add(writeOffTx);
                        await db.SaveChangesAsync(cancellationToken);
                        line.PostedLedgerTransactionId = writeOffTx.Id;
                    }
                }
        return null;
    }

    private async Task ApplyCustomerReturnDebtAdjustmentsAsync(
        ERP.Domain.Entities.CustomerReturn customerReturn,
        DateTime now,
        int effectiveActorId,
        CancellationToken cancellationToken)
    {
                var arAdjustmentAmount = decimal.Round(
                    customerReturn.Lines
                        .Where(l => l.Disposition is "RESTOCK_AVAILABLE" or "REFURBISH_REPACK")
                        .Sum(l => l.Quantity * l.UnitCost),
                    2,
                    MidpointRounding.AwayFromZero);
                if (arAdjustmentAmount > 0m && (customerReturn.ExportReceiptId.HasValue || customerReturn.SalesOrderId.HasValue))
                {
                    var arInvoice = customerReturn.ExportReceiptId.HasValue
                        ? await db.PartnerDebtInvoices.FirstOrDefaultAsync(
                            i => i.DebtType == ERP.Domain.Enums.PartnerDebtType.AR &&
                                 i.SourceType == "ExportReceipt" &&
                                 i.SourceId == customerReturn.ExportReceiptId.Value,
                            cancellationToken)
                        : null;
                    var customerId = arInvoice?.PartnerId;
                    if (!customerId.HasValue && customerReturn.ExportReceiptId.HasValue)
                    {
                        customerId = await db.ExportReceipts.AsNoTracking()
                            .Where(r => r.Id == customerReturn.ExportReceiptId.Value)
                            .Select(r => r.CustomerId)
                            .FirstOrDefaultAsync(cancellationToken);
                    }
                    if (!customerId.HasValue && customerReturn.SalesOrderId.HasValue)
                    {
                        customerId = await db.SalesOrders.AsNoTracking()
                            .Where(o => o.Id == customerReturn.SalesOrderId.Value)
                            .Select(o => (int?)o.CustomerId)
                            .FirstOrDefaultAsync(cancellationToken);
                    }

                    if (customerId is > 0)
                    {
                        await db.AcquireBusinessPartnerLockAsync(customerId.Value, cancellationToken);
                        var customer = await db.BusinessPartners.FirstOrDefaultAsync(p => p.Id == customerId.Value, cancellationToken);
                        if (customer != null)
                        {
                            customer.ReceivableBalance = Math.Max(0m, decimal.Round(customer.ReceivableBalance - arAdjustmentAmount, 2, MidpointRounding.AwayFromZero));
                            customer.UpdatedAt = now;
                            if (arInvoice != null && arInvoice.RemainingAmount > 0m)
                            {
                                arInvoice.RemainingAmount = Math.Max(0m, decimal.Round(arInvoice.RemainingAmount - arAdjustmentAmount, 2, MidpointRounding.AwayFromZero));
                                if (arInvoice.RemainingAmount == 0m)
                                    arInvoice.Status = ERP.Domain.Enums.PartnerDebtInvoiceStatus.Paid;
                                arInvoice.UpdatedAtUtc = now;
                            }

                            db.PartnerDebtLedgers.Add(new ERP.Domain.Entities.PartnerDebtLedger
                            {
                                PartnerId = customer.Id,
                                InvoiceId = arInvoice?.Id ?? 0,
                                DebtType = ERP.Domain.Enums.PartnerDebtType.AR,
                                TransactionType = ERP.Domain.Enums.PartnerDebtTransactionType.Adjustment,
                                AmountDelta = -arAdjustmentAmount,
                                BalanceAfter = customer.ReceivableBalance,
                                ReferenceType = "CustomerReturn",
                                ReferenceId = customerReturn.Id,
                                ReferenceCode = customerReturn.ReturnCode,
                                Note = $"Giảm trừ công nợ phải thu (AR Credit Note) từ hoàn hàng RMA {customerReturn.ReturnCode}",
                                CreatedBy = effectiveActorId,
                                CreatedAtUtc = now
                            });
                        }
                    }
                }

                var apRtvAmount = decimal.Round(
                    customerReturn.Lines
                        .Where(l => l.Disposition == "RTV_VENDOR")
                        .Sum(l => l.Quantity * l.UnitCost),
                    2,
                    MidpointRounding.AwayFromZero);
                if (apRtvAmount > 0m)
                {
                    var rtvProductIds = customerReturn.Lines.Where(l => l.Disposition == "RTV_VENDOR").Select(l => l.ProductId).Distinct().ToList();
                    var apInvoice = await db.PartnerDebtInvoices
                        .OrderByDescending(i => i.Id)
                        .FirstOrDefaultAsync(i => i.DebtType == ERP.Domain.Enums.PartnerDebtType.AP && i.RemainingAmount > 0m, cancellationToken);
                    var supplierId = apInvoice?.PartnerId ?? await db.ImportReceipts.AsNoTracking()
                        .Where(r => r.SupplierId.HasValue && r.Details.Any(d => rtvProductIds.Contains(d.ProductId)))
                        .OrderByDescending(r => r.Id)
                        .Select(r => r.SupplierId)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (supplierId is > 0)
                    {
                        await db.AcquireBusinessPartnerLockAsync(supplierId.Value, cancellationToken);
                        var supplier = await db.BusinessPartners.FirstOrDefaultAsync(p => p.Id == supplierId.Value, cancellationToken);
                        if (supplier != null)
                        {
                            supplier.PayableBalance = Math.Max(0m, decimal.Round(supplier.PayableBalance - apRtvAmount, 2, MidpointRounding.AwayFromZero));
                            supplier.UpdatedAt = now;
                            if (apInvoice != null && apInvoice.PartnerId == supplier.Id && apInvoice.RemainingAmount > 0m)
                            {
                                apInvoice.RemainingAmount = Math.Max(0m, decimal.Round(apInvoice.RemainingAmount - apRtvAmount, 2, MidpointRounding.AwayFromZero));
                                if (apInvoice.RemainingAmount == 0m)
                                    apInvoice.Status = ERP.Domain.Enums.PartnerDebtInvoiceStatus.Paid;
                                apInvoice.UpdatedAtUtc = now;
                            }

                            db.PartnerDebtLedgers.Add(new ERP.Domain.Entities.PartnerDebtLedger
                            {
                                PartnerId = supplier.Id,
                                InvoiceId = apInvoice != null && apInvoice.PartnerId == supplier.Id ? apInvoice.Id : 0,
                                DebtType = ERP.Domain.Enums.PartnerDebtType.AP,
                                TransactionType = ERP.Domain.Enums.PartnerDebtTransactionType.Adjustment,
                                AmountDelta = -apRtvAmount,
                                BalanceAfter = supplier.PayableBalance,
                                ReferenceType = "CustomerReturnRtv",
                                ReferenceId = customerReturn.Id,
                                ReferenceCode = customerReturn.ReturnCode,
                                Note = $"Giảm trừ công nợ phải trả nhà cung cấp (AP Debit Note RTV) từ {customerReturn.ReturnCode}",
                                CreatedBy = effectiveActorId,
                                CreatedAtUtc = now
                            });
                        }
                    }
                }
    }
}
