# Permission Code Authorization — current checkpoint

Status: **PERMISSION CODE AUTHORIZATION TESTING INCOMPLETE**. This is an owner-review checkpoint, not release closure.

## Browser/security successor — checkpoint a4f4556c, 2026-09-30

Status remains **TESTING INCOMPLETE**. No runtime/schema changes were made in this successor; automated evidence is associated with a4f4556c (API 180/180, Application 346/346 as previously qualified, frontend 65/65, build/EF/lint/build PASS), not a new test execution.

Fresh browser Run **39d474c6d89a4e4faf6bd34e0e58e730** used the official configured harness, strict pre-permission bootstrap and a fresh marker-owned database. Admin browser login occurred once. Browser UI title **Quản lý kho ERP**, permission catalog/bundle labels and safe route-denial copy were verified after the earlier fixes.

| Group | Fresh browser coverage | Remaining closure evidence |
| --- | --- | --- |
| A — effective permissions | Same-session receipt.read revoke removed the menu; direct route showed Vietnamese denial. Regrant restored menu and loaded receipt list/detail without login again. | Token renewal, controlled late auth/me and receipt responses, stale JWT role downgrade and raw next-request assertions. |
| B — administration concurrency | Explicit Admin catalog/bundle observed. | True overlapping grant/revoke, stale aggregate token, concurrent last-admin, fully granted Manager and locked/inactive Admin browser assertions. Existing SQL tests are separate evidence. |
| C — authorization/idempotency | Revoke and regrant each produced one audit and one completed claim; persisted response statuses 200/200. | Unauthorized replay after permission/membership revocation, raw 401/403/404 absence of claims and fingerprint assertions. |
| D — mixed Approval Center | Vietnamese queue/empty state observed; fixture had zero pending queue entries. | Independent read/reject grant, nonzero mixed count/pagination, isolation, SoD and exactly-once replay. Empty queue is not filtering proof. |
| E — granular capabilities | Receipt route recovered after regrant. | Category/Barcode/Warehouse/Location separation, receipt-only reader, warehouse/assignment and maker/checker/poster browser matrix. |
| F — raw Viewer/errors | Safe Vietnamese route-denial UI only. | Raw Viewer payloads and HTTP 401/403/404/409 assertions. Hidden UI is not server filtering evidence. |
| G — Vietnamese UX | Title, administration labels, receipt states and Approval Center labels observed. | Full accessibility/history scan, double-submit network counts, sensitive-state clearing and controlled late-response browser evidence. |

Final database evidence before cleanup: **53 Admin grants, one Permission.Revoke audit, one Permission.Grant audit, two completed permission claims with persisted HTTP 200**. No raw idempotency key/token/credential is recorded. Native confirm interrupted browser click; Enter resolved it, and no mutation was retried. Thus this run is not double-click or replay evidence.

The official browser API exposes read-only evaluate, DOM/AX interaction, logs and pageAssets/webmcp capabilities; it does not expose network interception or raw request replay on this surface. No unsupported interception, mutating evaluate or alternative browser driver was used. Raw-network and controlled-overlap gates remain not verified. Screenshot is an ignored run artifact, not source evidence committed to Git.

### Static security review finding still open

**Membership administration optimistic concurrency is incomplete.** UserWarehouseAccess grant/revoke share the global transaction lock and last-admin guard, but read DTOs and mutation payloads carry no aggregate RowVersion. The lock serializes execution; it does not reject a stale caller after a completed membership change. This fails the canonical administration optimistic-concurrency requirement (§33.6). No claim of security closure or membership stale-token PASS is made. A minimal aggregate-token implementation and SQL/HTTP/browser regressions remain required; existing migrations were not rewritten to conceal this gap.

Static inspection reconfirmed DB permission authorization precedes the idempotency action filter; resource denial rolls back its transaction-contained claim, and successful replay reauthorizes recorded warehouse metadata. These are static findings and existing automated evidence, not a substitute for fresh browser coverage. Ponytail assessment found no need for an override/DSL/cache/designer or new dependency; retained security controls are not simplification candidates. Whole-diff correctness/security approval is still pending.

### Freshness, cleanup and remote handoff

Native final Notion precheck matched Page 17 **2026-09-30T10:05:59.344Z**, Page 18 **2026-09-28T10:04:28.018Z**, Page 282 **2026-09-28T10:04:39.216Z**, Page 41 **2026-09-20T16:31:54.616Z**. No contract mutation. Drive pagination completed: root 140 images + 5 folders, children 16/24/56/10/176 images and no nested folders. All illustrative images remain metadata-only **NOT REVIEWED**.

Exact owned cleanup succeeded: API/frontend PIDs 2388/11308 verified by manifest and stopped; run database absent; credential.dpapi removed; browser tabs absent; target listeners zero. Metadata census showed no BrowserQA/Integration databases and ERP_KHO ONLINE; no business tables were read. Ignored run manifests/logs/screenshots remain unstaged. The previously policy-blocked %TEMP%/erp-permission-audit-ae38b9232abf496d8d9ed964027a2b04 folder was left untouched.

Origin remains EyeDemon/ERP_KHO. Fresh TCP probe failed and git ls-remote failed connecting github.com:443 after 21121 ms. Live remote baseline is unverified; no push, Draft PR, TLS/proxy/credential alteration or transport switch occurred. This network blocker is separate from incomplete browser/security verification.

## Historical checkpoint a4f4556c — authority and changes

Notion native recheck: Page 17 **2026-09-30T10:05:59.344Z**, Page 18 **2026-09-28T10:04:28.018Z**, Page 282 **2026-09-28T10:04:39.216Z**, Page 41 **2026-09-20T16:31:54.616Z**, all matched their supplied baselines. No Notion mutation this successor; approval.reject remains separate from receipt.cancel/complete, inbound read requires receipt.read, scope/SoD remain independent, QC-before-Post and Post-only receipt inventory boundary are preserved.

- BrowserQA now uses configured credential-free local Integrated Security. Startup applies pre-permission migrations, provisions legitimate synthetic bootstrap identities, then applies all permission migrations. Production bootstrap remains strict.
- Fresh-run reuse is refused. Manifest records exact PID, UTC start time, command line, executable, credential identifier and browser resource fields. Cleanup verifies those values and the exact database marker; failed startup invokes guarded cleanup only for a database created by that invocation. Marker failure is fail-closed and may require explicit recovery.
- Synthetic passwords are random; no connection string or credential is emitted. Connection guard self-check rejects missing config, remote server, SQL credentials, attachment override and business-database targets without connecting.
- Inbound history presentation maps technical actions through the existing helper to Vietnamese labels, with a safe fallback. Browser title and overview heading are Vietnamese.
- Identity refresh uses a generation guard across navigation, 403 refresh, token renewal and grant/revoke refresh. A late response cannot restore permissions after a newer refresh or explicit access clearing. 403 does not replay the rejected mutation.

## Successor verification and source association

- Release build PASS, **0 warnings/errors** after the final API registry test. EF pending-model PASS. Two intermediate compiler errors in newly added tests were fixed; they are not passing evidence.
- Full API SQL/HTTP final **180/180 PASS**, artifact TestResults/Permission/BrowserSuccessor/api-final-checkpoint.trx, TRX Run 315e2448-c1af-4cc1-bf37-a73740eba566; final backend/test source includes the migration inventory-preservation extension and metadata registry. Earlier 179/179 successor and separate registry 1/1 were supporting evidence. Registry covers **66 HTTP actions in 12 migrated controllers**, catalog membership and absence of role/policy alternatives. This does not substitute for raw HTTP coverage of every action.
- Bootstrap regression **1/1 PASS**, five independently owned databases: missing Admin, inactive Admin, locked Admin, ambiguous canonical Admin role, valid bootstrap. Failed Up leaves no Permission/RolePermission table or permission migration history. Valid case exercises additive Up/Down/Up, 53 explicit Admin grants and zero grants for the unknown fixture role. The final valid fixture additionally preserves two existing location/status balances (12 + 3 Base units, zero reservations) exactly across Up/Down/Up; no ledger is fabricated. Negative fixtures have zero stock. Production Down after administration data remains forbidden without backup/data plan.
- Earlier focused bootstrap Run IDs: `6d368eab68b64b8babef2323803cb1d7`, `d7656310844f4dd28b3b3fe5aab00efa`, `44e7d13e70fd4d7ab6e55e4a633e8fd7`, `039ab9c7bc25499eaf5ea788b47f5a8b`, `ea881c8e191a4fd1bf9d04a01ea63fbc`. Each exact marker-owned database was cleaned by its fixture.
- Full Application SQL **346/346 PASS**, Run ff9f196775964dc5be41e878a9e03adc, retained because this successor changed API tests, BrowserQA scripts and frontend only; no application/infrastructure runtime change after that suite.
- Final frontend **65/65 PASS**, including late permission-response regression; lint PASS; production build PASS.

## Fresh browser supporting evidence, not closure

Run **fd2c6fd5b9844ab8b4bed2aafff545bf** used fresh marker-owned database and loopback processes. Strict bootstrap/startup succeeded. Synthetic Admin logged in once; permission UI displayed Vietnamese labels and explicit role bundles. Attempting to revoke permission.assign from the sole canonical Admin produced safe Vietnamese conflict (server log HTTP 409/guard exception); database retained **53/53 Admin grants** and **zero permission administration audits**. The old technical browser title/overview heading discovered in this run were fixed afterward; no post-fix browser proof is claimed for them or the later identity generation guard. Browser CDP click timed out during the native confirmation flow; AX inspection recovered the authoritative conflict state.

Cleanup verified the run's exact PID/time/command/executable, stopped both processes, dropped the exact marker-owned database and removed credential.dpapi; agent-created tab was closed. A timestamp-string comparison initially refused cleanup because JSON decoded as DateTime; UTC tick comparison fixed the guard without weakening ownership. Target PIDs/listeners absent. ERP_KHO metadata ONLINE; no business-table access. Runtime logs/manifests/TRX remain ignored and unstaged. Final metadata census found zero BrowserQA/Integration QA databases after suites finished; unknown resources are never dropped.

## Remaining mandatory gates

1. Complete browser matrix: next-request grant/revoke, refresh/reload/token renewal, double-submit network/effect counts, true administration races and last-admin with fully granted Manager, Viewer raw filtering, warehouse/assignment/SoD, Approval Center independent read/reject and mixed counts, unauthorized replay, fingerprint, capability separation and receipt-only reader. Current browser evidence is partial.
2. Post-fix browser evidence for late-response clearing and Vietnamese titles/history; full Vietnamese/accessibility scan remains incomplete.
3. Complete whole-diff correctness/security closure. Generated Up/Down SQL was reviewed in full: permission/role schema and grants only, transaction-wrapped bootstrap fail, no inventory/receipt/ledger/location writes; nonzero balance preservation is now covered by the final API suite. Metadata registry alone is not exhaustive HTTP evidence. Membership administration retains its existing DTO contract; aggregate stale-token coverage outside role grants requires explicit review.
4. Final public-history safety and exact staged review before any push. Stored origin base is 699f7a1; live remote baseline remains unverifiable while github.com:443 is unreachable.

## Reference freshness, review and handoff

Drive paginated metadata recheck: root **140 images + 5 folders = 145 items**; Corrected **16**, Enriched **24**, Merged/Split **56**, New Screens **10**, Deprecated **176**. No new child folders. Illustrative artifacts remain **NOT REVIEWED**, no image rendered.

Ponytail/Ponytail Review, gstack-careful, gstack-review and QA/QA-only guidance were used for minimal reuse, resource ownership, security boundary inspection and browser evidence. User authorization to preserve dirty implementation and create one checkpoint takes precedence over skill defaults to stash/revert or commit every individual fix.

Ponytail: reused existing approval presentation helper; kept SQL transaction-owned global administration serialization, no new dependencies/UserPermission/DSL/cache/designer. Earlier duplicate catalog loading was consolidated. Whole-diff security/browser closure is not asserted.

Origin remains https://github.com/EyeDemon/ERP_KHO.git. Read-only DNS succeeded; TCP 443 failed and HTTPS probe did not establish a response. Credential helper is configured; no credentials/proxy values emitted or system settings changed. GitHub handoff is BLOCKED independently of passing automated tests. No release-ready claim, main push, merge or deployment is authorized.

## Endpoint registry validated by successor metadata test

| Controller action | Permission code |
| --- | --- |
| ImportReceiptsController.Create | `receipt.create` |
| ImportReceiptsController.Approve | `receipt.complete` |
| ImportReceiptsController.Receive | `receipt.receive` |
| ImportReceiptsController.RecordQcDisposition | `quality_inspection.execute` |
| ImportReceiptsController.Post | `receipt.post` |
| ImportReceiptsController.GetAll | `receipt.read` |
| ImportReceiptsController.GetById | `receipt.read` |
| ImportReceiptsController.GetDiscrepancies | `receiving_discrepancy.read` |
| ImportReceiptsController.GetDiscrepancyReasons | `reason_code.read` |
| ImportReceiptsController.Observe | `receiving_discrepancy.create` |
| ImportReceiptsController.SubmitDiscrepancy | `receiving_discrepancy.submit` |
| ImportReceiptsController.ApproveDiscrepancy | `receiving_discrepancy.approve` |
| ImportReceiptsController.RejectDiscrepancy | `receiving_discrepancy.reject` |
| ImportReceiptsController.RecountDiscrepancy | `receiving_discrepancy.resolve` |
| ImportReceiptsController.Cancel | `receipt.cancel` |
| ImportReceiptsController.SetSupplier | `receipt.update` |
| PutawayTasksController.Locations | `location.read` |
| PutawayTasksController.CreateLocation | `location.manage` |
| PutawayTasksController.UpdateLocation | `location.manage` |
| PutawayTasksController.List | `putaway.read` |
| PutawayTasksController.Detail | `putaway.read` |
| PutawayTasksController.Destinations | `putaway.read` |
| PutawayTasksController.Assign | `putaway.assign` |
| PutawayTasksController.Start | `putaway.execute` |
| PutawayTasksController.Move | `putaway.execute` |
| PutawayTasksController.Exception | `putaway.execute` |
| PutawayTasksController.Resume | `putaway.execute` |
| PutawayTasksController.Cancel | `putaway.cancel` |
| ProductsController.GetAll | `product.read` |
| ProductsController.GetPaged | `product.read` |
| ProductsController.GetById | `product.read` |
| ProductsController.Create | `product.create` |
| ProductsController.Update | `product.update` |
| ProductsController.Delete | `product.deactivate` |
| ProductsController.SetCategory | `product.update` |
| ProductCategoriesController.GetAll | `product_category.read` |
| ProductCategoriesController.Create | `product_category.manage` |
| ProductCategoriesController.Update | `product_category.manage` |
| ProductCategoriesController.Delete | `product_category.manage` |
| ProductBarcodesController.GetAll | `product.read` |
| ProductBarcodesController.Create | `product_barcode.manage` |
| ProductBarcodesController.Delete | `product_barcode.manage` |
| ProductBarcodeLookupController.Lookup | `product.read` |
| WarehousesController.GetAll | `warehouse.read` |
| WarehousesController.GetById | `warehouse.read` |
| WarehousesController.Create | `warehouse.manage` |
| WarehousesController.Update | `warehouse.manage` |
| WarehousesController.Delete | `warehouse.manage` |
| UnitsController.GetAll | `uom.read` |
| UnitsController.GetById | `uom.read` |
| UnitsController.Create | `uom.manage` |
| UnitsController.Update | `uom.manage` |
| UnitsController.Delete | `uom.manage` |
| BusinessPartnersController.Get | `partner.read` |
| BusinessPartnersController.Create | `partner.create` |
| BusinessPartnersController.Update | `partner.update` |
| BusinessPartnersController.Delete | `partner.deactivate` |
| UserWarehouseAccessController.Get | `user_warehouse.read` |
| UserWarehouseAccessController.Grant | `user_warehouse.manage` |
| UserWarehouseAccessController.Revoke | `user_warehouse.manage` |
| AccountSecurityController.Unlock | `user.manage` |
| AccountSecurityController.RevokeSessions | `user.manage` |
| PermissionsController.Catalog | `permission.read` |
| PermissionsController.Roles | `role.read` |
| PermissionsController.Grant | `permission.assign` |
| PermissionsController.Revoke | `permission.assign` |

Mixed ApprovalsController uses document-type authorization: ImportReceipt read/history receipt.read, reject approval.reject; outbound/transfer/stocktake retain separate compatibility. Auth/me is authenticated identity, not a capability grant.

REMOTE STAGING NOT AUTHORIZED
CAPACITY EXECUTION NOT AUTHORIZED
PRODUCTION NO-GO

## Historical checkpoints — all status and remaining-work statements below are superseded

# Historical Approval Center checkpoint — superseded

Status: **PERMISSION CODE AUTHORIZATION TESTING INCOMPLETE**.

## Current authority and implemented Approval Center fix — 2026-09-30

Page 17 section 33.4.2 and the section 19 link were updated using the owner-approved decision, then read back. Native timestamp: `2026-09-30T04:19:45.404Z` → `2026-09-30T10:05:59.344Z`. Final recheck matched the new timestamp. Page 18 (`2026-09-28T10:04:28.018Z`), Page 282 (`2026-09-28T10:04:39.216Z`) and Page 41 (`2026-09-20T16:31:54.616Z`) remain unchanged. QC-before-Post and the receipt inventory boundary were not edited. No connector truncation flag was asserted.

- ImportReceipt rejection through Approval Center requires `approval.reject`; it does not alias `receipt.cancel` or `receipt.complete`.
- Direct inbound detail/history requires `receipt.read`. Queue/history filter permitted document types before count/pagination. CanApprove and CanReject check independent completion/rejection grants and maker/state restrictions.
- A document-type authorization filter establishes current database role context before idempotency replay; stale JWT Admin cannot restore inbound capability/global scope. Outbound/transfer/stocktake retain their separate compatibility behavior.
- Additive EF migration `20260930100818_AddInboundApprovalRejectPermission` seeds the code for Admin/Manager only. Down removes its grants/catalog entry; production grant data requires backup and a reviewed rollback plan. Earlier migrations remain intact.
- Permissions screen catalog/role loading is consolidated with request-version cancellation. Approval UI uses Vietnamese document/state mappings, granular inbound action guards, safe error text and permission-change clearing.

## Successor automated evidence

- Release solution build: PASS, zero warnings/errors.
- EF pending-model check: PASS.
- Full Application SQL: **346/346 PASS**, Run `ff9f196775964dc5be41e878a9e03adc`; exact owned database `ERP_KHO_Integration_20260930_101312_ff9f1967` was cleaned by the official harness.
- Full API SQL/HTTP: **178/178 PASS**, `TestResults/Permission/ApprovalFinal/api-approval-final.trx`. Real HTTP regression covers independent read/reject grants, next-request changes with stale Admin header, maker denial, cross-warehouse detail/history/reject 404, successful replay exactly one rejection audit, changed fingerprint 409, revoked membership replay 404 and revoked permission replay 403.
- The intermediate API successor attempt was **174/176**, failing an obsolete Checker-policy assertion and a UTC fixture that omitted real warehouse membership. Assertions/fixture were corrected; runtime isolation was not weakened.
- Frontend final standalone suite: **64/64 PASS**; lint PASS without warnings; production build PASS. The earlier **61/64** attempt exposed missing safe correlation display and was superseded after the fix.

## Reference freshness and scans

Drive pagination: root **140 images + 5 folders = 145 items**; Corrected 16, Enriched 24, Merged/Split 56, New Screens 10, Deprecated 176; no new child folders. All artifacts are metadata-only **NOT REVIEWED** and remain illustrative references.

Public-history candidate scan inspected 292 historical blobs from the locally stored remote base `699f7a1` and current implementation files. No binary/runtime artifacts or invalid UTF-8 were detected. Credential-pattern hits in historical BrowserQA scripts are variable references to runtime-generated synthetic passwords, not literal secrets. This is a heuristic scan, not an assertion of exhaustive secret detection. The live remote baseline could not be verified because Git cannot connect to github.com:443.

## Remaining gates and handoff blockers

1. Browser full-stack matrix is NOT RUN for this successor. Existing BrowserQA startup migrates an empty database before provisioning bootstrap Admin and uses a hardcoded local connection. It must be adapted to configured official ownership/bootstrap rules before execution; no BrowserQA resource was created by this checkpoint.
2. Complete bootstrap-negative and migration Up/Down verification, exhaustive endpoint registry/HTTP coverage and whole-diff security review. Aggregate concurrency, canonical unlocked last-admin and bootstrap-positive SQL tests already pass; these are no longer implementation gaps, but browser administration races remain unverified.
3. Finish frontend inbound history technical-action mapping and exhaustive Vietnamese/accessibility/browser checks; no full Vietnamese closure is claimed.
4. Global run-resource census/ERP_KHO ONLINE metadata verification and exact helper cleanup remain pending. Official test databases have per-run cleanup evidence; this does not imply a global zero-resource census.
5. GitHub remote connectivity blocks push and Draft PR. No commit/push/PR was created. Checkpoint commit is authorized but awaits exact staged review, cleanup and security-safety completion.

Ponytail finding (duplicate catalog/role load) was fixed without adding dependencies. No full-diff `Lean already. Ship.` or security closure is claimed.

REMOTE STAGING NOT AUTHORIZED
CAPACITY EXECUTION NOT AUTHORIZED
PRODUCTION NO-GO

## Historical checkpoints — superseded by current status above

All status, remaining-work, no-commit and test statements below describe their own historical checkpoint. They are not the current implementation or closure status.

# Permission Code Authorization — implementation checkpoint

Status: **PERMISSION CODE AUTHORIZATION TESTING INCOMPLETE**.

This is an implementation checkpoint, not closure evidence. No permission implementation commit has been created.

## Canonical authority

Notion Page 17, `ERP WMS – Permission Registry & Authorization Matrix`, section 33 is authoritative. The earlier read-back timestamp `2026-09-28T10:04:14.778Z` is historical; the current owner-approved capability-separation baseline is `2026-09-30T04:19:45.404Z`. The current implementation must preserve warehouse/resource isolation, separation of duties, assigned-operator rules and server-side Viewer classification independently of capability grants.

Permission grants come from `RolePermission`. The server queries the database per request. Role claims do not grant migrated capabilities. User overrides, deny engines, scheduled grants, distributed caches and permission condition DSLs remain deferred.

## Implemented working-tree foundation

- Permission and RolePermission entities, catalog uniqueness, grant rowversion, generated EF migration/Designer/model snapshot.
- Explicit catalog and deterministic Admin/Manager/WarehouseStaff/Viewer bundle backfill.
- Bootstrap migration now rejects absence of an active, unlocked canonical Admin; empty database is not an exception. QA fixtures must provision a legitimate fixture bootstrap before applying this migration. No runtime or migration synthetic administrator is created.
- Database permission authorization filters on receipt, discrepancy, Putaway and selected master endpoints. Recount uses `receiving_discrepancy.resolve`.
- Active/locked account checks and fail-closed permission-store behavior.
- Database role context for migrated requests, preventing a stale Admin JWT role claim from restoring global-admin or Viewer-classification behavior.
- Authenticated `/api/auth/me` permission payload.
- Initial catalog/role-grant administration API. Its concurrency and last-administrator protection are not yet closure-ready.
- Reactive frontend permission set, migrated route/menu guards, identity reload on navigation, and refresh after 403 without automatic replay of the rejected mutation.
- Outbound/stocktake/reservation role compatibility remains outside the inbound capability cutover. Shared helpers no longer accidentally apply Putaway grants to those workflows.

## Fresh checks at this checkpoint

- Release solution build: PASS, zero warnings/errors.
- Focused API authorization/runtime/controller checks: 80/80 PASS.
- Frontend suite after reactive permission changes: 54/54 PASS.
- Frontend lint: PASS after the last layout changes.
- Frontend production build: PASS after the last layout changes.
- EF pending-model check: PASS; bootstrap SQL change does not change the EF model.
- `git diff --check`: PASS. Current changed/untracked implementation paths: strict UTF-8 validation found zero invalid files, runtime-artifact path scan found zero matches, private-key marker scan found zero matches. These limited scans do not replace the final comprehensive secret/security review.
- Full Application SQL, full API SQL/HTTP and browser authorization matrix: no closure PASS is claimed.
- Earlier broad API attempt failed because legacy metadata/fixture expectations had not been migrated and isolated SQL configuration was not passed to that invocation. It is not accepted full-suite evidence.

## Mandatory remaining work

1. Complete exact endpoint-to-permission mapping, including mixed approval surfaces, master dependencies and permission administration. Review all service role checks: capability checks must not depend on role names; independent assignment/SoD/Viewer classification remains intact.
2. Finish grant/revoke aggregate concurrency, concurrent last-admin lockout protection, idempotency/no-op semantics and all relevant role/user/warehouse-administration paths. A per-grant stale check alone is insufficient for the complete administration contract.
3. Update SQL/API fixtures for deterministic bootstrap-before-permission-migration and explicit grants. Mock JWT role switching must not stand in for real database role/grant changes.
4. Complete granular frontend action guards, sensitive-state clearing on denied/revoked access, Vietnamese permission-administration UX and corresponding regressions. Verify permission refresh and token renewal across reloads.
5. Review migration SQL, unknown-role reporting, bootstrap scenarios and rollback protection on owned QA databases.
6. Run fresh full Application SQL, API SQL/HTTP, frontend and browser matrices using unique Run IDs and exact ownership markers. No accepted Putaway test result substitutes for permission verification.
7. Recheck all required Notion sources and Drive recursively. Drive images remain NOT REVIEWED until actually rendered. No fresh Drive count is asserted by this checkpoint.
8. Complete Ponytail and correctness/security review, final scans, related inbound traceability documentation and exact resource cleanup before staging or committing.

## Safety and release gate

This checkpoint did not create a QA database or browser/server process and did not access ERP_KHO business tables. Main and user-owned files remain outside the implementation diff. `.npm-cache/` remains untracked and excluded from staging. Global owned-resource cleanup and ERP_KHO ONLINE verification remain mandatory before closure; they are not inferred from this statement.

REMOTE STAGING NOT AUTHORIZED

CAPACITY EXECUTION NOT AUTHORIZED

PRODUCTION NO-GO

## Owner static-review fixes — fresh verification checkpoint, 2026-09-30

The owner snapshot (530 verified files, SHA-256 `C9B0ACC062EF4A9F2CC3F9F46FCD07E90743A98E85301024DD892415DBC453A1`) is static supporting evidence. Sanitized snapshot files were not copied back into the repository. The implementation remains uncommitted and is not release-ready.

### Changes and evidence

- Role grants now use an aggregate Role RowVersion/GrantRevision. The additive EF-generated migration is `20260930091327_AddRoleGrantAggregateConcurrency`; historical permission migrations were retained.
- Permission administration uses a transaction-owned SQL Server application lock shared across roles and relevant administration mutations. Projected mutation, canonical active/unlocked Admin validation, audit and idempotent response remain in the same transaction. A Manager with all eight management grants does not count as the final canonical Admin.
- Grant/revoke with a fresh aggregate token supports an explicit no-op response (`changed=false`) without another business audit; stale aggregate tokens conflict. This behavior still requires browser closure.
- User-warehouse administration checks database capability grants and current database role/scope rather than a global-Admin JWT claim. Audit rows carry warehouse scope for replay authorization.
- Account unlock and administrative session revocation use `user.manage`, a shared administration transaction/lock and canonical administrator validation. There are no user role-change/disable/delete or role-disable/delete controller endpoints to manufacture for this slice.
- Official SQL fixtures provision legitimate bootstrap users before applying permission migrations. Rollback test fixtures follow that sequence too; production bootstrap validation was not weakened.
- SQL-specific Permission collation and generated Role/RolePermission rowversions are configured only for SQL Server. SQLite regression models retain explicit concurrency tokens instead of unsupported SQL Server generation.
- Receipt UI action guards are independent; receipt detail loads separately from optional discrepancy/reason data. Late detail/print responses cannot restore data after receipt-read revocation. Product/category/barcode controls and the minimum permission catalog/role-grant screen use Vietnamese presentation. Token renewal refreshes effective permissions; rejected 403 mutations are not automatically replayed.
- Latest Application full suite: **346/346 PASS**, Run ID `c85dbc92de814231a7f3d02bb1190cbc`, including account-security permission changes and corrected explicit-grant fixtures. The earlier passing `2d93b9f42f3d4ca0817e4eec8add3401` is supporting evidence. Three real SQL regressions passed: aggregate grant/revoke overlap, cross-admin last-administrator write-skew prevention, and locked canonical Admin versus fully granted Manager. The intermediate `54a5d6a6c70a45c48202b189935a71ae` run was 344/346 because two account-security fixtures still assumed a mock Admin flag; fixtures were corrected without weakening runtime enforcement.
- Earlier failed Application Run `285f99a9361846f68a8d83bf249b1a13`: 307/346 PASS, 39 failures (36 provider-collation, two bootstrap fixtures, one obsolete role-backed warehouse-administration fixture). It is superseded by the passing run; its database was cleaned.
- Fresh API full suite including database-role Viewer correction and action-result 401/403/404 rollback: **175/175 PASS**, `TestResults/Permission/ApiDenialFinal/api-permission-denial-final.trx`. The intermediate 174/174 run is supporting evidence; the earlier 173/174 attempt is not closure evidence. The denial regression proves zero durable claim, success audit and stock effect for all three action-result statuses.
- Frontend: **64/64 PASS** after granular guards and permission-screen regressions. Lint passed without warnings; production build passed. Latest Release solution build passed with zero warnings/errors; EF pending-model check passed.

### Fresh authority/reference reads

Page 17 `2026-09-30T04:19:45.404Z`, Page 18 `2026-09-28T10:04:28.018Z`, Page 282 `2026-09-28T10:04:39.216Z`, and Page 41 `2026-09-20T16:31:54.616Z` were fetched again and matched their native baselines. No Notion page was edited in this checkpoint. QC-before-Post remains canonical. The connector did not provide a truncation flag.

Drive pagination completed: Root **140 images + 5 folders = 145 items** (two image pages); Corrected 16, Enriched 24, Merged/Split 56, New Screens 10, Deprecated 176 (two pages). No additional child folder was returned. Approval Center and User/Role/Permission artifacts were metadata-only **NOT REVIEWED**; Drive is illustrative, not security authority.

### Remaining blockers

The ImportReceipt reject action on `/api/approvals/ImportReceipt/{id}/reject` has no explicit mapping in Page 17 section 33. Section 19 mentions generic `approval.reject`, but that code has no explicit MVP bundle in section 33. An owner decision is pending; neither `receipt.cancel` nor a new seeded `approval.reject` grant has been inferred. The mixed Approval Center remains a security blocker until document-type filtering, inbound database role/resource context and mutation mapping are implemented and tested. No authorization closure is claimed while that bypass surface remains.

Also pending: complete endpoint registry, remaining raw HTTP/replay and bootstrap-negative coverage, successor automated suites for final changes, full browser matrix, migration SQL/rollback review, final whole-diff Ponytail/security review, comprehensive scans and global owned-resource cleanup verification. No browser/server resource was created by this checkpoint. No staged file, implementation commit, push, merge or deployment was authorized by passing individual suites.

API closure-candidate run used seven independently marker-owned databases; the TRX records seven creations and seven matching cleanups. Run IDs: `1b1d3fd8c7604fea886d4aa34c8da3ab`, `7428c089b40c41909687e555c28e0cf2`, `eb8533e9a9ec49109f577f9511357a44`, `84aa7a99f8334aecb802874711a0a5cb`, `f0287d6b17f64046a91ce6d6c012477a`, `d1da15156f9f40ddb4f58eb652da3437`, `172e345debbe45eea2bb7998b1642623`. This proves cleanup for those exact resources, not an inferred global cleanup status.

Focused Ponytail inspection found duplicated catalog/role loading in `Permissions.tsx` (manual reload versus effect); consolidate after preserving stale-response cancellation. No whole-diff lean/security approval or `Lean already. Ship.` is claimed. Runtime security blocker remains mixed Approval Center. UTF-8/private-key-marker/runtime-path scans of current implementation paths passed; these limited checks do not replace the comprehensive final secret scan.

## Owner-approved capability separation — 2026-09-30 checkpoint

Page 17 was updated with the owner-approved action map and explicit additional bundles, then fetched again. Native timestamp changed from `2026-09-28T10:04:14.778Z` to `2026-09-30T04:19:45.404Z`. The connector did not provide a truncation flag. Pages 18, 282 and 41 were not edited; QC-before-Post remains unchanged.

The new independent codes are `product_category.read`, `product_category.manage`, `product_barcode.manage`, `warehouse.read` and `warehouse.manage`. Admin/Manager receive all five, Viewer receives Category/Warehouse read, and WarehouseStaff receives Warehouse read. Product update does not grant Category master or barcode management; Location management does not grant Warehouse management. Existing canonical grants remain intact.

Working-tree changes map Category, barcode mutation and Warehouse endpoints to these codes, update Warehouse menu/route/action guards, and separate Product update, Category selection/master management and barcode mutation controls. A new additive migration, `20260930042116_AddSeparateMasterPermissionCapabilities`, seeds the five codes and deterministic role additions without rewriting earlier migration history. Generated SQL review and owned-database migration verification are still pending.

Fresh checks for this checkpoint:

- Release solution build: PASS, zero warnings/errors.
- Focused authorization metadata/runtime permission tests: 41/41 PASS after replacing an obsolete barcode assertion. These tests do not constitute SQL-backed HTTP closure.
- Product frontend tests: 7/7 PASS, including independent Category and barcode guards.
- EF pending-model check: PASS.
- `git diff --check`: PASS; no staged files.

The idempotency filter now rolls back action-result 401/403/404 rather than persisting an unauthorized cached response. This production change is not closure evidence until SQL/HTTP regression and affected full-suite/browser verification pass.

Still blocking closure: role-aggregate grant/revoke concurrency; canonical active/unlocked last-admin protection against write-skew; bootstrap-before-migration official fixtures; remaining endpoint/service policies and granular frontend guards; Vietnamese presentation completeness; full SQL/API/frontend/browser verification; final Drive/Notion checks, security/Ponytail reviews, scans and cleanup verification. No READY, final-review PASS or full-suite PASS is claimed. No permission commit, QA database or browser/server resource was created by this checkpoint.
