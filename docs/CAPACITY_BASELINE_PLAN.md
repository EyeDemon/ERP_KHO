# Isolated Capacity Baseline Plan

Status: `PREPARED FOR OWNER REVIEW / EXECUTION NOT AUTHORIZED / PRODUCTION NO-GO`

## Purpose

This package prepares a representative capacity baseline for ERP_KHO. It does not prove production capacity, recovery, or an SLA. Execution requires a separately approved isolated environment, synthetic data, fixed acceptance thresholds, and a recorded owner decision.

## Workload mapped to current APIs

The mix uses existing routes and authorization boundaries:

| Activity | Route family | Role | Mix |
| --- | --- | --- | ---: |
| Login/session establishment | `POST /api/Auth/login` | all synthetic users | 5% |
| Current inventory and transaction reads | `GET /api/InventoryStocks/current`, `GET /api/InventoryTransactions` | scoped roles | 35% |
| Product, warehouse and receipt reads | existing list/detail GET routes | scoped roles | 20% |
| Create import drafts | `POST /api/ImportReceipts` | Manager/Admin | 4% |
| Create export/transfer/stocktake drafts | existing document POST routes | Manager/Admin/WarehouseStaff where the controller permits | 11% |
| Approval queue/detail/history reads | `GET /api/approvals/*` | Manager/Admin checker | 15% |
| Approve/reject transitions | document approve routes and `POST /api/approvals/{type}/{id}/reject` | non-maker Manager/Admin | 10% |

Every mutation worker owns distinct synthetic documents and products/quantity budgets. A worker advances only a state created for that scenario and never reuses another worker's receipt. Shared documents are forbidden except in a separately labelled contention scenario that tests idempotency/concurrency deliberately. Each logical mutation has its own idempotency key; only intentional replay reuses a key and payload. Maker and checker identities remain distinct; transfer checkers have both source and destination warehouse access. Approve/reject traffic uses Draft documents, and later dispatch/receive/complete actions use only their required predecessor states. Refresh/logout and session revocation are tested functionally, not churned as load traffic.

## Proposed stages (not owner-approved)

Twenty registered users is a planning assumption, not twenty concurrent users.

| Stage | Virtual users | Warm-up | Measurement | Purpose |
| --- | ---: | ---: | ---: | --- |
| Smoke | 1 | 1 min | 3 min | target, auth, fixture and metric validation |
| Typical | 3 | 2 min | 10 min | normal small-team concurrency |
| Busy | 6 | 3 min | 15 min | overlapping reads, drafts and approvals |
| Peak candidate | 10 | 5 min | 20 min | bounded peak hypothesis |
| Contention (separate) | 2 | 1 min | 5 min | same-document idempotency/race checks only |

The default guard caps execution at 10 virtual users, 30 minutes measurement and 5 minutes warm-up. Increasing a limit requires a reviewed config change and new owner approval.

Synthetic planning dataset: 2 warehouses, 20 users across real roles, 2,000 products, up to 4,000 inventory rows (the complete product/warehouse matrix allowed by the unique `(ProductId, WarehouseId)` key), 10,000 historical inventory transactions, and 1,000 documents per workflow type. Seed quantities must reconcile, use decimal `(18,4)`, and leave headroom for all planned mutations. These values are proposals, not measured requirements or owner-approved execution settings.

## Proposed acceptance thresholds (not owner-approved)

- Read/API end-to-end duration p95 <= 500 ms and p99 <= 1,000 ms, measured per completed request during the measurement interval (warm-up excluded).
- Mutation/approval end-to-end duration p95 <= 1,000 ms and p99 <= 2,000 ms, measured per completed request during the measurement interval.
- Unexpected HTTP error rate <= 0.5% of all requests. Expected scenario outcomes (`403/404/409`) are excluded from that numerator only when pre-labelled, but their count, rate, route and reason remain separate report fields. An unexpected business rejection is an error.
- No HTTP 500, deadlock victim, duplicate ledger/reservation, negative OnHand, or Reserved greater than OnHand.
- Typical-stage sustained throughput >= 5 completed requests/second and busy-stage >= 10, averaged across each measurement interval with warm-up excluded.
- SQL CPU average <= 70% across the measurement interval; one-minute sampled peak <= 85%. Host paging rate and available memory are recorded; a numeric memory threshold awaits confirmed host sizing.
- Data/log volume, IOPS, storage latency and growth are recorded, not assigned a PASS threshold until the host/storage choice is known.
- Final reconciliation must prove: no negative OnHand/Reserved; Reserved <= OnHand; InventoryStock.Reserved equals active reservation remaining quantity per product/warehouse; every completed import/export/transfer/stocktake movement has the expected single ledger reference/type/quantity; rejected/cancelled drafts have no movement; document line totals use decimal `(18,4)`; and before/after aggregate deltas equal successful labelled commands.

Failure or a safety stop invalidates the run. Results cannot be repaired or thresholds changed after execution to claim PASS.

## Target and ownership contract

- API and SQL must be on the approved isolated environment identity, not merely a plausible URL/database name.
- API TLS identity, deployment commit, environment marker and health response must match the approved manifest.
- SQL must report the expected server identity and `DB_NAME()`, and contain an ownership marker with exact Run ID, database name, marker type and creation timestamp.
- Allowed database names are `ERP_KHO_Capacity_yyyyMMdd_HHmmss_<8 hex>`. `ERP_KHO`, `master`, `model`, `msdb`, `tempdb`, missing markers and mismatched Run IDs are denied.
- Secrets are supplied interactively or through an approved secret store. Tokens and connection strings are never command-line arguments or result fields.

## Safety stops

Stop without retry for target mismatch, production connectivity, ownership mismatch, missing approval, expired test window, unexpected real data, error rate above 2%, any inventory invariant failure, repeated HTTP 500, uncontrolled resource growth, secret exposure, or elapsed-time/load limit breach.

The runner must preserve redacted partial results and stop new work. Cleanup is a separate exact-resource operation after evidence acceptance. It verifies the same Run ID and marker and never deletes by prefix or wildcard.

## Prepared commands

Review-only validation (safe, no network or SQL):

```powershell
powershell -NoProfile -File tools/ERP.CapacityBaseline/Prepare-CapacityBaseline.ps1 `
  -ConfigPath tools/ERP.CapacityBaseline/capacity.sample.psd1 `
  -PrepareOnly

powershell -NoProfile -File tools/ERP.CapacityBaseline/Test-CapacityBaselineGuards.ps1
```

The checked-in sample intentionally contains placeholders, so the first command must fail closed until an owner-approved local copy supplies real non-secret target identities. A successful `-PrepareOnly` run means only that offline schema/limits are valid. It does not attest DNS/TLS, API deployment identity, SQL identity/ownership, isolation, credentials, owner approval or execution readiness.

No executable load command is supplied yet. After approval, the implementation slice should use a pinned k6 version for HTTP load and read-only SQL/host collectors, generate the scenario from the reviewed manifest, and add an execution flag that still revalidates live API and SQL identities. This avoids treating an uninstalled or drifting load tool as approved.

## Capacity gate boundary

A successful run at one scale supplies baseline evidence only. Gate 1 also requires measured daily data/log/AuditLog growth, representative peak volume, storage/network characteristics, backup and restore throughput when separately authorized, maintenance window, evidence source and named confirmer.

## Owner decision sheet

| Decision | Proposed value | Status |
| --- | --- | --- |
| Environment | Dedicated isolated VPS/VM; 4 vCPU, 16 GB RAM, 200 GB NVMe planning profile | OWNER TO CONFIRM; provider/region not selected |
| Maximum budget | Owner-defined hard cap including compute, storage and egress | REQUIRED / TBD |
| SQL edition/version | Match intended production candidate; current evidence is SQL Server 2019 Express, while earlier 2025 Standard was planning input | OWNER TO SELECT AND CONFIRM LICENSE |
| Workload/data | Stages and synthetic dataset above | PROPOSED / NOT APPROVED |
| Test window | One approved window; maximum active measurement 30 min per stage, no unattended run | OWNER TO SET DATE/TIME |
| Thresholds/stops | Proposed thresholds and safety stops above | PROPOSED / NOT APPROVED |
| Result confirmer | Project owner plus a named technical cross-checker where available | OWNER TO NAME |
| Cleanup | Exact resources for one Run ID after evidence acceptance; no wildcard deletion | OWNER TO APPROVE SEPARATELY |
