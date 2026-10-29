# DATA INTEGRITY AUDIT (`WMS-AUDIT-20261026-1805`)

- **Audit Run ID:** `WMS-AUDIT-20261026-1805`
- **Database Verified:** `ERP_KHO` on `(localdb)\MSSQLLocalDB` (`Heavenly\LOCALDB#7B6FFF75`)
- **Database Isolation Compliance:** `100% PASS_VERIFIED` — Zero queries or connections touched `PMS_VIKING_2025`, `PMS_QLDH_VIKING_2025`, or any database other than `ERP_KHO`. No SQL `sa` login used.
- **Snapshots Captured:**
  - Pre-Audit Baseline: `evidence/data-snapshots/00_initial_db_snapshot.txt` (`2026-10-26 11:18:50 UTC`)
  - Post-Audit Final: `evidence/data-snapshots/01_final_db_snapshot.txt` (`2026-10-26 12:27:42 UTC`)

---

## 1. Nine-Dimension Inventory Conservation & Non-Negative Invariants (`Layer 1 & Layer 2`)

Every stock balance row in `InventoryStocks` is keyed across the canonical 9 inventory dimensions (`TenantId, WarehouseId, ProductId, Status, LocationId, LotId, SerialId, HandlingUnitId, OwnerId`) with deterministic `GrainHash` (`SHA2_256`) and row-level locking (`AcquireInventoryAllocationLocksAsync` with `UPDLOCK, ROWLOCK, REPEATABLEREAD`).

| Invariant Check | SQL / Reconciliation Verification | Observed Value | Status |
|---|---|---|---|
| Negative On-Hand Check (`Quantity < 0`) | `SELECT SUM(CASE WHEN Quantity < 0 THEN 1 ELSE 0 END) FROM InventoryStocks` | `0` rows | `PASS_VERIFIED` |
| Negative Reserved Check (`ReservedQuantity < 0`) | `SELECT SUM(CASE WHEN ReservedQuantity < 0 THEN 1 ELSE 0 END) FROM InventoryStocks` | `0` rows | `PASS_VERIFIED` |
| Over-Reserved Available Stock Check (`Quantity < ReservedQuantity AND Status = 0`) | `SELECT SUM(CASE WHEN Quantity < ReservedQuantity AND Status = 0 THEN 1 ELSE 0 END) FROM InventoryStocks` | `0` rows | `PASS_VERIFIED` |
| Available Quantity Identity (`Available = Quantity - ReservedQuantity`) | Verified across all `12` 9-dim grain rows (`TotalOnHand = 172.0000`, `TotalReserved = 9.0000`, `TotalAvailable = 163.0000`) | `0` discrepancies | `PASS_VERIFIED` |
| 9-Dimension Ledger vs. Balance Rebuild (`POST /api/inventory-reconciliation/rebuild-9dim-balances`) | Verified on `/inventory-reconciliation` (`TC-INV-006-01`, `TC-INV-006-03`): `3/3 dòng khớp (100%) • Số dư: 195 • Kỳ vọng: 195 • Lệch ròng: 0` | `Δ = 0` | `PASS_VERIFIED` |
| Stock Status Segregation (`Available=0`, `Quarantine=2`, `Damaged=3`) | `BIN-A01` holds `60.0000` (`Status=0` Available) and `25.0000` (`Status=2` Quarantine) in separate grain rows (`Id=3664` vs `Id=3665`); `BIN-DMG-01` holds `12.0000` (`Status=3` Damaged, `Id=3666`) | Segregated | `PASS_VERIFIED` |

---

## 2. Final 9-Dimension Balance Table (`InventoryStocks` Snapshot)

| Row Id | Warehouse | Location | Product Code | Status | OnHandQty | ReservedQty | AvailableQty |
|---:|---|---|---|---:|---:|---:|---:|
| `3663` | `WH01` (`Id=1`) | `RECEIVING` (`Id=1`) | `QA-E2E-001` (`Id=317`) | `0` (Available) | `30.0000` | `0.0000` | `30.0000` |
| `3664` | `WH01` (`Id=1`) | `BIN-A01` (`Id=9`) | `QA-E2E-001` (`Id=317`) | `0` (Available) | `60.0000` | `0.0000` | `60.0000` |
| `3665` | `WH01` (`Id=1`) | `BIN-A01` (`Id=9`) | `QA-E2E-001` (`Id=317`) | `2` (Quarantine) | `25.0000` | `0.0000` | `25.0000` |
| `3667` | `WH01` (`Id=1`) | `BIN-A02` (`Id=10`) | `QA-LOT-002` (`Id=517`) | `0` (Available) | `0.0000` | `0.0000` | `0.0000` |
| `3666` | `WH01` (`Id=1`) | `BIN-DMG-01` (`Id=11`) | `QA-E2E-001` (`Id=317`) | `3` (Damaged) | `12.0000` | `0.0000` | `12.0000` |
| `4663` | `WH01` (`Id=1`) | `BIN-A01-01` (`Id=1009`) | `QA-E2E-001` (`Id=317`) | `0` (Available) | `10.0000` | `9.0000` | `1.0000` |
| `5663` | `WH01` (`Id=1`) | `BIN-A01-01` (`Id=1009`) | `QA-LOT-002` (`Id=517`) | `0` (Available) | `0.0000` | `0.0000` | `0.0000` |
| `11675` | `WH01` (`Id=1`) | `BIN-A01-03` (`Id=2013`) | `QA-E2E-001` (`Id=317`) | `0` (Available) | `8.0000` | `0.0000` | `8.0000` |
| `11676` | `WH01` (`Id=1`) | `BIN-A01-03` (`Id=2013`) | `QA-LOT-002` (`Id=517`) | `0` (Available) | `6.0000` | `0.0000` | `6.0000` |
| `11672` | `WH01` (`Id=1`) | `RCV-01` (`Id=2014`) | `QA-E2E-001` (`Id=317`) | `0` (Available) | `0.0000` | `0.0000` | `0.0000` |
| `11673` | `WH01` (`Id=1`) | `RCV-01` (`Id=2014`) | `QA-LOT-002` (`Id=517`) | `0` (Available) | `0.0000` | `0.0000` | `0.0000` |
| `11674` | `WH02` (`Id=1403`) | `BIN-HN-01` (`Id=2031`) | `QA-E2E-001` (`Id=317`) | `0` (Available) | `21.0000` | `0.0000` | `21.0000` |

---

## 3. Immutable Append-Only Ledger & Reversal Chain (`INV-001`, `INV-002`, `INV-003`)

- **Ledger Summary (`InventoryTransactions`):**
  - `TransactionType = 0` (Inbound / Opening): `10` entries, total `240.0000`
  - `TransactionType = 3` (Adjustment / Stocktake): `3` entries, total `3.0000`
  - `TransactionType = 4` (Outbound / Issue): `4` entries, total `4.0000`
  - `TransactionType = 5` (Reversal / Return): `4` entries, total `4.0000`
  - `TransactionType = 7` (Putaway / Internal Move): `1` entry, total `5.0000`
  - `TransactionType = 9` (Transfer): `6` entries, total `15.0000`
- **Reversal Integrity (`POST /api/inventory-reversals/verify-reversal-integrity` -> `REV-INT-20261026-1151`):**
  - Verified on `/inventory-reversals` (`TC-INV-004-01`, `TC-INV-004-02`): `0` orphan reversals, `100%` append-only ledger linkage (`ReversalOfTransactionId` / `ReversedByTransactionId`), no `UPDATE` or `DELETE` on committed ledger rows.

---

## 4. Audit Trail, Idempotency & Outbox Sync (`INV-004`, `CON-001`, `AUD-001`)

- **Audit Trail (`AuditLogs`):** Every mutating operation executed during the browser audit created a persistent `AuditLogs` row with `UserId = 712` (`qa_admin`), UTC `Timestamp`, `Action`, `EntityName`, and `EntityId` (verified up to `AuditLog.Id = 97674` in `01_final_db_snapshot.txt`).
- **Idempotency Enforcement (`IdempotentCommandFilter`):**
  - Verified in `TC-IDEMP-001`: Sending identical `Idempotency-Key` + identical payload replayed `200 OK` without duplicate side effects; sending the same `Idempotency-Key` with a different payload returned `409 Conflict` (`IDEMPOTENCY_CONFLICT`).
- **Integration Outbox (`IntegrationOutboxMessages`):**
  - Verified in `TC-INV-006-03` (`POST /api/inventory-reconciliation/dispatch-outbox`): `4/4` outbox messages (`100%`) synchronized with `0` dead-letter failures.
