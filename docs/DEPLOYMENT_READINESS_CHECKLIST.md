# ERP_KHO Deployment Readiness Checklist

Status: `STAGING PREPARATION / SOLO DEVELOPER SELF REVIEW REQUIRED / PRODUCTION NO-GO`

## Build and provenance

- [ ] Immutable commit and artifact manifest recorded.
- [ ] Backend and frontend image digests recorded.
- [ ] Configuration keys reviewed without secret values.
- [ ] Release branch and migration list frozen.
- [ ] CI run linked to the exact commit.

## Container and network

- [x] Backend Dockerfile builds a release image and runs as non-root.
- [x] Frontend Dockerfile builds a release image and serves SPA fallback.
- [x] Backend liveness/readiness endpoints are implemented without secret detail.
- [x] Backend and frontend container healthchecks are defined.
- [ ] CPU, memory, disk and restart limits approved.
- [ ] TLS termination and forwarded-header policy verified.
- [ ] Ingress, egress and database firewall rules verified.
- [ ] Staging hostname and CORS allowlist verified.

## Configuration and secrets

- [ ] Secret store and rotation owner approved.
- [ ] Database connection is injected at runtime and targets isolated staging.
- [ ] JWT secret is injected at runtime and is not a placeholder.
- [ ] Cookie, SameSite and HTTPS behavior verified through the staging edge.
- [ ] Approval Aging thresholds recorded as operational indicators, not SLA.

## Database and recovery

- [ ] Database preflight completed from `DATABASE_STAGING_PREFLIGHT.md`.
- [ ] Verified recovery point exists before migration.
- [ ] Migration SQL/hash reviewed and recorded.
- [ ] Rollback/data decision tree approved.
- [ ] Post-migration reconciliation plan assigned.
- [ ] Destructive `scripts/seed.sql` and `scripts/seed2.sql` are excluded from operator workflow.

## Operational self-review

- [ ] Health checks are visible to the approved monitor.
- [ ] Logs are centralized or retained for the approved window.
- [ ] Error-rate and latency observations have an owner.
- [ ] Database, disk, CPU and memory checks have an owner.
- [ ] Incident escalation and rollback contacts are confirmed.
- [ ] Staging UAT matrix is signed by the named confirmer.

No checked item authorizes production deployment, live capacity execution,
backup automation or Scheduled Task creation.
