# ERP WMS Complete UI Blueprint & Mock Test Handoff

## Purpose

This branch provides a complete ERP/WMS UI/UX system blueprint driven by the canonical Notion specifications.

It is intentionally separated from production backend capability completion:

- Existing production routes remain linked where available.
- Planned/optional capabilities are rendered as explicit mock/spec previews.
- Mock data never mutates the real API or database.
- Inventory integrity rules remain authoritative.

## Branch / PR

- Branch: `feature/erp-wms-complete-ui-blueprint`
- Base: `feature/inbound-permission-code-authorization`
- Draft PR: #2

## Coverage

Static coverage audit on the branch:

- 17 module groups.
- 130 unique capability IDs.
- 17/17 module work centers have mock fixtures.
- 85 unique operational mock records.
- 130/130 dedicated capability fixtures.
- 10 golden end-to-end scenarios.
- Capability-level preview route for every capability, with real-route link where implementation exists.
- Mobile capabilities include scan-first phone preview.
- Blueprint mode has its own 17-module read-only navigation.

## Main routes

- `/system-blueprint` — complete ERP/WMS capability map.
- `/system-blueprint/:moduleKey` — module work center with searchable/filterable mock data.
- `/system-blueprint/:moduleKey/:capabilityId` — dedicated preview for an individual capability.
- `/system-blueprint/mock-data` — master/inventory/transfer/recount fixture explorer.
- `/system-blueprint/scenarios` — golden scenario lab.

## Mock reference data

### Warehouses
- WH-HCM-01 — DC Hồ Chí Minh
- WH-DN-01 — Kho Đà Nẵng
- WH-DL-01 — Kho Đà Lạt
- WH-HN-01 — Hub Hà Nội

### Product fixtures
Six SKUs cover:
- lot tracking,
- serial tracking,
- non-tracked packaging,
- multiple barcodes,
- UOM examples.

### Partners
Fixtures cover:
- suppliers,
- customers,
- carrier.

### Users
Fixtures cover:
- Admin,
- Warehouse Manager,
- Receiver,
- Picker,
- QC,
- warehouse-scoped access.

## Mock operational coverage

Every module has records with realistic:
- reference/document number,
- workflow status,
- warehouse,
- location,
- product/partner reference,
- quantity + UOM,
- assigned owner,
- priority,
- timestamp,
- warning/exception semantics.

Modules:
1. Overview & Work Centers
2. Master Data
3. Warehouse Structure
4. Inbound
5. Outbound
6. Inventory Control
7. Transfer & Replenishment
8. Count & Adjustment
9. Quality & Returns
10. Handling Unit / Packaging / Label
11. Dock / Yard / Cross-dock
12. Reports & Analytics
13. Administration & Security
14. Integration
15. Mobile WMS
16. Operations & Resilience
17. Advanced WMS & Planning

## Golden scenarios

The scenario lab includes:

1. Inbound Receipt → Post → Putaway.
2. Outbound Reserve → Allocate → Pick → Dispatch.
3. Warehouse Transfer Conservation.
4. Cycle Count → Recount → Adjustment.
5. Inbound QC → Disposition → Receipt Post (canonical pre-post QC flow).
6. Reversal & Corrective Receipt.
7. Concurrent Reservation.
8. Idempotent Shipment Dispatch.
9. Customer Return Inspection & Posting.
10. Mobile Offline Deferred Sync.

## Canonical invariants represented by tests

- Capability IDs are unique.
- Every blueprint module has fixture coverage.
- Mock record IDs are unique.
- Product/partner/warehouse references resolve.
- Quantities are non-negative where expected.
- Allocation <= Reservation.
- Reserved <= OnHand.
- Available = eligible OnHand - active Reserved; the current demo excludes QC_HOLD and QUARANTINE from eligible OnHand.
- Transfer Source + Transit + Destination = requested quantity.
- Recount attempts are immutable/sequential and only one final attempt is accepted.
- Count is not shown as completed before adjustment approval/post.
- Canonical Goods Receipt: Receive/QC/disposition are ledger-neutral; POST is the sole inventory boundary.
- Picking does not deduct warehouse OnHand.
- Dispatch does deduct warehouse OnHand.
- QC disposition preserves physical quantity.
- Reversal preserves ledger history.
- Concurrent reservation does not over-reserve.
- Repeated idempotent dispatch produces one business mutation.
- Offline high-risk mutations are blocked in the scenario model.

## Frontend tests added

- `erpWmsBlueprint.test.ts`
- `erpWmsMockData.test.ts`
- `erpWmsMockScenarios.test.ts`
- `MainLayout.test.tsx`
- `SystemBlueprint.test.tsx`
- `ModuleBlueprint.test.tsx`
- `CapabilityPreview.test.tsx`
- `MockDataLab.test.tsx`
- `MockScenarioLab.test.tsx`

## Expected validation commands

From `frontend/`:

```bash
npm ci
npm run lint
npm test
npm run build
npm audit --audit-level=high
```

Repository-level CI also verifies backend, SQL Server integration, encoding and frontend build/test.

## Canonical Goods Receipt override

Mock/UAT semantics follow the newer canonical override in spec 41/228:
- Receive does not mutate inventory or ledger.
- QC/disposition is resolved before READY_TO_POST.
- POST is the sole inventory boundary.
- Accepted quantity posts to AVAILABLE.
- Damaged quantity posts to DAMAGED.
- Rejected-at-door quantity does not enter Physical On Hand.
- QC_HOLD/QUARANTINE remain non-eligible inventory statuses for other explicitly modeled status-change flows.

## Production boundary

This blueprint does **not** mean all 130 capabilities have production backend implementations.

Statuses remain explicit:
- Live — current real feature exists.
- Foundation — compatible base exists but capability is not complete.
- Planned — canonical spec exists; implementation is pending.
- Optional — advanced capability enabled only when customer/industry applicability requires it.

No planned/optional mock screen is treated as transactional truth.

## Next implementation sequence

After this blueprint/mock checkpoint is QA clean, production implementation should continue by dependency:

1. Inventory integrity / posting primitives.
2. Receipt Post production evidence.
3. Reservation / Allocation.
4. Shipment Dispatch.
5. Inventory Control.
6. Transfer / Count / Returns based on customer evidence.
7. Advanced WMS only after core integrity and commercial need are proven.
