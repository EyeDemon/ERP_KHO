# INV-11 — Đối chiếu trạng thái tồn kho bằng SQL và Ledger (chỉ đọc)

Mốc triển khai: 10/10/2026. Phạm vi PR #31 Draft. Đây là phần mở rộng của API điều tra đã có; **không tạo thao tác sửa số dư tồn kho**.

## Phạm vi và nguồn bằng chứng

- `GET /api/InventoryReconciliation/investigation` giữ tham số warehouseId/productId, eventAnchorId, eventBeforeId, limit và quyền `inventory_ledger.read`. Mỗi lần truy vấn phải vượt qua `EnsureWarehouseAccessAsync(warehouseId)` trước khi đọc chi tiết.
- Mốc Ledger ID được chọn từ **tất cả trạng thái** của cặp kho–sản phẩm, thay vì chỉ `AVAILABLE`. Cờ `ledgerHasEventsAfterAnchor` theo dõi mọi giao dịch sau mốc trong phạm vi cặp. `eventStatus` nhận đúng tên một trong 8 trạng thái, mặc định `Available`. Bảng sự kiện Ledger trả `eventStatus`, `inventoryStatus`, `fromInventoryStatus`, `toInventoryStatus` và số có dấu theo trạng thái được chọn, với keyset `eventBeforeId`/giới hạn 1–100 mỗi trang; không trả toàn bộ lịch sử trong một request. `eventStatus` và `bucketStatus` là hai bộ lọc độc lập, không làm đổi mốc Ledger của nhau.
- `statusBreakdown[]` trả đủ 8 trạng thái (`Available`, `QcHold`, `Quarantine`, `Damaged`, `Rejected`, `Blocked`, `Expired`, `RecallBlocked`) với số bucket, tổng tồn và reserved **hiện tại**, biến động Ledger trực tiếp, lượng chuyển trạng thái vào/ra, số Ledger kỳ vọng và chênh lệch **tham khảo**.
- SQL tổng hợp `InventoryStocks` theo `Status` và `InventoryTransactions` theo `InventoryStatus / TransactionType / FromInventoryStatus / ToInventoryStatus`, lọc chính xác warehouseId/productId và `Id <= anchor`. Chỉ materialize các nhóm theo enum, **không tải toàn bộ Ledger hoặc bucket** lên ứng dụng.
- `StatusChange` hợp lệ được diễn giải **-Quantity** ở `FromInventoryStatus` và **+Quantity** ở `ToInventoryStatus`, luôn bảo toàn tổng toàn kho. `Move` và `Reversal` marker có dấu 0; `Import/Export/Ship/TransferIn/TransferOut/Adjustment` dùng canonical `TransactionType.ApplySign`. Không quy `StatusChange` thành lượng nhập mới.
- `allStatusCurrentQuantity`, `allStatusReservedQuantity`, `allStatusExpectedQuantity`, `allStatusDifference` là tổng của mọi trạng thái. Cặp chỉ có bucket/sự kiện `QcHold` hay `Quarantine` không bị trả 404 chỉ vì `AVAILABLE` rỗng.
- Khi gặp `TransferAdjustment`, loại giao dịch enum lạ, `StatusChange` thiếu nguồn/đích hoặc khai báo sai trạng thái/chiều âm, đặt `unclassifiedLedgerEventCount > 0`; **mọi ExpectedQuantity/Difference theo trạng thái và tổng toàn trạng thái = null**, giao diện hiển thị **Chưa xác định**, không được sử dụng như nguồn để điều chỉnh tồn.
- Các chỉ số `CurrentQuantity`, `ExpectedQuantity`, `Difference` **kế thừa từ phép đối chiếu AVAILABLE** và được gắn nhãn AVAILABLE dù đang chọn lịch sử `eventStatus` khác. Phép tổng hợp `AVAILABLE` này không tính chuyển trạng thái vào/ra và **không phải** số dự kiến đầy đủ từng trạng thái; các giá trị trạng thái chuẩn được cung cấp trong `statusBreakdown[]`. Nếu không phân loại được loại có dấu, `availableLedgerExpectedIsPartial` được bật. Giao diện phải cảnh báo không được dùng những con số này để sửa tồn.

## An toàn và giới hạn

- Dữ liệu tồn đang là **hiện tại**; Ledger được **cố định tại anchor**. Hai nguồn không phải snapshot cùng thời điểm; chênh lệch chỉ hỗ trợ điều tra. Status-only breakdown không chứng minh nguồn cấp lô/sê-ri, owner/HU hoặc lịch sử thay đổi từng bucket.
- Việc thiếu `unclassifiedLedgerEventCount` **không phải** bằng chứng rằng lịch sử đầy đủ hoặc đủ điều kiện tái dựng số dư. Dữ liệu lịch sử từ trước khi triển khai đủ Ledger/StatusChange, commit-gap identity, reconciliation của từng location/lot/serial/owner/HU có thể khiến kết quả chưa đáng tin để điều chỉnh.
- Không tạo `POST/PATCH`, không ghi `InventoryStock`, `InventoryTransaction`, `AuditLog`, không đảo chứng từ, không phê duyệt hay tự động rebuild. Để remediation cần quyền riêng, review/approval, snapshot/fingerprint nhất quán, transaction serializable/locking, audit, idempotency, rollback và QA staging.

## Bằng chứng cần PASS

- Application/InMemory: Available 12, chuyển 3 sang QC thì Available 9/QC 3; tổng 12 không đổi; các trạng thái khác 0. Thêm sự kiện `TransferAdjustment` và StatusChange thiếu nguồn → kết quả trạng thái không xác định; anchor cũ không bị pha lẫn. Cặp chỉ có QC vẫn phải truy vấn được. Unsupported type tại Available không gây lỗi 500, bật cờ partial.
- SQL Server: fixture stock 10 với Ledger Import 10; gọi **InventoryStatusService.ChangeAsync** thật chuyển 3 sang QC_HOLD, xác nhận Available 7 + QC 3 = 10 và không double count. Thêm legacy TransferAdjustment ghi lùi ngày, giữ anchor cũ có expected 10, anchor mới có unclassified=1 và expected=null.
- API: serialization `statusBreakdown`/`eventStatus`/`bucketStatus`/chiều nguồn-đích và số lượng/chỉ báo camelCase; giao diện tiếng Việt có bảng 8 trạng thái, quan hệ chuyển vào/ra, tổng tồn và cảnh báo không thể kết luận; chạy các bài regression trước đó.
- Chỉ nhận CI PASS khi đúng SHA có backend build, SQL/Application integration, API, frontend lint/Vitest/build đều xanh. Preview Vercel READY là demo, **không phải staging backend SQL/API hoặc browser QA**.
- PR #31 giữ Draft, không merge; Notion chỉ đọc, không nâng System Blueprint thành live.

## Chưa xong

Snapshot số dư nhất quán xuyên trang (keyset hiện giữ ranh giới ID nhưng không đóng băng số lượng); owner/HU custody, full historical provenance, controlled remediation và approval, INV-09 native document reversals, Return/Recall và nghiệm thu trình duyệt/staging. INV-11 vẫn `foundation`.

## Sửa hồi quy CI sau batch đầu tiên

- CI đầu tiên (run 37964881907) FAIL 1/530 Application tests ở trường hợp `TransferAdjustment` trong trang Ledger `AVAILABLE`: phần tổng hợp đã đánh dấu partial, nhưng DTO event chi tiết vẫn gọi `ApplySign` và ném `ArgumentOutOfRangeException`.
- Đã xử lý tận gốc bằng cách đổi `SignedQuantity` của event sang `decimal?`: với loại không có quy tắc dấu hoặc số lượng âm, trả **null** và hiển thị **Chưa xác định**, không gán 0 vì Move/Reversal hợp lệ mới có dấu 0. Bổ sung regression InMemory, API camelCase/null và Vitest, giữ nguyên Ledger bất biến.
- Chỉ đánh dấu batch PASS sau CI của commit sửa lỗi, không lấy kết quả Vercel READY hoặc các bước test đã PASS của commit thất bại để thay thế.

## Bổ sung: điều tra lịch sử Ledger theo 8 trạng thái (10/10/2026)

- `StatusChange` được đọc tại **cả trạng thái nguồn và đích**, sử dụng cùng `TransactionId`; signed quantity **âm** ở nguồn, **dương** ở đích. Không tạo thêm giao dịch hoặc nhân đôi tổng `allStatusExpectedQuantity`.
- Event keyset `Id < eventBeforeId` và `Id <= eventAnchorId` chạy SQL trên đúng kho/sản phẩm/trạng thái; mỗi trang **ủy quyền lại kho** trước mọi dữ liệu. Trạng thái đầu vào dạng số, không đúng tên chuẩn, sai hoa/thường bị chặn.
- Bằng chứng `StatusChange` hỏng nguồn/đích/quantity vẫn hiển thị `signedQuantity: null` thay vì giá trị 0 giả. Ứng dụng React từ chối phản hồi trả sai `eventStatus`/scope, reset event cursor khi đổi lịch sử trạng thái, nhưng giữ nguyên trang bucket.
- API/SQL/Vitest và Chromium QA trên môi trường SQL cô lập là điều kiện để checkpoint PASS; **remote authenticated staging chưa có hạ tầng**, do đó không coi phần này là production live.

## Guard danh sách tổng quan — fail-closed (tiếp nối PR #31)

- Trước khi hiển thị dữ liệu `GET /api/InventoryReconciliation`, frontend kiểm tra cấu trúc phân trang, ID/scope kho và sản phẩm theo bộ lọc, cặp trùng, số hữu hạn, và nhất quán `Match / Mismatch / Indeterminate` cùng `Difference`.
- Nếu server trả dữ liệu không hợp lệ, xóa danh sách và số liệu tổng trang, hiển thị lỗi tiếng Việt, chặn đường vào hồ sơ điều tra cũ. Không chuẩn hóa một kết quả sai thành số 0 hoặc `Match`.
- Guard phía trình duyệt không thay thế authorization/backend SQL; đây là xử lý an toàn trường hợp API bị sai schema hoặc scope. Regression Vitest tập trung vào scope, trạng thái giả, Ledger chưa phân loại, số không hữu hạn, dữ liệu trùng và lần tải hồi phục. Không có mutation SQL, Ledger, dữ liệu mô phỏng hay Notion.
- Mọi build/CI/browser QA phải gắn đúng SHA successor. Remote staging có API/SQL và quyền thật vẫn là điều kiện riêng để đóng PR.

## An toàn phép gom nhóm Ledger: số âm / StatusChange bằng 0 (10/10/2026)

- Trước khi tính kỳ vọng, SQL `GROUP BY` phải đếm **bản ghi âm** theo từng nhóm; phép `SUM` có thể triệt tiêu số âm với số dương và báo `Match` giả. Với `StatusChange`, còn phải đếm từng bản ghi **không dương** (gồm số 0) dù tổng nhóm lớn hơn 0.
- Nếu thấy các bản ghi này, toàn bộ kết quả Expected/Difference theo 8 trạng thái được đánh dấu `null` (chưa xác định); danh sách tổng quan trả `Indeterminate`. `UnclassifiedLedgerEventCount` chỉ tính số bản ghi có dấu sai, không đếm nhầm các bản ghi hợp lệ cùng nhóm.
- Tổng Legacy AVAILABLE là dữ liệu tham khảo từng phần; không được tính nhóm hỗn hợp số âm/dương như đã phân loại. `AvailableLedgerExpectedIsPartial=true` cảnh báo nguồn không đủ tin cậy, không tạo đề xuất sửa tồn.
- Regression gồm SQL Server thật, InMemory và kiểm tra cả danh sách + bằng chứng có anchor. Không thêm mutation, sửa Ledger, SQL schema/migration hoặc Notion. Vercel demo không được coi là chứng cứ staging thật.

## INV-11 — Hiển thị cặp chỉ có tồn QC/Quarantine không có Ledger

- Tổng quan phải lấy cặp kho–sản phẩm từ **tất cả 8 trạng thái** InventoryStocks cùng Ledger, không chỉ AVAILABLE. Các cặp legacy chỉ có bucket `QcHold`/`Quarantine` và không có sự kiện phải xuất hiện trong phân trang.
- Khi một cặp **chỉ có bucket ngoài AVAILABLE và không có lịch sử Ledger**, kết quả AVAILABLE tổng quan là `Indeterminate` với Expected/Difference `null`, **không** giả tạo `Match` từ phép tính 0−0. `UnclassifiedLedgerEventCount=0` vẫn có thể xảy ra: thiếu Ledger khác với Ledger không phân loại được.
- Phép cộng tồn AVAILABLE của trang chuyển sang `GROUP BY` SQL theo đúng cặp kho/sản phẩm, có đếm bucket theo AVAILABLE và trạng thái khác; không materialize toàn bộ bucket lên server. Áp dụng `GetAccessibleWarehouseIdsAsync` hoặc `EnsureWarehouseAccessAsync` **trước** tất cả truy vấn stock và ledger; không mở quyền kho.
- Regression InMemory kiểm tra hiển thị, liên kết điều tra và kho bị cấm; SQL Server integration kiểm tra cặp legacy chỉ có QC stock sau phép đổi trạng thái trong fixture. Không sửa Ledger/Balance production, không rebuild và không cập nhật Notion.

## INV-11 — Kết luận toàn bộ 8 trạng thái tại danh sách (10/10/2026)

- Kết quả tổng quan có `allStatusCurrentQuantity`, `allStatusExpectedQuantity`, `allStatusDifference` và `allStatusStatus`, độc lập với cột legacy AVAILABLE. Chỉ `Match` khi từng trạng thái đối chiếu khớp; tổng toàn kho bằng nhau không đủ (QC +1 và Quarantine -1 vẫn `Mismatch`).
- SQL aggregate buckets theo cặp kho–sản phẩm–trạng thái, có ủy quyền kho và giới hạn trang; Ledger nhóm SQL theo loại/trạng thái/nguồn-đích, StatusChange giảm ở nguồn và tăng ở đích, không double count. Không tải tất cả bucket.
- Các lịch sử chưa phân loại, trạng thái stock không hỗ trợ hoặc chỉ có bucket ngoài AVAILABLE nhưng không có Ledger phải là `Indeterminate`, Expected/Difference là null. Giao diện tiếng Việt hiển thị nhãn AVAILABLE riêng với kết luận 8 trạng thái, có kiểm tra JSON fail-closed.
- Cảnh báo: SQL Ledger và tồn hiện tại không cùng snapshot; đây là công cụ **chỉ đọc** để điều tra, không phê duyệt điều chỉnh hay tự động rebuild. Regression in-memory, SQL Server và Vitest bao gồm tổng 0 nhưng hai trạng thái lệch bù trừ; Notion và dữ liệu production không bị sửa.

## Kết luận tám trạng thái trong hồ sơ điều tra (2026-10-10)

- `GET /api/InventoryReconciliation/investigation` bổ sung `allStatusStatus = Match|Mismatch|Indeterminate`, tính từ chênh lệch của **từng** dòng `StatusBreakdown`, không lấy dấu `allStatusDifference`. QC +1 và Quarantine -1 có thể tạo tổng 0 nhưng vẫn là `Mismatch`.
- Frontend hiển thị badge tiếng Việt trong hồ sơ chi tiết; nếu tổng chênh lệch bằng 0 trong khi hai trạng thái lệch bù trừ, có cảnh báo rõ ràng và yêu cầu xem từng trạng thái. Kiểm tra fail-closed không chấp nhận server tuyên bố `Match` khi bảng từng trạng thái thực tế có chênh lệch.
- Nếu Ledger không thể phân loại, kết luận `Indeterminate`, không đưa ra khuyến nghị sửa tồn. Đây là bằng chứng read-only với bucket hiện tại khác snapshot mốc Ledger; các con số vẫn chỉ phục vụ điều tra.
- Regression: InMemory và SQL Server chứng minh cặp QC/Quarantine lệch đối ứng có tổng 0; Vitest kiểm tra cảnh báo và payload khai sai trạng thái. Không thay đổi schema hoặc mutation, không sửa Notion/Production.

## Đồng bộ AVAILABLE chi tiết với chuyển trạng thái Ledger (2026-10-10)

- Trước đây hồ sơ điều tra lấy `AVAILABLE.ExpectedQuantity` từ chỉ các giao dịch `InventoryStatus=Available`, rồi gọi `ApplySign(StatusChange)=0`. StatusChange rời AVAILABLE được ghi tại trạng thái đích, nên biến động `-3` bị bỏ sót và tạo chênh lệch AVAILABLE giả, mâu thuẫn với `StatusBreakdown` và danh sách tổng quan.
- Khi toàn bộ Ledger đến mốc đã phân loại an toàn, headline `AVAILABLE · Ledger đến mốc` nay tái sử dụng Expected đã cộng/trừ chuyển trạng thái của `StatusBreakdown[Available]`. Bao gồm chiều từ AVAILABLE sang QC và từ QC trở lại AVAILABLE; cùng một anchor, không thêm query tải toàn bộ Ledger.
- Khi có sự kiện chưa hỗ trợ, giữ `legacyClassifiedQuantity` chỉ để tham khảo và cờ `AvailableLedgerExpectedIsPartial` theo logic hiện hữu; `allStatusExpected/ allStatusDifference` là null, `Indeterminate`. Không coi con số một phần là đề xuất chỉnh tồn.
- Regression InMemory outbound+inbound `StatusChange`, SQL Server thật dùng service đổi trạng thái, Vitest hiển thị AVAILABLE nhất quán. Không thay đổi schema, Ledger, quyền kho hoặc Notion.
