# ERP_KHO Staging Environment Manifest

Status: `TEMPLATE / SOLO DEVELOPER SELF REVIEW REQUIRED / PRODUCTION NO-GO`

This manifest is a controlled-input template. It contains no credentials and
does not authorize provisioning, deployment, migration, backup or live
capacity execution.

## 1. Environment responsibility

| Responsibility | Required value | Status |
| --- | --- | --- |
| Developer/system owner | `Self` | SELF REVIEW PENDING |
| Release owner | `Self` | SELF REVIEW PENDING |
| Operations responsibility | `Self` | SELF REVIEW PENDING |
| Rollback responsibility | `Self` | SELF REVIEW PENDING |
| UAT acceptance verifier | `Self` | SELF REVIEW PENDING |
| Incident escalation | `NOT CONFIGURED / DECISION PENDING` | SELF REVIEW PENDING |

## 2. Infrastructure definition

| Field | Approved value | Status |
| --- | --- | --- |
| Target | `NOT PROVISIONED / DECISION PENDING` | SELF REVIEW PENDING |
| CPU/RAM/storage | `NOT SELECTED / DECISION PENDING` | SELF REVIEW PENDING |
| Network boundary | `NOT CONFIGURED / DECISION PENDING` | SELF REVIEW PENDING |
| Domain/subdomain | `NOT CONFIGURED / DECISION PENDING` | SELF REVIEW PENDING |
| TLS termination | `NOT CONFIGURED / DECISION PENDING` | SELF REVIEW PENDING |
| Deployment commit | `NOT FROZEN / DECISION PENDING` | SELF REVIEW PENDING |
| Container image digests | `NOT BUILT / DECISION PENDING` | SELF REVIEW PENDING |

The repository currently provides Docker build files and a local compose shape,
not a provisioned staging environment or a production ingress definition.

## 3. Database definition

| Field | Approved value | Status |
| --- | --- | --- |
| SQL Server edition/version | `NOT SELECTED / DECISION PENDING` | SELF REVIEW PENDING |
| Server identity | `NOT PROVISIONED / DECISION PENDING` | SELF REVIEW PENDING |
| Database name | `NOT PROVISIONED / DECISION PENDING` | SELF REVIEW PENDING |
| Connection strategy | `NOT CONFIGURED / DECISION PENDING` | SELF REVIEW PENDING |
| Migration responsibility | `Self` | SELF REVIEW PENDING |
| Recovery point | `NOT SELECTED / DECISION PENDING` | SELF REVIEW PENDING |

The database name must not be `ERP_KHO` during staging preparation unless a
separately identified staging target is explicitly named and isolated.

## 4. Secret management

- Secret source: `NOT SELECTED / DECISION PENDING`.
- Rotation responsibility: `Self`.
- JWT, database, TLS and operator credentials are injected at runtime only.
- No secret value, token, cookie, connection string or private key belongs in
  this repository or in test artifacts.
- Rotation and recovery procedure must be tested in staging before acceptance.

## 5. Cleanup policy

| Resource | Owner marker | Expiration | Cleanup authority |
| --- | --- | --- | --- |
| Staging compute/container | `NOT PROVISIONED / DECISION PENDING` | `NOT SET` | `Self` |
| Staging database | `NOT PROVISIONED / DECISION PENDING` | `NOT SET` | `Self` |
| Logs/artifacts | `NOT CONFIGURED / DECISION PENDING` | `NOT SET` | `Self` |

Cleanup must use exact resource IDs and a second ownership verification. No
wildcard or prefix deletion is allowed.

## Self-review record

- Self-review completed: `PENDING`.
- Migration decision: `PENDING`.
- Recovery point decision: `PENDING`.
- Staging deployment window: `PENDING`.
- Live capacity execution: `NOT AUTHORIZED`.
- Production: `NO-GO`.
