# Duyệt, giữ hàng và xác nhận xuất kho

Trạng thái: **TESTING INCOMPLETE — LOCAL VERIFICATION PASS; REMOTE RECONCILIATION / SUCCESSOR CI BLOCKED**. Base source: `cda05eba56d3cfb41a3df803bb48d0c84e05940f`; worktree riêng, branch `feature/outbound-dispatch-mvp`. PR #1 vẫn Draft và là dependency, không phải evidence của slice mới.

## Authority và read-back

Owner đã chốt ExportReceipt MVP, không thay thế Shipment canonical. Các addendum đã được ghi và đọc lại trực tiếp trước sửa source:

| Notion | Before native timestamp | After native timestamp |
| --- | --- | --- |
| 17 §34 Permission/action registry | 2026-10-03T15:19:58.948Z | 2026-10-04T02:21:04.902Z |
| 01 ExportReceipt state addendum | 2026-09-24T16:04:16.627Z | 2026-10-04T02:21:07.171Z |
| 30 Reservation §20 | 2026-09-17T19:47:03.149Z | 2026-10-04T02:21:08.881Z |
| 38 Shipment §14, separate model | 2026-09-17T19:49:07.648Z | 2026-10-04T02:21:10.317Z |
| 228 UX addendum | 2026-09-24T16:04:39.625Z | 2026-10-04T02:21:11.759Z |
| 229 Screen Matrix addendum | 2026-09-24T16:04:50.257Z | 2026-10-04T02:21:14.850Z |
| Dispatch screen row | 2026-09-19T14:54:37.404Z | 2026-10-04T02:21:16.451Z |

Connector returned content and native timestamps; it did not provide a truncation flag. Page 41/QC-before-Post and the receipt Post-only inventory boundary are unchanged. Historical contract-gap statements in [backlog audit](NEXT_MVP_BACKLOG_AUDIT.md) are superseded by these owner decisions, not runtime closure.

## Implementation plan reviewed before coding

Ponytail and plan-eng-review: reuse ExportReceipt, reservation service, eligible stock repository, unit of work, database permission filter, idempotency transaction and existing browser runner. No Shipment/Allocation/Picking/Packing/HU aggregate, authorization engine or new dependency. The required cross-layer files protect one existing workflow; reducing them to UI guards would leave alternate service/Approval Center/replay entry points unprotected.

1. Add nullable Base UOM snapshots on new ExportReceipt lines and receipt RowVersion through EF tooling. Existing quantity is directly consumed as base stock quantity by the legacy export/reservation services; legacy lines do not preserve a historical UOM. Keep their snapshots null, document this limit, and never infer a historical conversion/name from current master. Preserve old enum values, modes, reservations and ledger.
2. Seed six exact `export_receipt.*` grants additively: Admin/Manager all six; WarehouseStaff read/create/update/dispatch; Viewer read; unknown roles none. No aliases or wildcard. Keep existing `approval.reject` grants.
3. Cut over receipt controller and shared service to database permissions. Explicit reserve/immediate commands keep their named semantics irrespective of configuration; `/approve` remains legacy configuration compatibility, with original audited mode used on replay. Maker differs from checker; two-step checker differs from dispatcher without Admin override.
4. Protect receipt mutation with aggregate concurrency, then sorted product/location conditional stock updates in the existing transaction. Return actual consumed location quantities from reservation consumption and write matching immutable Export ledger rows. Any partial failure rolls back all effects. Reserve/cancel do not write export ledger or reduce On Hand.
5. Approval Center filters ExportReceipt by read grant before count/pagination; reject requires both `approval.reject` and `export_receipt.cancel`. Preserve transfer/stocktake compatibility. Viewer omits private note, cost/value and technical tokens.
6. Frontend uses granular grants for menu/route/actions and independent optional master loads; render Vietnamese state/mode/history/errors. Reuse generation guards and safe error presentation; synchronous mutation guard and fresh token payloads, no automatic mutation replay.
7. Focused tests first, followed by fresh affected full suites and owned full-stack browser cases. Review actual migration SQL, rollback limitations, public history, staged files and resource cleanup before commit/push.

## Acceptance matrix / evidence to collect

| Case | Required verification | Postcondition |
| --- | --- | --- |
| Approve/reserve | SQL-backed service/HTTP + UI | Approved; reservation increases; On Hand unchanged; no export ledger |
| Dispatch | SQL-backed HTTP + UI/browser HTTP | Consumed reservation; exact stock decrease; location-traceable ledger; one success audit/effect |
| Cancel and Dispatch/Cancel race | True overlapping SQL/HTTP and browser requests | One winner; release or consumption exactly once; no partial inventory effect |
| Competing reservations/dispatches | Controlled overlap | No over-reservation/negative stock/duplicate dispatch |
| Retry/fingerprint/revocation | Browser-origin HTTP + SQL claims/audit | Same-key exactly once; changed payload 409; revoked permission/membership cannot replay cached success |
| Scope and SoD | Real database grants + HTTP/browser | Foreign direct ID 404; missing grant 403; maker/checker/dispatcher remain independent |
| Eligible inventory | Seed mixed status/locations; raw HTTP and SQL | Only AVAILABLE at active/unblocked/pickable locations; no RECEIVING/DAMAGED/REJECTED deduction |
| UOM/precision/legacy | New and legacy SQL fixtures | New immutable base snapshot survives master changes; no rounding; old totals/history preserved |
| Viewer and late response | Raw JSON + component + delayed browser | No sensitive/token leakage; revoked access cannot be restored by old list/detail/print response |
| UX/double-submit | UI workflow and request/effect counts | Vietnamese labels/status/errors/accessibility; one mutation |
| Integration/migration | Release/EF, Application/API/frontend full suites | Inbound/QC/Putaway and compatibility remain valid; additive migration only |

Static review, automated verification, UI workflow, HTTP from browser and SQL postconditions will be recorded separately. Current automated checkpoint: focused SQL-backed API/mapping/middleware **46/46 PASS**, service regression **36/36 PASS**, runner guards **18/18 PASS**, Release build **0 warnings/errors**, EF **no pending model changes**. Full Application successor **352/352 PASS, 0 skipped**, Run `818a15875e5e420e8f7e27a7f44eb489` on 2026-10-05; its exact owned database was cleaned. Full API successor **204/204 PASS, 0 failed/skipped**, 32 exact marker-owned databases created/cleaned; representative Runs `ccc72b61dc464cd7af855809a8cfba32`, `af795360e1df47be9e7e2b933a5c3d5a`, `806a0d2a9a7146009f4bc32b61a9cabe`. Frontend full successor **100/100 PASS, 19 files**, using worker threads; focused successor **9/9 PASS** after correcting a test selector type. Lint, production build and dependency audit **PASS, 0 vulnerabilities**. Fresh browser closure below: all seven local groups are covered across a six-group run and a focused mounted successor. Remote reconciliation and CI for a reconciled final commit remain blocked; automated PASS alone is not browser closure. Accepted inbound CI 352/189/89 is historical and not successor evidence for this runtime change.

## Findings and successor association

All new evidence belongs to the uncommitted outbound working diff over accepted `cda05eb`, not to the inbound CI result. The runner records relevant source hashes; test artifacts are ignored and remain outside commits.

- Location ledger initially hit the legacy five-column unique export index when dispatch consumed multiple locations. Additive migration `20261004073800_AddOutboundLocationLedgerUniqueness` includes LocationId; real SQL verifies exact per-location deductions and a downgrade guard when old uniqueness cannot be restored safely.
- Export create action-result 400 initially retained an idempotency claim. Export command failures now roll back the encompassing transaction; invalid quantity/eligibility HTTP regressions verify zero claim/audit/stock effects.
- RECEIVING is explicitly excluded even if a location is incorrectly marked pickable. Cancellation can release holds on locations subsequently blocked/inactive without creating a physical effect.
- Viewer classification accepts canonical normalized role spelling while preserving database authority; raw response regression checks property omission.
- Generic 500 no longer exposes the outer provider/EF exception message. The middleware regression checks a safe Vietnamese message and absent technical detail.
- Historical test assumptions were updated for exact outbound grants, location-aware ledger uniqueness and isolated 404 wording. Failed full runs remain historical evidence, not hidden: Application Run `c37dccbd5a994b61ab52cca0dc99d253` had 350/352 PASS (two outdated message assertions); frontend had 97/99 PASS (two old export fixtures without outbound grants). Both fixture issues are fixed; full successors are required.

- The intermediate Application run `1aed9ee2ddd74f4a88382d153b2a5827` had 351/352 PASS: an old schema-probe helper left an EF connection open across Up/Down/Up. The helper now uses EF scalar querying; successor `818a15875e5e420e8f7e27a7f44eb489` passes the migration cycle and all 352 tests.
- The intermediate API full run had 201/203 PASS: the old InMemory WarehouseStaff fixture lacked explicit export grants, and an old customer-update request lacked the newly required idempotency header. Fixtures are corrected; real SQL/HTTP now verifies WarehouseStaff create and next-request revoke denial with zero new receipt/audit/claim effect. Focused successor **40/40 PASS**; full API successor **204/204 PASS**. Runtime was not weakened for these fixtures.

The first generated migration `20261004023307_AddOutboundDispatchMvp` adds the receipt aggregate token, nullable historical Base UOM snapshots and six explicit permission bundles. Existing historical migrations are unchanged. Generated Up/Down SQL was reviewed; fresh SQL tests exercise upgrade/downgrade/legacy totals and guarded failed downgrade.

## Fresh browser verification (2026-10-05)

Run `78f398675e2f4107b85eb9f9b0c0ff3a` failed startup at CREATE DATABASE timeout; exact metadata showed its database absent and no API/frontend/credential created. It is not browser evidence.

Run `d1c53877dfcd4ca29d0b9fbba01cfec6` on the current runtime: **6/7 groups PASS; mounted-delay group FAIL**. Failed evidence is retained as historical, not silently promoted.

| Browser group | Result / evidence |
| --- | --- |
| UI create → reserve → dispatch | PASS; 201/200/200; double-click reserve network count 1; reserve holds 25, On Hand 100, ledger 0; independent dispatcher produces On Hand 75, reserved 0, ledger quantity 25 at the actual location |
| Replay/fingerprint/revoked grant or membership | PASS; 200 retry exactly once, 409 changed payload, 403 revoked approve, 404 revoked warehouse; no additional effect/audit/claim; cancel releases without physical change; raw Viewer price/token/private content absent; safe 401/403/404/409 |
| Reserve, dispatch/cancel, duplicate dispatch races | PASS; three controlled overlapping pairs each 200/409, observed pending for 250 ms under a QA-only SQL lock; no oversell, one physical outcome, duplicate dispatch one audit/claim/effect |
| Approval Center | PASS; nonzero count 15→12 when three Export entries excluded, pageSize 1; independent read/reject/cancel; maker 403, foreign warehouse 404, rejection replay one audit and no physical effect |
| Base UOM/quantity | PASS; immutable precision 4/name Cái after master precision/name change; exact 0.0001 dispatch; zero/negative/overflow 400 with zero effects |
| Ineligible locations/status | PASS; create 400 and legacy reserve 409; six excluded buckets total On Hand 600, reservation/ledger 0, no claim or success audit |
| Mounted old list/detail/print | FAIL in capture at the first 401 after reload, before any delay assertions. Access token is memory-only; reload begins normal refresh. Runner coordination is corrected to settle refresh, then use a real grant/context change to load the authorized list while mounted. Superseded only for this group by focused Run `d563d10814c94f8fb8e510dbd4c2fbbb`, **PASS 3/3 mounted list/detail/print delays**. |

UI workflow, authenticated HTTP from a browser and SQL setup/postconditions are classified separately. Approval Center count/races/raw filtering above are browser HTTP, not a user-click approval workflow. Mounted tests use the test-only host and unchanged production component, not an unmount workaround.

Run d1 cleanup: browser contexts/server closed, remaining profiles 0; exact PID/start/executable/command and database marker checked; API/frontend stopped, database absent, DPAPI credential removed. No ERP_KHO business table was read.

Focused Run `d563d10814c94f8fb8e510dbd4c2fbbb`: list/detail/print each held a real authorized 200 response through revoke → real 403 identity refresh → regrant → fresh data → old response release. Original heading stayed connected throughout; old identifiers, detail and print dialog did not return. First list load is triggered by a real dispatch-grant/context change after normal reload refresh settles, not by a synthetic permission event. Three delays: 08:55:22.506Z→08:55:24.327Z, 08:55:25.029Z→08:55:26.558Z, 08:55:26.831Z→08:55:28.573Z. This runner-only correction does not change runtime/model or invalidate the six earlier groups. Runner guards/error propagation successor **18/18 PASS**.

Final local runtime/test file aggregate SHA-256 (docs excluded): `a4fd8756fa6d92ea1abb3acdeef13aa3ac05e22653047ec7804ad271cb2558ae`. The ignored source manifest contains per-file hashes; browser evidence records relevant runtime/runner hashes. These results belong to the uncommitted outbound implementation over cda05eb, not to the newly discovered remote implementation.

Final cleanup verified: both browser manifests report Closed=true and RemainingProfiles=[]; exact owned API/frontend stopped, DPAPI credentials absent, all BrowserQA/Integration targets absent in sys.databases, loopback listeners 5265/4175=0. ERP_KHO ONLINE was checked only in metadata; no business table access. Ignored evidence/log/TRX/dist remain unstaged; UNKNOWN/cache/policy-blocked resources were preserved.

Frontend intermediate default-fork execution had **92 PASS**, one worker startup timeout and eight tests not executed; no assertion failure. Full threads successor 100/100 and corrected-selector focused 9/9 are closure evidence. Production build initially caught unsupported `exact` in a Testing Library role selector; regex selector fixes the test type, with no runtime change.

Notion final direct recheck on 2026-10-05: all seven owner addenda timestamps above plus 18/282/41 match (**UNCHANGED**); no contract conflict. No Notion change was made to legitimize implementation.

## Review and handoff blocker

Internal correctness/security review traced controller/service/Approval Center/replay, database-role scope, SoD, aggregate locks/version, conditional location updates, actual location ledger, transaction/audit/idempotency, Viewer filtering, Base UOM snapshots and frontend generations. No unresolved High/Critical finding in the verified local diff. Ponytail whole-diff result: **Lean already. Ship.** This is complexity review only, not independent review or production approval. Warehouse-level location lock has an explicit ponytail ceiling; no speculative engine/dependency added.

Strict UTF-8/mojibake check, diff check and 66-file binary/runtime/private-key/token scan PASS. Two password-pattern matches are the known in-memory `password = undefined` clears, not literal secrets. Index remains empty; exact staged review and public new-commit/history review must still run when Git reconciliation is authorized.

Live origin remains EyeDemon/ERP_KHO. Inbound accepted base remains `cda05eba56d3cfb41a3df803bb48d0c84e05940f`, PR #1 untouched/Draft, main remains `699f7a1e7eb338eabdf17666b187a43133ab8af0`. Unexpected remote outbound HEAD `2ba782d7518be12daed0056fa66ab44725c922e0` contains 13 commits over the same base, created outside this local worktree. It was fetched/read only; no merge/reset/cherry-pick/rewrite/push occurred.

Three concrete differences require ownership/reconciliation before changing history or migration order:

- Remote `20261005143000_AddOutboundReceiptPermissions` seeds the same six codes as the local generated additive migration. Their combined ordering, grants and upgrade/Down must be reviewed; do not blindly apply two competing seeds.
- Remote migration grants WarehouseStaff `export_receipt.cancel`, contrary to the explicit four-code Staff bundle recorded in Notion §34.
- Remote service rejects approve-and-dispatch and makes legacy approve always reserve, whereas owner contract retains explicit immediate compatibility with both grants and original-mode replay authorization.

Owner clarification is pending on whether/how to preserve and integrate those remote commits. Local HEAD remains cda05eb, 66-file working diff preserved, no staging/commit/push/new PR. GitHub search found no outbound PR at this checkpoint. **No successor CI exists for this verified local diff** because handoff is blocked, not because local tests failed. Do not use a remote branch's CI as local closure evidence.

## Deployment and rollback

Deploy additive schema/catalog/backfill before permission-backed runtime/frontend. Do not drop snapshot/grant columns blindly after production changes: backup plus reviewed data plan required. Schema rollback is not a business reversal. No ERP_KHO business tables are accessed in this work.

Drive: 140 root images + 5 folders = 145 entries; subfolders 16/24/56/10/176 at the fresh recursive metadata recheck on 2026-10-05, with all provider pagination exhausted and no additional nested folders. Artifacts remain metadata-only **NOT REVIEWED**.

Deferred: separate canonical Shipment/HU/Allocation, alternate operation UOM outbound, returns/reversal, advanced allocation rules, membership UI (DEFERRED_BY_OWNER). No Dashboard work.

REMOTE STAGING NOT AUTHORIZED

CAPACITY EXECUTION NOT AUTHORIZED

PRODUCTION NO-GO
