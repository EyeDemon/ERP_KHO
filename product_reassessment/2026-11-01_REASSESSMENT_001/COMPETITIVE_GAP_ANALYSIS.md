# Deliverable E: Competitive Gap Analysis (`COMPETITIVE_GAP_ANALYSIS.md`)

**Run ID:** `2026-11-01_REASSESSMENT_001`  
**Document ID:** Deliverable E  
**Date Compiled:** 2026-11-01  
**Baseline Commit:** `6e0a333390c1db6222e55ae3945b7ce33a56bef6` (`feat/WMS-61-project-development`)  
**Scope:** Forensic competitive and architectural gap analysis classifying **26 concrete product, UX, security, and engineering gaps (`GAP-001` through `GAP-026`)** across all **17 Mandatory Gap Categories**, benchmarked against the 23 reference systems in Deliverables C & D and calibrated for **ERP_KHO's Target ICP** (1 warehouse, 5–30 staff, Vietnamese Shopee/Lazada/TikTok Shop e-commerce fulfillment and distribution).

---

## 1. Executive Summary & Coverage Across the 17 Gap Categories

| # | Gap Category Code | Gap IDs Assigned | Count | Highest Priority | Key Theme |
|---|-------------------|------------------|:-----:|:----------------:|-----------|
| 1 | `MISSING_CAPABILITY` | `GAP-001`, `GAP-002` | 2 | `P0` | Dedicated Customer Returns (RMA) UI & Replenishment Workbench UI |
| 2 | `INCOMPLETE_WORKFLOW` | `GAP-018`, `GAP-019` | 2 | `P1` | End-to-end Kitting/VAS execution & Vietnamese Carrier rate-shopping/AWB print |
| 3 | `MISSING_STATE_OR_TRANSITION` | `GAP-021` | 1 | `P2` | Stringly-typed `Status` on `CustomerReturn` & `KitWorkOrder` instead of Domain Enums |
| 4 | `MISSING_EXCEPTION_FLOW` | `GAP-022` | 1 | `P1` | Failed delivery (`ReturnToWarehouse`) not auto-triggering RMA quarantine intake |
| 5 | `MISSING_VALIDATION_OR_GUARD` | `GAP-023` | 1 | `P1` | `[HttpGet]` bound to stock-mutating `AutoBreakBulkReplenish` action |
| 6 | `DUPLICATE_OR_OVERLAPPING_FEATURE` | `GAP-006`, `GAP-007` | 2 | `P1` | Duplicate Replenishment endpoints & 4-screen Outbound fulfillment fragmentation |
| 7 | `MISPLACED_MODULE_OWNERSHIP` | `GAP-003`, `GAP-004`, `GAP-005` | 3 | `P1` | RMA in `ImportReceiptsController`, Kitting in `ProductsController`, Marketplace in `SalesOrdersController`, 3PL in `BusinessPartnersController` |
| 8 | `UI_UX_FRICTION` | `GAP-016`, `GAP-017` | 2 | `P1` | 2,000-line God pages & mixed `/api/<PascalCase>` vs `/api/v1/<kebab-case>` routes |
| 9 | `NAVIGATION_OVERWHELM` | `GAP-015` | 1 | `P1` | 36+ sidebar items across 45+ routes overwhelming 5–30 staff |
| 10 | `MOBILE_SCANNER_GAP` | `GAP-011`, `GAP-012` | 2 | `P0` | No dedicated full-screen PDA Shopfloor UI; missing GS1 `\x1d` `FNC1` & error haptics/undo |
| 11 | `REPORTING_GAP` | `GAP-024` | 1 | `P2` | Missing floor picker/packer SLA leaderboard ("Hero Board") & SKU return-rate analytics |
| 12 | `INTEGRATION_RELIABILITY_GAP` | `GAP-020` | 1 | `P1` | Missing Outbox/Inbox Dead-Letter & Marketplace Shop OAuth/Sync Health UI |
| 13 | `SECURITY_OR_PERMISSION_GAP` | `GAP-008`, `GAP-009` | 2 | `P0` | Split `AppPermissions` registry (5 vs 85+) & POST mutations guarded by `*.read` + `IsInRole` |
| 14 | `DATA_INTEGRITY_RISK` | `GAP-010` | 1 | `P0` | Null-service fallback branches in Controllers returning fabricated `200 OK` payloads |
| 15 | `OVERENGINEERED_FOR_ICP` | `GAP-013`, `GAP-014` | 2 | `P1` / `DEFERRED` | 15-state `ReceiptStatus` friction for SMB 1-step receiving & 10-state `DockYard` overkill |
| 16 | `PERFORMANCE_OR_SCALABILITY_RISK` | `GAP-025` | 1 | `P2` | 100KB+ monolithic React page chunks & missing React Error Boundaries |
| 17 | `TEST_COVERAGE_GAP` | `GAP-026` | 1 | `P1` | Controller unit tests relying on optional `= null` service injection instead of real handlers |

### Priority Distribution Summary
- **`P0` (Critical / Blocker for ICP Go-Live — 5 Gaps):** `GAP-001`, `GAP-008`, `GAP-009`, `GAP-010`, `GAP-011`
- **`P1` (High-Impact Workflow, Architecture & UX — 14 Gaps):** `GAP-002`, `GAP-003`, `GAP-006`, `GAP-007`, `GAP-012`, `GAP-013`, `GAP-015`, `GAP-016`, `GAP-018`, `GAP-019`, `GAP-020`, `GAP-022`, `GAP-023`, `GAP-026`
- **`P2` (Medium-Term Polish, Domain Hygiene & Analytics — 6 Gaps):** `GAP-004`, `GAP-005`, `GAP-017`, `GAP-021`, `GAP-024`, `GAP-025`
- **`DEFERRED` (Out of Scope for Current ICP — 1 Gap):** `GAP-014`

---

## 2. Detailed Gap Catalog (`GAP-001` to `GAP-026`)

---

### `GAP-001`: Missing Dedicated Customer Returns (RMA), Inspection & Disposition Frontend Module
- **Category:** `MISSING_CAPABILITY`
- **Priority:** `P0`
- **Current ERP_KHO State (File Evidence):**
  - Backend domain entities [`CustomerReturn`](file:///d:/ERP_KHO/ERP.Domain/Entities/CustomerReturnEntities.cs#L5-L22) and [`CustomerReturnLine`](file:///d:/ERP_KHO/ERP.Domain/Entities/CustomerReturnEntities.cs#L24-L40) exist with `Condition` (`GOOD | DAMAGED_BOX | DEFECTIVE`) and `Disposition` (`PENDING | RESTOCK_AVAILABLE | REFURBISH_REPACK | RTV_VENDOR | SCRAP_DISPOSAL`), registered in [`ErpKhoDbContext.cs:226-227`](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/ErpKhoDbContext.cs#L226-L227).
  - However, [`AppRoutes.tsx:77-133`](file:///d:/ERP_KHO/frontend/src/routes/AppRoutes.tsx#L77-L133) and [`productionNavigation.ts:61`](file:///d:/ERP_KHO/frontend/src/config/productionNavigation.ts#L61) contain **zero route or page** for Customer Returns / RMA.
- **Benchmark Reference:**
  - **Logiwa IO (`COM-11`)** & **ShipHero WMS (`COM-12`)**: Provide dedicated scan-by-AWB/Order return receiving, condition grading, photo capture, and 1-click disposition to restockable vs damaged bins.
  - **Odoo Inventory (`COM-09`)** & **ERPNext (`COM-10`)**: First-class Return workflow linked to original Delivery Order / Sales Order.
- **Business Impact on ICP:**
  - Vietnamese e-commerce sellers on Shopee, TikTok Shop, and Lazada face a **10–25% return / failed-delivery ("hoàn hàng") rate**. Without a dedicated screen to scan returned AWB parcels, inspect item condition, deduct packaging materials (`PackagingProductId`), and route stock from `Quarantine` back to `Available` or `Damaged`, warehouse staff must manually hack `ImportReceipts` or stock adjustments, losing reconciliation between returned parcels and carrier COD statements.
- **Recommended TO-BE Solution:**
  - Create a dedicated `/customer-returns` page (`CustomerReturns.tsx`) under **Nhập kho** (or a unified **Hoàn hàng / RMA** workflow) allowing operators to: (1) Scan carrier AWB or `OrderNumber` to auto-populate items from `Shipment`/`ExportReceipt`, (2) Record `Condition` (`GOOD`, `DAMAGED_BOX`, `DEFECTIVE`), (3) Execute `Disposition` (`RESTOCK_AVAILABLE`, `REFURBISH_REPACK`, `RTV_VENDOR`, `SCRAP_DISPOSAL`) via the canonical 9-dimension inventory engine.

---

### `GAP-002`: Missing Dedicated Replenishment & Min/Max Pick-Face Workbench UI
- **Category:** `MISSING_CAPABILITY`
- **Priority:** `P1`
- **Current ERP_KHO State (File Evidence):**
  - Backend implements replenishment suggestions (`GET /api/v1/inventory-control/replenishment-suggestions`), replenishment execution (`POST /api/v1/inventory-control/execute-replenishment`), and case-to-each break-bulk (`POST /api/v1/inventory-control/replenishment/break-bulk`) in [`InventoryControlController.cs:84-168`](file:///d:/ERP_KHO/ERP.Api/Controllers/InventoryControlController.cs#L84-L168).
  - Frontend [`AppRoutes.tsx`](file:///d:/ERP_KHO/frontend/src/routes/AppRoutes.tsx) has no dedicated Replenishment view or Min/Max bin configuration workbench; replenishment is only minimally surfaced inside internal movement utilities.
- **Benchmark Reference:**
  - **D365 WMS (`COM-02`)** & **NetSuite WMS (`COM-08`)**: Dedicated Min/Max and wave-demand replenishment queue that triggers reserve-to-pick-face moves before pickers arrive at empty forward bins.
  - **Odoo (`COM-09`)** & **ERPNext (`COM-10`)**: Clear Reordering Rules / Orderpoints screen per bin/SKU.
- **Business Impact on ICP:**
  - During Shopee 11.11 / 12.12 flash sales, forward picking bins on ground level empty rapidly while bulk pallets sit on high racks. Without a clear Replenishment & Break-Bulk queue on UI/PDA, pickers hit `PICK_SHORT` exceptions mid-wave, stalling packing stations.
- **Recommended TO-BE Solution:**
  - Surface a dedicated **Bổ sung kệ nhặt (Replenishment)** tab inside [`InventoryMovements.tsx`](file:///d:/ERP_KHO/frontend/src/pages/InventoryMovements.tsx) (or PDA task queue) connected to `GET /api/v1/inventory-control/replenishment-suggestions` and `POST /api/v1/inventory-control/execute-replenishment`.

---

### `GAP-003`: Customer Returns (RMA) & RTV Endpoints Buried Inside `ImportReceiptsController`
- **Category:** `MISPLACED_MODULE_OWNERSHIP`
- **Priority:** `P1`
- **Current ERP_KHO State (File Evidence):**
  - `POST rma-intake-check` (lines 155–182), `GET customer-returns` (lines 184–186), and `POST execute-rtv-refurbish` (lines 195–222) are implemented directly inside [`ImportReceiptsController.cs:155-222`](file:///d:/ERP_KHO/ERP.Api/Controllers/ImportReceiptsController.cs#L155-L222) under `/api/ImportReceipts/...` instead of a dedicated `CustomerReturnsController`.
- **Benchmark Reference:**
  - **Clean Architecture (`ARCH-002`, `API-001`)**, **OpenWMS.org (`OSS-04`)**, and **Logiwa IO (`COM-11`)**: Inbound Supplier PO Receipts (`inbound-receipts`) and Customer Returns / Vendor RTV (`customer-returns`) have distinct lifecycles, permissions, and accounting impacts (AP vs AR/COGS reversal).
- **Business Impact on ICP:**
  - Coupling customer return parcel intake and vendor RTV refurbishment to supplier import receipts confuses permissions (`receipt.read` is misused for RMA mutations) and bloats `ImportReceiptsController`.
- **Recommended TO-BE Solution:**
  - Extract RMA intake, customer return listing, inspection, and RTV/refurbish disposition into `[Route("api/v1/customer-returns")]` (`CustomerReturnsController.cs`) with dedicated permissions (`customer_return.read`, `customer_return.inspect`, `customer_return.dispose`).

---

### `GAP-004`: Kit Work Orders (VAS) & Marketplace Shop OAuth Buried Inside `ProductsController` and `SalesOrdersController`
- **Category:** `MISPLACED_MODULE_OWNERSHIP`
- **Priority:** `P2`
- **Current ERP_KHO State (File Evidence):**
  - Warehouse kitting execution endpoints (`GET/POST kit-work-orders`, `POST kit-work-orders/{id}/complete`, `POST kit-work-orders/{id}/cancel`) are placed inside master-data [`ProductsController.cs:337-373`](file:///d:/ERP_KHO/ERP.Api/Controllers/ProductsController.cs#L337-L373) and guarded by `product.update` (a catalog admin permission, not a warehouse floor execution permission!).
  - Marketplace OAuth & inventory push endpoints (`GET/POST marketplace-shops`, `oauth-exchange`, `oauth-refresh`, `sync-inventory`) are placed inside [`SalesOrdersController.cs:200-275`](file:///d:/ERP_KHO/ERP.Api/Controllers/SalesOrdersController.cs#L200-L275).
- **Benchmark Reference:**
  - **SAP EWM (`COM-03`)** & **Logiwa IO (`COM-11`)**: BOM/Bundle definition belongs to Catalog (`Products`), whereas **Kit Work Order Execution** (consuming component stock and minting kit stock in `InventoryTransactions`) belongs to Warehouse Execution / VAS. Similarly, Channel OAuth & Sync belongs to an Integration / Channels controller.
- **Business Impact on ICP:**
  - To let a warehouse packer complete a promotional gift-box kit work order before a Shopee campaign, the admin currently has to grant `product.update` (allowing the packer to edit master SKU prices/barcodes!).
- **Recommended TO-BE Solution:**
  - Move Kit Work Order execution endpoints to a dedicated `/api/v1/kit-work-orders` route (or `PackingSessionsController` / `InventoryControlController`) guarded by `kitting.execute`, and extract Marketplace Shop endpoints to `/api/v1/marketplace-channels`.

---

### `GAP-005`: 3PL Billing Computation Buried Inside `BusinessPartnersController` Without Dedicated 3PL Billing UI
- **Category:** `MISPLACED_MODULE_OWNERSHIP`
- **Priority:** `P2`
- **Current ERP_KHO State (File Evidence):**
  - Domain entities [`ThreePlStorageSnapshot`](file:///d:/ERP_KHO/ERP.Domain/Entities/ThreePlStorageSnapshot.cs) and [`ThreePlBillingStatement`](file:///d:/ERP_KHO/ERP.Domain/Entities/ThreePlBillingStatement.cs) exist in `ErpKhoDbContext.cs:233-234`, but 3PL billing logic (`ComputeDefault3PlBilling`) is embedded inside [`BusinessPartnersController.cs:242-295`](file:///d:/ERP_KHO/ERP.Api/Controllers/BusinessPartnersController.cs#L242-L295) rather than a dedicated 3PL Billing module.
- **Benchmark Reference:**
  - **Extensiv 3PL Warehouse Manager (`COM-13`)** & **Infor WMS (`COM-06`)**: Dedicated 3PL Activity Billing engine and Client Portal separated from generic Supplier/Customer address-book CRUD.
- **Business Impact on ICP:**
  - For Vietnamese mini-3PLs storing goods for multiple merchants (`OwnerId`), burying 3PL storage/handling fee calculation inside the Business Partners screen makes monthly client billing hard to discover and audit.
- **Recommended TO-BE Solution:**
  - Consolidate 3PL storage snapshots and billing statement generation under the **Tài chính** section (either as a dedicated tab in `PartnerDebts.tsx` / `CostValuation.tsx` or `/api/v1/three-pl-billing`).

---

### `GAP-006`: Duplicate Replenishment & Break-Bulk Endpoints Across `InventoryControlController` and `InventoryMovementsController`
- **Category:** `DUPLICATE_OR_OVERLAPPING_FEATURE`
- **Priority:** `P1`
- **Current ERP_KHO State (File Evidence):**
  - [`InventoryControlController.cs:150-168`](file:///d:/ERP_KHO/ERP.Api/Controllers/InventoryControlController.cs#L150-L168) defines `[HttpPost("replenishment/break-bulk")]` (`ExecuteBreakBulkReplenishment`).
  - [`InventoryMovementsController.cs:107-155`](file:///d:/ERP_KHO/ERP.Api/Controllers/InventoryMovementsController.cs#L107-L155) separately defines `[HttpPost("auto-break-bulk-replenish")]` (`AutoBreakBulkReplenish`) doing overlapping break-bulk replenishment via a different service (`_movementService` vs `_controlOps`).
- **Benchmark Reference:**
  - **`AGENTS.md` (Ponytail Rule #2: Reuse, don't rewrite; Deletion over addition)** and **`ARCH-004`**: A single warehouse domain action (breaking bulk UOM cartons into pick-face base units) must have exactly one authoritative Command Handler and one canonical `/api/v1/` endpoint.
- **Business Impact on ICP:**
  - Maintaining two separate break-bulk replenishment code paths risks divergent UOM conversion rounding, inconsistent lock ordering, and confusing API consumers.
- **Recommended TO-BE Solution:**
  - Deprecate and remove the duplicate break-bulk endpoint in `InventoryMovementsController.cs`, standardizing all replenishment and break-bulk operations on `InventoryControlController` (or a unified `InventoryMovements` command service).

---

### `GAP-007`: Fragmented Outbound Fulfillment Across 4 Overlapping Screens (`OutboundDemand`, `ExportReceipts`, `Shipments`, `HandoverManifests`)
- **Category:** `DUPLICATE_OR_OVERLAPPING_FEATURE`
- **Priority:** `P1`
- **Current ERP_KHO State (File Evidence):**
  - Sidebar section **Xuất kho** ([`productionNavigation.ts`](file:///d:/ERP_KHO/frontend/src/config/productionNavigation.ts)) exposes **8 separate menu items**: `ExportReceipts.tsx` (1,417 lines, which itself has "Immediate Dispatch" vs "Separate Dispatch" and carrier/tracking fields), `StockReservations.tsx`, `StockAllocations.tsx`, `PickingTasks.tsx`, `PackingSessions.tsx`, `Shipments.tsx` (732 lines, with its own 12-state Staging→Loading→Dispatched→InTransit→Delivered flow), `HandoverManifests.tsx` (373 lines, scanning AWB for courier handover), and `OutboundDemand.tsx` (1,021 lines, Sales Orders + Backorders).
- **Benchmark Reference:**
  - **Odoo Inventory (`COM-09`/`OSS-05`)**: Configurable **1-Step** (Deliver directly), **2-Step** (Pick → Ship), or **3-Step** (Pick → Pack → Ship) outbound routing.
  - **ShipHero (`COM-12`)** & **GreaterWMS (`OSS-06`)**: Linear, unified `Order → Pick → Pack (+ AWB print) → Manifest Handover` flow without forcing operators to create both a `SalesOrder`, an `ExportReceipt`, a `Shipment`, AND a `HandoverManifest` for a single Shopee parcel.
- **Business Impact on ICP:**
  - A 10-person Shopee/TikTok warehouse shipping 500 parcels/day cannot manually navigate through 8 separate screens (`SalesOrder` → `Reservation` → `Allocation` → `Picking` → `Packing` → `ExportReceipt` → `Shipment` → `HandoverManifest`). The mental model between `ExportReceipt` (traditional VN accounting "Phiếu xuất kho") and `SalesOrder` + `Shipment` (WMS fulfillment) is blurred.
- **Recommended TO-BE Solution:**
  - Clarify and streamline the Outbound mental model:
    1. **Express Mode (1–2 Step SMB):** `Phiếu xuất / Đơn hàng` → `Đóng gói & In vận đơn (Pack + AWB)` → `Bàn giao bưu cục (Handover Manifest)` (auto-allocating and auto-generating ledger entries behind the scenes).
    2. **Full WMS Mode (3-Step High Volume):** `Đơn hàng (SO)` → `Sóng lấy hàng (Pick)` → `Đóng gói (Pack)` → `Bàn giao vận chuyển (Ship/Manifest)`, automatically generating the accounting `ExportReceipt` upon dispatch.

---

### `GAP-008`: Split-Brain `AppPermissions` Registry (`ERP.Domain` 5 Constants vs `ERP.Api` 85+ Constants)
- **Category:** `SECURITY_OR_PERMISSION_GAP`
- **Priority:** `P0`
- **Current ERP_KHO State (File Evidence):**
  - [`ERP.Domain/Security/AppPermissions.cs:3-46`](file:///d:/ERP_KHO/ERP.Domain/Security/AppPermissions.cs#L3-L46) defines only **5 constants** (`users.manage`, `roles.manage`, `inventory.read`, `inventory.cost.read`, `export_receipt.reprint_awb`), and its `GetDefaultPermissionsForRole("WarehouseStaff")` returns ONLY `["inventory.read"]`!
  - Meanwhile, [`ERP.Api/Authorization/AppPermissions.cs:3-27`](file:///d:/ERP_KHO/ERP.Api/Authorization/AppPermissions.cs#L3-L27) defines a completely separate `AppPermissions` class with **85+ permission constants** (`receipt.read`, `putaway.execute`, `picking.execute`, `packing.execute`, etc.).
- **Benchmark Reference:**
  - **`PROJECT_RULES.md` (`SEC-001` — Single Permission Registry Spec 17)** and **`AUDIT_STATE.md:69-73`**.
- **Business Impact on ICP:**
  - Any domain service or seeder calling `ERP.Domain.Security.AppPermissions.GetDefaultPermissionsForRole("WarehouseStaff")` grants only `inventory.read`, locking warehouse staff out of receiving, putaway, picking, and packing unless manually patched in DB! Furthermore, having two classes named `AppPermissions` in `ERP.Domain` and `ERP.Api` is a severe maintenance hazard.
- **Recommended TO-BE Solution:**
  - Unify all 85+ canonical permission constants and role default matrices into [`ERP.Domain/Security/AppPermissions.cs`](file:///d:/ERP_KHO/ERP.Domain/Security/AppPermissions.cs), and make `ERP.Api.Authorization.AppPermissions` either alias or directly `using ERP.Domain.Security;`.

---

### `GAP-009`: Stock-Mutating POST Endpoints Guarded by Read-Only Permissions (`*.read`) + Hardcoded `IsInRole(Viewer)` Checks
- **Category:** `SECURITY_OR_PERMISSION_GAP`
- **Priority:** `P0`
- **Current ERP_KHO State (File Evidence):**
  - [`ImportReceiptsController.cs:195-200`](file:///d:/ERP_KHO/ERP.Api/Controllers/ImportReceiptsController.cs#L195-L200): `[HttpPost("execute-rtv-refurbish"), PermissionAuthorize(AppPermissions.ReceiptRead)]` with `if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();`.
  - [`InventoryControlController.cs:133-141`](file:///d:/ERP_KHO/ERP.Api/Controllers/InventoryControlController.cs#L133-L141) & [`153-161`](file:///d:/ERP_KHO/ERP.Api/Controllers/InventoryControlController.cs#L153-L161): `ExecuteReplenishment` and `ExecuteBreakBulkReplenishment` require only `AppPermissions.InventoryRead` + `!IsInRole(Viewer)`.
  - [`PickingTasksController.cs:140-146`](file:///d:/ERP_KHO/ERP.Api/Controllers/PickingTasksController.cs#L140-L146), [`168-173`](file:///d:/ERP_KHO/ERP.Api/Controllers/PickingTasksController.cs#L168-L173), [`188-193`](file:///d:/ERP_KHO/ERP.Api/Controllers/PickingTasksController.cs#L188-L193): `PutToWallSort`, `UpsertWaveTemplate`, and `ReleaseWave` require only `AppPermissions.PickingRead` + `!IsInRole(Viewer)`.
- **Benchmark Reference:**
  - **`rules-security-audit.md` (`[SEC-001] [MUST]`):** *"Mọi API endpoint... bắt buộc phải kiểm tra quyền ở Backend bằng mã quyền định danh trong Permission Registry (Spec 17) và khớp với Endpoint–Permission–State Matrix (Spec 25); tuyệt đối không dùng tên Role cứng (`"Admin"`, `"Staff"`) trong code kiểm quyền nghiệp vụ."*
- **Business Impact on ICP:**
  - Violates Separation of Duties (SoD): an Accountant or Customer Support user granted `receipt.read`, `inventory.read`, or `picking.read` to view order progress can invoke POST endpoints that mutate stock, release picking waves, or execute RTV write-offs because their role name is not `"Viewer"`!
- **Recommended TO-BE Solution:**
  - Replace `[PermissionAuthorize(AppPermissions.*Read)]` and `User.IsInRole(AppRoles.Viewer)` on all mutation endpoints with their proper write permissions (`receipt.receive` / `quality_disposition.approve`, `inventory_movement.create`, `picking.execute`, `picking.assign`).

---

### `GAP-010`: Controller Null-Service Fallbacks Returning Fabricated `200 OK` Success Payloads When `_receiptOps` / `pickingOps` Is Null
- **Category:** `DATA_INTEGRITY_RISK`
- **Priority:** `P0`
- **Current ERP_KHO State (File Evidence):**
  - In [`ImportReceiptsController.cs:161-182`](file:///d:/ERP_KHO/ERP.Api/Controllers/ImportReceiptsController.cs#L161-L182) (`RmaIntakeCheck`) and [`201-222`](file:///d:/ERP_KHO/ERP.Api/Controllers/ImportReceiptsController.cs#L201-L222) (`ExecuteRtvRefurbish`), if `_receiptOps == null`, the controller returns `Ok(new { customerReturnId = (int?)null, rmaCode = $"RMA-{now:yyyyMMdd}-01", status = "Đã thực thi & Ghi sổ kiểm toán", ... })` without touching the database!
  - In [`PickingTasksController.cs:125-137`](file:///d:/ERP_KHO/ERP.Api/Controllers/PickingTasksController.cs#L125-L137) (`GetPutToWallSlots`), [`161-166`](file:///d:/ERP_KHO/ERP.Api/Controllers/PickingTasksController.cs#L161-L166), [`174-178`](file:///d:/ERP_KHO/ERP.Api/Controllers/PickingTasksController.cs#L174-L178), and [`194-212`](file:///d:/ERP_KHO/ERP.Api/Controllers/PickingTasksController.cs#L194-L212) (`ReleaseWave`), if `pickingOps == null`, the controller returns a fabricated `200 OK` response (`WAVE-...`, `SLOT-01..08`, `"Đã phát hành sóng lấy hàng"`).
- **Benchmark Reference:**
  - **`PROJECT_RULES.md` Layer 2 & `[FAKE-001]`:** Strictly forbids returning fabricated success payloads when a service/DB dependency is missing. Compare with [`ProductsController.cs:350-352`](file:///d:/ERP_KHO/ERP.Api/Controllers/ProductsController.cs#L350-L352), which properly returns `StatusCode(503, new { code = "DB_UNAVAILABLE" })`.
- **Business Impact on ICP:**
  - If a DI registration is accidentally omitted or a test instantiates the controller without the operations service, the API silently lies that a wave was released or an RTV stock mutation was audited (`"Đã thực thi & Ghi sổ kiểm toán"`) while persisting nothing.
- **Recommended TO-BE Solution:**
  - Make `IImportReceiptOperationsService` and `IPickingTaskOperationsService` required constructor dependencies (or return HTTP `503 Service Unavailable` if null, matching `ProductsController.cs:351`), and delete all fabricated anonymous `Ok(new { ... })` fallback blocks.

---

### `GAP-011`: Lack of Dedicated Full-Screen Handheld PDA / Shopfloor Mode (Desktop God Pages Loaded on Mobile)
- **Category:** `MOBILE_SCANNER_GAP`
- **Priority:** `P0`
- **Current ERP_KHO State (File Evidence):**
  - [`MainLayout.tsx:88-94`](file:///d:/ERP_KHO/frontend/src/layouts/MainLayout.tsx#L88-L94) renders a 5-item mobile bottom navigation bar (`Tổng quan`, `Nhận hàng`, `Cất hàng`, `Lấy hàng`, `Kiểm kê`), but those links route directly to the desktop pages (`/import-receipts` = 1,991 lines, `/putaway-tasks` = 659 lines, `/picking-tasks` = 858 lines, `/stocktakes` = 1,568 lines).
  - On a 4.5-inch Android PDA (e.g., Zebra TC21, Urovo DT40, Chainway C6000) at `390x844` viewport, the operator must scroll past metric grids, filter toolbars, multi-column data tables, and telemetry panels just to reach the scanning workflow.
- **Benchmark Reference:**
  - **Odoo Barcode App (`COM-09`)**, **OCA `stock-logistics-shopfloor` (`OSS-02`)**, **Sentry WMS Mobile (`OSS-09`)**, and **GreaterWMS `OneAPP` PDA mode (`OSS-06`)**: Provide a dedicated, distraction-free, full-screen Handheld Scanner UI with 48px+ tap targets showing ONLY: (1) Current Task & Target Bin in giant font, (2) Scan Input / Laser Listener, (3) SKU Card with image & `Scanned / Expected` counter, and (4) 1-tap `Confirm` / `Short Pick` buttons.
- **Business Impact on ICP:**
  - Warehouse staff standing in narrow aisles holding a barcode scanner in one hand cannot operate desktop ERP tables on a small screen. This is the #1 operational adoption barrier on the warehouse floor.
- **Recommended TO-BE Solution:**
  - Introduce a dedicated **Handheld / PDA Mode** (or focused full-screen `<ScannerTaskView />` layout when viewport <= 768px or when toggled to `PDA Mode`) for Receiving, Putaway, Picking, Packing, and Cycle Count that hides desktop CRUD tables, analytics charts, and multi-tab chrome, keeping only the step-by-step scan card above the fold.

---

### `GAP-012`: Incomplete GS1-128 Barcode Parser (Missing ASCII 29 `FNC1` Separator) & Missing Error Buzz / Vibration / Undo Stack
- **Category:** `MOBILE_SCANNER_GAP`
- **Priority:** `P1`
- **Current ERP_KHO State (File Evidence):**
  - [`MobileBarcodeScannerBar.tsx:63-94`](file:///d:/ERP_KHO/frontend/src/components/MobileBarcodeScannerBar.tsx#L63-L94) (`parseGs1BarcodeClient`) only matches literal parentheses `(01)...(10)...(17)...(21)` or fixed `^01\d{14}10`. Real industrial 1D/2D scanners reading GS1-128 or GS1 DataMatrix labels emit raw digits without parentheses and use **ASCII 29 (`\x1d` Group Separator / `FNC1`)** to terminate variable-length Lot (`AI 10`) and Serial (`AI 21`) fields.
  - [`MobileBarcodeScannerBar.tsx:11-35`](file:///d:/ERP_KHO/frontend/src/components/MobileBarcodeScannerBar.tsx#L11-L35) (`playScannerBeep`) only plays an 880Hz success tone; there is no dissonant low-frequency error buzzer (`220Hz sawtooth`), no haptic vibration (`navigator.vibrate([200, 100, 200])`) on `PICK_WRONG_BIN` / `PICK_WRONG_PRODUCT`, and no multi-scan undo buffer.
- **Benchmark Reference:**
  - **Odoo 17/18 Barcode (`COM-09`)**: Full GS1 nomenclature parser supporting `\x1d` (`FNC1`) + weight/quantity AIs, distinct success/error audio + vibration feedback, and a **30-scan undo history**.
- **Business Impact on ICP:**
  - FMCG, cosmetics, and pharmaceutical distributors in Vietnam rely heavily on GS1-128 / DataMatrix barcodes for Lot + Expiry tracking. When looking at the shelf instead of the screen, operators need an unmistakable physical vibration and loud error buzz the instant they scan the wrong bin or expired lot.
- **Recommended TO-BE Solution:**
  - Upgrade `parseGs1BarcodeClient` in [`MobileBarcodeScannerBar.tsx`](file:///d:/ERP_KHO/frontend/src/components/MobileBarcodeScannerBar.tsx) to parse both parenthesized human-readable strings and raw `\x1d` (`FNC1`) unparenthesized streams (`01` 14 chars, `17` 6 chars, `10` up to `\x1d`/end, `21` up to `\x1d`/end), add `playScannerErrorBuzz()` + `navigator.vibrate`, and add a 1-tap `Hoàn tác lần quét vừa rồi (Undo Last Scan)` action.

---

### `GAP-013`: 15-State `ReceiptStatus` Inbound Lifecycle Lacks True 1-Click "Express Receive & Putaway" Bypass for SMB Sellers
- **Category:** `OVERENGINEERED_FOR_ICP`
- **Priority:** `P1`
- **Current ERP_KHO State (File Evidence):**
  - [`ReceiptStatus.cs:3-20`](file:///d:/ERP_KHO/ERP.Domain/Enums/ReceiptStatus.cs#L3-L20) defines **15 states** (`Draft`, `Approved`, `Cancelled`, `Dispatched`, `Received`, `ReadyToPost`, `Posted`, `QcPending`, `QcCompleted`, `DiscrepancyPending`, `DiscrepancySubmitted`, `DiscrepancyPendingApproval`, `DiscrepancyResolved`, `DiscrepancyRejected`, `CancelPendingPutback`).
  - While [`MainLayout.tsx:109-124`](file:///d:/ERP_KHO/frontend/src/layouts/MainLayout.tsx#L109-L124) has an `EXPRESS` vs `FULL` sidebar toggle, opening [`ImportReceipts.tsx`](file:///d:/ERP_KHO/frontend/src/pages/ImportReceipts.tsx) still exposes the full multi-step receiving worksheet, QC inspection, discrepancy resolution, and separate Putaway task generation.
- **Benchmark Reference:**
  - **Odoo Inventory (`COM-09`/`OSS-05`)**: Warehouse policy configures **1-Step Receiving** (Receive directly into target bin & post stock in 1 click) vs **2-Step** (Input → Putaway) vs **3-Step** (Input → QC → Putaway).
  - **ERPNext (`COM-10`)**: Purchase Receipt posts directly to target bin in 1 step when QC/Putaway rules are not enabled.
- **Business Impact on ICP:**
  - A 5–10 person Shopee seller receiving 20 cartons of apparel from a local workshop wants to select the supplier, scan/enter quantities, pick a default bin, and click **"Nhập kho nhanh (1 bước)"** in 30 seconds — not click through `Draft → Approve → Receive Line → QC → ReadyToPost → Post → Putaway Task → Assign → Start → Complete`.
- **Recommended TO-BE Solution:**
  - Add a **Warehouse Inbound Policy / Express Receive action** (`1-Step Express Post`) on `ImportReceipts` that (when QC and Discrepancy are not triggered and a target bin is specified) atomically transitions `Draft → Posted` and places stock directly into the target bin within a single transaction.

---

### `GAP-014`: Enterprise 10-State Dock & Yard Management (`DockYard.tsx`) Over-Engineered for Single-Warehouse E-Commerce ICP
- **Category:** `OVERENGINEERED_FOR_ICP`
- **Priority:** `DEFERRED`
- **Current ERP_KHO State (File Evidence):**
  - [`DockYard.tsx`](file:///d:/ERP_KHO/frontend/src/pages/DockYard.tsx) (1,305 lines, 83KB — 3rd largest page in the entire frontend!) and `DockYardController.cs` implement a 10-state `DockAppointmentStatus`, yard slot management, hazmat/temperature dock compatibility, and trailer dwell-time SLA monitoring.
- **Benchmark Reference:**
  - **Commercial Capability Matrix (`Deliverable D`, Row 15)**: None of the Tier-2 or Tier-3 e-commerce WMS benchmarks (**NetSuite, Odoo, ERPNext, Logiwa IO, ShipHero, Extensiv**) include Yard/Dock Trailer Management because SMB/mid-market e-commerce warehouses load vans/trucks directly at 1–3 roll-up doors without a gated trailer yard.
- **Business Impact on ICP:**
  - Maintaining a 1,305-line Yard & Dock module as the **very first item** under **Nhập kho** in `FULL` mode distracts Vietnamese SMB warehouse managers from core PO/Receipt/Putaway operations.
- **Recommended TO-BE Solution:**
  - Keep `DockYard` hidden by default (already hidden in `EXPRESS` mode; move it to the bottom of **Nhập kho** or under an optional `Advanced Enterprise Yard` feature flag in `FULL` mode) and freeze further feature expansion on `DockYard` (`DEFERRED`) until serving multi-dock 3PL distribution centers.

---

### `GAP-015`: 36+ Sidebar Items & 45+ Routes Overwhelm 5–30 Person Teams and Leak Hidden Routes on Direct Navigation
- **Category:** `NAVIGATION_OVERWHELM`
- **Priority:** `P1`
- **Current ERP_KHO State (File Evidence):**
  - [`productionNavigation.ts:61`](file:///d:/ERP_KHO/frontend/src/config/productionNavigation.ts#L61) and [`AppRoutes.tsx:77-133`](file:///d:/ERP_KHO/frontend/src/routes/AppRoutes.tsx#L77-L133) expose **6 sidebar sections with 31–36+ visible navigation items**, plus all 31 items repeated as cards in the Dashboard Navigation Hub (`Dashboard.tsx`).
  - Inventory alone is split across **8 separate sidebar links**: `Inventory`, `InventoryLocks`, `InventoryMovements`, `InventoryReversals`, `InventoryTraceability`, `InventoryReconciliation`, `Stocktakes`, `StockTransfers`.
  - Admin (`Quản trị`) has **10–11 separate sidebar links**: `Onboarding`, `Billing`, `Products`, `Warehouses`, `Locations`, `Map`, `Calendar`, `Units`, `Partners`, `Approvals`, `Permissions`.
- **Benchmark Reference:**
  - **ModernWMS (`OSS-07`)**, **ShipHero (`COM-12`)**, and **Odoo Inventory (`COM-09`)**: Group related sub-views into 5–7 top-level navigation hubs with clean horizontal tabs inside each hub, rather than listing 36 top-level links in the left sidebar.
- **Business Impact on ICP:**
  - High cognitive load during onboarding; new warehouse staff struggle to distinguish between `Biến động tồn`, `Đảo bút toán`, `Truy vết tồn`, and `Đối chiếu tồn`, or between `Kho`, `Cấu trúc kho`, `Bản đồ kho`, and `Lịch làm việc kho`.
- **Recommended TO-BE Solution:**
  - Consolidate the left sidebar into **12–15 high-cohesion workspace hubs** (e.g., merge `Warehouses`, `WarehouseLocations`, `WarehouseMap`, and `WarehouseCalendar` into a single **Quản lý Kho & Vị trí** workspace with 4 tabs; merge `Units` and `Categories` into **Sản phẩm & Danh mục**; group `InventoryLocks`, `InventoryReversals`, `InventoryTraceability`, and `InventoryReconciliation` under an **Kiểm toán & Truy vết Tồn kho** hub).

---

### `GAP-016`: God-Component Pages (`ImportReceipts.tsx` 1,991 Lines, `Permissions.tsx` 2,280 Lines) Combine Too Many Sub-Workflows in One Scroll View
- **Category:** `UI_UX_FRICTION`
- **Priority:** `P1`
- **Current ERP_KHO State (File Evidence):**
  - [`FRONTEND_AUDIT.md:795,808-809`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/FRONTEND_AUDIT.md#L795) documents **12 page components exceeding the 500-line ceiling (`Role 7 Rule`)**, led by:
    - [`Permissions.tsx`](file:///d:/ERP_KHO/frontend/src/pages/Permissions.tsx) — **2,280 lines (109KB)**
    - [`ImportReceipts.tsx`](file:///d:/ERP_KHO/frontend/src/pages/ImportReceipts.tsx) — **1,991 lines (128KB)**
    - [`Stocktakes.tsx`](file:///d:/ERP_KHO/frontend/src/pages/Stocktakes.tsx) — **1,568 lines (70KB)**
    - [`SaasBilling.tsx`](file:///d:/ERP_KHO/frontend/src/pages/SaasBilling.tsx) — **1,455 lines (72KB)**
    - [`ExportReceipts.tsx`](file:///d:/ERP_KHO/frontend/src/pages/ExportReceipts.tsx) — **1,417 lines (74KB)**
    - [`Inventory.tsx`](file:///d:/ERP_KHO/frontend/src/pages/Inventory.tsx) — **1,394 lines (77KB)**
    - [`InventoryReconciliation.tsx`](file:///d:/ERP_KHO/frontend/src/pages/InventoryReconciliation.tsx) — **1,307 lines (81KB)**
    - [`DockYard.tsx`](file:///d:/ERP_KHO/frontend/src/pages/DockYard.tsx) — **1,305 lines (83KB)**
    - [`Products.tsx`](file:///d:/ERP_KHO/frontend/src/pages/Products.tsx) — **1,304 lines (76KB)**
- **Benchmark Reference:**
  - **`SKILL_ROUTING_PROTOCOL.md` Role 7 Rule:** *"Cấm God Component (`>500` dòng phải tách)"* and **OpenBoxes (`OSS-01`) / Odoo (`COM-09`)** wizard/sub-component decomposition.
- **Business Impact on ICP:**
  - Rendering 1,991 lines of state and forms in a single component makes the UI visually dense, slows down low-end warehouse PCs/PDAs on state re-renders, and increases bug surface when editing one sub-panel (e.g., QC disposition vs receipt header creation).
- **Recommended TO-BE Solution:**
  - Decompose every page >500 lines into focused domain sub-components (`ReceiptListTable`, `ReceiptCreateDrawer`, `ReceivingWorksheetPanel`, `ReceiptQcDispositionPanel`, `DiscrepancyResolutionModal`) with isolated React state.

---

### `GAP-017`: Mixed Legacy `/api/<PascalCase>` vs Canonical `/api/v1/<kebab-case-plural>` API Routes Across Frontend & Backend
- **Category:** `UI_UX_FRICTION`
- **Priority:** `P2`
- **Current ERP_KHO State (File Evidence):**
  - [`FRONTEND_AUDIT.md:798,813-821`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/FRONTEND_AUDIT.md#L813-L821) shows that while newer endpoints follow `/api/v1/<kebab-case-plural>` (`/api/v1/handover-manifests`, `/api/v1/permissions/catalog`), ~10 core controllers still expose legacy routes (`/api/ImportReceipts`, `/api/ExportReceipts`, `/api/Inventory/stocks`, `/api/Reports/...`, `/api/Stocktakes`), and [`Dashboard.tsx`](file:///d:/ERP_KHO/frontend/src/pages/Dashboard.tsx) calls both conventions on the same page.
- **Benchmark Reference:**
  - **`PROJECT_RULES.md` Layer 3 & `[API-001]`:** *"Mọi API Controller phải tuân thủ chuẩn đường dẫn `[Route("api/v1/<kebab-case-plural>")]`."*
- **Business Impact on ICP:**
  - Third-party ERP/OMS integrators and frontend developers face inconsistent URL patterns (`/api/ImportReceipts` vs `/api/v1/import-receipts`), complicating API documentation, Postman collections, and webhook/gateway routing rules.
- **Recommended TO-BE Solution:**
  - Ensure every controller has `[Route("api/v1/<kebab-case-plural>")]` as its primary route (retaining `[Route("api/[controller]")]` temporarily as a backward-compatible alias during transition) and standardize all frontend `apiClient` calls and `demoApiAdapter.ts` routes to `/api/v1/<kebab-case-plural>`.

---

### `GAP-018`: Kitting / Bundle Assembly Workflow Disconnected Between `Products.tsx` Tab 4, `PackingSessions.tsx` Tab 3, and `KitWorkOrder` Backend
- **Category:** `INCOMPLETE_WORKFLOW`
- **Priority:** `P1`
- **Current ERP_KHO State (File Evidence):**
  - [`KitWorkOrder.cs:11-51`](file:///d:/ERP_KHO/ERP.Domain/Entities/KitWorkOrder.cs#L11-L51) models planned vs completed kit quantities, component ratio consumption, rolled-up unit cost, and `EarliestComponentExpiryDate` (FEFO propagation from components to assembled kit!).
  - However, on the frontend, bundle BOM setup is in [`Products.tsx`](file:///d:/ERP_KHO/frontend/src/pages/Products.tsx) (Tab 4: Bundle & Gift), ad-hoc kitting is in [`PackingSessions.tsx`](file:///d:/ERP_KHO/frontend/src/pages/PackingSessions.tsx) (Tab 3: Kitting/Video), and there is no unified work-order lifecycle view showing reserved component stock, work-order picking, and FEFO lot inheritance for pre-packed promotional sets.
- **Benchmark Reference:**
  - **Logiwa IO (`COM-11`)** & **ERPNext (`COM-10` Stock Entry — Repack/Manufacture)**: Clear separation between (a) **Virtual Bundle** (exploded at order download time into component pick lines) and (b) **Physical Kit Work Order** (assembled in advance into a new Stock SKU with rolled-up WAC cost and earliest component expiry date).
- **Business Impact on ICP:**
  - Vietnamese cosmetics/FMCG sellers frequently bundle "Mua 1 Tặng 1" or Tet gift boxes. If component expiry dates and costs are not cleanly tracked through a visible Kit Work Order screen, assembled kits either lose FEFO expiry tracking or distort moving-average COGS.
- **Recommended TO-BE Solution:**
  - Provide a unified **Lắp ráp Combo / Kitting (VAS)** work-order panel (accessible from `InventoryMovements` or `PackingSessions`) that connects directly to `GET/POST /api/v1/kit-work-orders` and visibly displays component stock availability, rolled-up unit cost, and inherited earliest expiry date.

---

### `GAP-019`: Carrier Integration Config & Rate-Shopping / Live Thermal AWB Label Generation Not Connected End-to-End in UI
- **Category:** `INCOMPLETE_WORKFLOW`
- **Priority:** `P1`
- **Current ERP_KHO State (File Evidence):**
  - Backend defines [`CarrierIntegrationConfig.cs:5-20`](file:///d:/ERP_KHO/ERP.Domain/Entities/CarrierIntegrationConfig.cs#L5-L20) (`CarrierCode`, `ShopId`, `EncryptedApiToken`, `SortingHubPrefix`, `BaseShippingFeeVnd`), `VnAdministrativeUnit`, and `CarrierLocationMapping` (`ErpKhoDbContext.cs:221-222,232`).
  - However, the frontend has **no UI screen or settings tab** to configure carrier credentials (GHN, GHTK, Viettel Post, J&T, Ninja Van), compare shipping fees (rate shopping) during order packing, or trigger 1-click `100x150mm` thermal AWB PDF generation directly from `PackingSessions.tsx`.
- **Benchmark Reference:**
  - **ShipHero (`COM-12`)** & **Logiwa IO (`COM-11`)**: Built-in carrier credential setup, automatic rate-shopping rule ("pick cheapest carrier for <2kg to Hanoi"), and instant `100x150mm` thermal label printing upon sealing a carton.
- **Business Impact on ICP:**
  - Warehouse packers currently have to copy-paste tracking numbers or switch to external carrier portals (GHN/GHTK web dashboards) to generate waybills, adding 30–45 seconds per parcel.
- **Recommended TO-BE Solution:**
  - Add a **Kết nối Hãng vận chuyển & Sàn TMĐT (Integrations Hub)** screen under **Quản trị** to manage `CarrierIntegrationConfig` and `MarketplaceShop`, and wire a 1-click **"Tạo vận đơn & In tem nhiệt AWB (100x150mm)"** button inside `PackingSessions.tsx` and `Shipments.tsx`.

---

### `GAP-020`: E-Commerce Marketplace Webhook & Inventory Push Sync Lacks Dedicated Channel Health, Retry & Dead-Letter Management UI
- **Category:** `INTEGRATION_RELIABILITY_GAP`
- **Priority:** `P1`
- **Current ERP_KHO State (File Evidence):**
  - Backend includes [`IntegrationOutboxMessage`](file:///d:/ERP_KHO/ERP.Domain/Entities/IntegrationMessaging.cs) and [`IntegrationInboxMessage`](file:///d:/ERP_KHO/ERP.Domain/Entities/IntegrationMessaging.cs) (`ErpKhoDbContext.cs:210-211`) plus [`MarketplaceShop.cs:27-43`](file:///d:/ERP_KHO/ERP.Domain/Entities/MarketplaceShop.cs#L27-L43) (`CalculateChannelPushQty` with `ReserveRatioPct`, `SafetyBufferQty`, `LowStockThreshold`, `TokenExpiresAtUtc`).
  - However, the frontend only shows a summary widget inside [`Dashboard.tsx`](file:///d:/ERP_KHO/frontend/src/pages/Dashboard.tsx) and SKU mapping in [`Products.tsx`](file:///d:/ERP_KHO/frontend/src/pages/Products.tsx) Tab 3; there is no UI to monitor expiring Shopee/TikTok OAuth tokens (`TokenExpiresAtUtc`), inspect failed Outbox/Inbox webhook messages, or manually retry dead-lettered order/stock sync events (`CON-004`).
- **Benchmark Reference:**
  - **Logiwa IO (`COM-11`)** & **Fleetbase (`OSS-10`)**: Dedicated Integration Sync Monitor showing channel OAuth token health, last stock-push timestamp per SKU, failed webhook payloads, and 1-click replay.
- **Business Impact on ICP:**
  - Shopee and TikTok Shop OAuth tokens expire periodically, and marketplace APIs frequently return `429 Too Many Requests` during Mega Campaign hours (00:00–02:00). If a stock push fails silently without a visible Outbox/Dead-Letter retry queue, the seller oversells out-of-stock items and suffers marketplace penalty points ("Sao quả tạ").
- **Recommended TO-BE Solution:**
  - Build an **Integration & Sync Monitor** panel (in the new Integrations Hub or Dashboard) displaying: (1) Connected `MarketplaceShops` with OAuth expiration countdown & `SafetyBufferQty` config, and (2) `IntegrationOutboxMessages` / `IntegrationInboxMessages` status counts with a **"Thử lại (Retry Dead-Letter)"** action.

---

### `GAP-021`: `CustomerReturn` (`QUARANTINED | INSPECTED | COMPLETED`) and `KitWorkOrder` (`Draft`) Use Raw Unvalidated Strings Instead of Typed Domain State Enums & Transition Guards
- **Category:** `MISSING_STATE_OR_TRANSITION`
- **Priority:** `P2`
- **Current ERP_KHO State (File Evidence):**
  - While 22 older domain entities use strongly typed enums (`ReceiptStatus`, `ShipmentStatus`, `StocktakeStatus`, etc.), newer entities [`CustomerReturn.cs:16`](file:///d:/ERP_KHO/ERP.Domain/Entities/CustomerReturnEntities.cs#L16) (`public string Status { get; set; } = "QUARANTINED";`), [`CustomerReturnLine.cs:34-35`](file:///d:/ERP_KHO/ERP.Domain/Entities/CustomerReturnEntities.cs#L34-L35) (`Condition`, `Disposition`), and [`KitWorkOrder.cs:19`](file:///d:/ERP_KHO/ERP.Domain/Entities/KitWorkOrder.cs#L19) (`public string Status { get; set; } = "Draft";`) use raw mutable `string` properties without domain state-machine transition methods or `[ConcurrencyCheck]` / `RowVersion` on `CustomerReturn`.
- **Benchmark Reference:**
  - **`rules-architecture.md` (`[ARCH-004]`)**, **`rules-api-concurrency.md` (`[CON-002]`)**, and **`rules-database.md` (`[DB-002]`)**: All business documents must enforce valid state transitions in Domain methods and include `RowVersion` optimistic concurrency tokens.
- **Business Impact on ICP:**
  - Raw strings allow invalid state assignments or double-completion race conditions on `CustomerReturn` (which lacks `RowVersion` in [`CustomerReturnEntities.cs:5-22`](file:///d:/ERP_KHO/ERP.Domain/Entities/CustomerReturnEntities.cs#L5-L22)), risking duplicate restock postings for the same returned parcel.
- **Recommended TO-BE Solution:**
  - Introduce strongly-typed `CustomerReturnStatus`, `ReturnItemCondition`, `ReturnDisposition`, and `KitWorkOrderStatus` domain state machines with explicit transition guards, and ensure `CustomerReturn` includes `RowVersion` (`IsRowVersion()`).

---

### `GAP-022`: Failed Delivery / Carrier Return-to-Sender (`ShipmentStatus.ReturnToWarehouse`) Does Not Automatically Spawn a Linked `CustomerReturn` / Quarantine Putaway Task
- **Category:** `MISSING_EXCEPTION_FLOW`
- **Priority:** `P1`
- **Current ERP_KHO State (File Evidence):**
  - [`ShipmentStatus.cs:14-15`](file:///d:/ERP_KHO/ERP.Domain/Enums/ShipmentStatus.cs#L14-L15) supports `DeliveryFailed = 8` and `ReturnToWarehouse = 10` in [`Shipments.tsx`](file:///d:/ERP_KHO/frontend/src/pages/Shipments.tsx).
  - However, transitioning a `Shipment` to `ReturnToWarehouse` does not automatically create or link a pending [`CustomerReturn`](file:///d:/ERP_KHO/ERP.Domain/Entities/CustomerReturnEntities.cs#L5-L22) (RMA) record for the inbound receiving dock to scan when the courier brings the undelivered parcel back.
- **Benchmark Reference:**
  - **Odoo (`COM-09`)**, **Logiwa IO (`COM-11`)**, and **Fleetbase (`OSS-10`)**: When a carrier webhook or dispatcher marks a shipment as `Failed Delivery / Returning to Sender (RTS)`, the system automatically generates an expected Inbound Return (RMA) document linked to the original `ShipmentId`, `ExportReceiptId`, and `TrackingNumber`.
- **Business Impact on ICP:**
  - In Vietnam, GHN/GHTK couriers return bags of 20–50 undelivered COD parcels ("hàng hoàn") at the end of the week. If `ReturnToWarehouse` shipments do not automatically populate an expected RMA receiving queue by `TrackingNumber`, parcels sit uncounted in corners or get stolen without reconciliation.
- **Recommended TO-BE Solution:**
  - When a `Shipment` transitions to `ReturnToWarehouse`, automatically stage a `CustomerReturn` in `QUARANTINED` (or `EXPECTED_RTS`) status pre-populated with the shipment's `TrackingNumber`, `SalesOrderId`, `ExportReceiptId`, and line items so the receiving operator only has to scan the AWB barcode upon physical arrival.

---

### `GAP-023`: HTTP `GET` Verb Bound to State-Mutating Action `AutoBreakBulkReplenish` in `InventoryMovementsController.cs:107`
- **Category:** `MISSING_VALIDATION_OR_GUARD`
- **Priority:** `P1`
- **Current ERP_KHO State (File Evidence):**
  - [`InventoryMovementsController.cs:107-111`](file:///d:/ERP_KHO/ERP.Api/Controllers/InventoryMovementsController.cs#L107-L111) decorates the same `AutoBreakBulkReplenish` action with BOTH `[HttpGet("auto-break-bulk-replenish")]` and `[HttpPost("auto-break-bulk-replenish")]`!
- **Benchmark Reference:**
  - **REST RFC 9110 Safety Invariant**, **`PROJECT_RULES.md` Layer 3**, and **`[API-001]` / `[CON-001]`**: `GET` requests must be strictly read-only (`AsNoTracking()`, zero side effects). Binding `[HttpGet]` to an action that calls `AutoBreakBulkReplenishAsync` violates HTTP safety and bypasses `POST` idempotency assumptions.
- **Business Impact on ICP:**
  - Browser pre-fetching, monitoring crawlers, or accidental GET requests can trigger unintended break-bulk state evaluations or bypass mutation guards.
- **Recommended TO-BE Solution:**
  - Remove `[HttpGet("auto-break-bulk-replenish")]` from `InventoryMovementsController.cs:107` (and consolidate the endpoint with `InventoryControlController` per `GAP-006`).

---

### `GAP-024`: Missing Operator Pick/Pack Productivity & SLA Floor Leaderboard ("Hero Board") and Return Rate by SKU/Channel Report
- **Category:** `REPORTING_GAP`
- **Priority:** `P2`
- **Current ERP_KHO State (File Evidence):**
  - [`Dashboard.tsx`](file:///d:/ERP_KHO/frontend/src/pages/Dashboard.tsx) and [`Inventory.tsx`](file:///d:/ERP_KHO/frontend/src/pages/Inventory.tsx) Tab 6 (`Phân tích vận hành`) provide strong inventory valuation, expiry aging, SKU velocity, and multi-warehouse health metrics.
  - However, there is no real-time **Warehouse Floor Productivity Leaderboard** (picks/hour, packs/hour, error/short-pick rate per operator `UserId`, carrier cutoff countdown timer) and no **Return/RMA Analytics Report** (return rate % by SKU, channel, and return reason `DEFECTIVE` vs `DAMAGED_BOX`).
- **Benchmark Reference:**
  - **ShipHero (`COM-12` — "Hero Board")** & **Körber WMS (`COM-07` — Gamification)**: Real-time TV/tablet leaderboard showing top pickers/packers and remaining parcels before the 18:00 Shopee/TikTok carrier pickup cutoff.
  - **Logiwa IO (`COM-11`)**: Return reason analytics identifying SKUs with high packaging damage or sizing defects.
- **Business Impact on ICP:**
  - For a 5–30 person Vietnamese warehouse, engineered labor standards (SAP EWM style) are overkill, but a simple "Hero Board" showing how many lines/orders each staff member picked and packed today (and how many orders remain before the 17:00 GHN/SPX pickup truck arrives) drives immediate 15–25% throughput gains.
- **Recommended TO-BE Solution:**
  - Add a **"Năng suất nhân sự & SLA cắt ca (Floor Hero Board)"** card/tab in `Dashboard.tsx` / `PickingTasks.tsx` aggregating completed `PutawayTasks`, `PickingTaskLines`, and `PackingSessions` by `UserId` for the active shift, plus a Return Reason breakdown in `CustomerReturns`.

---

### `GAP-025`: Monolithic Frontend Page Bundles (>100KB JSX + 1,168-Line Scanner Bar) and Absence of React Error Boundaries
- **Category:** `PERFORMANCE_OR_SCALABILITY_RISK`
- **Priority:** `P2`
- **Current ERP_KHO State (File Evidence):**
  - [`FRONTEND_AUDIT.md:802,808-809`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/FRONTEND_AUDIT.md#L802) confirms:
    1. No React `<ErrorBoundary>` exists in [`AppRoutes.tsx`](file:///d:/ERP_KHO/frontend/src/routes/AppRoutes.tsx) or [`MainLayout.tsx`](file:///d:/ERP_KHO/frontend/src/layouts/MainLayout.tsx); an uncaught render error in any single widget crashes the entire SPA to a white screen.
    2. [`MobileBarcodeScannerBar.tsx`](file:///d:/ERP_KHO/frontend/src/components/MobileBarcodeScannerBar.tsx) is 1,168 lines (44KB) and is imported eagerly by multiple pages (`ImportReceipts`, `PutawayTasks`, `PickingTasks`, `InventoryMovements`), pulling camera decoding and wizard state into every bundle.
- **Benchmark Reference:**
  - **`vite-react-best-practices` & `vercel-react-best-practices` skills (`Role 7`)**: Route-level and modal-level `<ErrorBoundary>` with correlation-ID recovery UI, plus lazy-loading heavy camera/canvas decoder modules only when the user clicks "Bật Camera".
- **Business Impact on ICP:**
  - On entry-level Android PDAs with 2–3GB RAM over warehouse Wi-Fi, loading 128KB page components without an `<ErrorBoundary>` risks white-screen crashes during busy shift hours.
- **Recommended TO-BE Solution:**
  - Wrap `<Outlet />` in `MainLayout.tsx` with a resilient `<WarehouseErrorBoundary>` (showing the Vietnamese error screen + 1-click Zalo Diagnostics copy + "Tải lại trang" button) and lazy-load `cameraBarcodeDecoder` only when camera scanning is activated.

---

### `GAP-026`: Controller Unit Tests Relying on Optional Null Service Injection (`_receiptOps = null`) Bypass Real Application/Domain Execution Paths
- **Category:** `TEST_COVERAGE_GAP`
- **Priority:** `P1`
- **Current ERP_KHO State (File Evidence):**
  - Several controllers ([`ImportReceiptsController.cs:161,186,201`](file:///d:/ERP_KHO/ERP.Api/Controllers/ImportReceiptsController.cs#L161), [`PickingTasksController.cs:125,161,174,194,220`](file:///d:/ERP_KHO/ERP.Api/Controllers/PickingTasksController.cs#L125), [`InventoryControlController.cs:87,136,156`](file:///d:/ERP_KHO/ERP.Api/Controllers/InventoryControlController.cs#L87)) accept optional operations services (`IImportReceiptOperationsService? receiptOps = null`, `IPickingTaskOperationsService? pickingOps = null`) and execute synthetic fallback branches when those services are null.
- **Benchmark Reference:**
  - **`SKILL_ROUTING_PROTOCOL.md` Role 8 Rule & `[TST-001]` / `[TST-002]`:** *"Cấm fake `_db == null` hay fallback `.First()` user; vượt đủ 5 Gates mới báo cáo xong."* Every API/Command test must exercise the real Application/Domain service against a real database or properly wired test harness.
- **Business Impact on ICP:**
  - Tests that pass by hitting the `if (_receiptOps == null)` synthetic branch provide false confidence (`Anti-Fake Test TST-002`) while leaving actual SQL queries, lock ordering, and ledger postings untested in those specific test cases.
- **Recommended TO-BE Solution:**
  - Remove optional `= null` service fallbacks from controllers (paired with `GAP-010`) and ensure all `ERP.Api.Tests` and `ERP.SqlIntegrationHarness` tests resolve real `IImportReceiptOperationsService`, `IPickingTaskOperationsService`, and `IInventoryControlOperationsService` instances backed by `ErpKhoDbContext`.

---

## 3. Prioritized Remediation Roadmap Summary

| Phase | Target Timeframe | Gap IDs Included | Primary Deliverable Outcome |
|-------|------------------|------------------|-----------------------------|
| **Wave 1: P0 Security, Data Integrity & Core ICP Blockers** | Immediate (Sprint 1) | `GAP-001`, `GAP-008`, `GAP-009`, `GAP-010`, `GAP-011` | 1. Unified `AppPermissions` registry across `ERP.Domain` & `ERP.Api`<br>2. Strict write-permission guards on all POST mutations (zero `*.read` / `IsInRole(Viewer)` shortcuts)<br>3. Elimination of all null-service fabricated `200 OK` controller fallbacks<br>4. Dedicated **Customer Returns (RMA) & Disposition** UI screen<br>5. Dedicated full-screen **Handheld PDA / Scanner Mode** for warehouse floor operators |
| **Wave 2: P1 Workflow Cohesion, Scanner Ergonomics & Navigation Cleanup** | Near-Term (Sprint 2) | `GAP-002`, `GAP-003`, `GAP-006`, `GAP-007`, `GAP-012`, `GAP-013`, `GAP-015`, `GAP-016`, `GAP-018`, `GAP-019`, `GAP-020`, `GAP-022`, `GAP-023`, `GAP-026` | 1. Full GS1-128 `\x1d` (`FNC1`) scanner support + error buzz/vibration + scan undo<br>2. 1-Click **Express Receive & Putaway** mode for SMB sellers + streamlined Outbound flow<br>3. Sidebar consolidation (36+ items → 12–15 high-cohesion hubs) + God-component decomposition (`<500` lines)<br>4. Clean controller ownership (`CustomerReturnsController`, `KitWorkOrders`, `Integrations`) & removal of duplicate/GET replenishment routes<br>5. Automatic `ReturnToWarehouse` → `CustomerReturn` RMA creation & Integrations Sync Monitor |
| **Wave 3: P2 Domain Hygiene, Route Standardization & Floor Analytics** | Medium-Term (Sprint 3) | `GAP-004`, `GAP-005`, `GAP-017`, `GAP-021`, `GAP-024`, `GAP-025` | 1. Strongly-typed enums & `RowVersion` on `CustomerReturn` and `KitWorkOrder`<br>2. 100% `/api/v1/<kebab-case-plural>` route parity<br>3. Warehouse Floor **Hero Board** SLA leaderboard & Return Rate analytics<br>4. `<WarehouseErrorBoundary>` + lazy-loaded camera decoder |
| **Deferred / Frozen Scope** | Post-ICP Scale | `GAP-014` | Freeze `DockYard` expansion until onboarding multi-dock enterprise 3PL clients |
