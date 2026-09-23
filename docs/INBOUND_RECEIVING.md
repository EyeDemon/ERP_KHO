# Inbound Receiving and Posting

## Closure freshness evidence — 2026-09-20

Checked directly through the connected Notion workspace at `2026-09-20T03:55Z`. `UNCHANGED` below means the provider timestamp exactly matches the stored baseline. The root page's previously stored value was the checkpoint's "as of" timestamp rather than its native edit timestamp, so its status cannot be reconstructed safely.

| Notion source | Checked at | Current last edited/version | Stored baseline | Change status | Relevant change | Impact |
|---|---|---|---|---|---|---|
| ERP WMS – Tài liệu Thiết kế Kỹ thuật Hệ thống | 2026-09-20T03:55Z | 2026-09-17T20:20:06.707Z; v1.0 | 2026-09-19T11:26:26.650Z; v1.0 | CHANGE STATUS UNKNOWN | Stored baseline used the fetch/checkpoint timestamp, not native metadata | No quantity/UOM conflict found; baseline metadata corrected prospectively |
| 01. Business Rules & State Machine Specification | 2026-09-20T03:55Z | 2026-09-17T19:03:14.380Z | 2026-09-17T19:03:14.380Z | UNCHANGED | None | None |
| 17. Permission Registry | 2026-09-20T03:55Z | 2026-09-17T19:40:55.996Z | 2026-09-17T19:40:55.996Z | UNCHANGED | None | Role-backed policies remain the implemented mapping; permission-code migration remains separate |
| 29. Inventory Ledger Posting Algorithm | 2026-09-20T03:55Z | 2026-09-17T19:47:03.149Z | 2026-09-17T19:47:03.149Z | UNCHANGED | None | None |
| 34. Goods Receipt Posting & Inbound Execution | 2026-09-20T03:55Z | 2026-09-17T19:49:07.648Z | 2026-09-17T19:49:07.648Z | UNCHANGED | None | None |
| 41. QC Hold & Quarantine | 2026-09-20T03:55Z | 2026-09-17T19:50:14.902Z | 2026-09-17T19:50:14.902Z | UNCHANGED | None | QC remains outside this slice |
| 84. Master Data Governance | 2026-09-20T03:55Z | 2026-09-17T20:01:13.919Z | 2026-09-17T20:01:13.919Z | UNCHANGED | None | None |
| 162. ERP Master Data Synchronization | 2026-09-20T03:55Z | 2026-09-17T20:21:11.335Z | 2026-09-17T20:21:11.335Z | UNCHANGED | None | None |
| 228. UX Specification by Core Business Flow | 2026-09-20T03:55Z | 2026-09-17T20:32:34.688Z | 2026-09-17T20:32:34.688Z | UNCHANGED | None | None |
| 229. Screen Inventory & Coverage Matrix | 2026-09-20T03:55Z | 2026-09-19T11:26:10.775Z | 2026-09-19T11:26:10.775Z | UNCHANGED | None | No bulk matrix update made |
| 282. UX Governance & Screen Matrix handoff | 2026-09-20T03:55Z | 2026-09-19T11:26:26.650Z | 2026-09-19T11:26:26.650Z | UNCHANGED | None | None |
| INB-RECEIVING-WORKBENCH | 2026-09-20T03:55Z | 2026-09-19T20:26:15.435Z; ZIP6-2026-09-19 | 2026-09-19T15:40:03.529Z; ZIP6-2026-09-19 | CHANGED | Row now links corrected/enriched Drive artifact; visual QA conflict marked resolved | No state/API/UOM conflict |
| INB-RECEIPT-DETAIL | 2026-09-20T03:55Z | 2026-09-19T20:25:42.539Z; ZIP6-2026-09-19 | 2026-09-19T15:40:01.492Z; ZIP6-2026-09-19 | CHANGED | Row now links corrected Drive artifact; visual QA conflict marked resolved | No state/API/UOM conflict |
| INB-RECEIPT-POST-CONFIRM | 2026-09-20T03:55Z | 2026-09-19T20:26:19.729Z; ZIP6-2026-09-19 | 2026-09-19T15:40:13.000Z; ZIP6-2026-09-19 | CHANGED | Row now links enriched Drive artifact and retains READY_TO_POST/idempotency contract | No implementation conflict |

## Dynamic Google Drive artifact baseline

Source: `https://drive.google.com/drive/folders/10qedIMZCw-_ZVjGbXVENqiBZQfqeBGKI`, checked recursively at `2026-09-20T04:06Z`. Provider pagination was followed rather than inferring totals from the 100-item folder listing cap. The root has 140 direct PNG files; the five child folders contain 13 (`01_Corrected`), 24 (`02_Enriched`), 56 (`03_Merged_and_Split`), 9 (`04_New_Screens`), and 136 (`00_Deprecated_Reference`) PNG files. No additional child folder was present. These counts overlap by design history and do not replace the historical ZIP count. Drive remains `Illustrative UI/UX Reference`; `00_Deprecated_Reference` is excluded from current design authority.

| File ID | Filename | Parent folder | MIME type | Size | Modified time | Previous baseline | Change status | Review status |
|---|---|---|---|---:|---|---|---|---|
| `1KCu8otBkkA9hfYlRbFi_BEQh76KPJsTs` | Nhận hàng (Receiving Workbench)_v2_enriched.png | 02_Enriched | image/png | 1,306,029 | 2026-09-19T19:47:04.872Z | No prior Drive manifest; Notion row previously referenced ZIP6 | STATUS UNKNOWN | REVIEWED 2026-09-20 |
| `14afMcuGrBOe1F2TkEM6trdCG-l_TLf7g` | Chi tiết phiếu nhập_v2_corrected.png | 01_Corrected | image/png | 1,556,166 | 2026-09-19T18:56:39.013Z | No prior Drive manifest; Notion row previously referenced ZIP6 | STATUS UNKNOWN | REVIEWED 2026-09-20 |
| `14QiquYq8qjBvA-OTbMaFC3k1M3W8gsHv` | Modal xác nhận post receipt_v2_enriched.png | 02_Enriched | image/png | 1,312,433 | 2026-09-19T19:22:57.899Z | No prior Drive manifest; Notion row previously referenced ZIP6 | STATUS UNKNOWN | REVIEWED 2026-09-20 |
Only the first three files were actually opened. The workbench image shows receiving quantities/UOM, the detail image includes a broader QC state chain, and the post modal describes the inventory effect. The QC elements are outside the executable no-QC slice and do not override Notion or API contracts.

## Specification freshness evidence

Checked directly through the connected Notion workspace at `2026-09-19T16:36:38Z`. The previous implementation checkpoint did not persist comparable Notion `page_last_edited_at` values, so recency alone cannot prove that a page is unchanged; all accessible sources are therefore recorded as `CHANGE STATUS UNKNOWN`.

| Notion source | Checked at | Last edited/version | Previous baseline | Change status | Relevant change | Impact |
|---|---|---|---|---|---|---|
| ERP WMS – Tài liệu Thiết kế Kỹ thuật Hệ thống | 2026-09-19T16:36:38Z | 2026-09-19T11:26:26.650Z; v1.0 | Content reviewed previously; edit timestamp not recorded | CHANGE STATUS UNKNOWN | Defines Product Base UOM, Product UOM conversion, receipt quantities, and Received ≠ Posted | No conflict; requires explicit quantity/UOM contract |
| 01. Business Rules & State Machine Specification | 2026-09-19T16:36:38Z | 2026-09-17T19:03:14.380Z | Edit timestamp not recorded | CHANGE STATUS UNKNOWN | `RECEIVED → READY_TO_POST → POSTED`; posting example separates available, QC hold, and rejected quantities | Foundation remains compatible |
| 17. Permission Registry | 2026-09-19T16:36:38Z | 2026-09-17T19:40:55.996Z | Edit timestamp not recorded | CHANGE STATUS UNKNOWN | `receipt.read/receive/complete/post`, `uom.read`, `product_uom.manage` | Current role policies remain an implementation mapping, not the target permission model |
| 29. Inventory Ledger Posting Algorithm | 2026-09-19T16:36:38Z | 2026-09-17T19:47:03.149Z | Edit timestamp not recorded | CHANGE STATUS UNKNOWN | Ledger and balance projection must commit atomically | No conflict |
| 34. Goods Receipt Posting & Inbound Execution | 2026-09-19T16:36:38Z | 2026-09-17T19:49:07.648Z | Edit timestamp not recorded | CHANGE STATUS UNKNOWN | Confirms ordered/expected/received/accepted/rejected/posted quantities and base-UOM posting | Quantity/UOM slice may proceed with no-QC boundary |
| 41. QC Hold & Quarantine | 2026-09-19T16:36:38Z | 2026-09-17T19:50:14.902Z | Edit timestamp not recorded | CHANGE STATUS UNKNOWN | Damaged/QC quantities require inventory-status dimensions | QC and non-available stock remain out of scope |
| 84. Master Data Governance | 2026-09-19T16:36:38Z | 2026-09-17T20:01:13.919Z | Not captured previously | CHANGE STATUS UNKNOWN | Conversion must be positive and historical semantics must be snapshotted | Snapshot conversion on receipt lines |
| 162. ERP Master Data Synchronization | 2026-09-19T16:36:38Z | 2026-09-17T20:21:11.335Z | Not captured previously | CHANGE STATUS UNKNOWN | Historical conversion changes need effective-date control | Persist conversion version/result before posting |
| 228. UX Specification by Core Business Flow | 2026-09-19T16:36:38Z | 2026-09-17T20:32:34.688Z | Edit timestamp not recorded | CHANGE STATUS UNKNOWN | Expected → Receive → Inspect → Disposition → Post | No-QC slice stops before QC features |
| 229. Screen Inventory & Coverage Matrix | 2026-09-19T16:36:38Z | 2026-09-19T11:26:10.775Z | Edit timestamp not recorded | CHANGE STATUS UNKNOWN | Flags ambiguous quantity/UOM and missing READY_TO_POST visibility | UI must show operation and base quantities |
| 282. UX Governance & Screen Matrix handoff | 2026-09-19T16:36:38Z | 2026-09-19T11:26:26.650Z | Edit timestamp not recorded | CHANGE STATUS UNKNOWN | Operator enters operation UOM; system shows Base UOM conversion from governed master/version | Binding rule for this slice |
| INB-RECEIVING-WORKBENCH | 2026-09-19T16:36:38Z | 2026-09-19T15:40:03.529Z; ZIP6-2026-09-19 | Row timestamp not recorded | CHANGE STATUS UNKNOWN | Calls out missing operation-to-base conversion | No conflict |
| INB-RECEIPT-DETAIL | 2026-09-19T16:36:38Z | 2026-09-19T15:40:01.492Z; ZIP6-2026-09-19 | Row timestamp not recorded | CHANGE STATUS UNKNOWN | Requires explicit receipt states and action permissions | No conflict |
| INB-RECEIPT-POST-CONFIRM | 2026-09-19T16:36:38Z | 2026-09-19T15:40:13.000Z; ZIP6-2026-09-19 | Row timestamp not recorded | CHANGE STATUS UNKNOWN | POST precondition READY_TO_POST and idempotency key | No conflict |

No fetched source conflicts with the current foundation. Notion does not specify a rounding mode. The safe implementation rule is to reject a conversion result that exceeds Base UOM precision rather than silently round it.

## Artifact evidence

- Source checked: `C:\Users\van40\Downloads\Ảnh_Chức_Năng.zip` and extracted folder `C:\Users\van40\Downloads\Ảnh_Chức_Năng`.
- ZIP SHA-256: `3AE991A71E8DBDFCCBC4C9B6F345CB96BA882B823A49592D1FD399424AF0A90F`; last write `2026-09-19T13:04:05.6351863Z`.
- Actual source count: 169 ZIP entries, 168 PNG entries; extracted folder contains 168 PNG files. This supersedes the older 154-image statement.
- Opened directly in this review: 13 images at `2026-09-19T16:36:38Z`: `ASN.png`, `Receiving Appointment.png`, `Tạo phiếu nhập kho.png`, `Chi tiết phiếu nhập.png`, `Phiếu nhập kho.png`, `Nhận hàng (Receiving Workbench).png`, both QC/no-QC receipt variants, `Kiểm tra chất lượng(QC).png`, `Modal xác nhận post receipt.png`, `Modal xử lý sai lệch nhận hàng.png`, `Danh sách cất hàng(Putaway).png`, and `Mobile Receiving.png`.
- The remaining 155 PNG files were counted but were not opened in this review. The ZIP and images remain outside Git.

## Sources reviewed

- Notion 01, Business Rules & State Machine Specification: Goods Receipt and command-processing sections.
- Notion 17, Permission Registry: `receipt.read/create/update/receive/complete/post/cancel` and warehouse scope.
- Notion 29, Inventory Ledger Posting Algorithm: atomic ledger and balance projection.
- Notion 34, Goods Receipt Posting & Inbound Execution: `DRAFT → RECEIVING → RECEIVED → QC_PENDING? → READY_TO_POST → POSTED`.
- Notion 35, Putaway Engine; 41, QC Hold & Quarantine; 228, Inbound UX flow; 229/282 UX governance.
- Screen Matrix rows `INB-RECEIVING-WORKBENCH` and `INB-RECEIPT-POST-CONFIRM`.
- Thirteen Inbound concept images previously reviewed directly from `Ảnh_Chức_Năng.zip`; these are illustrative references, not authoritative designs.

## Current implementation and this slice

Before this slice, Import Receipt used `Draft → Approved`; approval updated stock and wrote an Inventory Transaction. That made approval equivalent to posting.

This slice establishes the executable boundary:

```text
Draft --receive--> Received --checker approval--> ReadyToPost --post--> Posted
```

- `receive` validates persisted lines and records no inventory mutation.
- checker approval records the supplier snapshot and readiness decision; it records no inventory mutation.
- `post` alone updates stock and creates the receipt ledger transaction in the existing unit-of-work transaction.
- all commands retain idempotency middleware, warehouse authorization and optimistic status concurrency.
- historical `Approved` rows remain readable and are labelled as legacy data; they are not silently migrated.

`Cancelled` remains available only from `Draft`. The shared enum values used by Export Receipts and Stocktakes are unchanged; new Import states were appended.

## Foundation code evidence

| Claim | Code evidence | Fresh verification |
|---|---|---|
| State values and legacy stability | `ERP.Domain/Enums/ReceiptStatus.cs`; legacy numeric values 0–3 unchanged, new states appended as 4–6 | source review + SQL suite |
| Receive and approval do not post | `ImportReceiptService.ReceiveAsync`, `ApproveImportReceiptAsync` | application tests |
| Post is the inventory boundary | `ImportReceiptService.PostAsync` writes `InventoryStock`, `InventoryTransaction`, then `Posted` inside the unit of work | application + concurrency tests |
| Warehouse filtering | `ImportReceiptRepository.GetAllWithDetailsAsync` and `GetByIdWithDetailsAsync`; command services call `EnsureWarehouseAccessAsync` | source review + API suite |
| Idempotency | all create/receive/approve/post/cancel actions carry `IdempotentCommand`; filter keys by user, command scope, key hash and request fingerprint | API suite |
| Concurrency | `[ConcurrencyCheck]` on receipt status and stock quantity; losing post transaction rolls back | application SQL suite |
| Separation of duties | `ApprovalSafetyGuard` rejects maker as checker and maker as poster | application tests |
| Supplier history | supplier code/name snapshot occurs before READY_TO_POST | application tests |

Fresh baseline checks run in this review: solution build passed with 0 warnings/errors; owned SQL Run ID `7cae4e33d75e4940a2df288bb5fcc90d` passed Application 325/325 and API 154/154, then removed its owned database; frontend passed 50/50, lint, and production build. Earlier checkpoint counts are inherited evidence and are not used as the fresh run.

Foundation findings found and fixed before extending the slice: failed detail/command loads now clear stale receipt data; mutation guards use synchronous refs; `UnitPrice` is filtered server-side for Viewer; and the shared approval queue/reject path now treats Import Receipt `Received`, rather than `Draft`, as pending approval. These are implementation findings from this review, not independent review findings.

## Quantity and UOM audit

| Capability | Current code evidence | Notion rule | Gap | Decision |
|---|---|---|---|---|
| Quantity semantics | `ImportReceiptDetail.Quantity` is used at create, receive validation, post stock, and ledger | separate expected/received/accepted/rejected/posted quantities | one ambiguous field | preserve legacy column; add explicit fields and stop exposing ambiguous API `quantity` for new writes |
| Product Base UOM | `Product.UnitId` is the only product unit | Product has Base UOM | naming is implicit | treat current `UnitId` as Base UOM without changing existing FK |
| Multiple product UOMs | no entity/table | `product_uoms`, conversion > 0 | absent | add minimal governed conversion table and seed base factor 1 |
| Conversion history | none | snapshot transaction semantics | live master could change | snapshot UOM IDs/codes, factor/version, and computed base quantities on receipt line |
| Precision | decimal(18,4); Unit has no decimal places | UOM has decimal places; source does not define rounding mode | implicit database rounding risk | add precision metadata and reject excess precision; do not invent rounding |
| No-QC buckets | no buckets | expected/received/accepted/rejected/posted | absent | accepted must equal received; damaged/rejected must remain zero until status-dimension/QC work exists |
| Posting | uses live ambiguous `Quantity` | post normalized Base UOM quantity | unstable | post persisted Base Accepted Quantity only |
| Legacy rows | only `Quantity` and product Unit | no guessed historical conversion | migration required | backfill factor 1 because the old model had exactly one product unit; retain legacy `Approved` state |
| Cost visibility | `UnitPrice` always maps to response | server-side field authorization | leak to Viewer | omit cost server-side unless current implemented Admin/Manager mapping permits it |

## Implemented quantity and UOM contract

- New writes use explicit expected, received, accepted, damaged, rejected and posted fields. Legacy `Quantity` remains only for storage/source compatibility and is not exposed as the new write contract.
- `Product.UnitId` is the Base UOM. `ProductUom` versions alternate operation UOM conversions; factor must be positive.
- Receipt creation snapshots operation/Base UOM IDs, codes, decimal precision, conversion factor/version, and Base Expected Quantity. Receive persists Base Received/Accepted quantities from that snapshot. Post consumes the persisted Base Accepted Quantity and never reads live conversion master data.
- No-QC requires `accepted + damaged + rejected = received`, then rejects non-zero damaged/rejected because inventory status/QC disposition is outside this slice. This prevents rejected or damaged stock from increasing available stock.
- Values beyond the configured operation/Base UOM precision are rejected server-side. Trailing-zero decimal scale is accepted; no silent rounding is performed.
- Migration `20260919164747_AddInboundQuantityAndUomSnapshots` backfills legacy rows with factor/version 1 only because the old schema had exactly one product Unit. It does not infer historical alternate-UOM conversions.

## Deliberately deferred gaps

Lot/serial dimensions, QC inspection aggregate, location-level balance, outbox, Putaway task aggregate, and Product UOM administration UI remain outside this slice. No-QC is the only executable path; the QC path remains documented but unavailable.

Product, unit and warehouse names use current master data. Only supplier Code/Name is snapshotted after Draft, so historical reconstruction is limited accordingly. Unit price remains confidential operational data and is not added to the new command responses.

## API and authorization

| Command | Endpoint | Required gate | Inventory effect |
|---|---|---|---|
| Complete receiving | `POST /api/importreceipts/{id}/receive` | authenticated Admin/Manager + warehouse access | none |
| Approve readiness | `POST /api/importreceipts/{id}/approve` | shared checker policy + warehouse access + maker/checker separation | none |
| Post receipt | `POST /api/importreceipts/{id}/post` | shared checker policy + warehouse access + maker/poster separation | stock and ledger exactly once |

The deployment still uses role-backed API policies. Mapping these to the Notion permission codes `receipt.receive`, `receipt.complete`, and `receipt.post` is the next authorization migration; the backend gates remain authoritative.

## Error, concurrency and idempotency rules

- Invalid state returns a business conflict and performs no inventory mutation.
- Repeated requests use `Idempotency-Key`; a concurrent transition also loses the status concurrency race.
- UI buttons enter a disabled/loading state while requests are pending and refresh persisted backend state afterward.
- A failed load or command does not invent a client-side state transition.

## Test matrix

- receive from Draft and reject other states;
- verify receive and approval do not change stock or ledger;
- post only from ReadyToPost;
- repeated/concurrent post creates one inventory effect;
- rollback leaves document, stock and ledger consistent;
- warehouse isolation and maker/checker policy;
- frontend loading, error and double-submit guards;
- SQL integration and browser full-stack path using an owned synthetic database.

Fresh completion evidence on `2026-09-20`:

- solution Release build: 0 warnings, 0 errors;
- owned SQL Application Run ID `9d9266a302004ad7b0e73b9cca31db2f`: 328/328, owned database removed;
- API SQL suite: 154/154 (`TestResults/SqlIntegration/api-final-20260920/api-final-2.trx`); each test creates and removes only its ownership-verified database;
- frontend: 50/50, lint PASS, production build PASS;
- browser full-stack Run ID `6164fd03188442409aff53e9d7dbfae5`: `2 BOX × 12 = 24 EA`; receive left stock/ledger unchanged and a double click produced one receive audit; post produced one ledger row of 24 EA and stock 24 EA; processes, database, and synthetic credential were removed by the ownership-checked cleanup script.

## Next gaps

1. Receiving discrepancy and reason-code workflow.
2. Putaway/location-level internal movement.
3. Permission-code migration as a separate sprint when prioritized.
4. Rejected-at-door workflow.
5. Laboratory/evidence engine if prioritized.

## Closure verification — 2026-09-20

- HTTP cost filtering: fresh SQL-backed API tests verify Admin/Manager receive `unitPrice`, while Viewer list contains no cost property and Viewer detail serializes `unitPrice: null`; the raw 404 response contains no receipt, supplier, or price data.
- HTTP warehouse isolation: a Manager scoped only to Warehouse A receives 404 for Warehouse B detail/receive/approve/post and replay; Warehouse B is absent from list results. Receipt states, stock, ledger, audit and idempotency records remain unchanged.
- Browser full-stack Run ID `82db12c6d17b4765af8d2e7061688d8c`: retry with the same key returned 200/200 and posted 11 Base UOM once; concurrent distinct keys returned 200/400 and posted 13 Base UOM once; UI double-click posted 17 Base UOM once. Each receipt ended `Posted` with one ledger row and one post audit. Final stock was 141 Base UOM: 100 seed + 11 retry + 13 concurrent + 17 UI.
- Fresh suites: Release solution build 0 warnings/errors; Application SQL 328/328 (Run ID `1b84aab588f54bb49ca1e2b51fd3b411`); API SQL 156/156; frontend 52/52; lint PASS; production build PASS; EF pending-model check PASS.
- Cleanup: the browser Run ID database, API/frontend processes, synthetic credential and temporary browser page were removed. Interrupted owned Application run databases were ownership-verified and removed. Database `ERP_KHO` remained ONLINE; no business data was read or written.

## QC disposition audit checkpoint — 2026-09-20

**SUPERSEDED HISTORICAL CHECKPOINT.** This section records the specification conflict that existed before the canonical contract was corrected. The current implementation and verification state is recorded below.

The branch `feature/inbound-qc-disposition` was created from accepted commit `9261205c6790cb9b8d8c3a04155ef0cdb3673933`. No QC schema or state mutation was added because the current authoritative specifications disagree about whether QC occurs before or after receipt posting.

| Notion source | Checked at | Current version | Stored baseline | Change status | Relevant change | Impact |
|---|---|---|---|---|---|---|
| 01. Business Rules & State Machine | 2026-09-20T16:00Z | 2026-09-17T19:03:14.380Z | same | UNCHANGED | `RECEIVED → QC_PENDING → QC_COMPLETED → READY_TO_POST`; POST writes AVAILABLE/QC_HOLD/REJECTED buckets | Requires completed QC before Post |
| 17. Permission Registry | 2026-09-20T16:00Z | 2026-09-17T19:40:55.996Z | same | UNCHANGED | `receipt.complete`, `quality_inspection.execute/complete`, `receipt.post` | Target permission codes remain unimplemented role-backed targets |
| 29. Inventory Ledger Posting Algorithm | 2026-09-20T16:00Z | 2026-09-17T19:47:03.149Z | same | UNCHANGED | Ledger/balance atomicity and status dimension | Current stock model lacks status/location dimensions |
| 34. Goods Receipt Posting | 2026-09-20T16:00Z | 2026-09-17T19:49:07.648Z | same | UNCHANGED | QC requirement must be satisfied before READY_TO_POST; Post routes quantities by status | Supports pre-Post QC |
| 35. Putaway Engine | 2026-09-20T16:00Z | 2026-09-17T19:49:07.648Z | no stored baseline | CHANGE STATUS UNKNOWN | Putaway is a post-receipt internal move | No Putaway implementation in this sprint |
| 41. Inventory Status, QC Hold & Quarantine | 2026-09-20T16:00Z | 2026-09-17T19:50:14.902Z | same | UNCHANGED | `Receipt POST → QC_HOLD → QC PASS/FAIL` | Conflicts with 01/34 and the current QC screen row |
| 71. Exception Management | 2026-09-20T16:00Z | 2026-09-17T19:58:47.518Z | no stored baseline | CHANGE STATUS UNKNOWN | Disposition resolution needs reason/note/evidence per policy | Reason/evidence policy is not concrete enough for schema |
| 84. Master Data Governance | 2026-09-20T16:00Z | 2026-09-17T20:01:13.919Z | same | UNCHANGED | Inventory-status flags and reason codes are governed master data | Do not encode statuses as receipt booleans |
| 162. ERP Master Data Synchronization | 2026-09-20T16:00Z | 2026-09-17T20:21:11.335Z | same | UNCHANGED | Master changes must preserve transaction history | QC rule source/version remains unspecified |
| 228. UX Core Business Flow | 2026-09-20T16:00Z | 2026-09-17T20:32:34.688Z | same | UNCHANGED | Receipt → QC → Post → Putaway | Supports pre-Post QC |
| 229. Screen Matrix | 2026-09-20T16:00Z | 2026-09-19T11:26:10.775Z | same | UNCHANGED | Current rows remain illustrative governance | No bulk update made |
| 282. UX Governance handoff | 2026-09-20T16:00Z | 2026-09-19T11:26:26.650Z | same | UNCHANGED | Explicit QC/no-QC split; Putaway after Post | Supports pre-Post QC |
| INB-RECEIVING-WORKBENCH | 2026-09-20T16:00Z | 2026-09-20T15:24:36.707Z | 2026-09-19T20:26:15.435Z | CHANGED | Updated artifact and UOM QA metadata; commands still receive/complete | No new QC contract |
| INB-RECEIPT-DETAIL | 2026-09-20T16:00Z | 2026-09-19T20:25:42.539Z | same | UNCHANGED | State-driven actions | None |
| INB-RECEIPT-STATE-QC | 2026-09-20T16:00Z | 2026-09-19T15:40:09.724Z | no stored baseline | CHANGE STATUS UNKNOWN | Complete receiving to QC_PENDING; inspection completion to READY_TO_POST | Supports pre-Post QC but does not resolve page 41 |
| INB-RECEIPT-STATE-NO-QC | 2026-09-20T16:00Z | 2026-09-19T15:40:06.588Z | no stored baseline | CHANGE STATUS UNKNOWN | Complete receiving advances to READY_TO_POST | Clear no-QC target |
| INB-RECEIPT-POST-CONFIRM | 2026-09-20T16:00Z | 2026-09-19T20:26:19.729Z | same | UNCHANGED | READY_TO_POST and idempotency required | None |
| INB-RECEIPT-DISCREPANCY | 2026-09-20T16:00Z | 2026-09-19T15:40:17.774Z | no stored baseline | CHANGE STATUS UNKNOWN | Under-receipt example only | Over-receipt and reason workflow remain deferred |

### Drive delta

The dynamic Drive source was enumerated recursively with provider pagination. Current image counts are root 140, `01_Corrected` 16, `02_Enriched` 24, `03_Merged_and_Split` 56, `04_New_Screens` 10, and `00_Deprecated_Reference` 176. Compared with the previous baseline, Corrected increased 13→16, New Screens 9→10, and Deprecated 136→176. The current workbench is file `11No9JgfuXEczp2Rhd_TBXBax4GMpCwZK` (1,258,528 bytes, modified 2026-09-20T15:22:49.125Z); the prior file `1KCu8otBkkA9hfYlRbFi_BEQh76KPJsTs` is now deprecated. The current corrected receipt detail is file `1G3_AsgVijBgt7VCmiwYTFby8L5MMqxBO` (1,326,909 bytes, modified 2026-09-20T05:30:23.695Z). The post-confirm file `14QiquYq8qjBvA-OTbMaFC3k1M3W8gsHv` is unchanged. Metadata was read for all files; only the QC-required and no-QC receipt state images were successfully rendered and visually reviewed in this checkpoint. Other attempted Drive viewers did not render content and remain `NOT REVIEWED`. Drive remains illustrative only.

### Code audit and blocking decision

Current code has no `RequiresQc` snapshot, quality-inspection aggregate, QC states, reason-code relation, inventory-status dimension, location/bin dimension, or status-aware ledger line. `InventoryStock` is only Product + Warehouse + Quantity; `InventoryTransaction` has no status or location. The receipt line already persists expected/received/accepted/damaged/rejected quantities and Base-UOM snapshots, but the no-QC receive command deliberately rejects non-zero damaged/rejected quantities. Post consumes persisted `BaseAcceptedQuantity` and is the only inventory boundary.

The unresolved specification choice changes the schema, command order, inventory total, and migration strategy:

- pre-Post QC: no physical stock exists while `QC_PENDING`; Post creates accepted available and any supported non-available buckets together;
- post-then-QC: Post creates physical stock in `QC_HOLD`; later status-change ledger entries move it to AVAILABLE/QUARANTINE/DAMAGED.

Implementing either model would contradict one current Notion source. Owner/spec-owner must designate the canonical sequence and define whether rejected goods are warehouse On Hand, whether mixed line-level QC is allowed, and where the QC rule/version is snapshotted. Until then this slice is **BLOCKED BY NOTION CHANGE/CONFLICT**.

The independent API error decision is clear: Notion 03 and 18 define stale concurrency and invalid-state conflicts as HTTP 409. The service now preserves `ConcurrencyException` after rollback instead of converting it to a generic 400 business error. A focused Application regression passed 5/5. Full closure suites and browser QA are deferred because the QC implementation is blocked.

## Canonical QC disposition implementation checkpoint — 2026-09-21

Owner decisions were written to Notion before implementation and then fetched again successfully. The former Goods Receipt `Post → QC_HOLD → PASS/FAIL` wording in page 41 is explicitly marked `SUPERSEDED FOR GOODS RECEIPT`; it remains only as a possible future non-canonical exception.

| Notion source | Checked at | Current version after write/read-back | Previous baseline | Change status | Relevant change | Impact |
|---|---|---|---|---|---|---|
| 01. Business Rules & State Machine | 2026-09-20T16:32Z | 2026-09-20T16:31:50.443Z | 2026-09-17T19:03:14.380Z | CHANGED | QC before Post, mixed line QC, quantity invariant and 409 | Canonical state machine established |
| 17. Permission Registry | 2026-09-20T16:32Z | 2026-09-20T16:31:51.292Z | 2026-09-17T19:40:55.996Z | CHANGED | execute/complete QC, approve disposition and post target permissions | Current implementation remains role-backed and documents the gap |
| 29. Inventory Ledger Posting Algorithm | 2026-09-20T16:32Z | 2026-09-17T19:47:03.149Z | same | UNCHANGED | Atomic status-aware ledger remains authoritative | Post transaction covers receipt, balances and ledger |
| 34. Goods Receipt Posting | 2026-09-20T16:32Z | 2026-09-20T16:31:53.872Z | 2026-09-17T19:49:07.648Z | CHANGED | Pre-Post QC and status bucket posting | Implementation follows this sequence |
| 41. Inventory Status, QC Hold & Quarantine | 2026-09-20T16:32Z | 2026-09-20T16:31:54.616Z | 2026-09-17T19:50:14.902Z | CHANGED | Old post-to-QC flow superseded for Goods Receipt; status flags defined | Specification conflict removed |
| 84. Master Data Governance | 2026-09-20T16:32Z | 2026-09-20T16:31:55.831Z | 2026-09-17T20:01:13.919Z | CHANGED | Product–Supplier then Product precedence; line snapshot/version | `QcPolicy` and receipt-line snapshots implement it |
| 162. ERP Master Data Synchronization | 2026-09-20T16:32Z | 2026-09-17T20:21:11.335Z | same | UNCHANGED | Historical snapshots must survive master changes | Post never reads live QC/UOM master data |
| 228. UX Core Business Flow | 2026-09-20T16:32Z | 2026-09-20T16:31:56.622Z | 2026-09-17T20:32:34.688Z | CHANGED | QC/no-QC UI and status-bucket confirmation | UI exposes line QC and bucket totals |
| 229. Screen Matrix | 2026-09-20T16:32Z | 2026-09-19T11:26:10.775Z | same | UNCHANGED | No bulk matrix rewrite | Row updates were limited to the six affected records |
| 282. UX Governance handoff | 2026-09-20T16:32Z | 2026-09-20T16:31:57.510Z | 2026-09-19T11:26:26.650Z | CHANGED | Canonical QC governance | Drive remains illustrative |
| Affected six screen rows | 2026-09-20T16:32Z | 2026-09-20T16:32:13.773Z–16:32:19.232Z | prior row timestamps in the QC checkpoint | CHANGED | Commands, conflicts, revision and QC permission aligned | No contradictory screen-state contract remains |

### Implemented state, policy and quantity contract

```text
Draft --receive--> Received --------------------> ReadyToPost --post--> Posted
                    (all lines no-QC)                 ^
Draft --receive--> QcPending --disposition--> QcCompleted --approve--|
                    (one or more QC lines)
```

- `QcPolicy` versions Product and optional Supplier rules. At receipt creation an active Product–Supplier policy takes precedence over Product policy. No explicit receipt-line override exists.
- Every line snapshots `RequiresQc`, policy ID/version/source/effective time/rule and line state. Receipt-level QC is derived from its lines, so mixed receipts are supported.
- QC lines receive with zero disposition quantities. `record-qc-disposition` accepts partial line batches, requires `Accepted + Damaged + Rejected = Received`, requires a reason code for damaged/rejected quantities, and leaves the receipt `QcPending` until all QC lines complete.
- Receive and QC disposition do not mutate balances or ledger. Approve accepts `Received` or `QcCompleted`; unresolved QC cannot advance or Post.
- Post uses persisted Base UOM buckets: accepted → `AVAILABLE`, damaged → `DAMAGED`, rejected retained → `REJECTED`. This slice does not model rejected-at-door as inventory. `QC_HOLD` and `QUARANTINE` are defined status values but canonical pre-Post QC creates no balance in them.
- Stock uniqueness is Product + Warehouse + InventoryStatus. Reservation, export/decrement, transfer destination, stocktake compatibility and current-stock reporting stay scoped to `AVAILABLE`; non-available buckets cannot be allocated or picked through those paths.
- Migration `20260920163928_AddInboundQcDisposition` assigns all legacy stock and ledger rows status `AVAILABLE`, preserves receipt states and leaves legacy receipt QC snapshots empty/false. No historical QC decision is inferred.
- Invalid/stale state remains HTTP 409. The new QC command uses the existing user/command/key/fingerprint idempotency filter and warehouse authorization. Deployed authorization is still role-backed Admin/Manager plus existing checker/maker separation; Notion permission codes remain targets.

### Drive evidence

The last recursive metadata baseline remains root 140, Corrected 16, Enriched 24, Merged/Split 56, New Screens 10 and Deprecated 176. File IDs and timestamps listed in the preceding Drive checkpoint remain the comparison baseline. The QC-required and no-QC state images were visually reviewed. A raw connector fetch was retried for Post confirmation file `14QiquYq8qjBvA-OTbMaFC3k1M3W8gsHv`; the connector completed but did not return renderable image content, so it remains `NOT REVIEWED` for this checkpoint. No Drive image was committed. Drive remains `Illustrative UI/UX Reference`.

### Verification status

Fresh evidence on the current working tree:

- Release solution build: PASS, 0 warnings and 0 errors.
- Application tests that do not require owned SQL: 282/282 PASS, including Product–Supplier snapshot, mixed receipt, QC disposition invariant, status-bucket posting and pending-QC conflict.
- Frontend: 52/52 PASS; lint PASS; production build PASS.
- EF pending-model check: PASS.
- **Superseded recovery checkpoint:** full Application SQL and API SQL suites were blocked here because the admin connection was not yet supplied to the harness. The later worktree recovery verification below records the fresh owned-SQL results.
- Browser full-stack QC was not started at this historical checkpoint. Database `ERP_KHO` was not accessed.

This checkpoint therefore remains **INBOUND QC DISPOSITION TESTING INCOMPLETE — OWNED SQL ADMIN CONNECTION NOT CONFIGURED**. Implementation, migration, UI, SQL-backed authorization/concurrency and browser evidence must be rerun once the supported admin connection is available.

## Worktree recovery verification — 2026-09-23

The feature worktree was accidentally removed with Shift+Delete after commit `710cfbd635e7f0ae1f2e1362a3b4c27c7156eea4`. Recovery Phases B/C preserved three exact uncommitted patches on drive C:, validated every hunk against `710cfbd`, and identified the migration Designer and documentation as files requiring regeneration. The stale registration was the only item reported by `git worktree prune --dry-run`; it was removed with Git-supported pruning and the worktree was recreated from the unchanged feature ref.

Recovered source now restores Available-only current-stock, reconciliation and historical report queries plus their regression test. Migration `20260921090830_ScopeInventoryReportsToAvailable` was regenerated with repository EF tooling: Up recreates `sp_GetInventoryInOutReport` with exactly two `InventoryStatus = 0` predicates, Down restores the complete prior procedure, the Designer was regenerated, and the model snapshot remained unchanged. The migration source hash matches the Phase C deterministic candidate; no truncated recovery output was copied.

Fresh post-recovery evidence currently available:

- Release solution build: PASS, 0 warnings and 0 errors.
- EF pending-model check: PASS.
- Application owned-SQL suite: 333/333 PASS; Run ID `eee50f950d6e4c27a1e38a48be902c9f`; database `ERP_KHO_Integration_20260923_100927_eee50f95` cleaned after exact marker verification.
- API SQL/HTTP suite: 156/156 PASS. Its six isolated database runs all reported exact owned cleanup; Run IDs are recorded in `INBOUND_QC_DISPOSITION.md`.
- Frontend: 52/52 PASS; lint PASS; production build PASS.
- **SUPERSEDED HISTORICAL BROWSER BLOCKER.** Run ID `59d90fc735634fec8ca124fb8843f94c` ended before interaction and was fully cleaned. Fresh browser closure is recorded below.

Fresh browser Run `2f151b80af5344f09d2fffc5da613b1b` used the official Codex in-app browser, loopback API/frontend, isolated marker-owned SQLEXPRESS database and synthetic users. It passed no-QC, QC accepted, mixed line QC, mixed AVAILABLE/DAMAGED/REJECTED disposition, retry/concurrent QC and Post, warehouse isolation, Viewer filtering, Available-only reservation/export rejection, UI double-click/loading guards and stale 409 clearing. QC-product balances were AVAILABLE 7, DAMAGED 4 and REJECTED 3; no QC_HOLD/QUARANTINE balance was created. Every posted test receipt had one post audit. A discovered zero-bucket Post confirmation bug was fixed by loading authoritative detail after taking the synchronous mutation lock; the post-fix frontend suite remains 52/52.

Post-fix verification: Release build 0 warnings/errors; EF pending-model PASS; Application SQL 333/333 (Run `df61902fa7724bb497598096251b41c6`, owned database cleaned); API SQL/HTTP 156/156; frontend 52/52, lint and production build PASS. Exact BrowserQA cleanup stopped PIDs 4600/18368, removed the synthetic credential and dropped the marker-matched database; no owned QA database remained and `ERP_KHO` was ONLINE. Commit `65a65da` records source, migration and regression changes. Historical browser Run IDs elsewhere remain historical evidence only. Current status is **INBOUND QC DISPOSITION READY FOR OWNER REVIEW**.
