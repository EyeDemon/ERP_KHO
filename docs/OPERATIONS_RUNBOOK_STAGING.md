# ERP_KHO Staging Operations Runbook

Status: `DESIGN / STAGING WINDOW AND SOLO DEVELOPER REVIEW PENDING / PRODUCTION NO-GO`

This runbook is for an isolated staging deployment only. It does not authorize
migration, backup, restore, live capacity execution or production traffic.

## 1. Start service

1. Confirm the approved manifest, commit, image digests and staging window.
2. Confirm runtime secrets are present in the approved secret store without
   printing them.
3. Confirm the database target and recovery point using the database preflight.
4. Start the reviewed compose/deployment unit:

```powershell
docker compose up -d --build
```

5. Wait for backend readiness and frontend health before UAT.

## 2. Stop service

```powershell
docker compose stop
```

Use `docker compose down` only for the exact staging project after the owner
approves cleanup. Do not delete volumes as part of routine stop.

## 3. Check health

```powershell
Invoke-WebRequest https://<approved-staging-host>/health/live
Invoke-WebRequest https://<approved-staging-host>/health/ready
docker compose ps
```

Expected results:

- `/health/live`: HTTP 200 with a minimal status body.
- `/health/ready`: HTTP 200 only when required database connectivity is ready;
  otherwise HTTP 503 with no connection details.
- Container health state: `healthy`.

Do not expose health response details beyond status. Health does not prove
business readiness or production capacity.

## 4. Check logs

```powershell
docker compose logs --since 10m --no-color erp-backend
docker compose logs --since 10m --no-color erp-frontend
```

Check correlation IDs, startup failures, database errors, 5xx responses,
rate-limit responses and repeated readiness failures. Never copy tokens,
cookies, connection strings or secret values into the incident record.

## 5. Troubleshooting

| Symptom | Safe first action |
| --- | --- |
| Readiness 503 | Confirm the exact staging database identity and connectivity; do not change the connection string blindly. |
| Liveness 503 | Check container process and restart evidence; preserve logs before restart. |
| Frontend unavailable | Check frontend container health and ingress/TLS routing. |
| Repeated 5xx | Capture correlation ID, stop UAT mutations if data integrity is uncertain, escalate. |
| 409 concurrency/business conflict | Verify state and audit; do not retry blindly or edit inventory directly. |
| Disk/CPU/RAM pressure | Stop new test load, preserve metrics and review with the developer/operations responsibility. |

## 6. Rollback

1. Stop new UAT writes and record the incident/change ID.
2. Preserve logs, health output, migration output and correlation IDs.
3. Decide between forward-compatible application rollback and the approved
   database recovery plan.
4. Never run an unreviewed migration downgrade after dispatched export data.
5. Restore only to the exact isolated target and recovery point approved for
   the window.
6. Reconcile inventory, reservations, ledgers, approvals and audit before any
   restart.

## 7. Incident escalation

Escalate according to the developer-controlled responsibilities in
`STAGING_ENVIRONMENT_MANIFEST.md`:

- Developer/system owner: application and authorization behavior.
- Developer/operations responsibility: host, container, TLS, logs and monitoring.
- Developer/database and rollback responsibility: migration, recovery point and reconciliation.
- Developer/release responsibility: window, stop/go and cleanup decisions.

## Monitoring checklist

- [ ] Application logs and correlation IDs.
- [ ] HTTP error rate and 4xx/5xx split.
- [ ] Approval/aging query latency.
- [ ] Database readiness and connection failures.
- [ ] Disk/data/log usage.
- [ ] CPU and memory.
- [ ] Container restart and health state.
- [ ] Inventory/reservation reconciliation result.
- [ ] Backup/recovery status if separately authorized.

Monitoring implementation and alert routing remain owner/environment work; this
document does not claim they are active.
