# ERP_KHO Staging Database Execution Plan

Status: `DESIGN ONLY / DATABASE NOT PROVISIONED / MIGRATION NOT EXECUTED / PRODUCTION NO-GO`

## Target recommendation

Use an isolated SQL Server target selected for staging, with an explicit
edition/version recorded before execution. Do not connect staging tooling to
`ERP_KHO`, `master`, `model`, `msdb` or `tempdb`. SQL Server edition, server,
database and connection source are currently `NOT SELECTED / DECISION PENDING`.

## Database naming

Use an exact name such as:

`ERP_KHO_Staging_<UTCDate>_<RunId>`

The Run ID must be generated for the execution, recorded in an ownership
marker, verified with `DB_NAME()` and matched before every mutation. Never use
prefix-only discovery or cleanup.

## Migration flow

1. Freeze the application commit and record its SHA.
2. List migrations from the frozen source.
3. Generate reviewed SQL and SHA-256 into a controlled artifact directory.
4. Verify server identity, database identity, ownership marker and Run ID
   through a read-only preflight.
5. Confirm recovery point and rollback decision.
6. Apply only the reviewed migration artifact in the approved window.
7. Run post-migration schema and inventory reconciliation checks.
8. Preserve migration output, correlation IDs and redacted evidence.

The execution command is intentionally not run in this design step. In
particular, `dotnet ef database update` is not authorized here.

## Preflight checklist

- [ ] Exact isolated SQL Server and database verified.
- [ ] Target is not any denylisted database.
- [ ] SQL Server edition/version and compatibility recorded.
- [ ] Current migration history captured read-only.
- [ ] Pending migration IDs and generated SQL reviewed.
- [ ] SQL artifact hash recorded.
- [ ] Recovery point checksum/verification recorded.
- [ ] RPO/RTO decision recorded for staging.
- [ ] Rollback and reconciliation owner is `Self` and decision recorded.
- [ ] Connection secret injected at runtime, never in command history or Git.

## Recovery and rollback

If preflight fails, stop without mutation. If migration fails, preserve the
error and do not run ad-hoc repair SQL. If migration succeeds but reconciliation
fails, stop writes and use the approved forward-compatible application/data
plan or restore the exact isolated recovery point. Do not run `Down()` after
dispatched export data without a reviewed data plan.

## Current decision

`HOLD - TARGET, SQL VERSION, RECOVERY POINT AND MIGRATION WINDOW PENDING`
