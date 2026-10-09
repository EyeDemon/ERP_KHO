# INV-09 — Đồng thời khi ghi sổ điều chuyển

## Mục tiêu
Chặn xuất, nhận và hoàn trả theo dữ liệu chi tiết hoặc cặp kho đã thay đổi trong một giao dịch khác.

## Rào chắn ở backend
- Cả ba thao tác ghi sổ chạy trong transaction SQL Server `Serializable`.
- Đọc header chứng từ bằng `UPDLOCK, HOLDLOCK` trước khi lấy dòng sản phẩm, đối chiếu kho được cấp quyền và chuyển trạng thái.
- Lệnh CAS chuyển trạng thái kiểm tra cả trạng thái và đúng cặp kho nguồn/đích đã kiểm tra.
- Nếu không còn đúng điều kiện, trả xung đột 409 và rollback số dư, sổ cái và audit; tuyệt đối không sửa ledger lịch sử.
- Chỉnh sửa phiếu Nháp cũng dùng khóa header để không thể chen vào giữa bước đọc chi tiết và ghi sổ.

## Kiểm chứng và giới hạn
- SQL Server regression xác minh lệnh xuất lặp bị từ chối, chỉ một giao dịch `TransferOut` và một audit được ghi.
- Cần chạy thêm kiểm thử cạnh tranh cập nhật Nháp/duyệt/xuất, kiểm thử trình duyệt và staging API/SQL thật trước khi merge.
- Vercel Demo READY không xác nhận tính đúng đắn của SQL Server.
