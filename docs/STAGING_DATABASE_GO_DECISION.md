# ERP_KHO Staging Database GO Decision

Status: `HOLD / DATABASE NOT STARTED / PRODUCTION NO-GO`

Decision: `HOLD`

Database responsibility owner: `Self`.

## Decision items

| Item | Current value | Status |
| --- | --- | --- |
| SQL target | Not selected; isolated target required | HOLD |
| Database name | Not provisioned; use exact Run ID name when approved | HOLD |
| Migration approval | Not approved or executed | HOLD |
| Recovery point | Not selected or verified | HOLD |
| Rollback | Forward-compatible/data reconciliation design exists; target-specific decision pending | HOLD |

## Required evidence before database GO

- Exact SQL Server and database identity verified read-only.
- Target is isolated and not `ERP_KHO` or a system database.
- SQL edition/version and compatibility recorded.
- Migration list, generated SQL and SHA-256 reviewed from the frozen commit.
- Recovery point checksum/verification and RPO/RTO recorded.
- Rollback and reconciliation decision recorded.

No SQL command, `dotnet ef database update`, database creation, backup or
restore was run for this decision template.
