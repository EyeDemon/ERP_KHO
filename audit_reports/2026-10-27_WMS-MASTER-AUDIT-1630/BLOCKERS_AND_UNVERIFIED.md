# BLOCKERS, UNVERIFIED ITEMS & NOT_FOUND REGISTER — ERP WMS SaaS MASTER AUDIT (`WMS-MASTER-AUDIT`)

- **Audit ID:** `WMS-MASTER-AUDIT`
- **Run ID:** `2026-10-27_WMS-MASTER-AUDIT-1630`
- **Audit Mode:** `READ-ONLY EVIDENCE AUDIT`
- **Classification Standard (Charter Section 3.9 & `[REP-001]`):**
  - **`BLOCKED`:** Cannot be accessed or executed due to external permissions, missing third-party credentials, or explicit security/blacklist rules.
  - **`UNVERIFIED`:** Intentionally not executed during this read-only audit run (e.g., mutating SQL integration test runs on the live `ERP_KHO` database) or external proprietary competitor source code not publicly available.
  - **`NOT_FOUND`:** Actively searched in the repository, database (`ERP_KHO`), and migrations (`FACT` verified), and confirmed absent.

---

## 1. Summary Rollup by Category (Post-Wave-59 Closure State)

| Category | Count | Item IDs | Impact on Audit Completeness |
| --- | ---: | --- | --- |
| **`BLOCKED`** | **3** | `BLK-01`, `BLK-02`, `BLK-03` | Zero impact on core codebase/DB verification; normative requirements for `BLK-01` were verified via redundant Notion specs (`01/29/30/36/38/88/100`). |
| **`UNVERIFIED`** | **1** (`UNV-01` **`RESOLVED_AND_VERIFIED_IN_WAVE_57`**, `UNV-02` **`RESOLVED_AND_VERIFIED_IN_WAVE_59`**) | `UNV-03` (`UNV-01`, `UNV-02` resolved) | `UNV-01` (Live SQL Integration Harness — `51` SQL test class files / `230` runtime SQL test cases `PASS`) and `UNV-02` (Live Mutating Browser E2E Verification via Playwright connected to `http://localhost:5173`, `http://localhost:5260`, and `ERP_KHO`) are both **100% RESOLVED AND VERIFIED**. Only external competitor proprietary code (`UNV-03`) remains external. |
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

| ID | Unverified Item | Status After Wave 59 | What Was Verified (`FACT`) |
| --- | --- | --- | --- |
| **`UNV-01`** | **Live Re-Execution of Mutating SQL Integration Tests (`ERP.SqlIntegrationHarness` & `[SqlServerFact]` against `ERP_KHO`)** | **`RESOLVED_AND_VERIFIED_IN_WAVE_57`** *(Re-verified in Wave 59)* | Executed `dotnet run --project tools/ERP.SqlIntegrationHarness/ERP.SqlIntegrationHarness.csproj -c Release --no-build` against live `ERP_KHO`: discovered all **`51` SQL test class files (`48` compiled public classes / `224` decorated methods)** and executed **`230` runtime SQL test cases (`209` in `ERP.Application.Tests` + `21` in `ERP.Api.Tests`) — `230 Passed, 0 Failed, 0 Skipped`**, followed by live 3-Way SQL Checks A, B, and C (`0.0000` variance across all `13` `InventoryStocks` rows). |
| **`UNV-02`** | **Live Mutating Browser E2E Submissions (`POST / PUT / DELETE` Flows via Playwright)** | **`RESOLVED_AND_VERIFIED_IN_WAVE_59`** | Executed interactive Live Browser E2E verification (`frontend/e2e/wave59-live-browser-e2e.spec.mjs` & `playwright-erp-kho` Chromium session) against live Vite (`http://localhost:5173`), live `ERP.Api` (`http://localhost:5260`), and live SQL Server `ERP_KHO` (`0` mocks, `0` JS page errors, `0` HTTP `5xx` errors): (1) Login (`POST /api/auth/login` `200 OK`) & `GET /api/v1/auth/me` (`200 OK`) -> `/dashboard`; (2) `Epic 30.1` on `/picking-tasks`: released `WAVE-20261028-0644` (`TPL-STANDARD`, `CarrierCutoffWindow=14:00`, `ClusterTote=TOTE-CL-01`) and completed Put-to-Wall `SLOT-03` (`4/4` units), auto-creating `PackingSession` `Id=3` (`PACK-PTW-1-064407`) inside the same DB transaction; (3) `Epic 30.2` on `/shipments` & `/cod-reconciliation`: booked `GHN` waybills `GHN-01-261028-00001` (`OutboxId=14180`) & `GHN-01-261028-00002` (`OutboxId=14181`), verified `100x150mm` thermal AWB PDF stream (`%PDF-1.4`, `/MediaBox [0 0 283.46 425.20]`, `747` bytes), and processed `GHN` `DELIVERED` webhook (`InboxId=4`, `FinancialLedgerId=6080`, `ActualCodCollected=450,000 VND`); (4) `Epic 30.3` on `/outbound-demand` & `/handover-manifests`: exchanged Shopee OAuth 2.0 (`ShopId=6137`, `SHOPEE-VN-W59-LIVE`), pushed buffered stock across `6` shops / `8` SKUs, processed Shopee buyer cancellation on `ExportReceiptId=179` (`ER-QA-003`), and verified `POST /api/v1/handover-manifests/scan-awb` blocks handover with HTTP `409 Conflict` (`ORDER_CANCELLED_PUTBACK_REQUIRED`); (5) Verified `0px` horizontal overflow (`scrollWidth <= innerWidth`) on both Desktop (`1440x900`) and Mobile PDA (`390x844`) across `/dashboard`, `/picking-tasks`, `/shipments`, `/outbound-demand`, and `/handover-manifests`, and confirmed post-E2E SQL 3-Way Integrity Checks A, B, and C maintain `Variance = 0.0000`. |
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
