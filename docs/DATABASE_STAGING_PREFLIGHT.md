# ERP_KHO Database Staging Preflight

Status: `CHECKLIST ONLY / NO MIGRATION EXECUTED / PRODUCTION NO-GO`

This document is a controlled preflight plan. Commands below are examples for
an approved staging window; they are not authorization to run against
`ERP_KHO` or any production database.

## Target safety

- Confirm the exact server and database identity immediately before access.
- Confirm the target is an isolated staging database and is not `ERP_KHO`,
  `master`, `model`, `msdb` or `tempdb`.
- Record the deployment Run ID and ownership marker.
- Verify `DB_NAME()` and server identity through the approved read-only path.
- Stop on target mismatch; never fall back to a local or production database.

## Migration inventory and checksum

The source migration inventory is under `ERP.Infrastructure/Migrations/` and
must be frozen from the deployment commit. Before execution, record:

| Item | Required evidence | Status |
| --- | --- | --- |
| Current schema version | `__EFMigrationsHistory` read-only result | PENDING |
| Pending migration IDs | Reviewed list from the frozen commit | PENDING |
| Generated SQL | Reviewed SQL artifact | PENDING |
| SQL SHA-256 | Hash of the exact artifact | PENDING |
| Application commit | Immutable deployment SHA | PENDING |
| Operator | Named account | PENDING |

Read-only preparation command shape:

```powershell
dotnet ef migrations list --project ERP.Infrastructure --startup-project ERP.Api
dotnet ef migrations script --project ERP.Infrastructure --startup-project ERP.Api --output .staging-artifacts\erp-kho-migrations.sql
Get-FileHash .staging-artifacts\erp-kho-migrations.sql -Algorithm SHA256
```

Do not put a connection string or secret in command-line history or the
artifact. The migration itself is a separately approved deployment step.

## Backup and recovery prerequisite

- [ ] Verified full backup or approved staging recovery point exists.
- [ ] Backup checksum and restore verification recorded.
- [ ] Recovery owner and operator confirmed.
- [ ] RPO/RTO target recorded as an owner decision.
- [ ] Storage, retention and encryption policy recorded.
- [ ] Restore target is isolated and collision-checked.

The current repository evidence says backup automation is paused and the local
database is `SIMPLE`; the solo developer recovery decision is therefore still
pending and this checklist remains incomplete.

## Rollback decision tree

1. If preflight fails: stop, preserve evidence, do not mutate the database.
2. If migration fails before commit: stop and inspect the migration error; do
   not run an ad-hoc repair script.
3. If migration succeeds but post-check fails: stop writes, preserve logs and
   use the approved forward-compatible application rollback/data plan.
4. Do not run `Down()` after export rows reach `Dispatched`; the migration
   explicitly blocks that path and requires a data plan.
5. Restore a verified recovery point only through approved change/incident
   control.

## Post-migration reconciliation

- [ ] Migration history matches the recorded list.
- [ ] Required columns, constraints, foreign keys and indexes exist.
- [ ] Inventory invariants hold: no negative stock and no over-reservation.
- [ ] Reservation and inventory ledgers reconcile.
- [ ] Export/transfer/stocktake references reconcile.
- [ ] Approval, audit and idempotency tables are readable.
- [ ] Application health readiness is healthy.
- [ ] No unexpected seed or business data was introduced.

## Seed safety

`scripts/seed.sql` and `scripts/seed2.sql` are not staging operator tools. They
contain `USE ERP_KHO` and destructive `DELETE` statements. Do not run, copy or
include them in a staging deployment procedure. Use only the guarded QA seed
path, which requires Development and a LocalDB connection, for local QA.
