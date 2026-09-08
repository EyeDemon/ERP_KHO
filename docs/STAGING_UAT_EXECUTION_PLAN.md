# ERP_KHO Staging UAT Execution Plan

Status: `PLAN READY / UAT NOT EXECUTED / PRODUCTION NO-GO`

## Preconditions

- Self GO decision recorded for the exact isolated staging target.
- Frozen commit, image digests and runtime configuration manifest recorded.
- Database preflight, recovery point and migration decision complete.
- Synthetic users, roles, warehouses, products and documents prepared through
  an approved path.
- Evidence directory and Run ID established.

## Test order

1. Health, login, refresh rotation, logout and session revocation.
2. Warehouse read isolation and transfer source/destination authorization.
3. Import create, approve and reject.
4. Export create, reservation, approve, dispatch and cancel.
5. Transfer create, approve, dispatch, receive and completion.
6. Stocktake create, approve and reservation conflict behavior.
7. Reservation reserve, release, expiry handling and reconciliation.
8. Approval queue, detail, history, aging, maker-checker and idempotency.
9. Inventory, reservation, ledger, audit and correlation reconciliation.

Do not use `scripts/seed.sql` or `scripts/seed2.sql`. Data must be synthetic,
owned by the Run ID and sufficient for the matrix without sharing documents
between workers except intentional conflict cases.

## Evidence collection

Record UTC timestamp, actor, role, warehouse scope, document code, expected and
actual result, HTTP status, correlation ID and redacted logs. Capture before
and after inventory/reservation/ledger snapshots for mutation scenarios.

## Failure handling

Stop new writes on an unexpected 5xx, invariant mismatch, cross-warehouse
disclosure, duplicate transition or audit/idempotency mismatch. Preserve
evidence, do not retry blindly, and classify the result as failed or blocked.
Do not repair staging data manually before the discrepancy is understood.

## Exit criteria

- All applicable matrix rows PASS or have a recorded developer exception.
- No P0 finding or unresolved inventory/reservation/ledger mismatch.
- Authentication, maker-checker, warehouse isolation, audit and idempotency
  evidence reconcile.
- Health, logs, cleanup and rollback evidence are retained without secrets.
- The solo developer records the final staging decision.

UAT completion does not authorize production.
