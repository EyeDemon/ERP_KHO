# Cất hàng và di chuyển nội bộ theo vị trí

## Contract hiện hành

Cất hàng là di chuyển nội bộ sau Post. Balance key là `Product + Warehouse + InventoryStatus + Location`; Product, Warehouse, InventoryStatus và tổng On Hand không đổi. Post tiếp tục là inventory boundary duy nhất của receipt và atomically ghi stock vào system `RECEIVING`, ledger/Post audit, một task cho receipt và item cho từng receipt-line/status bucket dương. Door Rejected không tạo inventory hoặc item.

Task đi `OPEN → ASSIGNED → IN_PROGRESS → COMPLETED`, với nhánh cancel trước movement và `IN_PROGRESS → EXCEPTION → IN_PROGRESS`. Movement giảm source, tăng destination, ghi một immutable `InventoryLocationMovement`, audit và projection task/item trong cùng transaction. AVAILABLE chỉ vào active/unblocked/pickable STORAGE; DAMAGED chỉ vào DAMAGED; REJECTED chỉ vào REJECTED. Reservation/export/operational availability chỉ đọc AVAILABLE tại location active, unblocked, pickable.

Migration `20260927014531_AddInboundPutawayLocationMovement` tạo Location, task/item/movement, backfill deterministic system LEGACY locations và location-aware stock key. Stock cũ giữ nguyên quantity/status; không tạo fake task/movement/audit. Down chỉ an toàn trước khi schema mới có production data; phải backup và có data plan trước rollback sau khi phát sinh location/task/movement.

API gồm list/detail/destinations, assign/start/move/exception/resume/cancel và quản trị Location tối thiểu. Mọi mutation dùng warehouse scope, optimistic concurrency, Idempotency-Key/fingerprint và trả 409 cho stale/invalid/race. Viewer chỉ nhận operational fields; mutation token và internal metadata bị lọc.

Frontend hiển thị module **Cất hàng** hoàn toàn bằng tiếng Việt, ánh xạ status/inventory enums, có loading/empty/error, synchronous double-submit guard, stale clearing và responsive keyboard/scanner-wedge input. Đây không phải chứng nhận scanner vật lý.

## Fresh verification — 2026-09-27

- Notion Page 35 `2026-09-24T16:03:22.053Z`, `INB-PUTAWAY-LIST` `2026-09-24T16:05:15.830Z` và Page 41 `2026-09-20T16:31:54.616Z` được đọc lại, không đổi và không có conflict. QC-before-Post vẫn canonical.
- Drive artifact hiện hành `1Pluwl2RQk3fRujU_JpOiUUWJiejyjGi4`, modified `2026-09-19T18:47:49.013Z`, chỉ metadata-checked trong closure này: `NOT REVIEWED`. Drive là Illustrative UI/UX Reference.
- Release build PASS 0 warnings/errors; EF pending-model PASS.
- Application owned-SQL PASS 342/342, Run `35d531efbeeb476ca69134ae2e198551`; marker-owned database đã cleanup.
- API SQL/HTTP PASS 156/156; TRX `TestResults/Putaway/ApiClosure/api-putaway-closure.trx`; owned databases tự cleanup.
- Frontend PASS 56/56; lint và production build PASS.
- Browser Run `148ba598820e479298fa1fdfbf4cbd90` dùng loopback API/frontend, synthetic users và marker-owned database. UI tạo/hiển thị task sau Post, double-click Assign/Start/Move chỉ gửi một mutation, partial move 40 vào STORAGE-A và split 60 vào STORAGE-B hoàn tất task. Database evidence: RECEIVING 0, STORAGE-A 40, STORAGE-B 60, hai immutable movement rows tổng 100, hai movement audits, một task completed và một receipt Post transaction; warehouse/status total giữ 100.

## Trạng thái verification hiện tại

## Fresh closure evidence — 2026-09-28

Browser Runs `148ba598820e479298fa1fdfbf4cbd90`, `0f5ec2aae31c4e6ab370ca60f33397bc`, `14eb93c5ea2f46b6b0681618010b08fa` và `9dfb6177fbeb4da1a415f385bd15b080` lần lượt chứng minh happy path partial/split, replay/fingerprint và true concurrent Move, true concurrent Post, rồi closure Viewer/destination/availability/UOM/isolation. Run `7cfbe190e099440bab7fdad9b9604064` là evidence phát hiện lỗi Viewer RowVersion trước bản sửa.

Run closure xác nhận Viewer JSON loại bỏ hoàn toàn task/item/location `rowVersion`, exception private content, cost/value và idempotency metadata; Manager vẫn nhận token Base64 hợp lệ cho mutation. Mười destination không hợp lệ trả `400` hoặc isolated `404`, không tạo movement, balance hay success audit. AVAILABLE tại RECEIVING có On Hand nhưng pickable bằng 0; sau move 11 vào STORAGE, reservation và export reserve thành công, trong khi Putaway movement count giữ nguyên. Snapshot `2 BOX × 12 = 24 EA` tiếp tục được dùng sau khi master factor đổi thành 99; unsupported UOM, precision overflow, zero, negative và over-remaining đều trả `400` với zero effect. Cross-warehouse list/read/destinations và mọi mutation/replay giữ isolation `404`, zero idempotency claim và zero business effect.

Fresh automated closure: Release build 0 warning/error; EF pending-model PASS; focused Putaway/serialization 5/5; middleware 5/5; Application owned-SQL 343/343 Run `cfb502d4744e4fb7b4f0592b37a58f56`; API SQL/HTTP 159/159; frontend 56/56, lint và production build PASS. Drive artifact `1Pluwl2RQk3fRujU_JpOiUUWJiejyjGi4` remains metadata-only `NOT REVIEWED`. Marker-owned BrowserQA database, processes and credential were cleaned by exact Run ID.

**INBOUND PUTAWAY READY FOR OWNER REVIEW.**

Cleanup của Browser Run đã marker-verify và xóa database owned, credential tổng hợp và các process còn thuộc run. PID frontend ghi trong manifest đã được Windows tái sử dụng bởi process không thuộc run; ownership guard đã từ chối dừng process đó, đúng contract an toàn. Kiểm kê SQL sau cleanup chỉ còn database `ERP_KHO` ONLINE; không đọc business tables.

Browser QA phát hiện và sửa hai production defect: PutawayService từng mở transaction lồng với idempotency middleware; service nay tái sử dụng ambient transaction. Frontend từng giữ logical idempotency key sau mutation thành công; nay giải phóng key sau success. Hai fix có runnable regression tests.

Deferred: lot/serial/HU, weight/volume capacity, optimization/scoring, wave/routing, native/offline mobile, reverse completed movement, generic location transfer và advanced rule engine.


## Permission/presentation regression fix — 2026-10-05

Owner audit sau WH-06 phát hiện frontend hiện hành đã gom ba capability `putaway.assign`, `putaway.execute` và `putaway.cancel` thành một gate `putaway.execute`, trong khi API và permission registry vẫn tách độc lập. Fix này khôi phục đúng capability boundary ở UI: phân công chỉ theo `putaway.assign`, thực thi theo `putaway.execute`, hủy trước movement theo `putaway.cancel`. API regression test khóa exact action-to-permission mapping để tránh lệch lại.

Header Cất hàng cũng được đưa về presentation contract tiếng Việt, không hiển thị các từ kỹ thuật `Inbound`, `putaway`, `location` hoặc `exception handling` trong mô tả người dùng. Không thay đổi inventory movement, balance, Receipt POST, migration hoặc SQL semantics trong fix này.

Verification gate của fix là PR CI đầy đủ; không merge nếu backend, SQL, Application/API tests, frontend lint/tests/build chưa xanh.
