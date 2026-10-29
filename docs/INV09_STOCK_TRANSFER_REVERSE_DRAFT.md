# INV-09 — Điều chuyển ngược sau khi kho đích đã nhận (native document)

## Phạm vi

Chứng từ gốc ở trạng thái `Received` hoặc `Completed` không được đảo trực tiếp `TransferOut`/`TransferIn` trên ledger. Người có `inventory_reversal.create` và quyền cả hai kho có thể tạo **một phiếu điều chuyển ngược mới ở trạng thái Draft** bằng `POST /api/stock-transfers/{id}/reverse-draft`.

API bắt buộc `reasonCode` thuộc danh mục kiểm soát `GET /api/stock-transfers/return-reasons` và `reason` dài 1–400 ký tự. Phiếu ngược đảo chiều kho; mỗi dòng chỉ lấy `ReceivedQuantity > 0` đã được xác nhận, không lấy số thiếu/hỏng. Người dùng phải duyệt (maker-checker), xuất, nhận và hoàn tất phiếu mới qua các endpoint điều chuyển hiện hữu. Khi xuất, hệ thống kiểm tra tồn khả dụng thực tế tại kho đã nhận; nếu hàng đã dịch chuyển tiếp thì không thể xuất vượt tồn.

## Bất biến

- `ReverseOfTransferId` là liên kết chứng từ bất biến, có FK và unique filtered index để không thể lập hai phiếu ngược cho cùng một phiếu gốc, kể cả với hai idempotency key khác nhau.
- SQL Server transaction `Serializable` và `UPDLOCK, HOLDLOCK` trên chứng từ gốc bảo vệ cuộc đua tạo phiếu; endpoint có `IdempotentCommand`.
- Người gọi phải có quyền cả kho nguồn và kho đích. Chứng từ ngược không thể được chỉnh sửa bằng API cập nhật phiếu nháp, nhằm giữ nguyên nguồn gốc và số lượng đã nhận.
- Tạo Draft **không ghi tồn kho, không ghi InventoryTransactions**. Ledger cũ không thay đổi. Ledger mới chỉ được tạo bởi quy trình Dispatch/Receive hiện hữu.
- Trước khi tạo Draft, đối chiếu **từng dòng** số lượng xuất/nhận của chứng từ với `TransferOut` tại kho nguồn và `TransferIn` tại kho đích trong immutable Ledger. Nếu thiếu, thừa hoặc sai số lượng thì từ chối và yêu cầu đối soát; không tạo Draft/Audit mới. Bài kiểm thử SQL Server dùng dữ liệu synthetic sai lệch xuất/nhận và trường hợp thực nhận một phần.
- Lý do lưu tại `ReverseReasonCode`/`ReverseReason` trên chứng từ mới và `AuditLog`. Không backfill liên kết cho chứng từ lịch sử.
- UI tiếng Việt dùng danh mục lý do từ API, khóa nút khi tải thất bại hoặc không có quyền, và mở phiếu Draft vừa tạo.

## Giới hạn nghiệm thu

Batch có SQL Server integration, API test và React/Vitest. CI trên SHA mới, browser QA thật và xác minh staging SQL/API vẫn là các gate độc lập. Chưa triển khai đảo Receipt, Shipment, Adjustment, Return hoặc Scrap. Không chuyển System Blueprint sang trạng thái live và không merge PR khi chưa đủ bằng chứng.
