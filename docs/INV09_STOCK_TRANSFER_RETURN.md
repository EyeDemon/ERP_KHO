# INV-09 — Hoàn trả phiếu điều chuyển đang vận chuyển (batch)

## Phạm vi nghiệp vụ

Theo Notion 32 (chỉ đọc): trước xuất kho hủy chứng từ; sau xuất kho nhưng trước nhận phải dùng **quy trình hoàn trả có kiểm soát**; sau nhận phải lập chứng từ điều chuyển ngược mới, không đảo trực tiếp sổ cái.

Batch này bổ sung **POST /api/stock-transfers/{id}/return** cho phiếu ở trạng thái `InTransit`. Trạng thái cuối `Returned = 6` giữ nguyên số enum cũ, không viết lại phiếu đã ghi sổ. **GET /api/stock-transfers/return-reasons** trả danh mục mã lý do cố định. Mọi yêu cầu hoàn trả bắt buộc mã lý do hợp lệ và diễn giải không quá 400 ký tự.

## Invariant

- Chỉ Manager/WarehouseStaff/Admin được thực hiện, đồng thời bắt buộc có quyền cả kho nguồn và kho đích.
- Chỉ hoàn trả trước khi có xác nhận nhận hàng; không hỗ trợ hoàn trả một phần.
- Bắt buộc xác minh `TransferOut` gốc theo phiếu, kho nguồn, sản phẩm, số lượng.
- Trong transaction SQL, claim có điều kiện `InTransit -> Returned` cạnh tranh với `InTransit -> Received`; thất bại trả xung đột, không sinh thêm sổ cái.
- Mỗi dòng tạo `TransferIn` bù tại kho nguồn, giữ `ReferenceType = StockTransfer`, `ReferenceId` và liên kết `ReversalOfTransactionId` về `TransferOut` bất biến. Unique index trên liên kết đảo ngăn đảo trùng.
- Phải có vị trí `LEGACY` tương thích để nhận hàng hoàn trả; lỗi làm rollback toàn bộ trạng thái, tồn, ledger, audit.
- Audit `StockTransfer.Returned` ghi mã lý do và diễn giải; UI tải mã từ API, fail-closed nếu không lấy được.
- Không dùng `POST /api/inventory/reversals` để đảo `TransferOut` trực tiếp.

## Chứng cứ và giới hạn

SQL Server integration: hoàn trả hợp lệ, không đảo hai lần, chặn nhận sau hoàn trả, mã lý do, phân quyền hai kho, rollback, tranh chấp đồng thời Receive/Return. API unit test xác nhận điều hướng đúng service; React/Vitest xác nhận mã lý do, diễn giải, idempotency và fail-closed.

Chưa triển khai hoàn trả sau `Received`, return-to-vendor, shipment, receipt, adjustment, scrap. Chưa nghiệm thu browser E2E và backend/SQL staging; Vercel Demo READY không đủ chứng minh backend hoạt động. PR vẫn Draft cho đến khi đạt các gate.
