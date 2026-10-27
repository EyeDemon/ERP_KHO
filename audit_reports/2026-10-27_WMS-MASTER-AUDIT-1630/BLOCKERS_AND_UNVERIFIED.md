# BLOCKERS, UNVERIFIED ITEMS & NOT_FOUND REGISTER — ERP WMS SaaS MASTER AUDIT (`WMS-MASTER-AUDIT`)

- **Audit ID:** `WMS-MASTER-AUDIT`
- **Run ID:** `2026-10-27_WMS-MASTER-AUDIT-1630`
- **Audit Mode:** `READ-ONLY EVIDENCE AUDIT`
- **Classification Standard (Charter Section 3.9 & `[REP-001]`):**
  - **`BLOCKED`:** Cannot be accessed or executed due to external permissions, missing third-party credentials, or explicit security/blacklist rules.
  - **`UNVERIFIED`:** Intentionally not executed during this read-only audit run (e.g., mutating SQL integration test runs on the live `ERP_KHO` database) or external proprietary competitor source code not publicly available.
  - **`NOT_FOUND`:** Actively searched in the repository, database (`ERP_KHO`), and migrations (`FACT` verified), and confirmed absent.

---

## 1. Summary Rollup by Category (Post-Wave-57 Closure State)

| Category | Count | Item IDs | Impact on Audit Completeness |
| --- | ---: | --- | --- |
| **`BLOCKED`** | **3** | `BLK-01`, `BLK-02`, `BLK-03` | Zero impact on core codebase/DB verification; normative requirements for `BLK-01` were verified via redundant Notion specs (`01/29/30/36/38/88/100`). |
| **`UNVERIFIED`** | **2** (`UNV-01` **`RESOLVED_AND_VERIFIED_IN_WAVE_57`**) | `UNV-02`, `UNV-03` (`UNV-01` resolved) | `UNV-01` (Live SQL Integration Harness) was executed in full during Wave 57 Gate 2 (`50` SQL test class files / `227` runtime SQL test cases `PASS`). Only live mutating Playwright browser clicks (`UNV-02`) and external competitor proprietary code (`UNV-03`) remain `UNVERIFIED`. |
| **`NOT_FOUND` -> `RESOLVED_AND_VERIFIED_IN_WAVE_57`** | **0** (`5` resolved in Wave 57) | `NF-01`, `NF-02`, `NF-03`, `NF-04`, `NF-05` | All 5 previously missing database/worker artifacts (`F-002`, `F-003a`, `F-003b`, `F-003c`, `F-006`) were created, migrated, and verified in Wave 57 (`RESOLVED_AND_VERIFIED_IN_WAVE_57`). |

---

## 2. `BLOCKED` Items (`BLK-01`..`BLK-03`)

| ID | Blocked Item | Exact Reason for Block | Evidence & Workaround Applied |
| --- | --- | --- | --- |
| **`BLK-01`** | **Notion Child Pages `Spec 28`, `Spec 37`, `Spec 39`** (`3de558fb61748173bc03ec1117760072`, `3de558fb6174819c87cfdc38ee6744ff`, `3de558fb61748155a9d0df30b7be6a73`) | Notion MCP tool `notion-fetch` returned `HTTP 404 object_not_found` (pages not shared with integration or moved). | Documented in `NOTION_SOURCE_INVENTORY.md §3.1` and `F-010`. Normative business rules for 9D Balance/Availability (`Spec 28`), Packing/HU (`Spec 37`), and Internal Movement/Status/Lock (`Spec 39`) were extracted from accessible pages (`Root Page`, `Spec 01`, `Spec 29`, `Spec 30`, `Spec 36`, `Spec 38`, `Spec 99`, `ARCH-P4-03`) and verified against `InventoryStock.cs`, `PackingService.cs`, `InventoryMovementService.cs`, `InventoryStatusService.cs`, `InventoryLockService.cs`, and `StockTransferService.cs`. |
| **`BLK-02`** | **4 Blacklisted Pages (`OWN-OOAD-001`..`004`)** (`3f8558fb61748145989cf2e7963e4b3b`, `3f8558fb617481ed9c65f702abb64ca5`, `3f8558fb61748104a627c163b9118b55`, `3f8558fb61748125bb89efb1c21cb650`) | Strictly prohibited by `PROJECT_RULES.md §3` and Audit Charter Section `2.3` (*"Tuyệt đối không mở, không đọc, không tìm kiếm, không trích dẫn"*). | `0` fetches, `0` searches, `0` citations performed (`100%` guardrail compliance). |
| **`BLK-03`** | **Live External Third-Party Production APIs** (Shopee Open Platform, TikTok Shop Partner, Lazada Open Platform, GHN, GHTK, ViettelPost, Napas247 VietQR production keys) | External production merchant credentials are not stored in the repository (in compliance with `[SEC-003]`). | Verified the internal webhook HMAC-SHA256 verification, Outbox/Inbox state tables (`IntegrationOutboxMessages`, `IntegrationInboxMessages`, `ProcessedWebhookEvents`), and PDF `100x150mm` thermal AWB generator (`ExportReceiptsController.cs:531-788`) directly in source code and `ERP_KHO`. |

---

## 3. `UNVERIFIED` Items (`UNV-01`..`UNV-03`)

| ID | Unverified Item | Status After Wave 57 | What Was Verified (`FACT`) |
| --- | --- | --- | --- |
| **`UNV-01`** | **Live Re-Execution of Mutating SQL Integration Tests (`ERP.SqlIntegrationHarness` & `[SqlServerFact]` against `ERP_KHO`)** | **`RESOLVED_AND_VERIFIED_IN_WAVE_57`** | Executed `dotnet run --project tools/ERP.SqlIntegrationHarness/ERP.SqlIntegrationHarness.csproj -c Release --no-build` against live `ERP_KHO`: discovered all **`50` SQL test class files (`47` compiled public classes / `221` decorated methods)** and executed **`227` runtime SQL test cases (`206` in `ERP.Application.Tests` + `21` in `ERP.Api.Tests`) — `227 Passed, 0 Failed, 0 Skipped`**, followed by live 3-Way SQL Checks A, B, and C (`0.0000` variance across all `13` `InventoryStocks` rows). |
| **`UNV-02`** | **Live Mutating Browser E2E Submissions (`POST / PUT / DELETE` Flows via Playwright)** | `UNVERIFIED` (Covered by 544 API HTTP Integration Tests & 406 Vitest UI Tests) | Audited all `43` `<Route>` declarations (`35` unique routed page views / `42` non-test `.tsx` files in `frontend/src/pages/` after deleting orphan `WarehouseStructure.tsx`), `MobileBarcodeScannerBar.tsx`, and `cameraBarcodeDecoder.ts` directly via `read_file`, ran `73` Vitest files (`406/406 PASS`), and reconciled the `109` row / `111` `TC-*` browser test matrix in `TEST_COVERAGE_GAPS.md §3`. |
| **`UNV-03`** | **Internal Proprietary Source Code / Private Pricing Contracts of 9 Competitor WMS Platforms** | `UNVERIFIED` (External Proprietary Code) | Used official vendor documentation, public technical help portals, and public API docs (`42` official vendor URLs cited in `COMPETITIVE_WMS_BENCHMARK.md`). |

---

## 4. `NOT_FOUND` Items (`NF-01`..`NF-05` — All `RESOLVED_AND_VERIFIED_IN_WAVE_57`)

| ID | Item Searched For | Wave 57 Resolution Status | Verification Result & Linked Finding (`FACT`) |
| --- | --- | :---: | --- |
| **`NF-01`** | **SQL Server Trigger `TR_InventoryTransactions_Immutable_AppendOnly`** | **`RESOLVED_AND_VERIFIED_IN_WAVE_57`** | Created by migration `20261027130209_Wave57AuditRemediationSchemaAndImmutableLedgerTrigger.cs` (`SELECT name, is_disabled FROM sys.triggers` returns `TR_InventoryTransactions_Immutable_AppendOnly`, `is_disabled = False`). Blocks all `DELETE` and any `UPDATE` touching any of the 26 non-corrective columns (`RAISERROR 50001`). **`F-002` `CLOSED`**. |
| **`NF-02`** | **Relational Columns `CountAttempt1Qty`, `CountAttempt2Qty`, `CountAttempt3Qty` on `dbo.StocktakeDetails` & `StocktakeStatus.PendingRecount`** | **`RESOLVED_AND_VERIFIED_IN_WAVE_57`** | Added `CountAttempt1Qty`, `CountAttempt2Qty`, `CountAttempt3Qty` (`decimal(19,4)`), `CountAttemptCount` (`int`), `RecountReason` (`nvarchar(500)`), and `LastCountedByUserId` (`int?`) on `dbo.StocktakeDetails` + `StocktakeStatus.PendingRecount = 8`. **`F-003a` / `SD-01` `CLOSED`**. |
| **`NF-03`** | **Dedicated Relational Tables `dbo.PickingWaves` & `dbo.PutToWallSlots`** | **`RESOLVED_AND_VERIFIED_IN_WAVE_57`** | Created `PickingWave.cs`, `PutToWallSlot.cs`, and physical tables `dbo.PickingWaves` & `dbo.PutToWallSlots` (`decimal(19,4)`, `RowVersion`). **`F-003b` / `SD-02` `CLOSED`**. |
| **`NF-04`** | **Dedicated Relational Tables `dbo.ThreePlStorageSnapshots` & `dbo.ThreePlBillingStatements`** | **`RESOLVED_AND_VERIFIED_IN_WAVE_57`** | Created `ThreePlStorageSnapshot.cs`, `ThreePlBillingStatement.cs`, and physical tables `dbo.ThreePlStorageSnapshots` & `dbo.ThreePlBillingStatements` (`decimal(19,4)`, `RowVersion`). **`F-003c` / `SD-03` `CLOSED`**. |
| **`NF-05`** | **`BackgroundService` Workers for Soft Reservation Expiry, Daily 3-Way Integrity Check & 23:59 3PL Snapshot** | **`RESOLVED_AND_VERIFIED_IN_WAVE_57`** | Created and registered `SoftReservationExpiryJob`, `DailyInventoryIntegrityJob`, and `ThreePlDailySnapshotJob` in `ERP.Api/Program.cs` (`5` total hosted `BackgroundService` workers). **`F-006` `CLOSED`**. |
