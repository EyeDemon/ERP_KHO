# OWNER DECISION PROPOSALS (DELIVERABLE S)

**Project:** ERP_KHO — Cloud ERP & WMS SaaS  
**Document ID:** `DELIVERABLE-S`  
**Audit Run:** `2026-11-01_REASSESSMENT_001`  
**Mode:** READ-ONLY SYSTEM AUDIT + TO-BE DESIGN PROPOSAL (`PENDING OWNER DECISION`)  
**Baseline Branch / SHA:** `feat/WMS-61-project-development` (`6e0a333390c1db6222e55ae3945b7ce33a56bef6`)

> [!IMPORTANT]
> **Compliance with Rule `[WF-003]` & `[DOC-001]`:**
> As an independent advisory council, we do **not** make unilateral changes to DB Schema, REST API `/api/v1` contracts, RBAC Permission Codes, or Notion Specifications. All 8 Architecture & Product Decisions below are formally submitted as **`PENDING DECISION`** for the Project Owner's review and approval.

---

## Summary of Decisions Requiring Project Owner Sign-Off

| Decision ID | Title | Impact Domain | Recommended Option | Priority | Status |
|-------------|-------|---------------|--------------------|:--------:|:------:|
| **ODP-001** | Database Persistence & Hardening of TOTP 2FA Credentials | DB Schema + Security | **Option A:** Persist encrypted TOTP secret & hashed recovery codes on `Users` table; drop client secret input & deterministic fallback | 🔴 **P0** | `PENDING DECISION` |
| **ODP-002** | Customer Returns / RMA First-Class Module & Schema Completion | DB Schema + API + UI | **Option A:** Promote `CustomerReturn` to `/api/v1/customer-returns` + dedicated `/customer-returns` UI with `RowVersion`, Base-UOM snapshots & `LotId`/`SerialId` | 🔴 **P0** | `PENDING DECISION` |
| **ODP-003** | Canonical Permission Registry Unification & Mutation Permission Cutover | RBAC + Spec 17 | **Option A:** Merge split `AppPermissions` classes, split `inventory_opening_balance.import`, and replace `.read` + `!Viewer` on `POST` endpoints | 🔴 **P0** | `PENDING DECISION` |
| **ODP-004** | Public Tenant Signup vs Admin Tenant Provisioning Separation | Security + SaaS API | **Option A:** Remove `[AllowAnonymous]` from `/api/v1/saas/tenants/provision`; create rate-limited `/api/v1/auth/register-tenant` | 🔴 **P0** | `PENDING DECISION` |
| **ODP-005** | Sidebar Navigation Consolidation (36+ Flat Links → 16 Workspace Hubs) | UX / IA | **Option A:** Consolidate sub-modules into 16 primary sidebar items with horizontal workspace tabs while preserving all 39 URLs | 🟠 **P1** | `PENDING DECISION` |
| **ODP-006** | Dual Outbound Operating Modes (Express 2-Step vs Full 5-Step WMS) | Business Process + UX | **Option A:** Warehouse-level policy toggle between `Express ExportReceipt` (2-step) and `Full E-Commerce Fulfillment` (SO → Pick → Pack → Ship → Handover) | 🟠 **P1** | `PENDING DECISION` |
| **ODP-007** | Legacy `/api/PascalCase` Route Deprecation & Pagination Contract | API `/api/v1` Contract | **Option A:** Migrate 100% of frontend calls to `/api/v1/<kebab-case-plural>` and add server-side pagination to Receipt list endpoints | 🟠 **P1** | `PENDING DECISION` |
| **ODP-008** | Min/Max Pick-Face Replenishment & Kit Work Order Schema Upgrade | DB Schema + Process | **Option A:** Add `MinBaseQty`/`MaxBaseQty` on `WarehouseLocations` (Phase 3) and typed enum/Base-UOM snapshots on `KitWorkOrder` | 🟡 **P2** | `PENDING DECISION` |

---

## Detailed Decision Briefs

---

### `ODP-001` — Database Persistence & Hardening of TOTP 2FA Credentials
- **Status:** `PENDING DECISION`
- **Related Findings:** `FND-001`, `SEC-F01`
- **Context & Evidence:**
  In `AccountSecurityController.cs:122, 216-225, 570-594`, TOTP 2FA enrollment state is stored in a static in-memory `ConcurrentDictionary` (`TotpEnrollments`), `VerifyTotp` accepts a client-supplied `Base32Secret`, and `GetTotpSecretBytes` falls back to a deterministic SHA-256 hash of `userId` and `username`.
- **Options:**
  - **Option A (Recommended):** Add encrypted TOTP columns (`TotpSecretProtected`, `TotpEnrollmentStatus`, `TotpRecoveryCodeHashesJson`, `TotpActivatedAtUtc`) to the `Users` table via an additive EF Core Migration; remove `Base32Secret`/`SecretKey` from `VerifyTotpRequest`; delete `GetTotpSecretBytes` and static `TotpEnrollments`.
  - **Option B:** Keep TOTP in-memory for demo only and disable TOTP endpoints in production via feature flag.
- **Proposed Notion Spec Diff (Spec 02 & Spec 08):**
  - Update Spec 02 (`users` table) to include MFA persistence columns and Spec 08 to prohibit client-echoed TOTP secrets.

---

### `ODP-002` — Customer Returns / RMA First-Class Module & Schema Completion
- **Status:** `PENDING DECISION`
- **Related Findings:** `FND-003`, `DM-01`
- **Context & Evidence:**
  `CustomerReturn` and `CustomerReturnLine` exist in `ERP.Domain/Entities/CustomerReturnEntities.cs`, and 3 RMA endpoints sit inside `ImportReceiptsController.cs:148-200` under `receipt.read`, with no dedicated UI page in `frontend/src/pages/`. Furthermore, `CustomerReturn` lacks `RowVersion`, Base-UOM snapshots (`[INV-005]`), and `LotId`/`SerialId` on lines.
- **Options:**
  - **Option A (Recommended):**
    1. Create additive EF Core Migration adding `RowVersion`, Base-UOM snapshot columns, `LotId`, `SerialId`, and typed enums (`CustomerReturnStatus`, `ReturnDispositionType`) to `CustomerReturns` / `CustomerReturnLines`.
    2. Extract `CustomerReturnsController` (`/api/v1/customer-returns`) protected by `return.read/create/receive/inspect/disposition/post` (already defined in Notion Spec 17 §18).
    3. Build `CustomerReturns.tsx` (`/customer-returns`) for AWB scan intake, quarantine receipt, inspection, and atomic disposition posting.
  - **Option B:** Defer dedicated Returns UI and continue handling customer returns manually via standard `ImportReceipts`.
- **Proposed Notion Spec Diff (Spec 02, Spec 16, Spec 17, Spec 25):**
  - Map `/api/v1/customer-returns` endpoints to Spec 17 §18 permission codes (`return.*`) in Spec 25 Endpoint-Permission Matrix.

---

### `ODP-003` — Canonical Permission Registry Unification & Mutation Permission Cutover
- **Status:** `PENDING DECISION`
- **Related Findings:** `FND-004`, `FND-005`, `FND-006`, `SEC-F03`, `SEC-F04`, `SEC-F05`
- **Context & Evidence:**
  1. `ERP.Domain.Security.AppPermissions` (5 constants) and `ERP.Api.Authorization.AppPermissions` (~85 constants) are split, and `InventoryOpeningBalanceImport` aliases `"inventory_movement.create"`.
  2. Over 12 `POST` endpoints in `ImportReceiptsController`, `ExportReceiptsController`, `PermissionsController`, and `SaasBillingController` use `[PermissionAuthorize(*.Read)]` + `if (User.IsInRole("Viewer")) return Forbid();`.
  3. `ReportsController` uses `[Authorize(Roles = AppRoles.AdminManagerOrViewer)]` on `inventory-in-out-stock`, blocking `Accountant`.
- **Options:**
  - **Option A (Recommended):**
    1. Unify `AppPermissions` in `ERP.Domain.Security` and introduce distinct `inventory_opening_balance.import`.
    2. Replace all `.read` + `!Viewer` checks on `POST` mutations with their canonical Spec 17 mutation permissions (`return.create`, `return.disposition`, `shipment.dispatch`, `label.print`, `report.export`, `system_config.manage`) and seed them into default role bundles.
    3. Remove `[Authorize(Roles = ...)]` from `ReportsController` in favor of `[PermissionAuthorize]`.
  - **Option B:** Retain dual-read compatibility (`IsInRole` + `.read` permission) until Phase 2.
- **Proposed Notion Spec Diff (Spec 17 §14 & §33.3):**
  - Add `inventory_opening_balance.import` to Spec 17 §14 (`Inventory Query & Control`) and seed to `Admin` and `Manager`.

---

### `ODP-004` — Public Tenant Signup vs Admin Tenant Provisioning Separation
- **Status:** `PENDING DECISION`
- **Related Findings:** `FND-002`, `SEC-F02`
- **Context & Evidence:**
  `PermissionsController.ProvisionSaaSTenant` (`lines 128-144`) has both `[AllowAnonymous]` and `[PermissionAuthorize(AppPermissions.PermissionRead)]`, causing `PermissionAuthorizationFilter` to bypass authorization completely.
- **Options:**
  - **Option A (Recommended):** Remove `[AllowAnonymous]` from `PermissionsController.ProvisionSaaSTenant` (require `system_config.manage`), and expose a dedicated rate-limited public onboarding endpoint `POST /api/v1/auth/register-tenant` for `/register`.
  - **Option B:** Disable public self-service signup (`/register`) entirely and provision tenants exclusively via Admin backoffice (recommended if B2B sales are sales-led for the first 10 customers).

---

### `ODP-005` — Sidebar Navigation Consolidation (36+ Links → 16 Workspace Hubs)
- **Status:** `PENDING DECISION`
- **Related Findings:** `FND-012`, `FND-019`
- **Context & Evidence:**
  `productionNavigation.ts` renders up to 36 sidebar items across 6 sections.
- **Options:**
  - **Option A (Recommended):** Consolidate the desktop sidebar into **16 top-level workspace items** with horizontal sub-tabs (`UiTabs`) inside consolidated hubs (e.g., grouping `Tồn kho`, `Khóa tồn`, `Di chuyển vị trí`, `Đảo bút toán`, `Truy vết`, `Đối chiếu` into a unified Inventory Control Workspace while keeping all 39 URLs working via deep-linked tabs), and convert the mobile (`<= 768px`) landing screen into a 6-tile **PDA Scan-First Floor Launcher**.
  - **Option B:** Keep all 36 items in the sidebar but make sub-groups collapsible accordions.

---

### `ODP-006` — Dual Outbound Operating Modes (Express 2-Step vs Full 5-Step WMS)
- **Status:** `PENDING DECISION`
- **Related Findings:** `FND-014`
- **Context & Evidence:**
  Both `ExportReceipts` (2-step MVP: `Approve & Reserve → Dispatch`) and the full outbound suite (`SalesOrder → Reservation → Allocation → Picking → Packing → Shipment → Handover`) appear simultaneously in the UI without guided routing.
- **Options:**
  - **Option A (Recommended):** Make Outbound Operating Mode an explicit Warehouse Policy (`EXPRESS_2_STEP` for `< 10` staff / B2B wholesale vs `FULL_ECOMMERCE_5_STEP` for high-volume Shopee/TikTok warehouses) with a visual `OutboundPipelineStepper` at the top of outbound screens.
  - **Option B:** Keep both visible simultaneously without a stepper.

---

### `ODP-007` — Legacy `/api/PascalCase` Route Deprecation & Receipt Pagination Contract
- **Status:** `PENDING DECISION`
- **Related Findings:** `FND-007`, `FND-009`, `FND-010`
- **Context & Evidence:**
  Frontend calls a mix of `/api/ImportReceipts` and `/api/v1/...`, and `GET /api/ImportReceipts` / `GET /api/ExportReceipts` return unpaginated arrays and compute pipeline summaries in C# memory (`GetAllAsync().ToList()`).
- **Options:**
  - **Option A (Recommended):**
    1. Switch 100% of frontend `apiClient` calls to `/api/v1/<kebab-case-plural>`.
    2. Convert `GetPipelineSummary` to SQL `GROUP BY Status`.
    3. Add optional `pageNumber` & `pageSize` (`<= 100`) pagination to `GET /api/v1/import-receipts` and `GET /api/v1/export-receipts`.
  - **Option B:** Keep flat array response on `/api/ImportReceipts` for legacy tests and enforce pagination only on `/api/v1/import-receipts`.

---

### `ODP-008` — Min/Max Pick-Face Replenishment & Kit Work Order Schema Upgrade
- **Status:** `PENDING DECISION`
- **Related Findings:** `FND-017`, `FND-018`, `DM-02`, `DM-03`
- **Context & Evidence:**
  `KitWorkOrder.Status` is a raw string without Base-UOM component snapshots, and pick-face bins lack `MinBaseQty` / `MaxBaseQty` replenishment thresholds.
- **Options:**
  - **Option A (Recommended for Phase 3):** Approve additive migration `DM-02` (`KitWorkOrderStatus` enum + Base-UOM component snapshots) and `DM-03` (`MinBaseQty` / `MaxBaseQty` on `WarehouseLocations` or `LocationReplenishmentRules` to generate directed replenishment tasks).
  - **Option B:** Defer both to post-go-live backlog.
