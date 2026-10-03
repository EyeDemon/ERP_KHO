# ERP_KHO Staging Security Runtime Plan

Status: `DESIGN ONLY / RUNTIME VERIFICATION PENDING / PRODUCTION NO-GO`

## JWT and sessions

- Inject the JWT signing secret from an approved runtime secret source.
- Use the reviewed issuer, audience and lifetime settings for the staging
  environment; do not reuse a production secret.
- Record a rotation owner as `Self`, a rotation window and a revocation test.
- Verify refresh-token rotation, hash-only persistence and reuse-family
  revocation with synthetic users.

## Cookie and edge

- Serve authentication over HTTPS through the selected staging edge.
- Verify `Secure`, `HttpOnly` and the selected `SameSite` behavior.
- Verify forwarded headers, origin allowlist and CORS against the exact staging
  hostname.
- Do not weaken cookie or CORS settings to bypass a staging edge problem.

## Network and database access

- Restrict ingress to the selected staging edge and approved operator path.
- Restrict database access to the backend runtime and controlled maintenance
  path.
- Verify the database identity and denylist immediately before any migration.
- Do not expose SQL credentials or connection strings in logs or shell history.

## Logging and sensitive data

- Retain application and access logs for the selected staging window only.
- Mask tokens, cookies, passwords, connection strings, idempotency keys and
  private data before evidence capture.
- Preserve correlation IDs, status codes and safe error codes for UAT issues.
- Define log storage, retention and cleanup before runtime start.

## Security decision

Source and CI security evidence is available, but secret-store, HTTPS edge,
network and runtime logging are not verified. `PENDING RUNTIME VERIFICATION`.
