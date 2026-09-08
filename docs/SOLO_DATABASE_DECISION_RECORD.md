# ERP_KHO Solo Database Decision Record

Status: `HOLD / DATABASE NOT PROVISIONED / PRODUCTION NO-GO`

## Responsibility

Database responsibility owner: `Self`.

## Decision summary

| Item | Current status | Evidence or reason |
| --- | --- | --- |
| Database target | HOLD | No isolated staging database is provisioned or identified. |
| SQL Server version | HOLD | Edition and version are not selected for staging. |
| Migration status | HOLD | Migration source exists; no staging migration was executed or approved. |
| Recovery point | HOLD | No staging recovery point is selected or verified. |
| Rollback strategy | HOLD | Forward-compatible/data reconciliation guidance exists, but no target-specific rollback decision is recorded. |

## Required before any database operation

- Record an exact isolated server/database identity and verify it immediately
  before access.
- Select the SQL Server edition/version and record compatibility assumptions.
- Freeze the deployment commit and review generated migration SQL/hash.
- Select and verify a recovery point without using `ERP_KHO`.
- Record the forward-compatible rollback and reconciliation decision.
- Keep the current local database and recovery state unchanged.

## Decision

`HOLD`

No migration, SQL write, backup, restore, recovery-model change or seed
operation is authorized by this record.
