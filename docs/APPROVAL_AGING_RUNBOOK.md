# Approval Aging Runbook

## Scope

Approval aging is calculated for `Draft` items in the shared Approval Queue and detail API. It does not change approval workflow, authorization, warehouse isolation, inventory, audit, or persisted document data.

## SLA Levels

- `Normal`: waiting time is from 0 up to, but not including, 24 hours.
- `Warning`: waiting time is from 24 up to, but not including, 48 hours. The UI displays `Sắp đến hạn`.
- `Overdue`: waiting time is 48 hours or more. The UI displays `Quá hạn`.

The backend uses the document's existing `CreatedAt` timestamp as the approval-request timestamp because all four current approval workflows are reviewed directly from `Draft`. Calculation uses the current UTC time at query execution. Aging is not stored in SQL Server.

Completed, rejected, and cancelled documents return no waiting duration or SLA status in approval detail and are not included in the pending queue.

## Configuration

Configure thresholds in `ERP.Api/appsettings.json` or override them through the standard environment configuration providers:

```json
{
  "ApprovalAging": {
    "WarningAfterHours": 24,
    "OverdueAfterHours": 48
  }
}
```

Environment variable equivalents are `ApprovalAging__WarningAfterHours` and `ApprovalAging__OverdueAfterHours`. Both values must be positive, and `OverdueAfterHours` must be greater than `WarningAfterHours`. Invalid configuration prevents API startup.

## API Contract

Approval queue/detail summary items add nullable `waitingMinutes` and `slaStatus` properties. Existing consumers can ignore these additive fields. Queue requests may use the optional `slaStatus` filter with `Normal`, `Warning`, or `Overdue`.

`requestedAtUtc` remains the authoritative request instant and is serialized with UTC meaning. The UI renders local time and exposes the UTC value in the timestamp tooltip.

## Operational Notes

- No migration is required.
- No notification, Email, Zalo, push, or bulk-approval behavior is introduced.
- Thresholds are operational configuration, not a demonstrated production SLA.
- Production remains NO-GO.
