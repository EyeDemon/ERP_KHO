# INV-10 — Truy vết HU gốc/con đã xuất theo lô/sê-ri

Ngày triển khai: 09/10/2026. PR #31 Draft, nhánh module Inventory Control.

## Hợp đồng chỉ đọc
- `shipmentHuEvidence[]` trả bằng chứng liên kết HU chứa hàng từ `HandlingUnitContent` → dòng Picking đã xác thực → `StockAllocation` → Packing Session → Shipment có **SHIP ledger** cùng kho/sản phẩm/vị trí/trạng thái/lô/sê-ri trước `eventAnchorId`.
- Không chấp nhận chỉ cùng Packing Session là đủ: duyệt quan hệ `HandlingUnit.ParentHandlingUnitId` từ kiện chứa hàng đến kiện gốc, yêu cầu **gốc đó có bản ghi `ShipmentHandlingUnit` thực** thuộc Shipment và kho tương ứng. Mỗi cấp cha đều xác nhận cùng kho và phiên đóng gói. Vòng lặp, cha không tồn tại, sai kho/phiên hoặc kiện gốc chưa gắn Shipment đều bị loại.
- Phạm vi kho giới hạn bởi `IWarehouseAuthorizationService`; chỉ khởi tạo khi truy vấn có `productId` và ít nhất một mã lô/sê-ri, không mở rộng khi chỉ định tham chiếu chứng từ cụ thể.
- Giới hạn 100 dòng HU có content, tối đa 16 cấp tổ tiên và tối đa 2.000 HU gốc được xét; cờ `shipmentHuEvidenceTruncated` thông báo khi bất cứ mức giới hạn nào chạm hoặc các danh sách Shipment/Picking đầu vào bị cắt. Không thực hiện truy vấn HU toàn kho không giới hạn.
- API xuất `ShipmentId`, `WarehouseId`, `PickingTaskLineId`, HU gốc/chứa hàng, mã quét, ID cha, đường dẫn, số lượng đã đóng. Giao diện tiếng Việt dùng `UiCard`/`UiTableScroll`, có trạng thái rỗng và cảnh báo.

## Giới hạn nghiệp vụ
- Cấu trúc HU là **trạng thái hiện tại** (không phải ảnh chụp lịch sử tại thời điểm SHIP). Chỉ Ledger xuất giao được giới hạn bằng `eventAnchorId`.
- Đây là **bằng chứng đóng gói/lấy hàng để hỗ trợ rà soát thu hồi**, không phải bằng chứng đã giao khách, không tự xác định chủ hàng hoặc dòng Receipt/QC nguồn, không trừ hàng hoàn, không tạo lệnh recall hay biến đổi Ledger.
- `PackedQuantity`, `PickedQuantity`, `DispatchedQuantity` là các chỉ tiêu khác nhau; không tự đối chiếu hoặc coi chúng tương đương nếu chứng từ có nhiều dòng.

## Bằng chứng QA yêu cầu
- SQL Server: chuỗi thực Pallet → Carton có content sau Shipment dispatch, chống lộ HU gốc không gắn Shipment dù cùng phiên Packing; kiểm tra lọc lô không khớp, giữ mốc Ledger và thu hồi quyền kho.
- API contract JSON camelCase, Vitest giao diện Việt ngữ, cảnh báo giới hạn, lọc theo tham chiếu và warehouse-only không mở rộng.
- Chỉ được ghi PASS sau khi CI của đúng SHA hoàn tất; browser QA tương tác và staging backend SQL/API vẫn cần nghiệm thu. PR Draft, không merge, Notion chỉ đọc.
