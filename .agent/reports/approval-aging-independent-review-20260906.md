# Approval Aging Independent Review - 2026-09-06

## Executive summary

Independent source, diff, SQL-isolation, backend, and frontend review found no blocking defect in Approval in-app Indicators and Aging. No production source or test was changed by this review. The implementation is suitable for owner acceptance while production remains NO-GO.

## Scope and baseline

- Branch: `handoff/approval-complete-20260906`
- HEAD before/after: `886ad10b58ecd3caf7ac1656c902164a6e505386`
- Reviewed the Aging diff, `docs/APPROVAL_AGING_RUNBOOK.md`, and the prior implementation report without treating that report as test evidence.
- No migration, index, notification, Email, Zalo, push, or bulk-approval work was introduced.

## Source review

- All four creation paths create documents directly in `Draft` and assign `CreatedAt = DateTime.UtcNow`: ImportReceipt, ExportReceipt, StockTransfer, and Stocktake.
- The queue includes only `Draft` documents. Closed detail responses return `waitingMinutes = null` and `slaStatus = null`.
- `GetQueueAsync` captures one `nowUtc`; the same value drives cutoff filtering and item mapping for that query.
- SLA classification compares the exact elapsed `TimeSpan` before waiting minutes are floored.
- Warehouse authorization is applied to each source query before union/materialization. StockTransfer still requires both source and destination warehouse scope.
- SLA filtering occurs before `CountAsync`, stable ordering, `Skip`, and `Take`; total count therefore represents the authorized filtered result.
- Queue execution remains two server-side SQL operations (count and page). No per-row query or N+1 path was introduced.
- Invalid threshold order/values fail during API startup and inside the calculator. SQL `datetime2` values materialized as `Unspecified` are explicitly interpreted as UTC, matching the four verified UTC write paths. Future timestamps are clamped to zero waiting time and classified Normal.
- DTO fields are nullable and additive, preserving existing clients. Existing authorization, workflow, maker-checker, queue/detail contracts, and warehouse isolation remain unchanged.
- Frontend handles null as `Không áp dụng`, resets page 1 when the SLA filter changes, sends the server-side filter, renders textual and colored badges, and exposes the original UTC instant in the timestamp tooltip.

## Findings

### INFO I1 - CreatedAt is document age, not a distinct submission event

`CreatedAt` is currently a valid queue-entry proxy because all four workflows enter the approval queue immediately in `Draft`. The model has no separate Submit/PendingApproval transition. If draft editing, submission, or resubmission is added, Aging must move to an authoritative submitted/requested timestamp; otherwise drafting time would be counted. This is a documented business limitation and does not block acceptance under the current workflow.

### INFO I2 - Index decision remains evidence-driven

No index was added. Current filtering/count/pagination stays server-side and no N+1 was found. Composite-index work remains deferred until representative data, execution plans, logical reads, duration, and write-cost evidence are available.

No CRITICAL, HIGH, MEDIUM, or LOW findings remain.

## Test evidence

| Gate | Result |
| --- | --- |
| Approval Aging calculator | PASS - 7/7, 0 skipped |
| Approval page targeted component tests | PASS - 11/11 |
| Application plus isolated SQL integration | PASS - 303/303, 0 skipped |
| API suite using isolated SQL harness | PASS - 144/144, 0 skipped |
| Backend Release build | PASS - 0 warnings, 0 errors |
| Frontend lint | PASS |
| Frontend production build | PASS |
| `git diff --check` | PASS; informational LF-to-CRLF working-tree warnings only |
| Changed-content secret scan | PASS after review of 6 lexical matches; all were framework `CancellationToken` parameters or a test-only configuration key, with no credential value in the diff |

The SQL harness generated `ERP_KHO_Integration_20260906_021225_120c7584`, verified its ownership/run guard, and cleaned that exact database after execution.

## Safety evidence

The harness before/after comparison passed for database state, all application-table count/content hashes, backup history, backup files, Scheduled Task definitions/states, and integration-database inventory. `ERP_KHO` was not mutated. No migration, backup, restore, recovery-model change, task change, commit, push, PR, merge, or deployment occurred.

## Decision

`AGING READY FOR OWNER ACCEPTANCE / PRODUCTION NO-GO`

Suggested handoff commit contents are the Aging source, configuration, tests, runbook, implementation report, and this review report. Suggested message: `feat: add approval aging indicators`.
