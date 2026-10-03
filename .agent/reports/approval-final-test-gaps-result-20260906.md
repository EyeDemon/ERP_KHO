# Approval Final Test Gaps Result - 2026-09-06

## Executive summary

Status: `APPROVAL_FINAL_TEST_GAPS_CLOSED / READY_FOR_OWNER_RE_REVIEW / NO-GO PRODUCTION`

This test-only slice closes F1 with controlled concurrent HTTP requests through the complete application pipeline and closes F2 with dialog lifecycle, inert restoration, repeated-cycle and failed-submit focus assertions. No production source was changed and the tests did not expose a production runtime defect.

## Scope and exclusions

Changed only:

- `ERP.Api.Tests/ApprovalHttpIntegrationTests.cs`
- `frontend/src/components/AccessibleDialog.test.tsx`
- `frontend/src/pages/Approvals.test.tsx`
- This report

No migration, schema, business service, controller, idempotency production code, dialog production code, Approval page production code, index, backup automation or feature scope was changed.

## F1 - Controlled concurrent HTTP execution

The new `ConcurrentSameKeyHttpApprovals_OverlapAtIdempotencyBoundaryAndExecuteOnce` test creates an owned SQL Server database and sends two real HTTP POST requests to the same StockTransfer approval route with the same checker, document ID and idempotency key.

The requests traverse:

- `WebApplicationFactory` HTTP test host.
- Test authentication handler.
- Checker authorization policy.
- Routing and model binding.
- `IdempotentCommandFilter`.
- Controller and dependency injection.
- `StockTransferService`.
- EF Core and SQL Server.

### Controlled overlap

A test-only `SaveChangesInterceptor` is registered only in the test factory instance. It detects an added `IdempotencyRecord` for `StockTransfer.Approve`, counts arrivals and holds both requests on a release gate.

Evidence before release:

- Interceptor arrivals: exactly 2.
- Both HTTP request tasks: not completed.
- No arbitrary delay is used to claim concurrency.

Only after this assertion does the test release both requests. The hook has no production configuration path and does not change application behavior.

### HTTP and persistence results

- Request 1: HTTP 204.
- Request 2: HTTP 204, durable replay-equivalent outcome.
- HTTP 500 responses: 0.
- Persistent business transition: exactly one `Draft -> Approved`.
- `StockTransfer.Approved` audits: exactly 1.
- Completed idempotency records for actor/scope/key: exactly 1.
- Third replay after completion: HTTP 204 with the same response body.
- Same key with another document: HTTP 409; other document remains Draft.
- Raw key/hash: absent from responses.
- Stored request fingerprint: present, non-raw, and not equal to the key.
- Stock, reservation and inventory-ledger snapshot: unchanged.

Existing tests continue to prove different framework cancellation tokens do not alter the fingerprint, while different document IDs do. Existing SQL concurrency tests continue to prove one filter/business execution under same-key contention.

## F2 - Dialog lifecycle evidence

### Inert restoration

The component tests now create siblings with initially false/unset and initially true inert states. They verify all background siblings are inert while the dialog is open, the portal is not made inert, and direct unmount restores each original value. A sibling originally inert remains inert; an active sibling is no longer inert after close.

### Failed-submit focus

The Approval page test holds a Reject API request pending, confirms the submit control is disabled, rejects it with a correlation-bearing 409, and verifies:

- The dialog remains open.
- The correlation error is rendered.
- Active focus remains inside the dialog.
- Focus is neither `body` nor the background trigger.
- The reason can be edited.
- Retry succeeds.
- The retry uses the same logical idempotency headers.
- Exactly two calls occur: the failed logical attempt and its explicit retry, with no busy-state duplicate.

### Repeated cycles and cleanup

Three open/close cycles verify initial focus, one Escape callback per cycle, focus restoration to the correct trigger, inert cleanup and absence of leftover dialog portals. A busy-unmount case verifies Escape is suppressed, inert state is restored and the removed listener does not fire after unmount.

## Test-development observations

The first targeted frontend run reported three test-code failures:

- Two assertions expected `false` after an initially unset jsdom `inert` property was correctly restored to `undefined`.
- One correlation assertion used a singular text query although the UI intentionally renders the same error in the page banner and dialog.

Assertions were corrected to preserve the actual pre-open inert state and scope the correlation check to the dialog. A later TypeScript build found one test-only `Element`/`HTMLElement` typing issue; an explicit test cast fixed it. No production source was changed for these failures.

## Targeted results

| Gate | Result |
| --- | --- |
| Concurrent same-key HTTP integration | PASS within API suite |
| HTTP replay and different-document conflict | PASS |
| Existing direct HTTP authorization matrix | PASS |
| AccessibleDialog lifecycle tests | PASS |
| Approval failed-submit focus test | PASS |
| Approval + dialog targeted tests | 14/14 PASS, 0 skipped |

## Full quality gates

| Gate | Result |
| --- | --- |
| Application + isolated SQL integration | 295/295 PASS, 0 skipped |
| API including new HTTP concurrency test | 144/144 PASS, 0 skipped |
| Legacy SQL business tests | PASS within isolated Application suite |
| Operational backup test | DEFERRED / NOT EXECUTED / not counted as PASS |
| H1/Queue/History/Reject/race/idempotency regressions | PASS |
| Backend Release build | PASS, 0 warnings, 0 errors |
| Full frontend suite | 25/25 PASS across 7 files |
| Frontend lint | PASS |
| Frontend production build | PASS |
| EF pending-model check | PASS, no pending model changes |
| Encoding check | PASS |
| NuGet vulnerable-package scan | PASS, none reported |
| npm audit | PASS, 0 vulnerabilities |
| git diff --check | PASS; existing line-ending notices only |
| Changed-test secret scan | PASS |
| Raw key/hash/fingerprint response review | PASS |

## Safety snapshot

The safety harness captured the real database state, business-table hashes/counts, backup history, backup-file inventory, Scheduled Task definitions and integration database inventory before and after execution.

- Owned test database: `ERP_KHO_Integration_20260905_171007_3cc034be`
- Run ID: `3cc034be20a74fb6b957c5b86866ff71`
- Local Integrated Security, prefix, denylist, ownership marker and `DB_NAME()` guards: PASS
- Exact owned cleanup: PASS
- Remaining integration databases: 0
- `ERP_KHO` before/after: `ONLINE/SIMPLE`
- Business tables unchanged: true
- Backup history unchanged: true
- Backup files unchanged: true
- Scheduled Tasks unchanged: true
- `ERP_KHO-SQLBackup-Log-10Min-Pilot`: Disabled before/after
- HEAD before/after: `6c20b48505da6e9db141f84e6fc0f212363e344f`

No production database mutation, backup, restore, recovery change, task mutation, email, Zalo call, commit, push or deployment occurred.

## Findings

No new CRITICAL, HIGH, MEDIUM or LOW production finding was discovered. F1 and F2 now have direct automated evidence.

Composite-index assessment remains INFO and evidence-gated. Approval Notifications, Aging/SLA Indicators and bulk approval remain outside this slice.

## Recommendation

Perform an independent Owner Re-review of the test-only changes. This result closes the identified evidence gaps but does not authorize migration, release, deployment, backup automation or production use. Production remains `NO-GO`.
