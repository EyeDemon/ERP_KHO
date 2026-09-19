# Inbound Receiving and Posting

## Sources reviewed

- Notion 01, Business Rules & State Machine Specification: Goods Receipt and command-processing sections.
- Notion 17, Permission Registry: `receipt.read/create/update/receive/complete/post/cancel` and warehouse scope.
- Notion 29, Inventory Ledger Posting Algorithm: atomic ledger and balance projection.
- Notion 34, Goods Receipt Posting & Inbound Execution: `DRAFT → RECEIVING → RECEIVED → QC_PENDING? → READY_TO_POST → POSTED`.
- Notion 35, Putaway Engine; 41, QC Hold & Quarantine; 228, Inbound UX flow; 229/282 UX governance.
- Screen Matrix rows `INB-RECEIVING-WORKBENCH` and `INB-RECEIPT-POST-CONFIRM`.
- Thirteen Inbound concept images previously reviewed directly from `Ảnh_Chức_Năng.zip`; these are illustrative references, not authoritative designs.

## Current implementation and this slice

Before this slice, Import Receipt used `Draft → Approved`; approval updated stock and wrote an Inventory Transaction. That made approval equivalent to posting.

This slice establishes the executable boundary:

```text
Draft --receive--> Received --checker approval--> ReadyToPost --post--> Posted
```

- `receive` validates persisted lines and records no inventory mutation.
- checker approval records the supplier snapshot and readiness decision; it records no inventory mutation.
- `post` alone updates stock and creates the receipt ledger transaction in the existing unit-of-work transaction.
- all commands retain idempotency middleware, warehouse authorization and optimistic status concurrency.
- historical `Approved` rows remain readable and are labelled as legacy data; they are not silently migrated.

`Cancelled` remains available only from `Draft`. The shared enum values used by Export Receipts and Stocktakes are unchanged; new Import states were appended.

## Deliberately deferred gaps

The codebase has no receipt-level expected/received/accepted/damaged/rejected quantities, UOM conversion version, lot/serial dimensions, QC inspection aggregate, location-level balance, outbox, or Putaway task aggregate. Implementing those safely requires schema and migration work beyond this foundation. The current `Quantity` is treated as the persisted received quantity for the all-lines slice. No-QC is the only executable path; the QC path remains documented but unavailable.

Product, unit and warehouse names use current master data. Only supplier Code/Name is snapshotted after Draft, so historical reconstruction is limited accordingly. Unit price remains confidential operational data and is not added to the new command responses.

## API and authorization

| Command | Endpoint | Required gate | Inventory effect |
|---|---|---|---|
| Complete receiving | `POST /api/importreceipts/{id}/receive` | authenticated Admin/Manager + warehouse access | none |
| Approve readiness | `POST /api/importreceipts/{id}/approve` | shared checker policy + warehouse access + maker/checker separation | none |
| Post receipt | `POST /api/importreceipts/{id}/post` | shared checker policy + warehouse access + maker/poster separation | stock and ledger exactly once |

The deployment still uses role-backed API policies. Mapping these to the Notion permission codes `receipt.receive`, `receipt.complete`, and `receipt.post` is the next authorization migration; the backend gates remain authoritative.

## Error, concurrency and idempotency rules

- Invalid state returns a business conflict and performs no inventory mutation.
- Repeated requests use `Idempotency-Key`; a concurrent transition also loses the status concurrency race.
- UI buttons enter a disabled/loading state while requests are pending and refresh persisted backend state afterward.
- A failed load or command does not invent a client-side state transition.

## Test matrix

- receive from Draft and reject other states;
- verify receive and approval do not change stock or ledger;
- post only from ReadyToPost;
- repeated/concurrent post creates one inventory effect;
- rollback leaves document, stock and ledger consistent;
- warehouse isolation and maker/checker policy;
- frontend loading, error and double-submit guards;
- SQL integration and browser full-stack path using an owned synthetic database.

## Next gaps

Add receipt-line quantity buckets and UOM conversion first, then QC/no-QC branching. Putaway should start from posted inventory in a receiving/QC location and move it internally; it must not be bundled into receipt posting.
