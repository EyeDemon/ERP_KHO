# INV-11 — Phân trang Ledger có mốc cố định cho hồ sơ điều tra

Ngày thực hiện: 09/10/2026. PR #31 Draft; đây là chức năng truy vấn SQL read-only, **không** phải sửa chênh lệch.

## Hợp đồng API
- Giữ `GET /api/InventoryReconciliation/investigation?warehouseId=...&productId=...&limit=50`, thêm tham số tùy chọn `eventBeforeId` đi cùng `eventAnchorId` bắt buộc khi phân trang. Cả hai là **ID giao dịch**, không phải offset/trang theo thời gian.
- Trả `eventAnchorId`, `eventBeforeId` (mốc chặn riêng trang), `nextEventBeforeId`, `eventsTruncated` (còn trang sự kiện cũ hơn), `eventCount` (tổng trong toàn bộ mốc), `ledgerHasEventsAfterAnchor` (Ledger đã phát sinh thêm sau mốc).
- `eventBeforeId` chỉ hợp lệ nếu 1 ≤ before ≤ anchor và anchor được gửi rõ. ID âm/0, lớn hơn anchor hoặc thiếu anchor bị từ chối trước SQL. Giới hạn `limit` 1–100; mỗi trang trả tối đa `limit` sự kiện và 1 sentinel để xác định còn trang.
- `WHERE WarehouseId = @warehouseId AND ProductId = @productId AND InventoryStatus = AVAILABLE AND Id <= @anchor AND Id < @before` áp mọi trang, sắp `ORDER BY Id DESC`. Cờ `ledgerHasEventsAfterAnchor` đến từ truy vấn ledger cùng scope có `Id > anchor`; **không** gộp sự kiện mới vào tổng `ExpectedQuantity`.
- Bắt buộc quyền `inventory_ledger.read` và `EnsureWarehouseAccessAsync` **mỗi request**, kể cả bấm trang sau hoặc trang trước. Không nới quyền khi dùng cursor, không cho phép lộ sự kiện ngoài kho được cấp quyền.
- Dòng Ledger bất biến trong cùng mốc có thể đọc hết qua keyset paging mà không bị trùng/lệch do append sau mốc hay `TransactionDate` ghi lùi ngày. Dữ liệu có ID cấp trong transaction chưa commit vào thời điểm đầu tiên vẫn là giới hạn isolation không thể khắc phục chỉ bằng high-water ID; nghiệm thu staging phải kiểm chứng mô hình commit/identity của DB.

## Giao diện và QA
- `Xem bằng chứng` mở trang đầu, nút **Sự kiện cũ hơn / Sự kiện mới hơn** dùng lịch sử cursor với **cùng anchor**; không dùng `Skip` và không đổi kho/sản phẩm. Nút **Làm mới mốc Ledger** chủ động lấy mốc mới và trở lại trang đầu.
- Khi phát hiện sự kiện sau anchor, hiển thị thông báo rõ. Số dư tồn/bucket vẫn là **hiện tại**, không phải snapshot tại mốc; chênh lệch chỉ là gợi ý điều tra và **không** được ghi trực tiếp thành điều chỉnh.
- Quyền kho bị thu hồi ở trang sau → 404 và đóng dữ liệu cũ (fail-closed). Giao diện tự loại payload có sai product/warehouse/anchor/cursor, ID sai, thứ tự ID không giảm hoặc cursor tiếp theo sai; đổi bộ lọc/đổi trang/đóng/mở hủy kết quả cũ bằng request generation.
- Unit/InMemory: 6 events trải qua 3 trang, không trùng, đến hạn hết trang; append event backdated sau anchor vẫn giữ số lượng và tổng mốc cũ, mốc mới phát hiện cập nhật; validate cursor sai trước authorization.
- SQL Server: 2 events import/adjustment → trang 1/trang 2, append Export ghi ngày quá khứ nhưng ID mới; khi anchored vẫn chỉ 2 events và same total, mốc mới có 3 events cùng signed quantity đúng.
- API contract JSON camelCase, Vitest nút trang cũ/mới, refresh, quyền thu hồi, cursor sai. Cổng CI backend/Application(SQL)/API/Vitest/lint/build phải PASS đúng SHA trước khi đánh dấu nghiệm thu kỹ thuật.

## Giới hạn không thay đổi
- Bucket detail vẫn giới hạn 100 bucket ở trạng thái AVAILABLE hiện tại, chưa có cursor riêng vì tồn là mutable và cần snapshot/concurrency policy. Owner/HU trạng thái khác cũng chưa được đối soát.
- Không tạo lệnh sửa InventoryStock/Ledger, không tạo approval/adjustment hay workflow Return/Recall. Chưa có browser QA và staging SQL/API thực, PR vẫn Draft; tuyệt đối không cập nhật Notion hoặc Blueprint sang trạng thái live.
