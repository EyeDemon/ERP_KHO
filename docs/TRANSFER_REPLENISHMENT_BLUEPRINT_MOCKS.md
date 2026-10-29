# Transfer & Replenishment Blueprint Domain Mocks

Status: **FRONTEND BLUEPRINT ENHANCEMENT**

## Specialized planned panels

- **TR-02 — In-Transit Inventory**
  - source → transit → destination conservation;
  - transfer reference and owner;
  - explicit mismatch/reconciliation guard.

- **TR-05 — Replenishment**
  - pick-face min/max demand;
  - reserve source eligibility;
  - destination capacity;
  - suggested quantity and internal move task semantics.

## Existing surfaces intentionally not replaced

- **TR-01 — Warehouse Transfer** — live route and Screen Matrix content.
- **TR-03 — Transfer Dispatch** — foundation.
- **TR-04 — Transfer Receive** — foundation.

## Inventory semantics

- Transfer create/approve is ledger-neutral.
- Transfer Dispatch moves quantity from source to transit exactly once.
- Transfer Receive moves quantity from transit to destination exactly once.
- Source + transit + destination must preserve the transfer quantity.
- Replenishment is a same-warehouse move and must preserve total warehouse On Hand.
- Replenishment source must remain eligible by status/lock/owner and destination capacity.

## Runtime boundary

The new panels are Blueprint-only:

- no production API calls;
- no database writes;
- no inventory mutation;
- no fake posting;
- no maturity promotion.

## UI/UX

The screens follow the project UI UX Pro Max direction with semantic status text, responsive contained tables, 4/8 spacing rhythm, readable quantities and no emoji as structural icons.
