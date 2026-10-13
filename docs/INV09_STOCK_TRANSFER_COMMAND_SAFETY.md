# INV-09 — An toàn thao tác phiếu điều chuyển trên giao diện

Phạm vi: trang Điều chuyển kho của hệ thống thật. Đây là lớp bảo vệ phía người dùng; kiểm tra quyền kho, khóa SQL, giao dịch, idempotency và tính bất biến của Ledger vẫn phải được thực thi tại backend.

## Các quy tắc đã triển khai

- Khóa idempotency của thao tác nhận hàng bao gồm toàn bộ nội dung kết quả nhận từng dòng. Gửi lại cùng nội dung sau lỗi mạng dùng lại khóa cũ; thay đổi số lượng tạo khóa mới, tránh gửi payload khác với khóa đã ghi nhận.
- Chặn gửi tạo phiếu lần thứ hai khi POST đầu tiên chưa kết thúc, kể cả hai sự kiện submit đến trong cùng một lượt render. Trong thời gian gửi, nút tạo và đóng biểu mẫu bị vô hiệu hóa; lỗi trả về vẫn cho phép thử lại.
- Chặn lệnh chuyển trạng thái trùng trong cùng lượt render bằng khóa đồng bộ, không chỉ dựa vào trạng thái React.
- Chỉ phản hồi mới nhất của danh sách điều chuyển được phép cập nhật danh sách, tổng trang, danh mục và trạng thái tải/lỗi. Phản hồi API cũ không được ghi đè kết quả của bộ lọc mới.

## Kiểm thử hồi quy

- Retry nhận hàng cùng payload giữ Idempotency-Key; chỉnh số lượng nhận đổi Idempotency-Key.
- Gửi form tạo phiếu hai lần khi POST chưa trả kết quả chỉ phát một POST.
- Phản hồi danh sách cũ đến sau truy vấn lọc mới không thay thế kết quả mới.

## Giới hạn nghiệm thu

Không coi kiểm thử React là bằng chứng API/SQL staging. PR vẫn Draft cho đến khi CI đúng SHA, browser QA và kiểm thử backend SQL/API staging hoàn tất.
