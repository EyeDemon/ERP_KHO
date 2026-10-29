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
- 179 unique capability IDs.
- 17/17 module work centers have mock fixtures.
- 134 unique operational mock records.
- 179/179 dedicated capability fixtures.
- 22 golden end-to-end scenarios.
- 282/282 canonical Notion specifications indexed in the runtime documentation register; capability-linked specs and platform/governance documents are distinguished explicitly.
- Wave 6 planning/decision-support specs 89 and 179–203 decomposed into explicit optional capabilities; recommendations/simulations remain ledger-neutral until canonical execution commands.
- Finance/costing boundary, integration reconciliation, partner SLA, calendar exceptions and controlled support/repair are explicit capabilities.
- High-level UX scope now includes Activity Feed, Mobile Product Lookup and Mobile Exception Handling as read/shared-workflow capabilities.
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
11. Shipment Tracking / POD / Delivery Failure.
12. Cartonization → Packing → Load Plan → Dispatch.
13. Advanced Reverse Logistics / Refurbishment / RTV.
14. Cold Chain Excursion → Quarantine → Disposition.
15. Catch Weight / Dual-UOM Receipt.
16. 3PL Billable Activity → Rating → Reconciliation.
17. Network Rebalancing Proposal → Canonical Transfer.
18. Procurement Suggestion → Approval → ERP PO.
19. Period Close → Finance Export → Reconciliation.
20. Controlled Production Repair → Reversal → Corrected Transaction.
21. WCS / Robotics Device Job → WMS Command.
22. Safety / Hazmat Task Eligibility Gate.

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

## Latest QA checkpoint — 2026-10-03

- HEAD: `af1814300ba3c15dd9319f145117a84bebe2f2ff`.
- GitHub Actions CI #233: PASS.
- Application / SQL integration: 347/347 PASS.
- API: 184/184 PASS.
- Frontend: 35 test files / 168 tests PASS.
- Frontend production build: PASS.
- npm audit: 0 vulnerabilities.
- SonarCloud Quality Gate: PASS; 0 new issues, 0 security hotspots, 1.1% duplication on new code.
- Vercel Blueprint deployment: READY.
- Runtime smoke-check returned HTTP 200 for System Blueprint, Coverage, Mock Data Lab, Golden Scenario Lab and Global Search routes.
- No Vercel runtime errors were observed in the checked one-hour window.
## Production boundary

This blueprint does **not** mean all 179 capabilities have production backend implementations.

Statuses remain explicit:
- Live — current real feature exists.
- Foundation — compatible base exists but capability is not complete.
- Planned — canonical spec exists; implementation is pending.
- Optional — advanced capability enabled only when customer/industry applicability requires it.

No planned/optional mock screen is treated as transactional truth.

## Production synchronization checkpoint — 2026-10-05

Blueprint implementation status has been refreshed against the main integration branch and current Notion contracts.

Production capabilities now reflected as `live` include:
- Product Category, multi-barcode and Business Partner work centers.
- Inbound: Purchase Order / ASN, Goods Receipt, Receiving, Discrepancy, line-level QC, Receipt POST and Cất hàng are live; Receiving Appointment is `foundation` because Dock/Yard scheduling/check-in exists but canonical PO/ASN/Receipt linkage is incomplete.
- ExportReceipt MVP: Draft → Approved/Reserved → Dispatched plus Reservation; this is explicitly separate from Shipment LOADED/DISPATCHED.
- Transfer: TR-03 Dispatch and TR-04 Receive are live on `/stock-transfers`; TR-02 in-transit inventory remains `foundation` because the current view is transfer-centric rather than a complete owner-aware inventory browser.
- Gate Check-in, Dock Scheduling and Yard Management from WH-06 Dock & Yard.
- Inventory reconciliation is a live route/read model but INV-11 remains `foundation`: mismatch detection exists, controlled rebuild/remediation does not.
- Role & Permission Matrix with database-backed grant/revoke.

The following outbound capabilities remain intentionally **not live** and must not be inferred from ExportReceipt MVP:
- Allocation.
- Picking / Short Pick.
- Packing / HU.
- Staging & Loading.
- Shipment Dispatch.
- Shipment Tracking / POD.

## Next implementation sequence

Continue production implementation by the canonical dependency order from Notion:

1. OUT-03 Allocation.
2. OUT-05 Picking / Short Pick.
3. OUT-06 Packing / HU foundation.
4. OUT-07 Staging & Loading.
5. OUT-08 Shipment Dispatch only after LOADED/HU/allocation prerequisites exist.
6. OUT-10 Tracking / POD after Shipment Dispatch.
7. Continue remaining Inventory Control / Transfer enhancements / Count / Returns according to evidence and customer priority; do not re-implement Transfer Dispatch/Receive, which are already live.

Do not treat ExportReceipt `APPROVED` as Shipment `LOADED`; the two workflows remain separate.
