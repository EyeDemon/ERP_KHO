# Export Workflow Forward-Only Rollback Runbook

## Purpose

This runbook covers rollback of the export dispatch workflow introduced by
`20260825104054_AddExportReceiptDispatchWorkflow`. It does not authorize a
production deployment or a destructive database downgrade.

## Recovery objectives

- RPO: **TBD - Product Owner approval required**.
- RTO: **TBD - Product Owner approval required**.

Decision options for Product Owner (not selected by engineering):

| Option | RPO target | RTO target | Operational requirement |
|---|---:|---:|---|
| A | Near-zero after the last committed dispatch | Shortest practical window | Transaction-log backups, tested point-in-time restore, and staffed DBA response |
| B | Last scheduled backup | One maintenance window | Verified full/differential backup and documented manual reconciliation |
| C | No numeric commitment for the first controlled release | Best effort | Keep NO-GO for production until measured restore drills establish achievable targets |

The release owner must record the selected option and concrete numeric targets
before changing the production verdict to GO.

## Compatibility matrix

| Application/schema combination | Status | Reason |
| --- | --- | --- |
| Current application + current schema | Supported | Handles `Approved`, `Dispatched`, reservations, and dispatch metadata. |
| Current application + pre-workflow schema | Unsupported | Current queries and writes expect dispatch columns and reservation semantics. |
| Baseline application + current schema | Schema-compatible, operational rollback NO-GO | The baseline contains the dispatch migration, enum, API, reservations, and dispatch workflow, but lacks the current kill switch and revoked-access-token enforcement. Redeploying it would remove controls added after the baseline. |
| Baseline application + downgraded schema | Unsupported | The baseline application expects the dispatch columns and workflow schema. The `51011` guard only determines whether the schema migration may run down; it does not make this application/schema pair compatible. |
| Separately identified pre-workflow application + downgraded schema | Conditional and unproven | Requires zero `Dispatched` receipts, a passing guarded downgrade in an isolated environment, and explicit compatibility evidence for that exact application artifact. |

The safe application rollback target is a separately built and tested compatibility
release that preserves the current schema and dispatch semantics while retaining the
kill switch and session-revocation enforcement. Do not redeploy the baseline binary
as a rollback shortcut: its schema support is established, but its operational and
security controls are older than the current reviewed application.

## Pre-deployment evidence

1. Record application commit, migration list, generated SQL hash, and database backup identifier.
2. Run the migration preflight read-only checks for negative/over-reserved stock,
   duplicate export transactions, exact Approved-receipt ledger mapping, and active reservations.
3. Confirm `ExportWorkflow:WriteEnabled=true` in the intended environment without logging secrets.
4. Run Release build/tests, SQL integration tests with zero skips, frontend gates,
   encoding gate, and pending-model check.
5. Record reconciliation totals by warehouse. Never modify `OnHand` or `Reserved` in a validation script.

## Deployment sequence

1. Take and validate a restorable database backup.
2. Announce the maintenance window and stop export writes with
   `ExportWorkflow:WriteEnabled=false` (or the environment equivalent), then restart/reload configuration according to the hosting model.
3. Verify approve/reserve, approve/dispatch, dispatch, and cancellation of an
   Approved receipt return HTTP 503 while inventory/report/reconciliation reads remain available.
4. Wait for in-flight export requests to finish and capture reconciliation again.
5. Apply the reviewed forward migration once.
6. Validate migration history, columns, foreign key, unique export-transaction index, and preflight invariants.
7. Deploy the compatible application release.
8. Smoke-test read paths, then re-enable export writes.
9. Smoke-test both dispatch modes with controlled data and reconcile stock,
   reservations, receipts, transactions, and audit events.

## GO criteria

- Backup restore validation is available.
- Preflight returns zero unsafe rows.
- Release and SQL integration gates pass with zero skipped tests.
- Kill switch blocks all inventory-affecting export transitions without mutation.
- Migration SQL matches the reviewed migration artifact.
- Post-migration reconciliation matches the captured baseline.
- A current-schema-compatible application rollback artifact is available.

## NO-GO criteria

- Any preflight row is unsafe or unexplained.
- Backup/restore evidence, RPO, or RTO approval is missing.
- Release runtime is blocked by WDAC/AppLocker/Code Integrity.
- Tests are skipped or any quality gate fails.
- Migration script differs from the reviewed artifact.
- Reconciliation changes outside the controlled smoke-test quantities.

## Forward-only rollback

1. Set `ExportWorkflow:WriteEnabled=false` and verify HTTP 503 on all protected writes.
2. Keep read/report/reconciliation endpoints available for diagnosis.
3. Roll back only to a current-schema-compatible application release.
4. Preserve all `Dispatched` receipts, reservations, transactions, and audit logs.
5. Diagnose and prepare a new reviewed corrective migration. Do not edit ledger
   rows, `OnHand`, or `Reserved` manually.
6. Apply the corrective migration only after backup, read-only preflight, review,
   and explicit deployment approval.
7. Reconcile before re-enabling writes.

## Schema downgrade boundary

`Down()` executes `THROW 51011` as its first operation when any receipt has status
`Dispatched`. This is a schema rollback guard, not a data rollback mechanism. A
downgrade is allowed only when the guard passes and independent evidence confirms
that no dispatch meaning would be lost. Never run `Down()` on the local QA database
to test this behavior; use an isolated rollback-test database.

## Evidence retention

Retain command, exit code, migration SQL/hash, preflight result, backup identifier,
application version, kill-switch verification, reconciliation snapshots, and
operator/timestamp. Logs must not contain passwords, access/refresh tokens, cookies,
connection strings, or signing keys.
