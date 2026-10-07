# Rà backlog và lựa chọn slice MVP tiếp theo

Ngày rà backlog: **2026-10-04**; checkpoint reconciliation: **2026-10-07** (Asia/Saigon). Trạng thái hiện hành: **INTEGRATED LOCAL VERIFICATION PASS — SUCCESSOR CI IN NEW DRAFT PR**. Owner đã chốt contract; xem [kế hoạch và authority hiện hành](OUTBOUND_DISPATCH_MVP.md). Phần rà backlog bên dưới là historical checkpoint, superseded tại những đoạn còn chờ quyết định. Đây là báo cáo đối chiếu nguồn và readiness, chưa phải kế hoạch implementation đã được duyệt hoặc evidence của một slice mới.

Source được đối chiếu: `cda05eba56d3cfb41a3df803bb48d0c84e05940f`. [PR #1](https://github.com/EyeDemon/ERP_KHO/pull/1) vẫn Draft; branch đã nghiệm thu, source, index và evidence không thay đổi. [CI 37137026845](https://github.com/EyeDemon/ERP_KHO/actions/runs/37137026845) được đọc lại từ GitHub: completed/success. Counts Application **352**, API **189**, frontend **89** là evidence hiện có tại source này, không phải các lần chạy mới trong lượt rà backlog.

Current slice: `feature/outbound-dispatch-reconciled` giữ ancestry remote `2ba782d` và local checkpoint `e7a9b32`. Public migration được giữ nguyên; corrective migration `20261007074831` thay unpublished local schema, sửa default Staff bundle theo provenance và chặn rollback mất dữ liệu. Application 352/API 206/frontend 100, browser bảy nhóm và focused Approval Center snapshot successor (77efadec…) cùng cleanup PASS; CI cuối được ghi trong Draft PR mới. Chi tiết source hashes, precursor failures, legacy reservation và review trong [outbound evidence](OUTBOUND_DISPATCH_MVP.md#integrated-successor-evidence--2026-10-07). PR #1/source/evidence inbound không đổi.

## Baseline và phương pháp

- Main: `D:\ERP_KHO`, `handoff/approval-complete-20260906`, `699f7a1e7eb338eabdf17666b187a43133ab8af0`.
- Branch đã nghiệm thu: `D:\ERP_KHO-product-category-barcode`, `feature/inbound-permission-code-authorization`, local/remote cùng `cda05eba56d3cfb41a3df803bb48d0c84e05940f`; chỉ `?? .npm-cache/`, index sạch.
- Worktree test `C:\Users\van40\.codex\worktrees\1af9\ERP_KHO` và các thay đổi có sẵn được giữ nguyên.
- Worktree mới, được tạo/đăng ký riêng từ đúng accepted HEAD: `C:\Users\van40\.codex\worktrees\outbound-dispatch-mvp\ERP_KHO`; branch `feature/outbound-dispatch-mvp`. Tên branch chưa thay thế quyết định business model còn chờ.
- Main user-owned paths, literal UNKNOWN cache và helper đã bị policy chặn cleanup không bị sửa/xóa/stage/ignore. Bốn registrations hiện hành hợp lệ; không prune hoặc rewrite lịch sử.

Rà nội bộ bằng Notion trực tiếp và static tracing entity → controller → service → repository → UI → tests. Không gọi đây là independent review, browser QA hoặc production verification. Ponytail giới hạn ở một luồng vận hành, ưu tiên primitive đã có; careful bảo toàn ownership; review kiểm tra boundary; plan-eng-review kiểm tra readiness; QA/QA-only phân loại evidence cần thiết; ui-ux-pro-max đối chiếu trạng thái, phản hồi và accessibility. Không chạy command suy đoán từ tên skill.

## Historical freshness và authority — backlog audit trước addendum

`UNCHANGED` chỉ dùng khi native `page_last_edited_at` khớp baseline đáng tin trong checkpoint. `CHANGE STATUS UNKNOWN` nghĩa là đã đọc nội dung trực tiếp nhưng chưa có native baseline cũ đáng tin; không có nghĩa là chưa đọc. Connector không cung cấp cờ truncation để khẳng định `truncated=false`.

| Nguồn Notion | Native timestamp đọc trực tiếp | So sánh / tác động |
| --- | --- | --- |
| [Root design](https://www.notion.so/3de558fb617480b98a4ae4b7694b3c63) | 2026-09-17T20:20:06.707Z | UNCHANGED; index và mô hình tổng thể |
| [05 — Blueprint/Master Plan](https://www.notion.so/3de558fb6174803ebebbc7b0d5553d7d) | 2026-09-22T03:52:36.827Z | CHANGE STATUS UNKNOWN; §185 commercial overlay ưu tiên Shipment Dispatch sau inbound, WIP = 1 |
| [211 — Roadmap](https://www.notion.so/3de558fb617481f1b9efc9b67d2cfbbd) | 2026-09-17T20:29:14.801Z | CHANGE STATUS UNKNOWN; wave/maturity là design roadmap, không phải trạng thái code |
| [212 — Gap Register](https://www.notion.so/3de558fb61748170a4c3e0d3576e2e96) | 2026-09-17T20:29:14.801Z | CHANGE STATUS UNKNOWN; có framework phân loại, chưa có bảng gap thực tế để coi là backlog đã kiểm chứng |
| [01 — State Machine](https://www.notion.so/3de558fb6174809c9ad3dc5d0dfd377c) | 2026-09-24T16:04:16.627Z | UNCHANGED; không thay boundary nhập kho |
| [17 — Permissions](https://www.notion.so/3de558fb617481738e43c2aed135580a) | 2026-10-03T15:19:58.948Z | UNCHANGED theo QC successor; §33 inbound authority, outbound còn compatibility; §13 có shipment codes nhưng chưa ánh xạ ExportReceipt |
| [18 — Errors](https://www.notion.so/3de558fb61748108a719ee42f0e0cfed) | 2026-09-28T10:04:28.018Z | UNCHANGED; safe 401/403/404/409, Shipment phải được chất hàng trước Dispatch |
| [20 — Reason/Configuration](https://www.notion.so/3de558fb617481fc8881dd60d068781f) | 2026-09-23T15:38:55.860Z | UNCHANGED theo exact native baseline; không dùng bản chép .859 |
| [29 — Ledger](https://www.notion.so/3de558fb617481a7a9dcf97a779e5d5a) | 2026-09-24T16:04:23.927Z | UNCHANGED; immutable movement, receipt boundary và location invariant |
| [34 — Goods Receipt](https://www.notion.so/3de558fb617481e09c78c145fb7a71fd) | 2026-09-24T16:04:27.613Z | UNCHANGED; QC/discrepancy trước Post, Post tạo stock/task atomically |
| [35 — Putaway](https://www.notion.so/3de558fb617481418d64ce37b9419128) | 2026-09-24T16:03:22.053Z | UNCHANGED; MVP locations, availability, UOM snapshots; advanced scope deferred |
| [41 — QC/Status](https://www.notion.so/3de558fb617481bb8836ceeae5382f64) | 2026-09-20T16:31:54.616Z | UNCHANGED; QC-before-Post giữ nguyên |
| [71 — Exceptions](https://www.notion.so/3de558fb61748118ae3fc4fed98cfe87) | 2026-09-23T15:38:59.281Z | UNCHANGED; Receiving Discrepancy override vẫn canonical |
| [84 — Master Governance](https://www.notion.so/3de558fb617481d5b771c17162c9462b) | 2026-09-24T16:04:32.570Z | UNCHANGED; snapshot, reference và location governance |
| [162 — Master Sync](https://www.notion.so/3de558fb61748146a2ead1d83b5a1a24) | 2026-09-24T16:04:36.075Z | UNCHANGED; không đổi master để tính lại lịch sử |
| [228 — UX Flow](https://www.notion.so/3de558fb617481f8986cf1eb92970e55) | 2026-09-24T16:04:39.625Z | UNCHANGED; outbound có reserve/allocate/pick/pack/load/dispatch |
| [229 — Screen Matrix](https://www.notion.so/3de558fb617481e683aeca0f8def3e96) | 2026-09-24T16:04:50.257Z | UNCHANGED; implementation status cũ được đối chiếu source, không tự coi Unknown là thiếu code |
| [282 — UX Governance](https://www.notion.so/3e0558fb61748146a490ed8f32142203) | 2026-09-28T10:04:39.216Z | UNCHANGED; tiếng Việt, snapshot quantity, sensitive filtering |
| [30 — Reservation/Allocation](https://www.notion.so/3de558fb6174817d89fdffa036973301) | 2026-09-17T19:47:03.149Z | CHANGE STATUS UNKNOWN; allocation là reference cụ thể, không giảm Available lần hai |
| [36 — Picking](https://www.notion.so/3de558fb617481678a91d8ab25c0635c) | 2026-09-17T19:49:07.648Z | CHANGE STATUS UNKNOWN; cần allocation, picking không trừ On Hand |
| [37 — Packing/HU](https://www.notion.so/3de558fb61748107ade5e201ac42abd6) | 2026-09-17T19:49:07.648Z | CHANGE STATUS UNKNOWN; cần picked content/HU traceability |
| [38 — Shipment](https://www.notion.so/3de558fb617481de93f6dfa9ff96393d) | 2026-09-17T19:49:07.648Z | CHANGE STATUS UNKNOWN; Dispatch chỉ từ LOADED; stock/ledger/reservation/allocation/audit/outbox cùng transaction |
| [32 — Reversal](https://www.notion.so/3de558fb617481e88822e1dc1ea54d05) | 2026-09-17T19:47:03.149Z | CHANGE STATUS UNKNOWN; inverse immutable transaction, không sửa chứng từ gốc |
| [77 — Inventory Locks](https://www.notion.so/3de558fb617481c59d8ce2d8a70ee619) | 2026-09-17T20:00:05.759Z | CHANGE STATUS UNKNOWN; nhiều scope/rule, không đồng nhất với Location.IsBlocked |
| [88 — Availability](https://www.notion.so/3de558fb617481f880f3da6aee72b3ce) | 2026-09-17T20:02:53.372Z | CHANGE STATUS UNKNOWN; physical On Hand khác eligible operational stock |

Các screen rows đọc trực tiếp: ASN `2026-09-19T20:25:59.504Z`, Appointment `2026-09-20T07:22:38.424Z`, Reservation `2026-09-19T15:30:22.613Z`, Allocation `2026-09-19T15:30:24.230Z`, Dispatch `2026-09-19T14:54:37.404Z`. Không có native baseline cũ đáng tin cho các row này: **CHANGE STATUS UNKNOWN**. Row ASN/Appointment/Dispatch còn implementation Unknown; source export/reservation có thật, nên không lấy nhãn đó làm bằng chứng duy nhất. Screen Matrix vẫn có wording implementation Putaway chưa bắt đầu, đã stale so với [accepted module evidence](INBOUND_PUTAWAY_LOCATION_MOVEMENT.md). Không sửa Notion trong lượt này.

Drive đã đi hết pagination và kiểm tra folder children: **140 ảnh root + 5 folders = 145 entries**; Corrected **16**, Enriched **24**, Merged/Split **56**, New Screens **10**, Deprecated **176** ảnh. Không có folder con mới/nested folder được trả về. Counts khớp baseline; root và Deprecated có hai image pages. Dispatch artifact `1i26t55QOKsjB-fyq7yUHain7lrxsQZKg` và danh sách/tạo phiếu xuất chỉ kiểm tra metadata: **NOT REVIEWED**. Drive là Illustrative UI/UX Reference, không định nghĩa quyền hoặc inventory boundary.

## Ma trận backlog đối chiếu source

Ưu tiên: **P0** là quyết định/dependency cần trước coding; **P1** là candidate vận hành tiếp theo; **P2** cần workflow/customer proof hoặc sequencing; **Deferred** theo owner/commercial overlay. Evidence đã nghiệm thu được dẫn chiếu, không chạy lại để tăng số lần PASS.

| Capability | Contract | Source / API / UI thực tế | Evidence | Gap | Dependency | Priority | Classification |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Category/Barcode/Product UOM | 84/162, 17 §33 | Masters, exact lookup, independent actions, product UOM snapshots | [Category/Barcode](PRODUCT_CATEGORY_BARCODE.md), accepted PR/CI | Không có finding mới trong lượt này | Masters cho inbound/outbound | Retain | Implemented trong phạm vi đã nghiệm thu |
| Partner/receipt print | 84/162, 228/282 | Customer/supplier snapshots, scoped fresh print, Vietnamese state mappings | [Partners](BUSINESS_PARTNERS.md), [Printing](RECEIPT_PRINTING.md), accepted matrix | Không mở lại fixes đã đóng | Product/Warehouse/Partner | Retain | Implemented; physical printer certification ngoài evidence |
| Receive/QC/discrepancy/Post | 01/17/20/29/34/41/71 | ImportReceipts controller/shared services/UI; conditional QC capabilities; Post-only inventory | [Receiving](INBOUND_RECEIVING.md), [QC](INBOUND_QC_DISPOSITION.md), [Discrepancy](INBOUND_RECEIVING_DISCREPANCY.md) | Không có finding mới; không đồng nghĩa production GO | Accepted PR #1 | Retain | Implemented |
| Putaway/location stock | 29/35/84 | RECEIVING task, status-aware balances, immutable Location movement, assignment | [Putaway](INBOUND_PUTAWAY_LOCATION_MOVEMENT.md), accepted SQL/browser | Lot/HU/capacity/slotting chưa trong MVP | Posted receipt/location | Retain | Implemented; advanced scope deferred |
| Database permissions/admin | 17 §33, 18/282 | Per-request RolePermission, aggregate grant/membership/security tokens, runtime guards/UI | [Permissions](PERMISSION_CODE_AUTHORIZATION.md), accepted matrix | Membership UI không phải gap bắt buộc | Accepted PR #1 | Retain | Implemented; membership UI DEFERRED_BY_OWNER |
| Reservation | 30, OUT-RESERVATION-WORKBENCH | StockReservationService; `/api/stock-reservations`; StockReservations UI; conditional eligible-stock allocation | Existing SQL reservation tests + accepted compatibility integration | Chưa là canonical SalesOrder/Allocation lifecycle; controller còn role-backed | Eligible active/unblocked/pickable stock | P1 dependency | Compatibility; không ghi missing toàn bộ |
| ExportReceipt approve/reserve/dispatch | 05 §185; 38 chỉ khi model boundary được chốt | ExportReceiptService, ExportReceiptsController, ExportReceipts.tsx; two dispatch modes | Existing SQL export atomicity/concurrency + accepted full CI | Export Approved khác Shipment LOADED; action map/bundles chưa canonical | Reservations, source scope, approved workflow boundary | P0/P1 | Compatibility; proposed next slice, CONTRACT GAP |
| Canonical Allocation | 30/228, OUT-ALLOCATION-WORKBENCH | Repository chọn bucket theo LocationId; chưa có Allocation entity/API/UI lifecycle | Static source inventory; không có fresh canonical acceptance | Không có persisted allocation/pick reference, FIFO/FEFO lifecycle | Reservation + policy/quantity model | P2, hoặc mandatory dependency nếu chọn Shipment chuẩn | Not implemented canonical; primitive hiện có không được đổi tên thành engine |
| Shipment Loading/Dispatch/POD | 38/01/228, Dispatch row | Chưa có Shipment entity/controller/route; ExportReceipt là model khác | Static tracing; không có Shipment browser evidence | Loaded/HU/allocation/outbox và mapping chưa có | Allocation → Picking → Packing/HU | P1 target; P0 boundary | Not implemented canonical |
| Picking/Packing/HU | 36/37/228 | Chưa có execution entities/controllers/UI | Static source inventory | Không thể giả task/HU đã hoàn tất để Dispatch đạt precondition | Allocation, demand/reference model | P2 dependency nếu Shipment chuẩn | Not implemented; không mở nhiều module trong lượt này |
| ASN/appointment | Root/01, ASN/Appointment rows | Chưa có entities/controllers/UI tương ứng | Source đối chiếu row Designed/Unknown | Expected-arrival/dock scheduling chưa có | Customer workflow/dock constraints cần chứng minh | P2 | Designed, chưa implemented; không bắt buộc cho manual receipt MVP đã đóng |
| Reason/tolerance/QC-policy administration | 20/41/84 | Entities/evaluator/snapshot và reason read có; chưa có complete CRUD management surfaces | Inbound configuration/snapshot tests hiện có | Không đồng nhất seeded/configured policies với UI quản trị đầy đủ | Exact action/versioning/admin contract | P2 | Partially implemented/configuration; chưa chọn slice này |
| Full locks/reversal/balance reconstruction | 77/32/88/05 | Location blocking + InventoryTransaction/location movement; chưa có generic InventoryLock/Reversal lifecycle | Existing scoped ledger/integration evidence | Chưa đạt toàn canonical dimensions/temporal reversal; không suy diễn Down là business reversal | Explicit correction/lock semantics | P2 dependency theo contract cần chọn | Compatibility/partial foundation; production gate riêng |
| Transfer/stocktake/report | Existing services/controllers/UI; 29/35 compatibility | Legacy/status-limited integration đã tồn tại; current stock query chỉ eligible AVAILABLE | Accepted compatibility matrix/full suites | Không phải acceptance của canonical multi-warehouse engine/status-inclusive physical report | Customer evidence, ledger/status semantics | P2 | Compatibility; không migration thêm quyền trong slice này |
| Returns / advanced WMS / Dashboard | 05 §185, 211 | Ngoài scope selected; không tạo trang để coi là DONE | Không có fresh slice evidence | Discovery/scale/customer proof chưa được chốt | First Paying Customer Gate | Deferred | Không chọn; không tự đưa vào sprint |

## Slice duy nhất được đề xuất: xác nhận xuất kho end-to-end

Vấn đề người dùng: hàng đã được nhận và cất vào vị trí có thể sử dụng cần được giữ cho phiếu xuất, rồi xác nhận xuất vật lý một lần, không trừ kho khi chỉ duyệt/giữ hàng và không double-ship khi retry hoặc cạnh tranh.

Lý do chọn: Blueprint §185.3 ưu tiên Shipment Dispatch sau inbound. Source đã có reservation, stock conditional updates, export ledger/audit, idempotency middleware và UI; vì vậy nên xem xét tái sử dụng trước khi tạo Shipment/HU engine. Tuy nhiên reuse chỉ là **recommendation**, chưa phải owner-approved replacement của Page 38.

Luồng thực tế hiện hành:

```text
ExportReceipt Draft --ApproveAndReserve--> Approved --Dispatch--> Dispatched
                        On Hand giữ nguyên            On Hand giảm
Draft --ApproveAndDispatch--> Dispatched  (compatibility mode riêng)
Draft/Approved --Cancel--> Cancelled; reservation được giải phóng

Shipment canonical: Draft → Ready → Staging → Loading → Loaded → Dispatched
                                                     ^ yêu cầu của Page 38
```

Không tự ánh xạ Approved thành Loaded; không đổi enum numbers hoặc silently bỏ mode đã có. Không gọi stock bucket selection của repository là canonical Allocation. Không tạo Shipment mới với HU/pick/allocation giả để thỏa validation.

### Action inventory hiện có cần contract trước cutover

| Endpoint thực tế | Authorization hiện tại | Quyết định còn thiếu |
| --- | --- | --- |
| GET `/api/ExportReceipts` và `/{id}` | AllRoles + warehouse scope | Exact database read grant, Viewer projection và mixed Approval Center read map |
| POST `/api/ExportReceipts` | Admin/Manager/Staff + warehouse scope | Exact create grant/bundle; giữ quantity/snapshot rule được chốt |
| PUT `/api/ExportReceipts/{id}/customer` | Admin/Manager/Staff + shared scoped partner assignment | Exact update grant; đọc Partner là capability độc lập |
| POST `/{id}/approve-and-reserve` | Checker policy + maker/checker + service role check | Exact approve/reserve permission(s), mode và separation |
| POST `/{id}/approve-and-dispatch` | Checker policy + service role check + configured mode | Giữ compatibility/defer/replace phải do owner chốt; đây là stock boundary riêng |
| POST `/{id}/approve` | Checker policy + server-configured mode | Legacy alias/compatibility behavior không tự bỏ |
| POST `/{id}/dispatch` | Admin/Manager/Staff + service role check | Exact dispatch grant/bundle, precondition và operator separation |
| POST `/{id}/cancel` | Admin/Manager/Staff + scope/state | Exact cancel grant/bundle; không alias inbound receipt.cancel |
| Approval Center ExportReceipt actions | Outbound compatibility ngoài inbound cutover | Mapping theo document type nếu slice cutover; approval.reject inbound không mở outbound |

Page 17 có `shipment.*`, `reservation.*`, `allocation.*`, nhưng chưa chốt chúng áp dụng thế nào cho các ExportReceipt actions và role bundles. Không tự tạo `export.*`, seed capability giả, cấp prefix/wildcard hoặc dùng `receipt.*` thay thế. Chưa có endpoint mapping được duyệt cho slice mới, nên chưa sửa catalog/policies/bundles/migrations.

### Historical readiness — superseded by owner contract and integrated implementation

1. **Model/state boundary:** tái sử dụng ExportReceipt với contract MVP riêng “duyệt và giữ hàng → xác nhận xuất kho”, hay Shipment riêng theo Page 38 bắt buộc Loaded? Nếu chọn Shipment chuẩn, dependencies allocation/pick/packing/HU cần sequencing riêng; không gom chúng vào một slice.
2. **Permission/action contract:** sau khi model được chốt, ánh xạ exact codes/bundles cho actions thực tế, cả Approval Center; xác định compatibility modes được giữ và phạm vi cutover. Existing outbound role-backed behavior không được tự coi là database-permission implemented.

Owner question về mục 1 đã gửi; chưa có câu trả lời tại checkpoint này. Không sửa Notion để hợp thức hóa recommendation. Blueprint §7 Definition of Ready chưa đạt; không bắt đầu runtime/schema implementation khi inventory/security decisions còn mở.

### Acceptance cần chứng minh sau khi contract được chốt

| Nhóm | Evidence yêu cầu | Invariant |
| --- | --- | --- |
| Quantity/boundary | Focused service + SQL-backed HTTP + UI | Base quantity/snapshot chính xác; approve/reserve inventory-neutral; physical dispatch giảm đúng một lần theo model được duyệt |
| Eligibility | Owned SQL fixtures, browser HTTP và SQL postconditions | Chỉ AVAILABLE tại active/unblocked/pickable Location; RECEIVING/DAMAGED/REJECTED không được xuất |
| Atomicity | SQL failure injection theo test pattern hiện có | Stock, reserved counter/ledger, document state, success audit và idempotency commit/rollback cùng transaction |
| Replay/concurrency | True overlapping SQL/HTTP; browser-origin replay phân loại riêng | Same key same payload exactly once; changed payload/stale safe 409; không oversell hoặc duplicate Export/SHIP |
| Authorization | Real database grants + raw HTTP/browser | Permission không vượt warehouse/maker/checker/assignment; foreign direct-ID 404; revoke trước replay không lấy cached success |
| Viewer/late responses | Raw JSON + component/browser delayed response | Không giá trị/ghi chú riêng/token nội bộ; quyền bị thu hồi không được response cũ phục hồi |
| UX/accessibility | Component + UI browser workflow | Vietnamese labels/status/history/errors, keyboard/focus/accessible names, loading/empty, synchronous double-submit |
| Migration/legacy | Nếu schema/catalog thay đổi: EF generated SQL + owned fresh/legacy QA | Additive migrations; không sửa lịch sử, inventory totals hoặc accepted inbound state/boundary |

Source landmarks để reuse: `ERP.Application/Services/ExportReceiptService.cs`, `ERP.Infrastructure/Services/StockReservationService.cs`, `ERP.Infrastructure/Repositories/InventoryStockRepository.cs`, `ERP.Api/Infrastructure/IdempotentCommandFilter.cs`, `ERP.Api/Infrastructure/IdempotentCommandAttribute.cs`, và official QA helpers dưới `tools/ERP.BrowserQa`. Không tạo một harness/transaction/authorization engine thứ hai.

UX gap có bằng chứng static trong compatibility UI: `ExportReceipts.tsx` hiện hiển thị “Workflow xuất kho” và raw `dispatchMode` trong detail. Export DTO hiện dùng Product.UnitName live, không có operation-UOM/factor/version snapshot như inbound. Đây là backlog cần xét trong contract slice xuất, không dùng chúng để mở lại acceptance của inbound hoặc giả rằng toàn ERP đã READY. Nếu alternate-UOM outbound được yêu cầu thì cần chốt/persist snapshot trước implementation; không tự dùng live conversion để xuất.

Exclusions: không Dashboard, Returns, generic posting/reversal/authorization engine, advanced FIFO/FEFO/lot/serial/HU/mobile/offline/carrier integration; membership UI vẫn DEFERRED_BY_OWNER. Nếu Page 38 giữ một dependency này bắt buộc thì phải giải quyết contract/sequencing, không tự đánh dấu deferred để bypass precondition.

## Historical review, verification và handoff — backlog audit only

Ponytail scope assessment: reuse primitives hiện có là phương án nhỏ nhất cần owner chốt; chưa đủ contract để nói “ship”. Correctness/readiness review xác định hai gap trên, không tuyên bố có exploit mới hoặc findings đã đóng bị mở lại. plan-eng-review mới ở readiness/scope challenge, chưa có approved implementation plan. UX review chỉ static; chưa render application/reference image hoặc kiểm thử accessibility của slice mới.

Trong lượt này: **không** chạy lại build/SQL/API/frontend/browser của PR #1; không tạo database, credential, API/frontend process hoặc browser profile; không truy cập database/business tables ERP_KHO; không sửa source/model/catalog/migration. Không có QA resources mới cần kill/drop. Worktree mới giữ lại để tiếp tục từ quyết định owner. Các path UNKNOWN/policy-blocked được giữ nguyên.

Báo cáo này là file mới duy nhất, chưa stage/commit/push. Nội dung được đọc lại và kiểm tra UTF-8 strict, secret/private-key patterns, binary/NUL, đường dẫn source tham chiếu và diff-check cho cả file chưa tracked; các kiểm tra PASS sau khi bỏ trailing whitespace. Đây là document checks, không thay thế runtime verification. Chưa tạo PR mới. Khi contract và implementation đạt gates, PR slice mới dùng accepted feature branch làm base để diff chỉ gồm slice mới; phụ thuộc PR #1, không lặp accumulated diff và không merge/main push. PR #1 tiếp tục Draft.

REMOTE STAGING NOT AUTHORIZED

CAPACITY EXECUTION NOT AUTHORIZED

PRODUCTION NO-GO
