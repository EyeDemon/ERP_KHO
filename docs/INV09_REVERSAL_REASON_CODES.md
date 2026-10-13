# INV-09 — Mã lý do đảo giao dịch nội bộ

## Phạm vi hiện thực
Bổ sung **Reason Code bắt buộc** cho đảo giao dịch `Move` và `StatusChange` trong Inventory Control.
Đây **chưa phải** toàn bộ Reason Code Registry hoặc hệ thống đảo chứng từ theo Spec 32 trong Notion. Notion **chỉ đọc**, không cập nhật.

## Hợp đồng API
- `GET /api/inventory/reversal-reasons`, quyền `inventory_ledger.read`: trả danh mục `code`, `name`, `transactionType` (null khi dùng cho cả hai nguồn).
- `POST /api/inventory/reversals`, quyền `inventory_reversal.create`, yêu cầu `Idempotency-Key`:
  `{"originalTransactionId":123,"reasonCode":"LOCATION_ERROR","reason":"Di chuyển sai vị trí đã xác minh"}`.
- `reasonCode`: bắt buộc với yêu cầu mới, chữ in hoa chuẩn hóa, tối đa 40 ký tự; từ chối mã không thuộc danh mục hoặc không đúng nguồn.
- Mã cho `Move`: `LOCATION_ERROR`; mã cho `StatusChange`: `STATUS_ERROR`; dùng chung: `OPERATION_CORRECTION`, `DATA_ENTRY_ERROR`.
- `reason`: diễn giải/bằng chứng thực tế bắt buộc, tối đa 400 ký tự, độc lập với mã lý do.
- Kết quả thành công trả `reasonCode`; API truy vết hiển thị `reasonCode` của dấu đảo.

## Dữ liệu SQL và lịch sử
- Migration `20261008230000_AddInventoryReversalReasonCode` tạo cột **nullable** `InventoryTransactions.ReasonCode NVARCHAR(40)`.
- **Không backfill** dữ liệu lịch sử: bản ghi cũ không có mã xác thực phải tiếp tục giữ `NULL`; không sửa/xóa giao dịch đã ghi sổ.
- Ghi mã vào *reversal marker* mới; `AuditLog.NewValues` lưu `ReasonCode` cùng ID giao dịch gốc, hiệu chỉnh và dấu đảo.
- Marker, bước hiệu chỉnh, audit nằm trong cùng giao dịch có kiểm tra tồn, khóa, quyền kho, chống đảo trùng và idempotency hiện hành.
- Danh mục hiện là bộ mã cố định cho phạm vi này. **Không** tự tuyên bố có bảng mã lý do cấu hình toàn hệ thống.

## Kiểm thử & giới hạn nghiệm thu
- xUnit: kiểm tra mã bắt buộc, giới hạn độ dài, loại nguồn, quyền API.
- SQL Server: sai mã cho loại giao dịch → không ghi marker/audit; mã hợp lệ → marker, audit, kết quả API, truy vết nhất quán.
- Frontend/Vitest: tải danh mục có kiểm soát, chặn gửi khi chưa chọn mã hoặc danh mục lỗi, kiểm tra mã theo loại nguồn, không tái dùng khóa idempotency khi thay đổi mã/lý do; Vercel demo chỉ cho phép GET.
- **Chưa khẳng định runtime live:** cần chạy CI đúng HEAD, SQL migration thật trên staging, xác nhận rollback/retry, browser QA focus/keyboard/responsive và quyền người dùng; chưa đủ thì PR tiếp tục Draft.
- Receipt, Shipment, Transfer, Adjustment, Return, Scrap và Opening Balance correction cần đảo bằng quy trình chứng từ gốc, không cho phép đảo thẳng ledger trên màn này.
