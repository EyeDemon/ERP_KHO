# Quality, Returns & Disposition Blueprint Domain Mocks

Status: **FRONTEND BLUEPRINT ENHANCEMENT**

## Specialized planned panels

- **QR-01 — QC Work Center**
  - inspection queue, criteria and evidence;
  - failed criteria require an explicit disposition.

- **QR-02 — QC Hold / Quarantine**
  - reserve / allocation / pick eligibility blocking;
  - controlled release back to AVAILABLE.

- **QR-03 — Damaged Inventory**
  - shared DAMAGE_FOUND semantics across receiving, putaway, picking, transfer, count and returns;
  - isolation, QC review and disposition.

- **QR-04 — Customer Returns / RMA**
  - authorized return and original shipment linkage;
  - inspection and disposition;
  - explicit Return Receipt POST boundary.

- **QR-05 — Recall**
  - in-stock and shipped exposure;
  - inventory blocking;
  - customer exposure and genealogy completeness.

- **QR-06 — Scrap**
  - reason/evidence;
  - segregation of duties and approval;
  - explicit inventory posting boundary.

## Runtime boundary

All panels are Blueprint-only:

- no production API calls;
- no database writes;
- no inventory mutation;
- no fake disposition / approval / posting;
- no capability maturity promotion.

## Inventory semantics

- QC failure does not automatically release inventory.
- QC_HOLD / QUARANTINE stock remains ineligible for reserve, allocation and pick until policy release.
- Damaged quantity cannot exceed observed / eligible quantity.
- Customer return RECEIVE / INSPECT does not increase On Hand; Return Receipt POST is the inventory boundary.
- Recall cannot close while traceability scope is incomplete.
- Scrap approval is ledger-neutral; only Scrap POST decreases inventory exactly once.

## UI/UX

The panels follow the project UI UX Pro Max direction:
- operational flat/minimal visual language;
- semantic status text in addition to color;
- responsive contained layouts;
- readable quantities and state boundaries;
- reduced-motion safe;
- vector icons instead of emoji as structural UI.
