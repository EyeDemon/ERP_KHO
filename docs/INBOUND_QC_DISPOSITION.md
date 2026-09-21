
# Inbound QC Disposition

The canonical Goods Receipt flow performs line-level QC before Post. Receive, QC disposition and readiness approval do not write inventory. Post atomically writes persisted Base UOM quantities to AVAILABLE, DAMAGED and retained REJECTED balances and ledger rows.

Mixed receipts are allowed. A line snapshots the effective Product–Supplier policy first, otherwise Product policy, at receipt creation. A receipt stays `QcPending` until every required line has a valid disposition; then it becomes `QcCompleted` and a different checker may approve it to `ReadyToPost`.

The invariant is `Accepted + Damaged + Rejected = Received` in operation UOM and persisted Base UOM. Damaged and rejected quantities never increase Available. Rejected-at-door is deferred and creates no inventory in this slice.

Commands:

- `POST /api/importreceipts/{id}/receive`: Draft to Received or QcPending; no stock effect.
- `POST /api/importreceipts/{id}/qc-disposition`: completes one or more pending QC lines; no stock effect.
- `POST /api/importreceipts/{id}/approve`: Received/QcCompleted to ReadyToPost; no stock effect.
- `POST /api/importreceipts/{id}/post`: writes status balances and ledger once, then Posted.

Invalid/stale transitions return 409. All mutations retain warehouse scope, idempotency and optimistic concurrency. Current gates are role-backed; Notion permission codes are target governance and permission-code migration is deferred.

Migration `20260920163928_AddInboundQcDisposition` backfills legacy balances/ledger as AVAILABLE and does not infer historical QC policies or alter legacy receipt states.

Verification as of 2026-09-21: Release build, EF model check, 281 non-SQL Application tests, 52 frontend tests, lint and production build pass. Full owned-SQL and browser QA remain blocked because `ERP_KHO_SQLSERVER_ADMIN_CONNECTION` is not configured.
