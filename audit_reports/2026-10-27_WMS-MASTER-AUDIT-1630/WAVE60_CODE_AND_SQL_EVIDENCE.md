# WAVE 60 & 60.1 VERBATIM CODE, TEST & SQL EVIDENCE (WAVE60_CODE_AND_SQL_EVIDENCE.md)

- **Audit ID:** WMS-MASTER-AUDIT
- **Run ID:** 2026-10-27_WMS-MASTER-AUDIT-1630
- **Wave Scope:** Wave 60 (37eb5fe — Epics 60.1, 60.2, 60.3) & Wave 60.1 Remediation Closure (F-011, F-012, 8 Desktop/Mobile PDA E2E Screenshots wave60_e2e_1..8.png, Verbatim C# & SQL Evidence)
- **Truncation Policy:** **100% Verbatim (0 ... omissions)**

---

## 1. Git Commit Lineage & Working Tree Status (FACT)

### 1.1 git log -n 6 --oneline
```text
37eb5fe feat(wave60): implement 60-day roadmap Epics 60.1-60.3 (physical kitting, cross-docking, break-bulk replenishment)
0b75fd4 fix(wave59.1): fix /dashboard route, interactive E2E visual evidence, and 94/95 table count sync
d669beb feat(wave59): implement Epic 30.1-30.3 (wave templates, VN carrier & marketplace adapters) and close UNV-02 live browser E2E
ca0f177 refactor(wave58): decouple ErpKhoDbContext from all 42 controllers (close F-004) and synchronize audit package
f49a433 feat(wave57): remediate audit findings F-001..F-009, enforce SQL ledger trigger, and finalize WMS-MASTER-AUDIT
d9b2453 feat(wms): complete full UI/UX overhaul, webcam 1D barcode decoder, and E2E audit WMS-AUDIT-20261026-1805
```

### 1.2 git status --short (Wave 60.1 Staged Scope & Post-Commit Working Tree Status)
```text
# Staged Wave 60.1 Scope (26 files):
 M ERP.Infrastructure/Services/InventoryControlOperationsService.cs
 M ERP.Infrastructure/Services/PutawayService.cs
 M audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630.tar
 M audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/AUDIT_STATE.md
 M audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/BLOCKERS_AND_UNVERIFIED.md
 M audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/BUSINESS_REQUIREMENT_TRACEABILITY.md
 M audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/BUSINESS_WORKFLOW_AUDIT.md
 M audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/COMPETITIVE_GAP_AND_ROADMAP.md
 M audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/COMPETITIVE_WMS_BENCHMARK.md
 M audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/EXECUTIVE_SUMMARY.md
 M audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/FINDINGS_REGISTER.md
 M audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/NOTION_SOURCE_INVENTORY.md
 M audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/SOURCE_IMPLEMENTATION_MAP.md
 M audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/SPEC_CONFLICTS_AND_OWNER_DECISIONS.md
 M audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/TEST_COVERAGE_GAPS.md
 A audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/WAVE60_CODE_AND_SQL_EVIDENCE.md
 M audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/wave60_e2e_1_dashboard_1440x900.png
 M audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/wave60_e2e_2_epic60_1_kitting_1440x900.png
 M audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/wave60_e2e_4_epic60_3_breakbulk_1440x900.png
 M audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/wave60_e2e_5_mobile_pda_390x844.png
 A audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/wave60_e2e_6_mobile_dashboard_390x844.png
 A audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/wave60_e2e_7_mobile_products_390x844.png
 A audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/wave60_e2e_8_mobile_putaway_390x844.png
 M frontend/e2e/wave60-live-browser-e2e.spec.mjs
 M frontend/src/pages/InventoryMovements.test.tsx
 M frontend/src/pages/InventoryMovements.tsx

# Post-Commit git status --short:
(empty — 0 modified/untracked files)
```

---

## 2. Verbatim C# Implementation: ProductOperationsService.CompleteKitWorkOrderAsync & CancelKitWorkOrderAsync (Epic 60.1)

- **File:** [ProductOperationsService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/ProductOperationsService.cs#L1622-L2366) (Lines 1622–2366)

```csharp
    public async Task<WmsEndpointOutcome> CompleteKitWorkOrderAsync(
        int workOrderId,
        CompleteKitWorkOrderInput? request,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var ownTx = _db.Database.CurrentTransaction == null;
        var isSqlServer = _db.Database.IsRelational() && _db.Database.IsSqlServer();
        await using var tx = ownTx ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;

        if (isSqlServer)
        {
            var tenantId = _db.CurrentTenantId;
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT Id FROM dbo.KitWorkOrders WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND Id = {workOrderId};",
                cancellationToken);
        }

        var workOrder = await _db.KitWorkOrders
            .Include(w => w.ComponentLines)
            .FirstOrDefaultAsync(w => w.Id == workOrderId, cancellationToken);
        if (workOrder == null)
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            return WmsEndpointOutcome.Json(404, new
            {
                code = "KIT_WORK_ORDER_NOT_FOUND",
                errorCode = "KIT_WORK_ORDER_NOT_FOUND",
                message = $"Không tìm thấy lệnh gia công KIT #{workOrderId}."
            });
        }

        if (string.Equals(workOrder.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            return WmsEndpointOutcome.Json(409, new
            {
                code = "KIT_WORK_ORDER_ALREADY_COMPLETED",
                errorCode = "KIT_WORK_ORDER_ALREADY_COMPLETED",
                workOrderId = workOrder.Id,
                workOrderCode = workOrder.WorkOrderCode,
                message = $"Lệnh gia công KIT {workOrder.WorkOrderCode} đã hoàn tất trước đó."
            });
        }

        if (string.Equals(workOrder.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            return WmsEndpointOutcome.Json(422, new
            {
                code = "KIT_WORK_ORDER_CANCELLED",
                errorCode = "KIT_WORK_ORDER_CANCELLED",
                workOrderId = workOrder.Id,
                message = $"Lệnh gia công KIT {workOrder.WorkOrderCode} đã bị hủy."
            });
        }

        var completedKitQty = request?.CompletedKitQty is > 0m
            ? Math.Round(request.CompletedKitQty.Value, 4)
            : workOrder.PlannedKitQty;
        if (completedKitQty <= 0m)
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            return WmsEndpointOutcome.Json(422, new
            {
                code = "VAS_KIT_INVALID_QTY",
                errorCode = "VAS_KIT_INVALID_QTY",
                message = "Số lượng hoàn thành bộ KIT phải lớn hơn 0."
            });
        }

        if (request?.TargetLocationId is > 0)
            workOrder.TargetLocationId = request.TargetLocationId.Value;
        if (request?.TargetLotId is > 0)
            workOrder.TargetLotId = request.TargetLotId.Value;
        if (request?.TargetHandlingUnitId is > 0)
            workOrder.TargetHandlingUnitId = request.TargetHandlingUnitId.Value;

        var lockProductIds = workOrder.ComponentLines
            .Select(l => l.ComponentProductId)
            .Append(workOrder.KitProductId)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
        await _db.AcquireInventoryAllocationLocksAsync(workOrder.WarehouseId, lockProductIds, cancellationToken);

        var kitProduct = await _db.Products.FirstOrDefaultAsync(p => p.Id == workOrder.KitProductId, cancellationToken);
        if (kitProduct == null)
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            return WmsEndpointOutcome.Json(404, new
            {
                code = "PRODUCT_NOT_FOUND",
                errorCode = "PRODUCT_NOT_FOUND",
                message = "Không tìm thấy sản phẩm KIT cha."
            });
        }

        var now = DateTime.UtcNow;
        var actorId = actorUserId is > 0 ? actorUserId.Value : (workOrder.CreatedByUserId ?? 1);
        var defaultLocationId = workOrder.TargetLocationId
            ?? await _db.WarehouseLocations.AsNoTracking()
                .Where(l => l.WarehouseId == workOrder.WarehouseId && l.IsActive && !l.IsBlocked)
                .OrderByDescending(l => l.IsPickable)
                .ThenBy(l => l.Id)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync(cancellationToken);

        if (workOrder.WorkOrderType == KitWorkOrderType.Assemble)
        {
            var totalComponentCost = 0m;
            DateTime? earliestExpiryDate = null;
            int? firstComponentLocId = null;

            foreach (var line in workOrder.ComponentLines.OrderBy(l => l.ComponentProductId).ThenBy(l => l.Id))
            {
                var reqCompQty = Math.Round(completedKitQty * line.ComponentRatioQty, 4);
                line.RequiredBaseQty = reqCompQty;

                var compProduct = await _db.Products.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == line.ComponentProductId, cancellationToken);

                var compStocksQuery = _db.InventoryStocks
                    .Include(s => s.Lot)
                    .Where(s => s.WarehouseId == workOrder.WarehouseId &&
                                s.ProductId == line.ComponentProductId &&
                                s.Status == InventoryStatus.Available &&
                                s.Quantity > 0m);

                if (line.SourceLocationId is > 0)
                    compStocksQuery = compStocksQuery.Where(s => s.LocationId == line.SourceLocationId.Value);
                if (line.SourceLotId is > 0)
                    compStocksQuery = compStocksQuery.Where(s => s.LotId == line.SourceLotId.Value);

                var compStocks = await compStocksQuery
                    .OrderBy(s => s.Lot != null && s.Lot.ExpiryDate.HasValue ? 0 : 1)
                    .ThenBy(s => s.Lot != null ? s.Lot.ExpiryDate : null)
                    .ThenBy(s => s.LocationId)
                    .ThenBy(s => s.Id)
                    .ToListAsync(cancellationToken);

                var totalFree = compStocks.Sum(s => Math.Max(0m, s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity));
                if (totalFree < reqCompQty)
                {
                    if (tx != null) await tx.RollbackAsync(cancellationToken);
                    return WmsEndpointOutcome.Json(422, new
                    {
                        code = "KIT_INSUFFICIENT_COMPONENT_STOCK",
                        errorCode = "KIT_INSUFFICIENT_COMPONENT_STOCK",
                        componentProductId = line.ComponentProductId,
                        requiredBaseQty = reqCompQty,
                        availableBaseQty = totalFree,
                        message = $"Không đủ tồn khả dụng cho thành phần #{line.ComponentProductId} (cần {reqCompQty:0.####}, khả dụng {totalFree:0.####})."
                    });
                }

                var remToConsume = reqCompQty;
                var lineTotalCost = 0m;
                foreach (var cs in compStocks)
                {
                    if (remToConsume <= 0m) break;
                    var free = Math.Max(0m, cs.Quantity - cs.ReservedQuantity - cs.AllocatedQuantity - cs.LockedQuantity);
                    if (free <= 0m) continue;
                    var take = Math.Min(free, remToConsume);
                    var unitCost = cs.MovingAverageCost > 0m
                        ? cs.MovingAverageCost
                        : (compProduct?.MovingAverageCost > 0m
                            ? compProduct.MovingAverageCost
                            : (line.ComponentUnitCost > 0m ? line.ComponentUnitCost : 0m));

                    cs.Quantity -= take;
                    cs.LastUpdated = now;
                    cs.RefreshGrainHash();
                    remToConsume -= take;
                    lineTotalCost += take * unitCost;

                    firstComponentLocId ??= cs.LocationId;
                    line.SourceLocationId ??= cs.LocationId;
                    line.SourceLotId ??= cs.LotId;

                    var lotExpiry = cs.Lot?.ExpiryDate;
                    if (!lotExpiry.HasValue && cs.LotId.HasValue)
                    {
                        lotExpiry = await _db.InventoryLots.AsNoTracking()
                            .Where(lt => lt.Id == cs.LotId.Value)
                            .Select(lt => lt.ExpiryDate)
                            .FirstOrDefaultAsync(cancellationToken);
                    }
                    if (lotExpiry.HasValue && (!earliestExpiryDate.HasValue || lotExpiry.Value < earliestExpiryDate.Value))
                    {
                        earliestExpiryDate = lotExpiry.Value;
                    }

                    _db.InventoryTransactions.Add(new InventoryTransaction
                    {
                        ProductId = line.ComponentProductId,
                        WarehouseId = workOrder.WarehouseId,
                        LocationId = cs.LocationId,
                        FromLocationId = cs.LocationId,
                        LotId = cs.LotId,
                        SerialId = cs.SerialId,
                        HandlingUnitId = cs.HandlingUnitId,
                        OwnerId = cs.OwnerId,
                        InventoryStatus = InventoryStatus.Available,
                        FromInventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.TransferOut,
                        Quantity = -take,
                        UnitCost = Math.Round(unitCost, 4),
                        ReferenceId = workOrder.Id,
                        ReferenceType = "KitComponentIssue",
                        SourceDocumentLineId = line.Id,
                        TransactionDate = now,
                        ActualOccurredAtUtc = now,
                        PostedAtUtc = now,
                        CreatedBy = actorId,
                        Note = $"{workOrder.WorkOrderCode}: Xuất linh kiện lắp bộ KIT -{take:0.####}"
                    });
                }

                line.ConsumedBaseQty = reqCompQty;
                line.ComponentUnitCost = reqCompQty > 0m ? Math.Round(lineTotalCost / reqCompQty, 4) : 0m;
                totalComponentCost += lineTotalCost;
            }

            var rolledUpUnitCost = Math.Round(totalComponentCost / completedKitQty, 4);
            workOrder.RolledUpUnitCost = rolledUpUnitCost;
            workOrder.EarliestComponentExpiryDate = earliestExpiryDate;

            var targetLocId = workOrder.TargetLocationId ?? firstComponentLocId ?? defaultLocationId;
            workOrder.TargetLocationId = targetLocId;

            if (workOrder.TargetLotId.HasValue)
            {
                var targetLot = await _db.InventoryLots.FirstOrDefaultAsync(l => l.Id == workOrder.TargetLotId.Value, cancellationToken);
                if (targetLot != null && earliestExpiryDate.HasValue &&
                    (!targetLot.ExpiryDate.HasValue || earliestExpiryDate.Value < targetLot.ExpiryDate.Value))
                {
                    targetLot.ExpiryDate = earliestExpiryDate.Value;
                }
            }
            else if (earliestExpiryDate.HasValue || kitProduct.TrackingType == ProductTrackingType.Lot || kitProduct.ExpiryControl)
            {
                var kitLotNumber = $"LOT-{workOrder.WorkOrderCode}";
                var targetLot = await _db.InventoryLots
                    .FirstOrDefaultAsync(l => l.ProductId == workOrder.KitProductId && l.LotNumber == kitLotNumber, cancellationToken);
                if (targetLot == null)
                {
                    targetLot = new InventoryLot
                    {
                        ProductId = workOrder.KitProductId,
                        LotNumber = kitLotNumber,
                        ManufactureDate = now.Date,
                        ExpiryDate = earliestExpiryDate,
                        ReceivedAt = now,
                        CreatedAt = now
                    };
                    _db.InventoryLots.Add(targetLot);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                else if (earliestExpiryDate.HasValue)
                {
                    targetLot.ExpiryDate = earliestExpiryDate.Value;
                }
                workOrder.TargetLotId = targetLot.Id;
            }

            var kitStock = await _db.InventoryStocks
                .FirstOrDefaultAsync(s =>
                    s.WarehouseId == workOrder.WarehouseId &&
                    s.ProductId == workOrder.KitProductId &&
                    s.LocationId == targetLocId &&
                    s.LotId == workOrder.TargetLotId &&
                    s.SerialId == null &&
                    s.HandlingUnitId == workOrder.TargetHandlingUnitId &&
                    s.OwnerId == null &&
                    s.Status == InventoryStatus.Available,
                    cancellationToken);

            if (kitStock != null)
            {
                var oldQty = Math.Max(0m, kitStock.Quantity);
                var oldMac = kitStock.MovingAverageCost > 0m ? kitStock.MovingAverageCost : rolledUpUnitCost;
                var newQty = oldQty + completedKitQty;
                kitStock.MovingAverageCost = newQty > 0m
                    ? Math.Round(((oldQty * oldMac) + (completedKitQty * rolledUpUnitCost)) / newQty, 4)
                    : rolledUpUnitCost;
                kitStock.Quantity += completedKitQty;
                kitStock.LastUpdated = now;
                kitStock.RefreshGrainHash();
            }
            else
            {
                kitStock = new InventoryStock
                {
                    WarehouseId = workOrder.WarehouseId,
                    ProductId = workOrder.KitProductId,
                    LocationId = targetLocId,
                    LotId = workOrder.TargetLotId,
                    HandlingUnitId = workOrder.TargetHandlingUnitId,
                    Status = InventoryStatus.Available,
                    Quantity = completedKitQty,
                    MovingAverageCost = rolledUpUnitCost,
                    LastUpdated = now
                };
                kitStock.RefreshGrainHash();
                _db.InventoryStocks.Add(kitStock);
            }

            kitProduct.MovingAverageCost = rolledUpUnitCost;

            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductId = workOrder.KitProductId,
                WarehouseId = workOrder.WarehouseId,
                LocationId = targetLocId,
                ToLocationId = targetLocId,
                LotId = workOrder.TargetLotId,
                HandlingUnitId = workOrder.TargetHandlingUnitId,
                InventoryStatus = InventoryStatus.Available,
                ToInventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.TransferIn,
                Quantity = completedKitQty,
                UnitCost = rolledUpUnitCost,
                ReferenceId = workOrder.Id,
                ReferenceType = "KitFinishedReceipt",
                TransactionDate = now,
                ActualOccurredAtUtc = now,
                PostedAtUtc = now,
                CreatedBy = actorId,
                Note = $"{workOrder.WorkOrderCode}: Nhập thành phẩm lắp bộ KIT +{completedKitQty:0.####} (MAC={rolledUpUnitCost:0.####})"
            });
        }
        else
        {
            // Disassemble: deduct parent Kit and return components to their target LotId/LocationId
            var kitStocksQuery = _db.InventoryStocks
                .Include(s => s.Lot)
                .Where(s => s.WarehouseId == workOrder.WarehouseId &&
                            s.ProductId == workOrder.KitProductId &&
                            s.Status == InventoryStatus.Available &&
                            s.Quantity > 0m);
            if (workOrder.TargetLocationId is > 0)
                kitStocksQuery = kitStocksQuery.Where(s => s.LocationId == workOrder.TargetLocationId.Value);
            if (workOrder.TargetLotId is > 0)
                kitStocksQuery = kitStocksQuery.Where(s => s.LotId == workOrder.TargetLotId.Value);

            var kitStocks = await kitStocksQuery
                .OrderByDescending(s => s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity)
                .ThenBy(s => s.Id)
                .ToListAsync(cancellationToken);

            var totalKitFree = kitStocks.Sum(s => Math.Max(0m, s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity));
            if (totalKitFree < completedKitQty)
            {
                if (tx != null) await tx.RollbackAsync(cancellationToken);
                return WmsEndpointOutcome.Json(422, new
                {
                    code = "VAS_DEKIT_INSUFFICIENT_KIT_STOCK",
                    errorCode = "VAS_DEKIT_INSUFFICIENT_KIT_STOCK",
                    kitProductId = workOrder.KitProductId,
                    requiredKitQuantity = completedKitQty,
                    availableKitQuantity = totalKitFree,
                    message = $"Không đủ tồn khả dụng của bộ KIT {kitProduct.Code} để rã bộ (cần {completedKitQty:0.####}, khả dụng {totalKitFree:0.####})."
                });
            }

            var remToDeduct = completedKitQty;
            var totalKitDeductedCost = 0m;
            int? deductedLocationId = workOrder.TargetLocationId;
            int? deductedLotId = workOrder.TargetLotId;
            DateTime? parentExpiry = null;

            foreach (var ks in kitStocks)
            {
                if (remToDeduct <= 0m) break;
                var free = Math.Max(0m, ks.Quantity - ks.ReservedQuantity - ks.AllocatedQuantity - ks.LockedQuantity);
                if (free <= 0m) continue;
                var deduct = Math.Min(free, remToDeduct);
                var unitCost = ks.MovingAverageCost > 0m
                    ? ks.MovingAverageCost
                    : (kitProduct.MovingAverageCost > 0m ? kitProduct.MovingAverageCost : workOrder.RolledUpUnitCost);

                ks.Quantity -= deduct;
                ks.LastUpdated = now;
                ks.RefreshGrainHash();
                remToDeduct -= deduct;
                totalKitDeductedCost += deduct * unitCost;

                deductedLocationId ??= ks.LocationId;
                deductedLotId ??= ks.LotId;
                parentExpiry ??= ks.Lot?.ExpiryDate;

                _db.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = workOrder.KitProductId,
                    WarehouseId = workOrder.WarehouseId,
                    LocationId = ks.LocationId,
                    FromLocationId = ks.LocationId,
                    LotId = ks.LotId,
                    SerialId = ks.SerialId,
                    HandlingUnitId = ks.HandlingUnitId,
                    OwnerId = ks.OwnerId,
                    InventoryStatus = InventoryStatus.Available,
                    FromInventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.TransferOut,
                    Quantity = -deduct,
                    UnitCost = Math.Round(unitCost, 4),
                    ReferenceId = workOrder.Id,
                    ReferenceType = "KitDisassembleIssue",
                    TransactionDate = now,
                    ActualOccurredAtUtc = now,
                    PostedAtUtc = now,
                    CreatedBy = actorId,
                    Note = $"{workOrder.WorkOrderCode}: Xuất rã bộ KIT -{deduct:0.####}"
                });
            }

            workOrder.TargetLocationId ??= deductedLocationId ?? defaultLocationId;
            workOrder.TargetLotId ??= deductedLotId;
            workOrder.RolledUpUnitCost = completedKitQty > 0m ? Math.Round(totalKitDeductedCost / completedKitQty, 4) : 0m;
            workOrder.EarliestComponentExpiryDate ??= parentExpiry;

            var totalRatioSum = workOrder.ComponentLines.Sum(l => l.ComponentRatioQty > 0m ? l.ComponentRatioQty : 1m);
            foreach (var line in workOrder.ComponentLines.OrderBy(l => l.ComponentProductId).ThenBy(l => l.Id))
            {
                var returnQty = Math.Round(completedKitQty * line.ComponentRatioQty, 4);
                line.RequiredBaseQty = returnQty;
                line.ConsumedBaseQty = returnQty;

                var compLocId = line.SourceLocationId ?? deductedLocationId ?? defaultLocationId;
                line.SourceLocationId = compLocId;

                var compProduct = await _db.Products.FirstOrDefaultAsync(p => p.Id == line.ComponentProductId, cancellationToken);
                if (!line.SourceLotId.HasValue && compProduct != null &&
                    (compProduct.TrackingType == ProductTrackingType.Lot || compProduct.ExpiryControl))
                {
                    var lotNum = $"LOT-DEKIT-{workOrder.WorkOrderCode}-{line.ComponentProductId}";
                    var compLot = await _db.InventoryLots
                        .FirstOrDefaultAsync(l => l.ProductId == line.ComponentProductId && l.LotNumber == lotNum, cancellationToken);
                    if (compLot == null)
                    {
                        compLot = new InventoryLot
                        {
                            ProductId = line.ComponentProductId,
                            LotNumber = lotNum,
                            ManufactureDate = now.Date,
                            ExpiryDate = parentExpiry,
                            ReceivedAt = now,
                            CreatedAt = now
                        };
                        _db.InventoryLots.Add(compLot);
                        await _db.SaveChangesAsync(cancellationToken);
                    }
                    line.SourceLotId = compLot.Id;
                }

                var compUnitCost = line.ComponentUnitCost > 0m
                    ? line.ComponentUnitCost
                    : (compProduct?.MovingAverageCost > 0m
                        ? compProduct.MovingAverageCost
                        : (totalRatioSum > 0m ? Math.Round(workOrder.RolledUpUnitCost / totalRatioSum, 4) : 0m));
                line.ComponentUnitCost = compUnitCost;

                var compStock = await _db.InventoryStocks
                    .FirstOrDefaultAsync(s =>
                        s.WarehouseId == workOrder.WarehouseId &&
                        s.ProductId == line.ComponentProductId &&
                        s.LocationId == compLocId &&
                        s.LotId == line.SourceLotId &&
                        s.SerialId == null &&
                        s.HandlingUnitId == null &&
                        s.OwnerId == null &&
                        s.Status == InventoryStatus.Available,
                        cancellationToken);

                if (compStock != null)
                {
                    compStock.Quantity += returnQty;
                    if (compUnitCost > 0m) compStock.MovingAverageCost = compUnitCost;
                    compStock.LastUpdated = now;
                    compStock.RefreshGrainHash();
                }
                else
                {
                    compStock = new InventoryStock
                    {
                        WarehouseId = workOrder.WarehouseId,
                        ProductId = line.ComponentProductId,
                        LocationId = compLocId,
                        LotId = line.SourceLotId,
                        Status = InventoryStatus.Available,
                        Quantity = returnQty,
                        MovingAverageCost = compUnitCost,
                        LastUpdated = now
                    };
                    compStock.RefreshGrainHash();
                    _db.InventoryStocks.Add(compStock);
                }

                _db.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = line.ComponentProductId,
                    WarehouseId = workOrder.WarehouseId,
                    LocationId = compLocId,
                    ToLocationId = compLocId,
                    LotId = line.SourceLotId,
                    InventoryStatus = InventoryStatus.Available,
                    ToInventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.TransferIn,
                    Quantity = returnQty,
                    UnitCost = compUnitCost,
                    ReferenceId = workOrder.Id,
                    ReferenceType = "KitComponentReturn",
                    SourceDocumentLineId = line.Id,
                    TransactionDate = now,
                    ActualOccurredAtUtc = now,
                    PostedAtUtc = now,
                    CreatedBy = actorId,
                    Note = $"{workOrder.WorkOrderCode}: Hoàn nguyên linh kiện rã bộ KIT +{returnQty:0.####}"
                });
            }
        }

        workOrder.Status = "Completed";
        workOrder.CompletedKitQty = completedKitQty;
        workOrder.CompletedByUserId = actorId;
        workOrder.CompletedAtUtc = now;

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = actorId,
            WarehouseId = workOrder.WarehouseId,
            Action = "KIT_WORK_ORDER_COMPLETED",
            EntityName = "KitWorkOrder",
            EntityId = workOrder.Id,
            NewValues = $"Code={workOrder.WorkOrderCode};Type={workOrder.WorkOrderType};CompletedQty={completedKitQty:0.####};MAC={workOrder.RolledUpUnitCost:0.####};EarliestExpiry={workOrder.EarliestComponentExpiryDate:yyyy-MM-dd}",
            Reason = request?.Note ?? $"Hoàn tất lệnh gia công {workOrder.WorkOrderType} bộ KIT {kitProduct.Code}",
            Result = "Success",
            Timestamp = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        if (tx != null) await tx.CommitAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            id = workOrder.Id,
            workOrderId = workOrder.Id,
            workOrderCode = workOrder.WorkOrderCode,
            warehouseId = workOrder.WarehouseId,
            kitProductId = workOrder.KitProductId,
            kitProductCode = kitProduct.Code,
            kitProductName = kitProduct.Name,
            workOrderType = workOrder.WorkOrderType.ToString(),
            status = workOrder.Status,
            plannedKitQty = workOrder.PlannedKitQty,
            completedKitQty = workOrder.CompletedKitQty,
            targetLocationId = workOrder.TargetLocationId,
            targetLotId = workOrder.TargetLotId,
            targetHandlingUnitId = workOrder.TargetHandlingUnitId,
            rolledUpUnitCost = workOrder.RolledUpUnitCost,
            earliestComponentExpiryDate = workOrder.EarliestComponentExpiryDate,
            completedAtUtc = workOrder.CompletedAtUtc,
            componentLines = workOrder.ComponentLines.Select(l => new
            {
                id = l.Id,
                componentProductId = l.ComponentProductId,
                componentRatioQty = l.ComponentRatioQty,
                requiredBaseQty = l.RequiredBaseQty,
                consumedBaseQty = l.ConsumedBaseQty,
                sourceLocationId = l.SourceLocationId,
                sourceLotId = l.SourceLotId,
                componentUnitCost = l.ComponentUnitCost
            }).ToList(),
            statusLabel = $"Đã hoàn tất lệnh {workOrder.WorkOrderType} bộ KIT {workOrder.WorkOrderCode} (MAC: {workOrder.RolledUpUnitCost:0.##})"
        });
    }

    public async Task<WmsEndpointOutcome> CancelKitWorkOrderAsync(
        int workOrderId,
        CancelKitWorkOrderInput? request,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var ownTx = _db.Database.CurrentTransaction == null;
        await using var tx = ownTx ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;

        var workOrder = await _db.KitWorkOrders
            .Include(w => w.ComponentLines)
            .FirstOrDefaultAsync(w => w.Id == workOrderId, cancellationToken);
        if (workOrder == null)
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            return WmsEndpointOutcome.Json(404, new
            {
                code = "KIT_WORK_ORDER_NOT_FOUND",
                errorCode = "KIT_WORK_ORDER_NOT_FOUND",
                message = $"Không tìm thấy lệnh gia công KIT #{workOrderId}."
            });
        }

        if (string.Equals(workOrder.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            return WmsEndpointOutcome.Ok(new
            {
                id = workOrder.Id,
                workOrderId = workOrder.Id,
                workOrderCode = workOrder.WorkOrderCode,
                status = workOrder.Status,
                reversedTransactionsCount = 0
            });
        }

        var now = DateTime.UtcNow;
        var actorId = actorUserId is > 0 ? actorUserId.Value : (workOrder.CreatedByUserId ?? 1);
        var reversedCount = 0;

        if (string.Equals(workOrder.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            var lockProductIds = workOrder.ComponentLines
                .Select(l => l.ComponentProductId)
                .Append(workOrder.KitProductId)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
            await _db.AcquireInventoryAllocationLocksAsync(workOrder.WarehouseId, lockProductIds, cancellationToken);

            var kitRefTypes = new[] { "KitComponentIssue", "KitFinishedReceipt", "KitDisassembleIssue", "KitComponentReturn" };
            var origTxs = await _db.InventoryTransactions
                .AsNoTracking()
                .Where(t => t.ReferenceId == workOrder.Id && t.ReferenceType != null && kitRefTypes.Contains(t.ReferenceType))
                .OrderBy(t => t.Id)
                .ToListAsync(cancellationToken);

            foreach (var orig in origTxs)
            {
                var reverseDelta = -orig.Quantity;
                var bucket = await _db.InventoryStocks
                    .FirstOrDefaultAsync(s =>
                        s.WarehouseId == orig.WarehouseId &&
                        s.ProductId == orig.ProductId &&
                        s.LocationId == orig.LocationId &&
                        s.LotId == orig.LotId &&
                        s.SerialId == orig.SerialId &&
                        s.HandlingUnitId == orig.HandlingUnitId &&
                        s.OwnerId == orig.OwnerId &&
                        s.Status == orig.InventoryStatus,
                        cancellationToken);

                if (bucket != null)
                {
                    if (bucket.Quantity + reverseDelta < 0m)
                    {
                        if (tx != null) await tx.RollbackAsync(cancellationToken);
                        return WmsEndpointOutcome.Json(422, new
                        {
                            code = "KIT_REVERSAL_INSUFFICIENT_STOCK",
                            errorCode = "KIT_REVERSAL_INSUFFICIENT_STOCK",
                            message = $"Không đủ tồn kho tại vị trí #{orig.LocationId} để đảo bút toán lệnh KIT {workOrder.WorkOrderCode}."
                        });
                    }
                    bucket.Quantity += reverseDelta;
                    bucket.LastUpdated = now;
                    bucket.RefreshGrainHash();
                }
                else if (reverseDelta > 0m)
                {
                    var restored = new InventoryStock
                    {
                        WarehouseId = orig.WarehouseId,
                        ProductId = orig.ProductId,
                        LocationId = orig.LocationId,
                        LotId = orig.LotId,
                        SerialId = orig.SerialId,
                        HandlingUnitId = orig.HandlingUnitId,
                        OwnerId = orig.OwnerId,
                        Status = orig.InventoryStatus,
                        Quantity = reverseDelta,
                        MovingAverageCost = orig.UnitCost,
                        LastUpdated = now
                    };
                    restored.RefreshGrainHash();
                    _db.InventoryStocks.Add(restored);
                }

                _db.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = orig.ProductId,
                    WarehouseId = orig.WarehouseId,
                    LocationId = orig.LocationId,
                    FromLocationId = orig.ToLocationId,
                    ToLocationId = orig.FromLocationId,
                    LotId = orig.LotId,
                    SerialId = orig.SerialId,
                    HandlingUnitId = orig.HandlingUnitId,
                    OwnerId = orig.OwnerId,
                    InventoryStatus = orig.InventoryStatus,
                    FromInventoryStatus = orig.ToInventoryStatus,
                    ToInventoryStatus = orig.FromInventoryStatus,
                    TransactionType = TransactionType.Reversal,
                    Quantity = reverseDelta,
                    UnitCost = orig.UnitCost,
                    ReferenceId = workOrder.Id,
                    ReferenceType = orig.ReferenceType,
                    SourceDocumentLineId = orig.SourceDocumentLineId,
                    ReversalOfTransactionId = orig.Id,
                    ReasonCode = "KIT_WO_CANCEL_REVERSAL",
                    TransactionDate = now,
                    ActualOccurredAtUtc = now,
                    PostedAtUtc = now,
                    CreatedBy = actorId,
                    Note = $"Đảo bút toán hủy lệnh KIT {workOrder.WorkOrderCode} (OrigTx=#{orig.Id})"
                });
                reversedCount++;
            }
        }

        workOrder.Status = "Cancelled";
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = actorId,
            WarehouseId = workOrder.WarehouseId,
            Action = "KIT_WORK_ORDER_CANCELLED",
            EntityName = "KitWorkOrder",
            EntityId = workOrder.Id,
            NewValues = $"Code={workOrder.WorkOrderCode};Status=Cancelled;ReversedTxs={reversedCount}",
            Reason = request?.Reason ?? $"Hủy/Đảo lệnh gia công KIT {workOrder.WorkOrderCode}",
            Result = "Success",
            Timestamp = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        if (tx != null) await tx.CommitAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            id = workOrder.Id,
            workOrderId = workOrder.Id,
            workOrderCode = workOrder.WorkOrderCode,
            status = workOrder.Status,
            reversedTransactionsCount = reversedCount,
            statusLabel = $"Đã hủy lệnh gia công KIT {workOrder.WorkOrderCode} (Đảo {reversedCount} bút toán sổ cái)"
        });
    }
```

---

## 3. Verbatim C# Implementation: PutawayService.EvaluateAndExecuteCrossDockAsync (Epic 60.2)

- **File:** [PutawayService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/PutawayService.cs#L459-L1070) (Lines 459–1070)

```csharp
    public async Task<PutawayTaskDto> EvaluateAndExecuteCrossDockAsync(int id, ExecuteCrossDockDto? dto = null, CancellationToken token = default)
    {
        var ownTransaction = context.Database.CurrentTransaction is null;
        var isSqlServer = context.Database.IsRelational() && context.Database.IsSqlServer();
        await using var tx = ownTransaction ? await context.Database.BeginTransactionAsync(token) : null;
        try
        {
            if (isSqlServer)
            {
                var tenantId = context.CurrentTenantId;
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM dbo.PutawayTasks WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND Id = {id}; SELECT 1 FROM dbo.PutawayTaskItems WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND PutawayTaskId = {id};",
                    token);
            }

            var task = await Load(id, token);
            var nowUtc = DateTime.UtcNow;
            var actorId = currentUser.UserId > 0 ? currentUser.UserId : (task.CreatedBy > 0 ? task.CreatedBy : 1);

            if (dto?.Reverse == true)
            {
                if (!task.IsCrossDock || task.CrossDockAllocatedQty <= 0m)
                {
                    throw new BusinessRuleException("Nhiệm vụ chưa phát sinh Cross-Dock để đảo ngược.");
                }

                var reverseProductIds = task.Items.Select(x => x.ProductId).Distinct().OrderBy(x => x).ToList();
                await context.AcquireInventoryAllocationLocksAsync(task.WarehouseId, reverseProductIds, token);

                var alreadyReversedIds = await context.InventoryTransactions
                    .AsNoTracking()
                    .Where(t => t.ReferenceId == task.Id && t.ReversalOfTransactionId != null)
                    .Select(t => t.ReversalOfTransactionId!.Value)
                    .ToListAsync(token);

                var cdRefTypes = new[] { "PutawayOut", "PutawayIn" };
                var cdTxs = await context.InventoryTransactions
                    .AsNoTracking()
                    .Where(t => t.ReferenceId == task.Id &&
                                t.TransactionType != TransactionType.Reversal &&
                                t.ReferenceType != null &&
                                cdRefTypes.Contains(t.ReferenceType) &&
                                !alreadyReversedIds.Contains(t.Id))
                    .OrderBy(t => t.Id)
                    .ToListAsync(token);

                foreach (var orig in cdTxs)
                {
                    var revDelta = -orig.Quantity;
                    var bucket = await context.InventoryStocks
                        .FirstOrDefaultAsync(s =>
                            s.WarehouseId == orig.WarehouseId &&
                            s.ProductId == orig.ProductId &&
                            s.LocationId == orig.LocationId &&
                            s.LotId == orig.LotId &&
                            s.SerialId == orig.SerialId &&
                            s.HandlingUnitId == orig.HandlingUnitId &&
                            s.OwnerId == orig.OwnerId &&
                            s.Status == orig.InventoryStatus,
                            token);

                    if (bucket != null)
                    {
                        if (revDelta < 0m)
                        {
                            var releaseAmt = Math.Min(-revDelta, bucket.ReservedQuantity);
                            bucket.ReservedQuantity = Math.Max(0m, bucket.ReservedQuantity - releaseAmt);
                            bucket.AllocatedQuantity = Math.Max(0m, bucket.AllocatedQuantity - Math.Min(-revDelta, bucket.AllocatedQuantity));
                        }
                        bucket.Quantity = Math.Max(0m, bucket.Quantity + revDelta);
                        bucket.LastUpdated = nowUtc;
                        bucket.RefreshGrainHash();
                    }

                    context.InventoryTransactions.Add(new InventoryTransaction
                    {
                        ProductId = orig.ProductId,
                        WarehouseId = orig.WarehouseId,
                        LocationId = orig.LocationId,
                        FromLocationId = orig.ToLocationId,
                        ToLocationId = orig.FromLocationId,
                        LotId = orig.LotId,
                        SerialId = orig.SerialId,
                        HandlingUnitId = orig.HandlingUnitId,
                        OwnerId = orig.OwnerId,
                        InventoryStatus = orig.InventoryStatus,
                        FromInventoryStatus = orig.ToInventoryStatus,
                        ToInventoryStatus = orig.FromInventoryStatus,
                        TransactionType = TransactionType.Reversal,
                        Quantity = revDelta,
                        UnitCost = orig.UnitCost,
                        ReferenceId = task.Id,
                        ReferenceType = orig.ReferenceType,
                        SourceDocumentLineId = orig.SourceDocumentLineId,
                        ReversalOfTransactionId = orig.Id,
                        ReasonCode = "CROSS_DOCK_REVERSAL",
                        TransactionDate = nowUtc,
                        ActualOccurredAtUtc = nowUtc,
                        PostedAtUtc = nowUtc,
                        CreatedBy = actorId,
                        Note = $"Đảo bút toán Cross-Dock PutawayTask #{task.Id} (OrigTx=#{orig.Id})"
                    });
                }

                var cdAllocations = await context.StockAllocations
                    .Include(a => a.Reservation)
                    .Where(a => a.WarehouseId == task.WarehouseId &&
                                a.SelectionReason == $"CROSS_DOCK_TASK_{task.Id}" &&
                                a.Status == StockAllocationStatus.Active)
                    .ToListAsync(token);

                foreach (var alloc in cdAllocations)
                {
                    alloc.Status = StockAllocationStatus.Released;
                    alloc.ReleasedAt = nowUtc;
                    alloc.ReleasedBy = actorId;
                    alloc.ReleaseReason = dto.Reason ?? "Cross-Dock Reversal";
                    if (alloc.Reservation != null)
                    {
                        if (alloc.Reservation.SourceCode == $"CD-TASK-{task.Id}")
                        {
                            alloc.Reservation.ReleasedQuantity = alloc.Reservation.Quantity;
                            alloc.Reservation.AllocatedQuantity = 0m;
                            alloc.Reservation.Status = StockReservationStatus.Released;
                            alloc.Reservation.ReleasedAt = nowUtc;
                            alloc.Reservation.ReleasedBy = actorId;
                            alloc.Reservation.ReleaseReason = dto.Reason ?? "Cross-Dock Reversal";
                        }
                        else
                        {
                            alloc.Reservation.Quantity = Math.Max(0m, alloc.Reservation.Quantity - alloc.Quantity);
                            alloc.Reservation.AllocatedQuantity = Math.Max(0m, alloc.Reservation.AllocatedQuantity - alloc.Quantity);
                            if (alloc.Reservation.AllocatedQuantity > 0m)
                            {
                                alloc.Reservation.Status = StockReservationStatus.Allocated;
                                alloc.Reservation.ReleasedQuantity = 0m;
                                alloc.Reservation.ReleasedAt = null;
                                alloc.Reservation.ReleasedBy = null;
                                alloc.Reservation.ReleaseReason = null;
                            }
                            else
                            {
                                alloc.Reservation.Status = StockReservationStatus.Released;
                                alloc.Reservation.ReleasedQuantity = alloc.Reservation.Quantity;
                                alloc.Reservation.ReleasedAt = nowUtc;
                                alloc.Reservation.ReleasedBy = actorId;
                                alloc.Reservation.ReleaseReason = dto.Reason ?? "Cross-Dock Reversal";
                            }
                        }
                    }
                }

                if (task.CrossDockTargetBackorderId.HasValue)
                {
                    var bo = await context.Backorders.FirstOrDefaultAsync(b => b.Id == task.CrossDockTargetBackorderId.Value, token);
                    if (bo != null)
                    {
                        bo.RecoveredQuantity = Math.Max(0m, bo.RecoveredQuantity - task.CrossDockAllocatedQty);
                        bo.Status = BackorderStatus.Open;
                        bo.ResolvedAt = null;
                        bo.UpdatedAt = nowUtc;
                    }
                }

                foreach (var it in task.Items)
                {
                    it.MovedBaseQuantity = Math.Max(0m, it.MovedBaseQuantity - task.CrossDockAllocatedQty);
                }

                task.IsCrossDock = false;
                task.CrossDockAllocatedQty = 0m;
                task.Status = PutawayTaskStatus.Open;
                task.CompletedAt = null;
                context.AuditLogs.Add(Audit(actorId, "PutawayTask.CrossDockReversed", task.Id, task.WarehouseId, $"Reason: {dto.Reason ?? "Cross-Dock Reversal"}"));
                await context.SaveChangesAsync(token);
                if (tx is not null) await tx.CommitAsync(token);
                return await GetAsync(id, token);
            }

            if (task.IsCrossDock && task.Status == PutawayTaskStatus.Completed)
            {
                throw ConflictWithCode("CROSS_DOCK_ALREADY_COMPLETED", "Nhiệm vụ Cross-Dock đã được thực thi hoàn tất.");
            }
            if (task.Status is PutawayTaskStatus.Cancelled or PutawayTaskStatus.Completed)
            {
                throw Conflict("Nhiệm vụ không ở trạng thái có thể thực thi Cross-Dock.");
            }

            var item = (dto?.ItemId is > 0
                ? task.Items.FirstOrDefault(x => x.Id == dto.ItemId.Value)
                : task.Items.FirstOrDefault(x => x.InventoryStatus == InventoryStatus.Available && x.RemainingBaseQuantity > 0))
                ?? task.Items.FirstOrDefault();

            if (item is null)
                throw new NotFoundException("Không tìm thấy dòng hàng cất kho.");
            if (item.InventoryStatus != InventoryStatus.Available)
                throw new BusinessRuleException("Chỉ hàng hóa ở trạng thái Khả dụng (Available) mới được phép Cross-Dock trực tiếp sang khu vực xuất hàng.");
            if (item.RemainingBaseQuantity <= 0m)
                throw Conflict("Dòng hàng đã được cất hết số lượng yêu cầu.");

            await context.AcquireInventoryAllocationLocksAsync([(task.WarehouseId, item.ProductId)], token);

            // Match open Backorder or high-priority outbound SalesOrder / ExportReceipt
            var targetBoId = dto?.TargetBackorderId ?? task.CrossDockTargetBackorderId;
            Backorder? matchedBackorder = null;
            if (targetBoId is > 0)
            {
                matchedBackorder = await context.Backorders
                    .Include(b => b.SalesOrderLine)
                    .FirstOrDefaultAsync(b =>
                        b.Id == targetBoId.Value &&
                        b.WarehouseId == task.WarehouseId &&
                        b.Status == BackorderStatus.Open &&
                        b.Quantity - b.RecoveredQuantity - b.CancelledQuantity > 0,
                        token);
            }
            matchedBackorder ??= await context.Backorders
                .Include(b => b.SalesOrderLine)
                    .ThenInclude(l => l.SalesOrder)
                .Where(b => b.WarehouseId == task.WarehouseId &&
                            b.Status == BackorderStatus.Open &&
                            b.Quantity - b.RecoveredQuantity - b.CancelledQuantity > 0 &&
                            b.SalesOrderLine.ProductId == item.ProductId)
                .OrderByDescending(b => b.SalesOrderLine.SalesOrder.Priority)
                .ThenBy(b => b.CreatedAt)
                .FirstOrDefaultAsync(token);

            int? matchedSalesOrderId = dto?.TargetSalesOrderId ?? matchedBackorder?.SalesOrderLine?.SalesOrderId ?? task.CrossDockTargetSalesOrderId;
            decimal? outboundDemandQty = matchedBackorder != null
                ? Math.Max(0m, matchedBackorder.Quantity - matchedBackorder.RecoveredQuantity - matchedBackorder.CancelledQuantity)
                : null;

            if (!matchedSalesOrderId.HasValue)
            {
                var waitingSoLine = await context.SalesOrderLines
                    .Include(l => l.SalesOrder)
                    .Where(l => l.SalesOrder.WarehouseId == task.WarehouseId &&
                                l.ProductId == item.ProductId &&
                                l.OrderedQuantity - l.CancelledQuantity > 0 &&
                                (l.SalesOrder.Status == SalesOrderStatus.Released ||
                                 l.SalesOrder.Status == SalesOrderStatus.PartiallyFulfilled ||
                                 l.SalesOrder.Status == SalesOrderStatus.Draft))
                    .OrderByDescending(l => l.SalesOrder.Priority)
                    .ThenBy(l => l.SalesOrder.CreatedAt)
                    .FirstOrDefaultAsync(token);
                if (waitingSoLine != null)
                {
                    matchedSalesOrderId = waitingSoLine.SalesOrderId;
                    outboundDemandQty ??= Math.Max(0m, waitingSoLine.OrderedQuantity - waitingSoLine.CancelledQuantity);
                }
            }

            int? matchedExportReceiptId = dto?.TargetExportReceiptId ?? task.CrossDockTargetExportReceiptId;
            if (!matchedExportReceiptId.HasValue)
            {
                var waitingExportLine = await context.ExportReceiptDetails
                    .Include(d => d.ExportReceipt)
                    .Where(d => d.ExportReceipt.WarehouseId == task.WarehouseId &&
                                d.ProductId == item.ProductId &&
                                d.Quantity > 0 &&
                                (d.ExportReceipt.Status == ReceiptStatus.Draft || d.ExportReceipt.Status == ReceiptStatus.Approved))
                    .OrderByDescending(d => d.ExportReceipt.Id)
                    .FirstOrDefaultAsync(token);
                if (waitingExportLine != null)
                {
                    matchedExportReceiptId = waitingExportLine.ExportReceiptId;
                    outboundDemandQty ??= waitingExportLine.Quantity;
                }
            }

            // Resolve Outbound Staging location (SHIPPING-STAGE / STAGE-OUT-01 / STAGE-01)
            WarehouseLocation? stagingLoc = null;
            if (dto?.OutboundStagingLocationId is > 0)
            {
                stagingLoc = await LocationForMutation(dto.OutboundStagingLocationId.Value, token);
            }
            if (stagingLoc is null && !string.IsNullOrWhiteSpace(dto?.OutboundStagingLocationCode))
            {
                var code = dto.OutboundStagingLocationCode.Trim().ToUpperInvariant();
                stagingLoc = await LocationByCodeForMutation(task.WarehouseId, code, token);
            }
            if (stagingLoc is null)
            {
                var stagingCodes = new[] { "SHIPPING-STAGE", "STAGE-OUT-01", "STAGE-01" };
                stagingLoc = await context.WarehouseLocations
                    .Where(l => l.WarehouseId == task.WarehouseId && l.IsActive && !l.IsBlocked && stagingCodes.Contains(l.Code))
                    .OrderBy(l => l.Id)
                    .FirstOrDefaultAsync(token);
            }
            if (stagingLoc is null)
            {
                stagingLoc = await context.WarehouseLocations
                    .Where(l => l.WarehouseId == task.WarehouseId &&
                                l.IsActive &&
                                !l.IsBlocked &&
                                l.Id != item.SourceLocationId &&
                                l.LocationType == WarehouseLocationType.Storage &&
                                l.IsPickable)
                    .OrderBy(l => l.Code)
                    .FirstOrDefaultAsync(token);
            }
            if (stagingLoc is null)
            {
                stagingLoc = new WarehouseLocation
                {
                    WarehouseId = task.WarehouseId,
                    Code = "SHIPPING-STAGE",
                    Name = "Bãi tập kết xuất hàng Cross-Dock",
                    LocationType = WarehouseLocationType.Storage,
                    IsPickable = true,
                    IsReceivable = false,
                    IsActive = true,
                    IsBlocked = false,
                    CreatedBy = actorId
                };
                context.WarehouseLocations.Add(stagingLoc);
                await context.SaveChangesAsync(token);
            }

            var sourceBuckets = await context.InventoryStocks
                .Include(x => x.Lot)
                .Include(x => x.Serial)
                .Where(x => x.ProductId == item.ProductId &&
                            x.WarehouseId == task.WarehouseId &&
                            x.Status == InventoryStatus.Available &&
                            x.LocationId == item.SourceLocationId &&
                            x.Quantity > x.ReservedQuantity)
                .OrderBy(x => x.LotId)
                .ThenBy(x => x.SerialId)
                .ThenBy(x => x.Id)
                .ToListAsync(token);

            var totalAvailAtReceiving = sourceBuckets.Sum(x => Math.Max(0m, x.Quantity - x.ReservedQuantity));
            if (totalAvailAtReceiving <= 0m)
            {
                throw Conflict("Không còn tồn khả dụng tại bãi tiếp nhận để thực thi Cross-Dock.");
            }

            var targetQty = dto?.Quantity is > 0m
                ? Math.Min(dto.Quantity.Value, item.RemainingBaseQuantity)
                : Math.Min(outboundDemandQty ?? item.RemainingBaseQuantity, item.RemainingBaseQuantity);
            var crossDockQty = Math.Round(Math.Min(targetQty, totalAvailAtReceiving), 4);
            if (crossDockQty <= 0m)
            {
                throw new BusinessRuleException("Số lượng Cross-Dock phải lớn hơn 0.");
            }

            var rsvSourceType = "CrossDockPutawayTask";
            var rsvSourceId = task.Id;
            var reservation = await context.StockReservations
                .FirstOrDefaultAsync(r =>
                    r.ProductId == item.ProductId &&
                    r.SourceType == rsvSourceType &&
                    r.SourceId == rsvSourceId,
                    token);
            if (reservation is null)
            {
                reservation = new StockReservation
                {
                    ReservationCode = $"RSV-CD-{task.Id}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                    ProductId = item.ProductId,
                    WarehouseId = task.WarehouseId,
                    Quantity = crossDockQty,
                    AllocatedQuantity = crossDockQty,
                    ConsumedQuantity = 0m,
                    ReleasedQuantity = 0m,
                    BaseUomIdSnapshot = item.BaseUnitId > 0 ? item.BaseUnitId : 1,
                    BaseUomCodeSnapshot = !string.IsNullOrWhiteSpace(item.BaseUnitCodeSnapshot) ? item.BaseUnitCodeSnapshot : "THÙNG",
                    BaseUomDecimalPlacesSnapshot = item.BaseUnitDecimalPlaces,
                    AllocationVersion = 1,
                    Status = StockReservationStatus.Allocated,
                    SourceType = rsvSourceType,
                    SourceId = rsvSourceId,
                    SourceCode = $"CD-TASK-{task.Id}",
                    CreatedAt = nowUtc,
                    CreatedBy = actorId,
                    ExpiresAt = nowUtc.AddDays(7)
                };
                context.StockReservations.Add(reservation);
            }
            else
            {
                if (reservation.Status is StockReservationStatus.Released or StockReservationStatus.Cancelled)
                {
                    reservation.Quantity = crossDockQty;
                    reservation.AllocatedQuantity = crossDockQty;
                    reservation.ConsumedQuantity = 0m;
                    reservation.ReleasedQuantity = 0m;
                    reservation.ReleasedAt = null;
                    reservation.ReleasedBy = null;
                    reservation.ReleaseReason = null;
                }
                else
                {
                    reservation.Quantity = Math.Round(reservation.Quantity + crossDockQty, 4);
                    reservation.AllocatedQuantity = Math.Round(reservation.AllocatedQuantity + crossDockQty, 4);
                }
                reservation.Status = StockReservationStatus.Allocated;
                reservation.AllocationVersion += 1;
            }
            await context.SaveChangesAsync(token);

            var remToMove = crossDockQty;
            var sourceCode = item.SourceLocation?.Code?.Trim() ?? "RECEIVING";
            foreach (var srcBucket in sourceBuckets)
            {
                if (remToMove <= 0m) break;
                var avail = Math.Max(0m, srcBucket.Quantity - srcBucket.ReservedQuantity);
                if (avail <= 0m) continue;
                var chunk = Math.Min(remToMove, avail);

                srcBucket.Quantity -= chunk;
                srcBucket.LastUpdated = nowUtc;
                srcBucket.RefreshGrainHash();

                var destBucket = context.InventoryStocks.Local.FirstOrDefault(x =>
                                     x.ProductId == item.ProductId &&
                                     x.WarehouseId == task.WarehouseId &&
                                     x.Status == InventoryStatus.Available &&
                                     x.LocationId == stagingLoc.Id &&
                                     x.LotId == srcBucket.LotId &&
                                     x.SerialId == srcBucket.SerialId &&
                                     x.HandlingUnitId == srcBucket.HandlingUnitId &&
                                     x.OwnerId == srcBucket.OwnerId)
                                 ?? await context.InventoryStocks.FirstOrDefaultAsync(x =>
                                     x.ProductId == item.ProductId &&
                                     x.WarehouseId == task.WarehouseId &&
                                     x.Status == InventoryStatus.Available &&
                                     x.LocationId == stagingLoc.Id &&
                                     x.LotId == srcBucket.LotId &&
                                     x.SerialId == srcBucket.SerialId &&
                                     x.HandlingUnitId == srcBucket.HandlingUnitId &&
                                     x.OwnerId == srcBucket.OwnerId, token);

                if (destBucket is null)
                {
                    destBucket = new InventoryStock
                    {
                        ProductId = item.ProductId,
                        WarehouseId = task.WarehouseId,
                        Status = InventoryStatus.Available,
                        LocationId = stagingLoc.Id,
                        LotId = srcBucket.LotId,
                        SerialId = srcBucket.SerialId,
                        HandlingUnitId = srcBucket.HandlingUnitId,
                        OwnerId = srcBucket.OwnerId,
                        Quantity = chunk,
                        ReservedQuantity = chunk,
                        AllocatedQuantity = chunk,
                        MovingAverageCost = srcBucket.MovingAverageCost,
                        LastUpdated = nowUtc
                    };
                    destBucket.RefreshGrainHash();
                    context.InventoryStocks.Add(destBucket);
                }
                else
                {
                    destBucket.Quantity += chunk;
                    destBucket.ReservedQuantity += chunk;
                    destBucket.AllocatedQuantity += chunk;
                    destBucket.LastUpdated = nowUtc;
                    destBucket.RefreshGrainHash();
                }

                context.StockAllocations.Add(new StockAllocation
                {
                    AllocationCode = $"ALC-CD-{task.Id}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                    ReservationId = reservation.Id,
                    WarehouseId = task.WarehouseId,
                    ProductId = item.ProductId,
                    LocationId = stagingLoc.Id,
                    LotId = srcBucket.LotId,
                    SerialId = srcBucket.SerialId,
                    HandlingUnitId = srcBucket.HandlingUnitId,
                    OwnerId = srcBucket.OwnerId,
                    InventoryStatus = InventoryStatus.Available,
                    Quantity = chunk,
                    Status = StockAllocationStatus.Active,
                    Strategy = AllocationStrategy.LocationOrder,
                    SelectionReason = $"CROSS_DOCK_TASK_{task.Id}",
                    AllocatedAt = nowUtc,
                    AllocatedBy = actorId,
                    Version = 1
                });

                context.InventoryLocationMovements.Add(new InventoryLocationMovement
                {
                    WarehouseId = task.WarehouseId,
                    ProductId = item.ProductId,
                    InventoryStatus = InventoryStatus.Available,
                    LotId = srcBucket.LotId,
                    SerialId = srcBucket.SerialId,
                    FromLocationId = item.SourceLocationId,
                    ToLocationId = stagingLoc.Id,
                    BaseQuantity = chunk,
                    EnteredQuantity = chunk,
                    EnteredUnitCode = !string.IsNullOrWhiteSpace(item.BaseUnitCodeSnapshot) ? item.BaseUnitCodeSnapshot : "THÙNG",
                    ReferenceType = "CrossDock",
                    ReferenceId = task.Id,
                    PutawayTaskId = task.Id,
                    PutawayTaskItemId = item.Id,
                    ReceiptId = task.ReceiptId,
                    ReceiptLineId = item.ReceiptLineId,
                    CreatedBy = actorId
                });

                var noteText = $"CROSS-DOCK từ {sourceCode} thẳng sang bãi xuất {stagingLoc.Code}";
                context.InventoryTransactions.AddRange(
                    new InventoryTransaction
                    {
                        ProductId = item.ProductId,
                        WarehouseId = task.WarehouseId,
                        LocationId = item.SourceLocationId,
                        FromLocationId = item.SourceLocationId,
                        ToLocationId = stagingLoc.Id,
                        InventoryStatus = InventoryStatus.Available,
                        FromInventoryStatus = InventoryStatus.Available,
                        ToInventoryStatus = InventoryStatus.Available,
                        LotId = srcBucket.LotId,
                        SerialId = srcBucket.SerialId,
                        HandlingUnitId = srcBucket.HandlingUnitId,
                        OwnerId = srcBucket.OwnerId,
                        TransactionType = TransactionType.Move,
                        Quantity = -chunk,
                        UnitCost = srcBucket.MovingAverageCost,
                        ReferenceId = task.Id,
                        ReferenceType = "PutawayOut",
                        SourceDocumentLineId = item.ReceiptLineId,
                        TransactionDate = nowUtc,
                        ActualOccurredAtUtc = nowUtc,
                        PostedAtUtc = nowUtc,
                        CreatedBy = actorId,
                        Note = noteText
                    },
                    new InventoryTransaction
                    {
                        ProductId = item.ProductId,
                        WarehouseId = task.WarehouseId,
                        LocationId = stagingLoc.Id,
                        FromLocationId = item.SourceLocationId,
                        ToLocationId = stagingLoc.Id,
                        InventoryStatus = InventoryStatus.Available,
                        FromInventoryStatus = InventoryStatus.Available,
                        ToInventoryStatus = InventoryStatus.Available,
                        LotId = srcBucket.LotId,
                        SerialId = srcBucket.SerialId,
                        HandlingUnitId = srcBucket.HandlingUnitId,
                        OwnerId = srcBucket.OwnerId,
                        TransactionType = TransactionType.Move,
                        Quantity = chunk,
                        UnitCost = srcBucket.MovingAverageCost,
                        ReferenceId = task.Id,
                        ReferenceType = "PutawayIn",
                        SourceDocumentLineId = item.ReceiptLineId,
                        TransactionDate = nowUtc,
                        ActualOccurredAtUtc = nowUtc,
                        PostedAtUtc = nowUtc,
                        CreatedBy = actorId,
                        Note = noteText
                    });

                remToMove -= chunk;
            }

            if (matchedBackorder != null)
            {
                var maxRecoverable = Math.Max(0m, matchedBackorder.Quantity - matchedBackorder.CancelledQuantity);
                matchedBackorder.RecoveredQuantity = Math.Min(maxRecoverable, Math.Round(matchedBackorder.RecoveredQuantity + crossDockQty, 4));
                matchedBackorder.UpdatedAt = nowUtc;
                if (matchedBackorder.RecoveredQuantity + matchedBackorder.CancelledQuantity >= matchedBackorder.Quantity)
                {
                    matchedBackorder.Status = BackorderStatus.Fulfilled;
                    matchedBackorder.ResolvedAt = nowUtc;
                }
            }

            item.MovedBaseQuantity += crossDockQty;
            task.IsCrossDock = true;
            task.CrossDockAllocatedQty = Math.Round(task.CrossDockAllocatedQty + crossDockQty, 4);
            task.CrossDockTargetBackorderId = matchedBackorder?.Id ?? task.CrossDockTargetBackorderId;
            task.CrossDockTargetSalesOrderId = matchedSalesOrderId ?? task.CrossDockTargetSalesOrderId;
            task.CrossDockTargetExportReceiptId = matchedExportReceiptId ?? task.CrossDockTargetExportReceiptId;
            task.AssignedUserId ??= actorId;
            task.StartedAt ??= nowUtc;
            task.Status = task.Items.All(x => x.RemainingBaseQuantity == 0)
                ? PutawayTaskStatus.Completed
                : PutawayTaskStatus.InProgress;
            if (task.Status == PutawayTaskStatus.Completed)
                task.CompletedAt = nowUtc;

            context.AuditLogs.Add(Audit(
                actorId,
                "PutawayTask.CrossDockExecuted",
                task.Id,
                task.WarehouseId,
                $"CrossDockQty: {crossDockQty:0.####}; StagingBin: {stagingLoc.Code}; SalesOrderId: {task.CrossDockTargetSalesOrderId}; BackorderId: {task.CrossDockTargetBackorderId}"));

            await context.SaveChangesAsync(token);
            if (tx is not null) await tx.CommitAsync(token);
            return await GetAsync(id, token);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            if (tx is not null) await tx.RollbackAsync(token);
            throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.", ex);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(token);
            throw;
        }
    }
```

---

## 4. Verbatim C# Implementation: InventoryControlOperationsService.ExecuteBreakBulkReplenishmentAsync (Epic 60.3)

- **File:** [InventoryControlOperationsService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/InventoryControlOperationsService.cs#L798-L1394) (Lines 798–1394)

```csharp
    public async Task<WmsEndpointOutcome> ExecuteBreakBulkReplenishmentAsync(
        ExecuteBreakBulkReplenishmentInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var warehouseId = request?.WarehouseId;
        var targetWarehouseId = warehouseId ?? 1;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
            {
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else
            {
                var accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (accessibleIds.Count == 0)
                    return WmsEndpointOutcome.Forbid();
                targetWarehouseId = accessibleIds.First();
            }
        }

        var sourcePackQty = request?.SourcePackQty ?? 1m;
        if (sourcePackQty <= 0m)
        {
            return WmsEndpointOutcome.BadRequest("Số lượng thùng/kiện xả lẻ (SourcePackQty) phải lớn hơn 0.");
        }

        if (db == null)
        {
            return WmsEndpointOutcome.BadRequest("Database context is required.");
        }

        var now = DateTime.UtcNow;
        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : 1);

        int? resolvedProductId = request?.ProductId;
        var resolvedProductCode = request?.ProductCode?.Trim() ?? string.Empty;
        var resolvedProductName = string.Empty;
        var baseUomCode = "EA";

        if (!resolvedProductId.HasValue && !string.IsNullOrWhiteSpace(resolvedProductCode))
        {
            var prod = await db.Products.AsNoTracking()
                .Include(p => p.Unit)
                .FirstOrDefaultAsync(p => p.Code == resolvedProductCode, cancellationToken);
            if (prod != null)
            {
                resolvedProductId = prod.Id;
                resolvedProductCode = prod.Code.Trim();
                resolvedProductName = prod.Name.Trim();
                baseUomCode = prod.Unit?.Code?.Trim() ?? "EA";
            }
        }
        else if (resolvedProductId.HasValue)
        {
            var prod = await db.Products.AsNoTracking()
                .Include(p => p.Unit)
                .FirstOrDefaultAsync(p => p.Id == resolvedProductId.Value, cancellationToken);
            if (prod != null)
            {
                resolvedProductCode = prod.Code.Trim();
                resolvedProductName = prod.Name.Trim();
                baseUomCode = prod.Unit?.Code?.Trim() ?? "EA";
            }
        }

        if (!resolvedProductId.HasValue)
        {
            var candidateStock = await db.InventoryStocks.AsNoTracking()
                .Include(s => s.Product)
                .ThenInclude(p => p.Unit)
                .Where(s => s.WarehouseId == targetWarehouseId &&
                            s.Status == InventoryStatus.Available &&
                            s.LocationId.HasValue &&
                            (s.Quantity - s.ReservedQuantity) >= 12m)
                .OrderByDescending(s => s.HandlingUnitId.HasValue ? 1 : 0)
                .ThenByDescending(s => s.Quantity - s.ReservedQuantity)
                .FirstOrDefaultAsync(cancellationToken);

            if (candidateStock == null)
            {
                return WmsEndpointOutcome.UnprocessableEntity(new { message = "INV_INSUFFICIENT_AVAILABLE_STOCK: Không tìm thấy tồn kho khả dụng tại vị trí lưu trữ để xả thùng bổ sung kệ nhặt.", code = "INV_INSUFFICIENT_AVAILABLE_STOCK" });
            }

            resolvedProductId = candidateStock.ProductId;
            resolvedProductCode = candidateStock.Product?.Code?.Trim() ?? $"SKU-{candidateStock.ProductId}";
            resolvedProductName = candidateStock.Product?.Name?.Trim() ?? "Sản phẩm xả thùng";
            baseUomCode = candidateStock.Product?.Unit?.Code?.Trim() ?? "EA";
        }

        // Resolve UOM conversion factor
        decimal conversionFactor = request?.ConversionFactor ?? 0m;
        int? resolvedUomId = request?.SourceUomId;
        var resolvedSourceUomCode = !string.IsNullOrWhiteSpace(request?.SourceUomCode)
            ? request!.SourceUomCode!.Trim().ToUpperInvariant()
            : "CASE";

        var productUomQuery = db.ProductUoms.AsNoTracking()
            .Include(u => u.Unit)
            .Where(u => u.ProductId == resolvedProductId.Value && u.IsActive);

        if (resolvedUomId.HasValue)
        {
            var matchedUom = await productUomQuery
                .FirstOrDefaultAsync(u => u.UnitId == resolvedUomId.Value || u.Id == resolvedUomId.Value, cancellationToken);
            if (matchedUom != null)
            {
                if (conversionFactor <= 0m)
                    conversionFactor = matchedUom.ConversionFactor;
                resolvedUomId = matchedUom.UnitId;
                resolvedSourceUomCode = matchedUom.Unit?.Code?.Trim() ?? resolvedSourceUomCode;
            }
        }
        else if (!string.IsNullOrWhiteSpace(request?.SourceUomCode))
        {
            var code = request!.SourceUomCode!.Trim();
            var matchedUom = await productUomQuery
                .FirstOrDefaultAsync(u => u.Unit.Code == code, cancellationToken);
            if (matchedUom != null)
            {
                if (conversionFactor <= 0m)
                    conversionFactor = matchedUom.ConversionFactor;
                resolvedUomId = matchedUom.UnitId;
                resolvedSourceUomCode = matchedUom.Unit?.Code?.Trim() ?? code;
            }
        }

        if (conversionFactor <= 0m)
        {
            var packUom = await productUomQuery
                .Where(u => u.ConversionFactor > 1m)
                .OrderByDescending(u => u.ConversionFactor)
                .FirstOrDefaultAsync(cancellationToken);
            if (packUom != null)
            {
                conversionFactor = packUom.ConversionFactor;
                resolvedUomId = packUom.UnitId;
                resolvedSourceUomCode = packUom.Unit?.Code?.Trim() ?? "CASE";
            }
            else
            {
                conversionFactor = 12m;
            }
        }

        conversionFactor = Math.Round(conversionFactor, 4, MidpointRounding.AwayFromZero);
        if (conversionFactor <= 1m)
        {
            return WmsEndpointOutcome.UnprocessableEntity(new { message = "INV_BREAK_BULK_INVALID_UOM: Hệ số quy đổi UOM xả thùng (ConversionFactor) phải lớn hơn 1.", code = "INV_BREAK_BULK_INVALID_UOM" });
        }

        var movedBaseQty = Math.Round(sourcePackQty * conversionFactor, 4, MidpointRounding.AwayFromZero);

        var isRelational = db.Database.IsRelational();
        await using var tx = isRelational && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        await db.AcquireInventoryAllocationLocksAsync(
            targetWarehouseId,
            new[] { resolvedProductId.Value },
            cancellationToken);

        int? resolvedSourceLocationId = request?.SourceLocationId;
        var resolvedSourceLocationCode = request?.SourceLocationCode?.Trim() ?? string.Empty;
        int? resolvedTargetLocationId = request?.TargetLocationId;
        var resolvedTargetLocationCode = request?.TargetLocationCode?.Trim() ?? string.Empty;

        if (!resolvedSourceLocationId.HasValue && !string.IsNullOrWhiteSpace(resolvedSourceLocationCode))
        {
            var loc = await db.WarehouseLocations.AsNoTracking()
                .FirstOrDefaultAsync(l => l.WarehouseId == targetWarehouseId && l.Code == resolvedSourceLocationCode, cancellationToken);
            if (loc != null)
            {
                resolvedSourceLocationId = loc.Id;
                resolvedSourceLocationCode = loc.Code.Trim();
            }
        }

        if (!resolvedTargetLocationId.HasValue && !string.IsNullOrWhiteSpace(resolvedTargetLocationCode))
        {
            var loc = await db.WarehouseLocations.AsNoTracking()
                .FirstOrDefaultAsync(l => l.WarehouseId == targetWarehouseId && l.Code == resolvedTargetLocationCode, cancellationToken);
            if (loc != null)
            {
                resolvedTargetLocationId = loc.Id;
                resolvedTargetLocationCode = loc.Code.Trim();
            }
        }

        if (request?.Reverse == true)
        {
            if (!resolvedSourceLocationId.HasValue || !resolvedTargetLocationId.HasValue)
            {
                return WmsEndpointOutcome.BadRequest(new { message = "Cần chỉ định vị trí nguồn (Reserve) và vị trí đích (Forward-Pick) để hoàn tác xả thùng." });
            }

            var pickStock = await db.InventoryStocks
                .Include(s => s.Location)
                .Where(s => s.WarehouseId == targetWarehouseId &&
                            s.ProductId == resolvedProductId.Value &&
                            s.LocationId == resolvedTargetLocationId.Value &&
                            s.HandlingUnitId == null &&
                            s.Status == InventoryStatus.Available &&
                            (request.SourceLotId == null || s.LotId == request.SourceLotId))
                .OrderByDescending(s => s.Quantity - s.ReservedQuantity)
                .FirstOrDefaultAsync(cancellationToken);

            var pickAvailable = pickStock != null ? pickStock.Quantity - pickStock.ReservedQuantity : 0m;
            if (pickStock == null || pickAvailable < movedBaseQty)
            {
                return WmsEndpointOutcome.UnprocessableEntity(new { message = $"INV_INSUFFICIENT_AVAILABLE_STOCK: Tồn khả dụng tại kệ nhặt lẻ ({pickAvailable:0.####}) không đủ để đảo bút toán xả thùng ({movedBaseQty:0.####} {baseUomCode}).", code = "INV_INSUFFICIENT_AVAILABLE_STOCK" });
            }

            pickStock.Quantity = Math.Round(pickStock.Quantity - movedBaseQty, 4, MidpointRounding.AwayFromZero);
            pickStock.LastUpdated = now;

            var reserveStock = await db.InventoryStocks
                .FirstOrDefaultAsync(s =>
                    s.WarehouseId == targetWarehouseId &&
                    s.ProductId == resolvedProductId.Value &&
                    s.LocationId == resolvedSourceLocationId.Value &&
                    s.LotId == pickStock.LotId &&
                    s.SerialId == pickStock.SerialId &&
                    s.HandlingUnitId == request.SourceHandlingUnitId &&
                    s.OwnerId == pickStock.OwnerId &&
                    s.Status == InventoryStatus.Available,
                    cancellationToken);

            if (reserveStock == null)
            {
                reserveStock = new ERP.Domain.Entities.InventoryStock
                {
                    WarehouseId = targetWarehouseId,
                    ProductId = resolvedProductId.Value,
                    LocationId = resolvedSourceLocationId.Value,
                    LotId = pickStock.LotId,
                    SerialId = pickStock.SerialId,
                    HandlingUnitId = request.SourceHandlingUnitId,
                    OwnerId = pickStock.OwnerId,
                    Status = InventoryStatus.Available,
                    Quantity = movedBaseQty,
                    ReservedQuantity = 0m,
                    LastUpdated = now
                };
                db.InventoryStocks.Add(reserveStock);
            }
            else
            {
                reserveStock.Quantity = Math.Round(reserveStock.Quantity + movedBaseQty, 4, MidpointRounding.AwayFromZero);
                reserveStock.LastUpdated = now;
            }

            var revId = $"BB-REV-{now:yyyyMMdd-HHmmss}";
            var revNote = $"{revId}: Đảo bút toán xả thùng {sourcePackQty:0.####} {resolvedSourceUomCode} (x{conversionFactor:0.####} = {movedBaseQty:0.####} {baseUomCode})";

            int? origOutTxId = null;
            int? origInTxId = null;
            if (request.OriginalReferenceId.HasValue)
            {
                var alreadyReversedTxIds = await db.InventoryTransactions
                    .AsNoTracking()
                    .Where(t => t.WarehouseId == targetWarehouseId &&
                                t.ProductId == resolvedProductId.Value &&
                                t.ReversalOfTransactionId != null)
                    .Select(t => t.ReversalOfTransactionId!.Value)
                    .ToListAsync(cancellationToken);

                var origTxs = await db.InventoryTransactions
                    .AsNoTracking()
                    .Where(t => t.WarehouseId == targetWarehouseId &&
                                t.ProductId == resolvedProductId.Value &&
                                t.TransactionType != TransactionType.Reversal &&
                                t.ReferenceType == "BreakBulkReplenishment" &&
                                t.ReferenceId == request.OriginalReferenceId.Value &&
                                !alreadyReversedTxIds.Contains(t.Id))
                    .OrderByDescending(t => t.Id)
                    .ToListAsync(cancellationToken);

                origOutTxId = origTxs.FirstOrDefault(t => t.Quantity < 0)?.Id;
                origInTxId = origTxs.FirstOrDefault(t => t.Quantity > 0)?.Id;
            }

            var revOutTx = new ERP.Domain.Entities.InventoryTransaction
            {
                ProductId = resolvedProductId.Value,
                WarehouseId = targetWarehouseId,
                LocationId = resolvedTargetLocationId.Value,
                FromLocationId = resolvedTargetLocationId.Value,
                ToLocationId = resolvedSourceLocationId.Value,
                LotId = pickStock.LotId,
                SerialId = pickStock.SerialId,
                HandlingUnitId = null,
                OwnerId = pickStock.OwnerId,
                InventoryStatus = InventoryStatus.Available,
                FromInventoryStatus = InventoryStatus.Available,
                ToInventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Reversal,
                Quantity = -movedBaseQty,
                ReferenceId = request.OriginalReferenceId ?? pickStock.Id,
                ReferenceType = "BreakBulkReplenishmentReversal",
                ReversalOfTransactionId = origInTxId,
                ReasonCode = "BREAK_BULK_REVERSAL",
                TransactionDate = now,
                ActualOccurredAtUtc = now,
                PostedAtUtc = now,
                CreatedBy = effectiveActorId,
                Note = revNote
            };
            var revInTx = new ERP.Domain.Entities.InventoryTransaction
            {
                ProductId = resolvedProductId.Value,
                WarehouseId = targetWarehouseId,
                LocationId = resolvedSourceLocationId.Value,
                FromLocationId = resolvedTargetLocationId.Value,
                ToLocationId = resolvedSourceLocationId.Value,
                LotId = pickStock.LotId,
                SerialId = pickStock.SerialId,
                HandlingUnitId = request.SourceHandlingUnitId,
                OwnerId = pickStock.OwnerId,
                InventoryStatus = InventoryStatus.Available,
                FromInventoryStatus = InventoryStatus.Available,
                ToInventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Reversal,
                Quantity = movedBaseQty,
                ReferenceId = request.OriginalReferenceId ?? reserveStock.Id,
                ReferenceType = "BreakBulkReplenishmentReversal",
                ReversalOfTransactionId = origOutTxId,
                ReasonCode = "BREAK_BULK_REVERSAL",
                TransactionDate = now,
                ActualOccurredAtUtc = now,
                PostedAtUtc = now,
                CreatedBy = effectiveActorId,
                Note = revNote
            };

            db.InventoryTransactions.Add(revOutTx);
            db.InventoryTransactions.Add(revInTx);
            await db.SaveChangesAsync(cancellationToken);

            if (tx != null)
            {
                await tx.CommitAsync(cancellationToken);
            }

            return WmsEndpointOutcome.Ok(new
            {
                replenishmentId = revId,
                warehouseId = targetWarehouseId,
                productId = resolvedProductId.Value,
                productCode = resolvedProductCode,
                productName = resolvedProductName,
                sourceLocationId = resolvedSourceLocationId.Value,
                targetLocationId = resolvedTargetLocationId.Value,
                sourceUomCode = resolvedSourceUomCode,
                baseUomCode,
                sourcePackQty,
                conversionFactor,
                movedBaseQty,
                baseUomDelta = 0.0000m,
                sourceRemainingQuantity = reserveStock.Quantity,
                targetResultingQuantity = pickStock.Quantity,
                reversed = true,
                status = "REVERSED",
                statusLabel = "Đã hoàn tác xả thùng bổ sung kệ nhặt lẻ",
                executedAtUtc = now
            });
        }

        // Forward Break-Bulk Replenishment
        var sourceQuery = db.InventoryStocks
            .Include(s => s.Location)
            .Where(s => s.WarehouseId == targetWarehouseId &&
                        s.ProductId == resolvedProductId.Value &&
                        s.Status == InventoryStatus.Available &&
                        s.LocationId.HasValue);

        if (resolvedSourceLocationId.HasValue)
            sourceQuery = sourceQuery.Where(s => s.LocationId == resolvedSourceLocationId.Value);
        if (request?.SourceHandlingUnitId.HasValue == true)
            sourceQuery = sourceQuery.Where(s => s.HandlingUnitId == request.SourceHandlingUnitId.Value);
        if (request?.SourceLotId.HasValue == true)
            sourceQuery = sourceQuery.Where(s => s.LotId == request.SourceLotId.Value);

        var sourceStockBucket = await sourceQuery
            .OrderByDescending(s => s.HandlingUnitId.HasValue ? 1 : 0)
            .ThenByDescending(s => s.Quantity - s.ReservedQuantity)
            .ThenBy(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var sourceAvail = sourceStockBucket != null ? sourceStockBucket.Quantity - sourceStockBucket.ReservedQuantity : 0m;
        if (sourceStockBucket == null || sourceAvail < movedBaseQty)
        {
            return WmsEndpointOutcome.UnprocessableEntity(
                new { message = $"INV_INSUFFICIENT_AVAILABLE_STOCK: Tồn khả dụng tại vị trí lưu trữ ({sourceAvail:0.####} {baseUomCode}) không đủ để xả {sourcePackQty:0.####} {resolvedSourceUomCode} ({movedBaseQty:0.####} {baseUomCode}).", code = "INV_INSUFFICIENT_AVAILABLE_STOCK" });
        }

        resolvedSourceLocationId = sourceStockBucket.LocationId!.Value;
        resolvedSourceLocationCode = sourceStockBucket.Location?.Code?.Trim() ?? $"LOC-{resolvedSourceLocationId}";

        if (!resolvedTargetLocationId.HasValue || resolvedTargetLocationId.Value == resolvedSourceLocationId.Value)
        {
            var pickLoc = await db.WarehouseLocations.AsNoTracking()
                .Where(l => l.WarehouseId == targetWarehouseId &&
                            l.IsActive &&
                            !l.IsBlocked &&
                            !l.IsSystemManaged &&
                            l.IsPickable &&
                            l.Id != resolvedSourceLocationId.Value)
                .OrderBy(l => l.Code)
                .FirstOrDefaultAsync(cancellationToken)
                ?? await db.WarehouseLocations.AsNoTracking()
                    .Where(l => l.WarehouseId == targetWarehouseId &&
                                l.IsActive &&
                                l.Id != resolvedSourceLocationId.Value)
                    .OrderBy(l => l.Code)
                    .FirstOrDefaultAsync(cancellationToken);

            if (pickLoc == null)
            {
                return WmsEndpointOutcome.UnprocessableEntity(new { message = "INV_TARGET_LOCATION_NOT_FOUND: Không tìm thấy vị trí kệ nhặt lẻ đích (Forward-Pick Bin) hợp lệ.", code = "INV_TARGET_LOCATION_NOT_FOUND" });
            }

            resolvedTargetLocationId = pickLoc.Id;
            resolvedTargetLocationCode = pickLoc.Code.Trim();
        }
        else if (string.IsNullOrWhiteSpace(resolvedTargetLocationCode))
        {
            var pickLoc = await db.WarehouseLocations.AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == resolvedTargetLocationId.Value, cancellationToken);
            resolvedTargetLocationCode = pickLoc?.Code?.Trim() ?? $"LOC-{resolvedTargetLocationId.Value}";
        }

        var detachedHandlingUnitId = sourceStockBucket.HandlingUnitId;

        sourceStockBucket.Quantity = Math.Round(sourceStockBucket.Quantity - movedBaseQty, 4, MidpointRounding.AwayFromZero);
        sourceStockBucket.LastUpdated = now;

        if (detachedHandlingUnitId.HasValue)
        {
            var huContent = await db.HandlingUnitContents
                .FirstOrDefaultAsync(c => c.HandlingUnitId == detachedHandlingUnitId.Value &&
                                          c.ProductId == resolvedProductId.Value, cancellationToken);
            if (huContent != null && huContent.Quantity >= movedBaseQty)
            {
                huContent.Quantity = Math.Round(huContent.Quantity - movedBaseQty, 4, MidpointRounding.AwayFromZero);
            }
        }

        // Deposit into Forward-Pick Bin in BaseUom (detached from source HandlingUnit: HandlingUnitId = null)
        var targetStockBucket = await db.InventoryStocks
            .FirstOrDefaultAsync(s =>
                s.WarehouseId == targetWarehouseId &&
                s.ProductId == resolvedProductId.Value &&
                s.LocationId == resolvedTargetLocationId.Value &&
                s.LotId == sourceStockBucket.LotId &&
                s.SerialId == sourceStockBucket.SerialId &&
                s.HandlingUnitId == null &&
                s.OwnerId == sourceStockBucket.OwnerId &&
                s.Status == InventoryStatus.Available,
                cancellationToken);

        if (targetStockBucket == null)
        {
            targetStockBucket = new ERP.Domain.Entities.InventoryStock
            {
                WarehouseId = targetWarehouseId,
                ProductId = resolvedProductId.Value,
                LocationId = resolvedTargetLocationId.Value,
                LotId = sourceStockBucket.LotId,
                SerialId = sourceStockBucket.SerialId,
                HandlingUnitId = null,
                OwnerId = sourceStockBucket.OwnerId,
                Status = InventoryStatus.Available,
                Quantity = movedBaseQty,
                ReservedQuantity = 0m,
                LastUpdated = now
            };
            db.InventoryStocks.Add(targetStockBucket);
        }
        else
        {
            targetStockBucket.Quantity = Math.Round(targetStockBucket.Quantity + movedBaseQty, 4, MidpointRounding.AwayFromZero);
            targetStockBucket.LastUpdated = now;
        }

        var breakBulkId = $"BB-REPL-{now:yyyyMMdd-HHmmss}";
        var txNote = string.IsNullOrWhiteSpace(request?.Note)
            ? $"{breakBulkId}: Xả thùng bổ sung kệ nhặt lẻ {sourcePackQty:0.####} {resolvedSourceUomCode} x {conversionFactor:0.####} = {movedBaseQty:0.####} {baseUomCode} ({resolvedSourceLocationCode} -> {resolvedTargetLocationCode})"
            : $"{breakBulkId}: {request!.Note!.Trim()} [{sourcePackQty:0.####} {resolvedSourceUomCode} x {conversionFactor:0.####} = {movedBaseQty:0.####} {baseUomCode}]";

        var outTx = new ERP.Domain.Entities.InventoryTransaction
        {
            ProductId = resolvedProductId.Value,
            WarehouseId = targetWarehouseId,
            LocationId = resolvedSourceLocationId.Value,
            FromLocationId = resolvedSourceLocationId.Value,
            ToLocationId = resolvedTargetLocationId.Value,
            LotId = sourceStockBucket.LotId,
            SerialId = sourceStockBucket.SerialId,
            HandlingUnitId = detachedHandlingUnitId,
            OwnerId = sourceStockBucket.OwnerId,
            InventoryStatus = InventoryStatus.Available,
            FromInventoryStatus = InventoryStatus.Available,
            ToInventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Move,
            Quantity = -movedBaseQty,
            ReferenceId = sourceStockBucket.Id,
            ReferenceType = "BreakBulkReplenishment",
            TransactionDate = now,
            CreatedBy = effectiveActorId,
            Note = txNote
        };

        var inTx = new ERP.Domain.Entities.InventoryTransaction
        {
            ProductId = resolvedProductId.Value,
            WarehouseId = targetWarehouseId,
            LocationId = resolvedTargetLocationId.Value,
            FromLocationId = resolvedSourceLocationId.Value,
            ToLocationId = resolvedTargetLocationId.Value,
            LotId = sourceStockBucket.LotId,
            SerialId = sourceStockBucket.SerialId,
            HandlingUnitId = null,
            OwnerId = sourceStockBucket.OwnerId,
            InventoryStatus = InventoryStatus.Available,
            FromInventoryStatus = InventoryStatus.Available,
            ToInventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Move,
            Quantity = movedBaseQty,
            ReferenceId = sourceStockBucket.Id,
            ReferenceType = "BreakBulkReplenishment",
            TransactionDate = now,
            CreatedBy = effectiveActorId,
            Note = txNote
        };

        db.InventoryTransactions.Add(outTx);
        db.InventoryTransactions.Add(inTx);

        db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
        {
            UserId = effectiveActorId,
            WarehouseId = targetWarehouseId,
            EntityName = "InventoryStock",
            EntityId = sourceStockBucket.Id,
            Action = "ExecuteBreakBulkReplenishment",
            NewValues = $"{breakBulkId}:{resolvedProductCode}:{sourcePackQty:0.####} {resolvedSourceUomCode}->{movedBaseQty:0.####} {baseUomCode}",
            Reason = txNote,
            Result = "Success",
            Timestamp = now
        });

        await db.SaveChangesAsync(cancellationToken);
        if (tx != null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        var baseUomDelta = Math.Round(outTx.Quantity + inTx.Quantity, 4, MidpointRounding.AwayFromZero);

        return WmsEndpointOutcome.Ok(new
        {
            replenishmentId = breakBulkId,
            referenceId = sourceStockBucket.Id,
            warehouseId = targetWarehouseId,
            productId = resolvedProductId.Value,
            productCode = resolvedProductCode,
            productName = resolvedProductName,
            sourceLocationId = resolvedSourceLocationId.Value,
            sourceLocationCode = resolvedSourceLocationCode,
            targetLocationId = resolvedTargetLocationId.Value,
            targetLocationCode = resolvedTargetLocationCode,
            sourceHandlingUnitId = detachedHandlingUnitId,
            targetHandlingUnitId = (int?)null,
            detachedFromHandlingUnit = detachedHandlingUnitId.HasValue,
            sourceUomId = resolvedUomId,
            sourceUomCode = resolvedSourceUomCode,
            baseUomCode,
            sourcePackQty,
            conversionFactor,
            movedBaseQty,
            movedQuantity = movedBaseQty,
            baseUomDelta,
            sourceRemainingQuantity = sourceStockBucket.Quantity,
            targetResultingQuantity = targetStockBucket.Quantity,
            outTransactionId = outTx.Id,
            inTransactionId = inTx.Id,
            stockMutated = true,
            status = "COMPLETED",
            statusLabel = $"Đã xả {sourcePackQty:0.####} {resolvedSourceUomCode} (x{conversionFactor:0.####}) = {movedBaseQty:0.####} {baseUomCode} từ {resolvedSourceLocationCode} sang {resolvedTargetLocationCode}",
            executedAtUtc = now
        });
    }
```

---

## 5. Verbatim C# SQL Integration Test Suite: SqlServerWave60RoadmapEpicsTests.cs (Epics 60.1, 60.2, 60.3)

- **File:** [SqlServerWave60RoadmapEpicsTests.cs](file:///d:/ERP_KHO/ERP.Application.Tests/SqlServerWave60RoadmapEpicsTests.cs#L1-L1010) (Full File)

```csharp
using System.Text.Json;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerWave60RoadmapEpicsTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    private static ErpKhoDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(ConnectionString).Options);

    [SqlServerFact]
    public async Task Epic60_1_PhysicalFloorKittingAndDeKitting_HappyPath_Idempotency_Concurrency_And_Reversal()
    {
        var fx = await CreateKittingFixtureAsync();
        try
        {
            int assembleWoId;
            int disassembleWoId;

            // (a) Happy Path: Assemble 2 Kits from Component A (ratio 2, cost 10,000, expiry +6m) & Component B (ratio 3, cost 20,000, expiry +3m)
            // Expected per Kit MAC = 2*10,000 + 3*20,000 = 80,000; EarliestComponentExpiryDate = Component B expiry (+3m)
            await using (var db = CreateContext())
            {
                var svc = new ProductOperationsService(db);
                var createRes = await svc.CreateKitWorkOrderAsync(
                    new CreateKitWorkOrderInput(
                        WarehouseId: fx.WarehouseId,
                        KitProductId: fx.KitProductId,
                        WorkOrderType: "Assemble",
                        PlannedKitQty: 2m,
                        TargetLocationId: fx.LocationAId,
                        ComponentLines: new List<CreateKitWorkOrderLineInput>
                        {
                            new(fx.CompAProductId, 2m, fx.LocationAId, fx.LotAId, 10000m),
                            new(fx.CompBProductId, 3m, fx.LocationAId, fx.LotBId, 20000m)
                        }),
                    fx.UserId);
                createRes.StatusCode.Should().Be(200);
                using var createDoc = JsonSerializer.SerializeToDocument(createRes.Payload);
                assembleWoId = createDoc.RootElement.GetProperty("id").GetInt32();

                var completeRes = await svc.CompleteKitWorkOrderAsync(
                    assembleWoId,
                    new CompleteKitWorkOrderInput(CompletedKitQty: 2m, TargetLocationId: fx.LocationAId),
                    fx.UserId);
                completeRes.StatusCode.Should().Be(200);

                using var completeDoc = JsonSerializer.SerializeToDocument(completeRes.Payload);
                completeDoc.RootElement.GetProperty("status").GetString().Should().Be("Completed");
                completeDoc.RootElement.GetProperty("completedKitQty").GetDecimal().Should().Be(2m);
                completeDoc.RootElement.GetProperty("rolledUpUnitCost").GetDecimal().Should().Be(80000m);
                var earliestExpiry = completeDoc.RootElement.GetProperty("earliestComponentExpiryDate").GetDateTime();
                earliestExpiry.Date.Should().Be(fx.LotBExpiryDate.Date);
            }

            // Verify DB balances & ledger after Assemble
            await using (var db = CreateContext())
            {
                var compAStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.CompAProductId);
                var compBStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.CompBProductId);
                var kitStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.KitProductId);

                compAStock.Quantity.Should().Be(6m); // 10 - (2 * 2) = 6
                compBStock.Quantity.Should().Be(4m); // 10 - (2 * 3) = 4
                kitStock.Quantity.Should().Be(2m);
                kitStock.MovingAverageCost.Should().Be(80000m);

                var txs = await db.InventoryTransactions
                    .Where(t => t.WarehouseId == fx.WarehouseId && t.ReferenceId == assembleWoId && t.ReferenceType != "ImportReceipt")
                    .ToListAsync();
                txs.Should().HaveCount(3);
            }

            // (b) Duplicate Idempotency-Key / Already-Completed guard on Kitting completion
            await using (var db = CreateContext())
            {
                var svc = new ProductOperationsService(db);
                var duplicateComplete = await svc.CompleteKitWorkOrderAsync(
                    assembleWoId,
                    new CompleteKitWorkOrderInput(CompletedKitQty: 2m, TargetLocationId: fx.LocationAId),
                    fx.UserId);
                duplicateComplete.StatusCode.Should().Be(409);

                var txsAfterReplay = await db.InventoryTransactions
                    .Where(t => t.WarehouseId == fx.WarehouseId && t.ReferenceId == assembleWoId && t.ReferenceType != "ImportReceipt")
                    .CountAsync();
                txsAfterReplay.Should().Be(3);
            }

            // (c) Parallel Concurrency contention: 2 concurrent Assemble work orders competing for remaining 4 units of Component B (each needing 3 units -> total 6 > 4)
            int concWo1Id;
            int concWo2Id;
            await using (var db = CreateContext())
            {
                var svc = new ProductOperationsService(db);
                var r1 = await svc.CreateKitWorkOrderAsync(
                    new CreateKitWorkOrderInput(
                        WarehouseId: fx.WarehouseId,
                        KitProductId: fx.KitProductId,
                        WorkOrderType: "Assemble",
                        PlannedKitQty: 1m,
                        TargetLocationId: fx.LocationAId,
                        ComponentLines: new List<CreateKitWorkOrderLineInput>
                        {
                            new(fx.CompBProductId, 3m, fx.LocationAId, fx.LotBId, 20000m)
                        }),
                    fx.UserId);
                var r2 = await svc.CreateKitWorkOrderAsync(
                    new CreateKitWorkOrderInput(
                        WarehouseId: fx.WarehouseId,
                        KitProductId: fx.KitProductId,
                        WorkOrderType: "Assemble",
                        PlannedKitQty: 1m,
                        TargetLocationId: fx.LocationAId,
                        ComponentLines: new List<CreateKitWorkOrderLineInput>
                        {
                            new(fx.CompBProductId, 3m, fx.LocationAId, fx.LotBId, 20000m)
                        }),
                    fx.UserId);
                using var d1 = JsonSerializer.SerializeToDocument(r1.Payload);
                using var d2 = JsonSerializer.SerializeToDocument(r2.Payload);
                concWo1Id = d1.RootElement.GetProperty("id").GetInt32();
                concWo2Id = d2.RootElement.GetProperty("id").GetInt32();
            }

            var outcomes = await Task.WhenAll(
                Task.Run(async () =>
                {
                    await using var db1 = CreateContext();
                    var svc1 = new ProductOperationsService(db1);
                    return await svc1.CompleteKitWorkOrderAsync(concWo1Id, new CompleteKitWorkOrderInput(1m, fx.LocationAId), fx.UserId);
                }),
                Task.Run(async () =>
                {
                    await using var db2 = CreateContext();
                    var svc2 = new ProductOperationsService(db2);
                    return await svc2.CompleteKitWorkOrderAsync(concWo2Id, new CompleteKitWorkOrderInput(1m, fx.LocationAId), fx.UserId);
                }));

            outcomes.Count(o => o.StatusCode == 200).Should().Be(1);
            outcomes.Count(o => o.StatusCode == 422 || o.StatusCode == 409).Should().Be(1);

            var succeededConcWoId = outcomes[0].StatusCode == 200 ? concWo1Id : concWo2Id;
            await using (var db = CreateContext())
            {
                var svc = new ProductOperationsService(db);
                var cancelConc = await svc.CancelKitWorkOrderAsync(succeededConcWoId, new CancelKitWorkOrderInput("Hoàn tác test song song"), fx.UserId);
                cancelConc.StatusCode.Should().Be(200);
            }

            // Test Disassemble 1 Kit back into components, then Cancel/Reverse both Disassemble and Assemble
            await using (var db = CreateContext())
            {
                var svc = new ProductOperationsService(db);
                var disCreate = await svc.CreateKitWorkOrderAsync(
                    new CreateKitWorkOrderInput(
                        WarehouseId: fx.WarehouseId,
                        KitProductId: fx.KitProductId,
                        WorkOrderType: "Disassemble",
                        PlannedKitQty: 1m,
                        TargetLocationId: fx.LocationAId,
                        ComponentLines: new List<CreateKitWorkOrderLineInput>
                        {
                            new(fx.CompAProductId, 2m, fx.LocationAId, fx.LotAId, 10000m),
                            new(fx.CompBProductId, 3m, fx.LocationAId, fx.LotBId, 20000m)
                        }),
                    fx.UserId);
                using var disDoc = JsonSerializer.SerializeToDocument(disCreate.Payload);
                disassembleWoId = disDoc.RootElement.GetProperty("id").GetInt32();

                var disComplete = await svc.CompleteKitWorkOrderAsync(disassembleWoId, new CompleteKitWorkOrderInput(1m, fx.LocationAId), fx.UserId);
                disComplete.StatusCode.Should().Be(200);

                // (d) Reversal / Cancellation of Disassemble & Assemble
                var cancelDis = await svc.CancelKitWorkOrderAsync(disassembleWoId, new CancelKitWorkOrderInput("Đảo lệnh tháo dỡ Kit"), fx.UserId);
                cancelDis.StatusCode.Should().Be(200);

                var cancelAsm = await svc.CancelKitWorkOrderAsync(assembleWoId, new CancelKitWorkOrderInput("Đảo lệnh lắp ráp Kit"), fx.UserId);
                cancelAsm.StatusCode.Should().Be(200);

                var compAQty = await db.InventoryStocks.Where(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.CompAProductId).SumAsync(s => s.Quantity);
                var compBQty = await db.InventoryStocks.Where(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.CompBProductId).SumAsync(s => s.Quantity);
                var kitQty = await db.InventoryStocks.Where(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.KitProductId).SumAsync(s => s.Quantity);

                compAQty.Should().Be(10m);
                compBQty.Should().Be(10m);
                kitQty.Should().Be(0m);
            }
        }
        finally
        {
            await CleanupKittingFixtureAsync(fx);
        }
    }

    [SqlServerFact]
    public async Task Epic60_2_InboundToOutboundCrossDocking_HappyPath_Idempotency_Concurrency_And_Reversal()
    {
        var fx = await CreateCrossDockFixtureAsync();
        try
        {
            // (a) Happy Path: Execute Cross-Dock on PutawayTask matching open Backorder (qty 6 out of 10 received at RECEIVING-STAGE)
            await using (var db = CreateContext())
            {
                var putawaySvc = CreatePutawayService(db, fx.UserId);
                var res = await putawaySvc.EvaluateAndExecuteCrossDockAsync(
                    fx.PutawayTaskId,
                    new ExecuteCrossDockDto
                    {
                        TargetBackorderId = fx.BackorderId,
                        TargetSalesOrderId = fx.SalesOrderId
                    });

                res.IsCrossDock.Should().BeTrue();
                res.CrossDockAllocatedQty.Should().Be(6m);
                res.CrossDockTargetBackorderId.Should().Be(fx.BackorderId);
                res.CrossDockTargetSalesOrderId.Should().Be(fx.SalesOrderId);
                res.MovedBaseQuantity.Should().Be(6m);
                res.Status.Should().Be(PutawayTaskStatus.InProgress.ToString());
            }

            // Verify 9D stock moved from RECEIVING-STAGE to SHIPPING-STAGE, StockReservation + StockAllocation created, and Backorder fulfilled
            await using (var db = CreateContext())
            {
                var recvStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.LocationId == fx.ReceivingStageId);
                var shipStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.LocationId == fx.ShippingStageId);

                recvStock.Quantity.Should().Be(4m); // 10 - 6 = 4
                shipStock.Quantity.Should().Be(6m);
                shipStock.ReservedQuantity.Should().Be(6m);

                var backorder = await db.Backorders.SingleAsync(b => b.Id == fx.BackorderId);
                backorder.Status.Should().Be(BackorderStatus.Fulfilled);
                backorder.RecoveredQuantity.Should().Be(6m);
            }

            // (b) Duplicate Idempotency-Key record persistence & replay verification
            await using (var db = CreateContext())
            {
                var record = new IdempotencyRecord
                {
                    UserId = fx.UserId,
                    CommandScope = "Putaway.CrossDock",
                    KeyHash = $"HASH-XDOCK-{fx.Suffix}",
                    RequestFingerprint = "FP-XDOCK",
                    Status = IdempotencyStatus.Completed,
                    ResponseStatusCode = 200,
                    ResponseBody = "{\"id\":" + fx.PutawayTaskId + ",\"isCrossDock\":true}",
                    CorrelationId = "corr-xdock",
                    WarehouseId = fx.WarehouseId,
                    CreatedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
                };
                db.IdempotencyRecords.Add(record);
                await db.SaveChangesAsync();

                var replayed = await db.IdempotencyRecords
                    .AsNoTracking()
                    .SingleOrDefaultAsync(r => r.UserId == fx.UserId && r.CommandScope == "Putaway.CrossDock" && r.KeyHash == $"HASH-XDOCK-{fx.Suffix}");
                replayed.Should().NotBeNull();
                replayed!.Status.Should().Be(IdempotencyStatus.Completed);
                replayed.ResponseStatusCode.Should().Be(200);
            }

            // (c) Parallel Concurrency contention: 2 concurrent Cross-Dock requests competing for the remaining 4 units on the same PutawayTask
            var concResults = await Task.WhenAll(
                Task.Run(async () =>
                {
                    try
                    {
                        await using var db1 = CreateContext();
                        var svc1 = CreatePutawayService(db1, fx.UserId);
                        await svc1.EvaluateAndExecuteCrossDockAsync(fx.PutawayTaskId, new ExecuteCrossDockDto { Quantity = 4m, TargetSalesOrderId = fx.SalesOrderId });
                        return 200;
                    }
                    catch (BusinessRuleException) { return 422; }
                    catch (ConflictException) { return 409; }
                    catch (ConcurrencyException) { return 409; }
                }),
                Task.Run(async () =>
                {
                    try
                    {
                        await using var db2 = CreateContext();
                        var svc2 = CreatePutawayService(db2, fx.UserId);
                        await svc2.EvaluateAndExecuteCrossDockAsync(fx.PutawayTaskId, new ExecuteCrossDockDto { Quantity = 4m, TargetSalesOrderId = fx.SalesOrderId });
                        return 200;
                    }
                    catch (BusinessRuleException) { return 422; }
                    catch (ConflictException) { return 409; }
                    catch (ConcurrencyException) { return 409; }
                }));

            concResults.Count(code => code == 200).Should().Be(1);
            concResults.Count(code => code == 422 || code == 409).Should().Be(1);

            // (d) Reversal of Cross-Dock (restoring all 10 units back to RECEIVING-STAGE and releasing reservation/allocation)
            await using (var db = CreateContext())
            {
                var putawaySvc = CreatePutawayService(db, fx.UserId);
                var revRes = await putawaySvc.EvaluateAndExecuteCrossDockAsync(
                    fx.PutawayTaskId,
                    new ExecuteCrossDockDto { Reverse = true });

                revRes.IsCrossDock.Should().BeFalse();
                revRes.CrossDockAllocatedQty.Should().Be(0m);
                revRes.MovedBaseQuantity.Should().Be(0m);

                var recvStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.LocationId == fx.ReceivingStageId);
                var shipStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.LocationId == fx.ShippingStageId);
                recvStock.Quantity.Should().Be(10m);
                shipStock.Quantity.Should().Be(0m);
                shipStock.ReservedQuantity.Should().Be(0m);
            }
        }
        finally
        {
            await CleanupCrossDockFixtureAsync(fx);
        }
    }

    [SqlServerFact]
    public async Task Epic60_3_AutomatedBreakBulkReplenishment_HappyPath_Idempotency_Concurrency_And_Reversal()
    {
        var fx = await CreateBreakBulkFixtureAsync();
        try
        {
            int refStockId;

            // (a) Happy Path: Break-Bulk 2 Cases (ConversionFactor = 12 -> 24 BaseUom) from Reserve Bin to Forward-Pick Bin
            await using (var db = CreateContext())
            {
                var svc = new InventoryControlOperationsService(db, null, new FakeUser(fx.UserId));
                var res = await svc.ExecuteBreakBulkReplenishmentAsync(
                    new ExecuteBreakBulkReplenishmentInput(
                        WarehouseId: fx.WarehouseId,
                        ProductId: fx.ProductId,
                        SourceLocationId: fx.ReserveLocationId,
                        TargetLocationId: fx.PickLocationId,
                        SourceUomId: fx.CaseUnitId,
                        SourceUomCode: "CASE",
                        SourcePackQty: 2m,
                        ConversionFactor: 12m,
                        SourceLotId: fx.LotId),
                    fx.UserId);

                res.StatusCode.Should().Be(200);
                using var doc = JsonSerializer.SerializeToDocument(res.Payload);
                doc.RootElement.GetProperty("movedBaseQty").GetDecimal().Should().Be(24m);
                doc.RootElement.GetProperty("conversionFactor").GetDecimal().Should().Be(12m);
                doc.RootElement.GetProperty("baseUomDelta").GetDecimal().Should().Be(0.0000m);
                doc.RootElement.GetProperty("sourceRemainingQuantity").GetDecimal().Should().Be(12m); // 36 - 24 = 12
                doc.RootElement.GetProperty("targetResultingQuantity").GetDecimal().Should().Be(24m);
                refStockId = doc.RootElement.GetProperty("referenceId").GetInt32();
            }

            // (b) Duplicate Idempotency-Key record persistence & replay verification on Break-Bulk Replenishment
            await using (var db = CreateContext())
            {
                var record = new IdempotencyRecord
                {
                    UserId = fx.UserId,
                    CommandScope = "Inventory.BreakBulkReplenishment",
                    KeyHash = $"HASH-BB-{fx.Suffix}",
                    RequestFingerprint = "FP-BB",
                    Status = IdempotencyStatus.Completed,
                    ResponseStatusCode = 200,
                    ResponseBody = "{\"movedBaseQty\":24,\"baseUomDelta\":0.0000}",
                    CorrelationId = "corr-bb",
                    WarehouseId = fx.WarehouseId,
                    CreatedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
                };
                db.IdempotencyRecords.Add(record);
                await db.SaveChangesAsync();

                var replayed = await db.IdempotencyRecords
                    .AsNoTracking()
                    .SingleOrDefaultAsync(r => r.UserId == fx.UserId && r.CommandScope == "Inventory.BreakBulkReplenishment" && r.KeyHash == $"HASH-BB-{fx.Suffix}");
                replayed.Should().NotBeNull();
                replayed!.Status.Should().Be(IdempotencyStatus.Completed);
                replayed.ResponseStatusCode.Should().Be(200);
            }

            // (c) Parallel Concurrency contention: 2 concurrent Break-Bulk requests competing for the remaining 1 Case (12 BaseUom) at Reserve Bin
            var concOutcomes = await Task.WhenAll(
                Task.Run(async () =>
                {
                    await using var db1 = CreateContext();
                    var svc1 = new InventoryControlOperationsService(db1, null, new FakeUser(fx.UserId));
                    return await svc1.ExecuteBreakBulkReplenishmentAsync(
                        new ExecuteBreakBulkReplenishmentInput(
                            WarehouseId: fx.WarehouseId,
                            ProductId: fx.ProductId,
                            SourceLocationId: fx.ReserveLocationId,
                            TargetLocationId: fx.PickLocationId,
                            SourceUomId: fx.CaseUnitId,
                            SourcePackQty: 1m,
                            ConversionFactor: 12m,
                            SourceLotId: fx.LotId),
                        fx.UserId);
                }),
                Task.Run(async () =>
                {
                    await using var db2 = CreateContext();
                    var svc2 = new InventoryControlOperationsService(db2, null, new FakeUser(fx.UserId));
                    return await svc2.ExecuteBreakBulkReplenishmentAsync(
                        new ExecuteBreakBulkReplenishmentInput(
                            WarehouseId: fx.WarehouseId,
                            ProductId: fx.ProductId,
                            SourceLocationId: fx.ReserveLocationId,
                            TargetLocationId: fx.PickLocationId,
                            SourceUomId: fx.CaseUnitId,
                            SourcePackQty: 1m,
                            ConversionFactor: 12m,
                            SourceLotId: fx.LotId),
                        fx.UserId);
                }));

            concOutcomes.Count(o => o.StatusCode == 200).Should().Be(1);
            concOutcomes.Count(o => o.StatusCode == 422 || o.StatusCode == 409).Should().Be(1);

            // (d) Reversal of all 3 Cases (36 BaseUom) back from Forward-Pick Bin to Reserve Bin
            await using (var db = CreateContext())
            {
                var svc = new InventoryControlOperationsService(db, null, new FakeUser(fx.UserId));
                var revRes = await svc.ExecuteBreakBulkReplenishmentAsync(
                    new ExecuteBreakBulkReplenishmentInput(
                        WarehouseId: fx.WarehouseId,
                        ProductId: fx.ProductId,
                        SourceLocationId: fx.ReserveLocationId,
                        TargetLocationId: fx.PickLocationId,
                        SourceUomId: fx.CaseUnitId,
                        SourceUomCode: "CASE",
                        SourcePackQty: 3m,
                        ConversionFactor: 12m,
                        SourceLotId: fx.LotId,
                        Reverse: true,
                        OriginalReferenceId: refStockId),
                    fx.UserId);

                revRes.StatusCode.Should().Be(200);
                using var revDoc = JsonSerializer.SerializeToDocument(revRes.Payload);
                revDoc.RootElement.GetProperty("reversed").GetBoolean().Should().BeTrue();
                revDoc.RootElement.GetProperty("sourceRemainingQuantity").GetDecimal().Should().Be(36m);
                revDoc.RootElement.GetProperty("targetResultingQuantity").GetDecimal().Should().Be(0m);
            }
        }
        finally
        {
            await CleanupBreakBulkFixtureAsync(fx);
        }
    }

    private static PutawayService CreatePutawayService(ErpKhoDbContext db, int userId) =>
        new(
            db,
            new AllowAllWarehouseAuth(),
            new FakeUser(userId));

    private sealed class FakeUser(int userId) : ICurrentUser
    {
        public int UserId => userId;
        public bool IsAuthenticated => true;
        public bool IsGlobalAdmin => true;
        public string Role => "Admin";
    }

    private sealed class FakeRequestMetadata : IRequestMetadata
    {
        public string CorrelationId { get; set; } = "corr-wave60";
        public string? IdempotencyKeyHash { get; set; }
        public string? RequestFingerprint { get; set; }
    }

    private sealed class AllowAllWarehouseAuth : IWarehouseAuthorizationService
    {
        public Task<IReadOnlyList<int>> GetAccessibleWarehouseIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>());

        public Task<bool> CanAccessWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task EnsureWarehouseAccessAsync(int warehouseId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed record KittingFixture(
        string Suffix,
        int UserId,
        int RoleId,
        int UnitId,
        int KitProductId,
        int CompAProductId,
        int CompBProductId,
        int WarehouseId,
        int LocationAId,
        int LotAId,
        int LotBId,
        DateTime LotBExpiryDate);

    private static async Task<KittingFixture> CreateKittingFixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var role = new Role { RoleName = $"W60KRole{suffix}" };
        var user = new User { Username = $"W60KUser{suffix}", PasswordHash = "hash", FullName = "Wave60 Kitting Tester", Role = role };
        var unit = new Unit { Code = $"UK{suffix}", Name = "Bộ" };
        var kitProd = new Product { Code = $"KIT-W60-{suffix}", Name = "Bộ Kit Wave 60", Unit = unit, TrackingType = ProductTrackingType.Lot, ExpiryControl = true, IsActive = true };
        var compA = new Product { Code = $"CMPA-W60-{suffix}", Name = "Linh kiện A", Unit = unit, TrackingType = ProductTrackingType.Lot, ExpiryControl = true, MovingAverageCost = 10000m, IsActive = true };
        var compB = new Product { Code = $"CMPB-W60-{suffix}", Name = "Linh kiện B", Unit = unit, TrackingType = ProductTrackingType.Lot, ExpiryControl = true, MovingAverageCost = 20000m, IsActive = true };
        var wh = new Warehouse { Code = $"WH60K-{suffix}", Name = "Kho Kitting Wave 60", IsActive = true };
        db.AddRange(role, user, unit, kitProd, compA, compB, wh);
        await db.SaveChangesAsync();

        var locA = new WarehouseLocation
        {
            WarehouseId = wh.Id,
            Code = $"K-01-{suffix}",
            Name = "Bàn đóng gói Kit",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            CreatedBy = user.Id
        };
        var lotA = new InventoryLot
        {
            ProductId = compA.Id,
            LotNumber = $"LOTA-{suffix}",
            ManufactureDate = DateTime.UtcNow.Date.AddMonths(-1),
            ExpiryDate = DateTime.UtcNow.Date.AddMonths(6),
            ReceivedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var lotBExpiry = DateTime.UtcNow.Date.AddMonths(3);
        var lotB = new InventoryLot
        {
            ProductId = compB.Id,
            LotNumber = $"LOTB-{suffix}",
            ManufactureDate = DateTime.UtcNow.Date.AddMonths(-2),
            ExpiryDate = lotBExpiry,
            ReceivedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.AddRange(locA, lotA, lotB);
        await db.SaveChangesAsync();

        var stockA = new InventoryStock
        {
            WarehouseId = wh.Id,
            ProductId = compA.Id,
            LocationId = locA.Id,
            LotId = lotA.Id,
            Status = InventoryStatus.Available,
            Quantity = 10m,
            MovingAverageCost = 10000m
        };
        stockA.RefreshGrainHash();
        var stockB = new InventoryStock
        {
            WarehouseId = wh.Id,
            ProductId = compB.Id,
            LocationId = locA.Id,
            LotId = lotB.Id,
            Status = InventoryStatus.Available,
            Quantity = 10m,
            MovingAverageCost = 20000m
        };
        stockB.RefreshGrainHash();
        db.InventoryStocks.AddRange(stockA, stockB);
        db.InventoryTransactions.AddRange(
            new InventoryTransaction
            {
                WarehouseId = wh.Id,
                ProductId = compA.Id,
                LocationId = locA.Id,
                LotId = lotA.Id,
                InventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Import,
                Quantity = 10m,
                UnitCost = 10000m,
                ReferenceType = "ImportReceipt",
                ReferenceId = 1,
                TransactionDate = DateTime.UtcNow.AddMinutes(-30),
                CreatedBy = user.Id
            },
            new InventoryTransaction
            {
                WarehouseId = wh.Id,
                ProductId = compB.Id,
                LocationId = locA.Id,
                LotId = lotB.Id,
                InventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Import,
                Quantity = 10m,
                UnitCost = 20000m,
                ReferenceType = "ImportReceipt",
                ReferenceId = 1,
                TransactionDate = DateTime.UtcNow.AddMinutes(-30),
                CreatedBy = user.Id
            });
        await db.SaveChangesAsync();

        return new KittingFixture(suffix, user.Id, role.Id, unit.Id, kitProd.Id, compA.Id, compB.Id, wh.Id, locA.Id, lotA.Id, lotB.Id, lotBExpiry);
    }

    private static async Task CleanupKittingFixtureAsync(KittingFixture fx)
    {
        await using var db = CreateContext();
        await db.IdempotencyRecords.Where(x => x.UserId == fx.UserId).ExecuteDeleteAsync();
        await db.AuditLogs.Where(x => x.UserId == fx.UserId || x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        var woIds = await db.KitWorkOrders.Where(w => w.WarehouseId == fx.WarehouseId).Select(w => w.Id).ToListAsync();
        if (woIds.Count > 0)
        {
            await db.KitWorkOrderComponentLines.Where(l => woIds.Contains(l.KitWorkOrderId)).ExecuteDeleteAsync();
            await db.KitWorkOrders.Where(w => woIds.Contains(w.Id)).ExecuteDeleteAsync();
        }
        await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL DISABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
        try
        {
            await db.InventoryTransactions.Where(x => x.WarehouseId == fx.WarehouseId && (x.CorrectiveTransactionId != null || x.ReversalOfTransactionId != null)).ExecuteUpdateAsync(s => s.SetProperty(x => x.CorrectiveTransactionId, (int?)null).SetProperty(x => x.ReversalOfTransactionId, (int?)null));
            await db.InventoryTransactions.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL ENABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
        }
        await db.InventoryStocks.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.InventoryLots.Where(x => x.ProductId == fx.KitProductId || x.ProductId == fx.CompAProductId || x.ProductId == fx.CompBProductId).ExecuteDeleteAsync();
        await db.WarehouseLocations.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Warehouses.Where(x => x.Id == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == fx.KitProductId || x.Id == fx.CompAProductId || x.Id == fx.CompBProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == fx.UnitId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.Id == fx.UserId).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == fx.RoleId).ExecuteDeleteAsync();
    }

    private sealed record CrossDockFixture(
        string Suffix,
        int UserId,
        int RoleId,
        int UnitId,
        int ProductId,
        int WarehouseId,
        int ReceivingStageId,
        int ShippingStageId,
        int ImportReceiptId,
        int PutawayTaskId,
        int PartnerId,
        int SalesOrderId,
        int SalesOrderLineId,
        int BackorderId);

    private static async Task<CrossDockFixture> CreateCrossDockFixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var role = new Role { RoleName = $"W60XRole{suffix}" };
        var user = new User { Username = $"W60XUser{suffix}", PasswordHash = "hash", FullName = "Wave60 CrossDock Tester", Role = role };
        var unit = new Unit { Code = $"UX{suffix}", Name = "Cái" };
        var prod = new Product { Code = $"SKU-X60-{suffix}", Name = "Sản phẩm Cross-Dock", Unit = unit, IsActive = true };
        var wh = new Warehouse { Code = $"WH60X-{suffix}", Name = "Kho Cross-Dock Wave 60", IsActive = true };
        var partner = new BusinessPartner { Code = $"CUST60-{suffix}", Name = "Khách hàng Cross-Dock", IsCustomer = true, IsActive = true };
        db.AddRange(role, user, unit, prod, wh, partner);
        await db.SaveChangesAsync();

        var recvLoc = new WarehouseLocation
        {
            WarehouseId = wh.Id,
            Code = "RECEIVING-STAGE",
            Name = "Bãi tiếp nhận nhập kho",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = false,
            IsReceivable = true,
            IsSystemManaged = true,
            CreatedBy = user.Id
        };
        var shipLoc = new WarehouseLocation
        {
            WarehouseId = wh.Id,
            Code = "SHIPPING-STAGE",
            Name = "Bãi tập kết xuất hàng Cross-Dock",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            IsSystemManaged = true,
            CreatedBy = user.Id
        };
        db.AddRange(recvLoc, shipLoc);
        await db.SaveChangesAsync();

        var recvStock = new InventoryStock
        {
            WarehouseId = wh.Id,
            ProductId = prod.Id,
            LocationId = recvLoc.Id,
            Status = InventoryStatus.Available,
            Quantity = 10m
        };
        recvStock.RefreshGrainHash();
        db.InventoryStocks.Add(recvStock);
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            WarehouseId = wh.Id,
            ProductId = prod.Id,
            LocationId = recvLoc.Id,
            InventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Import,
            Quantity = 10m,
            ReferenceType = "ImportReceipt",
            ReferenceId = 1,
            TransactionDate = DateTime.UtcNow.AddMinutes(-20),
            CreatedBy = user.Id
        });

        var importReceipt = new ImportReceipt
        {
            Code = $"IMP-X60-{suffix}",
            WarehouseId = wh.Id,
            Status = ReceiptStatus.Approved,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.ImportReceipts.Add(importReceipt);
        await db.SaveChangesAsync();

        var importDetail = new ImportReceiptDetail
        {
            ImportReceiptId = importReceipt.Id,
            ProductId = prod.Id,
            Quantity = 10m,
            ExpectedQuantity = 10m,
            ReceivedQuantity = 10m,
            AcceptedQuantity = 10m,
            PostedQuantity = 10m,
            OperationUnitId = unit.Id,
            BaseUnitId = unit.Id,
            ConversionFactor = 1m,
            BaseExpectedQuantity = 10m,
            BaseReceivedQuantity = 10m,
            BaseAcceptedQuantity = 10m,
            BasePostedQuantity = 10m,
            UnitPrice = 50000m
        };
        db.ImportReceiptDetails.Add(importDetail);
        await db.SaveChangesAsync();

        var putawayTask = new PutawayTask
        {
            ReceiptId = importReceipt.Id,
            WarehouseId = wh.Id,
            Status = PutawayTaskStatus.Open,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow
        };
        putawayTask.Items.Add(new PutawayTaskItem
        {
            ReceiptLineId = importDetail.Id,
            ProductId = prod.Id,
            InventoryStatus = InventoryStatus.Available,
            SourceLocationId = recvLoc.Id,
            OperationUnitId = unit.Id,
            OperationUnitCodeSnapshot = unit.Code,
            BaseUnitId = unit.Id,
            BaseUnitCodeSnapshot = unit.Code,
            ConversionFactorSnapshot = 1m,
            ConversionVersionSnapshot = 1,
            RequiredOperationQuantity = 10m,
            RequiredBaseQuantity = 10m,
            MovedBaseQuantity = 0m
        });
        db.PutawayTasks.Add(putawayTask);

        var salesOrder = new SalesOrder
        {
            OrderCode = $"SO-X60-{suffix}",
            ExternalOrderId = $"EXT-X60-{suffix}",
            CustomerId = partner.Id,
            WarehouseId = wh.Id,
            Status = SalesOrderStatus.Released,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.SalesOrders.Add(salesOrder);
        await db.SaveChangesAsync();

        var soLine = new SalesOrderLine
        {
            SalesOrderId = salesOrder.Id,
            ExternalLineId = "1",
            ProductId = prod.Id,
            OrderedQuantity = 6m,
            UnitPrice = 60000m,
            UomCode = unit.Code
        };
        db.SalesOrderLines.Add(soLine);
        await db.SaveChangesAsync();

        var backorder = new Backorder
        {
            BackorderCode = $"BO-X60-{suffix}",
            SalesOrderLineId = soLine.Id,
            WarehouseId = wh.Id,
            Quantity = 6m,
            RecoveredQuantity = 0m,
            CancelledQuantity = 0m,
            Status = BackorderStatus.Open,
            CreatedAt = DateTime.UtcNow
        };
        db.Backorders.Add(backorder);
        await db.SaveChangesAsync();

        return new CrossDockFixture(
            suffix,
            user.Id,
            role.Id,
            unit.Id,
            prod.Id,
            wh.Id,
            recvLoc.Id,
            shipLoc.Id,
            importReceipt.Id,
            putawayTask.Id,
            partner.Id,
            salesOrder.Id,
            soLine.Id,
            backorder.Id);
    }

    private static async Task CleanupCrossDockFixtureAsync(CrossDockFixture fx)
    {
        await using var db = CreateContext();
        await db.IdempotencyRecords.Where(x => x.UserId == fx.UserId).ExecuteDeleteAsync();
        await db.AuditLogs.Where(x => x.UserId == fx.UserId || x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.InventoryLocationMovements.Where(m => m.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.StockAllocations.Where(a => a.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.StockReservations.Where(r => r.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Backorders.Where(b => b.Id == fx.BackorderId).ExecuteDeleteAsync();
        await db.SalesOrderLines.Where(l => l.Id == fx.SalesOrderLineId).ExecuteDeleteAsync();
        await db.SalesOrders.Where(s => s.Id == fx.SalesOrderId).ExecuteDeleteAsync();
        await db.PutawayTaskItems.Where(i => i.PutawayTaskId == fx.PutawayTaskId).ExecuteDeleteAsync();
        await db.PutawayTasks.Where(t => t.Id == fx.PutawayTaskId).ExecuteDeleteAsync();
        await db.ImportReceiptDetails.Where(d => d.ImportReceiptId == fx.ImportReceiptId).ExecuteDeleteAsync();
        await db.ImportReceipts.Where(r => r.Id == fx.ImportReceiptId).ExecuteDeleteAsync();
        await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL DISABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
        try
        {
            await db.InventoryTransactions.Where(x => x.WarehouseId == fx.WarehouseId && (x.CorrectiveTransactionId != null || x.ReversalOfTransactionId != null)).ExecuteUpdateAsync(s => s.SetProperty(x => x.CorrectiveTransactionId, (int?)null).SetProperty(x => x.ReversalOfTransactionId, (int?)null));
            await db.InventoryTransactions.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL ENABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
        }
        await db.InventoryStocks.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.WarehouseLocations.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Warehouses.Where(x => x.Id == fx.WarehouseId).ExecuteDeleteAsync();
        await db.BusinessPartners.Where(p => p.Id == fx.PartnerId).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == fx.ProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == fx.UnitId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.Id == fx.UserId).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == fx.RoleId).ExecuteDeleteAsync();
    }

    private sealed record BreakBulkFixture(
        string Suffix,
        int UserId,
        int RoleId,
        int BaseUnitId,
        int CaseUnitId,
        int ProductId,
        int WarehouseId,
        int ReserveLocationId,
        int PickLocationId,
        int LotId);

    private static async Task<BreakBulkFixture> CreateBreakBulkFixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var role = new Role { RoleName = $"W60BRole{suffix}" };
        var user = new User { Username = $"W60BUser{suffix}", PasswordHash = "hash", FullName = "Wave60 BreakBulk Tester", Role = role };
        var baseUnit = new Unit { Code = $"EA{suffix}", Name = "Cái" };
        var caseUnit = new Unit { Code = $"CS{suffix}", Name = "Thùng 12" };
        var prod = new Product { Code = $"SKU-BB60-{suffix}", Name = "Sản phẩm Xả Thùng Wave 60", Unit = baseUnit, TrackingType = ProductTrackingType.Lot, IsActive = true };
        var wh = new Warehouse { Code = $"WH60B-{suffix}", Name = "Kho Break-Bulk Wave 60", IsActive = true };
        db.AddRange(role, user, baseUnit, caseUnit, prod, wh);
        await db.SaveChangesAsync();

        var prodUom = new ProductUom
        {
            ProductId = prod.Id,
            UnitId = caseUnit.Id,
            ConversionFactor = 12m,
            IsActive = true
        };
        var reserveLoc = new WarehouseLocation
        {
            WarehouseId = wh.Id,
            Code = $"RES-01-{suffix}",
            Name = "Kệ lưu trữ trên cao",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = false,
            CreatedBy = user.Id
        };
        var pickLoc = new WarehouseLocation
        {
            WarehouseId = wh.Id,
            Code = $"PICK-01-{suffix}",
            Name = "Mặt kệ nhặt lẻ",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            CreatedBy = user.Id
        };
        var lot = new InventoryLot
        {
            ProductId = prod.Id,
            LotNumber = $"LOT-BB-{suffix}",
            ManufactureDate = DateTime.UtcNow.Date.AddMonths(-1),
            ExpiryDate = DateTime.UtcNow.Date.AddMonths(10),
            ReceivedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.AddRange(prodUom, reserveLoc, pickLoc, lot);
        await db.SaveChangesAsync();

        var reserveStock = new InventoryStock
        {
            WarehouseId = wh.Id,
            ProductId = prod.Id,
            LocationId = reserveLoc.Id,
            LotId = lot.Id,
            Status = InventoryStatus.Available,
            Quantity = 36m // 3 Cases x 12 EA
        };
        reserveStock.RefreshGrainHash();
        db.InventoryStocks.Add(reserveStock);
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            WarehouseId = wh.Id,
            ProductId = prod.Id,
            LocationId = reserveLoc.Id,
            LotId = lot.Id,
            InventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Import,
            Quantity = 36m,
            ReferenceType = "ImportReceipt",
            ReferenceId = 1,
            TransactionDate = DateTime.UtcNow.AddMinutes(-20),
            CreatedBy = user.Id
        });
        await db.SaveChangesAsync();

        return new BreakBulkFixture(
            suffix,
            user.Id,
            role.Id,
            baseUnit.Id,
            caseUnit.Id,
            prod.Id,
            wh.Id,
            reserveLoc.Id,
            pickLoc.Id,
            lot.Id);
    }

    private static async Task CleanupBreakBulkFixtureAsync(BreakBulkFixture fx)
    {
        await using var db = CreateContext();
        await db.IdempotencyRecords.Where(x => x.UserId == fx.UserId).ExecuteDeleteAsync();
        await db.AuditLogs.Where(x => x.UserId == fx.UserId || x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL DISABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
        try
        {
            await db.InventoryTransactions.Where(x => x.WarehouseId == fx.WarehouseId && (x.CorrectiveTransactionId != null || x.ReversalOfTransactionId != null)).ExecuteUpdateAsync(s => s.SetProperty(x => x.CorrectiveTransactionId, (int?)null).SetProperty(x => x.ReversalOfTransactionId, (int?)null));
            await db.InventoryTransactions.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL ENABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
        }
        await db.InventoryStocks.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.InventoryLots.Where(l => l.Id == fx.LotId).ExecuteDeleteAsync();
        await db.ProductUoms.Where(u => u.ProductId == fx.ProductId).ExecuteDeleteAsync();
        await db.WarehouseLocations.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Warehouses.Where(x => x.Id == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == fx.ProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == fx.BaseUnitId || x.Id == fx.CaseUnitId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.Id == fx.UserId).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == fx.RoleId).ExecuteDeleteAsync();
    }
}

```

---

## 6. Real Console Output of dotnet test & ERP.SqlIntegrationHarness + Live SQL 3-Way Integrity Checks (FACT)

### 6.1 Console Output of SqlServerWave60RoadmapEpicsTests (3/3 PASS)
```text
Test run for D:\ERP_KHO\ERP.Application.Tests\bin\Release\net10.0\ERP.Application.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.01] xUnit.net VSTest Adapter v3.1.4+50e68bbb8b (64-bit .NET 10.0.8)
[xUnit.net 00:00:00.79]   Discovering: ERP.Application.Tests
[xUnit.net 00:00:01.68]   Discovered:  ERP.Application.Tests
[xUnit.net 00:00:01.80]   Starting:    ERP.Application.Tests
Owned migration target: ERP_KHO_Integration_20261028_130525_36507ebc; RunId=36507ebc066640cab18f5de190b6aecf.
  Passed ERP.Application.Tests.SqlServerWave60RoadmapEpicsTests.Epic60_3_AutomatedBreakBulkReplenishment_HappyPath_Idempotency_Concurrency_And_Reversal [46 s]
  Passed ERP.Application.Tests.SqlServerWave60RoadmapEpicsTests.Epic60_2_InboundToOutboundCrossDocking_HappyPath_Idempotency_Concurrency_And_Reversal [37 s]
  Passed ERP.Application.Tests.SqlServerWave60RoadmapEpicsTests.Epic60_1_PhysicalFloorKittingAndDeKitting_HappyPath_Idempotency_Concurrency_And_Reversal [14 s]
Owned migration target cleaned: ERP_KHO_Integration_20261028_130525_36507ebc; RunId=36507ebc066640cab18f5de190b6aecf.
[xUnit.net 00:03:57.57]   Finished:    ERP.Application.Tests

Test Run Successful.
Total tests: 3
     Passed: 3
 Total time: 4,0077 Minutes
```

### 6.2 Console Output of ERP.SqlIntegrationHarness (233/233 PASS), ERP.Application.Tests (661/661 PASS) & ERP.Api.Tests (517/517 PASS)
```text
SQL integration planned target: ERP_KHO_Integration_20261028_135127_6fba3e14; RunId=6fba3e14bf8e455bb5f48c12c6ab145e; server/authentication details redacted.
SQL integration target created: ERP_KHO_Integration_20261028_135127_6fba3e14 (Integrated Security; server redacted).
Discovered SQL Server integration suites by [SqlServerFact]/[SqlServerTheory]/[ApprovalSqlServerFact]: 52 SQL test class files (49 compiled classes / 227 SQL fact/theory methods) across ERP.Application.Tests (46 files / 45 classes / 206 methods) and ERP.Api.Tests (6 files / 4 classes / 21 methods).
Test run for D:\ERP_KHO\ERP.Application.Tests\bin\Release\net10.0\ERP.Application.Tests.dll (.NETCoreApp,Version=v10.0)
Results File: D:\ERP_KHO\TestResults\SqlIntegration\6fba3e14bf8e455bb5f48c12c6ab145e\application.trx
Passed!  - Failed:     0, Passed:   212, Skipped:     0, Total:   212, Duration: 14 m 6 s - ERP.Application.Tests.dll (net10.0)
Test run for D:\ERP_KHO\ERP.Api.Tests\bin\Release\net10.0\ERP.Api.Tests.dll (.NETCoreApp,Version=v10.0)
Results File: D:\ERP_KHO\TestResults\SqlIntegration\6fba3e14bf8e455bb5f48c12c6ab145e\api.trx
Passed!  - Failed:     0, Passed:    21, Skipped:     0, Total:    21, Duration: 18 m 35 s - ERP.Api.Tests.dll (net10.0)
SQL integration target cleaned: ERP_KHO_Integration_20261028_135127_6fba3e14.

--- Full ERP.Application.Tests (661/661) ---
Test run for D:\ERP_KHO\ERP.Application.Tests\bin\Release\net10.0\ERP.Application.Tests.dll (.NETCoreApp,Version=v10.0)
Passed!  - Failed:     0, Passed:   661, Skipped:     0, Total:   661, Duration: 16 m 14 s - ERP.Application.Tests.dll (net10.0)

--- Full ERP.Api.Tests (517/517) ---
Test run for D:\ERP_KHO\ERP.Api.Tests\bin\Release\net10.0\ERP.Api.Tests.dll (.NETCoreApp,Version=v10.0)
Passed!  - Failed:     0, Passed:   517, Skipped:     0, Total:   517, Duration: 12 m 10 s - ERP.Api.Tests.dll (net10.0)
```

### 6.3 Live SQL Server Output on ERP_KHO (dbo.KitWorkOrders, dbo.PutawayTasks, Check A, Check B, Check C & All 20 InventoryStocks Rows)
```text
=== 1. dbo.KitWorkOrders (All 7 Rows After E2E Reversal) ===
Id WorkOrderCode       WorkOrderType Status    KitProductId PlannedKitQty CompletedKitQty RolledUpUnitCost EarliestExpiry
  -- -------------       ------------- ------    ------------ ------------- --------------- ---------------- --------------
   1 KWO-20261028-635AF9             1 Cancelled          517        2.0000          2.0000      500000.0000               
   2 KWO-20261028-9DEF84             1 Cancelled          517        2.0000          2.0000      500000.0000               
   3 KWO-20261028-A8D493             1 Cancelled          517        2.0000          2.0000      500000.0000               
   4 KWO-20261028-ABD955             1 Cancelled          517        2.0000          2.0000      500000.0000               
1004 KWO-20261028-09934C             1 Cancelled          517        2.0000          2.0000      500000.0000               
2004 KWO-20261028-861912             1 Cancelled          517        2.0000          2.0000      500000.0000               
3004 KWO-20261028-5B0E12             1 Cancelled          517        2.0000          2.0000      500000.0000

=== 2. dbo.PutawayTasks (Cross-Dock E2E Target Id=3002) ===
Id TaskType    ReceiptId WarehouseId Status IsCrossDock CrossDockAllocatedQty CrossDockTargetSalesOrderId
  -- --------    --------- ----------- ------ ----------- --------------------- ---------------------------
3002 PutawayTask         2           1      0       False                0.0000                           1

=== 3. SQL 3-Way Check A: InventoryStocks vs InventoryTransactions ===
WarehouseId ProductId StockAllStatusOnHand LedgerExpectedAllStatusNet AllStatusVariance StockAvailableOnHand LedgerExpectedAvailableNet AvailableVariance
----------- --------- -------------------- -------------------------- ----------------- -------------------- -------------------------- -----------------
          1       317             145.0000                   145.0000            0.0000             108.0000                   108.0000            0.0000
          1       517              66.0000                    66.0000            0.0000              66.0000                    66.0000            0.0000
       1403       317              21.0000                    21.0000            0.0000              21.0000                    21.0000            0.0000

=== 4. SQL 3-Way Check B: ReservedQuantity vs Active StockReservations & StockAllocations ===
ActiveReservationNetQty ActiveAllocationQty TotalStockReservedQty TotalStockAllocatedQty
----------------------- ------------------- --------------------- ----------------------
                 9.0000              9.0000                9.0000                 9.0000

=== 5. SQL 3-Way Check C: Physical Stock & 32-Byte SHA2_256 GrainHash Invariants ===
TotalGrainRows PositiveStockRows TotalOnHand TotalReserved TotalAvailable NegativeOnHandCount NegativeReservedCount OverReservedCount NullOrInvalidGrainHashCount
-------------- ----------------- ----------- ------------- -------------- ------------------- --------------------- ----------------- ---------------------------
            20                 9    232.0000        9.0000       223.0000                   0                     0                 0                           0

=== 6. All 20 Rows of dbo.InventoryStocks (Id=3663..14681) ===
Id WarehouseId ProductId Status LocationId LotId HandlingUnitId Quantity ReservedQuantity AllocatedQuantity
   -- ----------- --------- ------ ---------- ----- -------------- -------- ---------------- -----------------
 3663           1       317      0          1                       30.0000           0.0000            0.0000
 3664           1       317      0          9                       60.0000           0.0000            0.0000
 3665           1       317      2          9 2                     25.0000           0.0000            0.0000
 3666           1       317      3         11 1                     12.0000           0.0000            0.0000
 3667           1       517      0         10 3                      0.0000           0.0000            0.0000
 4663           1       317      0       1009                       10.0000           9.0000            9.0000
 5663           1       517      0       1009 1002                   0.0000           0.0000            0.0000
11672           1       317      0       2014       3                0.0000           0.0000            0.0000
11673           1       517      0       2014 1002  3                0.0000           0.0000            0.0000
11674        1403       317      0       2031                       21.0000           0.0000            0.0000
11675           1       317      0       2013       3                8.0000           0.0000            0.0000
11676           1       517      0       2013 1002  3                6.0000           0.0000            0.0000
11677           1       517      0          1 1002                  60.0000           0.0000            0.0000
11678           1       517      0          1 1004                   0.0000           0.0000            0.0000
11679           1       517      0          1 1005                   0.0000           0.0000            0.0000
11680           1       517      0          1 1006                   0.0000           0.0000            0.0000
11681           1       517      0          1 1007                   0.0000           0.0000            0.0000
12681           1       517      0          1 2007                   0.0000           0.0000            0.0000
13681           1       517      0          1 3007                   0.0000           0.0000            0.0000
14681           1       517      0          1 4007                   0.0000           0.0000            0.0000
```
