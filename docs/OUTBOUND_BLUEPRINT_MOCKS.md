# Outbound Blueprint Domain Mocks

Status: **FRONTEND BLUEPRINT ENHANCEMENT**

## Scope

This batch adds specialized read-only Blueprint UI for outbound capabilities that were still represented by generic simulator content.

### Specialized mock panels

- **OUT-03 — Allocation**
  - reserved demand → eligible location/lot candidates;
  - status/lock/lot eligibility;
  - explicit zero warehouse On Hand effect.

- **OUT-04 — Wave / Batch / Cluster**
  - priority/cutoff grouping;
  - workload and capacity fit;
  - task-release semantics without inventory mutation.

- **OUT-07 — Staging & Loading**
  - HU readiness;
  - staging lane;
  - vehicle, seal and load sequence;
  - missing-HU guard.

- **OUT-08 — Shipment Dispatch**
  - pre-flight validation;
  - idempotency/concurrency context;
  - single outbound physical deduction boundary;
  - reservation/allocation consumption.

- **OUT-09 — Backorder**
  - ordered/reserved/allocated/picked/shipped/backorder quantity waterfall;
  - supply ETA and promise replanning.

- **OUT-10 — Shipment Tracking / POD**
  - post-dispatch logistics timeline;
  - POD state;
  - delivery failure/retry/return semantics without double inventory movement.

## Existing surfaces intentionally not replaced

- **OUT-01 — Sales / Export Order** — live route.
- **OUT-02 — Reservation** — live route.
- **OUT-05 — Picking** — already has Screen Matrix specialized content.
- **OUT-06 — Packing** — already has Screen Matrix specialized content.

## Inventory boundary

Outbound execution semantics are explicit:

- Reservation protects demand but does not reduce On Hand.
- Allocation binds eligible stock but does not reduce On Hand.
- Picking/packing/staging/loading do not reduce warehouse On Hand.
- **Shipment Dispatch is the only outbound deduction boundary**.
- Post-dispatch tracking/POD does not create another deduction.
- Delivery failure does not auto-return stock; return inventory requires its own valid inbound/return posting flow.

## Runtime boundary

All new panels are Blueprint-only:

- no production API calls;
- no database writes;
- no inventory mutation;
- no fake success response;
- no maturity promotion.

## UI/UX

The panels follow the project UI UX Pro Max direction:

- operational flat/minimal hierarchy;
- semantic status text plus color;
- responsive contained tables;
- readable numeric states with tabular figures;
- no emoji as structural icons;
- reduced-motion safe;
- no horizontal page overflow.
