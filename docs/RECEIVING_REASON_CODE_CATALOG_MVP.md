# Tra cứu danh mục lý do nhận hàng và lịch sử phiên bản

## Current checkpoint

**LOCAL VERIFICATION PASS — successor Sonar/CI handoff pending.** Draft PR #34, branch `feature/receiving-reason-code-catalog`, accepted Manual base `7b039993832eca715c3210e0782b9d43394f635e`; source-specific local automated, utility UI and supplemental mounted-browser evidence is recorded below. No migration, permission seed, bundle, dependency or production workflow changes. PR #32 remains Draft and its owner acceptance is limited to the accepted Manual source. Existing 75 CODE_SMELL findings remain in their associated backlog; they are neither suppressed nor claimed fixed. Final published HEAD/CI/Sonar will be recorded in PR #34's body, not assigned from an older PR.

### Published-head check and narrow test-runner fix

At source `17073b2808f6c578e0be364eae386ab9e9d33690`, CI **38162903482 SUCCESS**: Application **357/357**, API **217/217**, frontend **118/118**; build/lint/dependency audit passed, zero failed/skipped and zero dependency vulnerabilities. SonarCloud was independently **ERROR** for one vulnerability, `javascript:S4036`, `tools/ERP.BrowserQa/reason-catalog-browser.mjs:22`: source-HEAD lookup searched PATH for Git. It is not an API authorization finding. The issue was fixed by using the verified absolute Git for Windows executable in its protected Program Files directory, with no PATH fallback, suppression or gate change.

Affected successor checks: runner syntax PASS; the exact executable selected by the runner returned source HEAD `17073b2` with an empty PATH; existing capture/ownership/redaction/error-propagation regressions **18/18 PASS**, zero failed/skipped. The one-line executable change does not alter browser coordination, UI, API or model, so the utility UI and mounted 3/3 evidence below remain associated with unchanged behavior. No new QA database/process/credential was created, and full SQL/frontend/browser suites were not repeated solely for source-HEAD metadata lookup. The successor commit's CI and Sonar results must be checked before claiming READY.

Precursor fix `24c3e60d425718fc321e05bb5af880d0e6e983fd` used a Windows absolute path with forward slashes; the executable check passed but its Sonar analysis still reported S4036. [SonarSource's rule implementation](https://github.com/SonarSource/SonarJS/blob/master/packages/analysis/src/jsts/rules/S4036/rule.ts) recognizes a `C:\` prefix, not `C:/`. The successor uses the normal escaped Windows-backslash literal for the same verified protected executable. It again passes syntax, empty-PATH source-HEAD lookup (at `24c3e60`) and **18/18** helper tests. This is not an accepted/suppressed issue or altered gate; final Sonar/CI must independently confirm the successor. The failed precursor remains recorded.

The first inline executable probe for the backslash spelling had a quoting SyntaxError; it is not counted as PASS and created no resources. The corrected probe decoded the runner's actual literal with JSON.parse, asserted Windows absolute-path semantics, and successfully executed it with an empty PATH. The runner itself passed its separate Node syntax check.

PR #34's first analysis also reported **48 CODE_SMELL** findings (1 Critical, 9 Major, 38 Minor), separately from the existing 75-item Manual backlog. The Critical item is `javascript:S6859` on the browser-context Vite `/src/services/apiClient.ts` import; it is a maintainability classification, not automatically a security vulnerability. Findings are retained, not suppressed or blanket-refactored to lower metrics. No owner acceptance is inferred from CI or a quality gate.

## Canonical contract and freshness

Owner-approved read-only addenda were written and read back before implementing filtering/visibility. The connector did not expose a `truncated` field; no `truncated=false` assertion is made.

| Source | Native timestamp before | Native timestamp after/read-back | Relevant contract |
| --- | --- | --- | --- |
| Page 17 §33.4.5 | 2026-10-08T16:48:56.271Z | 2026-10-11T07:47:43.461Z | All three catalog reads require database `reason_code.read`; no new grants |
| Page 20 §22 | 2026-09-23T15:38:55.860Z | 2026-10-11T07:47:40.820Z | UTC `[EffectiveFrom, EffectiveTo)`, null end unlimited, independent activity/time, overlap retained |
| Page 84 §16 | 2026-09-24T16:04:32.570Z | 2026-10-11T07:47:42.178Z | Global all-version catalog; master versions are distinct from saved transaction snapshots |
| Page 222 | No trustworthy stored baseline | 2026-09-17T20:32:34.688Z | CHANGE STATUS UNKNOWN; its visual content does not determine this slice's authorization/effectivity |

The catalog is global. A user with `reason_code.read` can view inactive, future, expired and overlapping versions without warehouse membership. This does not authorize reading transaction documents or changing the existing workflow's effective-reason selection. Activity and temporal effectivity are separate filters. Reading never repairs overlap, chooses a winner, updates effectivity, creates mutation audit/claims or recalculates historical snapshots.

## Implementation and endpoint mapping

| Endpoint | Permission | Behavior |
| --- | --- | --- |
| `GET /api/receiving-reason-codes` | `reason_code.read` | Code/name search, independent activity/time filters, stable code/version/ID order, filtered count and pagination |
| `GET /api/receiving-reason-codes/{id}` | `reason_code.read` | Exact master-version detail; unknown ID returns safe 404 |
| `GET /api/receiving-reason-codes/versions/{code}` | `reason_code.read` | All versions of the code, including overlap and inactive/future/expired entries; no effective-only API imitation |

`ReceivingReasonCatalogQuery` reads only `ReceivingReasonCodes` with `AsNoTracking` and one UTC instant per query. The response allowlist contains business identifiers, version/name/category, activity/effectivity, UTC range and note/evidence/approval requirements. It contains no concurrency token, private evidence, transaction usage, cost/value or idempotency metadata. Existing permission/session checks remain authoritative; no role/JWT alternative is introduced.

The Vietnamese `/receiving-reason-codes` page reuses MainLayout, permission context, inner-scroll tables and AccessibleDialog. Menu/route/page reads use `reason_code.read`. UTC is explicit, dates use Vietnamese formatting, and history explains the difference between the original catalog and saved transaction snapshots. List cancellation plus separate list/detail generations discard older responses; permission-context changes clear the table, dialog and history. No mutation controls exist.

## Acceptance / verification matrix

| Case | Planned verification | Current evidence |
| --- | --- | --- |
| Database grant/no grant, stale Admin claim, disabled actor | SQL-backed authenticated HTTP | Focused HTTP PASS; not a real-JWT utility-browser claim |
| Global read without membership; list/detail/history | SQL HTTP and utility UI | PASS; Viewer remained logged in and read 30 versions with zero warehouse memberships |
| Search, pagination/count, independent filters | Query/SQL HTTP/component and utility UI | PASS; utility 20+10 rows, case-insensitive code search, inactive/future/expired/empty filters |
| Exactly From, exactly To, null To, inactive, overlap | Fixed-clock query and SQL HTTP | PASS; no rounding or winner selection |
| Stable transaction snapshot; zero master/doc/stock/ledger/audit/claim writes | Query/SQL postconditions | PASS; QA fixture grants are outside the read-only measurement |
| Loading, empty, safe error and missing detail | Component/HTTP and utility UI | PASS; utility loading/disabled, empty and real permission-denied states; unknown ID/other errors are HTTP/component evidence |
| Mounted late list/detail/history after revoke→regrant | Component promises and supplemental real-browser response capture | PASS 3/3 mounted component-in-browser, distinct from utility UI; original authorized 200 responses, no older data/dialog restoration |
| Keyboard containment, Escape/return focus, mobile inner scroll | Component and utility UI | PASS; utility Tab/Shift+Tab/Escape/return focus, 375/768/1280 containment and inner-scroll table |

## Automated evidence (source-specific, not browser closure)

Release solution build: PASS, zero warnings/errors. EF pending-model check: PASS, no changes. Focused query tests: **5/5**. Focused SQL HTTP: **1/1**, Run `838945715aa045a49fd8ead049d0957b`, exact owned database cleaned. Full Application SQL: **357/357**, zero failed/skipped, Run `69ed25df85664c74996a8b4913197bb7`; TRX recovered from the completed run rather than rerunning it, exact database absence verified by metadata.

Full API SQL/HTTP: **217/217**, zero failed/skipped, duration 7m40s; `TestResults/ReasonCatalog/FullApi/api.trx`. The official per-test owned runs include `d2893455c1bb4721b260e9e92d586ac1` and 42 additional Run IDs retained in the ignored TRX, rather than an invented single suite database. All 43 exact database names were verified absent via metadata after completion.

Frontend full tests: **118/118**, 21 files, zero failed/skipped. A precursor focused run was **7/8** because its assertion expected a different Vietnamese date order; the corrected explicit four-digit-year UTC presentation and assertion passed the successor. The later literal-only translation of “master/snapshot” passed the affected component tests **8/8**; final lint, production build and dependency audit passed (zero vulnerabilities). No historical CI/Sonar result is assigned to this uncommitted source.

Source association: accepted Git HEAD plus the exact reviewed working diff; SHA-256 `ca1f5362bfb229dbc307baa394daef3146ee1ee62c6ad5bf7f235a2604553a38` of newline-joined `relative-path:SHA256(file)` records emitted by `git ls-files --modified --others --exclude-standard`, excluding docs. Fourteen source/test/docs files were scanned as UTF-8; no private-key, token, literal credential/connection-string or binary/runtime-artifact findings. Index remains empty.

The preceding fingerprint is the historical pre-runner checkpoint. Final reviewed code/test fingerprint is `406d0cc5d0a90985b3683ac1f730a252e42cef27e1773f49b5db1fca14088b7e`, SHA-256 of sorted newline-joined `path:SHA256(file)` records excluding Markdown across the final 19-file diff. UTF-8/diff/secret/private-key/artifact scans found no actual secret or runtime artifact; six regex candidates were reviewed as `password=await`/`password=undefined` runtime handling, not literal credentials. Capture/ownership/redaction regression checks passed **18/18** after helper export reuse. A lint invocation at the repository root reported a missing script and is not PASS; the correct frontend invocation passed. Its earlier test-entry Fast Refresh export warning was corrected without suppression, then lint/build passed. Backend/model did not change during these test-only additions, so the preceding full SQL evidence remains source-associated rather than unnecessarily rerun.

## Utility-browser evidence

Official owned Run `c278d1684ab74e6fbe791c6facf543a8` reached StartupReady at `2026-10-11T17:49:10.7685725Z`; API 5265/frontend 4175 health checks passed and listener PIDs matched the manifest. QA-only fixture setup created 30 catalog versions, including inactive, future, expired and overlapping rows plus pagination data. Baseline: stock 100, reserved 0, ledger 1, receipt count 4, exports/reservations 0, mutation audits 16 and claims 13. These are harness setup effects, not catalog effects or browser observations.

Precursor: Codex In-app Browser (`cua_repl`) `createBrowserTab` failed with **“Timed out waiting for the Browser webview to attach for this browser-use page”**; subsequent inventory showed zero tabs. `open_in_codex` returned `queued` for this calling thread. This was attachment/availability, not an automatic execution-policy rejection, URL-policy denial or demonstrated production application error. The supported troubleshooting documentation was read; no driver/transport/security workaround was used.

Successor inventory subsequently exposed tab `1`, provider `4c6d4e7d-42f4-40a6-899a-70d0bdfc3af3`, at the exact loopback login URL. The utility bound it and displayed the Vietnamese login labels. Owner entered the synthetic password directly; the utility submitted login as `qa_viewer_browser`. No password field, clipboard or token was read by the utility. The attachment blocker was resolved, not hidden by changing driver/transport.

Utility actions at `http://127.0.0.1:4175/receiving-reason-codes`: menu navigation, observed loading/disabled controls, 30-version count, next page with 10 rows, `qa_catalog` search with both overlapping versions, exact version detail/history, requirements and Vietnamese UTC ranges, inactive combined with future empty result, future and expired filters. Tab/Shift+Tab trapped focus between Close/history region; Escape removed the dialog and returned focus to the exact version-2 trigger. No create/edit/publish controls or transaction data were presented.

At 375×812, outer document width was 360 (scrollbar), table client/scroll widths 318/980; dialog width 324 and history region 279/980. Escape returned focus to the action while inner table scrollLeft became 652 and outer scrollX stayed zero. At 768×1024, document width 768/table 476/980; at 1280×900, document 1280/table 988/988. Screenshots and read-only DOM metrics were observed through the utility; no full-page overflow hiding was added. A first screenshot immediately after viewport resize had an incomplete paint; the subsequent fresh screenshot showed the complete desktop layout. It is not presented as an application overflow finding.

SQL fixture revoked only Viewer `reason_code.read`; utility clicked Reload. After normal identity refresh, the menu disappeared, table/dialog counts were zero, older display names were absent and safe Vietnamese 403 copy appeared. SQL fixture regranted the code and removed Viewer memberships; utility reloaded in the same session and displayed 30 versions with zero memberships. These SQL changes are fixture setup, not a browser permission-administration workflow. A selector lookup during refresh/temporary layout remount timed out; fresh accessibility state resolved the control and final assertions passed. No unsupported force/evaluate or retry of a policy-denied action was used.

The utility documentation does not provide request interception, raw-response assertions or controlled delay. Those are explicitly separate SQL-backed HTTP and supplemental runner evidence; no network count/raw response is attributed to the utility.

### Supplemental mounted-browser late responses

`reason-catalog-browser.mjs` reuses the official helper, ownership/redaction functions and verified `captureOriginal`/bounded cleanup with installed test-only Playwright 1.62.1. The test-only HTML entry mounts the unchanged production page outside MainLayout's loading gates; it is excluded from the production entry/bundle. Authentication and real 403 identity refresh use the normal apiClient. No mock identity, production endpoint/hook, security/limiter override or second SQL/credential harness.

Precursor `reason-catalog-browser-1791741779441.json`: list PASS, detail FAIL, history NOT RUN, exit 1, profiles removed; retained as **1/3 partial**, never full-run PASS. Its attempt to use an outside refresh button while a dialog makes siblings inert was invalid coordination; the test was changed to a real apiClient request from browser context. The first sanitized report has only a failure hash, not a precise failure line; that diagnostic limitation is retained. Successor reports preserve a safe failure location/phase if failure occurs.

Successor `reason-catalog-browser-1791741945365.json`: **3/3 PASS**, exit 0, profiles removed. Original list 200 received `18:05:57.933Z`, released `18:06:06.405Z`; detail `/10` 200 received `18:06:09.476Z`, released `18:06:24.269Z`; history 200 received `18:06:28.984Z`, released `18:06:43.085Z` on 2026-10-11. Each original contained older QA-only names held in memory. Through revoke/regrant/new-name fixture coordination and real apiClient 403 refresh, the same heading element stayed connected; releasing old responses restored neither older names nor a dialog. Master fixture names were restored afterward.

### SQL postconditions and cleanup

Final full master-row hash remains `9338CC535DD061618C9B0ADCAD943E5A6EC12A48BB993CB40C2CE32C8F345023` with 30 versions; stock 100, reserved 0, ledger 1, receipts 4, exports/reservations 0, non-session mutation audits 14 and claims 13 remained fixed. Additional audits are explicitly authentication session events (3 LoginSucceeded, 2 RefreshRotated at the final measurement), not catalog mutations. Query tests separately verify historical transaction snapshots without reading them for the catalog.

Viewport override was reset and the exact utility tab was closed; inventory returned no tabs. Guarded Stop validated exact Run ID/database marker and process identity, then reported database absent/processes stopped/credential removed. The exact QA-only fixture JSON was removed without recursive deletion; runner temporary profiles were verified removed. Unknown/policy-blocked resources, clipboard, other worktrees and user-owned paths were untouched. No ERP_KHO business tables were accessed.

Final metadata-only check confirmed ERP_KHO ONLINE, the BrowserQA/focused/Application databases absent and no 4175/5265 listener. All 43 API-suite database names were separately verified absent. Notion 17/20/84 final native timestamps remained equal to the read-back values above; **UNCHANGED** since the owner addenda. No further contract edits. Drive illustrations were not rendered or used as authority for this slice and remain NOT REVIEWED.

## Internal review

Ponytail review: reused clock, EF query/DI boundary, permission context, dialog, inner-scroll styles and official runner helpers; no dependency, override/cache/engine or speculative administration. Correctness/security review traced three reads through database permission/session checks and entity-only allowlist. Count/pagination/history retain overlap; activity is independent from UTC effectivity. Static/automated review is internal, not independent. Utility acceptance and supplemental response coordination are separately scoped above. No unresolved source correctness/security finding was established. Final published-head CI/Sonar remain the handoff gate; existing maintainability backlog is not suppressed.

## Deferred / limitations

Create/edit/publish/version administration, policy administration, attachment engine, approval workflow and transaction-usage history are excluded. Existing workflow selection and business quantity/stock/reservation/ledger boundaries remain unchanged. Membership UI and backup/restore remain DEFERRED_BY_OWNER. Drive illustrations not rendered remain NOT REVIEWED.

No commit/push until mandatory slice gates pass. All QA uses the existing Run ID/ownership-marked harness; no ERP_KHO business-table access. Keep unknown/policy-blocked resources unchanged. PRs remain Draft; no merge/deploy/remote staging/capacity. **PRODUCTION NO-GO.**
