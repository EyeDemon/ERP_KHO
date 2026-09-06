# Approval In-App Indicators And Aging Result

Date: 2026-09-06

## Scope

Implemented query-time approval aging for the existing Approval Queue and detail summary. No workflow, authorization, warehouse scope, inventory, audit, notification, or database schema behavior was changed.

## Contract

- Existing `CreatedAt` is the approval request timestamp for all four current Draft approval workflows.
- DTO additions: nullable `WaitingMinutes` and `SlaStatus`.
- Queue query addition: optional `SlaStatus` filter.
- Values: `Normal`, `Warning`, and `Overdue`.
- Default UTC thresholds: 24 hours for Warning and 48 hours for Overdue.
- Closed/non-Draft detail items return null aging fields.
- Configuration section: `ApprovalAging.WarningAfterHours` and `ApprovalAging.OverdueAfterHours`.
- Aging is calculated from `TimeProvider` at query time and is not persisted.

## Frontend

- Added textual and colored SLA badges: `Bình thường`, `Sắp đến hạn`, and `Quá hạn`.
- Added elapsed waiting-time display.
- Added the server-side SLA filter.
- The request timestamp retains local display and exposes the UTC wire value in a tooltip.

## Verification

- Targeted aging calculator: 7/7 PASS, 0 skipped.
- Targeted Approval component: 11/11 PASS.
- Application plus isolated SQL integration: 303/303 PASS, 0 skipped.
- API integration: 144/144 PASS, 0 skipped.
- Full frontend: 26/26 PASS across 7 files.
- Backend Release build: PASS, 0 warnings, 0 errors.
- Frontend lint/build: PASS.
- EF pending-model check: PASS, no pending model changes.
- Encoding: PASS.
- NuGet vulnerable package scan: PASS, no vulnerable packages reported.
- npm audit: PASS, 0 vulnerabilities.
- `git diff --check`: PASS with line-ending warnings only.

The isolated database `ERP_KHO_Integration_20260906_020123_8a49d57f` was created with the owned harness and cleaned successfully. Database state, business tables, backup history/files, and remaining integration database inventory matched before and after. A Windows-owned `RefreshCache` Scheduled Task changed its definition during the snapshot window; no ERP task was changed by this implementation.

## Database And Release Safety

- Migration required: No.
- Migration applied to `ERP_KHO`: No.
- Production deployment: No.
- Email, Zalo, push notification, and bulk approval: Not implemented.
- Production remains NO-GO.

## Remaining Risks

- Thresholds are configurable operational indicators, not a demonstrated production SLA.
- Aging uses `CreatedAt` because the current workflow enters approval directly from Draft. A future Submitted state would require an explicit submitted timestamp decision.
- Composite index decisions remain pending representative execution-plan evidence.
