# SAAS SECURITY, MULTI-TENANCY & PERMISSION REVIEW (DELIVERABLE N)

**Project:** ERP_KHO — Cloud ERP & WMS SaaS  
**Document ID:** `DELIVERABLE-N`  
**Audit Run:** `2026-11-01_REASSESSMENT_001`  
**Mode:** READ-ONLY SYSTEM AUDIT + TO-BE SECURITY & RBAC DESIGN PROPOSAL  
**Baseline Branch / SHA:** `feat/WMS-61-project-development` (`6e0a333390c1db6222e55ae3945b7ce33a56bef6`)

---

## 1. Executive Summary

ERP_KHO implements a **strong foundational multi-tenant and database-backed RBAC architecture**, including:
- Cryptographic JWT `TenantId` resolution (`ERP.Api/Authorization/HttpTenantContext.cs:12-63`) that ignores unsigned headers/query parameters to prevent Cross-Tenant IDOR (`[SAAS-001]`).
- Database-enforced Global Query Filters and `SaveChanges` cross-tenant foreign-key validation across all `ITenantEntity` tables (`ERP.Infrastructure/Persistence/ErpKhoDbContext.cs:662-755`).
- Per-request database permission evaluation in `PermissionAuthorizationFilter` (`ERP.Api/Authorization/PermissionAuthorizeAttribute.cs:59-85`), ensuring revoked permissions or locked users take effect immediately on the next request without waiting for JWT expiration (compliant with Notion Spec 17 §33.1).
- Server-side cost masking via `inventory.cost.read` (`ReportsController.cs:37, 154`; `ExportReceiptsController.cs:67, 314`).

However, our forensic line-by-line audit of `ERP.Api/Controllers` and `ERP.Api/Authorization` uncovered **8 high-priority security and authorization findings** that require remediation in the TO-BE architecture:

| ID | Severity | Finding Summary | Primary Evidence (`FACT`) |
|----|----------|-----------------|---------------------------|
| **SEC-F01** | 🔴 **P0 Critical** | **In-Memory TOTP 2FA State, Client-Supplied Secret Acceptance & Deterministic Secret Fallback** | `AccountSecurityController.cs:122, 216-225, 570-594` |
| **SEC-F02** | 🔴 **P0 Critical** | **`[AllowAnonymous]` Combined with `[PermissionAuthorize]` Bypasses Auth Filter on Tenant Provisioning** | `PermissionsController.cs:128-144` vs `PermissionAuthorizeAttribute.cs:61-64` |
| **SEC-F03** | 🟠 **P1 High** | **Mutation (`POST`) Endpoints Protected Only by `.read` Permission + Hardcoded `!IsInRole("Viewer")` Check** | `ImportReceiptsController.cs:155, 195`; `ExportReceiptsController.cs:202, 226, 243, 255, 267, 328, 337, 375`; `PermissionsController.cs:70, 161, 190` |
| **SEC-F04** | 🟠 **P1 High** | **Dual/Split `AppPermissions` Classes & Overloaded Permission String Constants** | `ERP.Domain/Security/AppPermissions.cs:3-46` (5 codes) vs `ERP.Api/Authorization/AppPermissions.cs:3-27` (~85 codes; 3 constants mapped to `"inventory_movement.create"`) |
| **SEC-F05** | 🟠 **P1 High** | **Legacy `[Authorize(Roles = ...)]` Attributes Still Present on `ReportsController`** | `ReportsController.cs:13, 98, 208, 225, 301, 323` (violates `[SEC-001]` & Spec 17 §1) |
| **SEC-F06** | 🟡 **P2 Medium** | **Refresh Token Cookie `Path = "/api/Auth"` Breaks `/api/v1/auth/refresh` Route** | `AuthController.cs:15, 59, 136` |
| **SEC-F07** | 🟡 **P2 Medium** | **Access Token Stored in `localStorage` on Frontend** | `frontend/src/services/apiClient.ts:159, 216` |
| **SEC-F08** | 🟡 **P2 Medium** | **Missing Target-User Tenant Scope Guard on TOTP Endpoints in `AccountSecurityController`** | `AccountSecurityController.cs:133-240` (uses `[PermissionAuthorize(AppPermissions.UserManage)]` without calling `EnsureTenantUserOwnershipAsync`) |

---

## 2. Multi-Tenant Isolation Audit (`[SAAS-001]`)

### 2.1 Tenant Context Resolution (`HttpTenantContext.cs`)
- **Verified Behavior (`FACT`):** `HttpTenantContext.TenantId` (`ERP.Api/Authorization/HttpTenantContext.cs:16-41`) resolves the active tenant in strict priority order:
  1. `SupportImpersonationMiddleware.ImpersonatedTenantIdItemKey` (only when a verified, non-expired support impersonation token is active).
  2. Cryptographically signed JWT claim `TenantClaimTypes.TenantId`.
  3. Fallback `TenantContext.DefaultTenantId` (`1`) only for unauthenticated/bootstrap contexts.
- **Security Strength:** Unsigned HTTP headers (`X-Tenant-Id`), query parameters, or request body `tenantId` fields are **never** trusted for standard authenticated requests, preventing Cross-Tenant Spoofing.

### 2.2 Database-Level Tenant Isolation (`ErpKhoDbContext.cs`)
- **Global Query Filters:** Applied to all entities implementing `ITenantEntity` (`ErpKhoDbContext.cs:662-710`), automatically appending `WHERE TenantId = @CurrentTenantId`.
- **Write-Time Enforcement:** `EnforceMultiTenantIsolation()` in `SaveChanges` / `SaveChangesAsync` (`ErpKhoDbContext.cs:712-755`) stamps `CurrentTenantId` on `Added` entities, blocks cross-tenant modifications/deletions, and validates that referenced foreign keys belong to the same `TenantId`.
- **Global Seed Exception (Spec 17 §33.4.5 — `OD-10`):** `Units` (UOMs) and `ReceivingReasonCodes` support Global System Seed (`TenantId == null` or system scope) combined with company scope, whereas `Products`, `ProductCategories`, `ProductBarcodes`, `Warehouses`, and all transactional tables are strictly tenant-isolated.

---

## 3. Deep-Dive Forensic Security Findings & TO-BE Remediations

### 3.1 `[SEC-F01]` In-Memory TOTP 2FA State, Client Secret Acceptance & Deterministic Fallback (`P0 Critical`)

**Evidence (`FACT`):**
In [`AccountSecurityController.cs`](file:///d:/ERP_KHO/ERP.Api/Controllers/AccountSecurityController.cs#L122-L595):
1. **Line 122:** `private static readonly ConcurrentDictionary<int, (byte[] SecretBytes, string Base32Secret, string[] RecoveryCodes, string Status)> TotpEnrollments = new();` — TOTP enrollment state is kept in a static in-memory dictionary on the controller class rather than persisted in SQL Server (`Users` table). Any app pool recycle, deployment, or horizontal scale-out wipes all 2FA enrollments.
2. **Lines 216–219 & 577–584:** `VerifyTotp` and `RegenerateRecoveryCodes` read `request?.Base32Secret ?? request?.SecretKey` from the HTTP request body and pass it to `ResolveEnrolledTotpSecretBytes(userId, username, providedSecret)`. If `TotpEnrollments` does not have an entry in memory, `ResolveEnrolledTotpSecretBytes` decodes the **client-provided secret** and verifies the 6-digit code against the secret the client just sent!
3. **Lines 586–594:** If neither in-memory state nor client-supplied secret is present, `GetTotpSecretBytes(userId, username)` computes `SHA256("ERP_WMS_RFC6238_TOTP_SECRET:{userId}:{username}")[..20]` — a hardcoded, deterministic formula that anyone who knows `userId` and `username` can compute offline to generate valid 6-digit TOTP codes!

**TO-BE Remediation:**
1. Persist TOTP state on the `User` entity (or dedicated `UserMfaCredential` table) in `ERP_KHO` via an additive EF Core Migration: `TotpSecretEncrypted` (`varbinary` encrypted via ASP.NET Core `IDataProtectionProvider`), `TotpEnrollmentStatus` (`IDLE`, `PENDING_OTP`, `PENDING_RECOVERY_ACK`, `ACTIVE`), `TotpRecoveryCodeHashesJson` (SHA-256 hashed one-time recovery codes), and `TotpActivatedAtUtc`.
2. **Remove** `providedBase32Secret` from `VerifyTotpRequest` and `ResolveEnrolledTotpSecretBytes` — the server must **only** verify against the server-stored pending/active secret in the database.
3. **Delete** `GetTotpSecretBytes(int userId, string username)` deterministic hash fallback (`lines 589-594`) and static `TotpEnrollments` dictionary (`line 122`).
4. Enforce `EnsureTenantUserOwnershipAsync(userId)` or self-service ownership (`actorUserId == userId` OR `user.manage` within same `TenantId`) on all TOTP endpoints.

---

### 3.2 `[SEC-F02]` `[AllowAnonymous]` Combined with `[PermissionAuthorize]` on Tenant Provisioning (`P0 Critical`)

**Evidence (`FACT`):**
In [`PermissionsController.cs:128-144`](file:///d:/ERP_KHO/ERP.Api/Controllers/PermissionsController.cs#L128-L144):
```csharp
[HttpPost("~/api/v1/saas/tenants/provision")]
...
[AllowAnonymous]
[PermissionAuthorize(AppPermissions.PermissionRead)]
[IdempotentCommand("SaaS.ProvisionTenant")]
public async Task<IActionResult> ProvisionSaaSTenant(...)
```
And in [`PermissionAuthorizeAttribute.cs:61-64`](file:///d:/ERP_KHO/ERP.Api/Authorization/PermissionAuthorizeAttribute.cs#L61-L64):
```csharp
if (context.ActionDescriptor.EndpointMetadata.OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any())
{
    return;
}
```
Because `[AllowAnonymous]` short-circuits `PermissionAuthorizationFilter`, `[PermissionAuthorize(AppPermissions.PermissionRead)]` on `ProvisionSaaSTenant` is completely bypassed for unauthenticated callers! Furthermore, `AllocateNewTenant = true` can be triggered anonymously without rate-limiting or CAPTCHA/verification token protection.

**TO-BE Remediation:**
1. Split Tenant Provisioning into two distinct, explicitly secured flows:
   - **Public Self-Service Signup (`POST /api/v1/auth/register-tenant`):** Decorated with `[AllowAnonymous]` and strict `[EnableRateLimiting("TenantRegistration")]` (e.g., max 3 registrations per IP per hour) + email/OTP verification, **never** placed inside `PermissionsController`.
   - **Admin Tenant Quota / Provisioning Management (`POST /api/v1/saas/tenants/provision`):** Remove `[AllowAnonymous]`, require authenticated SuperAdmin / System Admin permission (`system_config.manage`).

---

### 3.3 `[SEC-F03]` Mutation (`POST`) Endpoints Using `.read` Permissions + `!Viewer` Role Check (`P1 High`)

**Evidence (`FACT`):**
Across multiple controllers, state-changing `POST` endpoints are decorated with read-only permissions (`receipt.read`, `export_receipt.read`, `permission.read`, `inventory.read`, `user_warehouse.read`) and rely on `if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();` to block Viewers:

| Controller & Line | Endpoint (`POST`) | Current Attribute (`FACT`) | Required Canonical Permission (Spec 17 TO-BE) |
|-------------------|-------------------|----------------------------|-----------------------------------------------|
| `ImportReceiptsController.cs:155` | `POST rma-intake-check` | `receipt.read` + `!Viewer` | `return.create` / `return.receive` (Spec 17 §18) |
| `ImportReceiptsController.cs:195` | `POST execute-rtv-refurbish` | `receipt.read` + `!Viewer` | `return.disposition` / `return.post` (Spec 17 §18) |
| `ExportReceiptsController.cs:202` | `POST verify-dispatch-cutoff` | `export_receipt.read` + `!Viewer` | `export_receipt.dispatch` |
| `ExportReceiptsController.cs:226` | `POST batch-print-awb` | `export_receipt.read` + `!Viewer` | `label.print` (first print) + `export_receipt.reprint_awb` (reprint ≥ 2) |
| `ExportReceiptsController.cs:243` | `POST handover-manifests/scan-awb` | `export_receipt.read` + `!Viewer` | `shipment.dispatch` or `handover_manifest.execute` |
| `ExportReceiptsController.cs:255` | `POST handover-manifests/{id}/seal` | `export_receipt.read` + `!Viewer` | `shipment.dispatch` or `handover_manifest.seal` |
| `ExportReceiptsController.cs:267` | `POST confirm-carrier-handover` | `export_receipt.read` + `!Viewer` | `shipment.dispatch` |
| `ExportReceiptsController.cs:328` | `POST export-misa-einvoice` | `export_receipt.read` + `!Viewer` | `report.export` + `inventory.cost.read` |
| `ExportReceiptsController.cs:337` | `POST einvoice-nd70-draft` | `export_receipt.read` + `!Viewer` | `export_receipt.dispatch` or `accounting.einvoice.issue` |
| `ExportReceiptsController.cs:375` | `POST accounting-einvoice/sync` | `export_receipt.read` + `!Viewer` | `integration.manage` |
| `ReportsController.cs:140` | `POST cod-reconciliation/verify` | `inventory.read` + `!Viewer` | `cod_reconciliation.manage` or `inventory.cost.read` |
| `SaasBillingController.cs:41, 57` | `POST enforce-quota`, `POST vietqr-invoice` | `permission.read` + `!Viewer` | `system_config.manage` / `saas_billing.manage` |

**Why This Matters:**
Under `[SEC-001]` and Spec 17 §1 ("Không sử dụng role name trong domain/controller logic"), any custom role created by a tenant (e.g., `"JuniorAuditor"` or `"CustomerServiceReadOnly"`) that is granted `receipt.read` or `export_receipt.read` — and is NOT named `"Viewer"` — can execute all of those `POST` mutations!

**TO-BE Remediation:**
Replace every `[PermissionAuthorize(*.Read)]` + `IsInRole(AppRoles.Viewer)` on `POST/PUT/DELETE` endpoints with its dedicated mutation permission code from Spec 17, and seed those codes into the appropriate role bundles (`Admin`, `Manager`, `WarehouseStaff`, `Accountant`).

---

### 3.4 `[SEC-F04]` Unified Permission Registry & Role Bundle Matrix (`P1 High`)

**Current Issue (`FACT`):**
- `ERP.Domain/Security/AppPermissions.cs` defines only 5 constants (`users.manage` [plural!], `roles.manage` [plural!], `inventory.read`, `inventory.cost.read`, `export_receipt.reprint_awb`).
- `ERP.Api/Authorization/AppPermissions.cs` defines `user.manage` [singular!], `role.manage` [singular!], and ~85 other constants, where `InventoryMovementCreate`, `InventoryWrite`, and `InventoryOpeningBalanceImport` all equal `"inventory_movement.create"`.

**TO-BE Canonical Permission Architecture:**
1. Consolidate all permission constants into a single canonical class `ERP.Domain.Security.AppPermissions` (and alias `ERP.Api.Authorization.AppPermissions` via `global using` or type forwarding).
2. Normalize `users.manage` → `user.manage` and `roles.manage` → `role.manage` per Spec 17 §23 & §33.2.
3. Separate `inventory_opening_balance.import` from `inventory_movement.create` (opening stock import affects valuation and ledger opening balances, which a regular picker moving bins must not execute!).

#### Recommended Default Role Bundles for ICP (1 Warehouse, 5–30 Staff)

| Role Persona | Vietnamese Title | Key Permissions Granted | Explicitly Excluded (SoD Guard) |
|--------------|------------------|-------------------------|---------------------------------|
| **Owner / System Admin** (`Admin`) | Chủ kho / Quản trị hệ thống | All operational, financial (`inventory.cost.read`), master data, and IAM (`permission.assign`, `role.manage`, `user.manage`, `user_warehouse.manage`) permissions. | Still bound by Maker ≠ Checker / Maker ≠ Poster on two-step documents & self-lockout prevention (Spec 17 §33.2). |
| **Warehouse Manager** (`Manager`) | Quản lý kho | All Inbound, Outbound, Inventory, Stocktake (`cycle_count.approve/post`), Returns (`return.approve/post`), Approvals (`approval.approve/reject`), Cost (`inventory.cost.read`), Master Data. | Excluded: IAM privilege escalation (`permission.assign`, `role.manage`). |
| **Supervisor / QC Lead** (`Supervisor`) | Giám sát / Kiểm soát QC & Tồn | `receipt.complete`, `quality_inspection.execute/complete`, `quality_disposition.approve`, `receiving_discrepancy.approve`, `picking.assign/override`, `cycle_count.create/review/recount`, `inventory_status.change`, `inventory_lock.manage`, `return.inspect/disposition`. | Excluded: `inventory.cost.read` (optional by tenant policy), `cycle_count.post`, IAM. |
| **Inbound / Putaway Staff** (`Receiver`) | Nhân viên Nhận & Cất hàng | `receipt.read`, `receipt.receive`, `receiving_discrepancy.create/submit`, `putaway.read`, `putaway.execute` (assigned tasks), `return.receive`, `product.read`, `location.read`. | Excluded: `receipt.complete`, `receipt.post`, `inventory.cost.read`. |
| **Outbound Picker / Packer** (`PickerPacker`) | Nhân viên Lấy hàng, Đóng gói & Bàn giao | `picking.read`, `picking.execute`, `picking.short_pick`, `packing.read`, `packing.execute`, `handling_unit.create/modify`, `shipment.stage/load`, `label.print`, `export_receipt.read/dispatch`. | Excluded: `export_receipt.approve`, `export_receipt.reprint_awb` (requires supervisor override on 2nd+ print), `inventory.cost.read`. |
| **Accountant / Finance** (`Accountant`) | Kế toán kho & Đối soát | `inventory.read`, `inventory.cost.read`, `inventory_ledger.read`, `receipt.read`, `export_receipt.read`, `partner.read/update`, `report.inventory.read`, `report.export`, COD reconciliation & MISA/E-Invoice sync. | Excluded: physical warehouse execution (`putaway.execute`, `picking.execute`, `inventory_movement.create`). |
| **Auditor / Viewer** (`Viewer`) | Kiểm toán / Chỉ xem | Read-only (`*.read` except `inventory.cost.read` unless explicitly granted). Server-side DTO filtering strips unit cost, valuation, and concurrency tokens. | Zero mutation permissions. |

---

### 3.5 `[SEC-F06 & SEC-F07]` Session, Cookie & Token Hardening (`P2 Medium`)

1. **Fix Refresh Cookie Path (`AuthController.cs:136`):**
   - Change `Path = "/api/Auth"` to `Path = "/"` (or `/api`) with `HttpOnly = true`, `Secure = true`, `SameSite = SameSiteMode.Strict` so that both `/api/v1/auth/refresh` and `/api/Auth/refresh` receive the `erp_refresh` cookie seamlessly.
2. **Short-Lived Access Token + Memory Storage Transition (`frontend/src/services/apiClient.ts`):**
   - Keep JWT Access Token TTL short (15 minutes) and transition from `localStorage.getItem('token')` to an in-memory variable in `apiClient.ts` hydrated via `/api/v1/auth/refresh` (`HttpOnly` cookie) on page reload, eliminating XSS token exfiltration risk.

---

## 4. Segregation of Duties (SoD) & Anti-Enumeration Matrix

ERP_KHO's TO-BE security posture preserves and enforces all mandatory SoD invariants across every module:

| SoD Rule | Enforced Boundary | Error Code on Violation |
|----------|-------------------|-------------------------|
| **Inbound Maker ≠ Checker** | User who created/received `ImportReceipt` cannot approve (`receipt.complete`) the same receipt. | `403 SOD_MAKER_CHECKER_VIOLATION` |
| **Inbound Maker ≠ Poster** | User who created `ImportReceipt` cannot post (`receipt.post`) to inventory ledger. | `403 SOD_MAKER_POSTER_VIOLATION` |
| **Discrepancy Creator ≠ Approver** | User who submitted a `ReceivingDiscrepancy` cannot approve/reject that discrepancy. | `403 SOD_DISCREPANCY_APPROVER_VIOLATION` |
| **Outbound Maker ≠ Checker ≠ Dispatcher** | In 2-step `ExportReceipt`, Creator ≠ Approver (`export_receipt.approve`) and Approver ≠ Dispatcher (`export_receipt.dispatch`). | `403 SOD_OUTBOUND_SEPARATION_VIOLATION` |
| **Stocktake Counter ≠ Approver** | Operator who recorded physical count lines on a `Stocktake` cannot approve/post variance adjustments (`cycle_count.approve`). | `403 SOD_STOCKTAKE_APPROVER_VIOLATION` |
| **Last-Admin Lockout Prevention** | System blocks any role edit, user lock/deactivate, or permission revoke that leaves `0` active users in the tenant with all 8 IAM administration permissions (Spec 17 §33.2). | `409 IAM_LAST_ADMIN_LOCKOUT_BLOCKED` |
| **Cross-Warehouse Anti-Enumeration** | Accessing a resource ID belonging to another tenant or an unauthorized `WarehouseId` returns isolated `404 Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập` (never `403`). | `404 RESOURCE_NOT_FOUND_OR_ISOLATED` |
