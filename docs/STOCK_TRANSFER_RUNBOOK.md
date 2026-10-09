# Sổ tay điều chuyển kho — đối soát số lượng và hoàn trả

## Trạng thái giao dịch

```text
Nháp → Đã duyệt → Đang vận chuyển → Đã nhận → Hoàn tất
Nháp/Đã duyệt → Hủy
Đang vận chuyển → Hoàn trả kho nguồn
Đã nhận/Hoàn tất → Lập phiếu điều chuyển ngược (Nháp mới)
```

- Phiếu đã xuất kho không thể bị hủy như phiếu nháp. Hoàn trả khi **chưa nhận** phải đi qua lệnh `return`, tạo giao dịch hoàn kho nguồn và liên kết với chứng từ xuất gốc, không xóa sổ cái.
- Khi **đã nhận hoặc hoàn tất**, việc đưa hàng về kho nguồn phải tạo **phiếu điều chuyển ngược mới**, rồi duyệt, xuất và nhận theo quy trình thường.
- Các thao tác ghi sử dụng Idempotency-Key; backend kiểm tra quyền kho và trạng thái authoritative.

## Đối soát khi nhận

Với **từng sản phẩm** đã xuất, trước khi chuyển sang `Received` phải có:

```text
Thực nhận + Thiếu + Hỏng = Đã xuất
```

- Các phần không âm và có tối đa **4 chữ số thập phân**; không được vượt số đã xuất.
- Thiếu hoặc hỏng phải được khai báo rõ, không biến mất thành chênh lệch ngầm khi đóng phiếu.
- Không hỗ trợ nhận dở dang rồi chuyển phiếu sang trạng thái `Received`. Yêu cầu tương lai về partial receiving phải có trạng thái, ledger và reconciliation riêng.
- Chỉ cộng `Thực nhận` vào tồn kho đích; `Thiếu` và `Hỏng` nằm trên chi tiết chứng từ để điều tra. Nếu số liệu không hợp lệ, API từ chối trước bước chuyển trạng thái và ghi tồn/sổ cái.
- Nhận nhiều lần hoặc đồng thời bị ràng buộc chuyển trạng thái có điều kiện. Giao diện đối soát trước khi xác nhận; backend luôn là nguồn quyết định cuối cùng.

## Phân quyền và dữ liệu

- Quyền tạo/duyệt/xuất/nhận theo vai trò và phạm vi kho; xem phiếu khi có quyền trên ít nhất một đầu kho, nhưng ghi phải thỏa quyền tương ứng.
- Hoàn trả/điều chuyển ngược yêu cầu `inventory_reversal.create` và quyền kho liên quan.
- SQL giữ nguyên ledger gốc, liên kết đảo khi áp dụng và audit. Không cho phép sửa/xóa lịch sử posted.
- Môi trường Vercel Blueprint chỉ là bản demo frontend (read-only), **không** chứng minh backend SQL staging/production đã triển khai.

## Kiểm thử & nghiệm thu

- `SqlServerStockTransferTests`: thiếu phân bổ, vượt, số âm, quá 4 số thập phân và decimal quá lớn đều bị từ chối; xác minh không ghi thêm hàng tồn, ledger, audit hoặc chuyển phiếu sang Received; sau đó nhận đủ số lượng với khai báo thực nhận/thiếu/hỏng được phép.
- `StockTransferCommandSafety.test.tsx`: kiểm tra trước khi gửi API, khóa idempotency gắn với payload, chỉ gửi khi tổng từng dòng khớp số đã xuất.
- Khi thay đổi module: chỉ CI ở mốc batch đáng kể, xác minh bằng chứng SQL/API/Frontend theo đúng HEAD, và thực hiện QA trình duyệt + staging backend/SQL trước khi nghiệm thu production.
