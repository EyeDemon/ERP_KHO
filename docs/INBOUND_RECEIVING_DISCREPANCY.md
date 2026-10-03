# Inbound Receiving Discrepancy

## Current verification state

The discrepancy workflow has historical owned-SQL/browser closure below and now uses database permission grants. The accumulated PR acceptance report is `ACCUMULATED_PR_OWNER_ACCEPTANCE_REVIEW.md`; READY for this module does not mean READY for all ERP modules.

## Superseded implementation checkpoint — 2026-09-24

**RECEIVING DISCREPANCY TESTING INCOMPLETE — OWNED SQL ADMIN CONNECTION REQUIRES USER CONFIGURATION**

Canonical Notion sources were read back before implementation. Native timestamps remained unchanged from the accepted 2026-09-23 contract: 01 `15:38:52.544Z`, 17 `15:38:54.658Z`, 20 `15:38:55.860Z`, 34 `15:38:57.552Z`, 71 `15:38:59.281Z`, 84 `15:39:00.942Z`, 228 `15:39:01.746Z`, 282 `15:39:04.511Z`, and `INB-RECEIPT-DISCREPANCY` `15:39:06.751Z`. Page 41 remained `2026-09-20T16:31:54.616Z`; its post-then-QC text remains superseded for Goods Receipt.

Drive remains illustrative. Recursive metadata was unchanged: root 140 files plus five folders, Corrected 16, Enriched 24, Merged/Split 56, New Screens 10, Deprecated 176. The discrepancy artifact remains `1VgvBwWHLgKqAaqqJBwpoSs5slQCMLRBk`, modified `2026-09-18T12:17:39Z`, size 1,490,708 bytes. It was metadata-checked and **NOT REVIEWED** in this run.

## State and quantity contract

```text
Draft --observe exact--> Received/QcPending
Draft --observe mismatch--> DiscrepancyPending
DiscrepancyPending --submit within policy--> DiscrepancyResolved --> Received/QcPending
DiscrepancyPending --submit requiring approval--> DiscrepancyPendingApproval
DiscrepancyPendingApproval --approve--> DiscrepancyResolved --> Received/QcPending
DiscrepancyPendingApproval --reject--> DiscrepancyPending --recount/resubmit--> ...
```

- Expected is immutable after Draft.
- Observed is append-only raw count evidence; recount creates a version and can void/supersede identified observation items.
- Door Rejected is outside warehouse custody and never creates inventory, ledger or QC disposition.
- Final Received is derived from the accepted resolution version and feeds the existing QC/no-QC workflow.
- Signed difference is `Observed - Expected`.
- QC enforces `Accepted + Damaged + RejectedRetained = FinalReceived`.
- Operation/Base quantities use snapshotted conversion. A different observed UOM requires an active/effective conversion and explicit normalized-observation confirmation. Post never reads live UOM master data.

## Reason, tolerance and responsibility

`ReceivingReasonCode` is versioned master data. Resolution history snapshots its code, name, category, version, effective time and note/attachment/approval requirements. Seeded codes are `UNDER_RECEIPT`, `OVER_RECEIPT`, `DAMAGED_ON_RECEIPT`, `WRONG_PRODUCT`, `WRONG_LOT`, `REJECTED_AT_DOOR`, `UOM_MISMATCH` and `DUPLICATE_COUNT`.

Tolerance policies use Base UOM and follow Product–Supplier–Warehouse, Product–Supplier, Product, Warehouse default, System default precedence. The resolution snapshots the selected policy/version/source/effective time and evaluated thresholds. Viewer responses omit resolution notes, evidence references, responsible party and claim flags.

Typed actions are `ACCEPT_OBSERVED`, `ACCEPT_EXPECTED_REJECT_EXCESS`, `REJECT_AT_DOOR`, `REJECT_WRONG_PRODUCT` and `ROUTE_TO_QC`. Supplier claim is companion metadata only. Responsibility is Supplier, Carrier, Warehouse, Customer Return or Unknown; Unknown requires a note.

## API and security

- `GET /api/importreceipts/{id}/discrepancies`
- `GET /api/importreceipts/discrepancy-reasons`
- `POST /api/importreceipts/{id}/discrepancies/observe`
- `POST /api/importreceipts/{id}/discrepancies/{discrepancyId}/submit`
- `POST /api/importreceipts/{id}/discrepancies/{discrepancyId}/approve`
- `POST /api/importreceipts/{id}/discrepancies/{discrepancyId}/reject`
- `POST /api/importreceipts/{id}/discrepancies/{discrepancyId}/recount`

Every receipt command is warehouse-scoped. Mutations use the existing user/command/key/fingerprint idempotency filter. Discrepancy RowVersion protects submit/recount/approval, receipt Status protects the initial observe transition, and unique races map to HTTP 409. Capability authorization uses database `receiving_discrepancy.read/create/submit/approve/reject/resolve`; recount uses `resolve`. Maker/checker is independent. Permission authorization runs before the idempotency action filter; any claim created inside its transaction is rolled back on resource/state denial and completed replay reauthorizes warehouse scope. Denial leaves no durable claim, reusable fingerprint or cached success.

## Migration and compatibility

Migration `AddReceivingDiscrepancyWorkflow` creates reason/tolerance masters and append-only observation/resolution tables, adds line projections, self-version links, constraints and indexes, and updates the EF snapshot. Legacy receipt states are unchanged. New projection columns default to zero; no historical observation, reason, policy or custody decision is inferred. Existing no-QC Receive uses legacy `Quantity` when an old line lacks `ExpectedQuantity`.

## Verification

Fresh evidence on the current working tree:

- Release solution build: PASS, 0 warnings/errors.
- Focused discrepancy service tests: 3/3 PASS.
- Application non-SQL: 286/286 PASS.
- API non-SQL: 150/150 PASS.
- Frontend: 53/53 PASS; lint PASS; production build PASS.
- Migration was generated from the current model; final pending-model and SQL apply/rollback gates remain required.

The full owned-SQL Application/API suites and browser full-stack are not run because `ERP_KHO_SQLSERVER_ADMIN_CONNECTION` is absent from Process, User and Machine environment scopes. No isolated database was created. Historical QC SQL/browser runs are not evidence for this slice.

## Deferred

Putaway/location movement, permission-code migration, supplier/carrier claim execution and laboratory/evidence storage remain outside this slice. Evidence references are stored; an attachment engine is not simulated.

## Fresh closure verification — 2026-09-24

**RECEIVING DISCREPANCY READY FOR OWNER REVIEW.** The earlier SQL-admin and browser-coverage blockers above are superseded historical checkpoints.

Cất hàng closure preserves the discrepancy custody boundary: Door Rejected creates no stock or task item, while only persisted FinalReceived status buckets create RECEIVING balances and Cất hàng items at Post.

- Canonical Notion sources were re-read and remained unchanged: 01 `2026-09-23T15:38:52.543Z`, 17 `15:38:54.657Z`, 20 `15:38:55.859Z`, 34 `15:38:57.552Z`, 41 `2026-09-20T16:31:54.616Z`, 71 `2026-09-23T15:38:59.281Z`, 84 `15:39:00.941Z`, 228 `15:39:01.746Z`, 229 `2026-09-19T11:26:10.775Z`, 282 `2026-09-23T15:39:04.510Z`, and the discrepancy row `15:39:06.751Z`.
- Drive metadata remained root 140, Corrected 16, Enriched 24, Merged/Split 56, New Screens 10 and Deprecated 176. Artifact `1VgvBwWHLgKqAaqqJBwpoSs5slQCMLRBk` remained unchanged and metadata-only `NOT REVIEWED`.
- A browser-discovered alternate-UOM defect was fixed at the service boundary: raw observations retain their observed UOM, workflow quantities are normalized to the receipt operation UOM, and Base UOM values use the persisted receipt-line conversion snapshot. Invalid unit and precision inputs return 400. Regression coverage raises focused discrepancy tests to 5/5.
- Application owned-SQL passed 338/338, Run `275fd4239b3342c0a1f1bb497a14655c`; its marker-owned database was cleaned. API SQL/HTTP passed 156/156 using isolated self-cleaning databases. Frontend passed 53/53; lint and production build passed.
- Browser Runs `cb87071aca7a470685a099336fe57c3e` and `6ac961abc1ba44709f9ebb5b0d1edb96` close the five browser-only gaps. They prove `24 EA = 2 BOX`, persisted `24 EA` Base quantity at Post despite a later master conversion change, non-zero tolerance auto-resolution, same-key replay `200/200`, fingerprint mismatch `409`, concurrent terminal action `200/409`, raw Viewer filtering, and warehouse isolation `404` for read and every mutation/replay.
- Database evidence for the final UOM run shows one AVAILABLE transaction of 24 Base EA, one `ImportReceipt.Posted` audit, no invalid-observation discrepancy rows, and no pre-Post stock/ledger effect. Both BrowserQA databases, credentials and owned processes were cleaned after marker verification; `ERP_KHO` remained ONLINE without business-table access.

## Cất hàng compatibility — 2026-09-27

Only Final Received quantities accepted into custody reach Post and automatic Cất hàng items. Door Rejected remains excluded. Observation/resolution/approval remain inventory-neutral; Cất hàng begins only after the existing Post boundary.
