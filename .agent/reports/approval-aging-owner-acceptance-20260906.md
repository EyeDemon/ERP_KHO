# Approval Aging Owner Acceptance - 2026-09-06

## Decision

`AGING OWNER ACCEPTED / PRODUCTION NO-GO`

The owner accepts the implemented and independently reviewed Approval in-app Indicators and Aging scope only. This acceptance does not approve production, close any independent release gate, authorize migration or deployment, or accept any other product slice.

## Accepted scope

- Approval queue waiting duration calculated at query time in UTC.
- Default operational thresholds: Warning at 24 hours and Overdue at 48 hours.
- Additive queue/detail API fields and server-side SLA filter.
- Textual and color-supported Normal, Warning, and Overdue indicators in the Approval UI.
- Configuration validation, controlled-clock tests, SQL isolation tests, and frontend behavior tests.

## Accepted limitations

- The 24/48-hour thresholds are operational indicators, not a production SLA demonstrated by workload or reliability evidence.
- `CreatedAt` is the waiting-time origin for the current workflow, where a newly created `Draft` immediately enters the approval queue. A future Submit/Resubmit workflow requires reassessment and likely a dedicated authoritative timestamp.
- Composite indexing remains deferred until representative workload and execution-plan evidence exist.

## Evidence references

- `.agent/reports/approval-in-app-indicators-aging-result-20260906.md`
- `.agent/reports/approval-aging-independent-review-20260906.md`
- `docs/APPROVAL_AGING_RUNBOOK.md`

The independent review recorded Application plus isolated SQL `303/303`, API `144/144`, Approval calculator `7/7`, and targeted Approval frontend `11/11` as PASS. These are prior review results and were not rerun while recording this acceptance.

No database, migration, backup, Scheduled Task, Email, Zalo, push notification, bulk approval, PR, merge, push, or deployment action is authorized by this document.
