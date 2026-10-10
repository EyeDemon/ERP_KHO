# Count & Adjustment Blueprint Domain Mocks

Status: **FRONTEND BLUEPRINT ENHANCEMENT**

## Specialized planned panels

- **CT-03 — Blind Count**
  - hides system quantity from counters;
  - preserves location / stock identity / UOM context;
  - avoids accidental quantity leakage in helper states.

- **CT-04 — Freeze Strategy**
  - compares Hard Freeze, Soft Freeze and Snapshot-only;
  - shows open-work conflict checks;
  - keeps snapshot baseline explicit.

- **CT-05 — Recount**
  - immutable attempt history;
  - different counter / supervisor context;
  - final accepted count does not overwrite prior attempts.

- **CT-06 — Variance Resolution**
  - snapshot vs final count;
  - threshold evaluation;
  - evidence / recount / approval routing.

- **CT-07 — Inventory Adjustment**
  - signed delta;
  - segregation of duties;
  - approval is ledger-neutral;
  - POST is the only inventory-changing boundary.

## Existing surfaces intentionally not replaced

- **CT-01 — Full Count** — live stocktake route.
- **CT-02 — Cycle Count** — foundation capability.

## Runtime boundary

The new panels are Blueprint-only:

- no production API calls;
- no database writes;
- no inventory mutation;
- no fake approval/posting;
- no maturity promotion.

## Inventory semantics

- Blind Count must not expose system quantity to the counter when policy requires blindness.
- Recount attempts are immutable evidence.
- Count variance resolution does not itself mutate inventory.
- Approval does not change On Hand.
- Only a successful Inventory Adjustment POST creates an immutable inventory movement.
- Retry/posting must remain idempotent and auditable.

## UI/UX

The screens follow the project UI UX Pro Max direction with semantic text + color, responsive contained tables, readable quantities, 4/8 spacing rhythm and no emoji as structural icons.
