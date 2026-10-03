# ERP_KHO Staging GO Decision Checklist

Status: `DECISION PREPARATION / GO NOT RECORDED / PRODUCTION NO-GO`

This checklist is for the solo developer's future staging decision. Checking
an item does not authorize execution by itself.

## A. Code freeze

- [ ] Review all source, test, configuration and staging-document changes.
- [ ] Confirm the final commit SHA and branch.
- [ ] Confirm working tree is clean.
- [ ] Link CI evidence to the exact frozen commit.
- [ ] Confirm no untracked release artifact or secret is included.

## B. Infrastructure

- [ ] Provider/host option selected.
- [ ] Region and budget recorded.
- [ ] CPU, RAM and storage profile recorded.
- [ ] Network boundary and database firewall rules recorded.
- [ ] TLS termination and staging hostname recorded.

## C. Database

- [ ] SQL Server edition/version selected.
- [ ] Exact isolated database name recorded.
- [ ] `DB_NAME()` and server identity verification prepared.
- [ ] Recovery point selected and verified.
- [ ] Migration artifact and SHA-256 reviewed.
- [ ] Rollback and reconciliation boundary recorded.

## D. Security

- [ ] Runtime secret source selected without storing secret values in Git.
- [ ] JWT rotation and revocation procedure recorded.
- [ ] HTTPS, Secure, HttpOnly and SameSite behavior verified at the edge.
- [ ] CORS allowlist matches the exact staging origin.
- [ ] Logs mask tokens, cookies, passwords, connection strings and raw keys.

## E. Operations

- [ ] Monitoring and alert destination selected.
- [ ] Log retention and cleanup window recorded.
- [ ] Health/readiness observation procedure recorded.
- [ ] CPU, memory, disk, restart and stop thresholds recorded.
- [ ] Exact resource IDs and Run ID cleanup procedure recorded.

## F. UAT

- [ ] Unique UAT Run ID created for the approved window.
- [ ] Synthetic users, roles, warehouses, products and documents prepared.
- [ ] Evidence folder keyed by the Run ID created outside source control.
- [ ] UAT matrix and failure-stop procedure reviewed.
- [ ] Inventory, reservation, ledger, audit and idempotency reconciliation
  steps prepared.

## Decision rule

The solo developer may record GO only after all required items are evidenced.
Until then:

`STAGING HOLD / RUNTIME EXECUTION NOT AUTHORIZED / PRODUCTION NO-GO`
