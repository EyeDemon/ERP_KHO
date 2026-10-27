# SOURCE IMPLEMENTATION MAP — WMS-MASTER-AUDIT (`2026-10-27_WMS-MASTER-AUDIT-1630`)

- **Run ID:** `2026-10-27_WMS-MASTER-AUDIT-1630`
- **Audit Mode:** `READ-ONLY EVIDENCE AUDIT` (Zero Product Code / DB / Notion Mutation)
- **Auditor Role:** `Subagent Audit-A — Source Architecture, SQL Database Integrity & Test Evidence Auditor`
- **Inspected Git Commit (`HEAD`):** `d9b24530ba5826ae0d5f4f107e8f91be3f6f6009` (`feat/WMS-61-project-development`)
- **Compared Historical Baseline Commit:** `3ddab7261d4606f094239d4933923aea657aca28`
- **Database Inspected (Read-Only):** `ERP_KHO` on `(localdb)\MSSQLLocalDB` (Windows Integrated Auth `HEAVENLY\van40`)

---

## 1. Git Baseline Reconciliation (`3ddab72` vs `d9b2453`)

### 1.1 Commit Lineage (`FACT`)
Verified via `git log -n 5 --oneline` and `git show --stat`:

| Commit SHA | Author & Timestamp | Commit Message | Relationship |
|---|---|---|---|
| `d9b24530ba5826ae0d5f4f107e8f91be3f6f6009` | `van40` (`2026-10-26`) | `feat(wms): complete full UI/UX overhaul, webcam 1D barcode decoder, and E2E audit WMS-AUDIT-20261026-1805` | Current `HEAD` on `feat/WMS-61-project-development` |
| `3ddab7261d4606f094239d4933923aea657aca28` | `van40` (`2026-10-25`) | `feat(wave60): extract ISaasBillingService and IDataImportService and complete final production readiness closure` | Parent commit (`HEAD~1`) referenced as `HEAD` inside `2026-10-26_WMS-AUDIT-20261026-1805` reports |
| `92f1e15` | `van40` | `feat(wave57-59): complete real ClosedXML Excel/CSV import-export, full dark mode & responsive UI, and E2E verification` | `HEAD~2` |
| `128095b` | `van40` | `feat(wave54-56): complete v3 schema precision, 3-tier stocktake approval, and clean architecture service extraction` | `HEAD~3` |
| `de43c0b` | `van40` | `feat(wms): complete Wave 52-53 commercial optimization, dark mode, barcode labels, and audit readiness` | `HEAD~4` |

### 1.2 Why `2026-10-26_WMS-AUDIT-20261026-1805` Recorded `3ddab72` (`FACT`)
- **Root Cause (`FACT`):** During the execution of audit run `2026-10-26_WMS-AUDIT-20261026-1805`, `HEAD` was `3ddab7261d4606f094239d4933923aea657aca28`. At the conclusion of that session, all UI/UX overhaul changes, the pure-TypeScript 1D/2D camera barcode decoder, 2 backend controller/filter fixes, and the 65 audit report/screenshot artifacts in `audit_reports/2026-10-26_WMS-AUDIT-20261026-1805/` were committed together as commit `d9b24530ba5826ae0d5f4f107e8f91be3f6f6009`.
- **Exact Diff Summary (`git diff --stat 3ddab72..d9b2453`):** `153 files changed, 17659 insertions(+), 3269 deletions(-)`:
  1. **Backend C# Files Changed (`2` files):**
     - [ReportsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ReportsController.cs) (`24` lines changed: added alias route `[HttpGet("~/api/v1/reports/movements")]` and `[HttpGet("movements")]` handling).
     - [IdempotentCommandFilter.cs](file:///d:/ERP_KHO/ERP.Api/Infrastructure/IdempotentCommandFilter.cs) (`30` lines changed: added `IsOptionalHeaderScope(commandScope)` helper at `L55-L59, L273-L296` allowing selected UI command scopes to proceed when `Idempotency-Key` is omitted while keeping `Idempotency-Key` mandatory on core posting scopes).
  2. **Frontend Application & Test Files Changed (`69` files under `frontend/`):**
     - Added [cameraBarcodeDecoder.ts](file:///d:/ERP_KHO/frontend/src/utils/cameraBarcodeDecoder.ts) (`851` lines: pure-TS multi-scanline 1D `EAN-13`, `EAN-8`, `UPC-A`, `Code-128` decoder + native `BarcodeDetector` integration).
     - Expanded [MobileBarcodeScannerBar.tsx](file:///d:/ERP_KHO/frontend/src/components/MobileBarcodeScannerBar.tsx) (`+958 / -44` lines, total `1,168` lines: live WebRTC camera modal, retail barcode recognition via `isRetailProductBarcode`, Web Audio API 880Hz beep, GS1-128 parser).
     - Added/expanded unit tests: [PartnerDebtsAndScanner.test.tsx](file:///d:/ERP_KHO/frontend/src/pages/PartnerDebtsAndScanner.test.tsx) (`+252` lines), [Units.test.tsx](file:///d:/ERP_KHO/frontend/src/pages/Units.test.tsx) (`70` lines), [WarehouseCalendar.test.tsx](file:///d:/ERP_KHO/frontend/src/pages/WarehouseCalendar.test.tsx) (`67` lines), [Warehouses.test.tsx](file:///d:/ERP_KHO/frontend/src/pages/Warehouses.test.tsx) (`72` lines).
     - Updated styling/UI across `63` page, layout, and CSS files in `frontend/src/`.
  3. **Audit Reports, Design Docs & Screenshots (`82` files):**
     - `65` files under `audit_reports/2026-10-26_WMS-AUDIT-20261026-1805/` (`10` Markdown reports, `4` evidence text snapshots, `51` PNG screenshots).
     - `11` files under `docs/ui-ux-2026/` (`9` SVG diagrams, `README.md`, `UI_UX_2026_MASTER_SPEC.md`).
     - `6` root report/metric files (`NOTION_Q4_2026_SPEC_UPDATE_REPORT.md`, `UI_UX_DESIGN_AND_CHROME_INTERACTIVE_AUDIT.md`, `UX_UI_COMMERCIAL_SAAS_AUDIT_2026.md`, `VISUAL_UI_AUDIT_REPORT_2026.md`, `ux_audit_metrics.json`, `ux_audit_summary.csv`).

---

## 2. Clean Architecture & Project Dependency Map

### 2.1 Solution Structure ([ERP.slnx](file:///d:/ERP_KHO/ERP.slnx)) (`FACT`)
Note: Projects reside directly under the repository root `d:\ERP_KHO\` (and `d:\ERP_KHO\tools\`), not inside a `src/` or `tests/` directory.

| Project | `.csproj` Path | TargetFramework | Project References | NuGet / Framework Dependencies |
|---|---|---|---|---|
| `ERP.Domain` | [ERP.Domain.csproj](file:///d:/ERP_KHO/ERP.Domain/ERP.Domain.csproj) | `net10.0` | **None (`0`)** | **None (`0`)** — 100% pure .NET 10 BCL |
| `ERP.Application` | [ERP.Application.csproj](file:///d:/ERP_KHO/ERP.Application/ERP.Application.csproj) | `net10.0` | `ERP.Domain` | `FluentValidation 11.11.0` |
| `ERP.Infrastructure` | [ERP.Infrastructure.csproj](file:///d:/ERP_KHO/ERP.Infrastructure/ERP.Infrastructure.csproj) | `net10.0` | `ERP.Application` | `Dapper 2.1.79`, `Microsoft.EntityFrameworkCore.SqlServer 10.0.9`, `Microsoft.EntityFrameworkCore.Design 10.0.9`, `FrameworkReference: Microsoft.AspNetCore.App` |
| `ERP.Api` | [ERP.Api.csproj](file:///d:/ERP_KHO/ERP.Api/ERP.Api.csproj) | `net10.0` | `ERP.Application`, `ERP.Infrastructure` | `ClosedXML 0.105.0`, `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.5`, `Microsoft.AspNetCore.OpenApi 10.0.5`, `Microsoft.EntityFrameworkCore.Design 10.0.9`, `Microsoft.EntityFrameworkCore.SqlServer 10.0.9`, `Swashbuckle.AspNetCore 10.1.7` |
| `ERP.QaSeed` | [ERP.QaSeed.csproj](file:///d:/ERP_KHO/tools/ERP.QaSeed/ERP.QaSeed.csproj) | `net10.0` | `ERP.Application`, `ERP.Domain`, `ERP.Infrastructure` | Transitive via referenced projects |
| `ERP.SqlIntegrationHarness` | [ERP.SqlIntegrationHarness.csproj](file:///d:/ERP_KHO/tools/ERP.SqlIntegrationHarness/ERP.SqlIntegrationHarness.csproj) | `net10.0` | `ERP.Application.Tests`, `ERP.Api.Tests` | `xunit.abstractions 2.0.3` |
| `ERP.Application.Tests` | [ERP.Application.Tests.csproj](file:///d:/ERP_KHO/ERP.Application.Tests/ERP.Application.Tests.csproj) | `net10.0` | `ERP.Application`, `ERP.Domain`, `ERP.Infrastructure`, `ERP.QaSeed` | `xunit 2.9.3`, `Moq 4.20.72`, `FluentAssertions 8.9.0`, `Microsoft.EntityFrameworkCore.InMemory 10.0.5`, `coverlet.collector 6.0.4` |
| `ERP.Api.Tests` | [ERP.Api.Tests.csproj](file:///d:/ERP_KHO/ERP.Api.Tests/ERP.Api.Tests.csproj) | `net10.0` | `ERP.Api`, `ERP.Application`, `ERP.Domain`, `ERP.Infrastructure` | `xunit 2.9.3`, `Moq 4.20.72`, `FluentAssertions 8.9.0`, `Microsoft.AspNetCore.Mvc.Testing 10.0.5`, `Microsoft.EntityFrameworkCore.InMemory 10.0.5` |

### 2.2 Clean Architecture Rule Compliance (`[ARCH-001]` & `[ARCH-003]`) (`FACT`)
- **`[ARCH-001]` (One-Way Project Dependency):** **PASS (`100%`)**. `ERP.Domain` has `0` project or NuGet dependencies. `ERP.Application` depends only on `ERP.Domain` and `FluentValidation`.
- **`[ARCH-003]` (No `DbContext` or Business `if/else` in Controllers):** **PASS (`100%` — `F-004` Remediated & Closed in Wave 58)**.
  - **42 / 42 Controllers Free of `ErpKhoDbContext` (`PASS`):**
    - Verified via `@(Select-String -Path "ERP.Api/Controllers/*.cs" -Pattern "\bErpKhoDbContext\b").Count == 0`: **zero** controllers in `ERP.Api/Controllers/` inject or reference `ErpKhoDbContext`.
    - Prior to Wave 58, only `6 / 42` controllers (`BackordersController.cs`, `ImportTemplatesController.cs`, `InventoryStocksController.cs`, `InventoryTransactionsController.cs`, `PartnerDebtsController.cs`, `ProductBarcodesController.cs`) were decoupled from `ErpKhoDbContext`, while `36 / 42` controllers referenced `ErpKhoDbContext`.
    - In Wave 58, all direct `ErpKhoDbContext` queries and business logic across the remaining `36` controllers were extracted into dedicated Application/Infrastructure Services (`IWmsGovernanceOperationsService` / `WmsGovernanceOperationsService`, `IInventoryControlOperationsService` / `InventoryControlOperationsService`, `IShipmentOperationsService` / `ShipmentOperationsService`, `IPermissionOperationsService` / `PermissionOperationsService`, `IImportReceiptOperationsService` / `ImportReceiptOperationsService`, `IPickingTaskOperationsService` / `PickingTaskOperationsService`, `IStocktakeOperationsService` / `StocktakeOperationsService`, `IPackingSessionOperationsService` / `PackingSessionOperationsService`, `IProductOperationsService` / `ProductOperationsService`, `IExportReceiptOperationsService` / `ExportReceiptOperationsService`, `ISalesOrderOperationsService` / `SalesOrderOperationsService`, `IReportOperationsService` / `ReportOperationsService`, plus extended `IInventoryMovementService` and `IUserSessionService`), reducing all `42` controllers to Thin Controllers (`< 400` lines each).

---

## 3. Domain Entities & Canonical Alias Map (`OD-03`)

All **92** Domain entity classes defined across the **58** `.cs` files in `ERP.Domain/Entities/*.cs` map 1-to-1 to **92** `DbSet<T>` properties in [ErpKhoDbContext.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/ErpKhoDbContext.cs#L141-L232) and **92** physical user tables in SQL Server database `ERP_KHO` (plus `__EFMigrationsHistory` = **93** physical tables total). In Wave 57 (`20261027130209_Wave57AuditRemediationSchemaAndImmutableLedgerTrigger.cs`), `PickingWaves`, `PutToWallSlots`, `ThreePlStorageSnapshots`, and `ThreePlBillingStatements` were promoted to first-class relational tables (`decimal(19,4)`), eliminating all `AuditLogs.NewValues` JSON workarounds (`F-003a/b/c` closed).

| # | `ErpKhoDbContext` `DbSet<T>` (`Line`) | Domain Entity Class | Source File (`ERP.Domain/Entities/`) | Canonical Notion Spec Alias (`OD-03`) |
|---:|---|---|---|---|
| 1 | `Roles` (`L141`) | `Role` | [Role.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/Role.cs) | `roles` |
| 2 | `Permissions` (`L142`) | `Permission` | [Permission.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/Permission.cs) | `permissions` |
| 3 | `RolePermissions` (`L143`) | `RolePermission` | [RolePermission.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/RolePermission.cs) | `role_permissions` |
| 4 | `Users` (`L144`) | `User` | [User.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/User.cs) | `users` |
| 5 | `Units` (`L145`) | `Unit` | [Unit.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/Unit.cs) | `uoms` / `units_of_measure` |
| 6 | `Products` (`L146`) | `Product` | [Product.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/Product.cs) | `skus` / `products` |
| 7 | `ProductUoms` (`L147`) | `ProductUom` | [ProductUom.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/ProductUom.cs) | `uom_conversions` |
| 8 | `QcPolicies` (`L148`) | `QcPolicy` | [QcPolicy.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/QcPolicy.cs) | `qc_policies` |
| 9 | `ProductCategories` (`L149`) | `ProductCategory` | [ProductCategory.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/ProductCategory.cs) | `categories` |
| 10 | `ProductBarcodes` (`L150`) | `ProductBarcode` | [ProductBarcode.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/ProductBarcode.cs) | `product_barcodes` |
| 11 | `BusinessPartners` (`L151`) | `BusinessPartner` | [BusinessPartner.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/BusinessPartner.cs) | `business_partners` (`suppliers` / `customers` / `owners`) |
| 12 | `PartnerDebtInvoices` (`L152`) | `PartnerDebtInvoice` | [PartnerDebt.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/PartnerDebt.cs) | `partner_debt_invoices` |
| 13 | `PartnerDebtLedgers` (`L153`) | `PartnerDebtLedger` | [PartnerDebt.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/PartnerDebt.cs) | `partner_debt_ledgers` |
| 14 | `Warehouses` (`L154`) | `Warehouse` | [Warehouse.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/Warehouse.cs) | `warehouses` |
| 15 | `WarehouseCalendars` (`L155`) | `WarehouseCalendar` | [WarehouseCalendar.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/WarehouseCalendar.cs) | `warehouse_calendars` |
| 16 | `WarehouseCalendarDays` (`L156`) | `WarehouseCalendarDay` | [WarehouseCalendar.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/WarehouseCalendar.cs) | `warehouse_calendar_days` |
| 17 | `WarehouseShifts` (`L157`) | `WarehouseShift` | [WarehouseCalendar.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/WarehouseCalendar.cs) | `warehouse_shifts` |
| 18 | `Docks` (`L158`) | `Dock` | [DockYard.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/DockYard.cs) | `dock_doors` |
| 19 | `YardSlots` (`L159`) | `YardSlot` | [DockYard.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/DockYard.cs) | `yard_slots` |
| 20 | `DockAppointments` (`L160`) | `DockAppointment` | [DockYard.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/DockYard.cs) | `dock_appointments` |
| 21 | `DockAppointmentEvents` (`L161`) | `DockAppointmentEvent` | [DockYard.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/DockYard.cs) | `dock_appointment_events` |
| 22 | `PurchaseOrders` (`L162`) | `PurchaseOrder` | [InboundPlanning.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/InboundPlanning.cs) | `purchase_orders` |
| 23 | `PurchaseOrderLines` (`L163`) | `PurchaseOrderLine` | [InboundPlanning.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/InboundPlanning.cs) | `purchase_order_lines` |
| 24 | `Asns` (`L164`) | `Asn` | [InboundPlanning.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/InboundPlanning.cs) | `advance_shipping_notices` (`asns`) |
| 25 | `AsnLines` (`L165`) | `AsnLine` | [InboundPlanning.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/InboundPlanning.cs) | `asn_lines` |
| 26 | `InventoryStocks` (`L166`) | `InventoryStock` | [InventoryStock.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/InventoryStock.cs) | `inventory_balances` (9D Grain + `GrainHash`) |
| 27 | `InventoryTransactions` (`L167`) | `InventoryTransaction` | [InventoryTransaction.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/InventoryTransaction.cs) | `inventory_ledger` (Append-Only) |
| 28 | `InventoryStatusDefinitions` (`L168`) | `InventoryStatusDefinition` | [InventoryDimensions.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/InventoryDimensions.cs) | `inventory_status_definitions` |
| 29 | `InventoryLots` (`L169`) | `InventoryLot` | [InventoryDimensions.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/InventoryDimensions.cs) | `inventory_lots` (`lots`) |
| 30 | `InventorySerials` (`L170`) | `InventorySerial` | [InventoryDimensions.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/InventoryDimensions.cs) | `inventory_serials` (`serial_numbers`) |
| 31 | `InventoryLocks` (`L171`) | `InventoryLock` | [InventoryLock.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/InventoryLock.cs) | `inventory_locks` |
| 32 | `ImportReceiptInventoryIdentities` (`L172`) | `ImportReceiptInventoryIdentity` | [InventoryDimensions.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/InventoryDimensions.cs) | `import_receipt_inventory_identities` |
| 33 | `ImportReceipts` (`L173`) | `ImportReceipt` | [ImportReceipt.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/ImportReceipt.cs) | `goods_receipts` (`import_receipts`) |
| 34 | `ImportReceiptDetails` (`L174`) | `ImportReceiptDetail` | [ImportReceiptDetail.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/ImportReceiptDetail.cs) | `goods_receipt_lines` |
| 35 | `ExportReceipts` (`L175`) | `ExportReceipt` | [ExportReceipt.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/ExportReceipt.cs) | `outbound_shipments` / `export_receipts` (MVP) |
| 36 | `ExportReceiptDetails` (`L176`) | `ExportReceiptDetail` | [ExportReceiptDetail.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/ExportReceiptDetail.cs) | `export_receipt_lines` |
| 37 | `Stocktakes` (`L177`) | `Stocktake` | [Stocktake.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/Stocktake.cs) | `cycle_counts` (`stocktakes`) |
| 38 | `StocktakeDetails` (`L178`) | `StocktakeDetail` | [StocktakeDetail.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/StocktakeDetail.cs) | `cycle_count_lines` |
| 39 | `AuditLogs` (`L179`) | `AuditLog` | [AuditLog.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/AuditLog.cs) | `audit_logs` |
| 40 | `UserWarehouses` (`L180`) | `UserWarehouse` | [UserWarehouse.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/UserWarehouse.cs) | `user_warehouse_scopes` |
| 41 | `UserSessions` (`L181`) | `UserSession` | [UserSession.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/UserSession.cs) | `user_sessions` |
| 42 | `StockTransfers` (`L182`) | `StockTransfer` | [StockTransfer.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/StockTransfer.cs) | `stock_transfers` |
| 43 | `StockTransferDetails` (`L183`) | `StockTransferDetail` | [StockTransferDetail.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/StockTransferDetail.cs) | `stock_transfer_lines` |
| 44 | `StockReservations` (`L184`) | `StockReservation` | [StockReservation.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/StockReservation.cs) | `inventory_reservations` (`stock_reservations`) |
| 45 | `StockAllocations` (`L185`) | `StockAllocation` | [StockAllocation.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/StockAllocation.cs) | `stock_allocations` |
| 46 | `IdempotencyRecords` (`L186`) | `IdempotencyRecord` | [IdempotencyRecord.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/IdempotencyRecord.cs) | `idempotency_records` |
| 47 | `ReceivingDiscrepancies` (`L187`) | `ReceivingDiscrepancy` | [ReceivingDiscrepancy.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/ReceivingDiscrepancy.cs) | `receiving_discrepancies` |
| 48 | `ReceivingObservationVersions` (`L188`) | `ReceivingObservationVersion` | [ReceivingDiscrepancy.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/ReceivingDiscrepancy.cs) | `receiving_observation_versions` |
| 49 | `ReceivingObservationItems` (`L189`) | `ReceivingObservationItem` | [ReceivingDiscrepancy.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/ReceivingDiscrepancy.cs) | `receiving_observation_items` |
| 50 | `ReceivingResolutionVersions` (`L190`) | `ReceivingResolutionVersion` | [ReceivingDiscrepancy.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/ReceivingDiscrepancy.cs) | `receiving_resolution_versions` |
| 51 | `ReceivingReasonCodes` (`L191`) | `ReceivingReasonCode` | [ReceivingReasonAndTolerance.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/ReceivingReasonAndTolerance.cs) | `receiving_reason_codes` |
| 52 | `ReceivingTolerancePolicies` (`L192`) | `ReceivingTolerancePolicy` | [ReceivingReasonAndTolerance.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/ReceivingReasonAndTolerance.cs) | `receiving_tolerance_policies` |
| 53 | `WarehouseLocations` (`L193`) | `WarehouseLocation` | [WarehouseLocation.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/WarehouseLocation.cs) | `warehouse_locations` (`bins`) |
| 54 | `PutawayTasks` (`L194`) | `PutawayTask` | [PutawayTask.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/PutawayTask.cs) | `putaway_tasks` |
| 55 | `PutawayTaskItems` (`L195`) | `PutawayTaskItem` | [PutawayTask.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/PutawayTask.cs) | `putaway_task_items` |
| 56 | `InventoryLocationMovements` (`L196`) | `InventoryLocationMovement` | [PutawayTask.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/PutawayTask.cs) | `inventory_location_movements` |
| 57 | `PickingTasks` (`L197`) | `PickingTask` | [PickingTask.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/PickingTask.cs) | `picking_tasks` |
| 58 | `PickingTaskLines` (`L198`) | `PickingTaskLine` | [PickingTask.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/PickingTask.cs) | `picking_task_lines` |
| 59 | `ShortPickExceptions` (`L199`) | `ShortPickException` | [PickingTask.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/PickingTask.cs) | `short_pick_exceptions` |
| 60 | `PackingSessions` (`L200`) | `PackingSession` | [Packing.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/Packing.cs) | `packing_sessions` |
| 61 | `HandlingUnits` (`L201`) | `HandlingUnit` | [Packing.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/Packing.cs) | `handling_units` (`lpns`) |
| 62 | `HandlingUnitContents` (`L202`) | `HandlingUnitContent` | [Packing.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/Packing.cs) | `handling_unit_contents` |
| 63 | `Shipments` (`L203`) | `Shipment` | [Shipment.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/Shipment.cs) | `shipments` |
| 64 | `ShipmentHandlingUnits` (`L204`) | `ShipmentHandlingUnit` | [Shipment.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/Shipment.cs) | `shipment_handling_units` |
| 65 | `ShipmentTrackingEvents` (`L205`) | `ShipmentTrackingEvent` | [ShipmentTracking.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/ShipmentTracking.cs) | `shipment_tracking_events` |
| 66 | `ShipmentProofOfDeliveries` (`L206`) | `ShipmentProofOfDelivery` | [ShipmentTracking.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/ShipmentTracking.cs) | `pod_evidences` |
| 67 | `SalesOrders` (`L207`) | `SalesOrder` | [SalesOrder.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/SalesOrder.cs) | `sales_orders` |
| 68 | `SalesOrderLines` (`L208`) | `SalesOrderLine` | [SalesOrder.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/SalesOrder.cs) | `sales_order_lines` |
| 69 | `Backorders` (`L209`) | `Backorder` | [SalesOrder.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/SalesOrder.cs) | `backorders` |
| 70 | `IntegrationOutboxMessages` (`L210`) | `IntegrationOutboxMessage` | [IntegrationMessaging.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/IntegrationMessaging.cs) | `outbox_messages` |
| 71 | `IntegrationInboxMessages` (`L211`) | `IntegrationInboxMessage` | [IntegrationMessaging.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/IntegrationMessaging.cs) | `inbox_messages` |
| 72 | `ProductBundleComponents` (`L212`) | `ProductBundleComponent` | [MarketplaceShop.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/MarketplaceShop.cs) | `product_bundle_components` (`bom_components`) |
| 73 | `MarketplaceShops` (`L213`) | `MarketplaceShop` | [MarketplaceShop.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/MarketplaceShop.cs) | `marketplace_shops` |
| 74 | `ChannelSkuMappings` (`L214`) | `ChannelSkuMapping` | [MarketplaceShop.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/MarketplaceShop.cs) | `channel_sku_mappings` |
| 75 | `HandoverManifests` (`L215`) | `HandoverManifest` | [HandoverManifest.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/HandoverManifest.cs) | `handover_manifests` |
| 76 | `HandoverManifestLines` (`L216`) | `HandoverManifestLine` | [HandoverManifest.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/HandoverManifest.cs) | `handover_manifest_lines` |
| 77 | `SaasPlans` (`L217`) | `SaasPlan` | [SaasSubscriptionEntities.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/SaasSubscriptionEntities.cs) | `saas_plans` |
| 78 | `TenantSubscriptions` (`L218`) | `TenantSubscription` | [SaasSubscriptionEntities.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/SaasSubscriptionEntities.cs) | `tenant_subscriptions` |
| 79 | `SaasBillingInvoices` (`L219`) | `SaasBillingInvoice` | [SaasSubscriptionEntities.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/SaasSubscriptionEntities.cs) | `saas_billing_invoices` |
| 80 | `ShipmentFinancialLedgers` (`L220`) | `ShipmentFinancialLedger` | [ShipmentFinancialLedger.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/ShipmentFinancialLedger.cs) | `shipment_financial_ledgers` (`cod_reconciliation`) |
| 81 | `VnAdministrativeUnits` (`L221`) | `VnAdministrativeUnit` | [VnAdministrativeEntities.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/VnAdministrativeEntities.cs) | `vn_administrative_units` |
| 82 | `CarrierLocationMappings` (`L222`) | `CarrierLocationMapping` | [VnAdministrativeEntities.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/VnAdministrativeEntities.cs) | `carrier_location_mappings` |
| 83 | `PackingVideoEvidences` (`L223`) | `PackingVideoEvidence` | [PackingVideoEvidence.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/PackingVideoEvidence.cs) | `packing_video_evidences` |
| 84 | `PromotionGiftRules` (`L224`) | `PromotionGiftRule` | [PromotionGiftRule.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/PromotionGiftRule.cs) | `promotion_gift_rules` |
| 85 | `SupportImpersonationSessions` (`L225`) | `SupportImpersonationSession` | [SupportImpersonationSession.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/SupportImpersonationSession.cs) | `support_impersonation_sessions` |
| 86 | `CustomerReturns` (`L226`) | `CustomerReturn` | [CustomerReturnEntities.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/CustomerReturnEntities.cs) | `customer_returns` (`rma_returns`) |
| 87 | `CustomerReturnLines` (`L227`) | `CustomerReturnLine` | [CustomerReturnEntities.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/CustomerReturnEntities.cs) | `customer_return_lines` |
| 88 | `StocktakeAdjustmentProposals` (`L228`) | `StocktakeAdjustmentProposal` | [StocktakeAdjustmentProposal.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/StocktakeAdjustmentProposal.cs) | `stocktake_adjustment_proposals` |
| 89 | `PickingWaves` (`L229`) | `PickingWave` | [PickingWave.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/PickingWave.cs) | `picking_waves` |
| 90 | `PutToWallSlots` (`L230`) | `PutToWallSlot` | [PutToWallSlot.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/PutToWallSlot.cs) | `put_to_wall_slots` |
| 91 | `ThreePlStorageSnapshots` (`L231`) | `ThreePlStorageSnapshot` | [ThreePlStorageSnapshot.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/ThreePlStorageSnapshot.cs) | `three_pl_storage_snapshots` |
| 92 | `ThreePlBillingStatements` (`L232`) | `ThreePlBillingStatement` | [ThreePlBillingStatement.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/ThreePlBillingStatement.cs) | `three_pl_billing_statements` |

---

## 4. Application & Infrastructure Services, Repositories, Query Services & Workers Map

### 4.1 Application Layer (`ERP.Application`) (`FACT`)
- **Interfaces (`58` files in `ERP.Application/Interfaces/`):**
  - Core Inventory & Ledger: [IInventoryLockMoveService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IInventoryLockMoveService.cs) (`43` lines), [IInventoryQueryService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IInventoryQueryService.cs) (`9` lines), [IInventoryReconciliationQueryService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IInventoryReconciliationQueryService.cs) (`25` lines), [IInventoryReversalTraceabilityService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IInventoryReversalTraceabilityService.cs) (`41` lines), [IInventoryStatusService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IInventoryStatusService.cs) (`31` lines), [IInventoryTransactionQueryService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IInventoryTransactionQueryService.cs) (`24` lines). *(Note: Repository interfaces `IRepository.cs`, `IInventoryStockRepository.cs`, `IInventoryTransactionRepository.cs`, `IImportReceiptRepository.cs`, `IExportReceiptRepository.cs`, `IStocktakeRepository.cs`, `IProductCatalogRepository.cs`, `IProductRepository.cs`, `IReportRepository.cs`, `IUnitOfWork.cs`, `IUnitRepository.cs`, `IUserRepository.cs`, `IWarehouseRepository.cs`, and `IAuditLogRepository.cs` reside in `ERP.Domain/Interfaces/`.)*
  - Inbound & Putaway: [IImportReceiptService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IImportReceiptService.cs) (`24` lines), [IInboundPlanningService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IInboundPlanningService.cs) (`26` lines), [IReceiptInboundPlanningIntegration.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IReceiptInboundPlanningIntegration.cs) (`12` lines), [IReceiptInventoryIdentityService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IReceiptInventoryIdentityService.cs) (`31` lines), [IPutawayService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IPutawayService.cs) (`30` lines), [IReceivingDiscrepancyService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IReceivingDiscrepancyService.cs) (`14` lines), [IDockYardService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IDockYardService.cs) (`31` lines).
  - Outbound, Reservation, Allocation, Picking, Packing & Shipping: [IExportReceiptService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IExportReceiptService.cs) (`16` lines), [IStockReservationService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IStockReservationService.cs) (`22` lines), [IStockAllocationService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IStockAllocationService.cs) (`15` lines), [ISalesOrderBackorderService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/ISalesOrderBackorderService.cs) (`17` lines), [IPickingService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IPickingService.cs) (`32` lines), [IPackingService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IPackingService.cs) (`73` lines), [IShipmentService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IShipmentService.cs) (`55` lines), [IHandoverManifestService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IHandoverManifestService.cs) (`63` lines).
  - Stocktake & StockTransfer: [IStocktakeService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IStocktakeService.cs) (`11` lines), [IStocktakeQueryService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IStocktakeQueryService.cs) (`12` lines), [IStockTransferService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IStockTransferService.cs) (`19` lines).
  - Master Data, Security, Multi-Tenancy, SaaS Billing & Data Import: [IAuthService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IAuthService.cs) (`10` lines), [ITokenService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/ITokenService.cs) (`12` lines), [IUserSessionService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IUserSessionService.cs) (`16` lines), [IAccessTokenSessionValidator.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IAccessTokenSessionValidator.cs) (`6` lines), [IPasswordHasherService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IPasswordHasherService.cs) (`7` lines), [IAccountAdminService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IAccountAdminService.cs) (`7` lines), [IApprovalWorkflowService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IApprovalWorkflowService.cs) (`12` lines), [ICurrentUser.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/ICurrentUser.cs) (`12` lines), [ITenantContext.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/ITenantContext.cs) (`14` lines), [IRequestMetadata.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IRequestMetadata.cs) (`8` lines), [IWarehouseAuthorizationService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IWarehouseAuthorizationService.cs) (`14` lines), [IUserWarehouseAccessService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IUserWarehouseAccessService.cs) (`10` lines), [IWarehouseService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IWarehouseService.cs) (`13` lines), [IWarehouseCalendarService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IWarehouseCalendarService.cs) (`11` lines), [IProductService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IProductService.cs) (`15` lines), [IProductCatalogService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IProductCatalogService.cs) (`19` lines), [IUnitService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IUnitService.cs) (`13` lines), [IBusinessPartnerService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IBusinessPartnerService.cs) (`14` lines), [IPartnerDebtService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IPartnerDebtService.cs) (`49` lines), [IReportService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IReportService.cs) (`10` lines), [ISaasBillingService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/ISaasBillingService.cs) (`56` lines — extracted in Wave 60), [IDataImportService.cs](file:///d:/ERP_KHO/ERP.Application/Interfaces/IDataImportService.cs) (`34` lines — extracted in Wave 60).
  - Wave 58 Clean Architecture Operations Contracts (`12` interfaces extracted to close `F-004`): `IWmsGovernanceOperationsService.cs`, `IInventoryControlOperationsService.cs`, `IShipmentOperationsService.cs`, `IPermissionOperationsService.cs`, `IImportReceiptOperationsService.cs`, `IPickingTaskOperationsService.cs`, `IStocktakeOperationsService.cs`, `IPackingSessionOperationsService.cs`, `IProductOperationsService.cs`, `IExportReceiptOperationsService.cs`, `ISalesOrderOperationsService.cs`, `IReportOperationsService.cs`.
- **Application Services (`9` files in `ERP.Application/Services/`):**
  - [AuthService.cs](file:///d:/ERP_KHO/ERP.Application/Services/AuthService.cs) (`141` lines)
  - [ExportReceiptService.cs](file:///d:/ERP_KHO/ERP.Application/Services/ExportReceiptService.cs) (`800` lines — orchestrates outbound export receipt creation, approval, dispatch, UoM conversion, and 9D lock acquisition)
  - [ImportReceiptService.cs](file:///d:/ERP_KHO/ERP.Application/Services/ImportReceiptService.cs) (`750` lines — orchestrates inbound receipt creation, QC hold/release, approval, putaway task generation, and 9D balance/ledger posting)
  - [ProductCatalogService.cs](file:///d:/ERP_KHO/ERP.Application/Services/ProductCatalogService.cs) (`113` lines)
  - [ProductService.cs](file:///d:/ERP_KHO/ERP.Application/Services/ProductService.cs) (`253` lines)
  - [ReportService.cs](file:///d:/ERP_KHO/ERP.Application/Services/ReportService.cs) (`157` lines)
  - [StocktakeService.cs](file:///d:/ERP_KHO/ERP.Application/Services/StocktakeService.cs) (`671` lines)
  - [UnitService.cs](file:///d:/ERP_KHO/ERP.Application/Services/UnitService.cs) (`111` lines)
  - [WarehouseService.cs](file:///d:/ERP_KHO/ERP.Application/Services/WarehouseService.cs) (`171` lines)

### 4.2 Infrastructure Layer (`ERP.Infrastructure`) (`FACT`)
- **Persistence ([ErpKhoDbContext.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/ErpKhoDbContext.cs), `911` lines):**
  - Multi-tenant `CurrentTenantId` resolution (`L128-L139`) and global query filters.
  - Append-only ledger immutability guard (`EnsureImmutableLedgersNotMutated` at `L234-L260` and `ApplyImmutableAuditMetadata` at `L627-L731`) blocking `EntityState.Modified` and `EntityState.Deleted` on `InventoryTransaction`, `InventoryLocationMovement`, `PartnerDebtLedger`, `ShipmentTrackingEvent`, `DockAppointmentEvent`, and `AuditLog`.
- **Repositories (`14` files in `ERP.Infrastructure/Repositories/`):**
  - [InventoryStockRepository.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Repositories/InventoryStockRepository.cs) (`610` lines):
    - Implements 2-tier deterministic 9D lock in `AcquireInventoryAllocationLocksAsync` (`L88-L163`):
      1. Canonical ordering of keys by `(TenantId, WarehouseId, ProductId, Status, LocationId, LotId, SerialId, HandlingUnitId, OwnerId)` (`L97-L115`).
      2. Exclusive transaction-scoped SQL application lock via `sp_getapplock` on resource `ERP_KHO:INV9D:{GrainHash}` (`L119-L133`).
      3. Row-level locking query `FROM [InventoryStocks] WITH (UPDLOCK, ROWLOCK, REPEATABLEREAD)` (`L135-L161`).
  - [InventoryTransactionRepository.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Repositories/InventoryTransactionRepository.cs) (`42` lines — `AddAsync` only; no `Update` or `Delete` methods, satisfying `[INV-002]`).
  - Other 12 repositories: `AuditLogRepository.cs` (`20` lines), `ExportReceiptRepository.cs` (`192` lines), `ImportReceiptRepository.cs` (`119` lines), `ProductCatalogRepository.cs` (`130` lines), `ProductRepository.cs` (`118` lines), `ReportRepository.cs` (`71` lines), `Repository.cs` (`95` lines), `StocktakeRepository.cs` (`28` lines), `UnitOfWork.cs` (`107` lines), `UnitRepository.cs` (`51` lines), `UserRepository.cs` (`158` lines), `WarehouseRepository.cs` (`143` lines).
- **Query Services (`6` files in `ERP.Infrastructure/Queries/`):**
  - [InventoryLockQueryExtensions.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Queries/InventoryLockQueryExtensions.cs) (`29` lines)
  - [InventoryQueryService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Queries/InventoryQueryService.cs) (`93` lines)
  - [InventoryReconciliationQueryService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Queries/InventoryReconciliationQueryService.cs) (`835` lines)
  - [InventoryTraceabilityQueryService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Queries/InventoryTraceabilityQueryService.cs) (`704` lines)
  - [InventoryTransactionQueryService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Queries/InventoryTransactionQueryService.cs) (`245` lines)
  - [StocktakeQueryService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Queries/StocktakeQueryService.cs) (`151` lines)
- **Infrastructure Services (`55` files in `ERP.Infrastructure/Services/`):**
  - Includes `StockReservationService.cs` (`918` lines), `StockAllocationService.cs` (`651` lines), `PickingService.cs` (`1,016` lines), `PickingTaskIntegration.cs` (`218` lines), `PackingService.cs` (`673` lines), `PackingSessionIntegration.cs` (`69` lines), `ShipmentService.cs` (`1,116` lines), `ShipmentIntegration.cs` (`95` lines), `HandoverManifestService.cs` (`789` lines), `PutawayService.cs` (`1,184` lines), `InventoryMovementService.cs` (`1,162` lines), `InventoryStatusService.cs` (`866` lines), `InventoryLockService.cs` (`412` lines), `InventoryReversalService.cs` (`497` lines), `StockTransferService.cs` + 7 partial `.cs` files (`1,011` lines across 8 files), `DockYardService.cs` (`998` lines), `SalesOrderBackorderService.cs` (`1,174` lines), `InboundPlanningService.cs` (`507` lines), `ReceiptInboundPlanningIntegration.cs` (`370` lines), `ReceiptInventoryIdentityService.cs` (`443` lines), `ReceivingDiscrepancyService.cs` (`319` lines), `ApprovalWorkflowService.cs` (`508` lines), `SaasBillingService.cs` (`384` lines), `DataImportService.cs` (`347` lines), `PartnerDebtService.cs` (`764` lines), `BusinessPartnerService.cs` (`106` lines), `WarehouseCalendarService.cs` (`594` lines), `WarehouseAuthorizationService.cs` (`162` lines), `UserWarehouseAccessService.cs` (`130` lines), `UserSessionService.cs` (`346` lines), `TokenService.cs` (`206` lines), `PasswordHasherService.cs` (`72` lines), `AccountAdminService.cs` (`59` lines), `PermissionAdministrationGuard.cs` (`52` lines), `AccountSecurityState.cs` (`47` lines), `AccessTokenSessionValidator.cs` (`23` lines), plus the 12 Wave 58 operations services (`WmsGovernanceOperationsService.cs`, `InventoryControlOperationsService.cs`, `ShipmentOperationsService.cs`, `PermissionOperationsService.cs`, `ImportReceiptOperationsService.cs`, `PickingTaskOperationsService.cs`, `StocktakeOperationsService.cs`, `PackingSessionOperationsService.cs`, `ProductOperationsService.cs`, `ExportReceiptOperationsService.cs`, `SalesOrderOperationsService.cs`, `ReportOperationsService.cs`).

### 4.3 Background Hosted Workers (`ERP.Api/Hosting/`) (`FACT`)
Registered in [Program.cs](file:///d:/ERP_KHO/ERP.Api/Program.cs#L213-L217) (`5` `BackgroundService` workers total — `F-006` closed in Wave 57):
1. [IntegrationOutboxDispatcherJob.cs](file:///d:/ERP_KHO/ERP.Api/Hosting/IntegrationOutboxDispatcherJob.cs) (`139` lines): Polls `IntegrationOutboxMessages` where `Status == "PENDING"` and dispatches/updates `RetryCount` and `PublishedAt`.
2. [BuyerPiiRetentionAnonymizerJob.cs](file:///d:/ERP_KHO/ERP.Api/Hosting/BuyerPiiRetentionAnonymizerJob.cs) (`143` lines): Periodically anonymizes expired buyer PII on completed outbound records.
3. [SoftReservationExpiryJob.cs](file:///d:/ERP_KHO/ERP.Api/Hosting/SoftReservationExpiryJob.cs) (`84` lines): Periodically sweeps expired soft reservations across active tenants via `IStockReservationService.SweepExpiredAsync`.
4. [DailyInventoryIntegrityJob.cs](file:///d:/ERP_KHO/ERP.Api/Hosting/DailyInventoryIntegrityJob.cs) (`91` lines): Periodically executes 9D ledger-to-balance integrity checks across all tenants via `IInventoryReconciliationQueryService`.
5. [ThreePlDailySnapshotJob.cs](file:///d:/ERP_KHO/ERP.Api/Hosting/ThreePlDailySnapshotJob.cs) (`145` lines): Periodically captures idempotent daily end-of-day 3PL storage occupancy snapshots into `dbo.ThreePlStorageSnapshots` per `(TenantId, WarehouseId, OwnerId, SnapshotDate)`.

---

## 5. API Controllers & Route Contract Map (`42` Controllers)

All **42** controller `.cs` files in `ERP.Api/Controllers/` are mapped below (`FACT`). Every controller supports both legacy `/api/[controller]` and canonical `/api/v1/<kebab-case-plural>` routes. Following Wave 58 remediation (`F-004` `CLOSED`), **42 / 42** (`100%`) controllers have **`0` `ErpKhoDbContext` references** and all 42 are Thin Controllers (`< 400` lines each).

| # | Controller File (`ERP.Api/Controllers/`) | Lines | `ErpKhoDbContext` Refs | Clean Architecture Status (`[ARCH-003]`) |
|---:|---|---:|---:|---|
| 1 | [AccountSecurityController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/AccountSecurityController.cs) | 387 | **0** | **PASS (Thin Controller)** |
| 2 | [ApprovalsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ApprovalsController.cs) | 140 | **0** | **PASS (Thin Controller)** |
| 3 | [AsnsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/AsnsController.cs) | 142 | **0** | **PASS (Thin Controller)** |
| 4 | [AuthController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/AuthController.cs) | 141 | **0** | **PASS (Thin Controller)** |
| 5 | [BackordersController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/BackordersController.cs) | 46 | **0** | **PASS (Thin Controller)** |
| 6 | [BusinessPartnersController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/BusinessPartnersController.cs) | 357 | **0** | **PASS (Thin Controller)** |
| 7 | [DataImportsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/DataImportsController.cs) | 138 | **0** | **PASS (Thin Controller)** |
| 8 | [DockYardController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/DockYardController.cs) | 362 | **0** | **PASS (Thin Controller)** |
| 9 | [ExportReceiptsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ExportReceiptsController.cs) | 342 | **0** | **PASS (Thin Controller, `inventory.cost.read` guarded)** |
| 10 | [HandlingUnitsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/HandlingUnitsController.cs) | 91 | **0** | **PASS (Thin Controller)** |
| 11 | [HandoverManifestsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/HandoverManifestsController.cs) | 136 | **0** | **PASS (Thin Controller)** |
| 12 | [ImportReceiptsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ImportReceiptsController.cs) | 301 | **0** | **PASS (Thin Controller, `inventory.cost.read` guarded)** |
| 13 | [ImportTemplatesController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ImportTemplatesController.cs) | 129 | **0** | **PASS (Thin Controller)** |
| 14 | [InventoryControlController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/InventoryControlController.cs) | 219 | **0** | **PASS (Thin Controller)** |
| 15 | [InventoryMovementsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/InventoryMovementsController.cs) | 172 | **0** | **PASS (Thin Controller)** |
| 16 | [InventoryReconciliationController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/InventoryReconciliationController.cs) | 374 | **0** | **PASS (Thin Controller, `inventory.cost.read` guarded)** |
| 17 | [InventoryReversalTraceabilityController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/InventoryReversalTraceabilityController.cs) | 231 | **0** | **PASS (Thin Controller)** |
| 18 | [InventoryStatusController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/InventoryStatusController.cs) | 159 | **0** | **PASS (Thin Controller)** |
| 19 | [InventoryStocksController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/InventoryStocksController.cs) | 34 | **0** | **PASS (Thin Controller, `inventory.cost.read` guarded)** |
| 20 | [InventoryTransactionsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/InventoryTransactionsController.cs) | 49 | **0** | **PASS (Thin Controller, `inventory.cost.read` guarded)** |
| 21 | [PackingSessionsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/PackingSessionsController.cs) | 342 | **0** | **PASS (Thin Controller)** |
| 22 | [PartnerDebtsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/PartnerDebtsController.cs) | 52 | **0** | **PASS (Thin Controller)** |
| 23 | [PermissionsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/PermissionsController.cs) | 267 | **0** | **PASS (Thin Controller)** |
| 24 | [PickingTasksController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/PickingTasksController.cs) | 376 | **0** | **PASS (Thin Controller)** |
| 25 | [ProductBarcodeLookupController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ProductBarcodeLookupController.cs) | 308 | **0** | **PASS (Thin Controller)** |
| 26 | [ProductBarcodesController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ProductBarcodesController.cs) | 28 | **0** | **PASS (Thin Controller)** |
| 27 | [ProductCategoriesController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ProductCategoriesController.cs) | 66 | **0** | **PASS (Thin Controller)** |
| 28 | [ProductsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ProductsController.cs) | 341 | **0** | **PASS (Thin Controller, `inventory.cost.read` guarded)** |
| 29 | [PurchaseOrdersController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/PurchaseOrdersController.cs) | 202 | **0** | **PASS (Thin Controller, `inventory.cost.read` guarded)** |
| 30 | [PutawayTasksController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/PutawayTasksController.cs) | 293 | **0** | **PASS (Thin Controller)** |
| 31 | [ReportsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ReportsController.cs) | 354 | **0** | **PASS (Thin Controller, `inventory.cost.read` guarded)** |
| 32 | [SaasBillingController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/SaasBillingController.cs) | 124 | **0** | **PASS (Thin Controller)** |
| 33 | [SalesOrdersController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/SalesOrdersController.cs) | 373 | **0** | **PASS (Thin Controller)** |
| 34 | [ShipmentsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ShipmentsController.cs) | 261 | **0** | **PASS (Thin Controller)** |
| 35 | [StockAllocationsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/StockAllocationsController.cs) | 178 | **0** | **PASS (Thin Controller)** |
| 36 | [StockReservationsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/StockReservationsController.cs) | 135 | **0** | **PASS (Thin Controller)** |
| 37 | [StocktakesController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/StocktakesController.cs) | 290 | **0** | **PASS (Thin Controller)** |
| 38 | [StockTransfersController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/StockTransfersController.cs) | 172 | **0** | **PASS (Thin Controller)** |
| 39 | [UnitsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/UnitsController.cs) | 140 | **0** | **PASS (Thin Controller)** |
| 40 | [UserWarehouseAccessController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/UserWarehouseAccessController.cs) | 108 | **0** | **PASS (Thin Controller)** |
| 41 | [WarehouseCalendarsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/WarehouseCalendarsController.cs) | 205 | **0** | **PASS (Thin Controller)** |
| 42 | [WarehousesController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/WarehousesController.cs) | 210 | **0** | **PASS (Thin Controller)** |

---

## 6. Database Schema, Migrations, Indexes & Live Read-Only SQL Integrity Audit (`ERP_KHO`)

### 6.1 Database Schema & Constraints Summary (`FACT`)
Queried live from `ERP_KHO` on `(localdb)\MSSQLLocalDB` (`C:\Users\van40\ERP_KHO.mdf`, `72 MB`):
- **Physical Tables (`sys.tables`):** **`93`** (`92` EF Core business tables + `__EFMigrationsHistory`).
- **Applied EF Core Migrations (`dbo.__EFMigrationsHistory`):** **`59`** migrations applied (all under ProductVersion `10.0.9`), from `20260405082652_InitialCreate` through `20261027130209_Wave57AuditRemediationSchemaAndImmutableLedgerTrigger`.
- **Foreign Keys (`sys.foreign_keys`):** `226+` foreign keys (`DeleteBehavior.Restrict` / `NoAction` on domain relationships).
- **Unique Indexes (`sys.indexes WHERE is_unique = 1`):** `99` unique indexes (including `IX_PickingWaves_TenantId_WaveCode`, `IX_PutToWallSlots_TenantId_WarehouseId_WaveCode_SlotCode`, `IX_ThreePlStorageSnapshots_TenantId_WarehouseId_OwnerId_SnapshotDate`, `IX_ThreePlBillingStatements_TenantId_StatementCode`), plus:
  - `IX_InventoryStocks_CanonicalBucket` on `(TenantId, WarehouseId, ProductId, Status, LocationId, LotId, SerialId, HandlingUnitId, OwnerId)`
  - `IX_InventoryStocks_Tenant_GrainHash` on `(TenantId, GrainHash)`
  - `UX_InventoryTransactions_ReversalOfTransaction` (filtered unique index preventing double reversal of a ledger entry)
  - `UX_InventoryTransactions_CorrectiveTransaction`
  - `IX_IdempotencyRecords_TenantId_UserId_CommandScope_KeyHash`
- **Check Constraints (`sys.check_constraints`):** `58` check constraints, including:
  - `CK_InventoryStocks_Reservation`: `([ReservedQuantity]>=(0) AND [Quantity]>=[ReservedQuantity])`
  - `CK_InventoryStocks_Allocation`: `([AllocatedQuantity]>=(0) AND [LockedQuantity]>=(0) AND [ReservedQuantity]>=[AllocatedQuantity])`
  - `CK_InventoryTransactions_ReversalSign` and `CK_InventoryTransactions_StatusChangeBounds`
- **Stored Procedures (`sys.procedures`):** `1` (`dbo.sp_GetInventoryInOutReport`).
- **Database Triggers (`sys.triggers`):** **`1` active trigger in `ERP_KHO` (`TR_InventoryTransactions_Immutable_AppendOnly`, `is_disabled = 0`)**.
  - **Wave 57 Verification (`[INV-002]` & `F-002` Closed):** Append-only immutability on `InventoryTransactions` is enforced at **both** layers: (1) EF Core `ErpKhoDbContext.EnforceAppendOnlyInventoryLedger()` ([ErpKhoDbContext.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/ErpKhoDbContext.cs#L234-L260)) and (2) SQL Server database trigger `TR_InventoryTransactions_Immutable_AppendOnly` (`AFTER UPDATE, DELETE` in [20261027130209_Wave57AuditRemediationSchemaAndImmutableLedgerTrigger.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Migrations/20261027130209_Wave57AuditRemediationSchemaAndImmutableLedgerTrigger.cs#L204-L254)), which raises SQL error `50001` (`LEDGER_IMMUTABLE_VIOLATION`) and rolls back any `DELETE` or any `UPDATE` (except the one-time `CorrectiveTransactionId` link `NULL -> NOT NULL` when all other columns remain strictly unchanged).

### 6.2 Live SQL Integrity Check A — `InventoryStocks` vs `InventoryTransactions` (`FACT`)
We queried all **13** rows of `dbo.InventoryStocks` (`Id=3663..11677`) and all **28** rows of `dbo.InventoryTransactions` in `ERP_KHO` after Wave 57 remediation and verified `0.0000` variance across every `(WarehouseId, ProductId)` pair and every 9D bucket:

| Warehouse | Product (`ProductId` / `Code`) | `InventoryStocks` All-Status OnHand | `InventoryStocks` Available OnHand | `InventoryTransactions` Expected All-Status Net | `InventoryTransactions` Expected Available Net | Variance (`Current - Expected`) | Verdict |
|---|---|---:|---:|---:|---:|---:|---|
| `WH01` (`Id=1`) | `317` (`QA-E2E-001`) | `145.0000` | `108.0000` | `145.0000` | `108.0000` | **`0.0000`** | **MATCH (`100%`)** |
| `WH02` (`Id=1403`) | `317` (`QA-E2E-001`) | `21.0000` | `21.0000` | `21.0000` | `21.0000` | **`0.0000`** | **MATCH (`100%`)** |
| `WH01` (`Id=1`) | `517` (`QA-LOT-002`) | `66.0000` | `66.0000` | `66.0000` | `66.0000` | **`0.0000`** | **MATCH (`100%`)** |
| **TOTAL** | **All Warehouses & SKUs** | **`232.0000`** | **`195.0000`** | **`232.0000`** | **`195.0000`** | **`0.0000`** | **0 Mismatch Pairs (`100% PASS`)** |

#### Wave 57 Closure of `WH01 / QA-LOT-002 (ProductId=517)` (`F-001` Closed, `FACT`):
1. In `dbo.InventoryTransactions` for `WarehouseId=1, ProductId=517` (`14` rows total):
   - `Id=3718` (`TransactionType=0 [Import]`, `LocationId=1 [TIEP-NHAN], LotId=1002`): `+65.0000`
   - `Id=8722` (`TransactionType=9 [Move]`, `FromLocationId=1 -> ToLocationId=1009, LotId=1002`): `5.0000` (leaves `+60.0000` at `LocationId=1, LotId=1002` and `+5.0000` at `LocationId=1009, LotId=1002`)
   - `Id=8733` (`TransactionType=7 [Ship]`, `LocationId=1009 [BIN-A01-01], LotId=1002`): `-5.0000`
   - `Id=24849, 25852, 26848, 27848` (`TransactionType=4 [TransferOut]`, `ReferenceType='VAS_DEKIT'`, `LocationId=1009, LotId=1002`): `4 × -1.0000 = -4.0000`
   - `Id=24850, 25853, 26849, 27849` (`TransactionType=5 [TransferIn]`, `ReferenceType='VAS_DEKIT'`, `LocationId=1009, LotId=1002` — repaired in Wave 57 migration from `LotId=NULL` to `LotId=1002`): `4 × +1.0000 = +4.0000` (net `0.0000` at `LocationId=1009, LotId=1002`)
   - `Id=28849` (`TransactionType=0 [Import]`, `LocationId=2014 [RCV-01], LotId=1002, HandlingUnitId=3`): `+6.0000`
   - `Id=28854` & `Id=28855` (`TransactionType=9 [Move]`, `2014 -> 2013`, `LotId=1002, HandlingUnitId=3`): `-6.0000 / +6.0000` (leaving `+6.0000` at `LocationId=2013, LotId=1002, HandlingUnitId=3`)
   - **Total Ledger Expected Quantity for `WH01 / ProductId=517` = `60.0000 + 6.0000 = 66.0000`**.
2. In `dbo.InventoryStocks` for `WarehouseId=1, ProductId=517` (`5` rows total across the `13` rows in `dbo.InventoryStocks` `Id=3663..11677`):
   - `Id=3667` (`LocationId=10, LotId=3`): `Quantity = 0.0000`
   - `Id=5663` (`LocationId=1009 [BIN-A01-01], LotId=1002`): `Quantity = 0.0000` (`+5 - 5 - 4 + 4 = 0.0000`)
   - `Id=11673` (`LocationId=2014 [RCV-01], LotId=1002, HandlingUnitId=3`): `Quantity = 0.0000` (`+6 - 6 = 0.0000`)
   - `Id=11676` (`LocationId=2013 [STAGE-01], LotId=1002, HandlingUnitId=3`): `Quantity = 6.0000`
   - `Id=11677` (`LocationId=1 [TIEP-NHAN], LotId=1002, HandlingUnitId=NULL` — created by Wave 57 `RebuildBalancesAsync` / migration): `Quantity = 60.0000`
   - **Total `InventoryStocks` Current Quantity for `WH01 / ProductId=517` = `66.0000` (`Variance = 0.0000`)**.

### 6.3 Live SQL Integrity Check B — `InventoryStocks.ReservedQuantity` vs Active `StockReservations` & `StockAllocations` (`FACT`)
- **Active `StockReservations` (`Status IN (0, 1, 6, 7)`):**
  - `Id=237` (`RSV-QA-PICK-001`, `WarehouseId=1, ProductId=317, Status=7 [Allocated]`): `Quantity = 5.0000`, `ConsumedQuantity = 0.0000`, `ReleasedQuantity = 0.0000`, `AllocatedQuantity = 5.0000` (Net active = `5.0000`).
  - `Id=1237` (`RSV-QA-PACK-001`, `WarehouseId=1, ProductId=317, Status=7 [Allocated]`): `Quantity = 4.0000`, `ConsumedQuantity = 0.0000`, `ReleasedQuantity = 0.0000`, `AllocatedQuantity = 4.0000` (Net active = `4.0000`).
  - *(Other 4 rows `Id=112, 113, 114, 1238` are terminal `Status=2 [Consumed]` or `Status=5 [Cancelled]` with `0.0000` remaining reserved quantity.)*
  - **Total Active Reservation Net Quantity = `9.0000`**.
- **Active `StockAllocations` (`Status IN (0, 1, 2)`):**
  - `Id=1` (`ALC-QA-PICK-001`, `ReservationId=237, WarehouseId=1, ProductId=317, LocationId=1009, Status=0 [Active]`): `Quantity = 5.0000`.
  - `Id=2` (`ALC-QA-PACK-001`, `ReservationId=1237, WarehouseId=1, ProductId=317, LocationId=1009, Status=0 [Active]`): `Quantity = 4.0000`.
  - *(Row `Id=3` `ALC-QA-LOT-002` is terminal `Status=3 [Consumed]`.)*
  - **Total Active Allocation Quantity = `9.0000`**.
- **`InventoryStocks.ReservedQuantity` & `AllocatedQuantity`:**
  - `Id=4663` (`WarehouseId=1, ProductId=317, LocationId=1009, LotId=1, Status=0 [Available]`): `Quantity = 10.0000`, `ReservedQuantity = 9.0000`, `AllocatedQuantity = 9.0000`.
  - All other 12 rows of `InventoryStocks`: `ReservedQuantity = 0.0000`, `AllocatedQuantity = 0.0000`.
- **Check B Verdict:** **PASS (`0.0000` drift: `9.0000 = 9.0000 = 9.0000`)**.

### 6.4 Live SQL Integrity Check C — Physical Stock & GrainHash Invariants (`FACT`)
- `TotalGrainRows = 13` (`Id=3663, 3664, 3665, 3666, 3667, 4663, 5663, 11672, 11673, 11674, 11675, 11676, 11677`)
- `TotalOnHand = 232.0000`, `TotalReserved = 9.0000`, `TotalAvailable = 223.0000`
- `NegativeOnHandCount = 0`
- `NegativeReservedCount = 0`
- `OverReservedCount (ReservedQuantity > Quantity) = 0`
- `NullOrInvalidGrainHashCount = 0` (all `13/13` rows have valid 32-byte `SHA256` `GrainHash` values).
- **Check C Verdict:** **PASS (`100%`)**.

---

## 7. Frontend React 19 & Vite 8 SPA Implementation Map

### 7.1 Route Table ([AppRoutes.tsx](file:///d:/ERP_KHO/frontend/src/routes/AppRoutes.tsx#L1-L135)) (`FACT`)
- `AppRoutes.tsx` defines **43** `<Route>` declarations (`1` public `/login` route + `41` protected routes inside `MainLayout` [including `6` `<Navigate>` redirects] + `1` catch-all `*` redirect) backed by **39** `React.lazy()` imports (**35** unique routed page views in `frontend/src/pages/` plus `4` embedded tab components also imported in `AppRoutes.tsx`).
- In total, `frontend/src/pages/` contains **42** non-test `.tsx` files: **41** component `.tsx` files (`39` imported in `AppRoutes.tsx` + `StockTransferDetailsDialog.tsx` + `NotFound.tsx`; the unrouted legacy orphan `WarehouseStructure.tsx` was deleted in Wave 57 to close `F-008`) plus **1** shared helper module (`inboundPlanningShared.tsx`, `85` lines). Exactly **18** files in `frontend/src/pages/` exceed `500` lines.

| # | Route Path (`AppRoutes.tsx`) | Page Component (`frontend/src/pages/`) | Lines | `>500` Line God Component? |
|---:|---|---|---:|---|
| 1 | `/login` | [Login.tsx](file:///d:/ERP_KHO/frontend/src/pages/Login.tsx) | 180 | No |
| 2 | `/dashboard` | [Dashboard.tsx](file:///d:/ERP_KHO/frontend/src/pages/Dashboard.tsx) | 878 | **Yes (`878`)** |
| 3 | `/onboarding` | [OnboardingWizard.tsx](file:///d:/ERP_KHO/frontend/src/pages/OnboardingWizard.tsx) | 344 | No |
| 4 | `/products` | [Products.tsx](file:///d:/ERP_KHO/frontend/src/pages/Products.tsx) | 1,119 | **Yes (`1,119`)** |
| 5 | `/categories` | [Categories.tsx](file:///d:/ERP_KHO/frontend/src/pages/Categories.tsx) | 271 | No |
| 6 | `/units` | [Units.tsx](file:///d:/ERP_KHO/frontend/src/pages/Units.tsx) | 384 | No |
| 7 | `/warehouses` | [Warehouses.tsx](file:///d:/ERP_KHO/frontend/src/pages/Warehouses.tsx) | 443 | No |
| 8 | `/warehouse-locations` (`/warehouse-structure`) | [WarehouseLocations.tsx](file:///d:/ERP_KHO/frontend/src/pages/WarehouseLocations.tsx) | 878 | **Yes (`878`)** |
| 9 | `/warehouse-map` | [WarehouseMap.tsx](file:///d:/ERP_KHO/frontend/src/pages/WarehouseMap.tsx) | 488 | No |
| 10 | `/warehouse-calendar` | [WarehouseCalendar.tsx](file:///d:/ERP_KHO/frontend/src/pages/WarehouseCalendar.tsx) | 494 | No |
| 11 | `/business-partners` | [BusinessPartners.tsx](file:///d:/ERP_KHO/frontend/src/pages/BusinessPartners.tsx) | 362 | No |
| 12 | `/partner-debts` | [PartnerDebts.tsx](file:///d:/ERP_KHO/frontend/src/pages/PartnerDebts.tsx) | 728 | **Yes (`728`)** |
| 13 | `/purchase-orders` | [PurchaseOrders.tsx](file:///d:/ERP_KHO/frontend/src/pages/PurchaseOrders.tsx) | 449 | No |
| 14 | `/asns` | [Asns.tsx](file:///d:/ERP_KHO/frontend/src/pages/Asns.tsx) | 429 | No |
| 15 | `/import-receipts` | [ImportReceipts.tsx](file:///d:/ERP_KHO/frontend/src/pages/ImportReceipts.tsx) | 1,446 | **Yes (`1,446`)** |
| 16 | `/putaway-tasks` | [PutawayTasks.tsx](file:///d:/ERP_KHO/frontend/src/pages/PutawayTasks.tsx) | 395 | No |
| 17 | `/outbound-demand` | [OutboundDemand.tsx](file:///d:/ERP_KHO/frontend/src/pages/OutboundDemand.tsx) | 866 | **Yes (`866`)** |
| 18 | `/export-receipts` | [ExportReceipts.tsx](file:///d:/ERP_KHO/frontend/src/pages/ExportReceipts.tsx) | 1,118 | **Yes (`1,118`)** |
| 19 | `/stock-reservations` | [StockReservations.tsx](file:///d:/ERP_KHO/frontend/src/pages/StockReservations.tsx) | 341 | No |
| 20 | `/stock-allocations` | [StockAllocations.tsx](file:///d:/ERP_KHO/frontend/src/pages/StockAllocations.tsx) | 394 | No |
| 21 | `/picking-tasks` | [PickingTasks.tsx](file:///d:/ERP_KHO/frontend/src/pages/PickingTasks.tsx) | 819 | **Yes (`819`)** |
| 22 | `/packing-sessions` | [PackingSessions.tsx](file:///d:/ERP_KHO/frontend/src/pages/PackingSessions.tsx) | 1,086 | **Yes (`1,086`)** |
| 23 | `/shipments` | [Shipments.tsx](file:///d:/ERP_KHO/frontend/src/pages/Shipments.tsx) | 524 | **Yes (`524`)** |
| 24 | `/dock-yard` | [DockYard.tsx](file:///d:/ERP_KHO/frontend/src/pages/DockYard.tsx) | 935 | **Yes (`935`)** |
| 25 | `/inventory` (`/reports`, `/inventory-lots`, `/inventory-serials`) | [Inventory.tsx](file:///d:/ERP_KHO/frontend/src/pages/Inventory.tsx) | 1,405 | **Yes (`1,405`)** |
| 26 | `/inventory-buckets` | [InventoryBuckets.tsx](file:///d:/ERP_KHO/frontend/src/pages/InventoryBuckets.tsx) | 317 | No |
| 27 | `/inventory-movements` | [InventoryMovements.tsx](file:///d:/ERP_KHO/frontend/src/pages/InventoryMovements.tsx) | 489 | No |
| 28 | `/inventory-locks` | [InventoryLocks.tsx](file:///d:/ERP_KHO/frontend/src/pages/InventoryLocks.tsx) | 327 | No |
| 29 | `/inventory-reconciliation` | [InventoryReconciliation.tsx](file:///d:/ERP_KHO/frontend/src/pages/InventoryReconciliation.tsx) | 1,306 | **Yes (`1,306`)** |
| 30 | `/inventory-reversals` | [InventoryReversals.tsx](file:///d:/ERP_KHO/frontend/src/pages/InventoryReversals.tsx) | 308 | No |
| 31 | `/inventory-traceability` | [InventoryTraceability.tsx](file:///d:/ERP_KHO/frontend/src/pages/InventoryTraceability.tsx) | 718 | **Yes (`718`)** |
| 32 | `/stocktakes` | [Stocktakes.tsx](file:///d:/ERP_KHO/frontend/src/pages/Stocktakes.tsx) | 1,567 | **Yes (`1,567`)** |
| 33 | `/stock-transfers` | [StockTransfers.tsx](file:///d:/ERP_KHO/frontend/src/pages/StockTransfers.tsx) | 993 | **Yes (`993`)** |
| 34 | `/approvals` | [Approvals.tsx](file:///d:/ERP_KHO/frontend/src/pages/Approvals.tsx) | 741 | **Yes (`741`)** |
| 35 | `/permissions` | [Permissions.tsx](file:///d:/ERP_KHO/frontend/src/pages/Permissions.tsx) | 1,447 | **Yes (`1,447`)** |
| 36 | `/handover-manifests` | [HandoverManifests.tsx](file:///d:/ERP_KHO/frontend/src/pages/HandoverManifests.tsx) | 372 | No |
| 37 | `/cod-reconciliation` | [CodReconciliation.tsx](file:///d:/ERP_KHO/frontend/src/pages/CodReconciliation.tsx) | 449 | No |
| 38 | `/cost-valuation` | [CostValuation.tsx](file:///d:/ERP_KHO/frontend/src/pages/CostValuation.tsx) | 437 | No |
| 39 | `/saas-billing` | [SaasBilling.tsx](file:///d:/ERP_KHO/frontend/src/pages/SaasBilling.tsx) | 390 | No |

### 7.2 Frontend Architecture, Barcode Scanner & Mock Data Audit (`FACT`)
1. **API Client ([apiClient.ts](file:///d:/ERP_KHO/frontend/src/services/apiClient.ts), `360` lines):**
   - Automatically attaches `Idempotency-Key` (`auto-${method}-${uuid}`) on all `POST`, `PUT`, `PATCH`, `DELETE` requests (`L153-L192`) and reuses the cached key across network retries within a 5s window (`[FE-005]` PASS).
   - Stores `lastErrorCode` and `lastCorrelationId` (`x-correlation-id`) from error responses (`L222-L229`).
   - Performs single-flight `/api/Auth/refresh` on `401` (`L109-L127`, `L258-L273`) and live permission refresh on `403` (`L236-L248`).
2. **Camera & USB/Bluetooth Barcode Scanner ([MobileBarcodeScannerBar.tsx](file:///d:/ERP_KHO/frontend/src/components/MobileBarcodeScannerBar.tsx) [`1,168` lines] + [cameraBarcodeDecoder.ts](file:///d:/ERP_KHO/frontend/src/utils/cameraBarcodeDecoder.ts) [`851` lines]):**
   - Supports hardware wedge scanners, GS1-128 AI parsing (`(01)` GTIN, `(10)` Lot, `(17)` Expiry, `(21)` Serial, `(00)` SSCC at `MobileBarcodeScannerBar.tsx:63-94`), native `BarcodeDetector`, and a pure-TypeScript multi-scanline 1D barcode decoder (`EAN-13`, `EAN-8`, `UPC-A`, `Code-128`) with Modulo-10 checksum verification (`cameraBarcodeDecoder.ts:94-106`).
3. **Mock Data Import Audit (`[FE-002]` — `0` Violations After Wave 57):**
   - **`WarehouseStructure.tsx` (`F-008` Closed in Wave 57):** The unrouted legacy orphan component `frontend/src/pages/WarehouseStructure.tsx` was deleted in Wave 57, and [WarehouseStructure.test.tsx](file:///d:/ERP_KHO/frontend/src/pages/WarehouseStructure.test.tsx) was rewritten to test the live-routed [WarehouseLocations.tsx](file:///d:/ERP_KHO/frontend/src/pages/WarehouseLocations.tsx) against mocked `/api/warehouses` HTTP endpoints (`32/32` tests in `WarehouseStructure.test.tsx` PASS).
   - **[InventoryReconciliation.tsx](file:///d:/ERP_KHO/frontend/src/pages/InventoryReconciliation.tsx#L5-L6) (`Gated behind Demo Mode`):** Imports `demoReconciliationRows` (`L6`), but strictly guards it behind `if (demoRuntime)` (`isReadOnlyDemoRuntime()`, `L377`), using live `/api/InventoryReconciliation` in normal runtime (`L406`).
4. **Component Size Rule (`Role 7 — >500 lines must split`):**
   - **18** page components in `frontend/src/pages/` (`Stocktakes.tsx` `1,567`, `Permissions.tsx` `1,447`, `ImportReceipts.tsx` `1,446`, `Inventory.tsx` `1,405`, `InventoryReconciliation.tsx` `1,306`, `Products.tsx` `1,119`, `ExportReceipts.tsx` `1,118`, `PackingSessions.tsx` `1,086`, `StockTransfers.tsx` `993`, `DockYard.tsx` `935`, `Dashboard.tsx` `878`, `WarehouseLocations.tsx` `878`, `OutboundDemand.tsx` `866`, `PickingTasks.tsx` `819`, `Approvals.tsx` `741`, `PartnerDebts.tsx` `728`, `InventoryTraceability.tsx` `718`, `Shipments.tsx` `524`) and **1** shared component (`MobileBarcodeScannerBar.tsx`, `1,168` lines) exceed the 500-line threshold.
