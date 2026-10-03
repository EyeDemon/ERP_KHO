# Approval Final Owner Re-review - 2026-09-06

## Executive summary

Status: `APPROVAL_FINAL_OWNER_RE_REVIEW_PASSED / F1_F2_CLOSED / READY_FOR_OWNER_ACCEPTANCE / NO-GO PRODUCTION`

This independent, review-only pass verified the test-only remediation directly from source and repeated execution. F1 passed three targeted concurrent HTTP runs without flakiness. F2's lifecycle and failed-submit focus tests passed. Full backend, isolated SQL, API and frontend regressions passed, and the real database/backup/task safety snapshot remained unchanged.

No CRITICAL, HIGH, MEDIUM or LOW finding remains for F1/F2. This is technical acceptance only; production remains NO-GO.

## Scope and diff reviewed

Read and cross-checked:

- `.agent/reports/approval-release-conditions-owner-review-20260906.md`
- `.agent/reports/approval-final-test-gaps-result-20260906.md`
- `ERP.Api.Tests/ApprovalHttpIntegrationTests.cs`
- `frontend/src/components/AccessibleDialog.test.tsx`
- `frontend/src/pages/Approvals.test.tsx`

Production source was consulted for pipeline parity but not edited. The remediation is limited to the three test files above plus its report. Existing dirty working-tree changes were preserved.

HEAD before/after: `6c20b48505da6e9db141f84e6fc0f212363e344f`.

Target test SHA-256 values were unchanged before/after review:

- `ApprovalHttpIntegrationTests.cs`: `7AD7E5481233455192D61BE8D158232A2FF2B81AC5897EEC3093A43AD7ED6725`
- `AccessibleDialog.test.tsx`: `7A21B48CD4E6B54F6E457BF57B194241AA1CCE86C3CADE3D70C61E2786708985`
- `Approvals.test.tsx`: `CC8CC2524CBBFC4EA3C21BC89A5FD30EAD86F2E2FC53699EAAC54671A5AD80AF`

## F1 closure - concurrent HTTP idempotency

The reviewed test sends two actual POST requests to `/api/stock-transfers/{id}/approve` using the same authenticated Manager checker, document ID and idempotency key.

The requests traverse `WebApplicationFactory`, test authentication, checker authorization, routing/model binding, `IdempotentCommandFilter`, controller, DI, `StockTransferService`, EF Core and an owned SQL Server database.

The test-only `SaveChangesInterceptor` is passed only to the test factory and added only to that factory's DbContext options. It observes added `IdempotencyRecord` entries for `StockTransfer.Approve`; it does not replace SQL, uniqueness, the filter, service or response.

### Overlap evidence

- Both requests are started before either is awaited to completion.
- Two separate arrivals are required at the idempotency `SaveChanges` boundary.
- `Arrivals == 2` is asserted before release.
- Both HTTP tasks are asserted incomplete before release.
- There is no delay-based concurrency claim.
- Both arrival/release waits have a finite 15-second timeout. Failure cannot wait indefinitely; owned-database disposal retains marker/name/run guards.

### Assertions verified

- Both responses: HTTP 204; HTTP 500 count 0.
- One persisted `Draft -> Approved` transition.
- Exactly one `StockTransfer.Approved` audit.
- Exactly one completed idempotency record for actor/scope/key.
- Stock, reservation and inventory-ledger snapshot unchanged.
- Third replay after completion: HTTP 204 with the same body.
- Same key with another document: HTTP 409; other document remains Draft.
- Raw key/hash absent from response; stored fingerprint is non-empty and does not contain the raw key.

One transition/audit is persistence evidence rather than a synthetic production execution counter. Together with the previously accepted SQL/filter concurrency test, which counts one business delegate execution, the evidence is sufficient without adding production instrumentation.

### Three targeted runs

| Run | Result | Duration | Cleanup guard |
| --- | --- | --- | --- |
| 1 | 1/1 PASS, 0 skipped | 9 s | Integration database inventory returned to 0 |
| 2 | 1/1 PASS, 0 skipped | 9 s | Integration database inventory returned to 0 |
| 3 | 1/1 PASS, 0 skipped | 9 s | Integration database inventory returned to 0 |

Each run generated its database name/Run ID internally through `OwnedDatabasePolicy`; no arbitrary database name was accepted. The command validated local Integrated Security and `ERP_KHO` as the read-only source before each run, refused any pre-existing integration database, and verified zero remaining databases after each run.

F1 decision: CLOSED.

## F2 closure - dialog lifecycle and failure focus

The component tests mount the real dialog/page in jsdom and mock only the HTTP boundary.

Verified behavior:

- Initially active/unset and initially inert siblings both become inert while open.
- The portal is not made inert.
- Unmount restores each original inert property, including preserving jsdom `undefined` rather than coercing it to false.
- A sibling originally inert remains inert.
- A non-inert sibling is not left inert.
- Three open/close cycles each establish initial focus, invoke one Escape close, restore trigger focus, remove the portal and release background inertness.
- Busy Escape is suppressed.
- Busy unmount restores inertness and removes the key listener; a later Escape causes no callback.
- On a controlled HTTP 409, the Reject dialog remains open, correlation error is visible, and focus remains inside the dialog rather than body/background trigger.
- The user can edit the reason and retry.
- Busy state prevents duplicate submission; retry preserves the logical idempotency headers.

Targeted Approval/Dialog result: 14/14 PASS, 0 skipped.

F2 decision: CLOSED.

## Quality gates

| Gate | Actual result |
| --- | --- |
| Concurrent HTTP target, repeated | 3 x 1/1 PASS, 0 skipped |
| Approval/Dialog targeted | 14/14 PASS, 0 skipped |
| Application + isolated SQL integration | 295/295 PASS, 0 skipped |
| API suite | 144/144 PASS, 0 skipped |
| Legacy business SQL regression | PASS within isolated Application suite |
| Operational backup test | DEFERRED / NOT EXECUTED / not counted as PASS |
| Full frontend suite | 25/25 PASS across 7 files |
| Release build | PASS, 0 warnings, 0 errors |
| Frontend lint | PASS |
| Frontend production build | PASS |
| EF pending-model check | PASS, no pending model changes |
| Encoding gate | PASS |
| NuGet vulnerability scan | PASS, none reported |
| npm audit | PASS, 0 vulnerabilities |
| git diff --check | PASS; existing line-ending notices only |
| Changed-test secret scan | PASS, 0 hits |

Targeted tests are subsets and are not added to full-suite totals.

## Safety snapshot

The full safety harness used:

- Database: `ERP_KHO_Integration_20260905_171817_d298ed1b`
- Run ID: `d298ed1b821b4106a5770faf528b1e03`
- Prefix, denylist, ownership marker, local Integrated Security and `DB_NAME()` guards: PASS
- Exact owned cleanup: PASS
- Remaining integration databases: 0

Before/after comparison:

- `ERP_KHO`: `ONLINE/SIMPLE` both times.
- Business-table counts/hashes: unchanged.
- Backup history: unchanged.
- Backup-file inventory: unchanged.
- Scheduled Task definitions/states: unchanged.
- `ERP_KHO-SQLBackup-Log-10Min-Pilot`: Disabled.
- Source/test hashes: unchanged during review.

No migration, production database mutation, backup/restore, recovery change, task mutation, commit, push, merge, PR, deployment, email or Zalo action occurred.

## Findings and improvements

Blocking findings: none.

F1 and F2 are closed. Additional repetition or instrumentation would be optional improvement, not a new acceptance blocker without a demonstrated defect.

INFO: composite-index work remains deferred until representative execution-plan, reads/duration and write-cost evidence exists. Migration, release/rollback and DR/backup gates remain independent.

## Technical decision and next step

The Approval Queue, History & Reject remediation conditions are technically complete and ready for owner acceptance. No identical review loop is recommended unless code changes or new evidence appears.

After owner acceptance, the next product slice may be proposed separately. Approval Notifications and Aging/SLA must not start automatically, and bulk approval remains out of scope. Production remains `NO-GO`.
