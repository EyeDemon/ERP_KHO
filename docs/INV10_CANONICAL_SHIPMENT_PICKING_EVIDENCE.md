# INV-10 — Bằng chứng Shipment → Packing → Picking theo lô/sê-ri

Ngày: 09/10/2026 · Trạng thái: **foundation** trong PR #31 Draft.

## Phạm vi đã triển khai
- Khi truy vấn có `productId` cùng lô/sê-ri và **không** chọn tham chiếu chứng từ cụ thể, kết quả chỉ đọc trả `shipmentPickingEvidence[]` và `shipmentPickingEvidenceTruncated`.
- Mỗi dòng đại diện cho **một dòng Picking thực** nối bằng FK `Shipment.PackingSessionId → PackingSession.PickingTaskId → PickingTaskLine.AllocationId`, cùng kho, cùng sản phẩm, đúng vị trí xuất, trạng thái tồn, LotId và SerialId.
- Chỉ chấp nhận liên kết khi có **giao dịch SHIP bất biến** cùng Shipment/kho/sản phẩm/vị trí/trạng thái/lô/sê-ri, tại hoặc trước `eventAnchorId`. Shipment phải có trong danh sách chứng từ đã xuất và được phép của lần truy vấn hiện tại.
- Luồng luôn kiểm tra phạm vi kho qua `IWarehouseAuthorizationService`, không mở rộng kho từ ID do người dùng truyền vào.
- Danh sách giới hạn 100 dòng (lấy thêm bản ghi 101 để xác định `shipmentPickingEvidenceTruncated`). Nếu có hơn 100 Shipment, cảnh báo riêng của `shipmentExposuresTruncated` vẫn được hiển thị.
- Giao diện sử dụng thành phần ERP chung, chỉ đọc, tiếng Việt, có trạng thái rỗng và thông báo cần thu hẹp truy vấn.

## Không được suy luận
- Đây là liên kết nghiệp vụ thực từ Shipment tới Picking/Packing. Không chứng minh phiếu nhập nào là nguồn trực tiếp của Shipment, quan hệ QC tại từng lô, chuỗi Handling Unit gốc–con hay người nhận cuối.
- `PickedQuantity` là số đã Picking, **không** phải số đã giao; gross SHIP quantity là một chỉ tiêu khác.
- Không tạo lệnh recall, đổi trạng thái, sửa Ledger, thêm permission hoặc migration.

## Kiểm thử
- SQL Server integration dùng Shipment dispatch thật theo lô, xác minh FK Packing/Picking/Allocation/vị trí/quantity; giao dịch SHIP giả không có Shipment không tạo bằng chứng; cùng `eventAnchorId` không bị thay đổi do giao dịch giả ghi sau.
- API JSON serialization đảm bảo camelCase và trường dữ liệu đúng.
- Vitest: bảng liên kết, bộ lọc, giới hạn, chỉ hiển thị khi có sản phẩm và lô/sê-ri; lint/build CI phải PASS.
- **Chưa nghiệm thu**: browser QA tương tác, staging API/SQL thật, phả hệ end-to-end Receipt→QC→Pick→Shipment→Return/Recall; giữ PR Draft, Notion chỉ đọc và không nâng Blueprint thành live.
