# INV-10 — Stable Ledger History Paging (Draft PR #31, 2026-10-09)

## Contract
- `GET /api/inventory/traceability` returns `eventAnchorId`: highest matching, authorized ledger transaction ID at the beginning of a new query; `0` represents an empty ledger.
- The UI forwards that same ID on ledger and bucket page navigation and drops it only on a **new** search/filter change.
- Requests with `eventOffset > 0` require `eventAnchorId`. Negative anchors are rejected before querying warehouse permissions or SQL; zero is a valid empty anchor.
- Authorized event timeline, reversal markers and related ledger rows are limited to `InventoryTransaction.Id <= eventAnchorId`. A newer event with an older `TransactionDate` cannot shift the current history window.
- **Current stock remains live**. Reference-to-stock matching uses all authorized direct document events, not only the anchored timeline window; it still intersects product, warehouse, lot and serial filters.
- All data remains restricted by `IWarehouseAuthorizationService`; there is no user-supplied warehouse expansion, ledger mutation or schema migration.

## Verification required
- Application input tests: missing/negative anchor is rejected before scoped DB access.
- SQL Server integration: original page 1/page 2 stable after inserting two transactions (one backdated); fresh search sees newly committed transactions.
- API contract and UI Vitest: anchor forwarded and preserved for page navigation; absent anchor refuses unsafe later pages; new searches reset anchor.
- CI and Vercel Preview do not substitute for real browser QA and SQL/API staging signoff. Keep PR Draft.
