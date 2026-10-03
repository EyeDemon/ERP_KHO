# Export Workflow Production Readiness Audit

Date: 2026-08-26  
Scope: flexible export receipt workflow only  
Verdict: **NOT READY**

## Release blockers

### Critical

1. **No Git baseline exists.** Branch `main` has no commits, `git ls-files` returns `0`, and every project path is untracked. A trustworthy release diff, provenance check, rollback commit, and exact pre-existing-vs-new file list cannot be produced. Create and review an initial baseline commit before any production release.

### High

1. **Semantic data rollback is intentionally unavailable after a receipt reaches `Dispatched`.** `Down()` throws `51011` before dropping workflow columns whenever status `3` exists. This preserves dispatch evidence, but production rollback requires an approved data plan. Code rollback should remain forward-compatible with the new schema; schema rollback is GO only when no dispatched rows exist.

### Medium

1. Execution plans were not certified against production-scale statistics. Required indexes exist and local read-only preflight passes, but the local database is not evidence for production cardinality or lock duration. Capture actual/estimated plans and preflight duration on a restored, sanitized production-sized copy before GO.
2. Dispatch and cancel do not automatically retry deadlocks. They fail atomically and a client retry is idempotent through status concurrency, reservation state, and the unique Export transaction index. Operational retry policy and alerting should be documented before GO.
3. An unrelated untracked nested repository exists at `D:/ERP_KHO/~/gstack`. It was not created or modified by this audit and was deliberately not deleted without ownership evidence. Remove or relocate it after owner confirmation so it cannot contaminate packaging or source scans.

## Git and changed files

Git cannot identify a diff because the repository has no commit and no tracked files. Files changed during this audit are:

- `ERP.Infrastructure/Migrations/20260825104054_AddExportReceiptDispatchWorkflow.cs`
- `docs/AddExportReceiptDispatchWorkflow.sql`
- `ERP.Application/Services/ExportReceiptService.cs`
- `ERP.Infrastructure/Repositories/UnitOfWork.cs`
- `ERP.Application.Tests/ExportReceiptDispatchMigrationContractTests.cs`
- `ERP.Application.Tests/SqlServerExportDispatchReadinessTests.cs`
- `ERP.Application.Tests/SqlServerStockReservationTests.cs`
- `ERP.Application.Tests/UnitOfWorkDeadlockTests.cs`
- `docs/EXPORT_WORKFLOW_PRODUCTION_READINESS_AUDIT.md`

This is an execution ledger, not a substitute for a Git diff.

## Migration and preflight

The generated forward SQL matches the C# migration contract for all safety predicates, DDL, backfill, index, FK, and migration-history operations. Automated checks confirm preflight appears before the first `ALTER TABLE`.

`Up()` now performs a read-only preflight before any schema or data mutation and throws `51010` when any condition is unsafe:

- negative OnHand, negative Reserved, or Reserved greater than OnHand;
- duplicate Export transactions by receipt/product/warehouse;
- an old Approved receipt without details or with non-positive detail quantity;
- anything other than exactly one matching Export transaction per detail, including warehouse and quantity;
- transaction count different from detail count;
- an Active or PartiallyConsumed reservation for an old Approved receipt.

Local `ERP_KHO` preflight result: PASS. No checking or remediation script modified OnHand.

Backfill changes old status `Approved` to `Dispatched`, sets mode `DispatchOnApproval`, and copies approved actor/time to dispatched actor/time. A filtered unique index prevents a second physical Export transaction for the same receipt/product/warehouse.

## Up and Down safety

- **Up:** transactional, fail-fast preflight precedes DDL; local rollback/reapply succeeded.
- **Down schema-only:** allowed only while no status `Dispatched` exists.
- **Down semantic data rollback:** not automatic and not safe to infer. The guard preserves evidence instead of converting physically dispatched receipts back to Approved.
- **Required rollback strategy:** roll back application code only if it remains compatible with nullable workflow columns. Do not run migration Down after dispatch activity without an approved reconciliation and data-retention plan.

## Concurrency and idempotency

Verified with SQL Server integration tests:

- competing reservations cannot overbook;
- concurrent approve-and-dispatch creates one physical transaction per winning receipt;
- same receipt concurrent approval has one winner;
- repeated dispatch has one winner and one Export transaction;
- dispatch versus cancel has one terminal winner;
- consumed/released reservation state reconciles with InventoryStock;
- timeout/retry is state-idempotent: a committed first request causes retry to conflict without repeating stock mutation; an uncommitted request rolls back atomically;
- SQL deadlock `1205` at commit is translated to `DeadlockException`, allowing approve to retry the whole transaction. No retry path executes after a successful commit.

## Authorization

- Controller role metadata restricts mutations; service role checks independently enforce approve mode and dispatch permissions.
- Warehouse scope is enforced in repository queries and service transitions. Out-of-scope access raises `NotFoundException` and maps to HTTP 404.
- Service replaces method `userId` with authenticated `ICurrentUser.UserId`.
- Dispatch mode is selected by the endpoint/configuration, not trusted from frontend payload.
- Warehouse supplied on create is checked against server-side warehouse authorization.

## Observability

Audit rows include receipt ID (`EntityId`), actor (`UserId`), timestamp, warehouse ID, dispatch mode, and old/new status for approve, dispatch, and cancel. Logs do not include JWTs, passwords, connection strings, or signing secrets. API middleware records handled workflow failures and full unhandled exceptions. Migration tooling must capture preflight `THROW` number/message and deployment correlation ID in the deployment log.

## Index and query review

- Reservation source lookup: unique filtered index on `(SourceType, SourceId, ProductId)`.
- Physical export idempotency/backfill: unique filtered index `IX_InventoryTransactions_ExportReceiptReference` on `(ReferenceType, ReferenceId, TransactionType, ProductId, WarehouseId)` for `ExportReceipt`.
- Stock row: unique index on `(ProductId, WarehouseId)` plus reservation check constraint.
- Dispatch/cancel receipt lookup: primary key seek; details use the existing `ExportReceiptId` relationship index; reservation source index supports consume/release.
- Remaining action: capture plans and timings on production-sized sanitized data. A local empty/small database plan is not release evidence.

## Quality gates

- `dotnet restore ERP.slnx`: PASS.
- Release build with warnings as errors: PASS, 0 warnings, 0 errors.
- `dotnet test ERP.slnx --no-restore --configuration Release`: PASS, **342/342**, 0 skipped.
- SQL integration: enabled against database `ERP_KHO`; 0 skipped.
- `npm test`: PASS, 2/2.
- `npm audit --audit-level=high`: PASS, 0 vulnerabilities.
- `npm run lint`: PASS.
- `npm run build`: PASS.
- encoding gate: PASS.
- `dotnet ef migrations has-pending-model-changes`: no pending model changes.
- migration rollback/reapply on local `ERP_KHO`: PASS.

## Deployment runbook

### 1. Backup

1. Confirm target database name is exactly `ERP_KHO`; reject `sa` and forbidden database names.
2. Take a full checksum backup and run restore verification.
3. Record backup path, checksum, database recovery model, migration history, application version, and operator.

### 2. Read-only preflight

1. Run the preflight block at the start of `docs/AddExportReceiptDispatchWorkflow.sql` under a read-only deployment identity where possible.
2. Record counts and duration without printing sensitive configuration.
3. NO-GO on any `51010`, negative/over-reserved stock, duplicate Export transaction, unsafe Approved receipt, or active conflicting reservation.
4. Capture estimated plans for reservation source lookup, Export transaction lookup, legacy backfill, dispatch, and cancel on a production-sized sanitized restore.

### 3. Maintenance window

1. Stop new export approvals, dispatches, cancellations, reservation expiry jobs, and import/export integrations.
2. Wait for in-flight transactions to finish and verify no deployment blockers remain.
3. Keep read-only access available if operationally required.

### 4. Apply migration

1. Apply the reviewed idempotent SQL artifact.
2. Capture command output, duration, SQL error number, and deployment correlation ID.
3. On failure, stop; do not run an automatic stock repair script.

### 5. Post-migration validation

1. Confirm migration history row, three nullable columns, FK, and unique Export transaction index.
2. Re-run all preflight invariants and reservation reconciliation.
3. Confirm legacy Approved rows were converted only when their ledger was exact.

### 6. Smoke tests

1. Mode A: Draft -> Approved; verify OnHand unchanged, Reserved increases, no Export transaction. Then Dispatch; verify Reserved returns, OnHand decreases once, one Export transaction.
2. Mode B: Draft -> Dispatched; verify reservation is consumed, OnHand decreases once, one Export transaction.
3. Cancel one Draft and one Approved receipt; verify no physical transaction and reservation release for Approved.
4. Repeat dispatch request; expect conflict and no additional mutation.
5. Verify out-of-scope warehouse returns 404 and unauthorized roles cannot mutate.

### 7. GO / NO-GO

GO requires: reviewed Git baseline/release diff, verified backup, zero preflight findings, approved production-sized plans, migration success, reconciliation clean, both smoke modes pass, audit rows complete, and no duplicate transaction.

NO-GO on: missing baseline or backup, any unsafe preflight count, timeout/deadlock without clean retry evidence, reconciliation mismatch, authorization leak, duplicate transaction, failed quality gate, or absent approved rollback plan.

### 8. Rollback

- Before any Dispatched row: stop traffic, verify status `3` count is zero, run Down, validate schema/history, then deploy prior compatible code.
- After any Dispatched row: do **not** run Down. Keep schema, roll back only to forward-compatible code, stop write traffic, reconcile ledger/reservations, and obtain an approved data plan. Restore backup only through incident/change control with an explicit recovery-point impact assessment.

## Production deployment confirmation

No production deployment was performed during this audit.
