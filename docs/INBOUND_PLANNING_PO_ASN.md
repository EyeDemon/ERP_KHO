# Inbound Planning — Purchase Order & ASN

## Mục tiêu

Checkpoint này bổ sung lớp **expected inbound** phía trước Phiếu nhập:

`Purchase Order → ASN → Receipt/Receiving → QC → Post → Cất hàng`

Purchase Order và ASN là dữ liệu kế hoạch/điều phối nhận hàng. Chúng **không tạo InventoryStock, không tạo InventoryTransaction và không tăng On Hand**. Goods Receipt POST vẫn là inventory boundary duy nhất.

## Purchase Order

State machine:

`Nháp → Đang mở → Đã nhận một phần → Đã nhận đủ → Đã đóng`

Nhánh hủy hiện hỗ trợ Nháp/Đang mở; cập nhật trạng thái nhận một phần/đủ sẽ được nối ở checkpoint Receipt integration kế tiếp.

External identity là `SourceSystem + ExternalPoId`, duy nhất để tránh tạo trùng do retry. Dòng PO snapshot UOM, conversion factor/version và Base UOM tại thời điểm tạo/cập nhật.

## ASN

State machine:

`Nháp → Đã xác nhận → Đang vận chuyển → Đã đến → Đang tiếp nhận → Hoàn tất ASN`

Nháp/Đã xác nhận có thể hủy. ASN tham chiếu PO chỉ được lập khi PO đang mở hoặc đã nhận một phần. Tổng Base-UOM ASN đang hoạt động của một PO line không được vượt Ordered Base Qty cộng dung sai nhận vượt.

**Hoàn tất ASN không có nghĩa là hàng đã POST vào tồn kho.**

## Security

Mọi read/mutation đều warehouse-scoped. Direct ID ngoài warehouse scope trả not-found semantics.

Permission:
- `purchase_order.read/create/update/release/close/cancel`
- `asn.read/create/update/confirm/receive/cancel`

Viewer chỉ có read. WarehouseStaff có read PO/ASN và `asn.receive`. Admin/Manager nhận full capability trong migration bootstrap.

## Atomicity & audit

Create PO/ASN persist entity và audit trong cùng transaction, kể cả khi chạy ngoài idempotency middleware. Audit phải trỏ đúng persisted entity ID.

Command mutations sử dụng existing Idempotency-Key/fingerprint middleware và RowVersion conflict contract.

## Deferred sang checkpoint kế tiếp

- Link Receipt với PO/ASN.
- Receipt POST cập nhật PO received quantities/state.
- Tạo Receipt trực tiếp từ PO/ASN expected lines.
- Reconciliation Ordered / ASN / Received / Posted.
- Production frontend PO/ASN work centers.
- Browser QA toàn tuyến Inbound.
