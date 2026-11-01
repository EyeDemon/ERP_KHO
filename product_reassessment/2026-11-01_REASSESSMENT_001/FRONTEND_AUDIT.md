# ERP_KHO Frontend Deep Audit

**Audit Date:** 2026-11-01  
**Auditor:** Subagent — Senior ERP/WMS Solution Architect  
**Scope:** Complete read-only analysis of `d:\ERP_KHO\frontend\`

---

## 1. Project Structure & Build Toolchain

| Aspect | Detail | File Reference |
|--------|--------|----------------|
| **Framework** | React 19.2.7 SPA | [`package.json:16-17`](file:///d:/ERP_KHO/frontend/package.json#L16-L17) |
| **Build Tool** | Vite 8.1.1 + `@vitejs/plugin-react` 6.0.3 | [`package.json:26,30`](file:///d:/ERP_KHO/frontend/package.json#L26) |
| **Language** | TypeScript 6.0.2 | [`package.json:29`](file:///d:/ERP_KHO/frontend/package.json#L29) |
| **Router** | react-router-dom 7.18.2 | [`package.json:18`](file:///d:/ERP_KHO/frontend/package.json#L18) |
| **HTTP Client** | axios 1.21.0 | [`package.json:14`](file:///d:/ERP_KHO/frontend/package.json#L14) |
| **Icons** | lucide-react 0.468.0 | [`package.json:15`](file:///d:/ERP_KHO/frontend/package.json#L15) |
| **Test Runner** | Vitest 5.0.5 + @testing-library/react 16.3.3 | [`package.json:31,21`](file:///d:/ERP_KHO/frontend/package.json#L21) |
| **Linter** | oxlint 1.71.0 | [`package.json:28`](file:///d:/ERP_KHO/frontend/package.json#L28) |

### Notable Observations

- **Vite config is minimal** — no proxy, no aliases, no code-splitting config. Code-splitting happens via `React.lazy()` per route in [`AppRoutes.tsx`](file:///d:/ERP_KHO/frontend/src/routes/AppRoutes.tsx).
- **No CSS framework** — custom CSS with vanilla class names (`.ui-page`, `.ui-card`, `.ui-badge`). No Tailwind, no shadcn.
- **No state management library** — No Redux/Zustand/Jotai. State is local `useState` per page + `localStorage` for permissions/auth.
- **No i18n library** — Vietnamese strings are hardcoded directly in components.
- **Zero runtime dependencies beyond 4 packages**: React, React DOM, React Router, Axios, Lucide-React.

---

## 2. Route Map (39 Navigable Routes)

All routes defined in [`AppRoutes.tsx`](file:///d:/ERP_KHO/frontend/src/routes/AppRoutes.tsx#L77-L133).
Every page-level component is lazy-loaded via `React.lazy()` (lines 8–46).

| Route | Component | Permission Guard | Notes |
|-------|-----------|------------------|-------|
| `/login` | Login | None | Public |
| `/register` | OnboardingWizard | None | Public SaaS signup |
| `/` (index) | Dashboard | None (in layout) | Default landing |
| `/dashboard` | Dashboard | None | Same component |
| `/products` | Products | `product.read` | PermissionRoute |
| `/categories` | Categories | `product.read` | PermissionRoute |
| `/warehouses` | Warehouses | `warehouse.read` | PermissionRoute |
| `/warehouse-structure` | WarehouseLocations | `location.read` | PermissionRoute |
| `/warehouse-locations` | → redirect `/warehouse-structure` | — | Legacy redirect |
| `/warehouse-map` | WarehouseMap | `location.read` | PermissionRoute |
| `/warehouse-calendar` | WarehouseCalendar | `warehouse.read` | PermissionRoute |
| `/dock-yard` | DockYard | `dock_appointment.read` | PermissionRoute |
| `/units` | Units | `uom.read` | PermissionRoute |
| `/business-partners` | BusinessPartners | `partner.read` | PermissionRoute |
| `/partner-debts` | PartnerDebts | `partner.read` | PermissionRoute |
| `/export-receipts` | ExportReceipts | `export_receipt.read` | PermissionRoute |
| `/purchase-orders` | PurchaseOrders | `purchase_order.read` | PermissionRoute |
| `/asns` | Asns | `asn.read` | PermissionRoute |
| `/import-receipts` | ImportReceipts | `receipt.read` | PermissionRoute |
| `/inventory` | Inventory | `inventory.read` | PermissionRoute |
| `/inventory-locks` | InventoryLocks | `inventory_lock.read` | PermissionRoute |
| `/inventory-movements` | InventoryMovements | `inventory.read` | PermissionRoute |
| `/inventory-reversals` | InventoryReversals | `inventory_ledger.read` | PermissionRoute |
| `/inventory-traceability` | InventoryTraceability | `inventory_traceability.read` | PermissionRoute |
| `/inventory-reconciliation` | InventoryReconciliation | `inventory_ledger.read` | PermissionRoute |
| `/stocktakes` | Stocktakes | StocktakeRoute (custom) | role+permission gate |
| `/stock-transfers` | StockTransfers | `inventory.read` | PermissionRoute |
| `/stock-reservations` | StockReservations | `inventory.read` | PermissionRoute |
| `/stock-allocations` | StockAllocations | `allocation.read` | PermissionRoute |
| `/picking-tasks` | PickingTasks | `picking.read` | PermissionRoute |
| `/packing-sessions` | PackingSessions | `packing.read` | PermissionRoute |
| `/shipments` | Shipments | `shipment.read` | PermissionRoute |
| `/handover-manifests` | HandoverManifests | `inventory.read` | PermissionRoute |
| `/backorders` | OutboundDemand | `backorder.read \| sales_order.read` | OR logic |
| `/outbound-demand` | → redirect `/backorders` | — | Legacy redirect |
| `/cod-reconciliation` | CodReconciliation | `inventory.cost.read` | Financial RBAC |
| `/cost-valuation` | CostValuation | `inventory.cost.read` | Financial RBAC |
| `/misa-exports` | CostValuation (reused) | `inventory.cost.read` | Alias |
| `/onboarding-wizard` | OnboardingWizard | `permission.read` | PermissionRoute |
| `/saas-billing` | SaasBilling | `permission.read` | PermissionRoute |
| `/approvals` | Approvals | ApprovalRoute (custom) | permission+role gate |
| `/putaway-tasks` | PutawayTasks | `putaway.read` | PermissionRoute |
| `/permissions` | Permissions | `permission.read` | PermissionRoute |
| `/users` | → redirect `/permissions` | — | Legacy redirect |
| `/roles` | → redirect `/permissions` | — | Legacy redirect |
| `/warehouse-scopes` | → redirect `/permissions` | — | Legacy redirect |
| `*` | NotFound | — | 404 catch-all |

### Permission Route Mechanism
- [`PermissionRoute`](file:///d:/ERP_KHO/frontend/src/routes/AppRoutes.tsx#L60-L66): Calls `hasPermission(code)` from localStorage permissions set. Renders `<AccessDenied />` if no match.
- Supports array of permissions with OR logic (line 62–64).
- `StocktakeRoute` (line 68–71): Special gate for demo runtime OR stocktake permissions.
- `ApprovalRoute` (line 72–75): Uses `canViewApprovals()` — checks `receipt.read`, `export_receipt.read`, or Admin/Manager role.

---

## 3. Navigation / Sidebar Architecture

**Source:** [`MainLayout.tsx`](file:///d:/ERP_KHO/frontend/src/layouts/MainLayout.tsx) (476 lines)  
**Navigation config:** [`productionNavigation.ts`](file:///d:/ERP_KHO/frontend/src/config/productionNavigation.ts) (90 lines)

### 3.1 Sidebar Sections (6 groups, 31 visible items)

Defined in [`productionSections`](file:///d:/ERP_KHO/frontend/src/config/productionNavigation.ts#L61):

| Section | Items | Vietnamese Label |
|---------|-------|------------------|
| **Tổng quan** | 1 | Dashboard only |
| **Nhập kho** | 5 | Dock/Yard, PO, ASN, Import Receipts, Putaway |
| **Xuất kho** | 9 | Export Receipts, Reservations, Allocations, Picking, Packing, Shipments, Handover Manifests, Backorders |
| **Tồn kho** | 9 | Inventory, Locks, Movements, Reversals, Traceability, Reconciliation, Stocktakes, Stock Transfers |
| **Tài chính** | 3 | COD Reconciliation, Cost Valuation, Partner Debts |
| **Quản trị** | 10 | Onboarding, Billing, Products, Warehouses, Locations, Map, Calendar, Units, Partners, Approvals, Permissions |

### 3.2 Complexity Mode Toggle (EXPRESS / FULL)

[`MainLayout.tsx:109-124`](file:///d:/ERP_KHO/frontend/src/layouts/MainLayout.tsx#L109-L124): Persisted to `localStorage('wmsComplexityMode')`. In EXPRESS mode, items with `expressMode: false` are hidden from sidebar unless the user is currently on that page.

### 3.3 Mobile PDA Bottom Nav

[`MainLayout.tsx:88-94`](file:///d:/ERP_KHO/frontend/src/layouts/MainLayout.tsx#L88-L94): 5-item bottom nav bar for handheld devices:
- Tổng quan (Dashboard)
- Nhận hàng (Import Receipts)  
- Cất hàng (Putaway Tasks)
- Lấy hàng (Picking Tasks)
- Kiểm kê (Stocktakes)

Visibility is permission-gated per item. Hidden on desktop (`aria-hidden` when viewport > 768px).

### 3.4 Topbar Features

- **Page meta** (section eyebrow + title + description) from `resolveProductionPage()`.
- **Complexity mode toggle** button.
- **Help & Diagnostics** popover with 1-click copy of `TenantId|WarehouseId|UserId|Route|LastErrorCode|CorrelationId` for Zalo support.
- **Tenant badge** showing active warehouse.
- **Logout button** calling `POST /api/Auth/logout`.

### 3.5 Authentication Flow in Layout

[`MainLayout.tsx:134-179`](file:///d:/ERP_KHO/frontend/src/layouts/MainLayout.tsx#L134-L179):
1. On mount, calls `GET /api/auth/me` to fetch identity + permissions.
2. Stores `username`, `role`, `userId` in localStorage.
3. Calls `setCurrentPermissions(permissions)` to update global permission store.
4. If 401 response, clears tokens and redirects to `/login`.
5. Demo runtime skips API call and uses hardcoded `readOnlyDemoPermissions`.

---

## 4. Design System

### 4.1 Component Library — `ProductionUi.tsx`

**Source:** [`ui/ProductionUi.tsx`](file:///d:/ERP_KHO/frontend/src/ui/ProductionUi.tsx) (282 lines)

| Component | Purpose | ARIA |
|-----------|---------|------|
| `UiPage` | Page wrapper `<section className="ui-page">` | — |
| `UiPageHeader` | Title + eyebrow + description + actions | `<header>`, `<h1>` |
| `UiToolbar` | Filter bar container | — |
| `UiToolbarField` | Label + input/select wrapper | `<label>`, auto-injects `aria-label` |
| `UiCard` | Content card with title/subtitle | `<section>` |
| `UiBadge` | Status pill (neutral/success/warning/danger/info) | — |
| `UiEmptyState` | Empty data placeholder | — |
| `UiTableScroll` | Horizontal-scrollable table wrapper | — |
| `UiMetricGrid` | Dashboard metric card grid | — |
| `UiMetric` | Single metric (value + label) | `<article>` |
| `UiTabs` | Tab bar with ARIA role="tablist" | `role="tablist"`, `role="tab"`, `aria-selected` |
| `UiDrawer` | Slide-in panel (modal dialog) | `role="dialog"`, `aria-modal="true"`, `aria-labelledby`, focus trap, Escape to close |
| `UiPrintSignatures` | Print-only signature block for receipts | `aria-label` |

### 4.2 CSS Architecture

- **Global base styles:** [`index.css`](file:///d:/ERP_KHO/frontend/src/index.css), [`App.css`](file:///d:/ERP_KHO/frontend/src/App.css)
- **Design system:** [`ui/production-ui.css`](file:///d:/ERP_KHO/frontend/src/ui/production-ui.css)
- **Layout:** [`layouts/MainLayout.css`](file:///d:/ERP_KHO/frontend/src/layouts/MainLayout.css)
- **Per-page overrides:** 17 page-specific CSS files

### 4.3 Responsive Strategy

- Viewport breakpoint at **768px** ([`MainLayout.tsx:106`](file:///d:/ERP_KHO/frontend/src/layouts/MainLayout.tsx#L106)).
- Sidebar off-canvas on mobile with backdrop + hamburger toggle.
- `inert` attribute on hidden sidebar for a11y.
- Bottom nav appears on mobile, hidden on desktop.
- Skip link for keyboard navigation (`<a className="skip-link" href="#main-content">`).

---

## 5. Services / API Client Layer

### 5.1 Core API Client

**Source:** [`services/apiClient.ts`](file:///d:/ERP_KHO/frontend/src/services/apiClient.ts) (437 lines)

**Key Features:**

| Feature | Implementation | Lines |
|---------|---------------|-------|
| **Base URL** | `VITE_API_BASE_URL` env var, fallback `https://localhost:7198` | L6 |
| **Auth header** | `Bearer ${accessToken}` injected on every request | L216 |
| **Token refresh** | Single-flight refresh via `POST /api/Auth/refresh` on 401 | L151-172 |
| **Auto idempotency** | All POST/PUT/PATCH/DELETE get auto-generated `Idempotency-Key` header | L219-257 |
| **Fingerprint-based dedup** | Same method+URL+body within 5s reuses same idempotency key | L60-61, L86-102 |
| **GET response cache** | 3-second in-memory dedup for identical GET requests | L63-64 |
| **Auth/me session cache** | 60-second `sessionStorage` cache for `/api/auth/me` | L383-401 |
| **Rate limit retry** | Auto-retry up to 2× on HTTP 429, respects `Retry-After` header | L315-323 |
| **Permission refresh on 403** | Re-fetches `/api/auth/me` on any 403 to update permissions | L302-313 |
| **Error tracking** | Stores `lastErrorCode` + `lastCorrelationId` in localStorage | L292-297 |
| **Demo mode adapter** | Intercepts requests on Vercel demo domains via `demoApiAdapter` | L195-197 |
| **Idempotent replay handling** | Normalizes truncated replay responses | L266-284 |

### 5.2 Authorization Service

**Source:** [`services/authorization.ts`](file:///d:/ERP_KHO/frontend/src/services/authorization.ts) (45 lines)

- Permissions stored in `localStorage('permissions')` as JSON array.
- `hasPermission(code)` checks array inclusion.
- `usePermissionSet()` hook uses `useSyncExternalStore` for reactive updates via `permissions-changed` event.
- Revision counter prevents stale permission updates from concurrent requests.
- `canViewStocktakes()`: checks `stocktake.read` OR `inventory.read` OR non-Viewer role.
- `canViewApprovals()`: checks `receipt.read` OR `export_receipt.read` OR Admin/Manager role.

### 5.3 Idempotency Service

**Source:** [`services/idempotency.ts`](file:///d:/ERP_KHO/frontend/src/services/idempotency.ts) (20 lines)

- `idempotencyKeyFor(logicalAction)` — returns stable UUID for a logical action, reuses same key until `completeIdempotentAction()` is called.
- `idempotencyHeaders(logicalAction)` — convenience wrapper returning `{'Idempotency-Key': key}`.

### 5.4 Runtime Mode Service

**Source:** [`services/runtimeMode.ts`](file:///d:/ERP_KHO/frontend/src/services/runtimeMode.ts) (38 lines)

- `isReadOnlyDemoRuntime()` — true when hostname matches `erp-wms-*-demo*.vercel.app`.
- Exports `readOnlyDemoPermissions` array (37 read-only permissions for demo mode).

### 5.5 Permission Presentation Service

**Source:** [`services/permissionPresentation.ts`](file:///d:/ERP_KHO/frontend/src/services/permissionPresentation.ts) (141 lines)

- `permissionLabel(code)` — generates Vietnamese label from permission code (e.g., `receipt.post` → "Ghi nhận tồn kho từ phiếu nhập").
- `permissionDescription(code)` — longer description.
- `roleLabel(role)` — Vietnamese role name.
- `permissionError(err)` — extracts error message from API response with status-aware fallbacks.
- `conflictErrorMessage(err)` — specific handling for 409 Conflict with `errorCode` extraction.

---

## 6. Page-by-Page Audit

### 6.1 Dashboard (`/`)

**File:** [`Dashboard.tsx`](file:///d:/ERP_KHO/frontend/src/pages/Dashboard.tsx) (904 lines, 41KB)

**Purpose:** Operational command center with KPIs, task queues, exceptions, search, and financial summary.

**API Endpoints Called:**
- `GET /api/Reports/operational-dashboard` — warehouse/product/inventory counts
- `GET /api/Reports/warehouse-tasks` — open task queue
- `GET /api/Reports/operational-exceptions` — exception alerts
- `GET /api/Reports/multi-warehouse-control-tower` — multi-warehouse health
- `GET /api/Reports/global-search?q=` — enterprise-wide search
- `GET /api/v1/reports/owner-gross-profit` — financial summary
- `GET /api/v1/reports/cod-reconciliation` — COD summary
- `POST /api/Reports/operational-exceptions/acknowledge` — acknowledge exceptions

**Key UI Sections:**
1. **Global Search** — text search across all entities (Products, Locations, Receipts, etc.)
2. **Quick Links** — 4 shortcut cards (Nhập kho, Tồn kho, Đối chiếu, Điều chuyển)
3. **Dashboard Metrics** — warehouse count, product count, inventory bucket count, invalid stock, open tasks
4. **Exception Alerts** — operational exceptions with acknowledge button
5. **Multi-Warehouse Control Tower** — per-warehouse health status table (onHand, reserved, putaway/picking counts)
6. **Task Engine Queues** — open task counts by type with filter
7. **Recent Activity Feed** — audit log with Vietnamese action labels
8. **Owner Finance** — net revenue, COGS, gross profit, gross margin %, COD pending/received
9. **E-commerce Integration** — marketplace SKU sync, channel shop mapping
10. **Navigation Hub** — all 31 nav items as quick-access cards

**States:** Loading, Error, Empty states properly handled.

**Dashboard Sub-components:**
- [`DashboardMetrics.tsx`](file:///d:/ERP_KHO/frontend/src/components/dashboard/DashboardMetrics.tsx) — 4,475 bytes
- [`DashboardRecentActivity.tsx`](file:///d:/ERP_KHO/frontend/src/components/dashboard/DashboardRecentActivity.tsx) — 2,874 bytes
- [`DashboardExceptionAlerts.tsx`](file:///d:/ERP_KHO/frontend/src/components/dashboard/DashboardExceptionAlerts.tsx) — 7,239 bytes
- [`DashboardQuickActions.tsx`](file:///d:/ERP_KHO/frontend/src/components/dashboard/DashboardQuickActions.tsx) — 1,491 bytes

---

### 6.2 Import Receipts / Phiếu Nhập Kho (`/import-receipts`)

**File:** [`ImportReceipts.tsx`](file:///d:/ERP_KHO/frontend/src/pages/ImportReceipts.tsx) (1,991 lines, 128KB — **LARGEST PAGE**)

**Purpose:** Full inbound receipt lifecycle — create, receive, QC inspect, resolve discrepancies, post to inventory, print, cancel.

**API Endpoints:**
- `GET /api/ImportReceipts` — list with pagination
- `POST /api/ImportReceipts` — create draft
- `PUT /api/ImportReceipts/{id}` — update draft
- `POST /api/ImportReceipts/{id}/receive-line/{lineId}` — receive quantities
- `POST /api/ImportReceipts/{id}/approve` — approve receipt
- `POST /api/ImportReceipts/{id}/post` — post to inventory (with idempotency)
- `POST /api/ImportReceipts/{id}/cancel` — cancel receipt
- `GET /api/ImportReceipts/{id}/discrepancies` — receiving discrepancies
- `POST /api/ImportReceipts/{id}/discrepancies/{discId}/resolve` — resolve discrepancy
- `GET /api/v1/quality/disposition-reason-codes` — QC reason codes
- Various product, warehouse, supplier lookups

**Key UI Elements:**
- Receipt list table with status badges (Nháp, Đã duyệt, Đã ghi sổ, Đã hủy)
- Create/Edit form with product lines, supplier selection, UOM conversion
- **Receiving worksheet** — per-line received/accepted/damaged/rejected quantities
- **QC inspection panel** — quality check results per line
- **Discrepancy resolution** — observation vs expected with door rejection
- **Print preview** via `ReceiptPrintPreview` component
- **Inventory Identity Editor** — lot/serial/expiry per line via `ReceiptInventoryIdentityEditor`
- **Mobile barcode scanner** integration via `MobileBarcodeScannerBar`

**Permission checks:**
- `receipt.read`, `receipt.create`, `receipt.receive`, `receipt.approve`, `receipt.post`, `receipt.cancel`
- `quality_inspection.execute`, `quality_disposition.submit`, `receiving_discrepancy.resolve`

> **⚠️ SIZE WARNING:** At 1,991 lines / 128KB, this page is a God Component. Should be decomposed into sub-components for receipt list, receipt form, receiving worksheet, QC panel, discrepancy resolution.

---

### 6.3 Export Receipts / Phiếu Xuất Kho (`/export-receipts`)

**File:** [`ExportReceipts.tsx`](file:///d:/ERP_KHO/frontend/src/pages/ExportReceipts.tsx) (1,417 lines, 74KB)

**Purpose:** Outbound receipt lifecycle with reservation management and dispatch workflow.

**API Endpoints:**
- `GET /api/ExportReceipts` — list
- `POST /api/ExportReceipts` — create
- `PUT /api/ExportReceipts/{id}` — update
- `POST /api/ExportReceipts/{id}/approve` — approve (creates reservation)
- `POST /api/ExportReceipts/{id}/dispatch` — dispatch (consumes reservation)
- `POST /api/ExportReceipts/{id}/cancel` — cancel
- Product, warehouse, customer lookups

**Key UI Elements:**
- Receipt list with metrics (total, by status)
- Create/Edit form with product lines, customer, marketplace order, carrier/tracking
- Reservation status badge per receipt
- Dispatch mode selection (immediate vs separate dispatch)
- Print preview with barcode
- Status tabs for filtering

**Permission checks:** `export_receipt.read`, `export_receipt.create`, `export_receipt.approve`, `export_receipt.dispatch`, `export_receipt.cancel`

---

### 6.4 Inventory / Tồn Kho (`/inventory`)

**File:** [`Inventory.tsx`](file:///d:/ERP_KHO/frontend/src/pages/Inventory.tsx) (1,394 lines, 77KB)

**Purpose:** Multi-tab inventory overview with stock balances, transaction history, in/out report, expiry aging, and analytics.

**Tabs:**
1. **Tồn kho hiện tại** — stock balances by product/warehouse (onHand, reserved, available)
2. **Nhóm tồn kho chi tiết** — embedded `InventoryBuckets` sub-page
3. **Lịch sử biến động** — transaction history with type/status/lot/serial filtering
4. **Báo cáo xuất-nhập-tồn** — period in/out opening/closing report
5. **Tuổi hạn sử dụng** — expiry aging with EXPIRED/NEAR_EXPIRY/GOOD color coding
6. **Phân tích vận hành** — SKU velocity analysis, turnover rate, SLA metrics, top movers

**API Endpoints:**
- `GET /api/Inventory/stocks` — stock balances
- `GET /api/Inventory/transactions` — transaction history
- `GET /api/Inventory/in-out` — in/out report
- `GET /api/v1/inventory/expiry-aging` — expiry analysis
- `GET /api/v1/inventory/analytics` — operational analytics
- `POST /api/v1/inventory/status-change` — change inventory status

**Permission:** Cost-sensitive columns hidden if `inventory.cost.read` absent.

---

### 6.5 Stocktakes / Kiểm Kê (`/stocktakes`)

**File:** [`Stocktakes.tsx`](file:///d:/ERP_KHO/frontend/src/pages/Stocktakes.tsx) (1,568 lines, 70KB)

**Purpose:** Full cycle counting workflow — create, count, submit, approve/reject with adjustment posting.

**Features:**
- Create stocktake (blind count mode available)
- Multi-attempt counting (attempt 1 & 2)
- Auto-approval policy check
- Location-level counting
- Variance analysis with VND valuation
- Approve → posts adjustment to inventory ledger

**API Endpoints:**
- `GET /api/Stocktakes`
- `POST /api/Stocktakes`
- `POST /api/Stocktakes/{id}/approve`
- `POST /api/Stocktakes/{id}/cancel`
- `GET /api/v1/stocktakes/{id}/auto-approval-preview`

---

### 6.6 Stock Transfers / Điều Chuyển Kho (`/stock-transfers`)

**File:** [`StockTransfers.tsx`](file:///d:/ERP_KHO/frontend/src/pages/StockTransfers.tsx) (1,018 lines, 46KB)

**Purpose:** Inter-warehouse transfer lifecycle — Draft → Approved → In-Transit → Received → Completed.

**Features:**
- Create transfer with source/destination warehouse
- Multi-line product selection
- In-transit tracking with hours elapsed
- Receive with damaged/missing quantities
- Return flow for failed transfers
- Draft revision tracking
- Reverse transfer creation
- Details dialog (separate component [`StockTransferDetailsDialog.tsx`](file:///d:/ERP_KHO/frontend/src/pages/StockTransferDetailsDialog.tsx))

**Statuses:** Draft, Approved, InTransit, Received, Completed, Cancelled, Returned.

---

### 6.7 Picking Tasks / Lấy Hàng (`/picking-tasks`)

**File:** [`PickingTasks.tsx`](file:///d:/ERP_KHO/frontend/src/pages/PickingTasks.tsx) (858 lines, 47KB)

**Purpose:** Pick-by-allocation workflow with barcode scanning, wave grouping, and short-pick handling.

**Features:**
- Task list with assign/start/complete actions
- Per-line barcode scanning (location, product, quantity)
- Wave group summary (batch picking)
- Short-pick reporting with reason codes
- Short-pick resolution (Reallocate, Cancel, Skip)
- Mobile barcode scanner bar integration
- FEFO cutoff banner
- Automation telemetry panel (WCS robotics, RFID IoT, voice pick)

**API Endpoints:**
- `GET /api/picking-tasks`
- `GET /api/picking-tasks/waves`
- `POST /api/picking-tasks/{id}/assign`
- `POST /api/picking-tasks/{id}/start`
- `POST /api/picking-tasks/{id}/lines/{lineId}/pick`
- `POST /api/picking-tasks/{id}/lines/{lineId}/short-pick`
- `POST /api/picking-tasks/{id}/complete`
- `POST /api/picking-tasks/{id}/short-picks/{spId}/resolve`

**Error code handling:** `PICK_WRONG_LOCATION`, `PICK_WRONG_PRODUCT`, `PICK_WRONG_LOT`, `PICK_SHORT` — all mapped to Vietnamese messages.

---

### 6.8 Packing Sessions / Đóng Gói (`/packing-sessions`)

**File:** [`PackingSessions.tsx`](file:///d:/ERP_KHO/frontend/src/pages/PackingSessions.tsx) (1,087 lines, 64KB)

**Purpose:** Pack picked items into Handling Units (cartons, pallets, totes).

**Features:**
- 3 tabs: Workbench, Sessions & HU Registry, Kitting/Video
- Create HU (type selection: Carton, Pallet, Tote, Box)
- Pack items into HU by line
- HU nesting (parent/child hierarchy)
- HU sealing
- Barcode label preview
- SSCC code support
- Weight/volume recording
- Kitting VAS (assembly/disassembly)
- Carton recommendation (3D packing)

**Error code handling:** `PACK_QUANTITY_EXCEEDS_PICKED`, `HU_CIRCULAR_NESTING`, `HU_CLOSED`, `HU_CONTENT_MISMATCH` — Vietnamese messages.

---

### 6.9 Shipments / Giao Hàng (`/shipments`)

**File:** [`Shipments.tsx`](file:///d:/ERP_KHO/frontend/src/pages/Shipments.tsx) (732 lines, 45KB)

**Purpose:** Last-mile shipment lifecycle — Staging → Loading → Dispatched → In Transit → Delivered.

**Features:**
- Create shipment from packing session
- Assign staging location
- Dock appointment linking
- Load HUs onto shipment with barcode scanning
- Dispatch with carrier/vehicle/seal info
- Tracking event timeline (Created → Staged → Loading → Loaded → Dispatched → InTransit → Delivered)
- Proof of delivery with receiver name, GPS coordinates, evidence reference
- Delivery failure handling
- Return-to-warehouse flow

**Statuses:** Draft, Ready, Staging, Loading, Loaded, Dispatched, InTransit, Delivered, DeliveryFailed, ReturnToWarehouse, Completed, Cancelled.

---

### 6.10 Handover Manifests / Biên Bản Bàn Giao (`/handover-manifests`)

**File:** [`HandoverManifests.tsx`](file:///d:/ERP_KHO/frontend/src/pages/HandoverManifests.tsx) (373 lines, 16KB)

**Purpose:** AWB scanning and courier handover manifest management.

**Features:**
- Create manifest (carrier, vehicle plate, driver name)
- Scan AWB (Air Waybill) barcodes to add parcels
- Cancelled order detection and blocking
- Seal and finalize manifest
- Manifest list with status badges

**API:** `GET /api/v1/handover-manifests`, `POST /api/v1/handover-manifests`, `POST /api/v1/handover-manifests/{id}/scan-awb`, `POST /api/v1/handover-manifests/{id}/seal`

---

### 6.11 Putaway Tasks / Cất Hàng (`/putaway-tasks`)

**File:** [`PutawayTasks.tsx`](file:///d:/ERP_KHO/frontend/src/pages/PutawayTasks.tsx) (659 lines, 43KB)

**Purpose:** Directed putaway with bin suggestions, barcode validation, and cross-dock support.

**Features:**
- Task list with assign/start/execute/complete actions
- **Directed putaway suggestions** — AI-recommended bin with 5-factor scoring (SKU affinity, storage group, capacity, pick-face proximity, FEFO consolidation)
- 4 hard guards (active bin, unblocked, no stocktake freeze, capacity check)
- Barcode scanning for bin and product validation
- Cross-dock detection with target SO/export receipt linking
- Putback tasks (cancel and return to shelf)
- Exception handling with reason codes
- ABC-XYZ interleaving analysis with reslotting recommendations
- Workspace mode for direct putaway execution

**Error codes:** `PUTAWAY_WRONG_BIN`, `PUTAWAY_WRONG_PRODUCT`

---

### 6.12 Products / Sản Phẩm (`/products`)

**File:** [`Products.tsx`](file:///d:/ERP_KHO/frontend/src/pages/Products.tsx) (1,304 lines, 76KB)

**Purpose:** SKU master data with multi-UOM, barcodes, categories, channel mapping, and bundling.

**4 Tabs:**
1. Products — CRUD with tracking type (None/Lot/Serial), expiry control, QC required, storage class, weight/volume/pallet
2. Categories & UOM — category management + unit conversion
3. Channel Mapping — marketplace SKU mapping (Shopee, etc.)
4. Bundle & Gift — assembly/disassembly (kitting/de-kitting)

**Permission checks:** `product.read`, `product.create`, `product.update`, `product.deactivate`, `product_category.read`, `product_category.manage`, `product_barcode.manage`

---

### 6.13 Permissions / Quản Trị Quyền (`/permissions`)

**File:** [`Permissions.tsx`](file:///d:/ERP_KHO/frontend/src/pages/Permissions.tsx) (2,280 lines, 109KB — **2nd largest**)

**Purpose:** Complete IAM admin — users, roles, permissions, warehouse access, security dashboard.

**Tabs:**
1. Users & Security — user list, security status, lock/unlock, force password change
2. Roles & Permissions — role management with permission matrix
3. Warehouse Access — per-user warehouse scope grants
4. Audit Logs — permission change audit trail
5. Security Dashboard — metrics (active users, locked accounts, session count)

**API Endpoints:**
- `GET /api/v1/permissions/catalog` — permission definitions
- `GET /api/v1/permissions/roles` — role list
- `POST /api/v1/permissions/roles` — create role
- `PUT /api/v1/permissions/roles/{id}` — update role permissions
- `GET /api/v1/permissions/users/{id}/security-status` — user security
- `POST /api/v1/permissions/users/{id}/force-password-change`
- `POST /api/v1/permissions/users/{id}/toggle-lock`
- `POST /api/v1/permissions/users/{id}/terminate-sessions`
- `GET /api/v1/permissions/users/{id}/warehouse-access` — warehouse scopes
- `POST /api/v1/permissions/users/{id}/warehouse-access/grant`
- `DELETE /api/v1/permissions/users/{id}/warehouse-access/{whId}`
- `GET /api/v1/permissions/audit-logs`

---

### 6.14 Stock Allocations / Phân Bổ Tồn (`/stock-allocations`)

**File:** [`StockAllocations.tsx`](file:///d:/ERP_KHO/frontend/src/pages/StockAllocations.tsx) (395 lines, 24KB)

**Purpose:** View and manage location-level allocations from reservations. Release, reallocate with candidate ranking.

**Features:**
- Paginated allocation list
- Filter by status, warehouse, product
- Release allocation
- Reallocate with location candidate ranking (FEFO/FIFO/LocationOrder)
- Link to reservation details
- Drawer for release/reallocate actions

**Permissions:** `allocation.read`, `allocation.create`, `allocation.release`, `allocation.reallocate`

---

### 6.15 Stock Reservations / Giữ Hàng (`/stock-reservations`)

**File:** [`StockReservations.tsx`](file:///d:/ERP_KHO/frontend/src/pages/StockReservations.tsx) (342 lines, 15KB)

**Purpose:** View and manage stock reservations with release capability.

**Features:**
- Paginated reservation list
- Summary metrics (active, consumed, released)
- Release reservation action
- Status badges with source type display
- Conflict error handling (409)

---

### 6.16 Inventory Reconciliation / Đối Chiếu Tồn Kho (`/inventory-reconciliation`)

**File:** [`InventoryReconciliation.tsx`](file:///d:/ERP_KHO/frontend/src/pages/InventoryReconciliation.tsx) (1,307 lines, 81KB)

**Purpose:** Verify operational balances against immutable ledger. Drill-down investigation per SKU.

**Features:**
- Reconciliation row list (per product/warehouse: current vs expected quantity)
- Mismatch detection with status badges
- Deep investigation panel (buckets, ledger events, status changes)
- Multi-status evidence (Available, QcHold, Quarantine, etc.)
- Unclassified event detection
- Invalid reservation detection
- Negative physical bucket detection
- What-if simulation
- Decision policy audit
- Links to traceability page

---

### 6.17 Inventory Traceability / Truy Vết Tồn Kho (`/inventory-traceability`)

**File:** [`InventoryTraceability.tsx`](file:///d:/ERP_KHO/frontend/src/pages/InventoryTraceability.tsx) (719 lines, 47KB)

**Purpose:** Full genealogy and provenance tracking per SKU/Lot/Serial.

**Features:**
- Search by product, lot, serial, reference
- Current bucket display (location, status, onHand, reserved)
- Immutable ledger event timeline with reversal linking
- Related document index (receipts, transfers, shipments)
- Receipt exposure analysis
- Shipment exposure with picking evidence and HU evidence
- Export source tracing
- Recall tree simulation
- Pagination for large datasets

---

### 6.18 DockYard / Cổng Sân Bãi (`/dock-yard`)

**File:** [`DockYard.tsx`](file:///d:/ERP_KHO/frontend/src/pages/DockYard.tsx) (1,305 lines, 83KB)

**Purpose:** Dock door and yard management — appointment scheduling, vehicle check-in, dock assignment, dwell monitoring.

**Features:**
- Dock and yard slot CRUD
- Appointment lifecycle (Draft → Confirmed → Arrived → CheckedIn → DockAssigned → InService → Completed)
- Vehicle check-in with driver info and seal number
- Dock assignment with compatibility check (temperature, hazmat)
- Dwell time monitoring with SLA thresholds
- Link appointments to PO/ASN/Receipt
- Exception code recording
- Auto-suggest dock based on vehicle type

---

### 6.19 Additional Pages (Summary)

| Page | Lines | Purpose |
|------|-------|---------|
| **Login.tsx** | 191 | Username/password + tenant code, JWT token storage, multi-tenant domain parsing |
| **OnboardingWizard.tsx** | 500 | 5-step SaaS self-service setup wizard with template selection, Excel opening stock import |
| **SaasBilling.tsx** | 1,455 | Subscription plan management, VietQR payment, invoice history, QR barcode rendering |
| **CodReconciliation.tsx** | 450 | 3-way COD reconciliation: internal ledger vs carrier statement upload (CSV/Excel) |
| **CostValuation.tsx** | 437 | Moving average cost valuation, MISA AMIS export, e-invoice draft (NĐ 70/2025) |
| **BusinessPartners.tsx** | 1,062 | Supplier/Customer CRUD with AP/AR invoice, debt ledger, credit limit |
| **PartnerDebts.tsx** | ~900 | AP/AR debt management, immutable debt ledger, payment recording |
| **Approvals.tsx** | 747 | Unified approval queue (inbound, outbound), approve/reject with focus-trapped drawer |
| **WarehouseLocations.tsx** | 879 | Location CRUD, structure tree (Zone/Aisle/Rack/Level/Bin), capacity monitoring, barcode labels |
| **WarehouseMap.tsx** | 489 | Grid-based warehouse layout, utilization heatmap, BI productivity snapshot |
| **WarehouseCalendar.tsx** | 800+ | Weekly calendar, shift management, cutoff times, capacity per shift |
| **Warehouses.tsx** | ~500 | Warehouse CRUD with timezone, active status |
| **Units.tsx** | ~400 | UOM CRUD |
| **Categories.tsx** | ~300 | Product category CRUD |
| **PurchaseOrders.tsx** | ~1,000 | PO management, tolerance checking, ASN linking |
| **Asns.tsx** | ~1,000 | Advanced Shipment Notice management |
| **InventoryBuckets.tsx** | 317 | Granular 9-dimension inventory bucket view (status/lot/serial/location) with status change |
| **InventoryLocks.tsx** | ~700 | Inventory lock/freeze management with audit trail |
| **InventoryMovements.tsx** | ~800 | Internal location-to-location movement with barcode scanning |
| **InventoryReversals.tsx** | ~500 | Reversal of posted inventory transactions |
| **OutboundDemand.tsx** | 1,021 | Sales Order + Backorder management, SO release, backorder recovery |

---

## 7. Shared Components

### 7.1 Component Inventory

| Component | File | Size | Purpose |
|-----------|------|------|---------|
| **MobileBarcodeScannerBar** | [`components/MobileBarcodeScannerBar.tsx`](file:///d:/ERP_KHO/frontend/src/components/MobileBarcodeScannerBar.tsx) | 44KB | Mobile camera barcode scanner (camera access, scanning UI, result parsing) |
| **ReceiptPrintPreview** | [`components/ReceiptPrintPreview.tsx`](file:///d:/ERP_KHO/frontend/src/components/ReceiptPrintPreview.tsx) | 8KB | Print-optimized receipt layout with signatures and barcode |
| **ReceiptInventoryIdentityEditor** | [`components/ReceiptInventoryIdentityEditor.tsx`](file:///d:/ERP_KHO/frontend/src/components/ReceiptInventoryIdentityEditor.tsx) | 11KB | Lot/Serial/Expiry assignment per receipt line |
| **BarcodeSvg** | [`components/BarcodeSvg.tsx`](file:///d:/ERP_KHO/frontend/src/components/BarcodeSvg.tsx) | 5KB | SVG barcode rendering (Code128-like) |
| **AccessibleDialog** | [`components/AccessibleDialog.tsx`](file:///d:/ERP_KHO/frontend/src/components/AccessibleDialog.tsx) | 2KB | Accessible modal dialog with focus trap |
| **common/MobileBarcodeScannerBar** | [`components/common/MobileBarcodeScannerBar.tsx`](file:///d:/ERP_KHO/frontend/src/components/common/MobileBarcodeScannerBar.tsx) | 151B | Re-export of main scanner component |

---

## 8. Mock Data & Demo Mode

### 8.1 Mock Data Files

| File | Size | Purpose |
|------|------|---------|
| [`mocks/demoApiData.ts`](file:///d:/ERP_KHO/frontend/src/mocks/demoApiData.ts) | 54KB | Comprehensive mock data for ALL modules (products, receipts, inventory, etc.) |
| [`mocks/erpWmsMockData.ts`](file:///d:/ERP_KHO/frontend/src/mocks/erpWmsMockData.ts) | 37KB | Additional mock data |
| [`mocks/inventoryReconciliationDemo.ts`](file:///d:/ERP_KHO/frontend/src/mocks/inventoryReconciliationDemo.ts) | 2KB | Reconciliation-specific demo data |
| [`mocks/erpWmsMockData.test.ts`](file:///d:/ERP_KHO/frontend/src/mocks/erpWmsMockData.test.ts) | 4KB | Mock data integrity test |

### 8.2 Demo API Adapter

[`services/demoApiAdapter.ts`](file:///d:/ERP_KHO/frontend/src/services/demoApiAdapter.ts) (1,201 lines, 59KB):
- Intercepts ALL API calls when on Vercel demo hostname.
- Returns mock data for GET requests, blocks POST/PUT/DELETE with 405.
- Route matching via `normalizePath()`.
- Supports pagination via `paged()` helper.

### 8.3 Mock Data in Production — Assessment

**The mock data is ONLY used in demo mode** (when `isReadOnlyDemoRuntime()` returns true). The adapter is injected conditionally via the request interceptor ([`apiClient.ts:195-197`](file:///d:/ERP_KHO/frontend/src/services/apiClient.ts#L195-L197)).

**One exception noted:** [`InventoryReconciliation.tsx:6`](file:///d:/ERP_KHO/frontend/src/pages/InventoryReconciliation.tsx#L6) imports `demoReconciliationRows` directly, but it's used behind an `isReadOnlyDemoRuntime()` check so it's safe.

---

## 9. Test Coverage

### 9.1 Test File Count

**Total: 73 test files** across:
- **Pages:** 54 test files
- **Services:** 9 test files
- **Components:** 3 test files
- **UI:** 1 test file
- **Mocks:** 1 test file
- **Routes:** 1 test file
- **Layouts:** 1 test file

### 9.2 Test Coverage by Module

| Module | Test Files | Notes |
|--------|-----------|-------|
| Import Receipts | 3 | ProductionUi, Permissions, PrintLoading, PartnerDoubleSubmit |
| Export Receipts | 1 | ProductionUi test |
| Stock Transfers | 7 | CommandSafety, DraftRevision, ReasonReset, ReturnFlow, ReturnPermission, ReturnTraceability, ReverseDraft, ProductionUi |
| Inventory | 3 | Base, Buckets, ProductionUi |
| Inventory Reconciliation | 3 | Main (73KB!), ReservationIntegrity, SnapshotProvenance |
| Inventory Traceability | 2 | Main, ReceiptExposure |
| Inventory Reversals | 2 | Main, SkuSearch |
| Permissions | 1 | 20KB |
| Shipments | 1 | 13KB |
| SaaS Billing | 1 | 14KB |
| apiClient | 3 | adapter, spec, main test |
| All others | ~46 | 1 test file each |

### 9.3 Notable Test Patterns

- Tests use `@testing-library/react` with Vitest.
- API calls are mocked via `vi.mock('../services/apiClient')`.
- Permission checks are tested by manipulating `localStorage('permissions')`.
- Wave4/Wave5 remediation tests verify cross-cutting frontend fixes.

---

## 10. Architecture Assessment Summary

### 10.1 Strengths

| Area | Assessment |
|------|-----------|
| **Route security** | Every route has permission guard. OR logic supported for multi-permission routes. |
| **Idempotency** | Automatic idempotency key generation for ALL mutations — best-practice pattern. |
| **Error handling** | Consistent error extraction with `errorCode`, `correlationId`, HTTP status → Vietnamese message mapping. |
| **Token management** | Refresh flow with single-flight dedup, 401 auto-redirect, 403 permission re-sync. |
| **Vietnamese localization** | 100% Vietnamese UI labels, status badges, error messages. No English strings in user-facing content. |
| **Accessibility** | Skip link, ARIA labels on sidebar/topbar/drawer, focus management, `role="tablist"`, `role="dialog"`, `aria-current="page"`, `inert` on hidden sidebar. |
| **Demo mode** | Clean separation — mock data adapter only active on demo hostnames. |
| **Mobile PDA** | Bottom nav for warehouse handheld devices, barcode scanner integration. |
| **RBAC-aware UI** | Cost columns hidden without `inventory.cost.read`. Buttons disabled/hidden per permission. |
| **Complexity mode** | EXPRESS mode hides advanced features for simpler operations. |

### 10.2 Issues & Risks

| # | Severity | Issue | Evidence |
|---|----------|-------|----------|
| 1 | 🔴 HIGH | **God Components** — ImportReceipts (1,991 lines), Permissions (2,280 lines), DockYard (1,305 lines), InventoryReconciliation (1,307 lines), Stocktakes (1,568 lines) exceed 500-line guideline | File sizes in audit |
| 2 | 🟡 MEDIUM | **No CSS framework** — vanilla CSS without utility classes or design tokens means inconsistency risk | 20 CSS files, no `design-tokens.css` |
| 3 | 🟡 MEDIUM | **No state management** — permissions stored in localStorage with `useSyncExternalStore` hack; complex pages duplicate fetching logic | `authorization.ts:17` |
| 4 | 🟡 MEDIUM | **Mixed API route conventions** — some endpoints use `/api/ImportReceipts` (PascalCase), others `/api/v1/handover-manifests` (kebab-case) | Multiple pages |
| 5 | 🟡 MEDIUM | **Vite config has no proxy** — CORS relies on `withCredentials: true` and server-side CORS config | `vite.config.ts` |
| 6 | 🟢 LOW | **No React.lazy boundaries per sub-route** — all pages are lazy-loaded at route level, but large pages load everything at once | `AppRoutes.tsx` |
| 7 | 🟢 LOW | **Username/role stored in localStorage** — could be tampered with, but auth decisions rely on server-validated JWT | `apiClient.ts:159` |
| 8 | 🟢 LOW | **No error boundary** — React error boundaries not observed for runtime crash isolation | Not found in codebase |

### 10.3 File Size Distribution

| Size Range | Count | Files |
|------------|-------|-------|
| > 100KB | 3 | ImportReceipts (128KB), Permissions (109KB), DockYard (83KB) |
| 50–100KB | 9 | Inventory (77KB), Products (76KB), ExportReceipts (74KB), SaasBilling (72KB), Stocktakes (70KB), OutboundDemand (66KB), PackingSessions (64KB), InventoryReconciliation (81KB), InventoryTraceability (47KB) |
| 20–50KB | 12 | Dashboard (41KB), StockTransfers (46KB), PickingTasks (47KB), Shipments (45KB), PutawayTasks (43KB), etc. |
| < 20KB | ~15 | Units, Categories, Warehouses, HandoverManifests, StockReservations, CodReconciliation, CostValuation, etc. |

### 10.4 API Endpoint Convention Audit

| Convention | Count | Examples |
|------------|-------|---------|
| `/api/v1/<kebab-case>` (correct) | ~15 | `/api/v1/handover-manifests`, `/api/v1/inventory/expiry-aging`, `/api/v1/permissions/catalog` |
| `/api/<PascalCase>` (legacy) | ~10 | `/api/ImportReceipts`, `/api/ExportReceipts`, `/api/Inventory/stocks`, `/api/Reports/...` |
| Mixed within same page | Several | Dashboard uses both `/api/Reports/...` and `/api/v1/reports/...` |

> **Recommendation:** Migrate all legacy PascalCase routes to kebab-case `/api/v1/` convention.

---

## 11. Feature Matrix Summary

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     ERP_KHO Frontend Feature Map                       │
├───────────────┬─────────────────────────────────────────────────────────┤
│ INBOUND       │ PO → ASN → Dock/Yard → Receipt → QC → Post → Putaway  │
├───────────────┼─────────────────────────────────────────────────────────┤
│ OUTBOUND      │ SO/Backorder → Reserve → Allocate → Pick → Pack →      │
│               │ Ship → Handover Manifest → COD Reconciliation          │
├───────────────┼─────────────────────────────────────────────────────────┤
│ INVENTORY     │ 9-dim Buckets → Movements → Status Change → Locks →    │
│               │ Traceability → Reconciliation → Stocktake → Transfers  │
│               │ → Reversals                                            │
├───────────────┼─────────────────────────────────────────────────────────┤
│ FINANCE       │ Cost Valuation (WAC) → MISA Export → COD 3-way →       │
│               │ Partner Debts (AP/AR) → E-Invoice Draft                │
├───────────────┼─────────────────────────────────────────────────────────┤
│ ADMIN         │ Products → Categories → UOM → Warehouses → Locations → │
│               │ Map → Calendar → Partners → Permissions → SaaS Billing │
│               │ → Onboarding Wizard                                    │
└───────────────┴─────────────────────────────────────────────────────────┘
```

---

*End of Frontend Audit*
