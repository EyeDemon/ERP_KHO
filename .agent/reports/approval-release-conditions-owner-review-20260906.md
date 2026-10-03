# Approval Release Conditions Owner Review - 2026-09-06

## Executive summary

This was an independent, review-only assessment of M1, M2, M3, L1 and the shared idempotency fingerprint change. Source, test implementation, real HTTP responses, isolated SQL Server execution and safety snapshots were checked directly.

H1 remains closed and no CRITICAL/HIGH regression was found. M1 is closed. M2's required page behaviors are materially covered. M3 authorization is proven through the real HTTP pipeline, but concurrent same-key execution is only proven at the SQL/filter boundary rather than by concurrent HTTP requests. L1 implementation is sound on inspection, while two lifecycle/failure-focus assertions remain absent from automated component coverage.

Decision: `APPROVAL_RELEASE_CONDITIONS_OWNER_APPROVED_WITH_CONDITIONS / REMEDIATION_REQUIRED / NO-GO PRODUCTION`

## Scope and exclusions

Reviewed backend Approval mapping, DTOs, controllers, JSON behavior, StockTransfer approval, checker and warehouse authorization, idempotency filtering, isolated SQL harness and HTTP tests. Reviewed frontend Approval page, API helpers, Vitest/jsdom configuration and accessible dialog implementation/tests.

No source/test was edited. No migration, real-database mutation, backup, restore, recovery change, task change, email, Zalo call, commit, push or deployment occurred. Notifications, Aging/SLA, bulk approval and composite indexes remain excluded.

## Diff and source review

The remediation was separated from the already-dirty working tree using its explicit file list and line-level inspection. HEAD remained `6c20b48505da6e9db141f84e6fc0f212363e344f`.

Key evidence:

- `ApprovalWorkflowService` applies `AsUtc` only while mapping Approval queue/detail/history timestamps.
- `StockTransferService.ApproveAsync` performs checker-role validation, scoped lookup, source access, destination access and maker-checker validation before transition.
- StockTransfer list/detail retain the existing source-OR-destination read rule.
- `IdempotentCommandFilter.Fingerprint` skips values whose runtime type is exactly compatible with framework `CancellationToken`; all remaining route/body/query action arguments are canonically serialized by name.
- Approval DTOs do not expose raw idempotency keys, hashes or request fingerprints.

## M1 - UTC wire-contract evidence

Result: PASS.

The SQL-backed HTTP test stores a known `DateTimeKind.Utc` value in SQL Server, then independently proves SQL `datetime2` materializes it as `DateTimeKind.Unspecified`. Mapping converts known Approval workflow UTC fields to `Kind=Utc` without changing persisted data or schema.

Actual HTTP JSON is asserted for:

- Queue `requestedAtUtc`.
- Detail `requestedAtUtc` and nested history.
- Per-document history `timestampUtc`.
- Scoped history `timestampUtc`.

All are asserted as `2026-09-05T01:02:03Z`. Equivalent `Z` and `+07:00` boundaries return the same row; a one-second-later boundary excludes it. Frontend sends `datetime-local` values through `toISOString()` and renders the offset-bearing instant via `Date`/`toLocaleString()`.

The normalizer is confined to Approval workflow mappings. Repository evidence shows these source fields are UTC-designated and created with UTC timestamps; no unrelated timestamp contract or SQL value is rewritten. EF reports no pending model changes.

## M2 - Frontend behavioral tests

Result: PASS with LOW test-maintainability risk.

`Approvals.test.tsx` mounts the real page in jsdom and mocks only `apiClient`. Assertions exercise loading, empty/error states, queue data, filters, pagination, detail loading, history, capability buttons, approve confirmation, reject validation/submission, duplicate-submit prevention, `409` refresh, correlation ID, three distinct history labels, safe text rendering, UTC conversion and local-time rendering.

The tests are behavioral rather than snapshots and would fail if the corresponding controls, requests, dialog state or rendered output were removed. Targeted result: 10/10 across the Approval page and dialog files.

## M3 - Direct HTTP authorization

Result: PASS for authorization; MEDIUM evidence condition remains for HTTP concurrency.

`ApprovalHttpIntegrationTests` uses `WebApplicationFactory`, test authentication, the real checker policy, model binding, `IdempotentCommandFilter`, controller, DI, StockTransfer service, EF Core and an owned SQL Server database.

| Case | Verified result |
| --- | --- |
| Manager source-only | 404 |
| Manager destination-only | 404 |
| Manager no scope | 404 |
| Manager both scopes, non-maker | 204 |
| Admin non-maker | 204 under existing global policy |
| Manager/Admin maker | 403 |
| WarehouseStaff/Viewer | 403 |
| Non-Draft/new command | 409 |
| Same key and fingerprint replay | 204; one transition/audit |
| Same key, different document | 409; second document remains Draft |

Denied cases assert Draft state, zero approval-success audit and unchanged stock/reservation/ledger snapshot. Successful cases assert Approved state, exactly one `StockTransfer.Approved` audit and a completed persistent idempotency record.

## Idempotency CancellationToken review

Root cause is confirmed: MVC supplies a framework `CancellationToken` action argument; serializing its object graph reaches `WaitHandle/IntPtr` and can produce HTTP 500.

The fix skips only argument values satisfying `argument.Value is CancellationToken`. It does not filter by a broad name pattern and does not omit route ID, query values, request DTOs or reject reason. Canonical sorting is retained.

Evidence:

- Different token values with the same business ID produce the same fingerprint.
- A different document ID produces a different fingerprint/conflict.
- HTTP same-key/same-document replays successfully.
- HTTP same-key/different-document conflicts.
- Existing SQL filter concurrency executes business logic once and persists one audit/ledger mutation.
- Raw key is SHA-256 hashed and raw key/hash/fingerprint are absent from Approval response DTOs.
- Transaction ownership, commit/rollback and replay reauthorization were not changed by this fix.

## L1 - Accessibility assessment

Result: PASS implementation / LOW automated-evidence gap.

`AccessibleDialog` is portaled to `document.body`, has `role="dialog"`, `aria-modal="true"` and `aria-labelledby`. It sets initial focus, wraps Tab/Shift+Tab, closes on Escape only when not busy, marks background siblings inert, contains escaped focus, restores prior inert values, removes both listeners and returns focus to the opening control on unmount.

Tests directly prove initial focus, both focus-wrap directions, Escape, busy Escape suppression, trigger restoration, successful close and duplicate-submit prevention. Error tests prove the dialog remains open and the action can be retried with the same logical key.

## H1 and workflow regression

H1 remains closed at the service security boundary. The complete Application/SQL and API suites retain maker-checker, checker policy, non-disclosure, Approval Queue/detail/history, Reject `Draft -> Cancelled`, `ApprovalRejected`, existing Cancel, security `.Rejected`, idempotency, audit atomicity and race tests. Stock/reservation/ledger snapshots remain unchanged for rejected HTTP approvals.

## Quality gates

| Gate | Status | Independent evidence |
| --- | --- | --- |
| Targeted UTC HTTP and direct authorization | PASS | Included in API run against owned SQL database |
| Targeted fingerprint tests | PASS | 24/24 ApprovalRequestSafety tests |
| Application + SQL integration | PASS | 295/295, 0 skipped |
| API | PASS | 143/143, 0 skipped |
| Legacy SQL business tests | PASS | Included in isolated Application suite; operational backup test excluded |
| Operational backup test | DEFERRED / NOT EXECUTED | Not counted as PASS |
| Release build | PASS | 0 warnings, 0 errors |
| Approval/Dialog component tests | PASS | 10/10 |
| Full frontend suite | PASS | 21/21 across 7 files |
| Frontend lint/build | PASS | oxlint and Vite exit 0 |
| EF pending-model check | PASS | No pending model changes |
| Encoding | PASS | Required script exit 0 |
| NuGet vulnerable packages | PASS | No vulnerable direct/transitive package reported |
| npm audit | PASS | 0 vulnerabilities |
| git diff --check | PASS | Exit 0; line-ending notices only |
| Changed-content secret scan | PASS | No credential/private key/token/connection-string finding |
| Raw key/hash/fingerprint response review | PASS | Internal persistence only; absent from Approval DTO/controller response |

## Safety snapshot

The review harness captured the real database, business-table hashes/counts, backup history, backup files, Scheduled Task definitions and integration database inventory before and after testing.

- Owned database: `ERP_KHO_Integration_20260905_121257_adb55583`
- Run ID: `adb55583966d4baa8d247e87eee0997b`
- Prefix, local Integrated Security, `DB_NAME()`, marker and denylist guards: PASS
- Exact owned cleanup: PASS
- Remaining integration databases: 0
- Real database before/after: `ONLINE/SIMPLE`
- Business tables unchanged: true
- Backup history/files unchanged: true
- Scheduled Tasks unchanged: true
- `ERP_KHO-SQLBackup-Log-10Min-Pilot`: Disabled before/after

Only test-result artifacts and this Owner Review report were produced. Source/test working-tree content was not modified by the review.

## Remaining findings

### MEDIUM F1 - Concurrent same-key coverage does not traverse HTTP

- Location: `ERP.Api.Tests/ApprovalHttpIntegrationTests.cs:77`; `ERP.Api.Tests/SqlServerApprovalIdempotencyTests.cs:79`.
- Evidence/reproduction: the HTTP test sends replay requests sequentially. The concurrent test invokes `IdempotentCommandFilter` directly against SQL rather than issuing two concurrent HTTP requests.
- Impact: filter/database uniqueness and one-execution semantics are proven, but test authentication, routing, model binding and controller behavior under concurrent HTTP requests are not jointly exercised.
- Recommendation: add one owned-database HTTP integration test that sends two simultaneous same-key/same-document requests and asserts one business execution, equivalent responses, one transition and one success audit.
- Owner acceptance blocker: Yes for declaring all release conditions closed; no CRITICAL/HIGH product defect is demonstrated.

### LOW F2 - Dialog lifecycle/failure-focus assertions are incomplete

- Location: `frontend/src/components/AccessibleDialog.test.tsx:8`; `frontend/src/pages/Approvals.test.tsx:63`.
- Evidence/reproduction: implementation cleanup is visible, and tests prove core keyboard behavior, but no assertion checks restoration of prior `inert` values across repeated open/close cycles or the exact active element after a failed submit.
- Impact: small regression-detection gap for accessibility lifecycle behavior; no current runtime defect was found by source review.
- Recommendation: extend component tests with background inert restoration, repeated mount/unmount and failed-submit focus assertions.
- Owner acceptance blocker: No, but should be closed before release.

### INFO - Composite index remains evidence-gated

No index was added. Keep deferred until representative execution plans, reads/duration and write-cost evidence exist.

## Owner decision

H1, M1 and the direct authorization aspects of M3 are accepted. The `CancellationToken` fix is narrow and does not weaken business fingerprinting. M2 and L1 provide meaningful behavioral evidence, with the test gaps above tracked.

Because concurrent same-key behavior has not yet been exercised through the complete HTTP pipeline, this review does not use `CONDITIONS_CLOSED`. A narrow test-only remediation and independent re-review are recommended. Production remains `NO-GO`; this decision grants no migration, deployment, backup or scheduler authority.

## Next step

Create one test-only slice for F1 and F2, then rerun targeted HTTP/component tests and the safety harness. Approval Notifications and Aging/SLA may be planned only after those conditions are owner-reviewed; bulk approval remains out of scope.
