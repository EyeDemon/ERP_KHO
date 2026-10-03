# ERP_KHO Staging Execution Baseline

Status: `DESIGN READY / RUNTIME NOT PROVISIONED / PRODUCTION NO-GO`

## Objective

Prepare an isolated staging execution design for ERP_KHO so the solo developer
can later make a controlled GO/HOLD decision. This document does not authorize
deployment, migration, backup/restore, capacity execution or production use.

## Scope

- Backend API and frontend containers from the reviewed source.
- An isolated staging database with an exact target identity and Run ID.
- Runtime secrets injected outside Git.
- Health/readiness checks, logs, UAT and cleanup evidence.
- Approval, inventory, reservation, transfer, stocktake and aging workflows
  only within the existing application scope.

Excluded: production traffic, live capacity, notifications, bulk approval,
Scheduled Task changes and any use of the real `ERP_KHO` database.

## Candidate deployment commit

- Branch: `handoff/approval-complete-20260906`
- Current reference: `f72fbcc70c889cd66e7c2d11b9792bdbc4dc0f0c`
- Release commit: `NOT FROZEN / DECISION PENDING` because the working tree is
  currently dirty.

The commit must be frozen only after source, test, configuration and document
changes are reviewed together.

## Artifacts to create before execution

- Immutable backend and frontend image references with digests.
- Sanitized runtime configuration inventory.
- Isolated database target and ownership manifest.
- Reviewed migration SQL and SHA-256.
- Approved recovery point and rollback record.
- UAT evidence directory keyed by the execution Run ID.
- Health, log and reconciliation evidence.

## Image strategy

Use the existing multi-stage Dockerfiles. Build from the frozen commit, tag
locally with the commit SHA, record image digests, and promote only the exact
digest selected for the staging window. Do not use a mutable `latest` tag as
the release identity. A registry, retention policy and image signing decision
remain pending.

## Configuration strategy

Inject database, JWT, cookie, CORS and rate-limit settings at runtime from an
approved secret/configuration source. Keep placeholders and secret references
in documentation only; never commit secret values, connection strings or
tokens. Validate required settings before startup and record only redacted
configuration keys.

## Rollback boundary

Before database mutation, rollback is a container/image stop and replacement
decision. After a migration, do not assume an application image rollback is
safe. Use the approved forward-compatible data plan or an isolated recovery
point, then reconcile inventory, reservations, ledgers, approvals, audit and
idempotency. Never run an unreviewed migration downgrade after dispatched
export data.

## Current decision

`STAGING EXECUTION DESIGN READY / RUNTIME PROVISIONING PENDING / PRODUCTION NO-GO`
