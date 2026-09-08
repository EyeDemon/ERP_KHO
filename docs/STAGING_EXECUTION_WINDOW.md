# ERP_KHO Staging Execution Window

Status: `PENDING / NOT SCHEDULED / PRODUCTION NO-GO`

Window: `PENDING`

No execution window is open. The sequence below is a design template only.

## Before window

- Record the final frozen commit and verify a clean working tree.
- Preserve approved local state and record redacted configuration inventory.
- Verify runtime secret availability without printing values.
- Verify exact staging host, server, database, Run ID and ownership marker.
- Verify recovery point, migration artifact hash and rollback decision.
- Confirm stop conditions, evidence directory and cleanup scope.

## During window

1. Build or retrieve only the images matching the frozen commit digests.
2. Start the isolated runtime with injected configuration.
3. Verify `/health/live`, `/health/ready`, TLS and frontend health.
4. Apply migration only if the database GO decision is recorded.
5. Recheck schema and inventory/reconciliation invariants.
6. Begin UAT with synthetic data and the recorded Run ID.

Stop on target mismatch, readiness failure, unexpected 5xx, security
disclosure, duplicate mutation, audit/idempotency mismatch or invariant
failure. Preserve evidence and do not retry blindly.

## After window

- Run the approved smoke test and record UTC timestamps/correlation IDs.
- Start UAT only after health and reconciliation checks pass.
- Record UAT results and any exception.
- Decide whether to stop, continue, rollback or preserve the isolated target.
- Perform cleanup only for exact owned resources after the window.

## Current boundary

`RUNTIME NOT STARTED / EXECUTION NOT AUTHORIZED / PRODUCTION NO-GO`
