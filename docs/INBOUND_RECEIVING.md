# Inbound Receiving and Posting

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

Foundation findings to fix before extending the slice: receipt detail UI retains stale data after a failed reload; workflow double-submit uses React state rather than a synchronous ref; and `UnitPrice` is returned to Viewer responses even though Notion classifies cost as financial-confidential. These are implementation findings from this review, not independent review findings.

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

## Deliberately deferred gaps

The codebase has no receipt-level expected/received/accepted/damaged/rejected quantities, UOM conversion version, lot/serial dimensions, QC inspection aggregate, location-level balance, outbox, or Putaway task aggregate. Implementing those safely requires schema and migration work beyond this foundation. The current `Quantity` is treated as the persisted received quantity for the all-lines slice. No-QC is the only executable path; the QC path remains documented but unavailable.

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

## Next gaps

Add receipt-line quantity buckets and UOM conversion first, then QC/no-QC branching. Putaway should start from posted inventory in a receiving/QC location and move it internally; it must not be bundled into receipt posting.
