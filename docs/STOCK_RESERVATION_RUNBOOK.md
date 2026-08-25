# Stock Reservation Runbook

## Quantity model

- `OnHandQuantity = InventoryStocks.Quantity`
- `ReservedQuantity = InventoryStocks.ReservedQuantity`
- `AvailableQuantity = OnHandQuantity - ReservedQuantity`
- `StockReservations` is the reservation ledger and reconciliation source. The aggregate counter is updated atomically in the same transaction as every ledger transition.

## Lifecycle

- Manual reservations start as `Active` and use the configured UTC expiry (`StockReservation:DefaultExpiryMinutes`, default 120).
- Export receipts reserve during approval, then consume that reservation in the same database transaction. Draft receipts do not reserve indefinitely.
- Consume decreases both on-hand and reserved quantities. Only this physical movement creates an `InventoryTransaction`.
- Release, cancellation, and expiry decrease reserved quantity only and create an `AuditLog`.
- Expiry cleanup is idempotent through `POST /api/stock-reservations/expire`. Production still needs a scheduled caller; no scheduler is included in this release.

## Consistency and concurrency

- Reserve uses a conditional update requiring `Quantity - ReservedQuantity >= requested`.
- Unreserved export and Stock Transfer dispatch use the same available-stock condition.
- Consume and release use conditional updates against the current reserved counter.
- Multi-line export approval runs in one transaction and processes products in stable product-ID order.
- SQL Server check constraints prevent negative reserved/available quantities and invalid ledger allocations.

## Stocktake policy

Stocktake changes on-hand quantity but never silently changes reservations. Approval returns HTTP 409 when an actual quantity is lower than the reserved quantity for a product. Resolve or release the affected reservation first.

## Warehouse isolation and roles

Reservation list/detail queries apply warehouse scope in SQL before paging. Out-of-scope IDs return 404. Viewer can read; WarehouseStaff, Manager, and Admin can create/release/expire; reconciliation is Manager/Admin only. Creator identity always comes from JWT.

## API

- `GET /api/stock-reservations`
- `GET /api/stock-reservations/{id}`
- `POST /api/stock-reservations`
- `POST /api/stock-reservations/{id}/release`
- `POST /api/stock-reservations/expire`
- `GET /api/stock-reservations/reconciliation`

Reconciliation is read-only. It reports ledger/counter mismatch, negative available quantity, expired reservations still active, active rows without remaining quantity, and invalid Export Receipt references. It never repairs production data.

## Operational checks

1. Run reconciliation and resolve every finding before deployment.
2. Configure the production scheduler to call expiry cleanup at a controlled interval.
3. Monitor 409 responses for overbooking and concurrent transitions.
4. Never edit `ReservedQuantity` or reservation ledger rows directly.
