# INV-10 — Truy vết chứng từ nhập kho đã ghi sổ

## Phạm vi nghiệp vụ (09/10/2026)
- Truy vấn chỉ đọc `receiptExposures[]` và `receiptExposuresTruncated` yêu cầu ID sản phẩm + mã lô/sê-ri, không mở rộng khi đã chọn tham chiếu chứng từ cụ thể.
- SQL yêu cầu giao dịch `Import` bất biến có `ReferenceType=ImportReceipt`, đồng thời phải tồn tại phiếu `ImportReceipt` trạng thái `Posted` **cùng kho được cấp quyền**. Phiếu nháp và tham chiếu không có chứng từ không được tính.
- Kết quả gom theo phiếu nhập: mã, kho, số lượng nhập Base UOM, số sự kiện, ngày và ID giao dịch cuối; giới hạn 100 phiếu với bản ghi thứ 101 làm tín hiệu cảnh báo.
- Mốc `eventAnchorId` áp dụng cho sự kiện nhập, kể cả giao dịch mới được ghi với ngày quá khứ. Trạng thái chứng từ là giá trị hiện tại.
- Đây chỉ là bằng chứng ghi sổ nhập: **không** suy luận kết quả QC riêng từng lô, phả hệ chủ sở hữu/HU, nguồn trực tiếp của Shipment hoặc hoàn tất thu hồi.
- Giao diện Việt ngữ, bảng chỉ đọc, trạng thái trống và cảnh báo giới hạn. Không sửa ledger, schema, quyền hoặc Notion.

## Kiểm thử và điều kiện nghiệm thu
- SQL Server: phiếu Posted, phiếu Draft, tham chiếu không tồn tại, truy vết theo lô, mốc cố định trước giao dịch ghi sau.
- API: JSON contract. Vitest: bảng, cảnh báo 100, không mở rộng khi chọn tham chiếu.
- CI phải PASS đúng SHA; browser QA và staging backend SQL/API vẫn chưa nghiệm thu. INV-10 giữ `foundation`, PR #31 giữ Draft.
