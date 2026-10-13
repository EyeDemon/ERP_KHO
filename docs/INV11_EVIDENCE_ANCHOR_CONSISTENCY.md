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

## Status aggregate fingerprint across pinned evidence pages (2026-10-11)

The authorized per-status SQL GROUP BY (status ID, total Quantity,
ReservedQuantity and bucket count) receives a deterministic SHA-256 fingerprint.
The baseline is retained across keyset pages and status filters. A different
aggregate without new IDs makes `allStatusBucketAggregateChanged=true`,
`evidenceAnchorsStale=true`, `snapshotComparisonStatus=Indeterminate`, and
null snapshot expected/difference. An exact 64 lowercase hex value is accepted;
it is not an access token and warehouse authorization remains mandatory.

InMemory/SQL Server test in-place stock changes; API/Vitest verify propagation.
**Limitations:** mutually offsetting in-status edits and reverted changes can
escape an aggregate hash. Inter-statement races persist. This is NOT a row-level
or atomic Ledger/Balance snapshot; no repair permitted, staging still pending.
Notion and Production unchanged.

## Real SQL Server concurrent writer versus scoped reader (2026-10-11)

The detailed `GetInvestigationAsync` path now opens one SQL Server
`SERIALIZABLE` read transaction after input validation and before warehouse
access checks. A command interceptor verifies all reader commands use that
isolation level, with disposal verified after the response. The broad list is
unchanged (no global serializable transaction), and all stock updates remain
owned by the inventory posting engine.

A dedicated *two-connection* SQL Server regression pauses the authorized
detail reader **after the all-status stock aggregation, before the Ledger
GROUP BY**, then attempts a stock update on the same warehouse/product from
another DbContext. The update must remain blocked while the detail response
is assembled, then complete when the reader transaction is released. The
first response must retain the pre-update Balance quantities; a fresh second
request must see the updated quantities. This validates inter-statement
isolation on an actual SQL Server, not just a mocked transaction property.

**Operational boundary:** the test validates ordering and lock release for
one controlled concurrency case; it does not establish latency, throughput,
lock-escalation resistance, deadlock freedom, or transactionally immutable
cross-request evidence. A production-like, authenticated staging backend with
concurrent stock postings and realistic bucket volumes must still be measured
for lock waits, timeouts and deadlocks before this feature can be marked Live.
All evidence remains read-only. No business correction, schema change,
Notion edit, merge or production promotion is authorized.

## Scoped concurrent writers: unrelated product must stay writable (2026-10-13)

An additional real SQL Server assertion extends the two-connection pause scenario.
While the INV-11 detail reader holds the serializable stock/Ledger observation
for a single authorized `(warehouseId, productId)`, a separate writer updates
a **different product in the same warehouse** and must complete within the
bounded test cancellation window. An update of the *investigated* product still
must wait until the read transaction releases its locks. Both resulting physical
quantities are asserted using new DbContexts, and the test cleans up the two
isolated product fixtures.

This is a positive/negative lock-scope regression, **not** a throughput test or
guarantee that SQL Server never chooses broad plans or escalates locks under
production volumes. If it fails, investigate actual indexes/query plans and
locking before authorizing release; do not suppress the test or claim a safe
warehouse-wide posting SLA. Staging concurrency/load UAT remains required.
