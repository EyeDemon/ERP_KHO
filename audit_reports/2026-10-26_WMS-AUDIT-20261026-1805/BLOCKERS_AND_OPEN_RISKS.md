# BLOCKERS AND OPEN RISKS (`WMS-AUDIT-20261026-1805`)

- **Audit Run ID:** `WMS-AUDIT-20261026-1805`
- **Active Blockers (`BLOCKED_AUTH`, `BLOCKED_ENV`, `BLOCKED_DATA`):** `0`
- **Open Unresolved `P0` / `P1` / `P2` / `P3` Defects:** `0`

---

## 1. Blocker Status Summary

| Blocker Category | Count | Status | Notes |
|---|---:|---|---|
| `BLOCKED_AUTH` | `0` | Resolved / None | Authenticated as `qa_admin` (`Id=712`, `Admin`, `124` permissions) and verified `Viewer` (`qa_viewer_wh01`) RBAC restrictions in live browser (`TC-RBAC-001`, `TC-RBAC-002`) + unauthenticated `401` enforcement (`TC-SEC-001`). |
| `BLOCKED_ENV` | `0` | Resolved / None | Frontend (`http://localhost:5173`) and Backend (`http://localhost:5260` connected to `(localdb)\MSSQLLocalDB` / `ERP_KHO`) are online and verified end-to-end. |
| `BLOCKED_DATA` | `0` | Resolved / None | `ERP_KHO` contains full operational seed data across `WH01`, `WH02`, and `WH-MAIN` (`12` 9-dim stock rows, `28` ledger entries, POs, ASNs, Import/Export Receipts, Putaway/Picking Tasks, Packing Sessions, Shipments, Stocktakes, Stock Transfers, 3PL Partners, and Marketplace Shops). |

---

## 2. Operational & Governance Risk Register (Monitored / Mitigated)

| Risk ID | Area | Description | Mitigation Verified | Residual Risk Level |
|---|---|---|---|---|
| `RSK-OPS-001` | Windows File Locking on Running `ERP.Api.exe` | When `ERP.Api.exe` is running directly from `ERP.Api\bin\Debug\net10.0\`, Windows locks `.dll`/`.pdb` files in that folder (`MSB3021`), which previously caused `FND-DASH-002` (stale binary). | Build verification and CI builds use `-c Release` (`ERP.Api\bin\Release\net10.0\`) and stop/hot-swap `ERP.Api.exe` before restarting the runtime server. | `LOW` |
| `RSK-SEC-001` | Short-Lived 20-Minute JWT Access Token Expiry | Access tokens expire after 20 minutes, causing a transient `401 Unauthorized` response on the first request after expiry before `apiClient.ts` rotates the refresh token (`POST /api/auth/refresh`). | Verified automatic token rotation (`Authentication.RefreshRotated` in `AuditLogs`) transparently retries the failed request and succeeds (`200 OK`) without logging the user out. | `LOW` |
| `RSK-GOV-001` | Notion Spec Blacklist & Read-Only Spec Policy (`DOC-001`) | Pages `OWN-OOAD-001` through `OWN-OOAD-004` are strictly blacklisted, and Notion specs must never be overwritten by AI agents. | Zero blacklisted pages were searched, opened, or cited. No Notion write tools were invoked. | `NONE` |
