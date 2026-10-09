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
- **Chỉnh sửa phiếu Nháp** trực tiếp tại màn chi tiết: sửa kho, số lượng, ghi chú qua `PUT /api/stock-transfers/{id}`, cùng khóa idempotency gắn với mã phiếu và nội dung payload; biểu mẫu cho phép sửa trước khi duyệt và tải lại chi tiết sau lưu.
- Backend giữ khóa `UPDLOCK, HOLDLOCK` của phiếu trong giao dịch `Serializable` trước khi xác thực `Draft` và thay thế các dòng, không để phê duyệt đồng thời chen vào giữa. Các lệnh duyệt, hoàn tất và hủy kiểm tra lại cặp kho gốc/đích trong điều kiện cập nhật SQL để không xác nhận chứng từ đã bị đổi kho sau khi phân quyền.
- Khi cập nhật trả xung đột `409`, giao diện giữ nội dung để người vận hành kiểm tra, **khóa nút lưu để không gửi lại dữ liệu đã lỗi thời**, đồng thời cung cấp **Tải lại phiên bản mới**. Nếu phiếu vẫn Nháp thì nạp lại nội dung hiện hành; nếu đã duyệt thì đóng trình sửa và mở chi tiết chỉ đọc. Không dùng lại dữ liệu cũ để tự động ghi đè.
- Từ chối phiếu đã duyệt/xuất hoặc phiếu điều chuyển ngược; dữ liệu sai phải rollback đầy đủ. Dữ liệu lịch sử posted không được sửa.

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
- Khi nhiều phiếu khác nhau cùng nhận vào một bucket tồn kho đích (sản phẩm + kho + vị trí `LEGACY` + trạng thái), hệ thống giữ `sp_getapplock` độc quyền theo **bucket**, `LockOwner=Transaction` trước khi `MERGE`. Cơ chế này tránh deadlock chuyển khóa trên bucket chưa có dòng, không khóa nguyên kho; khóa được nhả cùng commit/rollback của phiếu, ledger và audit. Không chuyển lỗi deadlock thành thành công giả.
- Bài test `FourIndependentReceiptsIntoMissingCanonicalBucketAreSerializedWithoutDuplicateLedger` tạo bốn phiếu độc lập nhận đồng thời vào bucket không có sẵn, đối chiếu tồn đầu/cuối, đúng một bản ghi bucket, đúng bốn `TransferIn` và bốn audit nhận. Bài test cũ với hai phiếu vẫn giữ nguyên.

## Phân quyền và dữ liệu

- Quyền tạo/duyệt/xuất/nhận theo vai trò và phạm vi kho; xem phiếu khi có quyền trên ít nhất một đầu kho, nhưng ghi phải thỏa quyền tương ứng.
- Hoàn trả/điều chuyển ngược yêu cầu `inventory_reversal.create` và quyền kho liên quan.
- SQL giữ nguyên ledger gốc, liên kết đảo khi áp dụng và audit. Không cho phép sửa/xóa lịch sử posted.
- Môi trường Vercel Blueprint chỉ là bản demo frontend (read-only), **không** chứng minh backend SQL staging/production đã triển khai.

## Kiểm thử & nghiệm thu

- `SqlServerStockTransferTests`: thiếu phân bổ, vượt, số âm, quá 4 số thập phân và decimal quá lớn đều bị từ chối; xác minh không ghi thêm hàng tồn, ledger, audit hoặc chuyển phiếu sang Received; sau đó nhận đủ số lượng với khai báo thực nhận/thiếu/hỏng được phép.
- `StockTransferCommandSafety.test.tsx`: kiểm tra trước khi gửi API, khóa idempotency gắn với payload, chỉ gửi khi tổng từng dòng khớp số đã xuất.
- Khi thay đổi module: chỉ CI ở mốc batch đáng kể, xác minh bằng chứng SQL/API/Frontend theo đúng HEAD, và thực hiện QA trình duyệt + staging backend/SQL trước khi nghiệm thu production.
