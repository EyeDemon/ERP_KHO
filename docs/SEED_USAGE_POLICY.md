# ERP_KHO Seed Usage Policy

Status: `STAGING SAFETY POLICY / SOLO DEVELOPER SELF REVIEW REQUIRED / PRODUCTION NO-GO`

Governance note: this is a solo developer project. The developer owns the
seed decision and must record the self-review, but sole ownership does not
weaken the destructive-operation boundaries below.

## Prohibited staging operator inputs

- `scripts/seed.sql` is not a staging operator tool.
- `scripts/seed2.sql` is not a staging operator tool.
- Do not run either script against `ERP_KHO`.
- Do not execute direct `DELETE` statements against `ERP_KHO` to prepare test
  data.

These scripts contain destructive statements and are excluded from the staging
operator workflow. Keeping them in the repository does not authorize their
execution.

## Local QA seed boundary

The guarded QA seed path is for local Development and LocalDB only. It must
validate the environment and target before any write, and it must fail closed
outside that boundary.

## Staging test data

All staging test data must be synthetic or explicitly approved by the owner.
The staging database must be isolated, have a recorded Run ID and ownership
marker, and be verified immediately before any approved data operation.
Cleanup must use exact owned resource identifiers; wildcard or prefix-based
deletion is prohibited.

This policy does not authorize migration, backup/restore, live capacity
execution, production traffic, or Scheduled Task changes.
