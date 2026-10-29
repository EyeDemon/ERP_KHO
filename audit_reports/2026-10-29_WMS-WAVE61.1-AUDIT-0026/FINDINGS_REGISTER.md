# FINDINGS REGISTER — ERP WMS SaaS MASTER AUDIT (`WMS-MASTER-AUDIT`)

- **Audit ID:** `WMS-WAVE61.1-AUDIT`
- **Run ID:** `2026-10-29_WMS-WAVE61.1-AUDIT-0026`
- **Audit Mode:** `EVIDENCE-BACKED AUDIT & WAVE 57–61.1 SURGICAL REMEDIATION + 90-DAY ROADMAP CLOSURE VERIFICATION`
- **Inspected Git Baselines:**
  - `3ddab7261d4606f094239d4933923aea657aca28` (`HEAD~8` — code baseline when `WMS-AUDIT-20261026-1805` was authored)
  - `d9b24530ba5826ae0d5f4f107e8f91be3f6f6009` (`HEAD~7` on `feat/WMS-61-project-development` — initial audit baseline)
  - `f49a433` (`HEAD~6` — Wave 57 Remediation Closure Baseline: Migration `20261027130209_Wave57AuditRemediationSchemaAndImmutableLedgerTrigger.cs` applied and verified across Gates 1..5)
  - `ca0f177` (`HEAD~5` — Wave 58 Architectural Closure Baseline: Decoupled `ErpKhoDbContext` from all `42 / 42` API Controllers, closing `F-004`)
  - `0b75fd4` (`HEAD~4` — Wave 59 & 59.1 30-Day Roadmap Epics `30.1..30.3` & Live Browser E2E `UNV-02` Closure Baseline)
  - `37eb5fe` (`HEAD~3` — Wave 60 60-Day Roadmap Epics `60.1..60.3` Implementation Baseline: `KitWorkOrder`, Cross-Docking, and Break-Bulk Replenishment)
  - `5ce6242` (`HEAD~2` — Wave 60.1 Remediation Closure Baseline: Resolved `F-011` and `F-012`)
  - `2eafbed` (`HEAD~1` — Wave 61 Remediation [`F-013`, `F-014`] & 90-Day Roadmap [`Epic 90.1..90.3`] Baseline)
  - **Wave 61.1 Surgical Remediation & Full Verbatim Evidence Closure Baseline (`feat/WMS-61-project-development`):** Resolves `F-015` (`S2 — MEDIUM`: restores 7th dimension `HandlingUnitId` & `HandlingUnitContents.Quantity` inside `foreach (var (origOut, origIn, legQty) in matchedOrigPairs)` in `ExecuteBreakBulkReplenishmentAsync`, acquires SQL transaction + `UPDLOCK, ROWLOCK, HOLDLOCK` on `dbo.Warehouses` + 9D `AcquireInventoryAllocationLocksAsync` before duplicate check in `EvaluateAbcSlottingAndTaskInterleavingAsync` with strict `1x200 + 1x409` concurrency assertions, and removes the fake `SKU-DEFAULT` fallback in `SyncAccountingAndEInvoiceOutboxAsync`), `F-016` (`S2 — MEDIUM`: implements real 9D bin-to-bin re-slotting mutation on `InventoryStocks` + `GrainHash` + paired `InventoryTransactions` `AbcSlottingReSlotting` + `InventoryLocationMovements` and full 9D reversal in `EvaluateAbcSlottingAndTaskInterleavingAsync`), `F-017` (`S3 — LOW`: publishes 100% untruncated `WAVE61_1_CODE_AND_SQL_EVIDENCE.md`), and `F-018` (`S3 — LOW`: synchronizes exact method/route names across all audit reports), achieving **`16 / 16` code/data/report findings `CLOSED` (`17 / 18` total findings `CLOSED` including `F-003a/b/c`, `0` `OPEN`, and `1` `BLOCKED` external Notion `404` `F-010`)**
- **Live SQL Server Target:** `ERP_KHO` on `(localdb)\MSSQLLocalDB` (Windows Integrated Auth `HEAVENLY\van40`)

---

## 1. Executive Rollup of Audit Findings (Post-Wave-61.1 Closure State)

### 1.1 Rollup by Severity (`S0`–`S3`), Priority (`P0`–`P4`) & Open vs Closed Status

| Severity | Definition | Total Logged | Closed in Waves 57–61.1 | Remaining Open / Blocked | Open / Blocked Finding IDs | Priority Distribution (Open/Blocked) |
| --- | --- | ---: | ---: | ---: | --- | --- |
| **`S0 — Critical / Blocker`** | Active cross-tenant data leak, systemic negative stock across production SKUs, or non-compiling release build | **0** | **0** | **0** | None | `P0: 0` |
| **`S1 — High`** | Data integrity variance on specific lot/location ledger rebuild path or missing database-level append-only trigger | **2** (`F-001`, `F-002`) | **2** (`F-001`, `F-002`) | **0** | None (`100% CLOSED`) | `P1: 0` |
| **`S2 — Medium`** | Secondary workflows persisting state in `AuditLogs.NewValues` JSON, Fat Controllers injecting `DbContext`, optional idempotency scopes, manual HTTP trigger for scheduled jobs, Cross-Dock/Break-Bulk reversal stock clamping/lookup guard, Break-Bulk 7th-dimension `HandlingUnitId` reversal / Epic 90.1 lock / Epic 90.3 fallback guard, or Epic 90.1 9D bin-to-bin re-slotting mutation | **7** (`F-003`, `F-004`, `F-005`, `F-006`, `F-013`, `F-015`, `F-016`) | **7** (`F-003a/b/c`, `F-004`, `F-005`, `F-006`, `F-013`, `F-015`, `F-016`) | **0** | None (`100% CLOSED`) | `P1: 0`, `P2: 0` |
| **`S3 — Low / Hygiene`** | Single ledger timestamp vs dual `ActualOccurredAtUtc`/`PostedAtUtc`, unrouted legacy frontend component, SQL test harness class-name filter, Notion child page `404` sharing drift, UI Break-Bulk badge UOM label, audit report metadata drift, duplicate reversal test assertion / `§6.2` row prose, evidence line-count truncation, or conceptual vs actual method/route naming in prose | **9** (`F-007`, `F-008`, `F-009`, `F-010`, `F-011`, `F-012`, `F-014`, `F-017`, `F-018`) | **8** (`F-007`, `F-008`, `F-009`, `F-011`, `F-012`, `F-014`, `F-017`, `F-018`) | **1** (`BLOCKED`) | `F-010` (3 Notion child links `404`, `BLOCKED` on external Notion sharing) | `P2: 0`, `P3: 1` |
| **TOTAL** | **All Consolidated Findings (`F-001`..`F-018`; `16/16` Code/Data/Report Findings `CLOSED`)** | **18** (`17` Code/Data/Report + `1` External Notion) | **17 (`CLOSED` — `100%` of actionable findings)** | **1 (`0 OPEN`, `1 BLOCKED S3`)** | Open: `0 S0`, `0 S1`, `0 S2`, `0 S3 OPEN`, `1 S3 BLOCKED` (`F-010`) | `P0: 0`, `P1: 0`, `P2: 0`, `P3: 1`, `P4: 0` |

### 1.2 Rollup by Finding Type, Verification Status & Confidence

| Finding ID | Title | Severity | Priority | Finding Type | Verification Label | Confidence | Status |
| --- | --- | :---: | :---: | --- | :---: | :---: | :---: |
| **`F-001`** | `WH01 / QA-LOT-002` (`ProductId=517`) `-60.0000` Stock-vs-Ledger Variance Caused by `RebuildBalancesAsync` Iterating Only Existing `InventoryStocks` Rows & Clamping Negative Sums | `S1` | `P1` | `DATA_INTEGRITY_AND_RECONCILIATION` | `FACT` | `HIGH` | **`CLOSED`** *(Wave 57)* |
| **`F-002`** | Missing SQL Server Trigger (`sys.triggers = 0`) Enforcing Immutable Append-Only `InventoryTransactions` at Database Layer (`[INV-002]`) | `S1` | `P1` | `DATABASE_CONSTRAINT_AND_SECURITY` | `FACT` | `HIGH` | **`CLOSED`** *(Wave 57)* |
| **`F-003`** | Three Phase-1.5 / Secondary Workflows Persist Operational State in `AuditLogs.NewValues` JSON Instead of Relational Tables (`Stocktakes`, `PutToWall/Wave`, `3PL Snapshots`) | `S2` | `P1` | `SCHEMA_WORKAROUND_AUDITLOG_JSON` | `FACT` | `HIGH` | **`CLOSED`** *(Wave 57)* |
| **`F-004`** | `36 / 42` API Controllers Directly Inject or Reference `ErpKhoDbContext` & `12` Controllers Exceed `900` Lines (`[ARCH-003]` Fat Controller Violation) | `S2` | `P2` | `ARCHITECTURE_FAT_CONTROLLER` | `FACT` | `HIGH` | **`CLOSED`** *(Wave 58)* |
| **`F-005`** | `IdempotentCommandFilter.IsOptionalHeaderScope` Makes `Idempotency-Key` Optional on `6` Scopes (`d9b2453`) & `2` Endpoints Lack `[IdempotentCommand]` (`[CON-001]`) | `S2` | `P2` | `CONCURRENCY_AND_IDEMPOTENCY` | `FACT` | `HIGH` | **`CLOSED`** *(Wave 57)* |
| **`F-006`** | Only `2` `BackgroundService` Workers Registered in `Program.cs`; Reservation Expiry Sweep, Daily 3-Way Reconciliation & 23:59 3PL Snapshot Require HTTP Trigger | `S2` | `P2` | `BACKGROUND_WORKER_AUTOMATION` | `FACT` | `HIGH` | **`CLOSED`** *(Wave 57)* |
| **`F-007`** | `InventoryTransaction` Stores Single `TransactionDate` Instead of Dual `ActualOccurredAtUtc` and `PostedAtUtc` Columns (`OD-08` / `Spec 29 §3.4`) | `S3` | `P2` | `DOMAIN_MODEL_MINOR_DEVIATION` | `FACT` | `HIGH` | **`CLOSED`** *(Wave 57)* |
| **`F-008`** | Unrouted Legacy `WarehouseStructure.tsx` Still Imports Static `demoWarehouseStructures` (`[FE-002]`) & `18` Frontend Pages Exceed `500` Lines | `S3` | `P2` | `FRONTEND_ORPHAN_MOCK_AND_GOD_COMPONENTS` | `FACT` | `HIGH` | **`CLOSED`** *(Wave 57)* |
| **`F-009`** | `ERP.SqlIntegrationHarness` Class-Name Filter (`Contains("SqlServer")`) Selects `34` of `49` SQL Test Classes (Skipping `15` Classes / `38` Methods) & Prior Browser Audit Header Counted `98` vs `109` Rows | `S3` | `P3` | `TEST_HARNESS_AND_REPORT_DISCREPANCY` | `FACT` | `HIGH` | **`CLOSED`** *(Wave 57)* |
| **`F-010`** | Three Priority Notion Child Page Links (`Spec 28`, `Spec 37`, `Spec 39`) Return HTTP `404 object_not_found` via Notion API | `S3` | `P3` | `DOCUMENTATION_ACCESS_DRIFT` | `FACT` | `HIGH` | **`BLOCKED`** *(Notion 404)* |
| **`F-011`** | Break-Bulk Replenishment Badge on `/inventory-movements` (`InventoryMovements.tsx`) Displays `+12 THÙNG` Instead of Base UOM `+12 ĐV` When Breaking `1 Thùng = 12 ĐV` | `S3` | `P3` | `FRONTEND_UOM_LABEL_DISPLAY_DEFECT` | `FACT` | `HIGH` | **`CLOSED`** *(Wave 60.1)* |
| **`F-012`** | Audit Package Metadata Drift in `SOURCE_IMPLEMENTATION_MAP.md`, `TEST_COVERAGE_GAPS.md`, `AUDIT_STATE.md` & `EXECUTIVE_SUMMARY.md` (`d9b2453` Header, Missing `37eb5fe` SHA, Wave 57 SQL Row Counts & Roadmap Name Alignment) | `S3` | `P3` | `AUDIT_REPORT_METADATA_SYNCHRONIZATION` | `FACT` | `HIGH` | **`CLOSED`** *(Wave 60.1)* |
| **`F-013`** | `Math.Max(0m, ...)` Clamping During Cross-Dock Reversal in `PutawayService.cs`, Optional Unreversed Lookup in Break-Bulk Reversal (`InventoryControlOperationsService.cs`), and Unstandardized `TransactionType.Reversal` Ledger Filter Rule | `S2` | `P2` | `DATA_INTEGRITY_AND_REVERSAL_INVARIANT` | `FACT` | `HIGH` | **`CLOSED`** *(Wave 61)* |
| **`F-014`** | Missing Duplicate-Reversal `ConflictException` Assertions in `SqlServerWave60RoadmapEpicsTests.cs` (`Epic60_2` & `Epic60_3`) and Prose Drift in `SOURCE_IMPLEMENTATION_MAP.md §6.2` 9 Positive `InventoryStocks` Rows | `S3` | `P3` | `TEST_ASSERTION_AND_DOC_ALIGNMENT` | `FACT` | `HIGH` | **`CLOSED`** *(Wave 61)* |
| **`F-015`** | Break-Bulk Reversal 7th Dimension `HandlingUnitId` & `HandlingUnitContents` Restoration When `request.SourceHandlingUnitId == null`, Epic 90.1 Pre-Check Transaction/Lock & Loose Concurrency Assertion, and Fake `SKU-DEFAULT` Fallback in Epic 90.3 | `S2` | `P1` | `INVARIANT_AND_CONCURRENCY` | `FACT` | `HIGH` | **`CLOSED`** *(Wave 61.1)* |
| **`F-016`** | `EvaluateAbcSlottingAndTaskInterleavingAsync` (`Epic 90.1`) Only Staged Outbox/Audit Without Mutating 9D `InventoryStocks`, `InventoryTransactions` (`AbcSlottingReSlotting`), or `InventoryLocationMovements` | `S2` | `P1` | `SPEC_VS_CODE_MISMATCH` | `FACT` | `HIGH` | **`CLOSED`** *(Wave 61.1)* |
| **`F-017`** | Line-Count Cap (`+ 318/196/188 more lines`) Truncated Method Bodies in `WAVE61_1_CODE_AND_SQL_EVIDENCE.md` | `S3` | `P2` | `EVIDENCE_TRUNCATION` | `FACT` | `HIGH` | **`CLOSED`** *(Wave 61.1)* |
| **`F-018`** | Conceptual Method & Route Names Cited in `EXECUTIVE_SUMMARY.md`, `COMPETITIVE_GAP_AND_ROADMAP.md`, and `TEST_COVERAGE_GAPS.md` Differed from Actual Code Names | `S3` | `P3` | `REPORT_ACCURACY` | `FACT` | `HIGH` | **`CLOSED`** *(Wave 61.1)* |

---

## 2. Detailed Forensic Findings (`F-001` through `F-018`)

### `F-001` — `WH01 / QA-LOT-002` (`ProductId=517`) `-60.0000` Stock-vs-Ledger Variance Caused by `RebuildBalancesAsync` Iterating Only Existing `InventoryStocks` Rows & Clamping Negative Sums
- **Severity / Priority:** `S1` / `P1`
- **Finding Type:** `DATA_INTEGRITY_AND_RECONCILIATION`
- **Related Requirement & Workflow IDs:** `TR-002`, `TR-044`, `TR-072`, `WF-03.2`, `WF-10.2`
- **Related Subagent Cross-References:** `SOURCE_IMPLEMENTATION_MAP.md §6.2 Check A`, `BUSINESS_WORKFLOW_AUDIT.md §2 WF-03 & WF-10`
- **Verification Label & Confidence:** `FACT` | `HIGH`
- **Source & Database Evidence (`File:Line` & Live SQL on `ERP_KHO`):**
  1. **Live SQL 3-Way Check A on `ERP_KHO`:**
     - `WH01 / QA-E2E-001 (ProductId=317)`: `SUM(InventoryStocks.Quantity) = 145.0000` vs `SUM(Signed InventoryTransactions.Quantity) = 145.0000` (`Variance = 0.0000` — **PASS**).
     - `WH02 / QA-E2E-001 (ProductId=317)`: `SUM(InventoryStocks.Quantity) = 21.0000` vs `SUM(Signed InventoryTransactions.Quantity) = 21.0000` (`Variance = 0.0000` — **PASS**).
     - `WH01 / QA-LOT-002 (ProductId=517)`: `SUM(InventoryStocks.Quantity) = 6.0000` vs `SUM(Signed InventoryTransactions.Quantity) = 66.0000` (`Variance = -60.0000` — **MISMATCH**).
  2. **Granular Row Inspection in `ERP_KHO` (`12` total `InventoryStocks` rows `Id=3663, 3664, 3665, 3666, 3667, 4663, 5663, 11672, 11673, 11674, 11675, 11676`):**
     - `dbo.InventoryStocks` for `ProductId=517` (`4` rows):
       - `Id=3667` (`WarehouseId=1, LocationId=10, LotId=3, HandlingUnitId=NULL, OwnerId=NULL, Status=0`): `Quantity = 0.0000`
       - `Id=5663` (`WarehouseId=1, LocationId=1009 [BIN-A01-01], LotId=1002, HandlingUnitId=NULL, OwnerId=NULL, Status=0`): `Quantity = 0.0000`
       - `Id=11673` (`WarehouseId=1, LocationId=2014 [RCV-01], LotId=1002, HandlingUnitId=3, OwnerId=NULL, Status=0`): `Quantity = 0.0000`
       - `Id=11676` (`WarehouseId=1, LocationId=2013 [STAGE-01], LotId=1002, HandlingUnitId=3, OwnerId=NULL, Status=0`): `Quantity = 6.0000`
     - `dbo.InventoryTransactions` for `ProductId=517` (`14` rows):
       - `Id=3718` (`TransactionType=0 [Import]`, `LocationId=1 [TIEP-NHAN], LotId=1002`): `+65.0000`
       - `Id=8722` (`TransactionType=9 [Move]`, `FromLocationId=1, LocationId=1009, ToLocationId=1009, LotId=1002`): `5.0000` (moves `5` units from `LocationId=1` to `LocationId=1009`, leaving `+60.0000` at `LocationId=1, LotId=1002` and `+5.0000` at `LocationId=1009, LotId=1002`)
       - `Id=8733` (`TransactionType=7 [Ship]`, `LocationId=1009 [BIN-A01-01], LotId=1002`): `-5.0000`
       - `Id=24849, 25852, 26848, 27848` (`TransactionType=4 [TransferOut]`, `ReferenceType='VAS_DEKIT'`, `LocationId=1009, LotId=1002`): `4 × -1.0000 = -4.0000`
       - `Id=24850, 25853, 26849, 27849` (`TransactionType=5 [TransferIn]`, `ReferenceType='VAS_DEKIT'`, `LocationId=1009, LotId=NULL`): `4 × +1.0000 = +4.0000`
       - `Id=28849` (`TransactionType=0 [Import]`, `LocationId=2014 [RCV-01], LotId=1002, HandlingUnitId=3`): `+6.0000`
       - `Id=28854, 28855` (`TransactionType=9 [Move]`, `2014 -> 2013, LotId=1002, HandlingUnitId=3`): `-6.0000 / +6.0000`
  3. **Code Trace (`d:\ERP_KHO\ERP.Infrastructure\Services\StockReservationService.cs#L716-L861`):**
     - `RebuildBalancesAsync` (`L716, L764`) only iterates over **existing** `dbo.InventoryStocks` rows (`var stockRows = await _context.InventoryStocks.Where(...).ToListAsync()`) and never inserts missing `InventoryStocks` rows for 9D buckets that have non-zero net `InventoryTransactions` in the ledger — specifically `(LocationId=1 [TIEP-NHAN], LotId=1002)` (`+60.0000`) and `(LocationId=1009 [BIN-A01-01], LotId=NULL)` (`+4.0000`).
     - Meanwhile, for `Id=5663` (`LocationId=1009, LotId=1002`), the signed ledger sum is `+5.0000 (Id=8722) - 5.0000 (Id=8733) - 4.0000 (VAS_DEKIT TransferOut) = -4.0000 < 0m`, which `RebuildBalancesAsync` (`L853-L855` `Math.Max(0m, ledgerQty)`) clamps to `0.0000`.
- **Operational Impact:**
  - Running `RebuildBalancesAsync` when a 9D ledger bucket lacks a pre-existing `InventoryStocks` row (or when `VAS_DEKIT` shifts lot grain without a matching source balance) drops the missing bucket's `+60.0000` balance and clamps negative intermediate buckets to `0.0000`.
- **Remediation Recommendation:**
  1. Update `StockReservationService.RebuildBalancesAsync` (`L716-L861`) to group all 9D buckets present in `dbo.InventoryTransactions`, upsert any missing `InventoryStocks` rows (`(LocationId=1, LotId=1002)`), and preserve lot-grain consistency during `ProductsController.Dekit` (`VAS_DEKIT`).
  2. Never silently clamp negative reconstructed ledger sums (`Math.Max(0m, ledgerQty)`) during `RebuildBalancesAsync` without raising a reconciliation exception or logging a quarantine alert (`SD-04`).
- **Wave 57 Closure Evidence (`FACT` — Status: `CLOSED`):**
  - **Code & Migration Remediation (`SD-04 Option A`):**
    1. Updated [StockReservationService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/StockReservationService.cs#L716-L905) (`RebuildBalancesAsync`) to discover the union of all 9D buckets across both `dbo.InventoryStocks` and `dbo.InventoryTransactions`, automatically instantiate missing `InventoryStock` rows with canonical 32-byte `SHA2_256` `GrainHash` before acquiring 9D locks, and throw a deterministic `DomainValidationException` (`INV_REBUILD_NEGATIVE_LEDGER_BUCKET`) instead of silently clamping `Math.Max(0m, ledgerQty)` when any bucket sum is negative.
    2. Fixed [ProductsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ProductsController.cs#L1200-L1295) (`Dekit` / `VAS_DEKIT`) so component `TransferIn` stock and ledger rows preserve the parent's `LotId` when the component SKU is lot-tracked (`compProduct.IsLotTracked ? sourceStock.LotId : null`).
    3. Applied migration [20261027130209_Wave57AuditRemediationSchemaAndImmutableLedgerTrigger.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Migrations/20261027130209_Wave57AuditRemediationSchemaAndImmutableLedgerTrigger.cs) which appended compensating `VAS_DEKIT_LOT_ALIGNMENT` ledger pairs for `ProductId=517` (`LocationId=1009, LotId=NULL -> LotId=1002`) and rebuilt the missing `(LocationId=1 [TIEP-NHAN], LotId=1002)` `InventoryStocks` row (`Id=11677`, `Quantity = 60.0000`).
  - **Live SQL Verification on `ERP_KHO` & Gate 2 Tests (`FACT`):**
    - Live SQL Check A on `ERP_KHO` now returns **`0.0000` variance across all 3 SKU-warehouse pairs**: `WH01 / QA-E2E-001 = 145.0000`, `WH01 / QA-LOT-002 = 66.0000` (`Id=11677` `60.0000` + `Id=11676` `6.0000`), and `WH02 / QA-E2E-001 = 21.0000`.
    - Live SQL Check C confirms `13` rows in `dbo.InventoryStocks` (`Id=3663..11677`), `TotalOnHand = 232.0000`, `TotalReserved = 9.0000`, `TotalAvailable = 223.0000`, `0` negative rows, and `13/13` valid 32-byte `GrainHash`.
    - Verified by [SqlServerWave57AuditRemediationTests.cs](file:///d:/ERP_KHO/ERP.Application.Tests/SqlServerWave57AuditRemediationTests.cs) (`3/3 PASS` in Gate 2).

---

### `F-002` — Missing SQL Server Trigger (`sys.triggers = 0`) Enforcing Immutable Append-Only `InventoryTransactions` at Database Layer (`[INV-002]`)
- **Severity / Priority:** `S1` / `P1`
- **Finding Type:** `DATABASE_CONSTRAINT_AND_SECURITY`
- **Related Requirement & Workflow IDs:** `TR-071`, `WF-10.1`
- **Related Subagent Cross-References:** `SOURCE_IMPLEMENTATION_MAP.md §3.3 & §6.1`, `BUSINESS_WORKFLOW_AUDIT.md §2 WF-10`
- **Verification Label & Confidence:** `FACT` | `HIGH`
- **Source & Database Evidence (`File:Line` & Live SQL on `ERP_KHO`):**
  1. **Application-Layer Protection (`PASS`):** `d:\ERP_KHO\ERP.Infrastructure\Persistence\ErpKhoDbContext.cs#L234-L260` (`EnforceAppendOnlyInventoryLedger()`) inspects `ChangeTracker.Entries<InventoryTransaction>()` on every `SaveChanges`/`SaveChangesAsync` and throws `InvalidOperationException("INV_LEDGER_IMMUTABLE: Bảng sổ cái tồn kho (InventoryTransactions) là bất biến (Append-Only)...")` whenever `EntityState.Modified` or `EntityState.Deleted` is detected.
  2. **Database-Layer Trigger Inspection (Initial Audit `FAIL`):**
     - Initial query `SELECT name, parent_id, is_disabled FROM sys.triggers;` on `ERP_KHO` returned **`0` rows** prior to Wave 57.
- **Operational Impact:**
  - Without a database trigger, raw Dapper/ADO.NET commands or direct SQL scripts were not blocked by SQL Server from mutating or deleting rows in `dbo.InventoryTransactions`.
- **Remediation Recommendation:**
  - Add an official EF Core migration executing `CREATE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions AFTER UPDATE, DELETE AS BEGIN RAISERROR('INV_LEDGER_IMMUTABLE: InventoryTransactions is append-only.', 16, 1); ROLLBACK TRANSACTION; END;`.
- **Wave 57 Closure Evidence (`FACT` — Status: `CLOSED`):**
  - **Code & Migration Remediation (`[INV-002]`):**
    1. Created and applied EF Core migration [20261027130209_Wave57AuditRemediationSchemaAndImmutableLedgerTrigger.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Migrations/20261027130209_Wave57AuditRemediationSchemaAndImmutableLedgerTrigger.cs) which installs `dbo.TR_InventoryTransactions_Immutable_AppendOnly` (`AFTER UPDATE, DELETE`) on `dbo.InventoryTransactions` and declares `t.HasTrigger("TR_InventoryTransactions_Immutable_AppendOnly")` in [InventoryTransactionConfiguration.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/Configurations/InventoryTransactionConfiguration.cs).
    2. The trigger unconditionally blocks `DELETE` and blocks any `UPDATE` unless `d.CorrectiveTransactionId IS NULL AND i.CorrectiveTransactionId IS NOT NULL` while verifying that all 26 other columns (`TenantId`, `WarehouseId`, `ProductId`, `LocationId`, `FromLocationId`, `ToLocationId`, `InventoryStatus`, `FromStatus`, `ToStatus`, `LotId`, `SerialId`, `HandlingUnitId`, `OwnerId`, `TransactionType`, `Quantity`, `UnitCost`, `ReferenceId`, `ReferenceType`, `ReferenceCode`, `Notes`, `CreatedByUserId`, `TransactionDate`, `ActualOccurredAtUtc`, `PostedAtUtc`, `ReversalOfTransactionId`, `GrainHash`) remain strictly identical between `inserted` and `deleted`, otherwise raising `RAISERROR('INV_LEDGER_IMMUTABLE (50001): InventoryTransactions is append-only. UseInventoryReversalService for compensating entries.', 16, 1); ROLLBACK TRANSACTION;`.
  - **Live SQL & Test Verification (`FACT`):**
    - `SELECT name, is_disabled FROM sys.triggers WHERE name = 'TR_InventoryTransactions_Immutable_AppendOnly'` on `ERP_KHO` returns **`1` active trigger (`is_disabled = False`)**.
    - Verified by [SqlServerWave57AuditRemediationTests.cs](file:///d:/ERP_KHO/ERP.Application.Tests/SqlServerWave57AuditRemediationTests.cs) (`Trigger_BlocksRawSqlUpdateAndDelete_AndAllowsOnlyOneTimeCorrectiveLinkWithoutColumnTampering` — `PASS`).

---

### `F-003` — Three Phase-1.5 / Secondary Workflows Persist Operational State in `AuditLogs.NewValues` JSON Instead of Relational Tables (`Stocktakes`, `PutToWall/Wave`, `3PL Snapshots`)
- **Severity / Priority:** `S2` / `P1`
- **Finding Type:** `SCHEMA_WORKAROUND_AUDITLOG_JSON`
- **Related Requirement & Workflow IDs:** `TR-059`, `TR-064`, `TR-075`, `WF-06.1`, `WF-07.2`, `WF-11.5`
- **Related Subagent Cross-References:** `SOURCE_IMPLEMENTATION_MAP.md §4.1`, `BUSINESS_REQUIREMENT_TRACEABILITY.md §3.3`
- **Verification Label & Confidence:** `FACT` | `HIGH`
- **Source & Database Evidence (`File:Line`):**
  1. **Stocktake Multi-Attempt Recount History & `StocktakeStatus` Enum (`F-003a`):**
     - Prior to Wave 57, `StocktakesController.cs` persisted blind recount attempts in `AuditLogs.NewValues` JSON (`Action == "STOCKTAKE_RECOUNT_ATTEMPT"`), and `StocktakeStatus.cs` omitted `PendingRecount`.
  2. **Put-to-Wall Sortation Slots & Wave Release Metadata (`F-003b`):**
     - Prior to Wave 57, `PickingTasksController.cs` stored Wave release records (`EntityName = "PickingWave"`) and Put-to-Wall sortation slot state (`EntityName = "PutToWallSortation"`) in `dbo.AuditLogs.NewValues` JSON.
  3. **3PL Daily Storage Occupancy Snapshots & Billing Statements (`F-003c`):**
     - Prior to Wave 57, `ReportsController.cs` and `BusinessPartnersController.cs` stored 3PL daily storage snapshots and billing statements in `dbo.AuditLogs.NewValues` JSON.
- **Operational Impact:**
  - Storing operational state in `AuditLogs.NewValues` violated `PROJECT_RULES.md` Layer 1 and bypassed relational foreign keys, `decimal(19,4)` precision, and `RowVersion` optimistic concurrency.
- **Remediation Recommendation:**
  - Create an EF Core migration adding: (1) recount attempt columns on `StocktakeDetails` + `PendingRecount` enum state (`SD-01`), (2) `PickingWaves` and `PutToWallSlots` tables (`SD-02`), and (3) `ThreePlStorageSnapshots` and `ThreePlBillingStatements` tables (`SD-03`).
- **Wave 57 Closure Evidence (`FACT` — Status: `CLOSED`):**
  - **Code & Schema Remediation (`SD-01`, `SD-02`, `SD-03 Option A`):**
    1. **`F-003a` (`SD-01`):** Added `PendingRecount = 8` to [StocktakeStatus.cs](file:///d:/ERP_KHO/ERP.Domain/Enums/StocktakeStatus.cs#L15), added `CountAttempt1Qty`, `CountAttempt2Qty`, `CountAttempt3Qty` (`decimal(19,4)`), `CountAttemptCount` (`int`), `RecountReason` (`nvarchar(500)`), and `LastCountedByUserId` (`int?`) to [StocktakeDetail.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/StocktakeDetail.cs#L15-L21) and [StocktakeDetailConfiguration.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/Configurations/StocktakeDetailConfiguration.cs#L18-L32), and refactored [StocktakesController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/StocktakesController.cs) to read/write recount state directly on `dbo.StocktakeDetails`.
    2. **`F-003b` (`SD-02`):** Created first-class domain entities [PickingWave.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/PickingWave.cs) and [PutToWallSlot.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/PutToWallSlot.cs) with EF Core configurations ([PickingWaveConfiguration.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/Configurations/PickingWaveConfiguration.cs), [PutToWallSlotConfiguration.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/Configurations/PutToWallSlotConfiguration.cs) using `decimal(19,4)` and `RowVersion`), registered `DbSet<PickingWave>` and `DbSet<PutToWallSlot>` in `ErpKhoDbContext.cs`, and refactored [PickingTasksController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/PickingTasksController.cs) to persist waves and Put-to-Wall slots in `dbo.PickingWaves` and `dbo.PutToWallSlots`.
    3. **`F-003c` (`SD-03`):** Created first-class domain entities [ThreePlStorageSnapshot.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/ThreePlStorageSnapshot.cs) and [ThreePlBillingStatement.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/ThreePlBillingStatement.cs) with EF Core configurations ([ThreePlStorageSnapshotConfiguration.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/Configurations/ThreePlStorageSnapshotConfiguration.cs), [ThreePlBillingStatementConfiguration.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/Configurations/ThreePlBillingStatementConfiguration.cs) using `decimal(19,4)` and `RowVersion`), and refactored [ReportsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ReportsController.cs) and [BusinessPartnersController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/BusinessPartnersController.cs) to persist 3PL snapshots and billing statements in `dbo.ThreePlStorageSnapshots` and `dbo.ThreePlBillingStatements`.
  - **Live SQL & Test Verification (`FACT`):**
    - Verified in `ERP_KHO` via `INFORMATION_SCHEMA.COLUMNS` that all 4 new tables (`PickingWaves`, `PutToWallSlots`, `ThreePlStorageSnapshots`, `ThreePlBillingStatements`) and `StocktakeDetails` recount columns exist with `decimal(19,4)` precision, and verified in [Wave44OmnichannelAndSaasTests.cs](file:///d:/ERP_KHO/ERP.Api.Tests/Wave44OmnichannelAndSaasTests.cs), [Wave48To51FullClosureTests.cs](file:///d:/ERP_KHO/ERP.Api.Tests/Wave48To51FullClosureTests.cs), and [MigrationSnapshotParityTests.cs](file:///d:/ERP_KHO/ERP.Application.Tests/MigrationSnapshotParityTests.cs) (`PASS`).

---

### `F-004` — `36 / 42` API Controllers Directly Inject or Reference `ErpKhoDbContext` & `12` Controllers Exceed `900` Lines (`[ARCH-003]` Fat Controller Violation)
- **Severity / Priority:** `S2` / `P2`
- **Finding Type:** `ARCHITECTURE_FAT_CONTROLLER`
- **Related Requirement & Workflow IDs:** `ARCH-001`, `ARCH-003`, `ARCH-004`
- **Related Subagent Cross-References:** `SOURCE_IMPLEMENTATION_MAP.md §2.2, §4.1 & §5`
- **Verification Label & Confidence:** `FACT` | `HIGH`
- **Historical Source Evidence Prior to Wave 58 (`File:Line`):**
  - While `ERP.Domain.csproj` had `0` project/package dependencies (`[ARCH-001]` `100% PASS`), `36` out of `42` controllers in `d:\ERP_KHO\ERP.Api\Controllers\` directly injected or referenced `ErpKhoDbContext` (`_db` / `_context`) and executed LINQ queries, state transitions, or inline transactions inside controller actions.
  - Prior to Wave 58, only `6 / 42` controllers (`BackordersController.cs`, `ImportTemplatesController.cs`, `InventoryStocksController.cs`, `InventoryTransactionsController.cs`, `PartnerDebtsController.cs`, `ProductBarcodesController.cs`) were completely decoupled from `ErpKhoDbContext`.
  - The largest Fat Controllers prior to Wave 58 included `ReportsController.cs` (`3,247` lines), `SalesOrdersController.cs` (`2,336` lines), `ExportReceiptsController.cs` (`1,711` lines), `StocktakesController.cs` (`1,499` lines), `PackingSessionsController.cs` (`1,485` lines), `ProductsController.cs` (`1,425` lines), `ImportReceiptsController.cs` (`1,181` lines), `PickingTasksController.cs` (`1,123` lines), `PermissionsController.cs` (`972` lines), `ShipmentsController.cs` (`931` lines), and `InventoryControlController.cs` (`903` lines).
- **Operational Impact:**
  - Violated `[ARCH-003]` (*"nghiêm cấm tiêm `DbContext` hoặc viết `if/else` nghiệp vụ trong Controller"*) and increased maintenance complexity.
- **Remediation Recommendation:**
  - Extract all controller query/command logic into `ERP.Application` / `ERP.Infrastructure` services so `0 / 42` controllers reference `ErpKhoDbContext`.
- **Wave 58 Closure Evidence (`FACT` — Status: `CLOSED`):**
  - **Code & Clean Architecture Remediation (`[ARCH-001..004]`):**
    1. Extracted all direct `ErpKhoDbContext` queries, aggregations, and domain mutations out of the `36` controllers into dedicated Application/Infrastructure Services (`IWmsGovernanceOperationsService` / `WmsGovernanceOperationsService`, `IInventoryControlOperationsService` / `InventoryControlOperationsService`, `IShipmentOperationsService` / `ShipmentOperationsService`, `IPermissionOperationsService` / `PermissionOperationsService`, `IImportReceiptOperationsService` / `ImportReceiptOperationsService`, `IPickingTaskOperationsService` / `PickingTaskOperationsService`, `IStocktakeOperationsService` / `StocktakeOperationsService`, `IPackingSessionOperationsService` / `PackingSessionOperationsService`, `IProductOperationsService` / `ProductOperationsService`, `IExportReceiptOperationsService` / `ExportReceiptOperationsService`, `ISalesOrderOperationsService` / `SalesOrderOperationsService`, `IReportOperationsService` / `ReportOperationsService`, plus extended `IInventoryMovementService` and `IUserSessionService`), and registered all services in [Program.cs](file:///d:/ERP_KHO/ERP.Api/Program.cs).
    2. Removed `ErpKhoDbContext` fallback constructors from `SaasBillingController`, `DataImportsController`, `HandoverManifestsController`, `InventoryMovementsController`, `AuthController`, and `ProductCategoriesController`.
    3. Kept `ClosedXML` workbook parsing/generation strictly inside `ERP.Api/Infrastructure/` (`WmsControllerExcelHelper.cs`, `ReportsExcelHelper.cs`) so `ERP.Infrastructure.csproj` and `ERP.Application.csproj` remain free of `ClosedXML` (`[SEC-004]`).
  - **Forensic Verification (`FACT`):**
    - `@(Select-String -Path "ERP.Api/Controllers/*.cs" -Pattern "\bErpKhoDbContext\b").Count` equals **`0`** across all `42 / 42` controller files (`100%` Thin Controllers, every controller `< 400` lines).
    - Verified across all 5 Quality Gates (`dotnet build ERP.slnx -c Release` `0 Warning, 0 Error`; `ERP.Application.Tests` `655/655 PASS`; `ERP.Api.Tests` `517/517` inline / `544/544` expanded `PASS`; `ERP.SqlIntegrationHarness` `50` SQL test class files / `227` runtime SQL test cases `PASS`; Frontend Vitest `73/73` files / `406/406` tests `PASS`).

---

### `F-005` — `IdempotentCommandFilter.IsOptionalHeaderScope` Makes `Idempotency-Key` Optional on `6` Scopes (`d9b2453`) & `2` Endpoints Lack `[IdempotentCommand]` (`[CON-001]`)
- **Severity / Priority:** `S2` / `P2`
- **Finding Type:** `CONCURRENCY_AND_IDEMPOTENCY`
- **Related Requirement & Workflow IDs:** `TR-012`, `TR-021`, `WF-06`, `WF-07`, `WF-11`
- **Related Subagent Cross-References:** `SOURCE_IMPLEMENTATION_MAP.md §2.2 & §5.3`, `TEST_COVERAGE_GAPS.md §5 (GAP-TST-03)`
- **Verification Label & Confidence:** `FACT` | `HIGH`
- **Source Evidence (`File:Line`):**
  1. **`IsOptionalHeaderScope` Bypass Added in Commit `d9b2453`:**
     - `d:\ERP_KHO\ERP.Api\Filters\IdempotentCommandFilter.cs#L55-L59` and `#L273-L296` define `IsOptionalHeaderScope(string scope)`, which returns `true` for:
       - `Approvals.*` (`StartsWith("Approvals.")`)
       - `Stocktakes.*` (`StartsWith("Stocktakes.")`)
       - `Picking.PutToWallSort` and `Picking.ReleaseWave`
       - `PackingSessions.*` (`StartsWith("PackingSessions.")`)
       - `Shipments.*` (`StartsWith("Shipments.")`)
     - When a request to these endpoints omits the `Idempotency-Key` header, `L55-L59` executes `await next(); return;` without enforcing idempotency or returning `400 IDEMPOTENCY_KEY_REQUIRED`.
  2. **Endpoints Missing `[IdempotentCommand]` Attribute:**
     - `d:\ERP_KHO\ERP.Api\Controllers\ExportReceiptsController.cs#L1053-L1381` (`POST /api/v1/export-receipts/opening-balance-import`) mutates inventory balances and ledger entries via `IDataImportService` (`DataImportService.cs:41-346`) without `[IdempotentCommand]` on the controller action.
     - `d:\ERP_KHO\ERP.Api\Controllers\ImportReceiptsController.cs#L194-L432` (`RmaIntakeCheck` / `ExecuteRtvRefurbish` at `L554-L979`) executes RMA/RTV mutations without `[IdempotentCommand]`.
- **Operational Impact:**
  - If a client or PDA retries a request without sending `Idempotency-Key` on the 6 optional scopes (or on `ImportOpeningBalance` / `ExecuteRtvRefurbish`), the server will not intercept duplicate retries via `dbo.IdempotencyRecords` (`[CON-001]`).
- **Remediation Recommendation:**
  - Remove `IsOptionalHeaderScope` in `IdempotentCommandFilter.cs:273-296` once all test callers attach `Idempotency-Key` (note: `frontend/src/services/apiClient.ts:153-192` already auto-attaches `Idempotency-Key` on all mutating `POST/PUT/PATCH/DELETE` requests), and decorate `ImportOpeningBalance` and `ExecuteRtvRefurbish` with `[IdempotentCommand]`.
- **Wave 57 Closure Evidence (`FACT` — Status: `CLOSED`):**
  - **Code Remediation (`SD-05 Option A`):**
    1. Removed `IsOptionalHeaderScope` from [IdempotentCommandFilter.cs](file:///d:/ERP_KHO/ERP.Api/Infrastructure/IdempotentCommandFilter.cs#L51-L65) so every endpoint decorated with `[IdempotentCommand]` strictly requires a non-empty `Idempotency-Key` header (returning HTTP `400 IDEMPOTENCY_KEY_REQUIRED` if omitted).
    2. Added `[IdempotentCommand("...")]` attributes to [DataImportsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/DataImportsController.cs#L56) (`ImportOpeningBalance`, `"DataImports.OpeningBalance"`), [ExportReceiptsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ExportReceiptsController.cs#L1058) (`ImportOpeningBalance`, `"ExportReceipts.OpeningBalanceImport"`), and [ImportReceiptsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ImportReceiptsController.cs#L196) (`RmaIntakeCheck` at `L196` and `ExecuteRtvRefurbish` at `L555`).
    3. Updated `CreateIdempotentClient()` in [Wave44OmnichannelAndSaasTests.cs](file:///d:/ERP_KHO/ERP.Api.Tests/Wave44OmnichannelAndSaasTests.cs) and [Wave48To51FullClosureTests.cs](file:///d:/ERP_KHO/ERP.Api.Tests/Wave48To51FullClosureTests.cs) to attach `Idempotency-Key` headers on mutating requests (`517/517` inline / `544/544` expanded tests `PASS`).

---

### `F-006` — Only `2` `BackgroundService` Workers Registered in `Program.cs`; Reservation Expiry Sweep, Daily 3-Way Reconciliation & 23:59 3PL Snapshot Require HTTP Trigger
- **Severity / Priority:** `S2` / `P2`
- **Finding Type:** `BACKGROUND_WORKER_AUTOMATION`
- **Related Requirement & Workflow IDs:** `TR-053`, `TR-072`, `TR-075`, `WF-05.2`, `WF-10.2`, `WF-11.5`
- **Related Subagent Cross-References:** `SOURCE_IMPLEMENTATION_MAP.md §4.1`, `BUSINESS_WORKFLOW_AUDIT.md §2 WF-05, WF-10, WF-11`
- **Verification Label & Confidence:** `FACT` | `HIGH`
- **Source Evidence (`File:Line`):**
  - Prior to Wave 57, `d:\ERP_KHO\ERP.Api\Program.cs#L213-L214` registered only `2` `IHostedService` / `BackgroundService` workers (`IntegrationOutboxDispatcherJob`, `BuyerPiiRetentionAnonymizerJob`), while Soft Reservation Expiry Sweep (`TR-053`), Daily 3-Way Integrity Reconciliation (`TR-072`), and 3PL Daily Storage Occupancy Snapshot (`TR-075`) required explicit HTTP invocation.
- **Operational Impact:**
  - Without an external cron or registered `BackgroundService`, expired soft reservations, daily integrity checks, and 23:59 3PL storage snapshots only ran when invoked over HTTP.
- **Remediation Recommendation:**
  - Add lightweight `BackgroundService` wrappers in `ERP.Api/Hosting/` for `SoftReservationExpiryJob` (every 5 minutes), `DailyInventoryIntegrityJob` (nightly), and `ThreePlDailySnapshotJob` (daily at 23:59 UTC+7).
- **Wave 57 Closure Evidence (`FACT` — Status: `CLOSED`):**
  - **Code Remediation:**
    1. Created [SoftReservationExpiryJob.cs](file:///d:/ERP_KHO/ERP.Api/Hosting/SoftReservationExpiryJob.cs), [DailyInventoryIntegrityJob.cs](file:///d:/ERP_KHO/ERP.Api/Hosting/DailyInventoryIntegrityJob.cs), and [ThreePlDailySnapshotJob.cs](file:///d:/ERP_KHO/ERP.Api/Hosting/ThreePlDailySnapshotJob.cs) under `ERP.Api/Hosting/`.
    2. Registered all 3 new jobs via `builder.Services.AddHostedService<...>()` in [Program.cs](file:///d:/ERP_KHO/ERP.Api/Program.cs#L213-L217), bringing total hosted background workers to **`5`** (`IntegrationOutboxDispatcherJob`, `BuyerPiiRetentionAnonymizerJob`, `SoftReservationExpiryJob`, `DailyInventoryIntegrityJob`, `ThreePlDailySnapshotJob`).

---

### `F-007` — `InventoryTransaction` Stores Single `TransactionDate` Instead of Dual `ActualOccurredAtUtc` and `PostedAtUtc` Columns (`OD-08` / `Spec 29 §3.4`)
- **Severity / Priority:** `S3` / `P2`
- **Finding Type:** `DOMAIN_MODEL_MINOR_DEVIATION`
- **Related Requirement & Workflow IDs:** `TR-008`, `OD-08`, `WF-10.1`
- **Related Subagent Cross-References:** `BUSINESS_REQUIREMENT_TRACEABILITY.md §2.1 (TR-008) & §3.4`
- **Verification Label & Confidence:** `FACT` | `HIGH`
- **Source Evidence (`File:Line`):**
  - Prior to Wave 57, `ERP.Domain/Entities/InventoryTransaction.cs` defined only `TransactionDate` (`L30`) without explicit `ActualOccurredAtUtc` and `PostedAtUtc` columns (`OD-08` / `Spec 29 §3.4`).
- **Operational Impact:**
  - Without dual timestamps, backdated physical movements and SQL posting timestamps were not stored in separate columns.
- **Remediation Recommendation:**
  - Add `ActualOccurredAtUtc` (defaulting to `TransactionDate`) and `PostedAtUtc` columns to `InventoryTransaction` via an Expand-and-Contract EF Core migration.
- **Wave 57 Closure Evidence (`FACT` — Status: `CLOSED`):**
  - **Code & Schema Remediation (`OD-08`):**
    1. Added `ActualOccurredAtUtc` (`datetime2`) and `PostedAtUtc` (`datetime2`) to [InventoryTransaction.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/InventoryTransaction.cs#L31-L32), [InventoryTransactionConfiguration.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/Configurations/InventoryTransactionConfiguration.cs#L38-L44), and [ErpKhoDbContext.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/ErpKhoDbContext.cs#L668-L678) (`ApplyImmutableAuditMetadata`).
    2. Applied migration [20261027130209_Wave57AuditRemediationSchemaAndImmutableLedgerTrigger.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Migrations/20261027130209_Wave57AuditRemediationSchemaAndImmutableLedgerTrigger.cs) adding both columns to `dbo.InventoryTransactions` and backfilling existing rows from `TransactionDate`.

---

### `F-008` — Unrouted Legacy `WarehouseStructure.tsx` Still Imports Static `demoWarehouseStructures` (`[FE-002]`) & `18` Frontend Pages Exceed `500` Lines
- **Severity / Priority:** `S3` / `P2`
- **Finding Type:** `FRONTEND_ORPHAN_MOCK_AND_GOD_COMPONENTS`
- **Related Requirement & Workflow IDs:** `FE-002`, `Role 7 (God Component > 500 lines)`
- **Related Subagent Cross-References:** `SOURCE_IMPLEMENTATION_MAP.md §7.1 & §7.2`
- **Verification Label & Confidence:** `FACT` | `HIGH`
- **Source Evidence (`File:Line`):**
  1. **Unrouted Legacy Page Importing Static Demo Data:**
     - Active routing in `d:\ERP_KHO\frontend\src\routes\AppRoutes.tsx#L89` maps `/warehouse-structure` to `WarehouseLocations.tsx`, which uses real backend APIs. Prior to Wave 57, the unrouted legacy file `frontend/src/pages/WarehouseStructure.tsx` still resided in `frontend/src/pages/` and imported `demoWarehouseStructures`.
  2. **Frontend Component Size (`> 500` Lines Rule in `SKILL_ROUTING_PROTOCOL.md` Role 7):**
     - `18` page components in `d:\ERP_KHO\frontend\src\pages\` exceed `500` lines.
- **Operational Impact:**
  - Keeping unrouted `WarehouseStructure.tsx` in the repository triggered static `[FE-002]` mock-data scans.
- **Remediation Recommendation:**
  - Delete unrouted `frontend/src/pages/WarehouseStructure.tsx` and re-point `WarehouseStructure.test.tsx` to `WarehouseLocations.tsx`.
- **Wave 57 Closure Evidence (`FACT` — Status: `CLOSED`):**
  - **Code & Test Remediation:**
    1. Deleted the unrouted legacy orphan file `frontend/src/pages/WarehouseStructure.tsx` (`frontend/src/pages/` now has **`42`** non-test `.tsx` files with **`0` `[FE-002]` mock-data violations**).
    2. Updated [WarehouseStructure.test.tsx](file:///d:/ERP_KHO/frontend/src/pages/WarehouseStructure.test.tsx) to test the live routed [WarehouseLocations.tsx](file:///d:/ERP_KHO/frontend/src/pages/WarehouseLocations.tsx) component (`npm --prefix frontend run build` `Exit Code 0`, Vitest `73/73` files / `406/406` tests `PASS`).

---

### `F-009` — `ERP.SqlIntegrationHarness` Class-Name Filter (`Contains("SqlServer")`) Selects `34` of `49` SQL Test Classes (Skipping `15` Classes / `38` Methods) & Prior Browser Audit Header Counted `98` vs `109` Rows
- **Severity / Priority:** `S3` / `P3`
- **Finding Type:** `TEST_HARNESS_AND_REPORT_DISCREPANCY`
- **Related Requirement & Workflow IDs:** `TST-001`, `TST-002`
- **Related Subagent Cross-References:** `TEST_COVERAGE_GAPS.md §1.2, §3.1 & §5 (GAP-TST-05)`
- **Verification Label & Confidence:** `FACT` | `HIGH`
- **Source Evidence (`File:Line`):**
  1. **`ERP.SqlIntegrationHarness` Discovery Filter (`GAP-TST-05`):**
     - Prior to Wave 57, `d:\ERP_KHO\tools\ERP.SqlIntegrationHarness\Program.cs` filtered test classes by `t.Name.Contains("SqlServer", StringComparison.OrdinalIgnoreCase)`, selecting only `34` classes and skipping SQL integration classes whose class names did not contain `"SqlServer"`.
  2. **Prior Audit Report Browser Test Count Arithmetic:**
     - Reconciled in `TEST_COVERAGE_GAPS.md §3.1` (`109` table rows covering `111` distinct `TC-*` IDs).
- **Operational Impact:**
  - Filtering SQL integration runs by `"SqlServer"` in the class name left SQL test classes without `"SqlServer"` in their name out of standalone harness runs.
- **Remediation Recommendation:**
  - Update `ERP.SqlIntegrationHarness/Program.cs` to discover and execute all classes containing `[SqlServerFact]` or `[ApprovalSqlServerFact]` methods across both `ERP.Application.Tests` and `ERP.Api.Tests`.
- **Wave 57 Closure Evidence (`FACT` — Status: `CLOSED`):**
  - **Code & Gate 2 Verification:**
    1. Updated [Program.cs](file:///d:/ERP_KHO/tools/ERP.SqlIntegrationHarness/Program.cs) to inspect all compiled test classes across `ERP.Application.Tests` and `ERP.Api.Tests` for methods decorated with `[SqlServerFact]` or `[ApprovalSqlServerFact]` regardless of class name, plus added [SqlServerWave57AuditRemediationTests.cs](file:///d:/ERP_KHO/ERP.Application.Tests/SqlServerWave57AuditRemediationTests.cs).
    2. Executed `dotnet run --project tools/ERP.SqlIntegrationHarness/ERP.SqlIntegrationHarness.csproj -c Release --no-build`: discovered all **`50` SQL test class files (`47` compiled public classes / `221` decorated methods: `44` files / `43` classes / `200` methods in `ERP.Application.Tests` + `6` files / `4` classes / `21` methods in `ERP.Api.Tests`)** and executed **`227` runtime SQL test cases (`206` in `ERP.Application.Tests` + `21` in `ERP.Api.Tests`) — `227 Passed, 0 Failed, 0 Skipped`**.

---

### `F-010` — Three Priority Notion Child Page Links (`Spec 28`, `Spec 37`, `Spec 39`) Return HTTP `404 object_not_found` via Notion API
- **Severity / Priority:** `S3` / `P3`
- **Finding Type:** `DOCUMENTATION_ACCESS_DRIFT`
- **Related Requirement & Workflow IDs:** `TR-047`, `TR-048`, `TR-051`, `TR-060`
- **Related Subagent Cross-References:** `NOTION_SOURCE_INVENTORY.md §3.1`, `BUSINESS_REQUIREMENT_TRACEABILITY.md §1.2 & §3.4`
- **Verification Label & Confidence:** `FACT` | `HIGH`
- **Source Evidence:**
  - Fetching the following 3 child page IDs linked from the Notion Root Page (`3de558fb617480b98a4ae4b7694b3c63`) via `mcp_notion_notion-fetch` returns `HTTP 404 object_not_found`:
    1. `Spec 28` (`3de558fb61748173bc03ec1117760072`) — *04.1 Inventory Balance & Availability Model*
    2. `Spec 37` (`3de558fb6174819c87cfdc38ee6744ff`) — *05.2 Packing & Handling Unit Specification*
    3. `Spec 39` (`3de558fb61748155a9d0df30b7be6a73`) — *05.4 Internal Movement, Status Change & Lock Specification*
  - Normative rules for these 3 topics are redundantly documented in accessible pages (`Root Page`, `Spec 01`, `Spec 29`, `Spec 30`, `Spec 36`, `Spec 38`, `Spec 88`, `Spec 99`, `Spec 100`, `ARCH-P4-03`) and verified in code (`InventoryStock.cs`, `PackingService.cs`, `InventoryMovementService.cs`, `InventoryStatusService.cs`, `InventoryLockService.cs`, `StockTransferService.cs`).
- **Operational Impact:**
  - Automated Notion API audits cannot read the standalone body text of `Spec 28`, `Spec 37`, and `Spec 39` until they are re-shared with the Notion integration or their dead links on the Root Page are updated.
- **Remediation Recommendation:**
  - Re-share pages `28`, `37`, and `39` with the Notion MCP integration or update their page IDs on the Notion Root Page (`SD-06`).

---

### `F-011` — Break-Bulk Replenishment Badge on `/inventory-movements` (`InventoryMovements.tsx`) Displays `+12 THÙNG` Instead of Base UOM `+12 ĐV` When Breaking `1 Thùng = 12 ĐV`
- **Severity / Priority:** `S3` / `P3`
- **Finding Type:** `FRONTEND_UOM_LABEL_DISPLAY_DEFECT`
- **Related Requirement & Workflow IDs:** `Epic 60.3`, `BM-14`, `INV-005`, `FE-001`
- **Related Subagent Cross-References:** `COMPETITIVE_GAP_AND_ROADMAP.md §2 Epic 60.3`, `wave60_e2e_4_epic60_3_breakbulk_1440x900.png`, `wave60_e2e_5_mobile_pda_390x844.png`
- **Verification Label & Confidence:** `FACT` | `HIGH`
- **Source & Visual Evidence (`File:Line`):**
  1. In Wave 60 (`37eb5fe`), [InventoryMovements.tsx](file:///d:/ERP_KHO/frontend/src/pages/InventoryMovements.tsx) rendered the `TRẠNG THÁI SỔ CÁI NGUYÊN TỬ` badge on the Break-Bulk replenishment table (`Epic 60.3`) using the pack UOM (`THÙNG`) for both the Reserve deduction (`-1 THÙNG`) and the Forward-Pick addition (`+12 THÙNG`) instead of the base UOM (`+12 ĐV`), even though `conversionRate = 12` (`1 Thùng = 12 ĐV`) and `movedBaseQty = 12`.
  2. Additionally, when the fallback/initial row rendered before/after the E2E call, the source Reserve bin label needed explicit normalization (`BIN-A01 -> BIN-A01-01`) across Desktop (`1440x900`) and Mobile PDA (`390x844`).
- **Operational Impact:**
  - Purely visual UOM label discrepancy on the UI badge (`+12 THÙNG` vs `+12 ĐV`); backend `InventoryControlOperationsService.ExecuteBreakBulkReplenishmentAsync` correctly deducted and added `12.0000` base units (`Δ = 0.0000`).
- **Wave 60.1 Closure Evidence (`FACT` — Status: `CLOSED`):**
  - **Code & Test Remediation:**
    1. Updated [InventoryMovements.tsx](file:///d:/ERP_KHO/frontend/src/pages/InventoryMovements.tsx#L82-L110) and [InventoryMovements.tsx](file:///d:/ERP_KHO/frontend/src/pages/InventoryMovements.tsx#L159-L208) so `caseUom` (`THÙNG`) is used for the Reserve deduction (`-1 THÙNG`) and `baseUom` (`ĐV`) is used for the Forward-Pick addition (`+12 ĐV`), rendering **`Khớp 2 bút toán (-1 THÙNG / +12 ĐV • Δ = 0.0000)`** consistently across both the confirmation banner and the `TRẠNG THÁI SỔ CÁI NGUYÊN TỬ` badge, with source location `BIN-A01 -> BIN-A01-01` unified across Desktop (`1440x900`) and Mobile PDA (`390x844`).
    2. Added explicit unit-level assertion in [InventoryMovements.test.tsx](file:///d:/ERP_KHO/frontend/src/pages/InventoryMovements.test.tsx) (`4/4 PASS` in Vitest) and re-verified on live Chromium screenshots `wave60_e2e_4_epic60_3_breakbulk_1440x900.png` and `wave60_e2e_5_mobile_pda_390x844.png`.

---

### `F-012` — Audit Package Metadata Drift in `SOURCE_IMPLEMENTATION_MAP.md`, `TEST_COVERAGE_GAPS.md`, `AUDIT_STATE.md` & `EXECUTIVE_SUMMARY.md` (`d9b2453` Header, Missing `37eb5fe` SHA, Wave 57 SQL Row Counts & Roadmap Name Alignment)
- **Severity / Priority:** `S3` / `P3`
- **Finding Type:** `AUDIT_REPORT_METADATA_SYNCHRONIZATION`
- **Related Requirement & Workflow IDs:** `REP-001`, `REP-002`
- **Related Subagent Cross-References:** `SOURCE_IMPLEMENTATION_MAP.md §6.2–6.4`, `TEST_COVERAGE_GAPS.md`, `AUDIT_STATE.md §2.6–2.7`, `EXECUTIVE_SUMMARY.md`
- **Verification Label & Confidence:** `FACT` | `HIGH`
- **Source Evidence:**
  1. Top headers of `SOURCE_IMPLEMENTATION_MAP.md` and `TEST_COVERAGE_GAPS.md` retained `Inspected Git Commit (HEAD): d9b24530ba5826ae0d5f4f107e8f91be3f6f6009` in line 5 instead of reflecting the Wave 60 (`37eb5fe`) / Wave 60.1 commit lineage.
  2. Section 2.6 of `AUDIT_STATE.md` omitted the explicit Wave 60 commit SHA (`37eb5fe`), and Sections 6.2–6.4 of `SOURCE_IMPLEMENTATION_MAP.md` still displayed the Wave 57 SQL row count (`13` rows in `dbo.InventoryStocks` and `26` rows in `dbo.InventoryTransactions`) rather than the post-Wave-60.1 `ERP_KHO` snapshot (`20` rows in `dbo.InventoryStocks` `Id=3663..14681`, `62` rows in `dbo.InventoryTransactions`, `7` rows in `dbo.KitWorkOrders` all `Cancelled`, `TotalOnHand = 232.0000`, `TotalReserved = 9.0000`, `TotalAvailable = 223.0000`, `Variance = 0.0000`).
  3. Abbreviated summary references to `BM-04`, `BM-36`, and the 90-Day Roadmap (`Epic 90.1..90.3`) in `EXECUTIVE_SUMMARY.md` needed 100% verbatim alignment with `COMPETITIVE_WMS_BENCHMARK.md` and `COMPETITIVE_GAP_AND_ROADMAP.md`.
- **Wave 60.1 Closure Evidence (`FACT` — Status: `CLOSED`):**
  - **Documentation & Evidence Package Remediation:**
    1. Created [WAVE60_CODE_AND_SQL_EVIDENCE.md](file:///d:/ERP_KHO/audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630/WAVE60_CODE_AND_SQL_EVIDENCE.md) containing verbatim C# implementations of `ProductOperationsService.CompleteKitWorkOrderAsync` & `CancelKitWorkOrderAsync`, `PutawayService.EvaluateAndExecuteCrossDockAsync`, `InventoryControlOperationsService.ExecuteBreakBulkReplenishmentAsync`, `SqlServerWave60RoadmapEpicsTests.cs`, and live SQL 3-Way Check A/B/C console outputs.
    2. Synchronized all 12 `.md` audit files (`AUDIT_STATE.md`, `SOURCE_IMPLEMENTATION_MAP.md`, `TEST_COVERAGE_GAPS.md`, `EXECUTIVE_SUMMARY.md`, `FINDINGS_REGISTER.md`, `BLOCKERS_AND_UNVERIFIED.md`, `COMPETITIVE_WMS_BENCHMARK.md`, `COMPETITIVE_GAP_AND_ROADMAP.md`, `BUSINESS_REQUIREMENT_TRACEABILITY.md`, `BUSINESS_WORKFLOW_AUDIT.md`, `NOTION_SOURCE_INVENTORY.md`, `SPEC_CONFLICTS_AND_OWNER_DECISIONS.md`) with the Wave 60 (`37eb5fe`) + Wave 60.1 commit lineage, Post-Wave-60.1 SQL row counts, and canonical benchmark/roadmap naming.

---

### `F-013` — `Math.Max(0m, ...)` Clamping During Cross-Dock Reversal in `PutawayService.cs`, Optional Unreversed Lookup in Break-Bulk Reversal (`InventoryControlOperationsService.cs`), and Unstandardized `TransactionType.Reversal` Ledger Filter Rule
- **Severity / Priority:** `S2` / `P2`
- **Finding Type:** `DATA_INTEGRITY_AND_REVERSAL_INVARIANT`
- **Related Requirement & Workflow IDs:** `INV-001`, `INV-003`, `INV-006`, `Epic 60.2`, `Epic 60.3`
- **Related Subagent Cross-References:** `WAVE61_1_CODE_AND_SQL_EVIDENCE.md §2–5 & §10`, `SOURCE_IMPLEMENTATION_MAP.md §6.2`
- **Verification Label & Confidence:** `FACT` | `HIGH`
- **Source Evidence Prior to Wave 61 (`File:Line`):**
  1. **`PutawayService.cs` (`EvaluateAndExecuteCrossDockAsync`, `request.Reverse == true`):** Used `shippingStock.Quantity = Math.Max(0m, shippingStock.Quantity - revQty)`, `shippingStock.ReservedQuantity = Math.Max(0m, shippingStock.ReservedQuantity - revQty)`, and `shippingStock.AllocatedQuantity = Math.Max(0m, shippingStock.AllocatedQuantity - revQty)` which could clamp negative quantities to `0` while still appending `-revQty` ledger rows if shipping-stage stock had already been partially consumed.
  2. **`InventoryControlOperationsService.cs` (`ExecuteBreakBulkReplenishmentAsync`, `request?.Reverse == true`):** Only guarded `if (origOutTx != null)` when checking for prior reversal, instead of requiring both unreversed original transactions (`origOutTx` and `origInTx`) to exist before reversing.
  3. **`TransactionType.Reversal` Algebraic Rule:** Needed a single shared helper so `TransactionType.Reversal` rows with `CorrectiveTransactionId != null` (marker records created alongside a separate corrective transaction by `InventoryReversalService`) are excluded (`0m`), while direct reversal rows (`CorrectiveTransactionId == null`) contribute their signed `Quantity`.
- **Wave 61 Closure Evidence (`FACT` — Status: `CLOSED`):**
  - **Code & Test Remediation (`TASK-W61-01`):**
    1. Updated [PutawayService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/PutawayService.cs#L1090-L1208): removed all `Math.Max(0m, ...)` clamping in `EvaluateAndExecuteCrossDockAsync` (`Reverse == true`), added strict duplicate-reversal check throwing `ConflictException("CROSS_DOCK_ALREADY_REVERSED")`, and added strict pre-deduction check throwing `ConflictException("CROSS_DOCK_REVERSAL_INSUFFICIENT_STOCK")` when `shippingStock == null || shippingStock.Quantity < revQty || shippingStock.ReservedQuantity < revQty || shippingStock.AllocatedQuantity < revQty` before any balance or ledger mutation.
    2. Updated [InventoryControlOperationsService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/InventoryControlOperationsService.cs#L825-L985): enforced mandatory lookup of the latest unreversed `(origOutTx, origInTx)` pair (`BREAK_BULK_OUT` / `BREAK_BULK_IN` where no `TransactionType.Reversal` row has `ReversalOfTransactionId == orig.Id`), throwing `ConflictException("BREAK_BULK_REVERSAL_ORIGINAL_NOT_FOUND_OR_ALREADY_REVERSED")` if missing or already reversed, and linking both reversal rows via `ReversalOfTransactionId = origOutTx.Id` and `ReversalOfTransactionId = origInTx.Id`.
    3. Standardized `InventoryLedgerCalculationRules.IsIncludedInAlgebraicLedgerSum` and `ComputeAlgebraicDelta` in [InventoryTransactionTypeMapping.cs](file:///d:/ERP_KHO/ERP.Domain/Enums/InventoryTransactionTypeMapping.cs#L49-L77) and wired it into [InventoryReconciliationQueryService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Queries/InventoryReconciliationQueryService.cs#L289-L310).
    4. Verified by [SqlServerWave61RoadmapEpicsTests.cs](file:///d:/ERP_KHO/ERP.Application.Tests/SqlServerWave61RoadmapEpicsTests.cs) (`Wave61_F013_CrossDockAndBreakBulkReversal_InsufficientStock_And_UnreversedLookup_Guards` — `PASS`).

---

### `F-014` — Missing Duplicate-Reversal `ConflictException` Assertions in `SqlServerWave60RoadmapEpicsTests.cs` (`Epic60_2` & `Epic60_3`) and Prose Drift in `SOURCE_IMPLEMENTATION_MAP.md §6.2` 9 Positive `InventoryStocks` Rows
- **Severity / Priority:** `S3` / `P3`
- **Finding Type:** `TEST_ASSERTION_AND_DOC_ALIGNMENT`
- **Related Requirement & Workflow IDs:** `TST-001`, `TST-002`, `REP-001`, `REP-002`
- **Related Subagent Cross-References:** `WAVE61_1_CODE_AND_SQL_EVIDENCE.md §9 & §11.5`, `SOURCE_IMPLEMENTATION_MAP.md §6.2`
- **Verification Label & Confidence:** `FACT` | `HIGH`
- **Source Evidence Prior to Wave 61:**
  1. In `SqlServerWave60RoadmapEpicsTests.cs`, `Epic60_2` and `Epic60_3` tested forward execution, idempotency, concurrency, and first reversal (`Reverse = true`), but did not assert `Assert.ThrowsAsync<ConflictException>` on a second reversal call with a fresh `Idempotency-Key`.
  2. In `SOURCE_IMPLEMENTATION_MAP.md §6.2`, the bulleted prose list of 9 positive `dbo.InventoryStocks` rows had stale per-row quantities (`Id=3663: 84.0000`, etc.) that differed from the actual SQL table in `WAVE60_CODE_AND_SQL_EVIDENCE.md §6.3` (`Id=3663: 30.0000`, `Id=3664: 60.0000`, `Id=3665: 25.0000`, `Id=3666: 12.0000`, `Id=4663: 10.0000`, `Id=11674: 21.0000`, `Id=11675: 8.0000`, `Id=11676: 6.0000`, `Id=11677: 60.0000`; `TotalOnHand = 232.0000`).
- **Wave 61 Closure Evidence (`FACT` — Status: `CLOSED`):**
  - **Code, Test & Documentation Remediation (`TASK-W61-02`):**
    1. Added `Assert.ThrowsAsync<ConflictException>` duplicate-reversal assertions to both `Epic60_2_InboundToOutboundCrossDocking_HappyPath_Idempotency_Concurrency_And_Reversal` and `Epic60_3_AutomatedBreakBulkReplenishment_HappyPath_Idempotency_Concurrency_And_Reversal` in [SqlServerWave60RoadmapEpicsTests.cs](file:///d:/ERP_KHO/ERP.Application.Tests/SqlServerWave60RoadmapEpicsTests.cs) (`3/3 PASS`).
    2. Updated `SOURCE_IMPLEMENTATION_MAP.md §6.2` so all 9 positive-quantity rows of `dbo.InventoryStocks` (`Id=3663, 3664, 3665, 3666, 4663, 11674, 11675, 11676, 11677`) match 100% column-for-column with live `ERP_KHO` SQL output (`TotalOnHand = 232.0000`, `TotalReserved = 9.0000`, `TotalAvailable = 223.0000`).

---

### `F-015` — Break-Bulk Reversal 7th Dimension `HandlingUnitId` & `HandlingUnitContents` Restoration When `request.SourceHandlingUnitId == null`, Epic 90.1 Pre-Check Transaction/Lock & Loose Concurrency Assertion, and Fake `SKU-DEFAULT` Fallback in Epic 90.3
- **Severity / Priority:** `S2` / `P1`
- **Finding Type:** `INVARIANT_AND_CONCURRENCY`
- **Related Requirement & Workflow IDs:** `INV-001`, `INV-003`, `CON-002`, `CON-003`, `FAKE-001`, `TST-002`, `Epic 60.3`, `Epic 90.1`, `Epic 90.3`
- **Related Subagent Cross-References:** `WAVE61_1_CODE_AND_SQL_EVIDENCE.md §2, §3 & §5–7`
- **Verification Label & Confidence:** `FACT` | `HIGH`
- **Source Evidence Prior to Wave 61.1 (`File:Line`):**
  1. **Break-Bulk Reversal (`InventoryControlOperationsService.ExecuteBreakBulkReplenishmentAsync`):** When `request.Reverse == true` and `request.SourceHandlingUnitId == null`, `reserveStock` was queried once before the `matchedOrigPairs` loop using `s.HandlingUnitId == request.SourceHandlingUnitId` (`null`) instead of restoring per-leg `origOut.HandlingUnitId` inside `foreach (var (origOut, origIn, legQty) in matchedOrigPairs)` and restoring `dbo.HandlingUnitContents.Quantity`.
  2. **Epic 90.1 Concurrency Lock & Test Assertion (`EvaluateAbcSlottingAndTaskInterleavingAsync` & `SqlServerWave61RoadmapEpicsTests.cs`):** Checked `IntegrationOutboxMessages` before opening a SQL transaction and acquiring a row lock on `dbo.Warehouses`, and `SqlServerWave61RoadmapEpicsTests.cs` asserted `concResults.Count(c => c == 200).Should().BeGreaterThanOrEqualTo(1)` instead of strict `1x200 + 1x409`.
  3. **Epic 90.3 Fallback (`ExportReceiptOperationsService.SyncAccountingAndEInvoiceOutboxAsync`):** Contained an `else` fallback branch synthesizing `"SKU-DEFAULT"` (`500000.0000m`, `650000.0000m`) if `exportReceipt?.ExportReceiptDetails` was null or empty.
- **Wave 61.1 Closure Evidence (`FACT` — Status: `CLOSED`):**
  - **Surgical Code & Test Remediation (`TASK-W61.1-01`):**
    1. Updated [InventoryControlOperationsService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/InventoryControlOperationsService.cs#L798-L1466) (`ExecuteBreakBulkReplenishmentAsync`): moved `reserveStock` restoration inside `foreach (var (origOut, origIn, legQty) in matchedOrigPairs)` using `var targetHuId = request.SourceHandlingUnitId ?? origOut.HandlingUnitId;`, restored `huContent.Quantity` in `dbo.HandlingUnitContents` when `targetHuId.HasValue`, called `RefreshGrainHash()` on both `pickStock` and `reserveStock`, and set `revInTx.HandlingUnitId = targetHuId`. Verified in [SqlServerWave60RoadmapEpicsTests.cs](file:///d:/ERP_KHO/ERP.Application.Tests/SqlServerWave60RoadmapEpicsTests.cs) (`Epic60_3` with `HandlingUnitId = 603` and `Reverse: true` with `SourceHandlingUnitId == null` — `PASS`).
    2. Updated [InventoryControlOperationsService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/InventoryControlOperationsService.cs#L1468-L2193) (`EvaluateAbcSlottingAndTaskInterleavingAsync`) to begin a `ReadCommitted` SQL transaction and acquire `UPDLOCK, ROWLOCK, HOLDLOCK` on `dbo.Warehouses` plus sorted 9D `AcquireInventoryAllocationLocksAsync` before querying `IntegrationOutboxMessages`, and updated [SqlServerWave61RoadmapEpicsTests.cs](file:///d:/ERP_KHO/ERP.Application.Tests/SqlServerWave61RoadmapEpicsTests.cs) to strictly assert `concResults.Count(c => c == 200).Should().Be(1)` and `concResults.Count(c => c == 409).Should().Be(1)` (`PASS`).
    3. Updated [ExportReceiptOperationsService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/ExportReceiptOperationsService.cs#L1090-L1465) (`SyncAccountingAndEInvoiceOutboxAsync`): removed the `"SKU-DEFAULT"` fallback branch and enforced `KeyNotFoundException("EXPORT_RECEIPT_NOT_FOUND...")` (`404`) when the receipt does not exist and `ConflictException("EXPORT_RECEIPT_HAS_NO_LINES_FOR_ACCOUNTING_SYNC", ...)` (`409`) when the receipt has `0` detail lines, verified by [SqlServerWave61RoadmapEpicsTests.cs](file:///d:/ERP_KHO/ERP.Application.Tests/SqlServerWave61RoadmapEpicsTests.cs) (`Epic90_3` — `PASS`).

---

### `F-016` — `EvaluateAbcSlottingAndTaskInterleavingAsync` (`Epic 90.1`) Only Staged Outbox/Audit Without Mutating 9D `InventoryStocks`, `InventoryTransactions` (`AbcSlottingReSlotting`), or `InventoryLocationMovements`
- **Severity / Priority:** `S2` / `P1`
- **Finding Type:** `SPEC_VS_CODE_MISMATCH`
- **Related Requirement & Workflow IDs:** `INV-001`, `INV-002`, `INV-004`, `CON-003`, `BM-04`, `Epic 90.1`
- **Related Subagent Cross-References:** `WAVE61_1_CODE_AND_SQL_EVIDENCE.md §2 & §6`, `COMPETITIVE_GAP_AND_ROADMAP.md §3.3 Epic 90.1`
- **Verification Label & Confidence:** `FACT` | `HIGH`
- **Source Evidence Prior to Wave 61.1 (`File:Line`):**
  - In commit `2eafbed`, `EvaluateAbcSlottingAndTaskInterleavingAsync` (`ExecuteReSlotting == true`) acquired 9D locks and staged `IntegrationOutboxMessages` / `AuditLogs`, but did not deduct/increment `dbo.InventoryStocks` between the reserve bin and `goldenPickBin`, did not append paired `TransactionType.Move` rows (`ReferenceType = "AbcSlottingReSlotting"`) to `dbo.InventoryTransactions`, and did not insert `dbo.InventoryLocationMovements`.
- **Wave 61.1 Closure Evidence (`FACT` — Status: `CLOSED`):**
  - **Surgical Code & Test Remediation (`TASK-W61.1-02`):**
    1. Updated [InventoryControlOperationsService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/InventoryControlOperationsService.cs#L1468-L2193) (`EvaluateAbcSlottingAndTaskInterleavingAsync`):
       - **Forward (`ExecuteReSlotting == true && !Reverse`):** Selects top `Class A` SKU stock in non-pickable/non-golden bins (`sourceStock.LocationId != goldenPickBin.Id && (sourceStock.Quantity - sourceStock.ReservedQuantity) > 0m`), checks `goldenPickBin.MaxWeightKg` and `MaxVolumeM3`, deducts `reSlotMoveQty` from `sourceStock` (`RefreshGrainHash()`), adds `reSlotMoveQty` to `destStock` at `goldenPickBin.Id` preserving all other 8 dimensions (`RefreshGrainHash()`), inserts paired `TransactionType.Move` rows (`ReferenceType = "AbcSlottingReSlotting"`, `-reSlotMoveQty` with `Notes = "SLOTTING_MOVE_OUT..."` and `+reSlotMoveQty` with `Notes = "SLOTTING_MOVE_IN..."`) in `dbo.InventoryTransactions`, and inserts 1 `InventoryLocationMovement` (`Reason = "ABC_SLOTTING_RESLOT"`).
       - **Reversal (`Reverse == true`):** Finds unreversed `AbcSlottingReSlotting` transaction pairs (`SLOTTING_MOVE_OUT` / `SLOTTING_MOVE_IN`), validates `destStock.Quantity - destStock.ReservedQuantity >= legQty` (throwing `ConflictException("SLOTTING_REVERSAL_INSUFFICIENT_DEST_AVAILABLE_STOCK")` on shortage), restores `sourceStock` and deducts `destStock` (`RefreshGrainHash()`), appends compensating `TransactionType.Reversal` rows with `ReversalOfTransactionId = origOut.Id` / `origIn.Id`, and inserts a reversal `InventoryLocationMovement` (`Reason = "ABC_SLOTTING_REVERSAL"`).
    2. Verified in [SqlServerWave61RoadmapEpicsTests.cs](file:///d:/ERP_KHO/ERP.Application.Tests/SqlServerWave61RoadmapEpicsTests.cs) (`Epic90_1`: forward `reserveProdA.Quantity == 40m`, `goldenProdA.Quantity == 10m`, `InventoryLocationMovements == 1`; after reversal `reserveProdA.Quantity == 50m`, `goldenProdA.Quantity == 0m`, `3-Way Check A Variance == 0.0000` — `PASS`).

---

### `F-017` — Line-Count Cap (`+ 318/196/188 more lines`) Truncated Method Bodies in `WAVE61_1_CODE_AND_SQL_EVIDENCE.md`
- **Severity / Priority:** `S3` / `P2`
- **Finding Type:** `EVIDENCE_TRUNCATION`
- **Related Requirement & Workflow IDs:** `REP-001`, `REP-002`
- **Related Subagent Cross-References:** `WAVE61_1_CODE_AND_SQL_EVIDENCE.md §1–8`
- **Verification Label & Confidence:** `FACT` | `HIGH`
- **Source Evidence Prior to Wave 61.1:**
  - The evidence generator used in Wave 61 capped code blocks at `220` lines, emitting `... (+ 318 more lines in ERP.Infrastructure/Services/InventoryControlOperationsService.cs)` in `WAVE61_1_CODE_AND_SQL_EVIDENCE.md`.
- **Wave 61.1 Closure Evidence (`FACT` — Status: `CLOSED`):**
  - Generated [WAVE61_1_CODE_AND_SQL_EVIDENCE.md](file:///d:/ERP_KHO/audit_reports/2026-10-29_WMS-WAVE61.1-AUDIT-0026/WAVE61_1_CODE_AND_SQL_EVIDENCE.md) (`5,222` lines) containing 100% untruncated, verbatim C# source code from method signature to closing `}` across all 8 required sections (`0` occurrences of `more lines in` or `truncated`).

---

### `F-018` — Conceptual Method & Route Names Cited in `EXECUTIVE_SUMMARY.md`, `COMPETITIVE_GAP_AND_ROADMAP.md`, and `TEST_COVERAGE_GAPS.md` Differed from Actual Code Names
- **Severity / Priority:** `S3` / `P3`
- **Finding Type:** `REPORT_ACCURACY`
- **Related Requirement & Workflow IDs:** `REP-001`, `REP-002`
- **Related Subagent Cross-References:** `EXECUTIVE_SUMMARY.md`, `COMPETITIVE_GAP_AND_ROADMAP.md §3.3`, `TEST_COVERAGE_GAPS.md §4`
- **Verification Label & Confidence:** `FACT` | `HIGH`
- **Source Evidence Prior to Wave 61.1:**
  - `EXECUTIVE_SUMMARY.md` (`L31`, `L84`), `TEST_COVERAGE_GAPS.md` (`L111`), and `COMPETITIVE_GAP_AND_ROADMAP.md` (`L138-L140`) cited conceptual method/route names (`GetDynamicSlottingAndInterleavingPlanAsync`, `ExecuteDynamicSlottingPlanAsync`, `DispatchAccountingVoucherAsync`, `DispatchEinvoiceOutboxAsync`, `/api/v1/inventory-control/3pl-portal/summary`) instead of the exact C# method and route names implemented in `InventoryControlOperationsService.cs`, `ExportReceiptOperationsService.cs`, `InventoryControlController.cs`, `PutawayTasksController.cs`, `BusinessPartnersController.cs`, and `ExportReceiptsController.cs`.
- **Wave 61.1 Closure Evidence (`FACT` — Status: `CLOSED`):**
  - Synchronized all audit reports in `d:\ERP_KHO\audit_reports\2026-10-29_WMS-WAVE61.1-AUDIT-0026\` with the exact C# method and route names:
    - **Epic 90.1:** `EvaluateAbcSlottingAndTaskInterleavingAsync` (`GET/POST /api/v1/inventory-control/slotting-interleaving/evaluate`, `POST /api/v1/putaway-tasks/abc-slotting-interleaving`)
    - **Epic 90.2:** `GetThreePlDepositorPortalAsync` (`GET/POST /api/v1/inventory-control/3pl-depositor-portal`, `GET/POST /api/v1/business-partners/3pl-depositor-portal`)
    - **Epic 90.3:** `SyncAccountingAndEInvoiceOutboxAsync` (`GET /api/v1/export-receipts/accounting-einvoice/preview`, `POST /api/v1/export-receipts/accounting-einvoice/sync`)
  - Verified via `Select-String` across `2026-10-29_WMS-WAVE61.1-AUDIT-0026\*.md` that `0` conceptual method/route mismatches remain outside the historical `F-018` finding description itself.

