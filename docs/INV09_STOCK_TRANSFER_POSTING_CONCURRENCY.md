# INV-09 — Đồng thời khi ghi sổ điều chuyển

## Mục tiêu
Chặn xuất, nhận và hoàn trả theo dữ liệu chi tiết hoặc cặp kho đã thay đổi trong một giao dịch khác.

## Rào chắn ở backend
- Xuất kho dùng transaction `READ COMMITTED`, khóa chứng từ `UPDLOCK, HOLDLOCK` và `sp_getapplock` độc quyền theo sản phẩm/kho nguồn trước khi đọc các bucket tồn. Nhận/hoàn trả dùng `READ COMMITTED` và khóa bucket đích. Các khóa ghi giữ đến commit/rollback.
- Đọc header chứng từ bằng `UPDLOCK, HOLDLOCK` trước khi lấy dòng sản phẩm, đối chiếu kho được cấp quyền và chuyển trạng thái.
- Lệnh CAS chuyển trạng thái kiểm tra cả trạng thái và đúng cặp kho nguồn/đích đã kiểm tra.
- Nếu không còn đúng điều kiện, trả xung đột 409 và rollback số dư, sổ cái và audit; tuyệt đối không sửa ledger lịch sử.
- Chỉnh sửa phiếu Nháp cũng dùng khóa header để không thể chen vào giữa bước đọc chi tiết và ghi sổ.

## Kiểm chứng và giới hạn
- SQL Server regression `CompetingTransfersCannotDriveSourceStockNegative` xác minh hai phiếu độc lập cùng xuất 8 từ tồn 10: đúng một phiếu thành công, không âm tồn, không được trả deadlock 1205 như kết quả nghiệp vụ hợp lệ. Cần đối soát thêm ledger và audit trong CI.
- Cần chạy thêm kiểm thử cạnh tranh cập nhật Nháp/duyệt/xuất, kiểm thử trình duyệt và staging API/SQL thật trước khi merge.
- Vercel Demo READY không xác nhận tính đúng đắn của SQL Server.
