# Accumulated PR #1 owner acceptance review

Status: **OWNER REVIEW INCOMPLETE — QC ACTION/PERMISSION MAPPING REQUIRES OWNER DECISION**.

Review date: 2026-10-03. PR: https://github.com/EyeDemon/ERP_KHO/pull/1 (Draft). Comparison base is `699f7a1e7eb338eabdf17666b187a43133ab8af0`; reviewed checkpoint is `81f3232c77ba423dfec978c811f7f387e1272082`, plus the acceptance fixes described below. The final successor commit and its observed GitHub CI result are recorded in the PR body, without a documentation-only commit cycle. This is acceptance of the accumulated feature scope, not release readiness for all ERP.

## Review method and boundary

Internal static correctness/security and Ponytail review traced the implemented module controllers, shared services, repositories, transaction boundaries, migrations, permission bundles and affected frontend flows. The accumulated checkpoint contains 42 commits / 247 changed files, including generated migration Designers. This is not an independent review or a claim that every unchanged ERP path was tested. Attempts to delegate specialist review ended at the agent usage limit; the findings and conclusions here are from the main agent's manual review.

Automated regression, SQL-backed HTTP, component tests, historical UI workflow, browser-origin HTTP and SQL postconditions are separate evidence classes. Existing closed BrowserQA findings were not reopened or expanded. No new BrowserQA run, production hook, dependency, model change or migration was added for this review.

## Module acceptance matrix

| Module | Contract / implementation / API | Authorization and Vietnamese UX | Source-associated evidence | Finding / limit |
| --- | --- | --- | --- | --- |
| Product Category / Barcode | Page 84/162; normalized flat Category master, binary case-sensitive exact barcode lookup, explicit assignment/mutation actions | Independent `product_category.read/manage`, `product.update`, `product_barcode.manage`, `product.read`; no aliases | [Module evidence](PRODUCT_CATEGORY_BARCODE.md), browser `03e0d3d6fb8448388fa86e354c533b2b`; permission browser `3017ec885d284fa4968128222c66e7ef`; successor full SQL/API/frontend | Implemented scope reviewed; no new finding. Scanner-wedge simulation is not physical hardware verification |
| Business Partner / document snapshots | Page 84/162; immutable normalized Code, active matching role, restrictive FK, serializable assignment; historical supplier/customer Code/Name snapshots | Database partner grants and receipt supplier capability; outbound assignment retains compatibility. Foreign receipt scope must precede state/partner validation | [Module evidence](BUSINESS_PARTNERS.md), historical delayed UI `23e2c5bc5be5481497691691d83a006a`; new four-request HTTP isolation regression | P2 scope oracle fixed in the shared import/export assignment method |
| Receipt Printing | Page 64/228/282; fresh authoritative detail, snapshot partner fields, saved import operation-UOM quantity; browser PDF pagination | Detail-read capability and warehouse isolation; no mutation. All receipt states and controlled print labels now Vietnamese | [Print evidence](RECEIPT_PRINTING.md), historical PDF `c19d7e4a2b8f46d590a13c7e84b2f601`; mounted print `42464f71903e4898ad347ae047ab1d43`; new component regressions | P2 raw state/English print labels fixed. Physical printers and byte-for-byte historical Product/Warehouse names are not verified/promised |
| Receiving / Quantity / UOM | Pages 01/29/34/84/162; expected/received/Base quantities and conversion/version/precision snapshots; receive/readiness are inventory-neutral | Granular receipt permissions, optional dependencies, scoped detail and independent maker/poster | [Receiving evidence](INBOUND_RECEIVING.md); full successor Application/API tests; preserved module browsers | No new quantity/state finding. Historical legacy fallback remains explicit; masters do not recalculate posted quantities |
| QC / no-QC / disposition | Pages 01/17/34/41; QC before Post, mixed lines, positive AVAILABLE/DAMAGED/retained REJECTED buckets | `receipt.receive`, `quality_inspection.execute`, `receipt.complete`, `receipt.post` are enforced; separate QC complete/approve mapping is unresolved | [QC evidence](INBOUND_QC_DISPOSITION.md), historical `2f151b80af5344f09d2fffc5da613b1b`; successor SQL/API | **OPEN P1 authorization gap** below. Workflow PASS does not close missing capability enforcement |
| Receiving Discrepancy | Pages 01/20/34/71/84; append-only observation/resolution, reason/tolerance snapshots, door-rejected outside custody, Base-UOM normalization | Six independent discrepancy grants; recount uses resolve; warehouse and maker/checker independent; Viewer notes/evidence filtered | [Discrepancy evidence](INBOUND_RECEIVING_DISCREPANCY.md), `cb87071aca7a470685a099336fe57c3e` / `6ac961abc1ba44709f9ebb5b0d1edb96`; successor SQL/API | Implemented scope reviewed; obsolete current SQL-admin blocker corrected in documentation |
| Cất hàng / location balances | Pages 29/35/84; Post atomically creates RECEIVING stock/task; immutable movement changes Location only; deterministic LEGACY backfill | Independent read/assign/execute/cancel, assignment, warehouse isolation and Viewer filtering; Vietnamese mappings | [Putaway evidence](INBOUND_PUTAWAY_LOCATION_MOVEMENT.md), historical runs linked there; new stock/task deactivation guard and controlled SQL/HTTP race | P1 location governance/race fixed. No capacity, generic transfer or reverse-completed movement |
| Permission Code Authorization | Page 17 §33, 18/282; database grants per request, explicit Admin, aggregate tokens, shared administration lock, canonical last-admin guard | Safe errors; independent scope/SoD/Viewer classification; reactive context and generation guards | [Permission evidence](PERMISSION_CODE_AUTHORIZATION.md), browser `3017ec885d284fa4968128222c66e7ef`; mounted `42464f71903e4898ad347ae047ab1d43`; runner 18/18 retained | Existing BrowserQA findings closed. Whole-PR QC mapping gap remains; membership UI is DEFERRED_BY_OWNER |
| Reservation / export / transfer / stocktake / reports integration | AVAILABLE-only operational queries; allocation uses active/unblocked/pickable locations; transfer/legacy stocktake retain explicit LEGACY compatibility; current/historical reports filter AVAILABLE | Outbound/stocktake/reservation remain outside inbound permission cutover; their role-backed compatibility is not advertised as migrated | Successor full Application/API tests exercise the affected paths; Putaway historical before/after allocation browser evidence retained | Compatibility reviewed where accumulated changes touch it. This is not acceptance of a new outbound engine or status-inclusive report |

## Proven findings and minimal fixes

1. **P2 — Business Partner direct-ID state oracle.** `BusinessPartnerService.SetReceiptPartner` validated partner/state before warehouse authorization. An authenticated Manager with no warehouse membership could distinguish a foreign Draft from a foreign Approved receipt by 400/409 instead of isolated 404. The shared method now authorizes the loaded receipt warehouse first. New HTTP regression covers both import supplier and export customer assignment with null and invalid partner IDs: four isolated 404 responses, no private code/state disclosure, no link or successful audit change.
2. **P1 — Location deactivation with stock and destination race.** `PutawayService.UpdateLocationAsync` permitted deactivation of a non-system Storage containing stock, contrary to Page 84 §8; Move could validate eligibility before a concurrent master deactivation committed. The update now rejects nonzero stock or unfinished source tasks with 409. Update and Move use the same SQL location row lock (`UPDLOCK, HOLDLOCK`) held through the existing/owned transaction. No nested transaction, distributed lock or dependency was added. SQLite tests exercise the guard; controlled SQL/HTTP overlap holds the destination until both requests are pending, then releases it. Permitted outcomes are Move 200 / deactivation 409, or deactivation 200 / Move 400. An inactive destination never receives stock; totals, one-or-zero movement/audit/claim match the winner.
3. **P2 — Receipt print raw state/English labels.** `ReceiptPrintPreview` displayed raw `Received`, `ReadyToPost`, QC/discrepancy states and English controlled copy. The existing presentation now maps every receipt enum value, has a safe fallback, and preserves Draft/Cancelled banners. Component regressions cover every additional state and reject raw labels. CSS/pagination and fetch-generation logic were not changed; their previously valid evidence is retained.
4. **OPEN P1 — QC authorization actions do not cover separately seeded permissions.** Page 17 §33.3/33.4 includes `quality_inspection.complete` and `quality_disposition.approve`. `ImportReceiptsController.RecordQcDisposition` checks only `quality_inspection.execute` while the service automatically completes QC when the last required line is dispositioned. `Approve` checks `receipt.complete` for both Received and QcCompleted, without `quality_disposition.approve`; the frontend follows those same grants. A role retaining execute/receipt.complete after revocation of the two QC grants still performs the combined actions. Owner was asked to choose explicit conditional checks on the existing actions versus a canonical deferred designation; no new endpoint or assumed schema/security decision was introduced. Until that decision, implementation and targeted HTTP/UI evidence are missing. Notion was not changed to justify source.

Historical record text that described current Category/Partner/QC/Discrepancy authorization as role-backed, the `/imports` route, or superseded SQL-admin blockers is corrected without rewriting historical results.

## Fresh verification of acceptance fixes

Execution association: checkpoint `81f3232c77ba423dfec978c811f7f387e1272082` plus the three reviewed fixes and their tests; the successor commit publishes those exact source files. Test results are ignored local artifacts, never public PR payloads.

Tested runtime file SHA-256: BusinessPartnerService `21149f08125d0537e6169b1a8e1ef320c7a48ee1989423228fefcd27513e6bc8`; PutawayService `0d5e4614224084f9f54d6d9e769c58dade44144b1ec804b288d58a35a54bcfd5`; ReceiptPrintPreview `3bbf7c651b266dec1193b3a01482ccc19b0842bac56eaf9c1d766ab390302995`.

| Gate / evidence class | Result | Artifact / Run ID |
| --- | --- | --- |
| Focused Cất hàng application regression | PASS 5/5, 0 failed/skipped | `TestResults/OwnerAcceptance/putaway-owner-review.trx` |
| Focused real SQL-backed HTTP | PASS 2/2, 0 failed/skipped | `acceptance-focused-http.trx`; owned Runs `8d806f25a2dd42ac8b451f38c9c86d0f`, `fbfa9d02f3b746478647924776264bb9` |
| Full Application SQL | PASS **348/348**, 0 failed/skipped | Run `f8c9ba8dd65b47728301e5ddd699d062`; `TestResults/SqlIntegration/.../application.trx` |
| Full API SQL/HTTP | PASS **186/186**, 0 failed/skipped | `TestResults/OwnerAcceptance/api-owner-acceptance.trx`; 19 owned database runs, all matching cleanup records |
| Focused print component/loading | PASS 17/17 | Two component test files |
| Full frontend | PASS **85/85**, 18 files | Sequential `npm test`; source-associated acceptance fixes |
| Frontend lint / production build / dependency audit | PASS / PASS / zero vulnerabilities | No dependency or lockfile change |
| Release solution build / EF pending-model | PASS, 0 warnings/errors / PASS | No model or migration change |
| New browser execution | NOT RUN | No new UI/API-browser gate was manufactured. Closed runner 18/18 and mounted 3/3 retain their precise historical scope |
| Published checkpoint CI | Historical SUCCESS | `37116823150` at `81f3232`; 347/184/72. Successor CI is observed separately in PR #1 |

The fresh full suites include owned migration/bootstrap-negative, permission administration, session/security concurrency, replay reauthorization, warehouse isolation, Viewer and mixed Approval Center regressions. They are not browser evidence and do not prove the missing QC capability checks. Failure-injection SQL/provider log lines in ignored TRX are expected negative-test diagnostics; raw client error assertions still enforce sanitized responses.

API full-suite owned Run IDs: `11c33c1a12734998940c8649bbba155b`, `13a8b3180d6a4aec997759cff4d7ccea`, `1f301a47a8634799806bd8b6690c5a85`, `3944fcd2f06d4f44b1cf0e70915081c6`, `46eca490f2c14040bda0686fa53ac7ff`, `64e138853c3b44629ee57a6d57d5679a`, `65603ddd0c0345e7bce9ccb5bbb20e55`, `7022557018534fd294eb29eb70941573`, `7fdc3a7362b44aea892f460254fbfc42`, `88a58a52aed74bd0a26522fbfc916371`, `9fbac8e22edb4aae811252b5ecb72950`, `b902f50b8f714a54ac9bdfddd1fbe4a7`, `bbc5b2e3b0884ad486a7e5156499e61d`, `c1bb130dce53490181be53d95a80cd41`, `ce123c5497fd4a69a66e897b5c1821ba`, `d8e070665adb496ea224b0a26f76d114`, `dfeb0ea9dab4420aa682b9aa2cd98c75`, `e88899a5f4b04d71855ef48d3e68c175`, `f3c82706ef9d4999b8fbecee94001945`.

## Design freshness

Direct native Notion timestamps were fetched before review. Final recheck of 17/18/282/41 matches the same values. The connector provides `page_last_edited_at` but no truncation flag; no unsupported `truncated=false` claim is made. Root uses its own native timestamp, not a descendant's timestamp. No contract writes were performed.

| Source | Native timestamp | Comparison |
| --- | --- | --- |
| Root | 2026-09-17T20:20:06.707Z | UNCHANGED against trusted native baseline |
| 01 | 2026-09-24T16:04:16.627Z | UNCHANGED |
| 17 | 2026-09-30T19:50:06.373Z | UNCHANGED; §33 remains authority |
| 18 | 2026-09-28T10:04:28.018Z | UNCHANGED |
| 20 | 2026-09-23T15:38:55.860Z | UNCHANGED against exact native baseline; historical .859 transcription is not a fresh delta |
| 29 | 2026-09-24T16:04:23.927Z | UNCHANGED |
| 34 | 2026-09-24T16:04:27.613Z | UNCHANGED |
| 35 | 2026-09-24T16:03:22.053Z | UNCHANGED |
| 41 | 2026-09-20T16:31:54.616Z | UNCHANGED; QC-before-Post remains canonical |
| 71 | 2026-09-23T15:38:59.281Z | UNCHANGED |
| 84 | 2026-09-24T16:04:32.570Z | UNCHANGED; existing Location invariant exposed the finding |
| 162 | 2026-09-24T16:04:36.075Z | UNCHANGED |
| 228 | 2026-09-24T16:04:39.625Z | UNCHANGED |
| 229 | 2026-09-24T16:04:50.257Z | UNCHANGED |
| 282 | 2026-09-28T10:04:39.216Z | UNCHANGED |
| 25 API / 64 Printing / 65 Barcode | 2026-09-17T19:44:17.208Z / 19:57:09.885Z / 19:57:09.885Z | CHANGE STATUS UNKNOWN; no trusted stored native baseline |

Affected screen rows were fetched directly: Receiving Workbench `2026-09-20T16:32:17.484Z`, Putaway `2026-09-24T16:05:15.830Z`, Discrepancy `2026-09-28T10:08:49.402Z`, Post Confirm `2026-09-20T16:32:15.434Z`, Create `2026-09-19T20:26:09.479Z`, Detail `2026-09-20T16:32:18.477Z`, QC `2026-09-20T16:32:13.773Z`, no-QC `2026-09-20T16:32:14.381Z`. Putaway/Discrepancy match their exact stored baselines; rows with only coarse historical timestamp evidence are CHANGE STATUS UNKNOWN, not asserted UNCHANGED.

Drive was enumerated with complete provider pagination and folder-child checks: root **140 images + 5 folders = 145 entries**; Corrected **16**, Enriched **24**, Merged/Split **56**, New Screens **10**, Deprecated **176** images. Root/Deprecated required two pages. No new nested folder was returned. Counts match baseline. Relevant Approval/Variance/Adjustment/User–Role–Permission/Putaway and receipt metadata remain illustrative. No reference image was rendered in this review, so current visual status is **NOT REVIEWED**; prior explicitly rendered references remain historical evidence only.

## Security, migrations, Ponytail and deferred scope

The review retained database-per-request capability authority, current database role classification, independent warehouse membership, maker/checker and maker/poster, assigned operator, Viewer filtering, fail-closed store failure, resource-aware replay and transactional rollback of denied claims. The QC exception above prevents a whole-scope no-blocker conclusion. Existing tests/browser evidence protect aggregate grant/membership/security tokens, shared SQL administration lock, canonical active/unlocked last Admin and no Manager substitution.

Migration ordering and the official pre-permission bootstrap fixture were reviewed. Permission catalog/bundles are deterministic and explicit; unknown roles receive no grants. Putaway assigns legacy balances to deterministic LEGACY status locations without fabricated task/movement history. Generated Designers/model snapshot were not edited. Down after production grants/location balances is not an automatically safe deployment rollback: backup and an explicit data plan remain mandatory. No production migration or rollback was executed.

Ponytail whole-scope assessment: the new fixes reuse shared services, SQL row locks, existing transactions and presentation/component tests; no speculative dependency, override engine, DSL, cache or abstraction was added. Security controls were retained. This internal lean assessment does not waive the open capability gap or substitute for evidence.

Owner-deferred membership administration UI remains **DEFERRED_BY_OWNER**; the API is implemented and tested. Also deferred: lot/serial/HU, advanced capacity/slotting/routing, generic location transfer, reverse completed movement, laboratory/attachment-content engine, user overrides/deny DSL/distributed cache/scheduled grants/visual permission designer, native mobile/offline sync, external identity-provider synchronization and production deployment/capacity execution. Dashboard and unrelated ERP features were not opened.

## Ownership and handoff

The Application target and all 2 focused / 19 full API targets have matching exact Run ID/ownership cleanup records. Metadata census checked all **22** exact names and found **0 remaining**. No BrowserQA server, profile, tab or persistent credential was created in this review. `ERP_KHO` is **ONLINE**, verified through `master.sys.databases`; its business tables are never read or written. The metadata check was repeated with an explicit master target after correcting a PowerShell connection-builder property setter; no application/business query was issued.

Diff-check and repository UTF-8/mojibake validation PASS. Current source and all **453 changed blobs across 42 accumulated commits** were scanned for private keys, credential-bearing URLs, provider tokens and connection-string passwords; there are no untriaged real-secret findings or binary history blobs. Broad password-pattern candidates are generated QA variables, login-form selectors, or intentionally invalid synthetic guard-test examples, not credentials to publish. No runtime/binary/ZIP/TRX/log/profile/credential path is included. Exact final staged-file review and successor SHA/CI are recorded in the PR handoff.

Main stays `699f7a1e7eb338eabdf17666b187a43133ab8af0`; test worktree and its pre-existing changes remain untouched. All three registrations remain valid. User-owned main paths, UNKNOWN cache path, policy-blocked helper and feature `.npm-cache/` are preserved. Only explicit reviewed source/tests/docs are committed; TRX/logs/dist/credentials/browser artifacts remain excluded. PR #1 stays Draft without reviewer requests, ready transition, auto-merge or merge.

**OWNER REVIEW INCOMPLETE.** The concrete remaining gate is owner-approved QC complete/approve action mapping, its minimal enforcement and source-associated regression/affected verification. Passing successor suites do not erase that gap; owner acceptance does not authorize production release.

REMOTE STAGING NOT AUTHORIZED

CAPACITY EXECUTION NOT AUTHORIZED

PRODUCTION NO-GO
