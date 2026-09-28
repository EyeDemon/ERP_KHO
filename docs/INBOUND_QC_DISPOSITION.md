
# Inbound QC Disposition

The canonical Goods Receipt flow performs line-level QC before Post. Receive, QC disposition and readiness approval do not write inventory. Post atomically writes persisted Base UOM quantities to AVAILABLE, DAMAGED and retained REJECTED balances and ledger rows.

Mixed receipts are allowed. A line snapshots the effective Product–Supplier policy first, otherwise Product policy, at receipt creation. A receipt stays `QcPending` until every required line has a valid disposition; then it becomes `QcCompleted` and a different checker may approve it to `ReadyToPost`.

The invariant is `Accepted + Damaged + Rejected = Received` in operation UOM and persisted Base UOM. Damaged and rejected quantities never increase Available. Rejected-at-door is deferred and creates no inventory in this slice.

Commands:

- `POST /api/importreceipts/{id}/receive`: Draft to Received or QcPending; no stock effect.
- `POST /api/importreceipts/{id}/qc-disposition`: completes one or more pending QC lines; no stock effect.
- `POST /api/importreceipts/{id}/approve`: Received/QcCompleted to ReadyToPost; no stock effect.
- `POST /api/importreceipts/{id}/post`: writes status balances and ledger once, then Posted.

Invalid/stale transitions return 409. All mutations retain warehouse scope, idempotency and optimistic concurrency. Current gates are role-backed; Notion permission codes are target governance and permission-code migration is deferred.

Migration `20260920163928_AddInboundQcDisposition` backfills legacy balances/ledger as AVAILABLE and does not infer historical QC policies or alter legacy receipt states.

**SUPERSEDED HISTORICAL VERIFICATION — 2026-09-21.** Release build, EF model check, 282 non-SQL Application tests, 52 frontend tests, lint and production build passed at that checkpoint. Owned-SQL and browser QA were still blocked then.

## Recovery and fresh verification — 2026-09-23

The deleted feature worktree was recreated from unchanged commit `710cfbd635e7f0ae1f2e1362a3b4c27c7156eea4` after a dry-run proved there was exactly one stale registration. Three recovered patches were validated against that commit and restored individually. They add Available-only filtering to operational current-stock, reconciliation and historical report paths, add a focused regression test, and retain HTTP 409 semantics for either stale-write or already-posted concurrent losers.

Migration `20260921090830_ScopeInventoryReportsToAvailable` and its Designer were regenerated with repository EF tooling. Up recreates the inventory report procedure with exactly two `InventoryStatus = 0` predicates; Down restores the complete previous procedure. The EF model snapshot did not change and `has-pending-model-changes` passes.

Fresh evidence after recovery:

- Release build: PASS with 0 warnings/errors.
- Application owned-SQL suite: 333/333 PASS; Run ID `eee50f950d6e4c27a1e38a48be902c9f`; owned database `ERP_KHO_Integration_20260923_100927_eee50f95` cleaned after marker verification.
- API SQL/HTTP suite: 156/156 PASS. Six owned database runs (`667ca373641d443e9c64d9f184072254`, `64674705341c4ea388b646c3470547ab`, `52a583c22b714767bf2a28c76bd9d2ba`, `47f2a1c258314a44887613fd4f6bf81f`, `e36200901bcd47d2a0b4fe92abe66cd0`, `f4d02cba7af84faf9b40910e09070a3a`) all cleaned after marker verification.
- Frontend: 52/52 PASS; lint PASS; production build PASS.
- **SUPERSEDED HISTORICAL BROWSER BLOCKER.** Run ID `59d90fc735634fec8ca124fb8843f94c` ended before browser interaction because the then-available control runtimes failed. Its owned resources were cleaned. The closure evidence below replaces this blocker as current verification state.

### Browser closure

Final specification freshness at 2026-09-23 remained `UNCHANGED`: Root design, 01, 17, 29, 34, 41, 84, 162, 228, 229, 282 and the Workbench/Detail/QC/no-QC/Post screen rows retained their canonical timestamps (01/17/34/41/84/228/282 at 2026-09-20 16:31Z; 29/162 at their 2026-09-17 baselines; 229 at 2026-09-19 11:26Z; affected screen rows at 2026-09-20 16:32Z). No state, policy, permission, quantity, status or posting-boundary conflict was found.

Drive metadata also remained unchanged: 140 root files plus the five named folders, Corrected 16, Enriched 24, Merged/Split 56, New Screens 10 and Deprecated 176. Current IDs remain Workbench `11No9JgfuXEczp2Rhd_TBXBax4GMpCwZK`, Detail `1G3_AsgVijBgt7VCmiwYTFby8L5MMqxBO`, QC `1TTf_FBM5CFYV0sJWKrVgL7Z8iHqHqL8J`, no-QC `14GkuRbmX1YNnaNfufI-0svafYq-DP5io` and Post Confirm `14QiquYq8qjBvA-OTbMaFC3k1M3W8gsHv`. This final pass fetched metadata/content but did not render the images; existing review status therefore remains QC/no-QC `REVIEWED`, Workbench/Detail/Post Confirm `NOT REVIEWED`. Drive remains illustrative.

- Browser runtime root cause: the previous CUA kernel initialization failure was transient. The official Codex in-app browser control initialized successfully without package, browser or security changes; a loopback smoke page proved navigation, DOM reads, fill, click and request-count observation before QA resources were created.
- Fresh Run ID `2f151b80af5344f09d2fffc5da613b1b`, database `ERP_KHO_BrowserQA_2f151b80af5344f09d2fffc5da613b1b`, marker `LocalBrowserFullStackQA`, API `127.0.0.1:5265`, frontend `127.0.0.1:4175`, dedicated in-app browser tabs and synthetic users only.
- Browser-originated HTTP passed: no-QC `200/200/200`; QC pending approve/post `409/409`; QC retry `200/200`; concurrent QC `200/409`; mixed receipt pending approve/post `409/409`; Post retry `200/200`; concurrent Post `200/409`; warehouse-B list exclusion plus detail/receive/QC/approve/post/replay all `404`; Viewer list/detail `200`, unknown detail `404`, no real cost or supplier/receipt leakage.
- Mixed receipt snapshotted a no-QC line (`None`) and a QC line (`Product`, policy 1 version 1). Its QC invariant was `2 + 4 + 3 = 9`; Post produced QC-product balances AVAILABLE `7`, DAMAGED `4`, REJECTED `3`, with no QC_HOLD/QUARANTINE row. Each posted receipt had one `ImportReceipt.Posted` audit, and retry/concurrency created no duplicate ledger or audit.
- Reservation of 12 against QC-product AVAILABLE 7 returned `409`. The attempted export of 12 returned `400`; neither consumed DAMAGED/REJECTED.
- UI double-click QC and Post each completed one transition and one business audit. Buttons entered loading/disabled state. A deliberately stale approve returned the conflict message and cleared the open receipt detail. Browser QA also exposed and fixed a Post confirmation defect: list rows had no details, so the modal displayed zero buckets. Post now takes its synchronous lock, loads authoritative detail, shows persisted Base-UOM bucket totals, and then submits; regression coverage verifies totals and a single request.
- Post-fix gates: Application owned-SQL 333/333, Run ID `df61902fa7724bb497598096251b41c6`, database cleaned; API SQL/HTTP 156/156; frontend 52/52; lint and production build PASS; Release build 0 warnings/errors; EF pending-model PASS.

No business data in database `ERP_KHO` was accessed. Historical runs remain supporting history only. Exact helper cleanup closed the QA tabs, stopped API PID 4600 and frontend PID 18368, removed the DPAPI credential and marker-verified/dropped the BrowserQA database; no owned QA database remained and `ERP_KHO` was ONLINE. Commit `65a65da` records the recovered Available-only changes, migration/tests and the Post-confirmation fix. Current status is **INBOUND QC DISPOSITION READY FOR OWNER REVIEW**.

Cất hàng compatibility was verified after this closure: Posted AVAILABLE/DAMAGED/REJECTED buckets keep their status while moving from RECEIVING to eligible locations. QC-before-Post and the single receipt inventory boundary remain unchanged.

Next gaps after QC closure are receiving discrepancy/reason codes, Putaway/location movement, permission-code migration, rejected-at-door handling, and an optional laboratory/evidence engine. They do not expand this recovery slice.

## Successor slice note — 2026-09-24

Receiving discrepancy/reason-code implementation is now present on its dedicated successor branch and documented in `INBOUND_RECEIVING_DISCREPANCY.md`. This does not revise the accepted QC contract: discrepancy resolution remains before QC/readiness and does not write inventory. The SQL-admin blocker in this note is a superseded historical checkpoint.

Fresh successor evidence is Application SQL 338/338, API SQL/HTTP 156/156, frontend 53/53 and complete browser closure. Discrepancy observation/resolution remains inventory-neutral; only the existing Post command writes persisted Base UOM quantities. The QC invariant and AVAILABLE/DAMAGED/REJECTED behavior are unchanged.

The remaining functional gaps after discrepancy closure are Putaway/location movement, permission-code migration, and the optional laboratory/evidence engine. Rejected-at-door is modeled by discrepancy custody exclusion in the successor slice; supplier/carrier claim execution remains deferred.

## Cất hàng compatibility — 2026-09-27

Cất hàng is a post-Post location-only movement. It does not restore Post-then-QC, change a QC disposition, or create QC_HOLD/QUARANTINE in the canonical receipt flow. AVAILABLE/DAMAGED/REJECTED totals remain invariant while their location balances move from RECEIVING to eligible destinations.
