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

1. QC/no-QC branching and inventory-status disposition.
2. Receiving discrepancy and reason-code workflow.
3. Putaway from posted receiving/QC location.
4. Permission-code migration as a separate sprint when prioritized.

## Closure verification — 2026-09-20

- HTTP cost filtering: fresh SQL-backed API tests verify Admin/Manager receive `unitPrice`, while Viewer list contains no cost property and Viewer detail serializes `unitPrice: null`; the raw 404 response contains no receipt, supplier, or price data.
- HTTP warehouse isolation: a Manager scoped only to Warehouse A receives 404 for Warehouse B detail/receive/approve/post and replay; Warehouse B is absent from list results. Receipt states, stock, ledger, audit and idempotency records remain unchanged.
- Browser full-stack Run ID `82db12c6d17b4765af8d2e7061688d8c`: retry with the same key returned 200/200 and posted 11 Base UOM once; concurrent distinct keys returned 200/400 and posted 13 Base UOM once; UI double-click posted 17 Base UOM once. Each receipt ended `Posted` with one ledger row and one post audit. Final stock was 141 Base UOM: 100 seed + 11 retry + 13 concurrent + 17 UI.
- Fresh suites: Release solution build 0 warnings/errors; Application SQL 328/328 (Run ID `1b84aab588f54bb49ca1e2b51fd3b411`); API SQL 156/156; frontend 52/52; lint PASS; production build PASS; EF pending-model check PASS.
- Cleanup: the browser Run ID database, API/frontend processes, synthetic credential and temporary browser page were removed. Interrupted owned Application run databases were ownership-verified and removed. Database `ERP_KHO` remained ONLINE; no business data was read or written.
