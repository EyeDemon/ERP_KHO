# Inbound Blueprint Domain Mocks

Status: **FRONTEND BLUEPRINT ENHANCEMENT**

## Purpose

This document records the domain-specific frontend mocks for planned Inbound capabilities on the ERP/WMS Blueprint branch.

They improve UI/UX completeness without claiming production backend readiness.

## Planned capabilities with specialized mock UI

- **IN-01 — Purchase Order / ASN**
  - PO ↔ ASN reconciliation;
  - expected quantity variance;
  - explicit zero inventory effect before Receipt POST.

- **IN-02 — Receiving Appointment**
  - appointment windows;
  - arrival / check-in / dock assignment separation;
  - overlap and no-show handling.

- **IN-05 — Over/Under Receipt Discrepancy**
  - expected vs observed quantity;
  - tolerance threshold;
  - reason/evidence;
  - approval/resolution boundary.

- **IN-06 — Inbound QC**
  - inspection criteria;
  - evidence summary;
  - Accepted / Damaged / Rejected disposition;
  - quantity balance guard before READY_TO_POST.

- **IN-09 — Putaway Rule Engine**
  - candidate eligibility;
  - compatibility/capacity/travel inputs;
  - scoring and explainable recommendation.

## Existing foundation surfaces not replaced

The following capabilities remain represented by their existing foundation/production-oriented UI and are intentionally not shadowed by new planned-domain panels:

- **IN-03 — Goods Receipt Work Center**
- **IN-04 — Receiving Workbench**
- **IN-07 — Receipt Posting**
- **IN-08 — Putaway Tasks**

## Runtime boundary

All specialized panels above are frontend-only Blueprint views:

- no production API calls;
- no database writes;
- no inventory mutation;
- no fake successful POST/PUT/PATCH;
- no change to capability maturity.

A rich mock screen is **not** evidence that its backend, schema, authorization, concurrency or posting boundary has been implemented.

## UI/UX rules

The screens follow the repository UI UX Pro Max direction:

- operational flat/minimal visual language;
- semantic status text in addition to color;
- responsive contained tables;
- visible hierarchy and readable quantities;
- reduced-motion safe;
- consistent domain vocabulary;
- no emoji used as structural UI icons.
