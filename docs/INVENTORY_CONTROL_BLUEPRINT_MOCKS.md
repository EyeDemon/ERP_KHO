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
  - immutable original ledger history;
  - structured Original → Corrective → Reversal marker links;
  - database-authoritative one-reversal-per-original guard;
  - current production scope: Internal Move + Inventory Status Change;
  - document/aggregate reversals, partial reversal and downstream-dependency policy remain incomplete.
  - Production maturity: `foundation`.

- **INV-10 — Traceability & Genealogy**
  - current bucket + immutable ledger timeline;
  - Product / Lot / Serial / Reference search under warehouse authorization;
  - structured reversal-chain expansion from original/corrective/marker;
  - full Receipt → QC → Move → Pick → Shipment / Return / Recall genealogy, Owner/HU and recall orchestration remain incomplete.
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

INV-05 through INV-10 now have real production foundations elsewhere in the application. INV-09 and INV-10 remain deliberately non-live because their canonical aggregate/document reversal, full genealogy, return/recall, Owner/HU and related governance gaps are not complete.

## UI/UX

The screens follow the project UI UX Pro Max direction:

- operational flat/minimal layout;
- semantic status text in addition to color;
- contained responsive data tables;
- readable numeric states;
- no emoji as structural UI icons;
- reduced-motion safe;
- clear warning/recovery language.
