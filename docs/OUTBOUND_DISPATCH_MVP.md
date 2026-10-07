# Duyệt, giữ hàng và xác nhận xuất kho

Trạng thái local: **RECONCILIATION, MIGRATION, AUTOMATED/BROWSER GATES AND CLEANUP PASS**. Gate cuối là successor CI của merge commit; kết quả CI/owner-review readiness được ghi trong Draft PR mới, không suy diễn từ local PASS. Accepted base: `cda05eba56d3cfb41a3df803bb48d0c84e05940f`. Local checkpoint `e7a9b3221c459f7dbc5bd0ff2021cc5030d45b43` giữ nguyên 66 file đã kiểm chứng. Integration branch `feature/outbound-dispatch-reconciled`, worktree `D:\ERP_KHO-outbound-dispatch-reconciled`, từ live remote `2ba782d7518be12daed0056fa66ab44725c922e0`. PR #1 vẫn Draft và là dependency, không phải evidence của slice mới.

## Reconciliation plan and file/capability matrix — 2026-10-07

Owner authorizes internal development merge preserving both ancestries; no PR/main/blueprint merge. The local checkpoint is not pushed. Live remote still has the same 13 commits, and Notion §34/01/30/38/228/229/18/282/41 native timestamps are UNCHANGED. No contract rewrite.

| File / capability | Base → local | Base → remote | Decision |
| --- | --- | --- | --- |
| AppPermissions/controller metadata | Six exact codes, nine-action registry | Same code values with ExportReceipt constant names | Equivalent codes; retain remote names without duplicate constants, local exact registry plus remote no-role-policy regression |
| ExportReceipt options | Named commands independent of defaults | Two-step default, different-dispatcher default | Keep remote conservative defaults; explicit immediate compatibility and legacy config semantics restored per owner |
| ExportReceipt service/controller | DB grants, RowVersion, scope/SoD, location ledger, explicit immediate compatibility | Controller grants, disabled immediate path, no server grant enforcement | Keep remote valid permission cutover; resolve action blocks to owner semantics, preserve local runtime protections; no wholesale ours/theirs |
| ApprovalAuthorize / ApprovalWorkflow | Separate read and reject+cancel, database role, Viewer filtering | Type switches, read filtering, reject using cancel only | Retain remote type-switch clarity and compatibility branches; correct reject to BOTH grants and retain local filtering/snapshots |
| Frontend ExportReceipts | Granular grants, mutation token/guard, generation clearing, Vietnamese states | Granular grants/partner visibility, removed immediate button | Keep remote partner visibility and menu/route cutover; restore explicit compatibility button, checker/dispatcher guard, local response safety |
| MainLayout/routes/authorization | Export read visibility | Equivalent export read visibility | Auto-merge equivalent changes; no role OR permission bypass added |
| Product/UOM/partner, inventory/reservation | New snapshot/ledger/atomicity protections | No additional changes | Local additions retained; integration regressions required |
| Migration catalog | Unpublished local schema+seed and location-index migration | Public 20261005143000 seeds six codes, Staff also cancel | Preserve public migration byte-for-byte as sole catalog seed owner; recreate unpublished local schema after it; corrective seed-provenance grant repair and guarded Down |
| Tests/runner/docs | SQL/HTTP/component/browser coverage | Controller metadata assertions | Retain both useful regressions; replace old local migration IDs only; old PASS remains historical/source-specific |

Architecture: accepted base → remote public seed → generated additive schema/correction; local checkpoint merges as second parent. Runtime reuses the existing transaction, aggregate token, DB permissions and BrowserQA harness. Failure modes: duplicate catalog seed, overbroad Staff bundle, lost compatibility action, missing RowVersion, partial stock/ledger or unsafe Down are blocking regressions.

Implementation tasks: (1) preserve checkpoint/remote refs; (2) resolve six conflict files by capability; (3) regenerate post-remote schema and guard provenance/rollback; (4) fresh focused/full automated and all seven browser groups; (5) final security/Ponytail/scans/cleanup, merge commit, new-branch Draft PR and successor CI. Existing helpers cover each task; no second QA harness. NOT in scope: new business aggregates/UI, permission engine, dependency, membership UI, production rollback, main/blueprint/PR merge or deployment.

Migration graph: public `20261005143000_AddOutboundReceiptPermissions` is retained byte-for-byte and owns catalog seeding. Generated `20261007074831_ReconcileOutboundDispatchMvp` follows it, adds aggregate token/null legacy snapshots, replaces the imperative historical export index with location uniqueness and corrects seed-only Staff cancel. Actor-provenance or explicit Permission.Grant audit preserves administered grants; no audit/catalog/business-history deletion in the correction. Down acquires the administration lock and blocks loss of snapshots, administered grants/audit or a ledger that cannot fit the legacy uniqueness. It never reintroduces erroneous Staff cancel. Direct production rollback of the old public seed alone is not safe/authorized; backup/data plan and guarded migration path are required.

Precursors được giữ nguyên: build fail 10 test constant references; focused 32/43 PASS (11 migration collation failures); focused successor 42/43 PASS (1 EF raw-SQL JSON fixture-format failure). Sửa đúng constant/explicit SQL collation/fixture escaping; provenance focused 1/1 và full API 206/206 PASS. Không dùng precursor làm closure evidence.

Local historical evidence bên dưới không phải PASS cho source tích hợp. Integrated successor evidence hiện hành nằm ngay sau phần này; chỉ kết luận READY khi successor CI cũng đạt.


## Final snapshot-correction successor — 2026-10-07

Final source/runtime-test aggregate SHA-256: `9355fab2edc8f12d8d74ccf2ac331e13dcc985ea06a3e94804eca28190490c24` (64 files, docs excluded). The earlier integrated fingerprint `6350097…` remains historical; do not assign its Approval Center PASS to this fix without the successor below.

Final alternate-surface review found a **Medium correctness gap**: Approval Center ExportReceipt rejection changed Draft → Cancelled without Customer Code/Name snapshots. Accepted [partner contract](BUSINESS_PARTNERS.md#snapshot-migration-and-compatibility) requires snapshots at the first transition out of Draft. Raw SQL-backed HTTP reproduced null customerCode after reject (negative Run `eb7d18f9bb784938bc932bc6f57ddd70`, 0/1 PASS, artifact retained). Minimal shared reject-path fix uses one conditional EF UPDATE for status plus the persisted Customer Code/Name, inside the existing transaction. It preserves permissions/scope/SoD, replay/audit exactly once and inventory neutrality; no model/migration or inbound branch change.

- Focused HTTP successor **1/1 PASS**, Run `916ec255284f49808d7941df7e9c372a`: assign customer → reject/replay → rename master → original customer snapshot still returned.
- Final Release build **0 warnings/errors**, EF **no pending model changes**.
- Final affected Application SQL **352/352 PASS, 0 failed/skipped**, Run `a34ef9aebcd241e49082980ca0f844b8`; exact DB cleaned.
- Final affected API SQL/HTTP **206/206 PASS, 0 failed/skipped**; 33 owned DBs created/cleaned; representative Runs `06182688a88b44e5ad21b5870cda8ec4`, `0bd37d5c80ad44119ec0a6f6f6d41a10`, `0eb20ca759e54ba5aba68c74ab2bea2b`. These supersede the earlier full SQL runs for the changed shared service.
- Frontend production source/lockfile unchanged after its **100/100 + lint/build/audit PASS**, so those fresh results remain valid; new runner-source checks **18/18 PASS**. Focused outbound selector now executes the Approval Center case and an empty selection fails.
- Fresh browser focused successor **1/1 group PASS**, Run `77efadecbfe441869a104af4d093f7a6`, exit 0: current permissions, maker 403, warehouse 404, count **14 → 12** with **2** exports removed before pageSize=1, one rejection audit on replay, Viewer reads original customer snapshot after QA-only master rename; On Hand/ledger unchanged. Browser Closed=true, profiles=[], helper cleaned API/frontend/credential/exact marker-owned DB.

The six other browser groups from full Run `a7c2acd94cb24443bb1b2c0f94147931` remain source-specific and valid: this fix changes only the ExportReceipt branch of shared rejection, not receipt reserve/dispatch/cancel, eligibility, UOM, Viewer DTO projection, frontend generations or mounted response handling. Approval Center evidence is superseded by `77efadec…`; no unrelated full matrix was repeated. UI workflow, browser-origin HTTP and SQL setup/postconditions remain separate.

Internal correctness/Ponytail review includes the new fix: native EF projection/update, no engine/dependency or speculative abstraction; no remaining High/Critical finding. Notion handoff recheck 17/18/282/41/84 unchanged; 84 native `2026-09-24T16:04:32.570Z`. All precursor failure artifacts/source hashes are retained and ignored. Final staged/public-history scans and live remote verification precede the normal two-parent merge commit. CI of the final pushed HEAD is recorded in the new Draft PR body; local evidence is not CI evidence.

## Integrated successor evidence — 2026-10-07

Historical integrated baseline before the snapshot correction above; the final successor takes precedence for changed code paths.

Source là merge working tree với hai ancestry `2ba782d7518be12daed0056fa66ab44725c922e0` và `e7a9b3221c459f7dbc5bd0ff2021cc5030d45b43`, không phải remote code riêng. Browser manifest ghi HEAD `2ba782d` vì merge chưa commit khi QA chạy; association được chứng minh bằng hashes runtime/runner và aggregate runtime/test SHA-256 `6350097efb00a3d12d00954f129ff95e2a1ada200b2fd83eeb15a9a01ac8b03a` (64 file, docs excluded). Final merge commit/source SHA nằm trong Draft PR. Checkpoint branch không được push riêng; merge ancestry giữ đầy đủ checkpoint và 13 remote commits.

| Gate | Integrated evidence / giới hạn |
| --- | --- |
| Release / EF | PASS; 0 warning/error; no pending model changes; Designer/snapshot generated by EF |
| Application SQL | 352/352 PASS, 0 failed/skipped; Run `c6b0f6731ebd444ea4121db425d92c11`, exact owned DB cleaned |
| API SQL/HTTP | 206/206 PASS, 0 failed/skipped; 33 owned DBs created/cleaned; representative Runs `0935fa43034a46839b99a4f4f1830154`, `0bad8903b91948dfb46c85353ea90069`, `145153de8c7b4409ab26ad6a51ee1f5b` |
| Provenance/rollback focused | 1/1 PASS, Run `6848047491c14d79bdc2060d48de0bf2`; implicit Staff cancel removed, actor/audit-provenance grants retained, unsafe Down blocked 51013 atomically |
| Extended legacy fixture | 1/1 PASS, Run `7690224bd9ce4202b182568288ec8ce4`; accepted-base → public-remote → corrective and allowed Down/Up keep On Hand 100, reserved 7, old ledger 1, legacy Dispatched/Approved receipts and active hold; no fabricated UOM |
| Frontend successor | 100/100 PASS in 19 files, worker threads; lint/build PASS; `npm ci` and audit PASS, 0 vulnerabilities |
| Runner guards | 18/18 PASS; error/timeout propagation and bounded failure cleanup retained |
| Browser successor | Run `a7c2acd94cb24443bb1b2c0f94147931`: seven groups PASS, exit 0, browser Closed=true and RemainingProfiles=[]; official helper cleaned exact API/frontend/credential/DB |
| CI | Pending at commit creation; track final HEAD in the new Draft PR body. Local PASS never substitutes for CI |

Full SQL source fingerprint before the final test-only fixture extension and lockfile patch was `e4390ef8594da283cefb876be6d75fc1a77daaa0530a3d902c890a40f1382e8f`. Only `OutboundDispatchHttpTests.cs` and `frontend/package-lock.json` changed afterward; backend runtime/model stayed identical, so SQL suites are not repeated for unrelated dependency/doc changes. The extended migration fixture ran afterward on final source. Final GitHub CI runs the entire final test source.

Dependency precursor: `npm ci`/audit found one high `source-map-js` vulnerability, [GHSA-68fv-2mgg-jv7q](https://github.com/advisories/GHSA-68fv-2mgg-jv7q). Transitive 1.2.1 → 1.2.2 only; package.json unchanged, no dependency added/bulk upgrade/threshold relaxation. Fresh install/audit/tests/lint/build with corrected lockfile all PASS.

### Browser matrix and actual effects

| Group | Evidence class | Result / postcondition |
| --- | --- | --- |
| Create → reserve → dispatch / double-click | UI workflow; SQL postconditions | 201/200/200; reserve request count=1, disabled while response held; On Hand 100 unchanged, reserved 25, ledger 0 before dispatch; after dispatch On Hand 75, reserved 0, export ledger 25 with actual LocationId |
| Replay / fingerprint / revoke / Viewer / scope | HTTP from authenticated browser; SQL postconditions | Same key 200/200 once; changed payload 409; grant-revoked replay 403 and membership-revoked replay 404 with unchanged effects; raw Viewer list/detail/history omit unitPrice/RowVersion/cost/value/private markers/idempotency metadata; safe 401/403/404/409 |
| Three controlled races | Browser HTTP; SQL lock coordination/postconditions | Competing reserve, dispatch/cancel and duplicate dispatch each have both requests pending ≥250 ms and statuses 200/409; no oversell/negative stock; duplicate dispatch asserts one stock effect, +1 success audit and +1 claim |
| Mixed Approval Center | Browser HTTP; fixture/postconditions | Nonzero count 15 → 12 when three export entries excluded, pageSize=1; read independent from reject; reject needs BOTH approval.reject+export.cancel; maker/foreign scope denied; replay rejection audit=1 and no stock/ledger change |
| Base UOM | Browser HTTP; master-only QA setup/postconditions | 0.0001 exact Base quantity, snapshot precision 4/name Cái retained after current master precision/name changes; 0/negative/overflow requests 400, zero effect, no rounding |
| Ineligible stock | Browser HTTP; fixture/postconditions | RECEIVING even if pickable, DAMAGED/REJECTED/blocked/inactive/non-pickable excluded; their On Hand 600, reserved 0, ledger 0 unchanged; rejected create/reserve has no success claim/effect |
| Mounted late list/detail/print | Component in real browser; real 403 identity refresh; fixture label changes only | Three original authorized 200 responses held through revoke→regrant; same heading node stays connected; old identifiers/detail/print dialog never return. No unmount substituted for mounted evidence |

Four personas each login once through UI (200); no rate-limit/security override. SQL setup/grants are not described as browser UI administration. Audit/claim aggregates can include fixture history; exactly-once assertions target the actual command. Historical local browser runs remain below.

Integrated browser precursor `78b158f2cdb64f239c53dc5371cd8d14` passed all seven case assertions but runner exited 1 because browser close exceeded its unchanged bounded deadline; evidence remains FAILED/cleanupFailed. Exact process/profile census afterward showed browser/profile already gone, and official guarded helper removed API/frontend/credential/DB. No timeout was relaxed or failed artifact overwritten. Fresh successor `a7c2acd94cb24443bb1b2c0f94147931` then passed the full seven groups and cleanup with the same source/runner.

### Final internal review, freshness and cleanup

Ponytail whole-diff: **Lean already. Ship.** Complexity assessment only. Internal correctness/security review traced all six conflict resolutions, public seed ownership/provenance and guarded Up/Down, default Staff four-code bundle, explicit/legacy immediate semantics, DB authority before replay, scope/SoD/version locks, location ledger and atomic stock/reservation/audit/claim, raw Viewer filtering and mounted generations. No unresolved High/Critical finding; this is not an independent review or production approval.

Notion final read-back: §34/01/30/38/228/229/dispatch row/18/282/41 all native timestamps UNCHANGED against the table below and their stored baselines (18 `2026-09-28T10:04:28.018Z`, 282 `2026-09-28T10:04:39.216Z`, 41 `2026-09-20T16:31:54.616Z`). No Notion write. QC-before-Post and Post-only receipt boundary preserved. Drive final metadata pagination complete: root 140 images + 5 folders; 16/24/56/10/176 images, no nested/new folders; artifacts remain **NOT REVIEWED**, illustrative only.

Owned QA databases/processes/listeners/credentials/profiles verified absent after cleanup; ERP_KHO ONLINE checked by metadata only, no business-table read/write. Main/inbound/test worktrees, .npm-cache, UNKNOWN cache and the policy-blocked audit helper remain untouched. Old unpublished local migrations survive in checkpoint history but are replaced only in the final integration tree; the public seed blob remains byte-for-byte unchanged.

Precommit strict UTF-8/mojibake/diff/secret/private-key/binary/runtime scans and exact allowlisted staging are required before merge commit/push. The 14 parent-history commits (77 distinct changed blobs) were scanned with no secret/binary/artifact finding; in-memory credential helper/undefined-clearing regex hits were explicitly classified as non-secret, not ignored. Final staged/merge-history scan and live remote-base verification are recorded in handoff. New integration branch only; old remote outbound, PR #1, main and blueprint are not updated. No force/rewrite/PR merge/deployment.

Deferred: Shipment/Picking/Packing/HU/Allocation aggregate, alternate outbound UOM, return/reversal and membership UI. Production remains NO-GO regardless of owner-review readiness.

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

Historical local checkpoint only: the first generated migration `20261004023307_AddOutboundDispatchMvp` adds the receipt aggregate token, nullable historical Base UOM snapshots and six explicit permission bundles. Existing historical migrations are unchanged. Generated Up/Down SQL was reviewed; fresh SQL tests exercise upgrade/downgrade/legacy totals and guarded failed downgrade.

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

## Historical local review and handoff blocker — superseded by owner reconciliation on 2026-10-07

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

Historical Drive metadata (superseded by integrated final check above): 140 root images + 5 folders = 145 entries; subfolders 16/24/56/10/176 at the fresh recursive metadata recheck on 2026-10-05, with all provider pagination exhausted and no additional nested folders. Artifacts remain metadata-only **NOT REVIEWED**.

Deferred: separate canonical Shipment/HU/Allocation, alternate operation UOM outbound, returns/reversal, advanced allocation rules, membership UI (DEFERRED_BY_OWNER). No Dashboard work.

REMOTE STAGING NOT AUTHORIZED

CAPACITY EXECUTION NOT AUTHORIZED

PRODUCTION NO-GO
