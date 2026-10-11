# INV-11 — Ledger/Balance anchor provenance (read-only)

Canonical sources consulted **read-only**: Notion Spec 82, Inventory Snapshot, Reconciliation & Integrity Monitoring; Spec 29, Inventory Ledger Posting Algorithm & Balance Projection. Design: `design-system/erp-wms/MASTER.md`.

## Safety invariant

An investigation pins immutable Ledger events by scoped transaction ID and pages current buckets by selected-status stock ID. A second, independently pinned `allStatusBucketAnchorId` covers every status of the authorized warehouse/product, because a new QC/Quarantine bucket must invalidate the eight-status verdict even when the selected AVAILABLE page is unchanged. The Ledger anchor is scoped to the authorized warehouse/product across **all** inventory statuses; the bucket anchor belongs to the selected status. Every request rechecks warehouse access.

The existing `allStatusStatus`, `allStatusExpectedQuantity` and `allStatusDifference` fields remain **historical arithmetic / forensic evidence** for backwards compatibility, not a verified same-time snapshot. They must never be used as repair instructions.

New `evidenceAnchorsStale` is true when the query observes a newer Ledger event or selected-status bucket beyond either pinned high-water ID. In that case, the authoritative **advisory** `snapshotComparisonStatus` is `Indeterminate`, and `snapshotComparisonExpectedQuantity` and `snapshotComparisonDifference` are JSON null. The React work center displays this indeterminate verdict and a Vietnamese warning even if the legacy arithmetic says Match. Forged stale evidence claiming a completed comparison is rejected client-side. No Ledger/Balance writes are introduced.

With fresh anchors and classifiable history, the new comparison fields mirror the existing eight-status arithmetic. Even a fresh anchor does **not** establish an atomic SQL snapshot: current quantities/statuses can change between SQL statements, and an ID high-water mark does not freeze mutable rows. Therefore the verdict is investigation-only, never a certification for correction.

## Evidence and limitations

- InMemory tests: backdated new Ledger event, new bucket, stable historical keyset, fresh re-evaluation.
- SQL Server isolated integration: real authorized warehouse/product, backdated new event, stable pinned event IDs, null stale comparison, cleanup.
- API test: JSON provenance fields and null comparison values.
- Vitest: stale Ledger or bucket displays indeterminate status, rejects forged Match.
- CI and Preview must be evaluated against the branch **HEAD SHA**, not a synthetic test-merge SHA. Preview is Vite/frontend/demo only. Remote authenticated independent .NET/API/SQL staging UAT is still required before marking Blueprint live.

Notion remains unchanged. No projection rebuild, repair, auto-correction, PR merge or production deployment.

## Cross-status bucket insertion guard — 2026-10-11

- `allStatusBucketAnchorId` is optional on first read and returned by the API. Subsequent UI pages and status switches preserve it; the selected-status `bucketAnchorId` continues to protect its own keyset page.
- A forged future all-status anchor is rejected **after** warehouse authorization, using the exact warehouse/product scope. Other warehouses/products cannot poison this investigation.
- `allStatusBucketHasRowsAfterAnchor` detects a new stock bucket in **any** status and forces `evidenceAnchorsStale=true`, `snapshotComparisonStatus=Indeterminate`, null comparison totals. Historical arithmetic stays read-only.
- InMemory covers warehouse isolation/QC-only insert, SQL Server covers QC insertion without new Ledger/AVAILABLE bucket, API and Vitest cover field propagation and Vietnamese fail-closed UX.
- Identity anchors do **not** detect in-place bucket edits/deletes/status changes or create an atomic SQL snapshot. Remote authenticated .NET/API/SQL staging UAT and controlled remediation remain outstanding. Notion unchanged, no production promotion.
