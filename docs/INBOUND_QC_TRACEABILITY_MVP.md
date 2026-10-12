# Căn cứ kiểm tra chất lượng trên dòng phiếu nhập

Current checkpoint: **LOCAL VERIFICATION COMPLETE**. Successor CI/Sonar are pending at this pre-push checkpoint; their final result and source HEAD belong in the new Draft PR before handoff. Branch `feature/inbound-qc-traceability`, accepted base `2eaea6078ce1d8ae229a1d39d851108fdd6512ce`, plus the reviewed working diff. No owner acceptance is implied. PR #34 and earlier accepted-scope PRs remain Draft.

## Contract and field mapping

Read-only Notion authorities: [Page 17 §33.4](https://www.notion.so/3de558fb617481738e43c2aed135580a), native `2026-10-11T07:47:43.461Z`, and [Page 84 §15](https://www.notion.so/3de558fb617481d5b771c17162c9462b), native `2026-10-11T07:47:42.178Z`. Both unchanged against the accepted catalog audit; no Notion edit. The connector did not supply a truncation flag, so completeness is not claimed through an invented `truncated=false`.

The following mapping was established before code edits. Existing `GET /api/importreceipts/{id}`, database `receipt.read`, already exposes the fields. No DTO expansion or catalog permission is needed.

| API field | Persisted line source | Vietnamese presentation |
| --- | --- | --- |
| `requiresQc` | `RequiresQc`, accompanied by saved basis metadata | Yêu cầu kiểm tra chất lượng / Không yêu cầu kiểm tra chất lượng |
| `qcPolicySource` | `QcPolicySourceSnapshot` | Nguồn căn cứ: policy by product, product/supplier, explicit no selected policy, or discrepancy route-to-QC; translated, never raw enum |
| `qcPolicyVersion` | `QcPolicyVersion` | Phiên bản chính sách; a business version, not a concurrency token |
| `qcPolicyEffectiveAtUtc` | `QcPolicyEffectiveAtUtc` | Thời điểm bắt đầu hiệu lực đã lưu |

No EffectiveTo is persisted for this snapshot, so no end/range is invented. An explicitly zoned timestamp is formatted in Vietnamese UTC. An unzoned SQL datetime2 response retains its original wall-clock fields and explicitly says the response lacks timezone information; the UI does not append Z or interpret it in the browser timezone. Missing source/version/effective-time metadata displays **“Chưa có thông tin căn cứ đã lưu”**, regardless of the default boolean. Explicit source `None` is a saved no-policy decision, distinct from null legacy data. Missing individual fields remain explicitly missing. Policy IDs, rules/value thresholds, RowVersion and technical metadata are not presented.

## Implementation boundary

Receipt detail uses the existing permission context and generation guards. An accessible toggle expands each line's saved basis, exposes `aria-expanded`/`aria-controls`, and retains keyboard focus. The line table scrolls inside its own labelled region. A 16rem minimum for the explanation fixes a browser-observed compressed mobile column without hiding document overflow. Existing receipt date labels use Vietnamese formatting.

No live policy request, new endpoint/permission/bundle, migration, dependency or policy administration. No changes to QC decisions, quantity/UOM, state machines, warehouse isolation, Viewer filtering, stock/reservation/ledger, audit or idempotency runtime. QC remains before Post; Post remains the receipt inventory boundary.

## Verification matrix and source-specific evidence

| Gate | Current evidence | Status |
| --- | --- | --- |
| QC/no-QC/legacy, source/version/time, keyboard toggle, safe unknown source | Component checks; null legacy never inferred as no-QC, explicit None distinguished, UTC/zone-less formatting separated | PASS automated |
| Receipt-only reader, no master dependency/mutation, revoke clearing, mounted stale detail after revoke→regrant | Existing receipt page tests plus two regressions | PASS automated |
| Snapshot stability, Viewer/capability/scope, zero effects | Owned SQL-backed HTTP regression described below | PASS HTTP/SQL |
| Utility UI QC/no-QC/legacy, Viewer snapshot read without policy grants | Run `84f0e36a9c78469abb7299cdbba72de5` at precursor markup | PASS precursor; not mobile successor |
| Mobile/reflow/focus after correction | Utility successor at 375/768/1280px; explanation readable through inner table scroll; Enter/Space, Tab/Shift+Tab and retained focus observed | PASS utility UI |
| Mounted late responses in a real browser | Existing verified receipt runner, selected group only, fresh successor described below | PASS supplemental; separate from utility UI |
| Review/scans/owned cleanup | Internal review; explicit-file scans; exact successor cleanup described below | PASS local |
| Publication/CI/Sonar | Not claimed from older source; final result is recorded in the Draft PR | PENDING pre-push |

Release build: zero warnings/errors. EF pending-model check: no changes. SQL-backed HTTP `Qc_basis_read_preserves_saved_decision_after_master_changes_without_policy_grant_or_effects`: **1/1 PASS**, Run ID `a5fb3772063842909ccd89b74c6bbeb9`, exact owned database `ERP_KHO_Integration_20261012_123418_a5fb3772`, guarded cleanup confirmed. Real HTTP 200 before/after master edit returned identical detail; snapshot version 3 and effective fields unchanged; Viewer cost null, rule omitted; removed membership isolated 404; removed receipt.read safe 403 despite stale Admin claim. Audit/claim/ledger counts zero, stock 100, receipt state QcPending unchanged. The HTTP assertion, not middleware's pre-handler request-log status, is the observed client status.

Frontend full precursor **129/129**, 22 files, then final affected **21/21** after the unknown-source guard and mobile/date changes; no failed/skipped checks. Final affected lint, production build and dependency audit PASS, zero vulnerabilities. Full SQL suites were not repeated for markup/CSS; no backend/model runtime changed. One initial build invocation used the wrong solution filename and failed before building; the verified `ERP.slnx` invocation passed. These are local results, not successor CI/Sonar.

## Utility QA precursor and lifecycle limits

Official harness run `84f0e36a9c78469abb7299cdbba72de5`, Codex In-app Browser tab 1, loopback `/import-receipts`. Owner entered the synthetic Viewer credential directly; the agent did not read clipboard/password fields. Viewer role was fixture-restricted to `receipt.read`; QA-only lines represented saved Product/version 7, explicit None and null legacy. Opening detail and toggling via Enter visibly showed all three cases; aria-expanded matched visible state, no price 777.77/private rule/create command visible. A compound locator call was ambiguous and is not counted as successful clicking; subsequent distinct keyboard actions were observed. Desktop snapshot content evidence does not prove final responsive layout or network counts.

Before UI observation: non-session audits 14, claims 13, stock 100, reserved 0, ledger 1, receipts 5, policies 1, lines 7. This is a harness-created baseline, not proof of post-observation zero effects. At later handoff both owned PIDs were absent; lifecycle had StartupReady but no exit/cleanup marker. Cause remains UNKNOWN. Guarded Stop subsequently confirmed database absent/processes stopped/credential removed. Exact fixture/postcondition helper files removed; the tab is being reused for a fresh owned successor, not silently counted as closed. Windows clipboard remains UNKNOWN and untouched.

Fresh successor `6ef8b4fdc0304c0e84ff5aa3cc282772` uses a new marker/database/credential; startup returned healthy. Owner entered the new Viewer credential directly. Codex In-app Browser (browser 2, owned tab 1), `/import-receipts`: sign in → Phiếu nhập kho → QA-QC-BASIS → Chi tiết → expand the three line bases. Saved Product/version 7, explicit None/no-QC and null legacy were visibly distinguished without policy grants. No policy ID, rule or price was displayed. Business identifiers/names in synthetic fixtures are test data, not untranslated application labels.

Utility successor viewports: **375×812, 768×1024, 1280×900**. Document scroll widths were respectively **360, 753, 1265px** (vertical scrollbar accounted for), outer scrollLeft stayed **0**. At 375px, the 288px line region contained a 588px table; the 256px basis remained readable by scrolling that region, with the basis fully visible at inner scrollLeft 78px. At 768px the line region was 431px; long Vietnamese text wrapped. Desktop showed all three cases. Enter/Space opened and closed the native button, Tab moved to the next line and Shift+Tab returned; collapse retained focus and aria-expanded=false. No modal was introduced, so no new dialog containment/Escape claim is made. One batched action reached a renamed button after opening the three rows and failed to find the old name; that final attempted action is not counted as PASS. A subsequent freshly observed collapse verified the intended behavior.

Owned SQL setup then changed master policy 1 to version 8/no-QC and a later effective start. Utility reload/reopen still displayed the saved required-QC version 7 and 01/10/2026 08:30:00 with the missing-zone caveat. SQL is fixture setup/postcondition, not browser policy administration. Before/after utility reads: non-session audits **36→36**, claims **19→19**, stock **100→100**, reserved **0→0**, ledger **1→1**, receipts **20→20**, movements **0→0**; three saved line decisions/source/version/effective fields unchanged. Authentication session audit is excluded explicitly. No utility network-count claim is inferred from the screen.

After observations, viewport override reset and exact owned tab 1 closed; browser inventory was empty. Official Stop validated manifest/PID/database ownership and confirmed API/frontend stopped, owned database absent and encrypted credential removed. Runner profile was already absent. Exact fixture helper removed; ignored local logs/evidence remain unstaged. Clipboard ownership remains UNKNOWN and was not read or cleared. The precursor remains a separate partial/failure record.

Supplemental runner selection `^Mounted receipt list/detail/print late responses$`: **1/1 selected group PASS, 3/3 mounted delays**, exit 0. Existing Playwright 1.62.1, official helper/capture and unchanged test entry were reused; no new runner/dependency/hook. Actual original HTTP 200 captures occurred at 16:46:59.042Z (list), 16:47:04.368Z (detail), 16:47:08.610Z (print). Current DB grant revoke/regrant returned 200/200, application 403 refresh loaded identity 200, original heading stayed connected through three checks each, current fresh data won and old data/dialog did not return. Each administrative pair produced exactly two audits and two claims; these are intentional permission administration effects, not read effects. End postconditions: stock 100, ledger 1, movements 0, audit 42, claims 19. The runner used generic receipt fixtures and verifies shared response guards, not a separate utility QC workflow or raw-policy master lookup. Its actual ephemeral profile was verified absent after bounded runner cleanup; official API/frontend remain active for utility handoff. No old run retry, limiter override or full permission matrix rerun.

Final reviewed code/test association: accepted base plus working diff fingerprint `f5ef2dbf107ab76602ffc6b9a73e6f678eea433df51cbbdd0100be07444e2e20`, SHA-256 of sorted newline-joined `relative-path:lowercase-SHA256(file)` for the six changed/new code and test files. The runner records Git base HEAD; this additional fingerprint identifies the uncommitted source actually exercised. Final affected checks after preserving the existing QC state label: **21/21**, lint/build/audit PASS. UTF-8, diff, private-key/literal credential/token/connection-string and binary/artifact scans cover the eight explicit implementation/test/doc files. Internal Ponytail/correctness review found no new authorization/model mutation or speculative layer; it is not independent review. Final read-back of Page 17/84 retained the native timestamps above. Publication/CI/Sonar remain pre-push gates, with final source-associated results in the Draft PR rather than a CI-only documentation commit.

## Preserved backlog and stop gates

Historical migration `__EFMigrationsLock` and Location Move=500/deactivate=200 causes remain **OPEN — NOT REPRODUCED**, with the accepted diagnostic instrumentation and closure criteria in PR #34. This slice does not repair or classify those causes as environment/flaky. Preserve the 49 CODE_SMELL catalog backlog at accepted `2eaea607` and the older 75 at Manual `7b039993`; no suppression or metric-only refactoring.

Policy administration, additional effective ranges, full policy catalog and membership UI remain outside this slice. Backup/restore remains DEFERRED_BY_OWNER. Commit/push/new Draft PR only after mandatory slice gates pass. Ownership cleanup is exact; protected/UNKNOWN/policy-blocked resources stay untouched. No ERP_KHO business-table access. REMOTE STAGING NOT AUTHORIZED. CAPACITY EXECUTION NOT AUTHORIZED. **PRODUCTION NO-GO.**
