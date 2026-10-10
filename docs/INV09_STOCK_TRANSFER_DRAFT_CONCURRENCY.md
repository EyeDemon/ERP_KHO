# INV-09 — Chống ghi đè khi chỉnh sửa phiếu điều chuyển Nháp

## Bài toán

Hai người mở cùng một phiếu Nháp. Khóa SQL chỉ tuần tự hóa lệnh lưu, không phát hiện biểu mẫu đã mở trước đó bị lỗi thời. Nếu chỉ kiểm tra trạng thái Nháp, người lưu sau có thể âm thầm ghi đè số lượng và ghi chú của người lưu trước.

## Hợp đồng nghiệp vụ

- Mỗi phiếu có `DraftRevision` (bắt đầu từ 1); mỗi lần lưu Nháp thành công tăng đúng 1.
- `GET /api/stock-transfers/{id}` trả `draftRevision`.
- `PUT /api/stock-transfers/{id}` yêu cầu trường JSON `expectedDraftRevision`. Thiếu trường trả lỗi HTTP 400 trước khi vào service; phiên bản không hợp lệ hoặc cũ trả xung đột HTTP 409.
- Backend khóa header `UPDLOCK, HOLDLOCK`, xác minh trạng thái Nháp, kho được phép và phiên bản trước khi thay đổi dòng. `DraftRevision` là concurrency token của EF.
- Thay đổi dòng, tăng phiên bản và `StockTransfer.Updated` audit nằm trong một SQL transaction. Lỗi audit rollback toàn bộ. Không ghi Ledger cho chỉnh sửa Nháp.
- Migration `20261009140000_AddStockTransferDraftRevision` khởi tạo phiên bản 1 cho dữ liệu lịch sử; đây không phải khôi phục lịch sử chỉnh sửa cũ.
- Khóa idempotency gắn với toàn bộ payload, gồm phiên bản. Retry lỗi mạng giữ khóa cũ; 409 yêu cầu tải lại và thay khóa. Client cũ thiếu phiên bản bị từ chối, không cho phép ghi đè ngầm.

## Giao diện

- Hiển thị phiên bản Nháp bằng tiếng Việt trong hộp thoại.
- Khi xung đột 409, khóa lưu cho đến khi tải lại bản mới. Nếu API thiếu phiên bản hợp lệ, không mở trình sửa.
- Giữ điều hướng bàn phím, nút Tải lại phiên bản mới và trạng thái lỗi theo `design-system/erp-wms/MASTER.md`.

## Bằng chứng và giới hạn

- SQL Server integration: người thứ nhất lưu từ revision 1 lên 2, người thứ hai dùng revision 1 bị từ chối, sau tải lại revision 2 mới lưu lên 3; audit và ledger đúng.
- SQL Server integration: lỗi lưu audit phải rollback cả phiên bản lẫn dòng.
- Vitest: PUT chứa revision và sau 409 sử dụng revision mới.
- Chỉ CI đúng HEAD mới xác nhận kiểm thử; browser QA và staging backend/SQL thật là các cổng riêng. Notion chỉ đọc; PR Draft không merge khi thiếu nghiệm thu.

## Kiểm thử CI 09/10 — ổn định thời gian tương tác

- CI #638 (SHA `584c3148`) đạt Application SQL/API/lint, nhưng một Vitest trong `StockTransferCommandSafety.test.tsx` timeout ở giới hạn mặc định 5 giây trên Windows runner; 403/404 tests PASS. Không phát hiện assertion nghiệp vụ sai trong log, bài kiểm thử đã PASS ở checkpoint trước.
- Tăng **riêng thời gian tối đa bài kiểm thử chỉnh sửa Nháp nhiều bước** lên 15 giây, giữ nguyên toàn bộ assertion: dữ liệu prefill từ API, `PUT` chứa `expectedDraftRevision`, idempotency gắn payload, không gọi POST, đóng editor và mở lại chi tiết. Không tăng timeout toàn suite, không bỏ/chỉnh assertion và không sửa nghiệp vụ production.
- Bản sửa chỉ được chấp nhận khi CI HEAD kế tiếp PASS thực tế; không suy đoán hết flake. Browser QA và staging API/SQL vẫn là gate độc lập.
