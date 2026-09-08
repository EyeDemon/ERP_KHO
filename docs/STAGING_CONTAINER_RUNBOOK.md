# ERP_KHO Staging Container Runbook

Status: `DESIGN ONLY / RUNTIME NOT VERIFIED / PRODUCTION NO-GO`

This runbook describes a future controlled staging window. It does not authorize
image build, container start or deployment in the current step.

## Build and tag design

From the frozen repository commit, the planned command shape is:

```powershell
docker compose build
docker image inspect erp-kho-api:<commit-sha>
docker image inspect erp-kho-web:<commit-sha>
```

The exact implementation should tag images with the immutable commit SHA and
record their digests. Do not use `latest` as the evidence identity. Docker CLI
was unavailable during preparation, so these commands were not run.

## Runtime configuration

The compose shape expects runtime injection for:

- `ConnectionStrings__DefaultConnection`
- `JwtSettings__Secret`
- `Cors__AllowedOrigins__0`
- refresh-cookie and session security settings
- authentication and API rate limits

Values must come from the approved runtime secret/configuration source. Do not
place them in `.env` committed to Git, command history or logs.

## Health and startup order

1. Start the backend container with the isolated database configuration.
2. Wait for `/health/live` and then `/health/ready` to be healthy.
3. Start or release the frontend only after backend readiness.
4. Verify the frontend healthcheck and approved TLS edge.
5. Preserve redacted logs and correlation IDs.

The backend Docker healthcheck uses `/health/live`; Compose dependency uses the
backend `/health/ready` state. Health only proves process/database reachability,
not business or capacity readiness.

## Stop and cleanup boundary

Stop only the exact staging project identified by Run ID. Cleanup requires a
second exact ownership check and separate self decision. Do not remove volumes,
images or databases by wildcard or broad prefix.

## Current decision

`HOLD - IMAGE BUILD, HOST, SECRETS, TLS AND RUNTIME HEALTH NOT VERIFIED`
