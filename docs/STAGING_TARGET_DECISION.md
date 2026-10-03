# ERP_KHO Staging Target Decision

Status: `RECOMMENDATION RECORDED / NOT PROVISIONED / PRODUCTION NO-GO`

No provider, host, budget or paid resource has been selected. The comparison
below is a design recommendation, not provisioning authorization.

| Option | CPU/RAM/Disk | Network/TLS | Backup | Cost | Complexity | Assessment |
| --- | --- | --- | --- | --- | --- | --- |
| A. Local Docker Compose | Uses developer workstation; capacity not representative | Local ports; no approved remote edge | Local-only and not an operational recovery plan | Lowest | Lowest | Good for offline rehearsal, not remote staging evidence |
| B. Single VPS | Planning profile: 4 vCPU, 16 GB RAM, 200 GB NVMe; owner must confirm | Public/private network and TLS reverse proxy must be designed | Requires an approved isolated DB recovery plan and storage | Low to medium, provider-dependent | Medium | Smallest practical remote staging shape for a solo project |
| C. Private VM | Owner-selected CPU/RAM/disk; stronger isolation possible | Private network, controlled ingress and TLS edge | Fits enterprise recovery controls but needs platform integration | Medium to high, provider-dependent | Highest | Stronger isolation, more setup than current project needs |

## Recommended option

`OPTION B - SINGLE VPS`, subject to explicit decisions on provider, region,
budget, network boundary, SQL target, TLS, recovery point and cleanup. It is a
planning recommendation for one remote isolated staging environment, not a
request to provision or spend money.

Option A remains the only non-paid rehearsal path. Option C is preferred only
if a private network or compliance requirement makes a VPS insufficient.

## Decisions still pending

- Provider/region and maximum budget.
- Exact CPU/RAM/storage and disk encryption.
- Public versus private ingress and TLS termination.
- SQL Server edition/version and placement.
- Recovery point, retention and cleanup window.
- Monitoring/log destination and resource stop conditions.
