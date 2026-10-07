# ERP/WMS Blueprint Coverage Audit — 2026-10-02

## Purpose

Audit the current Complete UI Blueprint registry against the canonical Notion technical documentation, Screen Matrix 229, UX Governance 282, Capability Map 210, Roadmap 211, Gap Register 212, Master Index 214 and Final Coverage Audit 275.

This document distinguishes:

1. **Canonical documentation coverage** — whether the product/architecture is specified.
2. **Blueprint capability coverage** — whether the frontend blueprint registry exposes the capability explicitly.
3. **Implementation readiness** — whether production code, security, tests, operations and evidence exist.
4. **Applicability** — whether a capability belongs to current commercial scope or is conditional/advanced.

A canonical spec existing does **not** mean the capability must be implemented now.

## Remediation status — applied after audit

The blueprint registry has now been corrected in the same feature branch:

- capability count increased from 119 to **179** without adding a new module;
- 60 canonical/high-level capabilities were added explicitly across core gaps, advanced/conditional specs, Wave 6 planning, operational control-plane and root UX scope;
- high-confidence stale/wrong spec references were corrected;
- broad numerical spec ranges were removed from every capability;
- optional Wave 4/5 items remain `optional` and are not promoted into the current MVP implementation queue;
- advanced specs 235–249 are now decomposed into explicit optional capabilities instead of remaining hidden inside three umbrella cards;
- all 282 canonical Notion specs are indexed in Coverage & Readiness; specs not owned by a business capability remain visible as platform/governance documents rather than being turned into fake navigation;
- Wave 6 specs 89 and 179–203 are decomposed into explicit optional planning/decision-support capabilities with recommendation/simulation-only inventory semantics;
- specs 127/128/150/154/156/158/159/171/172/174/175/176 are represented through controlled repair/support, finance integration, reconciliation, SLA and calendar capabilities rather than being hidden in generic platform cards;
- root-level UX entries Activity Feed, Mobile Product Lookup and Mobile Exception Handling are represented as read/shared-workflow capabilities using existing canonical audit/search/exception semantics;
- registry tests now reject broad spec ranges and pin representative canonical mappings.
- Inventory-owned reconciliation/projection rebuild is now explicit as `INV-11` instead of being hidden behind a reporting-only capability.
- semantic mock coverage was strengthened for the newly added capabilities using targeted operational records instead of modulo-only sample assignment; the shared dataset now contains **134 unique operational mock records**.
- Golden Scenario Lab now contains **22 end-to-end scenarios** spanning core inventory, advanced WMS, planning, finance reconciliation, controlled repair, automation and safety.

The findings below remain the rationale/history for those corrections and for later structured metadata work.

## Production truth refresh — 2026-10-08

This is the current implementation-status checkpoint for the Blueprint.

**Implementation source of truth**
- Integration branch: `feature/erp-wms-complete-ui-blueprint`.
- Verified production commit: `297b52865bac4fa785847d678280282a4663588b`.
- Vercel production alias: `erp-wms-blueprint-demo.vercel.app`.
- Vercel deployment: `dpl_ADqrgZByknUrgTovTxtDMiRBY2DJ`, READY, target `production`, Git SHA exactly matches the released INV-09/INV-10 foundation commit.
- Notion remains the canonical business/architecture reference and is **read-only for this synchronization**. No Notion content is changed to make implementation look complete.

**Registry snapshot**
- 179 capabilities total.
- 33 `live`.
- 28 `foundation`.
- 72 `planned`.
- 46 `optional`.

Status meaning for production synchronization:
- `live`: merged/deployed/QA-verified capability is complete for the currently accepted production scope.
- `foundation`: real production code exists, but canonical scope still has named gaps.
- `planned`: mock/spec may exist, but production implementation is not released.
- `optional`: advanced/conditional capability; not an immediate implementation claim.

**Outbound production reality**
- OUT-01 ExportReceipt MVP and OUT-02 Reservation remain `live`.
- OUT-03 Allocation, OUT-05 Picking, OUT-06 Packing, OUT-07 Staging & Loading, OUT-08 Shipment Dispatch, OUT-09 Backorder and OUT-10 Shipment Tracking / POD / Delivery Failure are now real production foundations and remain `foundation` until their explicitly listed canonical gaps are closed.
- OUT-04 Wave / Batch / Cluster remains `optional`.

**Inventory production reality**
- INV-01 through INV-08 are production foundations at the verified commit, including Inventory Status, Lot/Serial/Expiry, Inventory Locks/Freeze and Internal Location Move.
- INV-09 Reversal is now `foundation`: production supports immutable corrective reversal for Internal Move and Inventory Status Change with structured Original/Corrective/Reversal links, DB uniqueness, audit, permission and warehouse scope. Aggregate-specific reversal for Receipt/Shipment/Transfer/Adjustment/Return/Scrap and complete downstream-dependency policy remain gaps.
- INV-10 Traceability & Genealogy is now `foundation`: production traces by Product/Lot/Serial/Reference within authorized warehouses, returns current buckets + immutable ledger timeline, and exposes the structured reversal chain. Full cross-flow Receipt→QC→Move→Pick→Shipment/Return/Recall genealogy, recall orchestration and Owner/HU lineage remain gaps.
- INV-11 Reconciliation remains `foundation`: mismatch detection exists, controlled repair/rebuild does not.

Therefore the 2026-10-05 refresh below is retained only as historical context and must not be used as the current implementation checkpoint.


## Historical implementation status refresh — 2026-10-05 (superseded)

A fresh comparison against the deployed main integration branch found that the registry had become stale after later production merges. This is a **status synchronization correction**, not a change to canonical business semantics.

Promoted to `live` because production code/routes and merged verification now exist:
- MD-02 Product Category.
- MD-03 multi-barcode.
- MD-05 Business Partner.
- IN-03 Goods Receipt Work Center.
- IN-04 Receiving Workbench.
- IN-05 Receiving Discrepancy.
- IN-06 Inbound QC.
- IN-07 Receipt Posting.
- IN-08 Putaway Tasks.
- DY-01 Gate Check-in.
- DY-02 Dock Scheduling.
- DY-03 Yard Management.
- RP-06 Ledger Reconciliation.
- AD-02 Role & Permission Matrix.

IN-01 remains live and its traceability now explicitly includes PO/ASN state/API/permission/UX sources. IN-02 Receiving Appointment is intentionally `foundation`: Dock/Yard appointment scheduling, arrival/check-in and dock assignment are production-capable, but the canonical linkage from appointment to PO/ASN/Receipt defined by Specs 03/46 is not yet complete. IN-06 is receipt-scoped Inbound QC and does not promote the separate cross-flow QR-01 QC Work Center.

OUT-01 ExportReceipt MVP and OUT-02 Reservation remain live. OUT-01 now explicitly records that ExportReceipt `Dispatched` is not Shipment `LOADED/DISPATCHED`, and OUT-02 remains product/warehouse reservation rather than location/lot/serial allocation. The refresh deliberately leaves OUT-03 Allocation, OUT-05 Picking, OUT-06 Packing, OUT-07 Staging & Loading, OUT-08 Shipment Dispatch and OUT-10 Tracking/POD non-live. Notion Spec 38 keeps Shipment as a separate state machine with LOADED/HU/allocation prerequisites.

Transfer maturity was also stale: TR-03 Transfer Dispatch and TR-04 Transfer Receive are now `live` because the production Stock Transfer flow implements Approved → InTransit → Received, source deduction/TransferOut, destination receipt/TransferIn, warehouse scope and receipt discrepancy quantities. TR-02 In-Transit Inventory is `foundation`: quantity is visible per transfer/source/destination, but a global owner-aware in-transit inventory browser is not complete.

Inventory capabilities remain deliberately conservative. INV-01/02/03/04 stay `foundation`: production has OnHand/Reserved/Available, movement history, InventoryStock projection and eligible-location reservation logic, but not the full location/status/lot/serial/lock/allocation model from the canonical specs. INV-11 is read-only reconciliation today; controlled rebuild/remediation is not yet implemented and is no longer described as if it were production-ready.

## Executive finding

The canonical documentation set is substantially complete. Spec 275 states that after pages 276–281 there are no obvious foundational solution-architecture/product/operations documentation gaps; conditional capabilities remain conditional.

At audit start, the blueprint had two material issues:

- several canonical capabilities are missing or under-modeled in the registry;
- many capability-to-spec references are stale, overly broad, or point at the wrong numbered document.

The latter is the larger traceability risk because broad ranges such as `47-56, 64-81` can make coverage appear complete while mapping a capability to unrelated specifications.

## Screen Matrix 229 status

The canonical Screen Matrix snapshot used at audit time contained:

| Coverage Status | Count |
| --- | ---: |
| Designed | 199 |
| Reviewed | 1 |
| Review Required | 4 |
| Deprecated | 3 |

No row was marked `Missing`.

The four rows that were `Review Required` in that source snapshot were:

- Internal Location Transfer.
- Picking.
- Packing.
- Create Warehouse Transfer.

**Blueprint remediation is now closed for all four mappings.** The current branch marks the corresponding capabilities as `Traceability Closed` and routes each to a concrete Blueprint preview:

- INV-08 Internal Location Transfer.
- OUT-05 Picking.
- OUT-06 Packing.
- TR-01 Warehouse Transfer.

This closes Blueprint screen traceability only. Production/backend maturity and release evidence remain tracked separately and must not be inferred from the closed UI mapping.

## High-confidence capability gaps

### Required core / platform coverage

These should be represented explicitly in the blueprint even if implementation remains `planned` or `foundation`.

| Capability | Canonical source | Why it is a gap |
| --- | --- | --- |
| Warehouse Task Engine & Work Queue | 80; Capability Map 210 | My Tasks exists as UI, but the shared task orchestration/claim/dependency/block/SLA capability is not modeled explicitly. |
| Shipment Tracking / POD / Delivery Failure | 58, 74 | Dispatch exists, but post-dispatch tracking, delivery confirmation, POD, failed delivery and return-to-warehouse lifecycle is not explicit. |
| Enterprise / Global Search | 95, 216 | A mock Global Search now exists, but the capability registry does not expose the canonical search architecture/security boundary. |
| Document / Attachment / Evidence | 62, 83, 96 | QC, damage, POD, return, scrap and audit all depend on secure evidence storage, but no cross-cutting capability owns it. |
| Number Sequence / Document Numbering | 69, 276 | Document numbers must be concurrency-safe and immutable by policy; no explicit platform/admin capability exists. |
| Notification Engine + Notification Center | 61, 271 | `AD-07 Notification` is too generic and references unrelated broad platform ranges instead of the canonical engine/inbox specs. |
| Business Rules / Workflow Configuration | 51, 276 | Configuration/feature flags exist, but scoped rule resolution and workflow configuration are not represented distinctly. |
| Controlled Data Import / Bulk Operations | 63, 76 | Canonical platform functionality exists but is not represented as a capability. |
| Inventory Reconciliation & Integrity Monitoring | 82 | Ledger reconciliation exists as a report, but canonical integrity monitoring/rebuild/reconciliation ownership is under-modeled. |
| Inventory Availability Engine | 88 | `INV-04 Availability Query` exists, but does not reference its canonical dedicated spec. |

### Master data / warehouse execution gaps

| Capability | Canonical source | Classification |
| --- | --- | --- |
| Packaging Type master | Root Master Data scope; 37, 238 | Planned; can be owned by Master Data / Packaging. |
| Equipment & MHE asset management | 59 | Wave 4 / Advanced WMS, but should be explicit rather than hidden inside labor/automation ranges. |

### Conditional / customer-evidence capabilities

These belong in the blueprint as `optional` / feature-enabled, **not** in the immediate MVP queue.

| Capability | Canonical source | Roadmap |
| --- | --- | --- |
| Kitting / Bundling / Assembly / De-kitting | 52, 243 | Wave 5 Enterprise/3PL |
| Value Added Services (VAS) | 53, 244 | Wave 5 Enterprise/3PL |
| Advanced Reverse Logistics / Refurbishment / RTV | 247 | Conditional; spec 43 explicitly requires customer validation before expanding returns |
| Cartonization / Cubing optimization | 238 | Advanced / feature-enabled |
| Load Planning / Trailer utilization | 239 | Advanced / feature-enabled |

### Coverage decisions, not automatic additions

- **Activity Feed** appears in the root functional overview but no dedicated canonical spec was found. Do not invent a separate module yet; it may be a read model over Audit/Notification.
- **Supplier Return** does not currently have a clearly separate execution specification; RTV is represented as a disposition in Returns/Reverse Logistics. Treat this as a product-boundary decision, not a missing screen by assumption.
- **Mobile Product Lookup / Exception Handling** appear in the high-level scope, while Mobile UX 223 centers on task execution. Decide whether these are dedicated mobile screens or shared Global Search / Exception flows before adding new IDs.

## Spec traceability corrections — high confidence

The following current references are misleading and should be corrected in the registry.

| Capability | Current reference | Canonical correction / primary sources |
| --- | --- | --- |
| OV-02 My Tasks | 216, 223 | add 80 |
| OV-03 Approval Center | 17, 71, 216 | 17, **70**, 221 |
| OV-04 Exception Center | 71, 216 | 71, **92**, 221 |
| MD-03 Barcode | 84, 162 | add **65** |
| MD-06 Carrier | 84, 161 | **58, 84, 164** |
| OUT-01 Sales/Export Order | 36, 161, 216 | 72, 87, 161, 228 |
| OUT-04 Wave/Batch/Cluster | 47-56, 64-81 | **45** |
| OUT-05 Picking | 37, 45, 72, 74 | **36**, 65, 72, 223/228 |
| OUT-06 Packing | 38, 87 | **37**, 64, 238 |
| OUT-07 Staging & Loading | 38, 45 | 38, 46, 239 |
| INV-01 Inventory Browser | 29, 40-44, 222 | 29, 41, 73, 77, 78, 82, 88, 222 |
| INV-02 Immutable Ledger | 28-33 | 28, 29, 32 |
| INV-03 Balance Projection | 29 | add 82 |
| INV-04 Availability Query | 30-33 | **88** plus relevant 30/41/77 |
| INV-06 Lot/Serial/Expiry | 31, 42, 73 | **48, 65, 78, 222** |
| INV-07 Inventory Locks | 32, 40 | **77**, 31, 40 |
| INV-08 Internal Location Move | 35, 39 | **29, 222, 282** |
| INV-09 Reversal | 29, 33 | **32** |
| TR-05 Replenishment | 49, 216, 223 | **44**, 223 |
| QR-03 Damaged Inventory | 41, 71, 229 | **42**, 41, 62, 71 |
| QR-05 Recall | 48 | add **79** |
| QR-06 Scrap | 44 | **42**, 62, 70 |
| HU-01 Handling Unit | 50, 216 | **37**, 216 |
| HU-02 Pallet/Carton/Tote | 50 | **37** |
| HU-03 Label Center | 51, 235-239 | **64**, 59 |
| HU-04 SSCC / Logistics Label | 235-239 | **64** |
| DY-03 Yard Management | 64-81 | **46, 248** |
| DY-04 Cross-Docking | 52, 216 | **47**, 216 |
| RP-01 Inventory Snapshot | 227 | **82**, 227 |
| RP-03 Inbound/Outbound Performance | 227 | **68, 90, 91, 227** |
| RP-04 Aging/Expiry | 73, 227 | **78, 187, 227** |
| RP-06 Ledger Reconciliation | 29, 155 | add **82** |
| RP-07 Productivity / Heatmap | 67, 227 | **67, 81, 227** |
| AD-04 Approval Workflow | 71, 221 | **70**, 221 |
| AD-07 Notification | 71, 94-129 | **61, 92, 271** |
| IG-06 Carrier / E-commerce / TMS | 160-177 | explicit 163, 164, 165, 166, 169 |
| MO-01 Home & My Tasks | 223, 229 | add **80** |
| MO-04 Picking Scan | 37, 223, 229 | **36, 65, 223, 229** |
| MO-05 Packing | 38, 223, 229 | **37, 64, 65, 223, 229** |
| MO-08 Replenishment | 223, 229 | add **44** |
| AX-01 Slotting | 47-56, 64-81 | **49** |
| AX-02 Labor / Workload | 64-81, 194 | **50, 60, 80, 81** |
| AX-05 3PL / Multi-owner / Billing | 157, 173, 240-249 | **54, 55, 56, 173, 245, 246** |
| AX-06 RFID / Voice / Pick-to-Light / Robotics | 235-239 | **235, 236, 237** |
| AX-07 Cold-chain / Hazmat / Catch-weight | 240-249 | **240, 241, 242** |

This table is intentionally conservative: it lists only mappings with strong canonical evidence. Supporting specs may still be added where they materially define UX/security/integration.

## Blueprint information model gaps

The current registry field `status: live | foundation | planned | optional` mixes several different concepts. It should be decomposed.

Recommended capability metadata:

- `canonicalSpecs: number[]` — exact primary specs, no broad numerical ranges.
- `supportingSpecs: number[]` — UX/security/integration references.
- `releaseWave` — Wave 0–6 from spec 211.
- `applicability` — REQUIRED_CORE / REQUIRED_WHEN_FEATURE_ENABLED / INDUSTRY_OPTIONAL / IMPLEMENTATION_SPECIFIC from spec 275.
- `maturity` — M0 Designed → M5 Optimized from spec 211.
- `implementationStatus` — code reality, separate from applicability.
- `ownerModule` — one owner per invariant per spec 210.
- `screenIds` — Screen Matrix 229 links.
- `permissionCodes`.
- `commands/apis`.
- `stateMachine`.
- `inventoryEffect` — none / movement / status / projection/read-only.
- `eventsErrors`.
- `testEvidence`.
- `specRevision/freshness`.

This supports the canonical completion chain:

`Persona/Requirement → UX/Screen → Permission → Command/API → State Machine → Data/Ledger Effect → Event/Error → Test → Operational Runbook → Release Evidence`.

## Cross-cutting information that must remain explicit

From UX Governance 282:

- quantity entry always shows operation UOM and Base UOM conversion;
- DAMAGE_FOUND is a shared exception pattern rather than five duplicated flows;
- sensitive/financial fields are filtered at server/query/projection, not merely hidden in frontend;
- derived data inherits the highest sensitivity of its inputs;
- Goods Receipt QC/disposition completes before POST;
- discrepancy resolution and QC are separate;
- Putaway occurs only after Receipt POST;
- user-visible permission experiences use canonical Vietnamese copy and server remains authoritative.

## Priority recommendation

### Correct now in blueprint metadata
1. Fix incorrect spec references.
2. Remove broad range references that create false coverage.
3. Add explicit required-core/platform capabilities: Task Engine, Shipment Tracking/POD, Search, Evidence, Number Sequence, Notification, Rules/Workflow config, Import/Bulk Ops, Availability/Reconciliation ownership.
4. Add structured applicability/maturity/release-wave metadata.

### Add as planned / Wave 4
- Equipment/MHE.
- Packaging Type.
- Advanced task/labor details.

### Add as optional / Wave 5+
- Kitting.
- VAS.
- Advanced reverse logistics/refurbishment.
- Cartonization.
- Load planning.

### Do not invent yet
- Activity Feed as a standalone capability.
- Separate Supplier Return workflow beyond canonical RTV disposition until product boundary is clarified by customer evidence.

## CI note

The previous workflow triggered on both `push` and `pull_request`, so a commit on a branch with an open PR produced duplicate runs.

The workflow is updated alongside this audit so:

- direct pushes still run CI on the stable/default and current integration branch;
- feature branches with an open PR run through `pull_request` only;
- superseded runs in the same PR/ref are cancelled via GitHub Actions concurrency.

This reduces duplicate compute without weakening the CI gate.
