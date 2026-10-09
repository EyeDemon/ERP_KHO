# INV-11 — Danh sách AVAILABLE không được báo khớp giả khi Ledger chưa xác định

**Phạm vi:** API GET /api/InventoryReconciliation, truy vấn EF/SQL Server và giao diện Đối chiếu tồn kho. Không có lệnh sửa số dư, không có migration hoặc cập nhật dữ liệu lịch sử.

## Sai lệch cần sửa

Trước thay đổi, danh sách chỉ lấy Ledger có InventoryStatus=Available và cộng trực tiếp ApplySign(TransactionType). Giao dịch StatusChange chuyển AVAILABLE sang QC_HOLD được ghi với InventoryStatus=QcHold, nên danh sách bỏ mất số lượng giảm ở nguồn. TransferAdjustment/loại chưa hỗ trợ bị diễn giải thành dấu 0 hoặc tính sai, khiến hàng có lịch sử không phân loại có thể bị đánh dấu Match/Mismatch dù không đủ chứng cứ. Trong khi đó hồ sơ điều tra INV-11 đã trả all-status expected/difference=null khi gặp sự kiện không phân loại.

## Hợp đồng sau thay đổi

- Lấy tất cả sự kiện Ledger **trong cùng cặp kho/sản phẩm đã được phân quyền**, nhóm tại SQL theo TransactionType, InventoryStatus, From/To status. Tính AVAILABLE bằng dòng nhập/xuất có dấu và cộng StatusChange vào AVAILABLE, trừ StatusChange rời AVAILABLE. Các trạng thái khác không tự động được tính vào AVAILABLE.
- Bất kỳ nhóm có TransactionType chưa được định nghĩa, TransferAdjustment chưa có dấu chuẩn, InventoryStatus ngoài enum, StatusChange thiếu from/to, trùng nguồn/đích, to không khớp InventoryStatus, quantity không dương, hoặc sự kiện âm bất thường: cả cặp trả `status=Indeterminate`, `expectedQuantity=null`, `difference=null`, `unclassifiedLedgerEventCount>0`. Không có đề xuất tự sửa hoặc lệnh ghi.
- Thành phần nhập/xuất chỉ lấy AVAILABLE; hai cột `statusChangeInQuantity`/`statusChangeOutQuantity` giải thích phần chuyển trạng thái. `Match` chỉ khi lịch sử phân loại đầy đủ và difference=0; `Mismatch` chỉ khi đủ chứng cứ và difference khác 0.
- UI tiếng Việt: trạng thái `Chưa xác định` màu cảnh báo, không đếm vào mismatch, không đưa null vào tổng độ lệch; hiển thị riêng số cặp chưa xác định. Dữ liệu null giữ nguyên, không ép về 0.
- Bộ lọc kho vẫn `EnsureWarehouseAccessAsync` (không có quyền => 404), lọc không chỉ định kho vẫn lấy danh sách kho được phân quyền; không đưa dữ liệu từ kho khác vào danh sách hoặc truy vấn nhóm.
- Đối chiếu này là phép đọc trạng thái hiện tại so với Ledger lịch sử, chưa có snapshot transaction-wide. Không cho phép dùng chênh lệch để tự động sửa số dư khi có giao dịch đồng thời.

## Kiểm thử

- Unit InMemory: TransferAdjustment không làm false Match, ExpectedQuantity=null và status=Indeterminate; các test INV-11 hiện có xác minh lọc kho và phân trang.
- API contract: JSON nullable `expectedQuantity`/`difference` là null, không phải 0; `unclassifiedLedgerEventCount` và StatusChangeOutQuantity được serialize.
- Vitest: dòng Indeterminate hiển thị Chưa xác định, số mismatch không tăng, tổng lệch không bị NaN, có dòng chuyển trạng thái.
- Browser QA: CI phải chạy Chromium thật trên backend .NET + SQL QA cô lập (xem `docs/BROWSER_QA_REQUIRED_GATE.md`). Browser Use Preview public chỉ xác minh smoke demo; API public hiện trả HTML fallback, chưa đủ điều kiện staging.
- Chạy SQL integration toàn bộ trong workflow hiện hành; nếu CI thất bại phải sửa code và chạy trên SHA mới, không rerun run đã PASS.

**Trạng thái:** code thay đổi đang chờ CI và Browser QA trên SHA mới. PR #31 tiếp tục Draft, Notion chỉ đọc, không merge hoặc đánh dấu production.
