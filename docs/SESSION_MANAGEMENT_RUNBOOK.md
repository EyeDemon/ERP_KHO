# Session Management Runbook

## Security model

- Access tokens expire after 15 minutes and contain user ID, role, `jti`, issuer, audience and expiration.
- Refresh tokens use 64 random bytes from a cryptographic RNG and live for 14 days by default.
- Only the SHA-256 refresh-token hash is stored in `UserSessions`; the raw token exists only in the `HttpOnly` cookie.
- Every refresh rotates the token. Reuse or concurrent reuse revokes the token family.
- Warehouse IDs are not embedded in JWTs. Warehouse authorization remains a live database check.
- Logout revokes the refresh session. The current access token is not immediately revoked and remains valid until its short expiry.

## Cookie, CORS and CSRF

The cookie is `HttpOnly`, `Secure` outside Development/Testing, scoped to `/api/Auth`, and defaults to `SameSite=Strict`. Axios sends credentials only to the configured API base URL. CORS uses an explicit origin allowlist with credentials and never uses a wildcard.

For a future genuinely cross-site frontend, use `SameSite=None; Secure` and add an antiforgery token before enabling that topology. The current same-site deployment relies on SameSite plus the CORS allowlist.

## Migration

Apply locally or in an approved non-production environment:

```powershell
dotnet ef database update --project ERP.Infrastructure --startup-project ERP.Api
```

Rollback only the session migration:

```powershell
dotnet ef database update 20260824113017_AddAccountLockout --project ERP.Infrastructure --startup-project ERP.Api
```

Reapply after rollback with the first command. Never place a connection string or JWT secret in source control.

## Retention

`IUserSessionService.CleanupAsync` removes sessions expired or revoked before a caller-provided UTC cutoff. No background scheduler was introduced. Operations may call it from an approved maintenance host using `SessionSecurity:RetentionDays`; security audit rows are not removed.

## Deferred scope

2FA, SSO, Company/Branch, distributed rate limiting, immediate access-token revocation and background cleanup scheduling are intentionally not included.
