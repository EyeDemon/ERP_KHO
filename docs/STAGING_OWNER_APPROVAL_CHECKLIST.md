# ERP_KHO Solo Developer Staging Self-Review Checklist

Status: `SELF REVIEW PENDING / STAGING ONLY / PRODUCTION NO-GO`

The developer is the sole developer, system owner, release owner and
operations owner. Complete this checklist only for an isolated staging
environment. A checked item does not authorize production deployment or live
capacity execution.

## Environment

- [ ] Developer-controlled environment identified
- [ ] Self-review date and decision owner recorded
- [ ] Deployment decision reviewed by the developer
- [ ] Rollback responsibility accepted by the developer
- [ ] Developer acceptance verification planned

## Infrastructure

- [ ] Host or container platform reviewed
- [ ] CPU, RAM, and storage profile reviewed
- [ ] TLS edge and certificate ownership reviewed
- [ ] Network, ingress, egress, and database firewall rules reviewed
- [ ] Immutable deployment commit and image digests recorded

## Database

- [ ] Isolated staging database identity confirmed
- [ ] SQL Server edition and version confirmed
- [ ] Recovery point selected and recorded
- [ ] Migration plan and SQL hash reviewed
- [ ] Rollback and reconciliation plan reviewed

## Operations

- [ ] Monitoring plan and alert destination defined
- [ ] Log retention period defined
- [ ] Health endpoint visibility planned for staging
- [ ] Incident escalation path defined
- [ ] Resource and restart limits defined

## Business and UAT

- [ ] UAT window scheduled
- [ ] Test users and role assignments prepared
- [ ] Synthetic test data approved by the developer
- [ ] UAT matrix reviewed
- [ ] UAT exit evidence and exception process recorded

## Explicit boundaries

- [ ] Production remains `NO-GO`
- [ ] No real migration is authorized by this checklist
- [ ] No backup/restore is authorized by this checklist
- [ ] No live capacity execution is authorized by this checklist
- [ ] Destructive seed scripts are excluded from the operator workflow

## Self-review decision

- [ ] GO TO STAGING
- [ ] HOLD

Do not mark GO until all required staging checks are complete and the decision
is recorded in `SOLO_STAGING_GO_NO_GO.md`.
