# INV-10 — Bằng chứng phiếu xuất kho nguồn của Shipment đã dispatch

Ngày: 09/10/2026. PR #31 Draft. Trạng thái: `foundation`.

## Phần triển khai thật
- Mở rộng API `GET /api/inventory/traceability` bằng `shipmentExportSources[]` và `shipmentExportSourcesTruncated`, chỉ khi bộ lọc có `productId` và `lotNumber`/ `serialNumber`, không có `referenceType`/ `referenceId`.
- Phải có Ledger bất biến `SHIP` trong danh sách Shipment đã xác minh trước `eventAnchorId`. Trên cùng Shipment đó, bắt buộc `Shipment.SourceType/SourceId` và `PickingTask.SourceType/SourceId` cùng tham chiếu một `ExportReceipt` thực trong kho được quyền xem; `PackingSession` và `PickingTask` đúng kho.
- Phiếu xuất phải đang ở trạng thái `Dispatched`, có `DispatchedAt` và có ít nhất một `ExportReceiptDetail` hợp lệ chứa sản phẩm được truy vết.
- Trả ID/mã Shipment, ID nhiệm vụ Picking, ID/mã phiếu xuất, thời điểm xuất và **tổng số lượng sản phẩm trên toàn phiếu xuất**. Tổng này không phải lượng của lô/sê-ri đang tìm; số lượng giao thực xem ở Shipment Ledger riêng.
- Giới hạn 100 Shipment và 101st sentinel; nếu tập Shipment đầu vào đã bị cắt thì giữ cờ `shipmentExportSourcesTruncated`. Không có dữ liệu xuyên kho hoặc mở rộng quyền ngầm.
- Giao diện tiếng Việt: bảng chỉ đọc, giải thích rõ mức độ bằng chứng và cảnh báo giới hạn, không có thao tác sửa/đảo giao dịch, tuân thủ `design-system/erp-wms/MASTER.md`.

## Kiểm thử regression
- SQL Server: fixture theo lô với Shipment dispatch thực + phiếu xuất nguồn chuyển trạng thái đồng bộ, xác nhận một nguồn hợp lệ; sửa nguồn Picking sang ID không khớp hoặc đưa phiếu xuất về Approved thì không còn nguồn xác thực, nhưng Ledger SHIP không bị thay đổi; nguồn không xuất hiện nếu lô sai, truy vấn kho-only hoặc lọc riêng một chứng từ.
- API: JSON camelCase, các trường số lượng và trạng thái truncated. Vitest: nội dung tiếng Việt, không suy diễn nguồn nhập theo lô, giới hạn kết quả và bộ lọc.
- Giữ mốc lịch sử `eventAnchorId` cho Ledger SHIP; thông tin phiếu xuất là trạng thái nghiệp vụ hiện tại, không phải snapshot vào thời điểm mốc.

## Chưa hoàn thiện/không tuyên bố
- `ExportReceiptDetail` không có LotId/SerialId, do đó liên kết nguồn phiếu xuất này chứng minh **chứng từ xuất cấp sản phẩm**, không chứng minh provenance của một lô cụ thể, phiếu nhập nguồn, QC, người nhận cuối hoặc việc giao hàng.
- Không thực hiện return/recall hoặc đảo Ledger. Chưa nghiệm thu browser QA, staging SQL/API thật. Giữ PR Draft; không merge, Notion chỉ đọc.
