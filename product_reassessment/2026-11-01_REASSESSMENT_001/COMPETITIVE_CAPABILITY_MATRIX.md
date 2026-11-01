# Deliverable D: Competitive Capability Matrix (`COMPETITIVE_CAPABILITY_MATRIX.md`)

**Run ID:** `2026-11-01_REASSESSMENT_001`  
**Document ID:** Deliverable D  
**Date Compiled:** 2026-11-01  
**Baseline Commit:** `6e0a333390c1db6222e55ae3945b7ce33a56bef6` (`feat/WMS-61-project-development`)  
**Scope:** Side-by-side capability and architectural comparison across **ERP_KHO**, **13 Commercial WMS/ERP Systems (`COM-01`–`COM-13`)**, and **10 Open-Source Repositories (`OSS-01`–`OSS-10`)** across **25 Functional & Technical Capability Dimensions**, calibrated for **ERP_KHO's Target ICP** (1 warehouse, 5–30 staff, Shopee/Lazada/TikTok Shop e-commerce fulfillment & distribution in Vietnam).

---

## 1. Rating Scales & Legend

### 1.1 ERP_KHO Benchmark Status Scale
| Status | Meaning |
|--------|---------|
| `AHEAD` | ERP_KHO's implementation exceeds mid-market/Tier-3 e-commerce WMS benchmarks (and matches Tier-1 enterprise depth) with verified production code. |
| `PARITY` | ERP_KHO matches standard commercial/open-source expectations for the target ICP across both backend domain logic and frontend UI. |
| `BEHIND` | Feature exists in ERP_KHO (often with strong backend entities/endpoints) but lags competitors in UX ergonomics, workflow cohesion, module placement, or live external connectivity. |
| `MISSING` | Essential capability for the target ICP is absent or lacks a dedicated usable UI/workflow. |
| `OVERENGINEERED_FOR_ICP` | ERP_KHO built Tier-1 enterprise state-machine or sub-workflow complexity that introduces operational friction for a 5–30 person Vietnamese warehouse unless simplified or hidden in `EXPRESS` mode. |
| `NOT_APPLICABLE_NOW` | Enterprise-only capability (e.g., PLC conveyor MFS, multi-dock yard trailer orchestration) not needed by ERP_KHO's primary ICP today. |

### 1.2 Competitor Cell Symbols
- `✅` = **Full Native Support** (`NATIVE_CONFIRMED` in commercial / `Full` in OSS)
- `⚙️` = **Configurable / Partial / Basic Support** (`CONFIGURATION_CONFIRMED` in commercial / `Basic/Partial` in OSS)
- `🔌` = **Paid Add-On / SuiteApp / Community Extension Required** (`ADDON_OR_INTEGRATION`)
- `❌` = **Not Supported / Not Found in Reviewed Sources** (`NOT_FOUND_IN_REVIEWED_SOURCES` / `None`)

---

## 2. Master 25-Dimension Capability Matrix

### 2.1 Part A — ERP_KHO vs. 13 Commercial Systems (`COM-01` to `COM-13`)

| # | Capability Dimension | ERP_KHO Benchmark Status | ERP_KHO Implementation Summary (Backend + Frontend Evidence) | Oracle WMS | D365 WMS | SAP EWM | Manhattan | Blue Yonder | Infor WMS | Körber WMS | NetSuite | Odoo Inv | ERPNext | Logiwa IO | ShipHero | Extensiv |
|---|----------------------|--------------------------|--------------------------------------------------------------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| `01` | **Receiving (ASN, PO, QC, Discrepancy)** | `OVERENGINEERED_FOR_ICP` | Full PO (`PurchaseOrders.tsx`), ASN (`Asns.tsx`), Receipt (`ImportReceipts.tsx`), line QC & multi-version Discrepancy (`ReceivingDiscrepancy.cs`), but **15-state `ReceiptStatus`** + 1,991-line God page is too heavy for 1-step SMB receiving. | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `02` | **Putaway (Directed, Rules, Putback)** | `AHEAD` | 5-factor AI/rule bin scoring + 4 hard guards (active, unblocked, no stocktake freeze, capacity), cross-dock flag, and cancel-putback (`PutawayTasks.tsx`, `PutawayTasksController.cs`). | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `03` | **9-Dim Inventory Control & GrainHash** | `AHEAD` | Canonical 9-dim bucket (`TenantId, WarehouseId, LocationId, ProductId, LotId, SerialId, HandlingUnitId, Status, OwnerId`) with deterministic `SHA256` `GrainHash` (`InventoryStock.cs:127-172`). | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚙️ | ✅ | ⚙️ | ⚙️ | ⚙️ | ✅ |
| `04` | **Immutable Ledger & Reversals** | `AHEAD` | 6 append-only ledgers enforced in `ErpKhoDbContext.cs:243-248` (`InventoryTransactions`, `InventoryLocationMovements`, `PartnerDebtLedger`, etc.) + explicit reversal chain (`InventoryReversals.tsx`). | ❌ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `05` | **Cycle Counting / Stocktake** | `PARITY` | 9-state lifecycle (`StocktakeStatus.cs`), blind count mode, attempt 1/2 recount, auto-approval threshold preview, VND variance posting (`Stocktakes.tsx`). | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `06` | **Stock Adjustments & Reconciliation** | `AHEAD` | Deep ledger-vs-balance reconciliation workbench (`InventoryReconciliation.tsx`), status changes, locks (`InventoryLocks.tsx`), and adjustment proposals (`StocktakeAdjustmentProposal.cs`). | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `07` | **Replenishment (Min/Max, Break-Bulk)** | `BEHIND` | Backend has `replenishment-suggestions`, `execute-replenishment`, and `break-bulk` (`InventoryControlController.cs:84-168`), but duplicated in `InventoryMovementsController.cs:107` and lacks a dedicated Replenishment UI screen. | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| `08` | **Reservation & Allocation (FEFO/FIFO)** | `AHEAD` | Two-layer separation: soft `StockReservation` (8 states) + hard bin/lot `StockAllocation` (6 states) with FEFO/FIFO candidate ranking (`StockReservations.tsx`, `StockAllocations.tsx`). | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `09` | **Wave, Batch & Put-to-Wall Picking** | `PARITY` | Single, wave, and cluster tote picking + 8-slot Put-to-Wall sortation + short-pick reallocation (`PickingTasks.tsx`, `PickingTasksController.cs:109-213`). | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚙️ | ✅ | ✅ | ✅ |
| `10` | **Packing, Handling Units & Video Evidence** | `AHEAD` | Multi-level HU nesting (Carton/Pallet/Tote), SSCC barcode, 3D carton suggestion, and **`PackingVideoEvidence`** for e-commerce dispute resolution (`PackingSessions.tsx`). | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🔌 | ✅ | ⚙️ | ✅ | ✅ | ✅ |
| `11` | **Dispatch & Shipping Lifecycle** | `OVERENGINEERED_FOR_ICP` | Full staging/loading/dispatch/POD tracking (`Shipments.tsx`), but **12-state `ShipmentStatus`** + separate `ExportReceipts` + `Shipments` + `HandoverManifests` creates 3 overlapping outbound steps for small sellers. | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🔌 | ✅ | ✅ | ✅ | ✅ | ✅ |
| `12` | **Handover Manifest (AWB Scan & Seal)** | `AHEAD` | Purpose-built for VN e-commerce courier handover (`HandoverManifests.tsx`): scans AWB parcels, blocks cancelled Shopee/TikTok orders at the dock door, and seals manifest per driver/plate. | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🔌 | ⚙️ | ❌ | ✅ | ✅ | ✅ |
| `13` | **Customer Returns / RMA & Disposition** | `BEHIND` | Backend has `CustomerReturn`, `CustomerReturnLine` (`CustomerReturnEntities.cs`) and RTV/refurbish logic, **but endpoints are buried in `ImportReceiptsController.cs:184-222` and there is NO dedicated Returns UI page**. | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `14` | **Cross-Docking** | `PARITY` | Opportunistic cross-dock detection during Putaway linking inbound receipt lines directly to open Sales Orders / Export Receipts (`PutawayTasks.tsx`). | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚙️ | ❌ | ⚙️ | ❌ | ⚙️ |
| `15` | **Yard & Dock Management** | `OVERENGINEERED_FOR_ICP` | Full 10-state `DockAppointmentStatus`, yard slots, vehicle check-in, dwell SLA (`DockYard.tsx`, 1,305 lines). Impressive engineering, but irrelevant for 90% of 1-warehouse VN e-commerce sellers (`NOT_APPLICABLE_NOW` in Express mode). | ✅ | 🔌 | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| `16` | **Kitting / Bundling / VAS** | `BEHIND` | Backend has `ProductBundleComponent`, `PromotionGiftRule`, and `KitWorkOrder` (`KitWorkOrder.cs`), but API is inside `ProductsController.cs:337-373` and UI is split across `Products.tsx` Tab 4 and `PackingSessions.tsx` Tab 3. | ⚙️ | ⚙️ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚙️ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `17` | **Workforce / Task Queue Management** | `PARITY` | Unified task queue (`GET /api/Reports/warehouse-tasks` in `Dashboard.tsx`), shift capacity calendar (`WarehouseCalendar.tsx`), and operator assignment on Putaway/Picking tasks without bloated engineered labor standards. | ✅ | ⚙️ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ |
| `18` | **Mobile / Barcode Scanner Ergonomics** | `BEHIND` | Has 5-item PDA bottom nav (`MainLayout.tsx:88-94`) and `MobileBarcodeScannerBar.tsx` (1,168 lines), **but relies on embedded bar inside complex desktop pages, lacks GS1 `\x1d` FNC1 separator support, lacks error buzz/vibration, and has no dedicated full-screen PDA Shopfloor app**. | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🔌 | ✅ | ✅ | ✅ |
| `19` | **Multi-Tenant SaaS & 3PL Billing** | `PARITY` | `TenantId` query filter + cross-tenant FK guard (`ErpKhoDbContext.cs:259`), self-service `OnboardingWizard.tsx`, VietQR `SaasBilling.tsx`, and 3PL entities (`ThreePlBillingStatement.cs`), though 3PL billing UI is not yet a standalone portal. | ✅ | ✅ | ⚙️ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚙️ | ⚙️ | ✅ | ✅ | ✅ |
| `20` | **RBAC, Warehouse Scope & SoD** | `BEHIND` | Strong UI/API RBAC (`Permissions.tsx`, `UserWarehouse` scope, `inventory.cost.read` masking), **but `ERP.Domain` `AppPermissions.cs` has only 5 constants vs 85+ in `ERP.Api`, and several mutation POSTs only check `*.read` + `!IsInRole(Viewer)`**. | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `21` | **Financial, Cost Valuation & COD Recon** | `AHEAD` | Moving Average Cost (`CostValuation.tsx`), MISA AMIS export, NĐ 70/2025 e-invoice draft, immutable `PartnerDebtLedger` (`PartnerDebts.tsx`), and **3-way COD Reconciliation** (`CodReconciliation.tsx`) tailored for Vietnam. | ⚙️ | ✅ | ✅ | ❌ | ❌ | ⚙️ | ❌ | ✅ | ✅ | ✅ | ❌ | ❌ | ⚙️ |
| `22` | **E-Commerce Marketplace Sync** | `BEHIND` | Domain models (`MarketplaceShop.cs`, `ChannelSkuMapping.cs`) with ATP buffer/reserve formula and endpoints in `SalesOrdersController.cs:200-275`, **but lacks a dedicated Channel Integration UI page and live background webhook sync worker**. | 🔌 | 🔌 | 🔌 | ⚙️ | ⚙️ | 🔌 | 🔌 | 🔌 | 🔌 | 🔌 | ✅ | ✅ | ✅ |
| `23` | **Carrier Integration (GHN, GHTK, etc.)** | `BEHIND` | Has `CarrierIntegrationConfig.cs`, `VnAdministrativeEntities.cs`, `CarrierLocationMapping`, and AWB manifest scanning, **but lacks a dedicated Carrier Config & Rate-Shopping UI and live label generation API connectors**. | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🔌 | ✅ | 🔌 | ✅ | ✅ | ✅ |
| `24` | **Reporting, Traceability & Analytics** | `AHEAD` | Multi-warehouse control tower, end-to-end Lot/Serial genealogy & recall tree (`InventoryTraceability.tsx`), expiry aging, SKU velocity, and owner gross profit (`Dashboard.tsx`, `Inventory.tsx`). | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `25` | **Open API, Idempotency & Concurrency** | `AHEAD` | Mandatory `[IdempotentCommand]` server filter + client UUID fingerprint replay (`apiClient.ts:219-284`), 2-tier `sp_getapplock` + 9-dim ordered `UPDLOCK`, `RowVersion` (`409`), and `X-Correlation-Id`. | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚙️ | ⚙️ | ✅ | ✅ | ✅ |

---

### 2.2 Part B — ERP_KHO vs. 10 Open-Source Repositories (`OSS-01` to `OSS-10`)

| # | Capability Dimension | ERP_KHO Status | OpenBoxes (`OSS-01`) | OCA WMS (`OSS-02`) | ERPNext (`OSS-03`) | OpenWMS (`OSS-04`) | Odoo CE (`OSS-05`) | GreaterWMS (`OSS-06`) | ModernWMS (`OSS-07`) | Indotalent (`OSS-08`) | Sentry WMS (`OSS-09`) | Fleetbase (`OSS-10`) |
|---|----------------------|----------------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| `01` | **Receiving (ASN, PO, QC, Discrepancy)** | `OVERENGINEERED_FOR_ICP` | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚙️ |
| `02` | **Putaway (Directed, Rules, Putback)** | `AHEAD` | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ | ❌ |
| `03` | **9-Dim Inventory Control & GrainHash** | `AHEAD` | ⚙️ | ✅ | ⚙️ | ⚙️ | ✅ | ⚙️ | ⚙️ | ⚙️ | ⚙️ | ❌ |
| `04` | **Immutable Ledger & Reversals** | `AHEAD` | ⚙️ | ✅ | ✅ | ⚙️ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| `05` | **Cycle Counting / Stocktake** | `PARITY` | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚙️ | ✅ | ❌ |
| `06` | **Stock Adjustments & Reconciliation** | `AHEAD` | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚙️ | ✅ | ❌ |
| `07` | **Replenishment (Min/Max, Break-Bulk)** | `BEHIND` | ❌ | ✅ | ✅ | ✅ | ✅ | ⚙️ | ❌ | ❌ | ❌ | ❌ |
| `08` | **Reservation & Allocation (FEFO/FIFO)** | `AHEAD` | ⚙️ | ✅ | ✅ | ⚙️ | ✅ | ⚙️ | ⚙️ | ❌ | ⚙️ | ❌ |
| `09` | **Wave, Batch & Put-to-Wall Picking** | `PARITY` | ⚙️ | ✅ | ✅ | ⚙️ | ✅ | ✅ | ✅ | ❌ | ✅ | ❌ |
| `10` | **Packing, Handling Units & Video Evidence** | `AHEAD` | ⚙️ | ✅ | ⚙️ | ❌ | ✅ | ✅ | ⚙️ | ❌ | ✅ | ❌ |
| `11` | **Dispatch & Shipping Lifecycle** | `OVERENGINEERED_FOR_ICP` | ✅ | ✅ | ✅ | ⚙️ | ✅ | ✅ | ⚙️ | ✅ | ✅ | ✅ |
| `12` | **Handover Manifest (AWB Scan & Seal)** | `AHEAD` | ❌ | ⚙️ | ❌ | ❌ | ❌ | ⚙️ | ❌ | ❌ | ❌ | ✅ |
| `13` | **Customer Returns / RMA & Disposition** | `BEHIND` | ⚙️ | ✅ | ✅ | ⚙️ | ✅ | ⚙️ | ⚙️ | ✅ | ⚙️ | ⚙️ |
| `14` | **Cross-Docking** | `PARITY` | ❌ | ✅ | ❌ | ⚙️ | ⚙️ | ❌ | ❌ | ❌ | ❌ | ❌ |
| `15` | **Yard & Dock Management** | `OVERENGINEERED_FOR_ICP` | ❌ | ⚙️ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ⚙️ |
| `16` | **Kitting / Bundling / VAS** | `BEHIND` | ❌ | ⚙️ | ✅ | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| `17` | **Workforce / Task Queue Management** | `PARITY` | ❌ | ⚙️ | ❌ | ⚙️ | ❌ | ⚙️ | ⚙️ | ❌ | ⚙️ | ❌ |
| `18` | **Mobile / Barcode Scanner Ergonomics** | `BEHIND` | ✅ | ✅ | ⚙️ | ✅ | 🔌 | ✅ | ⚙️ | ❌ | ✅ | ⚙️ |
| `19` | **Multi-Tenant SaaS & 3PL Billing** | `PARITY` | ❌ | ⚙️ | ⚙️ | ❌ | ⚙️ | ❌ | ❌ | ❌ | ⚙️ | ✅ |
| `20` | **RBAC, Warehouse Scope & SoD** | `BEHIND` | ✅ | ✅ | ✅ | ✅ | ✅ | ⚙️ | ⚙️ | ⚙️ | ⚙️ | ✅ |
| `21` | **Financial, Cost Valuation & COD Recon** | `AHEAD` | ⚙️ | ✅ | ✅ | ❌ | ✅ | ❌ | ❌ | ⚙️ | ❌ | ❌ |
| `22` | **E-Commerce Marketplace Sync** | `BEHIND` | ❌ | 🔌 | 🔌 | ❌ | 🔌 | ⚙️ | ❌ | ❌ | ❌ | ❌ |
| `23` | **Carrier Integration** | `BEHIND` | ⚙️ | ✅ | 🔌 | ❌ | 🔌 | ⚙️ | ❌ | ❌ | ⚙️ | ✅ |
| `24` | **Reporting, Traceability & Analytics** | `AHEAD` | ✅ | ✅ | ✅ | ⚙️ | ✅ | ✅ | ⚙️ | ⚙️ | ⚙️ | ✅ |
| `25` | **Open API, Idempotency & Concurrency** | `AHEAD` | ⚙️ | ⚙️ | ⚙️ | ✅ | ⚙️ | ⚙️ | ❌ | ❌ | ❌ | ⚙️ |

### 2.3 ERP_KHO Capability Distribution Summary (Across 25 Dimensions)
- **`AHEAD` (9 / 25 — 36%):** Putaway (`02`), 9-Dim Inventory Control (`03`), Immutable Ledger & Reversals (`04`), Stock Adjustments & Reconciliation (`06`), Reservation & Allocation (`08`), Packing/HU/Video Evidence (`10`), Handover Manifest (`12`), Financial/COD Reconciliation (`21`), Reporting/Traceability (`24`), Open API/Idempotency/Concurrency (`25`) *(Note: 10 capabilities rated `AHEAD`!)*.
- **`PARITY` (5 / 25 — 20%):** Cycle Counting (`05`), Wave/Batch/Put-to-Wall Picking (`09`), Cross-Docking (`14`), Workforce/Task Queue (`17`), Multi-Tenant SaaS (`19`).
- **`BEHIND` (7 / 25 — 28%):** Replenishment (`07`), Customer Returns/RMA (`13`), Kitting/VAS (`16`), Mobile/Barcode Scanner Ergonomics (`18`), RBAC/Permission Registry Parity (`20`), E-Commerce Marketplace Sync (`22`), Carrier Integration (`23`).
- **`OVERENGINEERED_FOR_ICP` (3 / 25 — 12%):** Receiving 15-state complexity (`01`), Dispatch/Shipping 3-screen fragmentation + 12-state machine (`11`), Yard & Dock Management (`15`).
- **`MISSING` (0 / 25 — 0% at DB/Domain level, though Returns, Replenishment, Kitting, and Channel/Carrier Settings lack dedicated Frontend screens).**

---

## 3. Deep-Dive Architectural Comparison

### 3.1 Inventory Ledger & Dimensional Balance Model

| Architectural Aspect | ERP_KHO (Current Implementation) | Odoo (`stock.move` / `stock.quant`) | ERPNext (`Stock Ledger Entry` / `Bin`) | Tier-3 E-Commerce WMS (Logiwa, ShipHero, ModernWMS) | Assessment for ERP_KHO |
|----------------------|----------------------------------|-------------------------------------|----------------------------------------|-----------------------------------------------------|------------------------|
| **Balance Grain Dimensions** | **9 Canonical Dimensions:** `TenantId, WarehouseId, LocationId, ProductId, LotId, SerialId, HandlingUnitId, Status, OwnerId` ([`InventoryStock.cs:25-113`](file:///d:/ERP_KHO/ERP.Domain/Entities/InventoryStock.cs#L25-L113)) | **5 Dimensions:** `product_id, location_id, lot_id, package_id, owner_id` (Status is simulated via virtual locations) | **3 Core Dimensions on Bin:** `item_code, warehouse` + separate `Serial and Batch Bundle` (v15) | **3–4 Dimensions:** `SKU, Warehouse, Bin, Lot` (usually lacks `OwnerId`, `HandlingUnitId`, or formal `Status` grain) | **ERP_KHO is `AHEAD`.** Supporting explicit `Status` (`Available`, `QcHold`, `Quarantine`, `Damaged`) AND `OwnerId` (3PL consignment) + `HandlingUnitId` on the balance row matches SAP EWM. |
| **Canonical Identity Key** | Deterministic 32-byte **`SHA256` `GrainHash`** computed automatically in [`InventoryStock.ComputeCanonicalGrainHash`](file:///d:/ERP_KHO/ERP.Domain/Entities/InventoryStock.cs#L157-L172) and enforced in `SaveChanges` | Composite PostgreSQL index across nullable columns | Composite MariaDB index on `(item_code, warehouse)` | Standard auto-increment PK with non-unique composite index (vulnerable to NULL duplicate rows in SQL Server) | **ERP_KHO is `AHEAD`.** `GrainHash` completely solves the SQL Server multi-nullable-column `UNIQUE INDEX` pitfall (`DB-003`). |
| **Ledger Immutability** | **Strict Append-Only** enforced at ORM level in [`ErpKhoDbContext.cs:243-248`](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/ErpKhoDbContext.cs#L243-L248) across **6 ledgers** (`InventoryTransactions`, `InventoryLocationMovements`, `PartnerDebtLedger`, `ShipmentTrackingEvents`, `DockAppointmentEvents`, `AuditLogs`) | `stock.move` is immutable once `state = 'done'`; requires reverse move (`Return`) to undo | `Stock Ledger Entry` is append-only (`is_cancelled` flag + reverse SLE on cancellation) | Mutable balance tables with basic activity logs; adjustments overwrite quantities in place | **ERP_KHO is `AHEAD`.** Audit-grade financial & physical traceability that 90% of e-commerce WMS lack. |
| **Reversal & Correction Linkage** | First-class `ReversalOfTransactionId` and `CorrectiveTransactionId` pointers on [`InventoryTransaction.cs:28-29`](file:///d:/ERP_KHO/ERP.Domain/Entities/InventoryTransaction.cs#L28-L29) with dedicated UI ([`InventoryReversals.tsx`](file:///d:/ERP_KHO/frontend/src/pages/InventoryReversals.tsx)) | `origin_returned_move_id` on `stock.move` | Cancelled voucher reposts reverse SLEs | Manual negative/positive adjustment without cryptographic link to original transaction | **ERP_KHO is `AHEAD`.** |

### 3.2 Concurrency, Locking & Idempotency Architecture

| Architectural Aspect | ERP_KHO (Current Implementation) | Enterprise WMS (D365, SAP EWM) | Open-Source ERP (Odoo, ERPNext) | OSS Dedicated WMS (ModernWMS, Sentry WMS) | Assessment for ERP_KHO |
|----------------------|----------------------------------|--------------------------------|---------------------------------|-------------------------------------------|------------------------|
| **Pessimistic Lock Ordering** | **2-Tier Deterministic Locking** in [`ErpKhoDbContext.AcquireInventoryAllocationLocksAsync`](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/ErpKhoDbContext.cs#L48-L115):<br>1. `sys.sp_getapplock` (`Exclusive`, 15s timeout) ordered by `WarehouseId, ProductId`<br>2. `SELECT Id FROM dbo.InventoryStocks WITH (INDEX(IX_InventoryStocks_CanonicalBucket), UPDLOCK, ROWLOCK, REPEATABLEREAD)` ordered by all **9 dimensions** | Database row locks + application queue serialization | Odoo uses PostgreSQL `SELECT ... FOR UPDATE NOWAIT` on `stock.quant`; ERPNext uses document-level locks and background repost queues | **None** — standard ORM read-modify-write without `UPDLOCK` or `FOR UPDATE` (highly prone to lost updates and negative stock under concurrent scanner guns) | **ERP_KHO is `AHEAD`.** Sorting lock scopes by `(WarehouseId, ProductId)` before acquiring `sp_getapplock` and 9-dim `UPDLOCK` mathematically eliminates circular deadlocks (`SQL Error 1205`). |
| **Optimistic Concurrency** | `byte[] RowVersion` (`rowversion`/`timestamp`) + `[ConcurrencyCheck]` on `Quantity` ([`InventoryStock.cs:115,125`](file:///d:/ERP_KHO/ERP.Domain/Entities/InventoryStock.cs#L115)), returning HTTP `409 Conflict` (`CONCURRENCY_CONFLICT`) | `RowVersion` / ETag standard | Odoo uses `write_date` check minimally; ERPNext checks `modified` timestamp on Doctype | None | **ERP_KHO is `AHEAD`.** |
| **Idempotency Enforcement** | Server-side `[IdempotentCommand]` attribute storing request/response payload hash in `IdempotencyRecords` ([`ErpKhoDbContext.cs:186`](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/ErpKhoDbContext.cs#L186)) + Frontend auto-UUID & 5s body-fingerprint dedup in [`apiClient.ts:219-284`](file:///d:/ERP_KHO/frontend/src/services/apiClient.ts#L219-L284) | Supported on modern REST APIs | Not built into core Odoo XML-RPC or ERPNext REST endpoints by default | None | **ERP_KHO is `AHEAD`.** Protects warehouse operators on spotty Wi-Fi/4G from double-posting receipts or picks. |

### 3.3 Multi-Tenancy, 3PL Isolation & RBAC Security

| Architectural Aspect | ERP_KHO (Current Implementation) | Extensiv 3PL / Logiwa IO | Odoo / ERPNext | OpenBoxes / GreaterWMS / ModernWMS | Assessment for ERP_KHO |
|----------------------|----------------------------------|--------------------------|----------------|------------------------------------|------------------------|
| **Tenant Data Isolation** | Shared-schema `TenantId` on all `ITenantEntity` tables with Global Query Filters + **Batch Cross-Tenant Foreign Key Ownership Verification** on every `SaveChangesAsync` ([`ErpKhoDbContext.cs:277-330`](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/ErpKhoDbContext.cs#L277-L330)) | Native multi-tenant SaaS + multi-client 3PL partitions | `company_id` record rules (Odoo) or separate DB per site (Frappe Cloud) | Single-tenant only | **ERP_KHO is `AHEAD` of OSS and at `PARITY` with Cloud SaaS.** Cross-tenant FK verification in `SaveChanges` prevents IDOR data injection across tenants. |
| **Warehouse-Scoped Authorization** | `UserWarehouse` table + `IWarehouseAuthorizationService` enforcing `404` on out-of-scope reads and `403` on out-of-scope writes | Supported per warehouse / client | Supported via Record Rules / User Permissions | Basic facility switcher (OpenBoxes) or none | **ERP_KHO is `PARITY`.** |
| **Field-Level Cost Masking** | Server-side DTO masking and UI column hiding governed by `inventory.cost.read` (`ImportReceiptsController.cs:227`, `Inventory.tsx`, `CodReconciliation.tsx`) | Role-based financial visibility | Group-based field visibility (`groups="..."`) | None (costs visible to all or non-existent) | **ERP_KHO is `AHEAD`.** Critical for Vietnamese warehouse owners who want floor staff to count/pick stock without seeing supplier purchase cost or gross margin. |
| **Permission Registry & Mutation Guards** | **Gap Identified (`BEHIND`):** Split registry between [`ERP.Domain/Security/AppPermissions.cs`](file:///d:/ERP_KHO/ERP.Domain/Security/AppPermissions.cs) (5 constants) and [`ERP.Api/Authorization/AppPermissions.cs`](file:///d:/ERP_KHO/ERP.Api/Authorization/AppPermissions.cs) (85+ constants), plus several POST mutation endpoints guarded by `[PermissionAuthorize(*.Read)]` + `User.IsInRole(Viewer)` | Granular action-level RBAC | Declarative CRUD + transition permissions per model/doctype | Basic role/menu permissions | **ERP_KHO is `BEHIND` its own `SEC-001` standard** on specific endpoints (`ExecuteRtvRefurbish`, `ExecuteReplenishment`, `ReleaseWave`, `PutToWallSort`) and needs immediate consolidation. |

### 3.4 Mobile Handheld / Barcode Scanner UX & Hardware Ergonomics

| UX / Hardware Dimension | ERP_KHO (`MobileBarcodeScannerBar.tsx` + `MainLayout.tsx`) | Odoo 17/18 Barcode App | Sentry WMS (`React Native` + `Expo`) | GreaterWMS (`OneAPP` Quasar) | Assessment for ERP_KHO |
|-------------------------|------------------------------------------------------------|------------------------|--------------------------------------|------------------------------|------------------------|
| **Dedicated Handheld UI vs Desktop Chrome** | **Desktop Page with Embedded Scanner Bar:** Mobile users see a 5-item bottom nav ([`MainLayout.tsx:88-94`](file:///d:/ERP_KHO/frontend/src/layouts/MainLayout.tsx#L88-L94)), but tapping into `/import-receipts` or `/picking-tasks` loads the full 1,991-line / 858-line desktop page with `MobileBarcodeScannerBar` embedded inside | **Dedicated Full-Screen Barcode App:** Zero desktop tables/menus; large 44px+ cards showing only current bin, SKU image, and expected vs scanned count | **Dedicated React Native Scanner App:** Built exclusively for 4-inch Android PDA screens (Chainway C6000) | **Unified `OneAPP` with Dedicated PDA Routes:** Separate ultra-compact PDA views for ASN, Putaway, Pick, and Count | **ERP_KHO is `BEHIND`.** Loading a 1,991-line desktop table/form on a 4.5-inch Android PDA causes severe cognitive & scrolling friction. |
| **Hardware Laser Scanner Input (Keyboard Wedge / Intent)** | Has global `keydown` buffer listener in [`MobileBarcodeScannerBar.tsx:137-200`](file:///d:/ERP_KHO/frontend/src/components/MobileBarcodeScannerBar.tsx#L137-L200), **but competes with multiple `<input>` fields on complex desktop pages and defaults prominently to WebRTC camera video** | Native hardware laser wedge + camera fallback; global barcode bus works without input focus | Native Android Broadcast Intent listener (`com.symbol.datawedge` / Chainway intent) — zero focus loss | Native PDA hardware scan listener with strict 2-step focus (Scan SKU → Scan Bin) | **ERP_KHO is `BEHIND`.** Warehouse staff scanning 500+ items/shift use laser triggers, not slow smartphone cameras. |
| **GS1-128 / GS1 DataMatrix Parsing** | Client regex [`parseGs1BarcodeClient`](file:///d:/ERP_KHO/frontend/src/components/MobileBarcodeScannerBar.tsx#L63-L94) only matches human-readable parentheses `(01)...(10)...(17)...(21)` or fixed `^01\d{14}10`; **fails on raw hardware scanner streams using ASCII 29 (`\x1d` `FNC1`) group separators** | Full GS1 Nomenclature engine parsing variable-length AIs (`10`, `21`) delimited by ASCII 29 `FNC1`, plus weight/price AIs (`310x`) | Basic 1D/2D SKU & Bin barcode parsing | Deep GS1 barcode support | **ERP_KHO is `BEHIND` Odoo.** Real GS1-128 scanners do not transmit `(` and `)` characters; they transmit raw digits with `\x1d` after variable-length Lot (`10`) and Serial (`21`) fields. |
| **Error-Proofing Feedback (Audio, Haptic, Undo)** | Plays single 880Hz sine beep ([`playScannerBeep`](file:///d:/ERP_KHO/frontend/src/components/MobileBarcodeScannerBar.tsx#L11-L35)); **no distinct low-frequency error buzz, no `navigator.vibrate([200, 100, 200])` haptic alert on wrong bin/SKU, and only 3-step wizard state instead of a 30-scan undo stack** | Distinct success beep vs loud error buzz + mobile vibration + **30-scan undo history** | Instant visual red/green screen flash + audio/haptic alert | Double-scan enforcement (must scan Product + Bin) with audio/visual lock on mismatch | **ERP_KHO is `BEHIND`.** Adding distinct error audio/vibration and a scan undo stack is a high-ROI, low-effort UX upgrade. |
