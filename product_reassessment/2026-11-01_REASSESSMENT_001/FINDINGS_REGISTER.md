# MASTER FINDINGS REGISTER (DELIVERABLE R)

**Project:** ERP_KHO — Cloud ERP & WMS SaaS  
**Document ID:** `DELIVERABLE-R`  
**Audit Run:** `2026-11-01_REASSESSMENT_001`  
**Mode:** READ-ONLY SYSTEM AUDIT + TO-BE DESIGN PROPOSAL  
**Baseline Branch / SHA:** `feat/WMS-61-project-development` (`6e0a333390c1db6222e55ae3945b7ce33a56bef6`)

---

## 1. Findings Summary & Taxonomy Matrix

All findings are classified using the **17 Mandatory Gap Categories** and prioritized for ERP_KHO's ICP (**1 warehouse, 5–30 staff, Vietnamese e-commerce seller & B2B distributor**):

| Priority | Count | Description |
|----------|------:|-------------|
| 🔴 **P0 (Critical / Blocker)** | **5** | Security vulnerabilities, RBAC bypasses, missing core e-commerce RMA UI, or data/session integrity risks that must be fixed first. |
| 🟠 **P1 (High Value / Core ICP)** | **10** | High-friction workflow gaps, controller/route standardization, God-component decomposition, hardware scanner wedge, and statutory reporting (`Thẻ Kho`). |
| 🟡 **P2 (Medium / Optimization)** | **6** | Navigation consolidation, Kit/VAS workbench completion, min/max replenishment rules, and performance indexing. |
| 🔵 **P3 / DEFERRED** | **3** | Over-engineered enterprise features (WCS/Robotics telemetry, complex multi-echelon yard orchestration) to hide/defer for 1-warehouse ICP. |
| **TOTAL** | **24** | **100% Evidence-Backed (`FACT` from repository `File:Line`)** |

---

## 2. Detailed Findings Catalog (`FND-001` → `FND-024`)

---

### `FND-001` — In-Memory TOTP 2FA State, Client Secret Acceptance & Deterministic Secret Fallback
- **Module:** IAM & Account Security (`ERP.Api`)
- **Gap Category:** `SECURITY_OR_PERMISSION_GAP` / `DATA_INTEGRITY_RISK`
- **Priority:** 🔴 **P0** | **Complexity:** `M` | **Risk:** `HIGH`
- **Current Behavior (`AS-IS`):**
  1. `AccountSecurityController` stores TOTP enrollment state in `static ConcurrentDictionary<int, ...> TotpEnrollments` (`AccountSecurityController.cs:122`), which is wiped on server restart.
  2. `VerifyTotp` and `RegenerateRecoveryCodes` accept `request.Base32Secret` / `request.SecretKey` from the client request body and validate the OTP against the client-supplied secret (`lines 216-219, 577-584`).
  3. If neither is present, `GetTotpSecretBytes(userId, username)` computes a deterministic secret from `SHA256("ERP_WMS_RFC6238_TOTP_SECRET:{userId}:{username}")[..20]` (`lines 589-594`).
- **Evidence (`FACT`):** [`AccountSecurityController.cs:122, 216-225, 570-594`](file:///d:/ERP_KHO/ERP.Api/Controllers/AccountSecurityController.cs#L122-L594)
- **Benchmark Reference:** ASP.NET Core Identity / OWASP ASVS V2.8 (TOTP secrets must be CSPRNG-generated server-side, persisted encrypted in DB, and never accepted from client verification payloads).
- **Business Problem:** Any server restart resets 2FA, and an attacker who knows `userId` and `username` (or sends their own `Base32Secret` in the verify payload) can forge valid TOTP codes.
- **Proposed `TO-BE` Design:**
  - Persist encrypted TOTP secret, enrollment status, and hashed recovery codes in SQL Server (`Users` table via additive EF Core migration).
  - Remove `Base32Secret`/`SecretKey` from `VerifyTotpRequest`.
  - Delete deterministic `GetTotpSecretBytes` and static `TotpEnrollments`.
- **Workflow Before → After:** In-memory/client-echoed verification → Server-persisted encrypted secret verification with one-time hashed recovery codes.
- **UI Changes:** None required on `Permissions.tsx` (contract output remains identical); remove any client-side secret echo on verify calls.
- **Data / API Impact:** Additive columns on `Users`; remove `Base32Secret`/`SecretKey` properties from `VerifyTotpRequest`.
- **Roles Affected:** `Admin`, all users enabling 2FA.
- **Dependencies:** Additive EF Core Migration (`DM-IAM-01`).
- **Acceptance Criteria & Tests:**
  - Integration test: Enroll TOTP → simulate app restart (clear memory) → verify OTP succeeds from DB.
  - Security test: Sending custom `Base32Secret` in `POST /totp/verify` is ignored and rejected with `400 INVALID_TOTP_CODE`.

---

### `FND-002` — `[AllowAnonymous]` Combined with `[PermissionAuthorize]` Bypasses Auth Filter on Tenant Provisioning
- **Module:** SaaS Onboarding & IAM (`ERP.Api`)
- **Gap Category:** `SECURITY_OR_PERMISSION_GAP`
- **Priority:** 🔴 **P0** | **Complexity:** `S` | **Risk:** `HIGH`
- **Current Behavior (`AS-IS`):** `PermissionsController.ProvisionSaaSTenant` (`POST ~/api/v1/saas/tenants/provision`) has both `[AllowAnonymous]` and `[PermissionAuthorize(AppPermissions.PermissionRead)]`. In `PermissionAuthorizationFilter.OnAuthorizationAsync`, `[AllowAnonymous]` causes an early `return;` (`line 61-64`), completely bypassing permission verification.
- **Evidence (`FACT`):** [`PermissionsController.cs:128-144`](file:///d:/ERP_KHO/ERP.Api/Controllers/PermissionsController.cs#L128-L144), [`PermissionAuthorizeAttribute.cs:61-64`](file:///d:/ERP_KHO/ERP.Api/Authorization/PermissionAuthorizeAttribute.cs#L61-L64)
- **Benchmark Reference:** Logiwa / Extensiv SaaS Onboarding (public signup is rate-limited and isolated from internal tenant admin APIs).
- **Business Problem:** Unauthenticated callers can invoke tenant provisioning endpoints without rate-limiting or admin verification.
- **Proposed `TO-BE` Design:**
  - Separate public self-service registration (`POST /api/v1/auth/register-tenant` with `[EnableRateLimiting("Signup")]`) from authenticated admin tenant provisioning (`POST /api/v1/saas/tenants/provision` without `[AllowAnonymous]`, requiring `system_config.manage`).
- **Workflow Before → After:** Single mixed anonymous/admin endpoint → Dedicated rate-limited public signup + strict authenticated admin provisioning.
- **UI Changes:** Point `/register` (`OnboardingWizard.tsx`) to `/api/v1/auth/register-tenant`.
- **Data / API Impact:** Route separation in `PermissionsController` / `TenantOnboardingController`.
- **Roles Affected:** Anonymous prospective customers, `Admin`.
- **Dependencies:** None.
- **Acceptance Criteria & Tests:**
  - API test: Unauthenticated `POST /api/v1/saas/tenants/provision` returns `401 Unauthorized`.

---

### `FND-003` — Customer Returns / RMA Has Domain Entities & Hidden Controller Actions, But Zero Dedicated UI Screen or Route
- **Module:** Returns & Reverse Logistics (`Inbound / Outbound`)
- **Gap Category:** `MISSING_CAPABILITY` / `MISPLACED_MODULE_OWNERSHIP`
- **Priority:** 🔴 **P0** | **Complexity:** `M` | **Risk:** `MEDIUM`
- **Current Behavior (`AS-IS`):**
  - Domain entities `CustomerReturn` and `CustomerReturnLine` exist (`CustomerReturnEntities.cs:5-40`).
  - Backend endpoints `GET customer-returns`, `POST rma-intake-check`, and `POST execute-rtv-refurbish` are hidden inside `ImportReceiptsController.cs:148-200` and gated only by `receipt.read`.
  - Frontend has **zero route or page** for `/customer-returns` or `/returns` in `AppRoutes.tsx` and `productionNavigation.ts`.
  - `CustomerReturn` lacks `RowVersion`, Base-UOM snapshot fields, and `LotId`/`SerialId` on lines.
- **Evidence (`FACT`):** [`CustomerReturnEntities.cs:1-40`](file:///d:/ERP_KHO/ERP.Domain/Entities/CustomerReturnEntities.cs#L1-L40), [`ImportReceiptsController.cs:148-200`](file:///d:/ERP_KHO/ERP.Api/Controllers/ImportReceiptsController.cs#L148-L200), [`AppRoutes.tsx:77-133`](file:///d:/ERP_KHO/frontend/src/routes/AppRoutes.tsx#L77-L133)
- **Benchmark Reference:** Logiwa IO, ShipHero, Odoo (`Return against Delivery Order`), ERPNext (`Sales Return`). In Vietnamese e-commerce (Shopee/TikTok Shop), return/RTO rates are 10–25% of volume.
- **Business Problem:** Warehouse staff cannot scan returned AWB parcels, inspect condition (`GOOD`, `DAMAGED_BOX`, `DEFECTIVE`), and route items to `Available` restock, `Refurbish/Repack`, `RTV`, or `Scrap` from a dedicated workbench.
- **Proposed `TO-BE` Design:**
  - Extract `CustomerReturnsController` (`/api/v1/customer-returns`) with permissions `return.read`, `return.create`, `return.receive`, `return.inspect`, `return.disposition`, `return.post` (Spec 17 §18).
  - Add `RowVersion`, Base-UOM snapshots, and `LotId`/`SerialId` to `CustomerReturn` / `CustomerReturnLine`.
  - Build dedicated frontend page `CustomerReturns.tsx` (`/customer-returns`) with AWB/Order barcode scan intake, 1-click Quarantine receipt, inspection checklist, and atomic disposition posting to 9-dimension `InventoryStock` + `InventoryTransactions`.
- **Workflow Before → After:** Ad-hoc manual import receipt workaround → Scan Returned AWB → Auto-load Dispatched Order Lines → Receive into `Quarantine` → Inspect & Select Disposition → Post Restock/Scrap/RTV to Ledger.
- **UI Changes:** Add `/customer-returns` ("Hàng hoàn & Đổi trả RMA") to Sidebar & PDA Scanner Mode.
- **Data / API Impact:** `CustomerReturnsController` (`/api/v1/customer-returns`) + additive migration on `CustomerReturns`.
- **Roles Affected:** `Receiver`, `Supervisor`, `Manager`, `Accountant`.
- **Dependencies:** `DM-01` migration.
- **Acceptance Criteria & Tests:**
  - SQL Integration test: Scan returned AWB → receive into `Quarantine` bucket → post `RESTOCK_AVAILABLE` disposition → verify `Quarantine` decreases, `Available` increases, and 2 immutable `InventoryTransactions` rows are written atomically.

---

### `FND-004` — Mutation (`POST`) Endpoints Protected by `.read` Permissions + Hardcoded `!IsInRole("Viewer")`
- **Module:** Cross-Cutting Authorization (`ERP.Api`)
- **Gap Category:** `SECURITY_OR_PERMISSION_GAP`
- **Priority:** 🔴 **P0** | **Complexity:** `M` | **Risk:** `HIGH`
- **Current Behavior (`AS-IS`):** Over 12 `POST` mutation endpoints across `ImportReceiptsController`, `ExportReceiptsController`, `ReportsController`, `PermissionsController`, and `SaasBillingController` use `[PermissionAuthorize(*.Read)]` combined with `if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();`.
- **Evidence (`FACT`):** [`ImportReceiptsController.cs:155, 195`](file:///d:/ERP_KHO/ERP.Api/Controllers/ImportReceiptsController.cs#L155-L195), [`ExportReceiptsController.cs:202, 226, 243, 255, 267, 328, 337, 375`](file:///d:/ERP_KHO/ERP.Api/Controllers/ExportReceiptsController.cs#L202-L375), [`ReportsController.cs:140`](file:///d:/ERP_KHO/ERP.Api/Controllers/ReportsController.cs#L140), [`SaasBillingController.cs:41, 57`](file:///d:/ERP_KHO/ERP.Api/Controllers/SaasBillingController.cs#L41-L57)
- **Benchmark Reference:** Notion Spec 17 §1 & §33.1 (`[SEC-001]`: Never authorize mutations using read permissions or hardcoded role names).
- **Business Problem:** Any user with a custom read-only role (not literally named `"Viewer"`) or a basic staff role with `receipt.read` / `export_receipt.read` can trigger RMA refurbishment stock mutations, seal handover manifests, issue E-Invoice drafts, or generate VietQR billing invoices.
- **Proposed `TO-BE` Design:** Replace `[PermissionAuthorize(*.Read)]` + `!Viewer` on all `POST` endpoints with their specific action permission codes (`return.create`, `return.disposition`, `shipment.dispatch`, `label.print`, `report.export`, `system_config.manage`) and remove hardcoded `User.IsInRole` checks.
- **Workflow Before → After:** `.read` + `!Viewer` role check → Pure database permission code enforcement per Spec 17.
- **UI Changes:** Update `<Can permission="...">` / `hasPermission(...)` checks on corresponding action buttons.
- **Data / API Impact:** Seed missing action permissions into default `Admin`, `Manager`, `WarehouseStaff`, and `Accountant` role bundles.
- **Roles Affected:** All roles.
- **Dependencies:** Role bundle seed migration.
- **Acceptance Criteria & Tests:**
  - API test: User with `export_receipt.read` (and role `"CustomAuditor"`) calling `POST /handover-manifests/seal` or `POST /einvoice-nd70-draft` receives `403 Forbidden`.

---

### `FND-005` — Legacy `[Authorize(Roles = ...)]` on `ReportsController` Blocks Accountant & Staff from Xuất-Nhập-Tồn Report
- **Module:** Reporting (`ERP.Api/Controllers/ReportsController.cs`)
- **Gap Category:** `SECURITY_OR_PERMISSION_GAP` / `REPORTING_GAP`
- **Priority:** 🔴 **P0** | **Complexity:** `S` | **Risk:** `LOW`
- **Current Behavior (`AS-IS`):** `ReportsController.GetInventoryInOutReport` (`line 301`) and `ExportInventoryInOutReport` (`line 323`) have `[Authorize(Roles = AppRoles.AdminManagerOrViewer)]` (`"Admin,Manager,Viewer"`), which blocks `Accountant` and `WarehouseStaff` even when they have `inventory.read`! Meanwhile, `SyncBiSnapshot` (`line 98`) and `AcknowledgeOperationalException` (`line 208, 225`) use `[Authorize(Roles = AppRoles.AdminManagerOrStaff)]`.
- **Evidence (`FACT`):** [`ReportsController.cs:13, 98, 208, 225, 301, 323`](file:///d:/ERP_KHO/ERP.Api/Controllers/ReportsController.cs#L301-L325)
- **Benchmark Reference:** Notion Spec 17 §1 & §22 (`report.inventory.read`, `inventory.read`).
- **Business Problem:** Accountants (`Accountant` role) cannot view or export the statutory `Báo cáo Xuất-Nhập-Tồn` (`403 Forbidden`), breaking month-end accounting workflows.
- **Proposed `TO-BE` Design:** Remove `[Authorize(Roles = ...)]` attributes from `ReportsController` and rely strictly on `[PermissionAuthorize(AppPermissions.InventoryRead)]` (plus `CanReadInventoryCost()` masking for monetary columns and `exception.resolve` for acknowledging exceptions).
- **Workflow Before → After:** Hardcoded role string gate → Database permission gate.
- **UI Changes:** None (`Inventory.tsx` Tab 4 already uses `inventory.read`).
- **Data / API Impact:** Attribute change on `ReportsController.cs`.
- **Roles Affected:** `Accountant`, `WarehouseStaff`, custom roles.
- **Dependencies:** None.
- **Acceptance Criteria & Tests:**
  - API test: `Accountant` user with `inventory.read` and `inventory.cost.read` calls `GET /api/v1/reports/inventory-in-out-stock` and receives `200 OK`.

---

### `FND-006` — Split `AppPermissions` Classes & Overloaded `"inventory_movement.create"` Constant
- **Module:** Domain & API Authorization
- **Gap Category:** `SECURITY_OR_PERMISSION_GAP` / `MISPLACED_MODULE_OWNERSHIP`
- **Priority:** 🟠 **P1** | **Complexity:** `S` | **Risk:** `LOW`
- **Current Behavior (`AS-IS`):**
  - `ERP.Domain.Security.AppPermissions` (`ERP.Domain/Security/AppPermissions.cs`) has 5 constants (`users.manage` [plural], `roles.manage` [plural], `inventory.read`, `inventory.cost.read`, `export_receipt.reprint_awb`).
  - `ERP.Api.Authorization.AppPermissions` (`ERP.Api/Authorization/AppPermissions.cs`) has ~85 constants (`user.manage` [singular], `role.manage` [singular]) and maps `InventoryMovementCreate`, `InventoryWrite`, and `InventoryOpeningBalanceImport` all to the same string `"inventory_movement.create"` (`line 13`).
- **Evidence (`FACT`):** [`ERP.Domain/Security/AppPermissions.cs:3-46`](file:///d:/ERP_KHO/ERP.Domain/Security/AppPermissions.cs#L3-L46), [`ERP.Api/Authorization/AppPermissions.cs:13-27`](file:///d:/ERP_KHO/ERP.Api/Authorization/AppPermissions.cs#L13-L27)
- **Benchmark Reference:** Spec 17 §2 & §14.
- **Business Problem:** Granting a warehouse operator `inventory_movement.create` (to move stock between bins) unintentionally grants them `InventoryOpeningBalanceImport` (ability to import opening stock balances and alter WAC cost!).
- **Proposed `TO-BE` Design:** Merge into a single canonical `AppPermissions` registry in `ERP.Domain.Security` and separate `inventory_opening_balance.import` from `inventory_movement.create`.
- **Workflow Before → After:** Dual classes + overloaded permission string → Single source of truth with distinct permission codes.
- **UI Changes:** Update `OnboardingWizard.tsx` / opening balance import check to use `inventory_opening_balance.import`.
- **Data / API Impact:** Add `inventory_opening_balance.import` to permission catalog seed for `Admin` and `Manager`.
- **Roles Affected:** `Admin`, `Manager`, `WarehouseStaff`.
- **Dependencies:** Permission catalog seed update.
- **Acceptance Criteria & Tests:**
  - Unit test: Verify `AppPermissions.Catalog` has unique, normalized `lowercase_snake.action` codes and `InventoryOpeningBalanceImport != InventoryMovementCreate`.

---

### `FND-007` — Cross-Domain Controller Bloat (`ExportReceiptsController`, `ImportReceiptsController`, `PermissionsController`)
- **Module:** Backend API Layer (`ERP.Api/Controllers`)
- **Gap Category:** `MISPLACED_MODULE_OWNERSHIP` / `DUPLICATE_OR_OVERLAPPING_FEATURE`
- **Priority:** 🟠 **P1** | **Complexity:** `M` | **Risk:** `LOW`
- **Current Behavior (`AS-IS`):**
  - `ExportReceiptsController` (391 lines) hosts endpoints for Handover Manifests (`lines 234-277`, duplicating `HandoverManifestsController`), Carrier Address Mappings (`line 279`), Data Import Templates & Opening Balance Import (`lines 283-309`, duplicating `DataImportsController`), and MISA / E-Invoice ND70 (`lines 311-390`).
  - `ImportReceiptsController` (301 lines) hosts Customer Returns / RMA endpoints (`lines 148-200`).
  - `PermissionsController` (277 lines) hosts SaaS Tenant Provisioning, Setup Wizard, Demo Seed, and Subscription Billing (`lines 128-260`, duplicating `SaasBillingController`).
- **Evidence (`FACT`):** `ExportReceiptsController.cs:234-390`, `ImportReceiptsController.cs:148-200`, `PermissionsController.cs:128-260`
- **Benchmark Reference:** Clean Architecture `[ARCH-002]`, `[ARCH-003]` & OpenWMS.org bounded-context controller separation.
- **Business Problem:** Difficult to maintain, causes duplicate route definitions, and mixes authorization policies across unrelated bounded contexts.
- **Proposed `TO-BE` Design:** Move misplaced actions to their canonical controllers (`CustomerReturnsController`, `HandoverManifestsController`, `DataImportsController`, `AccountingExportsController`, `SaasBillingController`) while keeping route aliases pointing to the canonical controller during deprecation.
- **Workflow Before → After:** Monolithic god-controllers → Focused bounded-context controllers.
- **UI Changes:** Point frontend API calls to canonical `/api/v1/<kebab-case-plural>` routes.
- **Data / API Impact:** Controller routing cleanup; zero breaking change if route aliases are preserved on canonical controllers.
- **Roles Affected:** Engineering & QA maintainability.
- **Dependencies:** None.
- **Acceptance Criteria & Tests:**
  - All existing `ERP.Api.Tests` and `ERP.SqlIntegrationHarness` tests pass 100%.

---

### `FND-008` — In-Controller Null-Service Hardcoded Fallback Payloads
- **Module:** Backend API Layer (`ERP.Api/Controllers`)
- **Gap Category:** `DATA_INTEGRITY_RISK` / `TEST_COVERAGE_GAP`
- **Priority:** 🟠 **P1** | **Complexity:** `M` | **Risk:** `MEDIUM`
- **Current Behavior (`AS-IS`):** `ImportReceiptsController`, `ExportReceiptsController`, `AccountSecurityController`, `UserWarehouseAccessController`, and `ReportsController` accept optional (`= null!`) operations services and return hardcoded synthetic success payloads (`rmaCode = "RMA-..."`, `buyerTaxCode = "0318999888"`, `FallbackWhenNoOpsAsync`) when those services are null.
- **Evidence (`FACT`):** [`ImportReceiptsController.cs:169-181`](file:///d:/ERP_KHO/ERP.Api/Controllers/ImportReceiptsController.cs#L169-L181), [`ExportReceiptsController.cs:212-214, 343-346`](file:///d:/ERP_KHO/ERP.Api/Controllers/ExportReceiptsController.cs#L343-L346), [`ReportsController.cs:63-65`](file:///d:/ERP_KHO/ERP.Api/Controllers/ReportsController.cs#L63-L65), [`UserWarehouseAccessController.cs:93-105`](file:///d:/ERP_KHO/ERP.Api/Controllers/UserWarehouseAccessController.cs#L93-L105)
- **Benchmark Reference:** Project Rule `[FAKE-001]` & `[ARCH-003]`.
- **Business Problem:** If a DI registration is ever accidentally omitted or misconfigured, the API silently returns HTTP `200 OK` with fake data instead of failing fast!
- **Proposed `TO-BE` Design:** Make all injected services mandatory (non-nullable constructor parameters), remove all hardcoded fallback blocks and `FallbackWhenNoOpsAsync`, and update unit test setup in `ERP.Api.Tests` to pass proper mocks/fakes.
- **Workflow Before → After:** Silent in-controller fallback when service is null → Mandatory DI resolution (fail-fast at startup).
- **UI Changes:** None.
- **Data / API Impact:** Removes ~250 lines of dead/fallback controller code.
- **Roles Affected:** All users (guarantees 100% real DB execution).
- **Dependencies:** Update controller instantiation in `ERP.Api.Tests`.
- **Acceptance Criteria & Tests:**
  - `grep -rn "FallbackWhenNoOpsAsync" ERP.Api/` returns `0` matches; all unit & SQL integration tests pass.

---

### `FND-009` — Mixed API Route Conventions (`/api/PascalCase` vs `/api/v1/kebab-case-plural`)
- **Module:** API & Frontend Service Layer
- **Gap Category:** `UI_UX_FRICTION` / `INTEGRATION_RELIABILITY_GAP`
- **Priority:** 🟠 **P1** | **Complexity:** `M` | **Risk:** `LOW`
- **Current Behavior (`AS-IS`):** Frontend pages call a mix of legacy PascalCase routes (`GET /api/ImportReceipts`, `GET /api/ExportReceipts`, `GET /api/Inventory/stocks`, `GET /api/Reports/operational-dashboard`) and modern v1 routes (`GET /api/v1/handover-manifests`, `GET /api/v1/inventory/expiry-aging`).
- **Evidence (`FACT`):** [`FRONTEND_AUDIT.md:254-396, 813-821`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/FRONTEND_AUDIT.md#L813-L821)
- **Benchmark Reference:** `[API-001]` & Notion Spec 17 §2 (`Normalized Route Standard: /api/v1/<kebab-case-plural>`).
- **Business Problem:** Inconsistent route conventions complicate API gateway rules, rate-limiting, OpenAPI documentation, and demo adapter path normalization.
- **Proposed `TO-BE` Design:** Standardize 100% of frontend `apiClient` calls onto `/api/v1/<kebab-case-plural>` while keeping backend legacy route attributes as deprecated aliases for 1 release cycle.
- **Workflow Before → After:** Mixed `/api/PascalCase` and `/api/v1/kebab-case` → 100% `/api/v1/<kebab-case-plural>`.
- **UI Changes:** Update URL constants in `frontend/src/pages/*` and `demoApiAdapter.ts`.
- **Data / API Impact:** Ensure every controller has `[Route("api/v1/<kebab-case-plural>")]`.
- **Roles Affected:** Developers, API integrators.
- **Dependencies:** None.
- **Acceptance Criteria & Tests:**
  - Grep `frontend/src/pages/` for `apiClient.(get|post|put|delete)\(['"]/api/(?!v1/)` returns `0` matches.

---

### `FND-010` — In-Memory Collection Loading & Unpaginated Receipt Endpoints (`GetAllAsync`)
- **Module:** Inbound & Outbound API (`ImportReceiptsController`, `ExportReceiptsController`)
- **Gap Category:** `PERFORMANCE_OR_SCALABILITY_RISK`
- **Priority:** 🟠 **P1** | **Complexity:** `M` | **Risk:** `MEDIUM`
- **Current Behavior (`AS-IS`):**
  - `GET /api/ImportReceipts` (`ImportReceiptsController.cs:112-114`) and `GET /api/ExportReceipts` (`ExportReceiptsController.cs:76-77`) return all records without pagination (`pageNumber`, `pageSize`).
  - `GetPipelineSummary` and `GetQcAndRmaSummary` (`ImportReceiptsController.cs:119, 135`; `ExportReceiptsController.cs:82, 115`) call `(await _service.GetAllAsync()).ToList()` and count statuses in C# memory!
- **Evidence (`FACT`):** [`ImportReceiptsController.cs:112-145`](file:///d:/ERP_KHO/ERP.Api/Controllers/ImportReceiptsController.cs#L112-L145), [`ExportReceiptsController.cs:76-126`](file:///d:/ERP_KHO/ERP.Api/Controllers/ExportReceiptsController.cs#L76-L126)
- **Benchmark Reference:** `[API-004]` (All collection `GET` endpoints must enforce server-side pagination `pageSize <= 100`) & `[DB-007]`.
- **Business Problem:** Once the first paying customer processes 10,000+ Shopee/TikTok orders (approx. 1–2 months of operation), `GetAllAsync()` and `GetPipelineSummary()` will load 10,000+ aggregates and lines into RAM on every page load!
- **Proposed `TO-BE` Design:**
  - Replace `GetPipelineSummary` in-memory counting with a single SQL `GROUP BY Status` query (`AsNoTracking()`).
  - Add server-side pagination (`pageNumber = 1`, `pageSize = 50`, max `100`) + filter parameters (`warehouseId`, `status`, `search`, `fromDate`, `toDate`) to `GET /api/v1/import-receipts` and `GET /api/v1/export-receipts`.
- **Workflow Before → After:** Full table scan in memory → Indexed SQL `GROUP BY` + paged query (`Skip/Take`).
- **UI Changes:** Connect `ImportReceipts.tsx` and `ExportReceipts.tsx` table pagination controls to server-side `pageNumber`/`pageSize`/`totalCount`.
- **Data / API Impact:** Add paginated query overload to `IImportReceiptService` and `IExportReceiptService`.
- **Roles Affected:** All warehouse staff and managers.
- **Dependencies:** None.
- **Acceptance Criteria & Tests:**
  - SQL query profiling confirms `SELECT COUNT(*), Status ... GROUP BY Status` and `OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY`.

---

### `FND-011` — Frontend God Components Exceeding 1,000–2,280 Lines of Code
- **Module:** Frontend Pages (`frontend/src/pages/`)
- **Gap Category:** `UI_UX_FRICTION` / `PERFORMANCE_OR_SCALABILITY_RISK`
- **Priority:** 🟠 **P1** | **Complexity:** `L` | **Risk:** `LOW`
- **Current Behavior (`AS-IS`):** 9 page components exceed 1,000 lines of code, violating Role 7 Skill Rule (`Cấm God Component > 500 dòng phải tách`):
  - `Permissions.tsx` (2,280 LOC / 109 KB)
  - `ImportReceipts.tsx` (1,991 LOC / 128 KB)
  - `Stocktakes.tsx` (1,568 LOC / 70 KB)
  - `SaasBilling.tsx` (1,455 LOC / 72 KB)
  - `ExportReceipts.tsx` (1,417 LOC / 74 KB)
  - `Inventory.tsx` (1,394 LOC / 77 KB)
  - `InventoryReconciliation.tsx` (1,307 LOC / 81 KB)
  - `DockYard.tsx` (1,305 LOC / 83 KB)
  - `Products.tsx` (1,304 LOC / 76 KB)
- **Evidence (`FACT`):** [`FRONTEND_AUDIT.md:795, 804-812`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/FRONTEND_AUDIT.md#L804-L812)
- **Benchmark Reference:** Role 7 React 19 / Vite 8 Best Practices & OCA Shopfloor modular component decomposition.
- **Business Problem:** Monolithic 2,000-line React files mix list tables, creation forms, worksheet drawers, QC modals, and print previews in a single component state tree, causing unnecessary re-renders and high cognitive load.
- **Proposed `TO-BE` Design:** Decompose each God Component into focused feature sub-components (`< 400 LOC` each) organized by domain folder (`pages/import-receipts/`, `pages/permissions/`, `pages/inventory/`, etc.) with lazy-loaded tab panels.
- **Workflow Before → After:** Single 2,000-line file with 40+ `useState` hooks → Container page (`~200 LOC`) + isolated tab/drawer sub-components.
- **UI Changes:** Zero visual regression; cleaner state separation and faster tab switching.
- **Data / API Impact:** None (pure frontend modularization).
- **Roles Affected:** All users (faster UI responsiveness) & developers.
- **Dependencies:** None.
- **Acceptance Criteria & Tests:**
  - Every `.tsx` file under `frontend/src/pages/` is `<= 500 LOC`; all 73 Vitest test suites pass 100%.

---

### `FND-012` — Navigation Overwhelm: 36+ Flat Sidebar Links Across 6 Sections
- **Module:** Information Architecture & Navigation (`MainLayout.tsx`, `productionNavigation.ts`)
- **Gap Category:** `NAVIGATION_OVERWHELM` / `UI_UX_FRICTION`
- **Priority:** 🟠 **P1** | **Complexity:** `M` | **Risk:** `LOW`
- **Current Behavior (`AS-IS`):** In `FULL` complexity mode, the desktop sidebar renders 31–36 flat navigation links simultaneously (e.g., 9 separate links under `Xuất kho` and 9 separate links under `Tồn kho`). A warehouse operator or manager must scroll a long sidebar to distinguish `Giữ hàng (Reservations)`, `Phân bổ (Allocations)`, `Đơn thiếu hàng (Backorders)`, `Phiếu xuất (Export Receipts)`, and `Giao hàng (Shipments)`.
- **Evidence (`FACT`):** [`productionNavigation.ts:1-90`](file:///d:/ERP_KHO/frontend/src/config/productionNavigation.ts#L1-L90), [`MainLayout.tsx:100-260`](file:///d:/ERP_KHO/frontend/src/layouts/MainLayout.tsx#L100-L260)
- **Benchmark Reference:** Odoo Inventory (top bar groups: Operations, Products, Reporting, Configuration), ShipHero, Logiwa IO.
- **Business Problem:** Cognitive overload for 5–30 staff warehouses. Related sub-screens (e.g., Reservations + Allocations + Backorders, or Locks + Movements + Reversals) are exposed as top-level sidebar items instead of consolidated hubs with sub-navigation tabs.
- **Proposed `TO-BE` Design:**
  - Reduce primary sidebar items from **36+ down to 16 core workspace hubs** grouped under 6 clear sections, using **Workspace Sub-Tabs (`UiTabs`)** inside consolidated hubs (while keeping all existing URL routes alive via automatic tab selection!).
  - Add **Role-Preset Workspace Switcher** (`Chế độ Nhanh / Express` for 1-5 staff vs `Chế độ Đầy đủ / Full WMS` for 10-30 staff, plus `Chế độ Máy Quét PDA`).
- **Workflow Before → After:** 36 flat sidebar links → 16 primary sidebar items + contextual horizontal workspace tabs.
- **UI Changes:** Update `productionNavigation.ts` and `MainLayout.tsx` with collapsible sub-groups and workspace tab headers.
- **Data / API Impact:** None.
- **Roles Affected:** All users.
- **Dependencies:** See `TO_BE_INFORMATION_ARCHITECTURE.md` (Deliverable J).
- **Acceptance Criteria & Tests:**
  - Sidebar fits on `1440x900` desktop screen without vertical scrolling in default collapsed-group state; all 39 legacy routes remain navigable.

---

### `FND-013` — Hardware Barcode Scanner (USB/Bluetooth HID Wedge & Android Broadcast Intent) Gap
- **Module:** Mobile / Scanner Experience (`MobileBarcodeScannerBar.tsx`)
- **Gap Category:** `MOBILE_SCANNER_GAP`
- **Priority:** 🟠 **P1** | **Complexity:** `M` | **Risk:** `LOW`
- **Current Behavior (`AS-IS`):** `MobileBarcodeScannerBar.tsx` focuses on phone camera scanning and manual text input. However, in real Vietnamese e-commerce warehouses (5–30 staff), workers use **industrial Android PDAs (Zebra, Chainway, Urovo, Sunmi)** or **wireless 2D USB/Bluetooth barcode guns** operating in **HID Keyboard Wedge mode** (rapid keystrokes `< 25ms/char` followed by `Enter`) or **GS1-128 composite barcodes** (`(01)GTIN(10)LOT(17)EXP(21)SERIAL`). Currently, if focus is not manually clicked inside the scan input field, pulling the hardware trigger on a scanner gun drops the scan!
- **Evidence (`FACT`):** [`MobileBarcodeScannerBar.tsx`](file:///d:/ERP_KHO/frontend/src/components/MobileBarcodeScannerBar.tsx), `COMPETITIVE_OPENSOURCE_RESEARCH.md` (Sentry WMS & GreaterWMS analysis), `COMPETITIVE_COMMERCIAL_RESEARCH.md` (Odoo GS1 & sound/vibration UX).
- **Benchmark Reference:** Sentry WMS (industrial scanner-first), Odoo Barcode (GS1 parser + audio/haptic feedback + 30-scan history), GreaterWMS.
- **Business Problem:** Warehouse pickers and packers cannot look at the screen and tap an input field before every scan; hardware trigger pulls must work globally on the active floor screen with instant audio beep (success chime vs error buzzer) and vibration feedback.
- **Proposed `TO-BE` Design:**
  - Build a global `useHardwareScannerWedge` hook that listens at `window` `keydown` level (when no modal text input is focused), buffers rapid keystroke bursts (`< 35ms` interval, length `>= 4`, terminated by `Enter`/`Tab`), and dispatches the scanned barcode to the active workbench handler.
  - Add a **GS1-128 / Composite Barcode Parser** utility (`parseWarehouseBarcode(raw)`) that extracts `ProductBarcode`, `LotNumber` (`AI 10`), `ExpiryDate` (`AI 17`), and `SerialNumber` (`AI 21`) in a single scan!
  - Add Web Audio API synthesized `beepSuccess()` / `beepError()` + `navigator.vibrate([100, 50, 100])` on scan outcomes.
- **Workflow Before → After:** Tap input box → scan → read screen to check error → Pull hardware trigger anywhere on page → auto-parse GS1 SKU+Lot+Expiry → instant audio/haptic confirmation.
- **UI Changes:** Upgrade `MobileBarcodeScannerBar.tsx` with "Chế độ Súng Quét Tự Động (Auto-Wedge Active)" indicator pill + scan history log.
- **Data / API Impact:** Client-side parser feeds existing `ProductCode`, `LotNumber`, `ExpiryDate`, `SerialNumber` API fields.
- **Roles Affected:** `Receiver`, `PickerPacker`, `WarehouseStaff`.
- **Dependencies:** None.
- **Acceptance Criteria & Tests:**
  - Vitest test: Dispatching rapid `keydown` events for `(01)08934567890128(10)LOT2026A(17)271231` + `Enter` without input focus parses SKU `8934567890128`, Lot `LOT2026A`, Expiry `2027-12-31` and invokes the scan callback.

---

### `FND-014` — Outbound Dual-Track Confusion: Express `ExportReceipt` vs Full `SalesOrder → Picking → Packing → Shipment`
- **Module:** Outbound & Fulfillment (`ExportReceipts`, `OutboundDemand`, `PickingTasks`, `PackingSessions`, `Shipments`)
- **Gap Category:** `INCOMPLETE_WORKFLOW` / `UI_UX_FRICTION`
- **Priority:** 🟠 **P1** | **Complexity:** `M` | **Risk:** `MEDIUM`
- **Current Behavior (`AS-IS`):** ERP_KHO has two outbound tracks in the UI:
  1. **Track A (Express MVP — Notion Spec 17 §34):** `ExportReceipt` (`Draft → Approved [SoftReserve] → Dispatched [Deduct OnHand + Write Ledger]`).
  2. **Track B (Full WMS):** `SalesOrder / Backorder` → `StockReservation` → `StockAllocation` → `PickingTask` → `PackingSession (HU)` → `Shipment` → `HandoverManifest`.
  However, in the UI sidebar under `Xuất kho`, all 8 screens from both tracks are listed side-by-side without explaining when to use Track A vs Track B, or showing a unified visual pipeline banner!
- **Evidence (`FACT`):** [`productionNavigation.ts:24-39`](file:///d:/ERP_KHO/frontend/src/config/productionNavigation.ts#L24-L39), [`ExportReceipts.tsx`](file:///d:/ERP_KHO/frontend/src/pages/ExportReceipts.tsx), [`OutboundDemand.tsx`](file:///d:/ERP_KHO/frontend/src/pages/OutboundDemand.tsx)
- **Benchmark Reference:** Odoo configurable `1-step / 2-step / 3-step Outbound Routes` (`Delivery only` vs `Pick + Ship` vs `Pick + Pack + Ship`).
- **Business Problem:** A new warehouse customer (5–15 staff) is confused whether to create a `Phiếu xuất kho (ExportReceipt)` or a `Đơn hàng (SalesOrder)`.
- **Proposed `TO-BE` Design:**
  - Explicitly define **2 Outbound Operating Modes** per Warehouse Policy (`WarehouseOutboundPolicy` / Complexity Toggle):
    - **Mode 1 — Xuất Kho Nhanh 2 Bước (Express 2-Step via `ExportReceipt`):** Default for `< 10` staff or B2B wholesale orders (`Tạo phiếu xuất → Duyệt & Giữ hàng → Xuất kho & Bàn giao ĐVVC`).
    - **Mode 2 — Xuất Kho Sàn TMĐT Đa Bước (Full E-Commerce Fulfillment):** Default for high-volume Shopee/TikTok Shop (`Đơn hàng SO → Phân bổ vị trí → Lấy hàng theo cụm/Wave → Đóng gói & Cân/Video → Biên bản bàn giao AWB`).
  - Add a **Visual Process Stepper Banner** at the top of Outbound screens showing exactly which step the order is in and providing a 1-click "Chuyển bước tiếp theo (Next Step)" button.
- **Workflow Before → After:** 8 disconnected outbound menu items → Guided 2-Step (Express) or 5-Step (Full WMS) pipeline with 1-click transition buttons.
- **UI Changes:** Add `OutboundPipelineStepper` component; filter sidebar items by active Outbound Mode (`EXPRESS` vs `FULL`).
- **Data / API Impact:** Uses existing backend endpoints; adds `OutboundFlowMode` preference to Warehouse/Tenant config.
- **Roles Affected:** `Manager`, `PickerPacker`, `WarehouseStaff`.
- **Dependencies:** None.
- **Acceptance Criteria & Tests:**
  - In `EXPRESS` mode, sidebar shows only `Phiếu xuất kho` and `Biên bản bàn giao AWB`; in `FULL` mode, full pipeline stepper guides user from `SO → Pick → Pack → Ship → Handover`.

---

### `FND-015` — Missing Statutory Vietnamese Stock Card (`Thẻ Kho Mẫu S12-DN`) Export
- **Module:** Reporting & Inventory (`Inventory.tsx`, `ReportsController.cs`)
- **Gap Category:** `REPORTING_GAP`
- **Priority:** 🟠 **P1** | **Complexity:** `S` | **Risk:** `LOW`
- **Current Behavior (`AS-IS`):** `ReportsController` exports `BaoCaoTonKho_*.xlsx` and `BaoCaoXuatNhapTon_*.xlsx`, and `Inventory.tsx` Tab 3 displays `InventoryTransactions`. However, there is no endpoint or button to export a per-SKU chronological **Thẻ Kho (Mẫu S12-DN — Thông tư 200/2014/TT-BTC)** showing Opening Balance, each Inbound/Outbound voucher number & date, transaction qty, and running `ClosingBalance` after each row.
- **Evidence (`FACT`):** [`ReportsController.cs:289-337`](file:///d:/ERP_KHO/ERP.Api/Controllers/ReportsController.cs#L289-L337), [`Inventory.tsx:350-370`](file:///d:/ERP_KHO/frontend/src/pages/Inventory.tsx#L350-L370)
- **Benchmark Reference:** MISA AMIS Kho, KiotViet, Sapo, OpenBoxes (`Electronic Stock Card`).
- **Business Problem:** Every Vietnamese business migrating from Excel or audited by tax authorities requires `Thẻ Kho Mẫu S12-DN` per SKU.
- **Proposed `TO-BE` Design:** Add `GET /api/v1/reports/stock-card` and `GET /api/v1/reports/stock-card/export` (`.xlsx` via `ClosedXML` + printable view in `Inventory.tsx` Tab 3) computed directly from immutable `InventoryTransactions`.
- **Workflow Before → After:** Manual Excel calculation from transaction list → 1-click `Xuất Thẻ Kho (S12-DN .xlsx / In PDF)` in `Inventory.tsx`.
- **UI Changes:** Add "Xuất Thẻ Kho (Mẫu S12-DN)" button in `Inventory.tsx` Transaction History tab when a SKU + Warehouse are selected.
- **Data / API Impact:** New read-only query method on `IReportService`.
- **Roles Affected:** `Accountant`, `Manager`, `Inventory Controller`.
- **Dependencies:** None.
- **Acceptance Criteria & Tests:**
  - Integration test: Export Stock Card `.xlsx` for SKU with 3 receipts and 2 dispatches; verify running balance column and final balance match `InventoryStock.Quantity`.

---

### `FND-016` — Refresh Token Cookie `Path = "/api/Auth"` Incompatible with `/api/v1/auth`
- **Module:** Authentication (`ERP.Api/Controllers/AuthController.cs`)
- **Gap Category:** `SECURITY_OR_PERMISSION_GAP` / `INTEGRATION_RELIABILITY_GAP`
- **Priority:** 🟡 **P2** | **Complexity:** `S` | **Risk:** `LOW`
- **Current Behavior (`AS-IS`):** `AuthController` declares `[Route("api/[controller]")]` and `[Route("api/v1/auth")]` (`lines 14-15`), but `CookieOptions` hardcodes `Path = "/api/Auth"` (`line 136`). When the frontend transitions to `/api/v1/auth/refresh` or `/api/v1/auth/logout`, the browser will not send the `erp_refresh` cookie!
- **Evidence (`FACT`):** [`AuthController.cs:14-15, 136`](file:///d:/ERP_KHO/ERP.Api/Controllers/AuthController.cs#L131-L139)
- **Benchmark Reference:** RFC 6265 Cookie Path matching rules.
- **Business Problem:** Silent 401 logout loops if client calls `/api/v1/auth/refresh`.
- **Proposed `TO-BE` Design:** Change `Path = "/api/Auth"` to `Path = "/"` (or `"/api"`) in `AuthController.CookieOptions`.
- **Workflow Before → After:** Cookie restricted to legacy `/api/Auth` → Cookie sent on both `/api/v1/auth` and `/api/Auth`.
- **UI Changes:** None.
- **Data / API Impact:** 1-line change in `AuthController.cs:136`.
- **Roles Affected:** All authenticated users.
- **Dependencies:** None.
- **Acceptance Criteria & Tests:**
  - Integration test: Login via `/api/v1/auth/login` → call `/api/v1/auth/refresh` using returned `Set-Cookie` → succeeds with `200 OK`.

---

### `FND-017` — Kitting / Bundle Work Orders (`KitWorkOrder`) Lack Standalone Lifecycle Visibility & BOM Validation
- **Module:** Value-Added Services / Kitting (`PackingSessions.tsx`, `Products.tsx`, `KitWorkOrder.cs`)
- **Gap Category:** `INCOMPLETE_WORKFLOW`
- **Priority:** 🟡 **P2** | **Complexity:** `M` | **Risk:** `LOW`
- **Current Behavior (`AS-IS`):** `KitWorkOrder` and `KitWorkOrderComponentLine` exist in `ERP.Domain/Entities/KitWorkOrder.cs`, and Kitting/De-kitting actions are split between `Products.tsx` Tab 4 ("Bundle & Gift") and `PackingSessions.tsx` Tab 3 ("Kitting/Video"). `KitWorkOrder.Status` is a raw string (`"Draft"`), and there is no clear BOM reservation → component pick → assembly completion → FEFO expiry roll-up (`EarliestComponentExpiryDate`) workflow view.
- **Evidence (`FACT`):** [`KitWorkOrder.cs:11-51`](file:///d:/ERP_KHO/ERP.Domain/Entities/KitWorkOrder.cs#L11-L51), `FRONTEND_AUDIT.md:456, 540`
- **Benchmark Reference:** Logiwa IO (DTC promotional kitting), ERPNext (`Stock Entry: Repack/Manufacture`).
- **Business Problem:** E-commerce sellers running Shopee/TikTok promotional combos (e.g., "Mua 2 tặng 1" or "Combo 3 hộp") need a unified place to define BOMs (in `Products`) and execute Kit Assembly/Disassembly with automatic `EarliestComponentExpiryDate` and `RolledUpUnitCost` inheritance.
- **Proposed `TO-BE` Design:** Consolidate BOM definition in `Products.tsx` (Tab 4) and Kit Work Order execution inside Inventory Control (`Chế biến / Đóng gói Combo`), enforcing typed `KitWorkOrderStatus` enum and Base-UOM component snapshots.
- **Workflow Before → After:** Split UI between Products and Packing Sessions → Clear BOM Master (in Products) + Kit Assembly Execution (in Inventory/VAS) with atomic component deduction and kit receipt.
- **UI Changes:** Add clear link between `Products` Bundle Tab and Kit Execution workbench.
- **Data / API Impact:** `DM-02` enum & Base-UOM snapshot enhancement on `KitWorkOrder`.
- **Roles Affected:** `Manager`, `WarehouseStaff`.
- **Dependencies:** `DM-02`.
- **Acceptance Criteria & Tests:**
  - SQL test: Assemble 10 Kits from Component A (Expiry `2027-03-01`, Cost `20,000`) and Component B (Expiry `2027-01-15`, Cost `30,000`) → Kit bucket receives `ExpiryDate = 2027-01-15` and `MovingAverageCost = 50,000`.

---

### `FND-018` — Missing Automated Min/Max Pick-Face Replenishment Rules
- **Module:** Inventory & Putaway (`WarehouseLocations.tsx`, `InventoryMovements.tsx`)
- **Gap Category:** `MISSING_CAPABILITY`
- **Priority:** 🟡 **P2** | **Complexity:** `M` | **Risk:** `LOW`
- **Current Behavior (`AS-IS`):** Spec 17 §15 defines `replenishment.read`, `replenishment.generate`, `replenishment.assign`, `replenishment.execute`, and `PickingTasks.tsx` shows short-pick handling, but there is no configurable `MinQty / MaxQty` per Pick-Face Bin to automatically trigger a bulk-rack-to-pick-face replenishment movement before a picker encounters a short-pick!
- **Evidence (`FACT`):** Spec 17 §15 vs `ERP.Domain/Entities/` (no `ReplenishmentRule` entity).
- **Benchmark Reference:** Odoo (`Reordering / Replenishment Rules`), NetSuite WMS (`Bulk-to-Pickface Replenishment`), D365 (`Min/Max Replenishment`).
- **Business Problem:** In multi-level racks (Floor Level 1 = Pick Face, Levels 2–4 = Bulk Reserve), pickers hit `Short Pick` when Level 1 runs dry even though pallets sit on Level 3.
- **Proposed `TO-BE` Design:** Add `MinBaseQty` and `MaxBaseQty` fields to `WarehouseLocation` (or `LocationSkuReplenishmentRule`) and a 1-click "Sinh lệnh bổ sung kệ nhặt (Generate Replenishment Tasks)" action in `InventoryMovements.tsx` / `PutawayTasks.tsx`.
- **Workflow Before → After:** Reactive short-pick during picking → Proactive Min/Max replenishment movement from Reserve Bin to Pick-Face Bin.
- **UI Changes:** Add "Bổ sung kệ nhặt (Replenishment)" tab inside `InventoryMovements.tsx` / `PutawayTasks.tsx`.
- **Data / API Impact:** Additive columns/table `LocationReplenishmentRules` (`DM-03`).
- **Roles Affected:** `Manager`, `Supervisor`, `WarehouseStaff`.
- **Dependencies:** `DM-03` (Phase 2).
- **Acceptance Criteria & Tests:**
  - When Pick-Face Bin `A-01-01` drops below `MinBaseQty = 20`, `POST /api/v1/replenishments/generate` creates a directed movement from Bulk Bin `A-01-03` (FEFO earliest lot) to `A-01-01` up to `MaxBaseQty = 100`.

---

### `FND-019` — PDA Mobile Bottom Navigation Missing Packing/Handover & Internal Move Shortcuts
- **Module:** Mobile / PDA Floor Experience (`MainLayout.tsx`)
- **Gap Category:** `MOBILE_SCANNER_GAP` / `UI_UX_FRICTION`
- **Priority:** 🟠 **P1** | **Complexity:** `S` | **Risk:** `LOW`
- **Current Behavior (`AS-IS`):** `MainLayout.tsx:88-94` defines 5 PDA bottom nav items: `Tổng quan` (`/`), `Nhận hàng` (`/import-receipts`), `Cất hàng` (`/putaway-tasks`), `Lấy hàng` (`/picking-tasks`), and `Kiểm kê` (`/stocktakes`). However, warehouse floor operators using handheld PDAs also perform **Chuyển vị trí (Internal Bin Move — `/inventory-movements`)** and **Bàn giao ĐVVC / Quét AWB (`/handover-manifests`)** on the dock floor! Meanwhile, `Tổng quan` (a heavy 904-line executive dashboard) is rarely used on a 4-inch PDA scanner screen.
- **Evidence (`FACT`):** [`MainLayout.tsx:88-94`](file:///d:/ERP_KHO/frontend/src/layouts/MainLayout.tsx#L88-L94)
- **Benchmark Reference:** OCA Shopfloor, GreaterWMS OneAPP, Sentry WMS mobile app (task-only launcher grid on handhelds).
- **Business Problem:** Dock workers scanning 500 Shopee/GHN parcels into a courier truck on a handheld PDA have no bottom-nav shortcut to `Biên bản bàn giao (/handover-manifests)` or `Chuyển vị trí (/inventory-movements)`.
- **Proposed `TO-BE` Design:** Replace `Tổng quan` on the mobile/PDA bottom bar with a **Floor Launcher Hub (`Tác vụ Kho`)** that gives big-button (`>= 56px` touch target) 1-tap access to all 6 physical floor operations: (1) Nhận hàng, (2) Cất hàng, (3) Lấy hàng, (4) Đóng gói & Bàn giao AWB, (5) Chuyển vị trí, (6) Kiểm kê & Hàng hoàn.
- **Workflow Before → After:** 904-line executive dashboard on 4-inch PDA → 6-tile Scan-First Floor Launcher on mobile viewports (`<= 768px`).
- **UI Changes:** Responsive mobile launcher view in `MainLayout.tsx` / `Dashboard.tsx`.
- **Data / API Impact:** None.
- **Roles Affected:** `Receiver`, `PickerPacker`, `WarehouseStaff`.
- **Dependencies:** None.
- **Acceptance Criteria & Tests:**
  - At `390x844` viewport, all 6 floor operations are reachable in `<= 1` tap with `0px` horizontal overflow.

---

### `FND-020` — CSS Token Fragmentation Across 20 Vanilla CSS Files Without Central `design-tokens.css`
- **Module:** Frontend Design System (`frontend/src/ui/`, `frontend/src/pages/*.css`)
- **Gap Category:** `UI_UX_FRICTION`
- **Priority:** 🟡 **P2** | **Complexity:** `M` | **Risk:** `LOW`
- **Current Behavior (`AS-IS`):** Styling is split across `index.css`, `App.css`, `production-ui.css`, `MainLayout.css`, and 17 page-specific `.css` files using hardcoded hex colors and spacing values rather than a 3-layer CSS custom property token system (`Primitive → Semantic → Component`).
- **Evidence (`FACT`):** [`FRONTEND_AUDIT.md:169-175, 796`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/FRONTEND_AUDIT.md#L169-L175)
- **Benchmark Reference:** Skill `design-system` (`references/token-architecture.md`).
- **Business Problem:** Inconsistent badge colors, table cell paddings, and focus ring styles across pages.
- **Proposed `TO-BE` Design:** Introduce `frontend/src/ui/design-tokens.css` with 3-layer CSS variables (`--color-*`, `--space-*`, `--font-mono`, `--radius-*`, `--status-*`) imported into `production-ui.css`, enforcing `font-variant-numeric: tabular-nums` on all numeric/quantity/currency cells (`[Rule Role 6]`).
- **Workflow Before → After:** Ad-hoc hex colors in 20 CSS files → Centralized 3-layer CSS token system.
- **UI Changes:** Tokenized styles in `production-ui.css` (Deliverable L).
- **Data / API Impact:** None.
- **Roles Affected:** All users.
- **Dependencies:** None.
- **Acceptance Criteria & Tests:**
  - All numeric table cells render with `font-variant-numeric: tabular-nums` and pass WCAG 2.1 AA contrast (`>= 4.5:1`).

---

### `FND-021` — Over-Engineered Enterprise Telemetry Panels Visible to Single-Warehouse ICP (`WCS Robotics / RFID / Voice Pick` & `Multi-Warehouse Control Tower`)
- **Module:** Picking, Dock/Yard & Dashboard (`PickingTasks.tsx`, `Dashboard.tsx`)
- **Gap Category:** `OVERENGINEERED_FOR_ICP`
- **Priority:** 🔵 **P3 / DEFERRED** | **Complexity:** `S` | **Risk:** `LOW`
- **Current Behavior (`AS-IS`):**
  - `PickingTasks.tsx` renders an "Automation Telemetry Panel (WCS robotics, RFID IoT, voice pick)" (`FRONTEND_AUDIT.md:433`).
  - `Dashboard.tsx` renders a prominent "Multi-Warehouse Control Tower" table even when a tenant only has `1` warehouse (`ICP: 1 warehouse, 5–30 staff`).
- **Evidence (`FACT`):** [`PickingTasks.tsx`](file:///d:/ERP_KHO/frontend/src/pages/PickingTasks.tsx), [`Dashboard.tsx:269`](file:///d:/ERP_KHO/frontend/src/pages/Dashboard.tsx#L269)
- **Benchmark Reference:** Product Principle Section 3 ("Không biến hệ thống thành ERP enterprise phức tạp không cần thiết").
- **Business Problem:** Clutters the screen for a 1-warehouse Shopee/TikTok seller who does not own WCS conveyors, AMRs, or RFID portals, reducing trust and screen real estate for actual picking lines.
- **Proposed `TO-BE` Design:**
  - Hide `WCS Robotics / RFID / Voice Pick` panel behind a tenant feature flag (`enableWcsAutomationTelemetry: false` by default).
  - Auto-collapse or hide `Multi-Warehouse Control Tower` on `Dashboard.tsx` when `warehouses.length <= 1`.
- **Workflow Before → After:** Always-visible enterprise hardware telemetry → Clean, focused picking table for 1-warehouse ICP (opt-in via feature flag for enterprise clients).
- **UI Changes:** Conditional rendering based on `warehouses.length > 1` and feature flag.
- **Data / API Impact:** None (keep backend capabilities intact, declutter UI).
- **Roles Affected:** `PickerPacker`, `Manager`, `Owner`.
- **Dependencies:** None.
- **Acceptance Criteria & Tests:**
  - Component test: When tenant has 1 warehouse and `enableWcsAutomationTelemetry == false`, neither panel renders.

---

### `FND-022` — Packing Station Weight/Dimension Variance vs Carrier Shipping Fee Overcharge Linkage
- **Module:** Packing & COD Reconciliation (`PackingSessions.tsx`, `CodReconciliation.tsx`)
- **Gap Category:** `INCOMPLETE_WORKFLOW` / `REPORTING_GAP`
- **Priority:** 🟡 **P2** | **Complexity:** `S` | **Risk:** `LOW`
- **Current Behavior (`AS-IS`):** `PackingSessions.tsx` records HU weight/volume (`GrossWeightKg`, dimensions) and `PackingVideoEvidence`, while `CodReconciliation.tsx` verifies `EstimatedShippingFee` vs `ActualShippingFee` (`ReportsController.cs:342`). However, when a carrier (GHN/GHTK/J&T) overcharges shipping weight fees on `CodReconciliation`, the UI does not link directly to the recorded `HandlingUnit` weight + `PackingVideoEvidence` to file a carrier dispute claim!
- **Evidence (`FACT`):** [`PackingSessions.tsx`](file:///d:/ERP_KHO/frontend/src/pages/PackingSessions.tsx), [`CodReconciliation.tsx`](file:///d:/ERP_KHO/frontend/src/pages/CodReconciliation.tsx), [`PackingVideoEvidence.cs`](file:///d:/ERP_KHO/ERP.Domain/Entities/PackingVideoEvidence.cs)
- **Benchmark Reference:** ShipHero (`ParcelView` photo/video dispute evidence linked to carrier billing).
- **Business Problem:** Vietnamese e-commerce sellers lose 2–5% margin to carrier dimensional weight overcharges and fraudulent "empty box" return claims; they already capture packing video/weight in ERP_KHO, so linking it directly to COD/Fee reconciliation unlocks massive ROI!
- **Proposed `TO-BE` Design:** In `CodReconciliation.tsx` and `CustomerReturns.tsx`, add a 1-click "Xem bằng chứng Đóng gói (Cân nặng & Video)" drawer linking `TrackingNumber (AWB)` → `ExportReceipt / Shipment` → `PackingSession` → `HandlingUnit.GrossWeightKg` & `PackingVideoEvidence`.
- **Workflow Before → After:** Manually searching packing sessions by order code during carrier dispute → 1-click evidence popup directly from COD variance row or RMA row.
- **UI Changes:** Add evidence link badge on `CodReconciliation.tsx` variance rows.
- **Data / API Impact:** Include `PackedGrossWeightKg` and `PackingVideoEvidenceUrl` in `CodSettlementItemDto` projection.
- **Roles Affected:** `Accountant`, `Manager`, `Owner`.
- **Dependencies:** None.
- **Acceptance Criteria & Tests:**
  - UI/API test: COD variance row with `ActualShippingFee > EstimatedShippingFee` displays recorded HU weight and video evidence reference.

---

### `FND-023` — Resource Authorization Must Precede Durable Idempotency Claim Creation
- **Module:** Idempotency & Authorization Pipeline (`ERP.Api/Infrastructure`, `ERP.Api/Authorization`)
- **Gap Category:** `SECURITY_OR_PERMISSION_GAP` / `DATA_INTEGRITY_RISK`
- **Priority:** 🟠 **P1** | **Complexity:** `S` | **Risk:** `MEDIUM`
- **Current Behavior (`AS-IS`):** Notion Spec 17 §33.5 mandates that authentication, database permission verification, resource lookup, warehouse scope, and maker/checker state checks must execute **before** creating a durable idempotency claim (or any claim must roll back on `403/404/409/422` denial). While `PermissionAuthorizeAttribute` has `Order = int.MinValue` (`line 58`), warehouse scope and maker/checker checks run inside the controller/service *after* the action filter pipeline begins.
- **Evidence (`FACT`):** [`PermissionAuthorizeAttribute.cs:58`](file:///d:/ERP_KHO/ERP.Api/Authorization/PermissionAuthorizeAttribute.cs#L58), Notion Spec 17 §33.5.
- **Benchmark Reference:** Stripe / IETF Idempotency-Key specification & Notion Spec 17 §33.5.
- **Business Problem:** If a request fails warehouse scope (`404`) or maker/checker separation (`403`) inside the service, the idempotency filter must never persist a durable claim or cache a denied response that blocks a legitimate user or retries after permission grant.
- **Proposed `TO-BE` Design:** Ensure `IdempotentCommandFilter` only commits `IdempotencyRecord` on `2xx` success inside the unit-of-work transaction (or deletes/rolls back provisional claims whenever the action returns `>= 400`).
- **Workflow Before → After:** Provisional claim risk on `4xx` → Strictly atomic commit of `IdempotencyRecord` only on authorized result.
- **UI Changes:** None.
- **Data / API Impact:** Filter guard verification in `IdempotentCommand` infrastructure.
- **Roles Affected:** All roles.
- **Dependencies:** None.
- **Acceptance Criteria & Tests:**
  - Integration test: User without warehouse access calls `POST /api/v1/import-receipts/{id}/post` with `Idempotency-Key: K1` → receives `404` → Admin grants warehouse access → User retries with same `K1` → succeeds `200 OK`.

---

### `FND-024` — Missing Global React Error Boundary & Unhandled Promise Recovery Banner
- **Module:** Frontend Resilience (`frontend/src/routes/AppRoutes.tsx`)
- **Gap Category:** `UI_UX_FRICTION`
- **Priority:** 🟡 **P2** | **Complexity:** `S` | **Risk:** `LOW`
- **Current Behavior (`AS-IS`):** `AppRoutes.tsx` wraps lazy-loaded routes in `<Suspense>`, but there is no React `<ErrorBoundary>` around route rendering (`FRONTEND_AUDIT.md:802`). If a network chunk fails to load during a Vite deployment or a component throws a render error, the user sees a blank white screen.
- **Evidence (`FACT`):** [`AppRoutes.tsx:77-133`](file:///d:/ERP_KHO/frontend/src/routes/AppRoutes.tsx#L77-L133), `FRONTEND_AUDIT.md:802`
- **Benchmark Reference:** Role 6 & Role 7 React 19 SPA resilience standards.
- **Business Problem:** A warehouse operator on the floor during a deployment chunk-hash update gets a blank screen instead of an automatic reload / Vietnamese error recovery card with Zalo support diagnostics.
- **Proposed `TO-BE` Design:** Wrap `<Suspense>` inside `MainLayout.tsx` / `AppRoutes.tsx` with `<WorkspaceErrorBoundary>` that catches chunk-load and render exceptions, displays a Vietnamese recovery card, and offers 1-click "Tải lại trang" + "Sao chép mã chẩn đoán (CorrelationId)".
- **Workflow Before → After:** Blank screen on chunk/render crash → Graceful Vietnamese recovery panel inside `MainLayout`.
- **UI Changes:** Add `WorkspaceErrorBoundary.tsx` (approx. 60 lines).
- **Data / API Impact:** None.
- **Roles Affected:** All users.
- **Dependencies:** None.
- **Acceptance Criteria & Tests:**
  - Vitest test: Throwing an error inside a child route renders the Vietnamese recovery banner with `data-testid="workspace-error-boundary"`.
