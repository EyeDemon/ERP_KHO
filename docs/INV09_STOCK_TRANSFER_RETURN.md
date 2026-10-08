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

## Truy vết native return (batch bổ sung 09/10)

- `TransferOut` gốc và `TransferIn` hoàn trả giữ nguyên trong immutable ledger; dòng hoàn trả liên kết `ReversalOfTransactionId` đến giao dịch xuất gốc. Không tạo thêm marker `TransactionType.Reversal` cho luồng chứng từ này.
- `GET /api/inventory/traceability?referenceType=StockTransfer&referenceId=<id>` phải trả đầy đủ chuỗi gốc–hoàn trả ngay cả khi giới hạn sự kiện chỉ chứa một dòng. Giao dịch gốc được hiển thị `isReversed=true`, liên kết đến giao dịch hoàn trả.
- Từ chi tiết chứng từ đã hoàn trả, người có `inventory_traceability.read` có liên kết SPA sang trang Truy vết; không có quyền thì không hiển thị liên kết. API vẫn tự kiểm tra warehouse scope.
- Trước khi hoàn trả, backend từ chối khi bất kỳ `TransferOut` gốc nào đã có `ReversalOfTransactionId` trỏ tới, kể cả marker lịch sử. Trạng thái chứng từ và tồn kho không được thay đổi khi từ chối.
- SQL Server regression kiểm tra chuỗi chỉ lấy 1 sự kiện, chống đảo trùng từ marker tồn tại, và giữ nguyên audit/tồn. React regression kiểm tra deep link và quyền truy vết.

**Giới hạn:** Không đồng nghĩa hỗ trợ đảo Receipt/Shipment/Adjustment/Return/Scrap; không xác nhận browser QA hoặc backend staging. PR vẫn Draft.
