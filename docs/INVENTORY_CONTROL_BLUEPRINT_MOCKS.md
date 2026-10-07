# Inventory Control Blueprint Domain Mocks

Status: **BLUEPRINT UI REFERENCE — production status synced 2026-10-07**

This document describes specialized Blueprint-only mock panels. It does **not** define production maturity by itself. Production status comes from the verified integration branch and `frontend/src/config/erpWmsBlueprint.ts`; Notion remains the canonical read-only business/spec reference.

## Specialized capability panels

The Inventory Control Blueprint includes domain-specific, read-only mock panels for:

### Production foundations

- **INV-05 — Inventory Status**
  - status quantities;
  - reserve/allocate/pick eligibility;
  - hold/block/release guardrails.
  - Production maturity: `foundation`.

- **INV-06 — Lot / Serial / Expiry**
  - lot/serial identity search;
  - expiry / FEFO context;
  - duplicate identity protection.
  - Production maturity: `foundation`.

- **INV-07 — Inventory Locks / Freeze**
  - lock scope;
  - lock type / effective overlap semantics;
  - open-work impact.
  - Production maturity: `foundation`.

- **INV-08 — Internal Location Move**
  - same-warehouse location movement;
  - lock/capacity/storage compatibility;
  - MOVE ledger and quantity preservation.
  - Production maturity: `foundation`.

- **INV-09 — Reversal**
  - immutable original ledger transaction;
  - corrective transaction + reversal marker;
  - structured Original / Corrective / Marker links;
  - DB-authoritative double-reversal guard;
  - production scope currently covers Internal Move and Inventory Status Change.
  - Production maturity: `foundation`.

- **INV-10 — Traceability & Genealogy**
  - Product / Lot / Serial / Reference trace within warehouse scope;
  - current inventory buckets + immutable ledger timeline;
  - structured reversal-chain projection;
  - full Return/Recall genealogy and orchestration remain incomplete.
  - Production maturity: `foundation`.

## Other production foundations

- **INV-01 — Inventory Browser** — `foundation` route.
- **INV-02 — Immutable Inventory Ledger** — `foundation`.
- **INV-03 — Balance Projection** — `foundation`.
- **INV-04 — Availability Engine** — `foundation`.
- **INV-11 — Integrity & Reconciliation** — `foundation` route.

## Integrity rules represented by the UI

- Posted ledger entries are immutable.
- Status controls do not bypass lock/security policy.
- Lot/serial identity does not bypass inventory eligibility.
- Reversal creates new signed entries; it never edits/deletes historical ledger rows.
- Genealogy with missing source/correlation links must be shown as incomplete rather than inferred.
- Same-warehouse move semantics remain owned by INV-08 and preserve total warehouse On Hand.

## Runtime boundary

The specialized panels themselves are Blueprint-only:

- they do not call production mutation APIs;
- they do not write the database;
- they do not mutate ledger or inventory;
- they must not display fake successful production commands;
- they must not promote capability maturity merely because a mock exists.

INV-05 through INV-10 now have real production foundations elsewhere in the application. INV-09 and INV-10 were promoted only after merge, green post-merge CI and READY production deployment evidence; their named canonical gaps keep them at `foundation`, not `live`.

## UI/UX

The screens follow the project UI UX Pro Max direction:

- operational flat/minimal layout;
- semantic status text in addition to color;
- contained responsive data tables;
- readable numeric states;
- no emoji as structural UI icons;
- reduced-motion safe;
- clear warning/recovery language.
