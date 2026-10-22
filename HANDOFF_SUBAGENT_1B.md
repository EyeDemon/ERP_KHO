# HANDOFF REPORT: SUBAGENT 1B (Core WMS Ledger & Outbound Engine `[P0-03]`)

**Wave:** 53  
**Subagent Role:** Core WMS Ledger & Outbound Engine (`[P0-03]`)  
**Status:** COMPLETED  
**Encoding Check (`scripts/check-encoding.ps1`):** PASSED (Exit Code 0)

---

## 1. Executive Summary

All 5 inventory and outbound integrity gaps assigned to **Subagent 1B** (`OD-04`, `OD-01`, `OD-08`, `OD-02` & `GAP-P0-03`, and `GAP-P0-02` & `GAP-P1-04` / `GAP-P1-02` / `GAP-P1-05`) have been implemented strictly within the 6 exclusively owned files. No files outside the exclusive boundary were modified.

---

## 2. Files Modified & Exact `File:Line` Coordinates

### 2.1 `ERP.Infrastructure/Repositories/ExportReceiptRepository.cs`
- **`GetByIdWithDetailsAsync` (`L17-L83`)**:
  - Added `.Include(e => e.SalesOrder)` (`L27`, `L40`) so `ExportReceipt.SalesOrder` is eagerly loaded for both the full projection and the lightweight fallback query (when `Users`/`Units` are unseeded in unit tests).
  - Added fallback resolution (`L58-L77`) to attach `receipt.SalesOrder` by `SalesOrderId` or `MarketplaceOrderId` if not already populated by navigation fixup.
- **`GetListAsync` (`L85-L103`)**:
  - Added `.Include(e => e.SalesOrder)` (`L92`).
- **`IExportReceiptBundleResolver` Implementation (`L124-L159`)**:
  - Implemented `GetActiveBundleComponentsAsync` (`L124-L135`), `GetSalesOrderLinesAsync` (`L137-L147`), and `GetActiveGiftProductIdsAsync` (`L149-L159`) on `ExportReceiptRepository` so `ExportReceiptService` can resolve BOM components, `SalesOrderLine` metadata (`IsFreeGift`, `ParentBundleProductId`), and active `PromotionGiftRules` without introducing an Infrastructure dependency in `ERP.Application`.

### 2.2 `ERP.Application/Services/ExportReceiptService.cs`
- **`CreateAsync` (`L72-L270`)**:
  - Mapped `SalesOrderId = dto.SalesOrderId` and `ShipmentId = dto.ShipmentId` onto `ExportReceipt` (`L97-L98`).
  - Resolved `SalesOrderLine` gift/bundle flags and active `ProductBundleComponents` via `IExportReceiptBundleResolver` (`L113-L237`), expanding bundle parent lines into component `ExportReceiptDetail` lines with `ParentBundleProductId = detailDto.ProductId` and proportional `UnitPrice`, while propagating `IsFreeGift = true` (`UnitPrice = 0m`) for promotional free gifts (`GAP-P0-02`, `GAP-P1-04`).
  - Grouped stock reservation validation and physical deduction by `ProductId` (`L103-L111`, `L243-L253`) so multi-line/component receipts deduct accurately per SKU.
- **`ApproveCoreAsync` (`L306-L346`) & `DispatchAsync` (`L423-L458`)**:
  - Added `EnsureNotCancelledOrPendingPutback(receipt)` guard (`L311`, `L429`, defined at `L555-L566`) that blocks approval and dispatch when `receipt.Status == ReceiptStatus.CancelPendingPutback` or `receipt.SalesOrder?.Status is SalesOrderStatus.Cancelled or SalesOrderStatus.CancelPendingPutback`, throwing `ConcurrencyException` with `ErrorCode = "ORDER_CANCELLED_PUTBACK_REQUIRED"` and `ex.Data["ErrorCode"] = "ORDER_CANCELLED_PUTBACK_REQUIRED"` (`OD-04`).
  - Added `AddGroupedExportTransactionsAsync` (`L511-L553`) to group physical stock deductions and `InventoryTransaction` audit entries by `ProductId` while preserving per-line `ExportReceiptDetail.UnitCost`.
- **`CalculateReceiptTotals` (`L663-L676`) & `MapToDto` (`L703-L735`)**:
  - Excluded `IsFreeGift` lines from `TotalAmount`, `TotalVatAmount`, and `FinalAmount` (`L665-L671`) and surfaced `IsFreeGift` and `ParentBundleProductId` on `ExportReceiptDetailDto` (`L717-L718`) (`GAP-P1-04`).
- **`IExportReceiptBundleResolver` Interface (`L793-L798`)**:
  - Defined application-layer contract for optional bundle/gift resolution.

### 2.3 `ERP.Infrastructure/Repositories/InventoryStockRepository.cs`
- **`GetByProductAndWarehouseAsync` (`L17-L33`) & `GetAvailableQuantityAsync` (`L116-L128`)**:
  - Excluded `WarehouseLocationType.Receiving` staging bins and expired lots (`s.Lot.ExpiryDate.Value.Date >= todayUtc`), and deducted `(s.ReservedQuantity > s.AllocatedQuantity ? s.ReservedQuantity : s.AllocatedQuantity)` and `s.LockedQuantity` from `s.Quantity` so `AllocatedQuantity` (which is a subset of `ReservedQuantity` during bin allocation) is not double-subtracted while still protecting buckets where `AllocatedQuantity > ReservedQuantity` (`OD-01`, Round 4 Cause A).
- **`ConsumeReservationWithBreakdownAsync` (`L236-L253`)**:
  - Updated `OriginalValues` snapshot and preserved state (`trackedEntry.OriginalValues.SetValues(tracked); trackedEntry.State = EntityState.Unchanged;`) after syncing `tracked.Quantity` / `tracked.ReservedQuantity` following `ExecuteUpdateAsync`, preventing EF Core `DetectChanges()` from marking the entity `Modified` with a stale `RowVersion` on subsequent `SaveChangesAsync()` (Round 4 Cause B).
- **`AllocateAsync` (`L417-L610`)**:
  - Included `.Include(s => s.Location)` and `.Include(s => s.Lot)` (`L434-L435`), filtered out `WarehouseLocationType.Receiving` (`L451`) and expired lots (`L454`) (`OD-01`).
  - Computed available unreserved capacity (`L500`, `L509`, `L588`) as `stock.Quantity - Math.Max(stock.ReservedQuantity, stock.AllocatedQuantity) - stock.LockedQuantity` and guarded the relational `ExecuteUpdateAsync` predicate (`L540`) with `x.Quantity - (x.ReservedQuantity > x.AllocatedQuantity ? x.ReservedQuantity : x.AllocatedQuantity) - x.LockedQuantity >= take` (Round 4 Cause A).
  - Updated `OriginalValues` snapshot and preserved state on `trackedEntry` (`L561-L581`) via `trackedEntry.OriginalValues.SetValues(tracked); trackedEntry.State = EntityState.Unchanged;` after `ExecuteUpdateAsync` so in-memory synchronization of `ReservedQuantity` / `Quantity` does not cause `DetectChanges()` to mark the entity `Modified` with a stale `RowVersion` (Round 4 Cause B).

### 2.4 `ERP.Infrastructure/Services/StockReservationService.cs`
- **`RebuildBalancesAsync` (`L701-L925`)**:
  - Loaded `InventoryTransactions` (`L724-L729`) and removed the single-bucket `group.Count() == 1` restriction so multi-bin products (`RECEIVING`, `STORAGE`, etc.) are reconciled per bucket across all 9 dimensions (`TenantId`, `WarehouseId`, `ProductId`, `LocationId`, `LotId`, `SerialId`, `HandlingUnitId`, `Status`, `OwnerId`) (`L765-L910`) (`OD-08`).
  - Handled both two-legged (`LocationId` set on each leg with signed `Quantity`) and single-row (`FromLocationId` / `ToLocationId`) `TransactionType.Move` transactions, as well as `Import`, `Export`, `Adjustment`, and `TransferOut` transactions (`L835-L896`).
  - Updated `bucket.Quantity = ledgerQty` whenever `hasMatchingLedgerTx && ledgerQty >= 0m` (`L904-L907`).

### 2.5 `ERP.Infrastructure/Services/PutawayService.cs`
- **`MoveAsync` Destination Bucket Lookup (`L616-L633`)**:
  - Added `x.HandlingUnitId == sourceBucket.HandlingUnitId && x.OwnerId == sourceBucket.OwnerId` (`L626-L627`) to the destination `InventoryStock` lookup so stock moves never merge into a bucket belonging to a different LPN (`HandlingUnitId`) or consignor (`OwnerId`) (`OD-02`).
- **`MoveAsync` Putback Completion Hook (`L752-L755`) & `CompletePutbackOrderCancellationAsync` (`L813-L989`)**:
  - When a putback task (`ReferenceType == "PUTBACK"` or `TaskNumber` starting with `"PUTBACK-"`) transitions to `PutawayTaskStatus.Completed`, `CompletePutbackOrderCancellationAsync` resolves the linked `ExportReceipt` and `SalesOrder` (`L819-L878`).
  - Transitions `ExportReceipt.Status` from `ReceiptStatus.CancelPendingPutback` to `ReceiptStatus.Cancelled` (`L880-L885`) and `SalesOrder.Status` from `SalesOrderStatus.CancelPendingPutback` to `SalesOrderStatus.Cancelled` (`L887-L892`).
  - Releases active `StockReservations` (`StockReservationStatus.Released`, `ReservedQuantity = 0`) and `StockAllocations` (`StockAllocationStatus.Released`, `AllocatedQuantity = 0`) and decrements `ReservedQuantity` and `AllocatedQuantity` on matching `InventoryStock` buckets (`L895-L988`) (`GAP-P0-03`).

### 2.6 `ERP.Infrastructure/Services/SalesOrderBackorderService.cs`
- **`CreateSalesOrderAsync` (`L74-L298`)**:
  - **SaaS Quota 115% Hard-Stop (`GAP-P1-05`, `L83-L108`)**: Queries active `TenantSubscription` for `command.TenantId`; when `MonthlyOrderQuota > 0` and `CurrentMonthOrderCount >= (int)Math.Ceiling(MonthlyOrderQuota * 1.15m)`, throws `ForbiddenException` with `ex.Data["ErrorCode"] = "SUBSCRIPTION_QUOTA_EXCEEDED"`. Otherwise increments `CurrentMonthOrderCount`.
  - **BOM / Kit Dynamic Explosion (`GAP-P0-02`, `L119-L176`)**: Queries active `ProductBundleComponents` for incoming line `ProductId`s and expands parent kit lines into child component `SalesOrderLine`s with `ParentBundleProductId = line.ProductId`, `OrderedQty = line.Quantity * comp.Quantity`, and proportional `UnitPrice`.
  - **Promotional Free-Gift Auto-Attach (`GAP-P1-02`, `L222-L280`)**: Evaluates active `PromotionGiftRules` where `orderSubtotal >= rule.MinOrderAmount` and trigger product quantity `>= rule.MinTriggerQuantity`. Checks remaining ATP (`availableByProduct - committedToRegularLines >= rule.GiftQuantity`); only when ATP is sufficient, attaches a free-gift `SalesOrderLine` (`IsFreeGift = true`, `UnitPrice = 0m`, `AllocatedQty = rule.GiftQuantity`) and creates the corresponding `StockReservation`; if gift stock is 0 or insufficient, skips gift attachment without creating any backorder.
