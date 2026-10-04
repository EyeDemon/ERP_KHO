# Inventory Control Blueprint Domain Mocks

Status: **FRONTEND BLUEPRINT ENHANCEMENT**

## Specialized planned capability panels

This batch adds domain-specific, read-only Blueprint UI for the planned Inventory Control capabilities that were still represented by generic simulator content:

- **INV-05 — Inventory Status**
  - status quantities;
  - reserve/allocate/pick eligibility;
  - hold/block/release guardrails.

- **INV-06 — Lot / Serial / Expiry**
  - lot/serial identity search;
  - expiry / FEFO context;
  - duplicate identity protection.

- **INV-07 — Inventory Locks / Freeze**
  - lock scope;
  - hard / soft / snapshot-only strategy;
  - open-work impact.

- **INV-09 — Reversal**
  - immutable original ledger transaction;
  - reversal transaction;
  - corrected transaction;
  - correlation and reversibility guards.

- **INV-10 — Traceability & Genealogy**
  - backward and forward lot/serial trace;
  - shipment exposure;
  - return branches;
  - explicit incomplete-chain state.

## Existing surfaces intentionally not replaced

- **INV-01 — Inventory Browser** — foundation route.
- **INV-02 — Immutable Inventory Ledger** — foundation.
- **INV-03 — Balance Projection** — foundation.
- **INV-04 — Availability Engine** — foundation.
- **INV-08 — Internal Location Move** — already has Screen Matrix specialized content.
- **INV-11 — Integrity & Reconciliation** — foundation route.

## Integrity rules represented by the UI

- Posted ledger entries are immutable.
- Status controls do not bypass lock/security policy.
- Lot/serial identity does not bypass inventory eligibility.
- Reversal creates new signed entries; it never edits/deletes historical ledger rows.
- Genealogy with missing source/correlation links must be shown as incomplete rather than inferred.
- Same-warehouse move semantics remain owned by INV-08 and preserve total warehouse On Hand.

## Runtime boundary

All new panels are Blueprint-only:

- no production API calls;
- no database writes;
- no ledger mutation;
- no inventory mutation;
- no fake successful command;
- no capability maturity promotion.

## UI/UX

The screens follow the project UI UX Pro Max direction:

- operational flat/minimal layout;
- semantic status text in addition to color;
- contained responsive data tables;
- readable numeric states;
- no emoji as structural UI icons;
- reduced-motion safe;
- clear warning/recovery language.
