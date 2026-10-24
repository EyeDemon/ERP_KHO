# HANDOFF_SUBAGENT_1B — Wave 54 Backend Audit Remediation, Modular Controller Split & Warehouse Floor Enhancements

## 1. Đã thay đổi gì (Changed Files & Coordinates)

1. `ERP.Api/Authorization/AppPermissions.cs` (`L12-13`)
   - Added `InventoryWrite = "inventory_movement.create"` and `InventoryOpeningBalanceImport = "inventory_movement.create"` aliases so opening balance import enforces write permissions without altering the 124-entry `Catalog` count.
2. `ERP.Api/Authorization/PermissionAuthorizeAttribute.cs` (`L16-66`)
   - Added `public PermissionAuthorizeAttribute(string permission, params string[] additionalPermissions)` constructor overload and updated `PermissionAuthorizationFilter` to evaluate any of the granted permissions.
3. `ERP.Application/Interfaces/IHandoverManifestService.cs` *(new, `L1-47`)*
   - Defined `IHandoverManifestService`, `ScanHandoverAwbInputDto`, `SealHandoverManifestInputDto`, `ConfirmCarrierHandoverInputDto`, and `HandoverManifestOperationResult`.
4. `ERP.Infrastructure/Services/HandoverManifestService.cs` *(new, `L1-767`)*
   - Extracted handover manifest query, AWB scan, seal, carrier handover confirmation, and `DispatchManifestReceiptsAsync` into `ERP.Infrastructure`.
   - Enforced SQL transaction, deterministic 9-dimension lock ordering (`AcquireInventoryAllocationLocksAsync`), `StockReservation` / `StockAllocation` consumption, `InventoryTransaction` ledger append, `IntegrationOutboxMessage` (`HandoverManifestDispatched`), and `AuditLog`.
   - Eliminated hardcoded `"51C-888.99"` / `"Bưu tá lấy hàng"` (`[A7]`), requiring explicit `VehiclePlate` and `DriverName` (`400 BadRequest` with `HANDOVER_VEHICLE_DRIVER_REQUIRED` when sealing/confirming without vehicle/driver).
   - Eliminated `.OrderBy(u => u.Id).Select(u => (int?)u.Id).FirstOrDefaultAsync(...)` user fallback (`[A3]`).
5. `ERP.Api/Controllers/ExportReceiptsController.cs` (`L19-46, L124-152, L535-720, L790-900, L902-1366, L1586-1590`)
   - Removed all 7 fake `if (_db == null) return Ok(...)` blocks (`[A2]`) and added `RequireDb()`.
   - Removed `.OrderBy(u => u.Id).Select(u => (int?)u.Id).FirstOrDefaultAsync(...)` fallback in `ResolveValidAuditUserIdAsync` (`[A3]`).
   - Removed `DispatchManifestReceiptsAsync` and direct `stock.Quantity` mutations from the controller (`[A4]`), delegating `GetHandoverManifests`, `ScanHandoverAwb`, `SealHandoverManifest`, and `ConfirmCarrierHandover` to `IHandoverManifestService`.
   - Changed `ImportOpeningBalance` authorization from `[PermissionAuthorize(AppPermissions.ExportReceiptRead)]` to `[PermissionAuthorize(AppPermissions.InventoryOpeningBalanceImport, AppPermissions.InventoryWrite)]` (`[A10]`) and wrapped opening balance mutations in an explicit SQL transaction with `AuditLog`.
6. `ERP.Api/Controllers/HandoverManifestsController.cs` *(new, `L1-151`)*
   - Dedicated controller for `[Route("api/v1/handover-manifests")]` and `[Route("api/handover-manifests")]` (`GET`, `POST scan-awb`, `POST {id:int}/seal`, `POST confirm`, `POST confirm-carrier-handover`, and `~/api/v1/integrations/handover-manifests/...`).
7. `ERP.Api/Controllers/DataImportsController.cs` *(new, `L1-51`)*
   - Dedicated controller for `[Route("api/v1/data-imports")]` and `[Route("api/data-imports")]` (`GET templates`, `POST opening-balance`).
8. `ERP.Api/Controllers/SaasBillingController.cs` *(new, `L1-55`)*
   - Dedicated controller for `[Route("api/v1/saas-billing")]` and `[Route("api/saas-billing")]` (`GET subscription`, `POST enforce-quota`, `POST vietqr-invoice`, `POST vietqr-webhook`).
9. `ERP.Api/Controllers/PermissionsController.cs` (`L854-861`)
   - Removed `.OrderBy(u => u.Id).Select(u => (int?)u.Id).FirstOrDefaultAsync(ct)` user fallback in `HandleVietQrBillingWebhook`.
10. `ERP.Api/Controllers/ShipmentsController.cs` (`L172-422, L878-888`)
    - Added `GET /api/v1/shipments/fleet-assignments` and `POST /api/v1/shipments/fleet-assignments` with `FLEET_VEHICLE_DRIVER_REQUIRED` validation and SQL persistence on `Shipment` / `DockAppointment` / `HandoverManifest`.
11. `ERP.Infrastructure/Services/PickingService.cs` (`L965-969`)
    - Sorted `PickingTaskDto.Lines` by optimal bin path (`SourceLocation.Code ASC`, `Sequence ASC`, `Id ASC`).
12. `ERP.Api/Controllers/PickingTasksController.cs` (`L87-178`)
    - Added `GET /api/v1/picking-tasks/{id:int}/alternate-bins` (and `/api/picking-tasks/{id:int}/alternate-bins`) returning FEFO/FIFO sorted pickable alternate bins for short-pick exceptions.
13. `ERP.Api/Controllers/PutawayTasksController.cs` (`L176-344`)
    - Added `GET /api/v1/putaway-tasks/{id:int}/directed-suggestion` and `GET /api/v1/putaway-tasks/directed-suggestions` ranking candidate storage bins by SKU consolidation, empty storage bin priority, and available capacity.
14. `ERP.Api/Controllers/ImportReceiptsController.cs` (`L928-1065`)
    - Added `GET /api/v1/import-receipts/{id:int}/mac-preview` (and `/api/import-receipts/{id:int}/mac-preview`) computing `(OldQty * OldMAC + IncomingQty * UnitCost) / (OldQty + IncomingQty)` per line, guarded by `inventory.cost.read` RBAC.
15. `ERP.Api/Program.cs` (`L206`)
    - Registered `IHandoverManifestService` / `HandoverManifestService` as `Scoped`.

## 2. Bằng chứng kiểm chứng (Verification Evidence)
- **FACT:** `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-encoding.ps1` -> `PASS (Exit Code 0)`.
- **FACT:** `dotnet build ERP.Api/ERP.Api.csproj -c Release --no-restore` -> `0 Error(s)`.

## 3. Những gì NOT VERIFIED
- **NOT VERIFIED:** Full solution test suite (`ERP.Application.Tests`, `ERP.Api.Tests`, `ERP.SqlIntegrationHarness`) and live Playwright browser verification are coordinated by the Master Orchestrator / Subagent 4 after all parallel wave subagents finish.

## 4. Rủi ro tồn đọng (Residual Risks)
- None within Subagent 1B file ownership boundary. All legacy route aliases on `ExportReceiptsController` and `PermissionsController` remain backward-compatible without duplicate route collisions.

## 5. Việc dở dang / Stub
- None (`0` TODO/FIXME/NotImplementedException/stub endpoints).
