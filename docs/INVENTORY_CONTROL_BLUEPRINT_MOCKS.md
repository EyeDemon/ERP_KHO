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
  - structured reversal marker + corrective transaction links;
  - SQL UNIQUE one-reversal guard;
  - reason/audit and transactional rollback.
  - Production maturity: `foundation`.
  - Current production scope is Internal Location Move + Inventory Status Change; broader document-bound reversal remains incomplete.

- **INV-10 — Traceability & Genealogy**
  - current bucket + immutable ledger trace by Product/Lot/Serial/Reference;
  - warehouse-scoped security;
  - structured Original ↔ Corrective ↔ Reversal marker chain.
  - Production maturity: `foundation`.
  - Full Receipt/QC/Pick/Shipment/Return/Recall genealogy and recall orchestration remain incomplete.

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

INV-05 through INV-10 now have real production foundations elsewhere in the application. INV-09 and INV-10 are intentionally still only `foundation`: the deployed scope is narrower than the full canonical reversal and genealogy requirements.

## UI/UX

The screens follow the project UI UX Pro Max direction:

- operational flat/minimal layout;
- semantic status text in addition to color;
- contained responsive data tables;
- readable numeric states;
- no emoji as structural UI icons;
- reduced-motion safe;
- clear warning/recovery language.
