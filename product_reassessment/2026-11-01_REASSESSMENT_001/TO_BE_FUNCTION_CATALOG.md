# TO-BE FUNCTION CATALOG — Danh Mục Chức Năng Nghiệp Vụ Toàn Hệ Thống

**Document ID:** Deliverable I (`TO_BE_FUNCTION_CATALOG.md`)  
**Run ID:** `2026-11-01_REASSESSMENT_001`  
**Total Cataloged Functions:** 132 Business Functions across 8 Bounded Context Modules (`M01` → `M08`)  
**Compliance Rules:** `SEC-001`, `SEC-002`, `API-001..004`, `INV-001..007`, `FE-001..007`

---

## 1. Quy Ước Phân Loại & Trạng Thái Thực Thi

### 1.1 Giải thích Trạng thái Thực thi (`Implementation Status`)
- **`VERIFIED_COMPLETE` (`FACT`):** Có đầy đủ Entity/Domain Service, API Endpoint, giao diện React và kiểm thử hoạt động.
- **`NEEDS_REFACTOR` (`FACT`):** Đã hoạt động đầy đủ nhưng nằm trong God Component (`>1,000 LOC`) hoặc dùng đường dẫn API PascalCase cũ cần chuẩn hóa về `/api/v1/<kebab-case-plural>`.
- **`PARTIAL` (`FACT`):** Đã có khung giao diện và API nhưng cần bổ sung hoàn chỉnh luồng nghiệp vụ hoặc đồng bộ RBAC/State Machine.
- **`BACKEND_ONLY_NEEDS_UI` (`FACT`):** Đã có bảng dữ liệu (`CustomerReturn`, `KitWorkOrder`, `ThreePlBillingStatement`) và API ở Backend nhưng chưa có màn hình/workbench chuyên biệt trên Frontend.

### 1.2 Giải thích Chế độ Hiển thị (`Express vs. Full Mode`)
- **`BOTH (Express + Full)`:** Hiển thị trực tiếp ở cả chế độ Rút gọn (5–10 nhân sự) và Đầy đủ (10–30+ nhân sự).
- **`EXPRESS_STREAMLINED`:** Ở chế độ Express được gộp thành thao tác 1 bước (ví dụ: Nhận hàng & Ghi sổ ngay); ở chế độ Full tách thành các bước độc lập.
- **`FULL_DEFAULT (Tab in Express)`:** Ở chế độ Express được ẩn khỏi thanh menu chính để giảm rối mắt nhưng vẫn truy cập được qua Tab bên trong màn hình cha hoặc qua URL trực tiếp.

---

## 2. Module `M01`: Trung Tâm Điều Hành & Phân Tích (`Command Center & Control Tower`)

| Mã CN (`Function ID`) | Tên chức năng (Vietnamese Name) | Module | Vai trò (`Actor`) | Chế độ (`Express / Full`) | Trạng thái (`Status`) | Mã quyền (`Permission Code`) | API Endpoint Chuẩn (`TO-BE` / `AS-IS`) | Ưu tiên |
|---|---|---|---|---|---|---|---|---|
| `FN-CTL-001` | Bảng điều khiển vận hành kho thời gian thực | `M01` | Tất cả vai trò | `BOTH` | `VERIFIED_COMPLETE` | `(Authenticated)` | `GET /api/v1/reports/operational-dashboard` (`/api/Reports/operational-dashboard`) | `P0` |
| `FN-CTL-002` | Tháp điều khiển đa kho (Multi-Warehouse Control Tower) | `M01` | Quản lý, Admin | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `warehouse.read` | `GET /api/v1/reports/multi-warehouse-control-tower` | `P1` |
| `FN-CTL-003` | Hàng đợi cảnh báo ngoại lệ vận hành (Operational Exceptions) | `M01` | Quản lý, Thủ kho | `BOTH` | `VERIFIED_COMPLETE` | `inventory.read` | `GET /api/v1/reports/operational-exceptions` | `P0` |
| `FN-CTL-004` | Xác nhận & đóng cảnh báo ngoại lệ (Acknowledge Exception) | `M01` | Quản lý, Thủ kho | `BOTH` | `VERIFIED_COMPLETE` | `inventory.read` | `POST /api/v1/reports/operational-exceptions/acknowledge` | `P1` |
| `FN-CTL-005` | Hàng đợi tác vụ kho tổng hợp (Task Engine Queue) | `M01` | Thủ kho, Nhân viên kho | `BOTH` | `VERIFIED_COMPLETE` | `inventory.read` | `GET /api/v1/reports/warehouse-tasks` | `P0` |
| `FN-CTL-006` | Tìm kiếm toàn cục đa thực thể (Global Enterprise Search) | `M01` | Tất cả vai trò | `BOTH` | `VERIFIED_COMPLETE` | `(Authenticated)` | `GET /api/v1/reports/global-search?q={query}` | `P1` |
| `FN-CTL-007` | Nhật ký hoạt động kho gần nhất (Recent Activity Feed) | `M01` | Quản lý, Thủ kho | `BOTH` | `VERIFIED_COMPLETE` | `inventory.read` | `GET /api/v1/reports/operational-dashboard` | `P2` |
| `FN-CTL-008` | Thẻ tóm tắt tài chính & lãi gộp chủ hàng trên Dashboard | `M01` | Chủ kho, Kế toán, Quản lý | `BOTH` | `VERIFIED_COMPLETE` | `inventory.cost.read` | `GET /api/v1/reports/owner-gross-profit` | `P1` |

---

## 3. Module `M02`: Nhập Kho & Tiếp Nhận Hàng Hóa (`Inbound & Receiving`)

| Mã CN (`Function ID`) | Tên chức năng (Vietnamese Name) | Module | Vai trò (`Actor`) | Chế độ (`Express / Full`) | Trạng thái (`Status`) | Mã quyền (`Permission Code`) | API Endpoint Chuẩn (`TO-BE` / `AS-IS`) | Ưu tiên |
|---|---|---|---|---|---|---|---|---|
| `FN-INB-001` | Quản lý danh mục cửa kho (Dock Doors) & vị trí sân bãi (Yard Slots) | `M02` | Điều phối bãi, Quản lý | `FULL_DEFAULT` | `NEEDS_REFACTOR` | `dock.read`, `dock.manage` | `GET/POST /api/v1/dock-yard/docks`, `/yard-slots` | `P2` |
| `FN-INB-002` | Đặt lịch hẹn xe vào kho (Dock Appointment Scheduling) | `M02` | Điều phối bãi, Mua hàng | `FULL_DEFAULT` | `NEEDS_REFACTOR` | `dock_appointment.read`, `dock_appointment.manage` | `GET/POST /api/v1/dock-yard/appointments` | `P2` |
| `FN-INB-003` | Check-in phương tiện tại cổng bảo vệ & kiểm tra niêm phong (Seal) | `M02` | Bảo vệ / Điều phối bãi | `FULL_DEFAULT` | `NEEDS_REFACTOR` | `yard.checkin` | `POST /api/v1/dock-yard/appointments/{id}/check-in` | `P2` |
| `FN-INB-004` | Phân bổ cửa kho tự động/thủ công & giám sát thời gian neo đậu (Dwell SLA) | `M02` | Điều phối bãi | `FULL_DEFAULT` | `NEEDS_REFACTOR` | `yard.assign_dock`, `yard.checkout` | `POST /api/v1/dock-yard/appointments/{id}/assign-dock` | `P2` |
| `FN-INB-005` | Tạo & quản lý Đơn mua hàng (Purchase Order — PO) | `M02` | Mua hàng, Quản lý | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `purchase_order.read`, `purchase_order.create`, `purchase_order.update` | `GET/POST/PUT /api/v1/purchase-orders` | `P1` |
| `FN-INB-006` | Phát hành, đóng hoặc hủy Đơn mua hàng (PO Lifecycle) | `M02` | Mua hàng, Quản lý | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `purchase_order.release`, `purchase_order.close`, `purchase_order.cancel` | `POST /api/v1/purchase-orders/{id}/release`, `/close`, `/cancel` | `P1` |
| `FN-INB-007` | Tạo & quản lý Thông báo giao hàng dự kiến (ASN) | `M02` | Mua hàng, Thủ kho | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `asn.read`, `asn.create`, `asn.update` | `GET/POST/PUT /api/v1/asns` | `P1` |
| `FN-INB-008` | Xác nhận ASN & chuyển sang tiếp nhận thực tế tại cửa kho | `M02` | Thủ kho, Nhân viên nhận | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `asn.confirm`, `asn.receive`, `asn.cancel` | `POST /api/v1/asns/{id}/confirm`, `/receive`, `/cancel` | `P1` |
| `FN-INB-009` | Tạo & cập nhật Phiếu nhập kho nháp (Import Receipt Draft) | `M02` | Thủ kho, Nhân viên nhận | `BOTH` | `NEEDS_REFACTOR` | `receipt.read`, `receipt.create`, `receipt.update` | `GET/POST/PUT /api/v1/import-receipts` (`/api/ImportReceipts`) | `P0` |
| `FN-INB-010` | Quét mã vạch tiếp nhận hàng hóa theo dòng (Line Receiving & UOM Conversion) | `M02` | Nhân viên nhận (Web/PDA) | `BOTH` | `NEEDS_REFACTOR` | `receipt.receive` | `POST /api/v1/import-receipts/{id}/receive-line/{lineId}` | `P0` |
| `FN-INB-011` | Khai báo định danh Lô / Sê-ri / Hạn sử dụng khi nhận hàng (Lot/Serial/FEFO) | `M02` | Nhân viên nhận (Web/PDA) | `BOTH` | `VERIFIED_COMPLETE` | `receipt.receive` | `POST /api/v1/import-receipts/{id}/receive-line/{lineId}` | `P0` |
| `FN-INB-012` | Kiểm tra chất lượng đầu vào (Inbound QC Inspection & Disposition) | `M02` | Nhân viên QC, Quản lý | `EXPRESS_STREAMLINED` | `NEEDS_REFACTOR` | `quality_inspection.execute`, `quality_inspection.complete`, `quality_disposition.approve` | `POST /api/v1/import-receipts/{id}/qc-inspect` | `P1` |
| `FN-INB-013` | Ghi nhận & xử lý sai lệch nhận hàng (Thừa / Thiếu / Hư hỏng / Từ chối tại cửa) | `M02` | Thủ kho, Quản lý | `EXPRESS_STREAMLINED` | `NEEDS_REFACTOR` | `receiving_discrepancy.read`, `receiving_discrepancy.resolve`, `receiving_discrepancy.approve` | `GET /api/v1/import-receipts/{id}/discrepancies`, `POST .../resolve` | `P1` |
| `FN-INB-014` | Phê duyệt Phiếu nhập kho (Approve Import Receipt) | `M02` | Quản lý kho | `BOTH` | `NEEDS_REFACTOR` | `receipt.approve` / `approval.manage` | `POST /api/v1/import-receipts/{id}/approve` | `P0` |
| `FN-INB-015` | Ghi sổ Phiếu nhập kho vào Sổ cái & Tồn kho 9 chiều (Post Receipt — Atomic) | `M02` | Thủ kho, Quản lý | `BOTH` | `NEEDS_REFACTOR` | `receipt.post` | `POST /api/v1/import-receipts/{id}/post` (Header: `Idempotency-Key`) | `P0` |
| `FN-INB-016` | Hủy Phiếu nhập kho (Cancel Receipt & Putback Guard) | `M02` | Quản lý kho | `BOTH` | `NEEDS_REFACTOR` | `receipt.cancel` | `POST /api/v1/import-receipts/{id}/cancel` | `P0` |
| `FN-INB-017` | In Phiếu nhập kho chuẩn kế toán (4 chữ ký + mã vạch Code128) | `M02` | Thủ kho, Kế toán | `BOTH` | `VERIFIED_COMPLETE` | `receipt.read` | `GET /api/v1/import-receipts/{id}` + `ReceiptPrintPreview` | `P1` |
| `FN-INB-018` | Đề xuất vị trí cất hàng thông minh 5 yếu tố (Directed Putaway Bin Suggestion) | `M02` | Nhân viên kho (Web/PDA) | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `putaway.read` | `GET /api/v1/putaway-tasks/{id}/suggestions` | `P1` |
| `FN-INB-019` | Phân công & thực thi nhiệm vụ Cất hàng bằng máy quét PDA (Execute Putaway) | `M02` | Nhân viên kho (PDA) | `EXPRESS_STREAMLINED` | `VERIFIED_COMPLETE` | `putaway.assign`, `putaway.execute` | `POST /api/v1/putaway-tasks/{id}/assign`, `/execute`, `/complete` | `P0` |
| `FN-INB-020` | Phát hiện & điều hướng Cross-Docking trực tiếp sang đơn xuất đang thiếu hàng | `M02` | Thủ kho, Nhân viên kho | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `putaway.execute` | `POST /api/v1/putaway-tasks/{id}/cross-dock` | `P2` |
| `FN-INB-021` | Thực thi nhiệm vụ Cất trả hàng về kệ (Putback) khi đơn xuất bị hủy giữa chừng | `M02` | Nhân viên kho (PDA) | `BOTH` | `VERIFIED_COMPLETE` | `putaway.execute` | `POST /api/v1/putaway-tasks/{id}/putback` | `P1` |
| `FN-INB-022` | Phân tích tái quy hoạch vị trí kệ theo tốc độ luân chuyển ABC-XYZ (Reslotting) | `M02` | Quản lý kho | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `putaway.read`, `location.manage` | `GET /api/v1/putaway-tasks/abc-xyz-slotting` | `P2` |

---

## 4. Module `M03`: Xuất Kho & Hoàn Tất Đơn Hàng (`Outbound & Omnichannel Fulfillment`)

| Mã CN (`Function ID`) | Tên chức năng (Vietnamese Name) | Module | Vai trò (`Actor`) | Chế độ (`Express / Full`) | Trạng thái (`Status`) | Mã quyền (`Permission Code`) | API Endpoint Chuẩn (`TO-BE` / `AS-IS`) | Ưu tiên |
|---|---|---|---|---|---|---|---|---|
| `FN-OUT-001` | Quản lý Đơn bán hàng (Sales Orders — SO) & phát hành xuống kho | `M03` | CSKH, Điều phối xuất | `BOTH` | `VERIFIED_COMPLETE` | `sales_order.read`, `sales_order.create`, `sales_order.release` | `GET/POST /api/v1/sales-orders`, `POST .../release` | `P0` |
| `FN-OUT-002` | Tạm giữ (Hold), Hủy (Cancel) hoặc Đóng Đơn bán hàng (kèm cờ `CancelPendingPutback`) | `M03` | Điều phối xuất, Quản lý | `BOTH` | `VERIFIED_COMPLETE` | `sales_order.hold`, `sales_order.cancel`, `sales_order.close` | `POST /api/v1/sales-orders/{id}/hold`, `/cancel`, `/close` | `P0` |
| `FN-OUT-003` | Quản lý Đơn thiếu hàng (Backorders) & tự động phục hồi khi có hàng nhập mới | `M03` | Điều phối xuất | `BOTH` | `VERIFIED_COMPLETE` | `backorder.read`, `backorder.manage` | `GET/POST /api/v1/backorders`, `POST /api/v1/backorders/{id}/recover` | `P1` |
| `FN-OUT-004` | Tạo & cập nhật Phiếu xuất kho (Export Receipt CRUD) | `M03` | Thủ kho, Điều phối xuất | `BOTH` | `NEEDS_REFACTOR` | `export_receipt.read`, `export_receipt.create`, `export_receipt.update` | `GET/POST/PUT /api/v1/export-receipts` (`/api/ExportReceipts`) | `P0` |
| `FN-OUT-005` | Phê duyệt Phiếu xuất kho & tự động giữ tồn kho (Approve & Reserve) | `M03` | Quản lý kho, Thủ kho | `BOTH` | `NEEDS_REFACTOR` | `export_receipt.approve` | `POST /api/v1/export-receipts/{id}/approve` | `P0` |
| `FN-OUT-006` | Xác nhận xuất kho & trừ tồn kho nguyên tử (Dispatch Export Receipt) | `M03` | Thủ kho | `BOTH` | `NEEDS_REFACTOR` | `export_receipt.dispatch` | `POST /api/v1/export-receipts/{id}/dispatch` (Header: `Idempotency-Key`) | `P0` |
| `FN-OUT-007` | Hủy Phiếu xuất kho & giải phóng lượng giữ hàng (Cancel Export Receipt) | `M03` | Quản lý kho | `BOTH` | `NEEDS_REFACTOR` | `export_receipt.cancel` | `POST /api/v1/export-receipts/{id}/cancel` | `P0` |
| `FN-OUT-008` | Tra cứu & giải phóng bản ghi Giữ hàng tồn kho (Stock Reservations) | `M03` | Điều phối xuất, Quản lý | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `inventory.read` (`stock_reservation.read`) | `GET /api/v1/stock-reservations`, `POST .../{id}/release` | `P1` |
| `FN-OUT-009` | Phân bổ tồn kho vào vị trí kệ cụ thể theo FEFO/FIFO (Stock Allocations) | `M03` | Điều phối xuất | `EXPRESS_STREAMLINED` | `VERIFIED_COMPLETE` | `allocation.read`, `allocation.create` | `GET/POST /api/v1/stock-allocations` | `P0` |
| `FN-OUT-010` | Giải phóng hoặc phân bổ lại vị trí lấy hàng có xếp hạng ứng viên (Reallocate) | `M03` | Điều phối xuất, Quản lý | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `allocation.release`, `allocation.reallocate` | `POST /api/v1/stock-allocations/{id}/release`, `/reallocate` | `P1` |
| `FN-OUT-011` | Khởi tạo & điều phối Đợt lấy hàng theo cụm đơn (Wave / Batch Picking) | `M03` | Giám sát xuất kho | `FULL_DEFAULT` | `PARTIAL` | `picking.read`, `picking.assign` | `GET/POST /api/v1/picking-tasks/waves` | `P1` |
| `FN-OUT-012` | Phân công & bắt đầu Nhiệm vụ lấy hàng (Assign & Start Picking Task) | `M03` | Giám sát, Nhân viên lấy hàng | `EXPRESS_STREAMLINED` | `VERIFIED_COMPLETE` | `picking.assign`, `picking.execute` | `POST /api/v1/picking-tasks/{id}/assign`, `/start` | `P0` |
| `FN-OUT-013` | Quét mã vạch Vị trí + Sản phẩm + Lô để xác nhận lấy hàng trên PDA (Scan-to-Pick) | `M03` | Nhân viên lấy hàng (PDA) | `BOTH` | `VERIFIED_COMPLETE` | `picking.execute` | `POST /api/v1/picking-tasks/{id}/lines/{lineId}/pick` | `P0` |
| `FN-OUT-014` | Báo cáo lấy thiếu hàng tại kệ (Short-Pick) & xử lý ngoại lệ (Phân bổ lại / Bỏ qua) | `M03` | Nhân viên lấy hàng, Giám sát | `BOTH` | `VERIFIED_COMPLETE` | `picking.short_pick`, `picking.override` | `POST /api/v1/picking-tasks/{id}/lines/{lineId}/short-pick`, `.../resolve` | `P0` |
| `FN-OUT-015` | Chia hàng theo ô Put-to-Wall cho cụm đơn thương mại điện tử | `M03` | Nhân viên đóng gói | `FULL_DEFAULT` | `PARTIAL` | `packing.execute` | `POST /api/v1/packing-sessions/{id}/put-to-wall` | `P2` |
| `FN-OUT-016` | Trạm đóng gói đơn hàng (Packing Workbench) & gợi ý kích thước thùng carton 3D | `M03` | Nhân viên đóng gói | `BOTH` | `VERIFIED_COMPLETE` | `packing.read`, `packing.execute` | `GET/POST /api/v1/packing-sessions` | `P0` |
| `FN-OUT-017` | Tạo, đóng gói, lồng ghép (Nesting), tách/gộp và niêm phong Đơn vị xử lý (HU / SSCC) | `M03` | Nhân viên đóng gói | `EXPRESS_STREAMLINED` | `VERIFIED_COMPLETE` | `handling_unit.read`, `handling_unit.create`, `handling_unit.modify`, `handling_unit.split`, `handling_unit.merge`, `handling_unit.repack` | `GET/POST /api/v1/handling-units`, `POST .../{id}/seal` | `P1` |
| `FN-OUT-018` | Ghi hình camera bằng chứng đóng gói chống khiếu nại sàn TMĐT (Packing Video Evidence) | `M03` | Nhân viên đóng gói | `BOTH` | `VERIFIED_COMPLETE` | `packing.execute` | `POST /api/v1/packing-sessions/{id}/video-evidence` | `P1` |
| `FN-OUT-019` | In tem vận đơn nhiệt AWB chuẩn `100x150mm` (`%PDF-1.4`) & kiểm soát quyền in lại lần 2 | `M03` | Nhân viên đóng gói, Quản lý | `BOTH` | `VERIFIED_COMPLETE` | `packing.execute`, `export_receipt.reprint_awb` | `POST /api/v1/export-receipts/{id}/print-awb` | `P0` |
| `FN-OUT-020` | Quản lý Lô giao hàng (Shipment) & đưa hàng ra khu tập kết (Staging) | `M03` | Điều phối giao vận | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `shipment.read`, `shipment.stage` | `GET/POST /api/v1/shipments`, `POST .../{id}/stage` | `P1` |
| `FN-OUT-021` | Quét mã HU xếp hàng lên xe tải (Loading) & xác nhận xuất bến (Dispatch Shipment) | `M03` | Nhân viên giao nhận | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `shipment.load`, `loading.execute`, `shipment.dispatch` | `POST /api/v1/shipments/{id}/load-hu`, `/dispatch` | `P1` |
| `FN-OUT-022` | Theo dõi hành trình giao hàng & xác nhận bằng chứng giao thành công (POD / GPS) | `M03` | Giao vận, CSKH | `BOTH` | `VERIFIED_COMPLETE` | `shipment.confirm_delivery` | `POST /api/v1/shipments/{id}/confirm-delivery` | `P1` |
| `FN-OUT-023` | Ghi nhận giao hàng thất bại & kích hoạt luồng hoàn kho (`ReturnToWarehouse`) | `M03` | Giao vận, Thủ kho | `BOTH` | `VERIFIED_COMPLETE` | `shipment.update` | `POST /api/v1/shipments/{id}/delivery-failed`, `/return-to-warehouse` | `P1` |
| `FN-OUT-024` | Tạo Biên bản bàn giao bưu tá (Courier Handover Manifest) theo hãng vận chuyển | `M03` | Nhân viên bàn giao | `BOTH` | `VERIFIED_COMPLETE` | `inventory.read` (`shipment.dispatch`) | `GET/POST /api/v1/handover-manifests` | `P0` |
| `FN-OUT-025` | Quét mã vận đơn AWB lên biên bản bàn giao & tự động chặn đơn báo hủy phút chót | `M03` | Nhân viên bàn giao (PDA/Web) | `BOTH` | `VERIFIED_COMPLETE` | `inventory.read` (`shipment.dispatch`) | `POST /api/v1/handover-manifests/{id}/scan-awb` | `P0` |
| `FN-OUT-026` | Niêm phong & chốt Biên bản bàn giao bưu tá có chữ ký xác nhận (Seal Manifest) | `M03` | Nhân viên bàn giao, Thủ kho | `BOTH` | `VERIFIED_COMPLETE` | `inventory.read` (`shipment.dispatch`) | `POST /api/v1/handover-manifests/{id}/seal` | `P0` |

---

## 5. Module `M04`: Tồn Kho, Sổ Cái & Kiểm Soát Chất Lượng (`Inventory & Quality Control`)

| Mã CN (`Function ID`) | Tên chức năng (Vietnamese Name) | Module | Vai trò (`Actor`) | Chế độ (`Express / Full`) | Trạng thái (`Status`) | Mã quyền (`Permission Code`) | API Endpoint Chuẩn (`TO-BE` / `AS-IS`) | Ưu tiên |
|---|---|---|---|---|---|---|---|---|
| `FN-INV-001` | Tra cứu Tồn kho tổng hợp theo Sản phẩm & Kho (`OnHand`, `Reserved`, `Available`) | `M04` | Tất cả vai trò kho | `BOTH` | `NEEDS_REFACTOR` | `inventory.read`, `inventory_availability.read` | `GET /api/v1/inventory/stocks` (`/api/Inventory/stocks`) | `P0` |
| `FN-INV-002` | Tra cứu Nhóm tồn kho chi tiết 9 chiều (`InventoryBuckets`: Vị trí, Lô, Sê-ri, HU, Trạng thái, Chủ hàng) | `M04` | Thủ kho, Quản lý, QC | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `inventory.read` | `GET /api/v1/inventory/buckets` | `P0` |
| `FN-INV-003` | Tra cứu Lịch sử biến động Sổ cái bất biến (`InventoryTransactions`) | `M04` | Thủ kho, Kế toán, Quản lý | `BOTH` | `NEEDS_REFACTOR` | `inventory.read`, `inventory_ledger.read` | `GET /api/v1/inventory/transactions` (`/api/Inventory/transactions`) | `P0` |
| `FN-INV-004` | Báo cáo Xuất - Nhập - Tồn theo kỳ (Tồn đầu, Nhập, Xuất, Tồn cuối) | `M04` | Kế toán, Quản lý | `BOTH` | `NEEDS_REFACTOR` | `inventory.read` | `GET /api/v1/inventory/in-out` (`/api/Inventory/in-out`) | `P0` |
| `FN-INV-005` | Cảnh báo Tuổi tồn kho & Hạn sử dụng FEFO (`EXPIRED`, `NEAR_EXPIRY`, `GOOD`) | `M04` | Thủ kho, Quản lý, QC | `BOTH` | `VERIFIED_COMPLETE` | `inventory.read` | `GET /api/v1/inventory/expiry-aging` | `P1` |
| `FN-INV-006` | Phân tích hiệu suất vận hành tồn kho (SKU Velocity, Vòng quay, Top Movers) | `M04` | Quản lý, Chủ hàng | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `inventory.read` | `GET /api/v1/inventory/analytics` | `P2` |
| `FN-INV-007` | Chuyển đổi trạng thái chất lượng tồn kho (`Available` ↔ `QcHold` / `Quarantine` / `Damaged`) | `M04` | Nhân viên QC, Quản lý | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `inventory_status_change.create` | `POST /api/v1/inventory/status-change` | `P1` |
| `FN-INV-008` | Khóa / đóng băng một phần hoặc toàn bộ nhóm tồn kho (`InventoryLocks`) | `M04` | Quản lý kho, QC | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `inventory_lock.read`, `inventory_lock.manage` | `GET/POST /api/v1/inventory-locks`, `POST .../{id}/release` | `P1` |
| `FN-INV-009` | Di chuyển vị trí tồn kho nội bộ bằng máy quét mã vạch (`InventoryMovements`) | `M04` | Nhân viên kho (PDA/Web) | `BOTH` | `VERIFIED_COMPLETE` | `inventory_movement.create` | `GET/POST /api/v1/inventory-movements` | `P0` |
| `FN-INV-010` | Đảo bút toán giao dịch tồn kho (`InventoryReversals` — Append-Only Compensating Entry) | `M04` | Quản lý kho, Kế toán trưởng | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `inventory_ledger.read`, `inventory_reversal.create` | `GET/POST /api/v1/inventory-reversals` | `P1` |
| `FN-INV-011` | Truy vết phả hệ Lô/Sê-ri & mô phỏng Cây thu hồi sản phẩm lỗi (`Recall Tree`) | `M04` | Quản lý, QC, Kiểm toán | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `inventory_traceability.read` | `GET /api/v1/inventory-traceability` | `P1` |
| `FN-INV-012` | Đối chiếu tự động Số dư Tồn kho (`InventoryStocks`) với Sổ cái bất biến (`InventoryTransactions`) | `M04` | Kế toán, Quản lý, Kiểm toán | `FULL_DEFAULT` | `NEEDS_REFACTOR` | `inventory_ledger.read` | `GET /api/v1/inventory-reconciliation` | `P1` |
| `FN-INV-013` | Điều tra chuyên sâu nguyên nhân lệch tồn & mô phỏng What-If trên màn hình Đối chiếu | `M04` | Quản lý, Kiểm toán | `FULL_DEFAULT` | `NEEDS_REFACTOR` | `inventory_ledger.read` | `GET /api/v1/inventory-reconciliation/{productId}/investigate` | `P2` |
| `FN-INV-014` | Tạo Phiếu kiểm kê kho định kỳ / đột xuất (Hỗ trợ chế độ Kiểm kê mù — Blind Count) | `M04` | Thủ kho, Quản lý | `BOTH` | `NEEDS_REFACTOR` | `stocktake.read`, `stocktake.create` | `GET/POST /api/v1/stocktakes` (`/api/Stocktakes`) | `P0` |
| `FN-INV-015` | Nhập kết quả đếm thực tế lần 1 & lần 2 theo từng vị trí kệ trên PDA/Web | `M04` | Nhân viên kiểm kê (PDA/Web) | `BOTH` | `NEEDS_REFACTOR` | `stocktake.execute` | `POST /api/v1/stocktakes/{id}/count` | `P0` |
| `FN-INV-016` | Kiểm tra ngưỡng tự động duyệt & Phê duyệt/Từ chối điều chỉnh chênh lệch kiểm kê | `M04` | Quản lý kho, Kế toán | `BOTH` | `NEEDS_REFACTOR` | `stocktake.approve` | `GET /api/v1/stocktakes/{id}/auto-approval-preview`, `POST .../approve` | `P0` |
| `FN-INV-017` | Tạo, sửa & duyệt Phiếu điều chuyển liên kho (`StockTransfers`) | `M04` | Thủ kho, Quản lý | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `inventory.read` (`stock_transfer.create`, `stock_transfer.approve`) | `GET/POST/PUT /api/v1/stock-transfers`, `POST .../{id}/approve` | `P1` |
| `FN-INV-018` | Xuất kho điều chuyển (`InTransit`), Nhận hàng tại kho đích & xử lý hoàn trả điều chuyển lỗi | `M04` | Thủ kho hai đầu kho | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `inventory.read` (`stock_transfer.execute`) | `POST /api/v1/stock-transfers/{id}/ship`, `/receive`, `/return` | `P1` |

---

## 6. Module `M05`: Hàng Hoàn, RMA & Logistics Ngược (`Returns & Reverse Logistics` — **[NEW UI WORKBENCH]**)

| Mã CN (`Function ID`) | Tên chức năng (Vietnamese Name) | Module | Vai trò (`Actor`) | Chế độ (`Express / Full`) | Trạng thái (`Status`) | Mã quyền (`Permission Code`) | API Endpoint Chuẩn (`TO-BE` / `AS-IS`) | Ưu tiên |
|---|---|---|---|---|---|---|---|---|
| `FN-RET-001` | Tra cứu danh sách Phiếu hàng hoàn khách trả (Customer Returns / RMA Queue) | `M05` | Nhân viên nhận hoàn, CSKH | `BOTH` | `BACKEND_ONLY_NEEDS_UI` | `customer_return.read` (hiện tại `receipt.read`) | `GET /api/v1/customer-returns` (hiện tại `GET /api/ImportReceipts/customer-returns`) | `P0` |
| `FN-RET-002` | Quét mã vận đơn hoàn / mã đơn hàng để tiếp nhận hàng hoàn vào khu Biệt trữ (`QUARANTINED`) | `M05` | Nhân viên nhận hoàn (PDA/Web) | `BOTH` | `BACKEND_ONLY_NEEDS_UI` | `customer_return.create` (`receipt.receive`) | `POST /api/v1/customer-returns/intake` (`POST /api/ImportReceipts/rma-intake-check`) | `P0` |
| `FN-RET-003` | Kiểm định chất lượng hàng hoàn & phân loại tình trạng (`GOOD`, `DAMAGED_BOX`, `DEFECTIVE`) | `M05` | Nhân viên QC / Nhận hoàn | `EXPRESS_STREAMLINED` | `BACKEND_ONLY_NEEDS_UI` | `customer_return.inspect` (`quality_inspection.execute`) | `POST /api/v1/customer-returns/{id}/inspect` | `P0` |
| `FN-RET-004` | Xử lý định đoạt: Nhập lại tồn khả dụng (`RESTOCK_AVAILABLE`) kèm sinh nhiệm vụ Putaway | `M05` | Thủ kho, QC | `BOTH` | `BACKEND_ONLY_NEEDS_UI` | `customer_return.dispose` (`receipt.post`) | `POST /api/v1/customer-returns/{id}/dispose` (`POST /api/ImportReceipts/execute-rtv-refurbish`) | `P0` |
| `FN-RET-005` | Xử lý định đoạt: Tân trang / đóng gói lại hộp (`REFURBISH_REPACK`) có trừ vật tư bao bì | `M05` | Nhân viên đóng gói / QC | `FULL_DEFAULT` | `BACKEND_ONLY_NEEDS_UI` | `customer_return.dispose` | `POST /api/v1/customer-returns/{id}/dispose` (`Disposition = REFURBISH_REPACK`) | `P1` |
| `FN-RET-006` | Xử lý định đoạt: Xuất trả nhà cung cấp (`RTV_VENDOR`) & ghi giảm công nợ AP | `M05` | Thủ kho, Mua hàng, Kế toán | `FULL_DEFAULT` | `BACKEND_ONLY_NEEDS_UI` | `customer_return.dispose` | `POST /api/v1/customer-returns/{id}/dispose` (`Disposition = RTV_VENDOR`) | `P1` |
| `FN-RET-007` | Xử lý định đoạt: Hủy bỏ hàng hỏng không thể phục hồi (`SCRAP_DISPOSAL`) có phê duyệt | `M05` | Quản lý kho, Kế toán | `FULL_DEFAULT` | `BACKEND_ONLY_NEEDS_UI` | `customer_return.dispose`, `approval.manage` | `POST /api/v1/customer-returns/{id}/dispose` (`Disposition = SCRAP_DISPOSAL`) | `P1` |
| `FN-RET-008` | Đồng bộ tự động kiện giao thất bại (`ReturnToWarehouse`) từ Lô giao hàng sang Hàng đợi RMA | `M05` | Giao vận, Nhân viên nhận hoàn | `BOTH` | `PARTIAL` | `shipment.update`, `customer_return.create` | `POST /api/v1/shipments/{id}/return-to-warehouse` | `P1` |

---

## 7. Module `M06`: Tài Chính, Giá Vốn & Cước 3PL (`Finance, Cost Valuation & 3PL Billing`)

| Mã CN (`Function ID`) | Tên chức năng (Vietnamese Name) | Module | Vai trò (`Actor`) | Chế độ (`Express / Full`) | Trạng thái (`Status`) | Mã quyền (`Permission Code`) | API Endpoint Chuẩn (`TO-BE` / `AS-IS`) | Ưu tiên |
|---|---|---|---|---|---|---|---|---|
| `FN-FIN-001` | Tính giá vốn bình quân gia quyền tức thời (Moving Average Cost — WAC) & định giá tồn kho | `M06` | Kế toán, Chủ hàng, Quản lý | `BOTH` | `VERIFIED_COMPLETE` | `inventory.cost.read` | `GET /api/v1/reports/owner-gross-profit` | `P0` |
| `FN-FIN-002` | Báo cáo Lãi gộp theo SKU / Đơn hàng / Kênh bán (Owner Gross Profit Analysis) | `M06` | Kế toán, Chủ hàng | `BOTH` | `VERIFIED_COMPLETE` | `inventory.cost.read` | `GET /api/v1/reports/owner-gross-profit` | `P1` |
| `FN-FIN-003` | Tải lên bảng kê đối soát ĐVVC (`.xlsx`/`.csv` thật qua `FormData`) & Đối soát COD 3 chiều | `M06` | Kế toán, Đối soát viên | `BOTH` | `VERIFIED_COMPLETE` | `inventory.cost.read` | `GET/POST /api/v1/reports/cod-reconciliation` | `P0` |
| `FN-FIN-004` | Cảnh báo lệch tiền thu hộ COD hoặc đội phí vận chuyển thực tế so với dự tính | `M06` | Kế toán | `BOTH` | `VERIFIED_COMPLETE` | `inventory.cost.read` | `GET /api/v1/reports/cod-reconciliation` | `P1` |
| `FN-FIN-005` | Kết xuất tệp Excel chuẩn MISA AMIS (`.xlsx` ClosedXML) & dự thảo Hóa đơn điện tử NĐ 70/2025 | `M06` | Kế toán | `BOTH` | `VERIFIED_COMPLETE` | `inventory.cost.read` | `GET /api/v1/reports/misa-export`, `POST /api/v1/reports/e-invoice-draft` | `P1` |
| `FN-FIN-006` | Quản lý Công nợ Phải trả NCC (AP) & Phải thu Khách hàng (AR) kèm hạn mức tín dụng | `M06` | Kế toán, Quản lý | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `partner.read`, `inventory.cost.read` | `GET/POST /api/v1/partner-debts` | `P1` |
| `FN-FIN-007` | Ghi nhận thanh toán công nợ & tra cứu Sổ cái công nợ bất biến (`PartnerDebtLedger`) | `M06` | Kế toán | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `partner.update`, `inventory.cost.read` | `POST /api/v1/partner-debts/{id}/payments`, `GET .../ledger` | `P1` |
| `FN-FIN-008` | Chụp ảnh số dư lưu kho hàng ngày theo Chủ hàng (`ThreePlStorageSnapshot`) | `M06` | Quản lý 3PL, Kế toán | `FULL_DEFAULT` | `BACKEND_ONLY_NEEDS_UI` | `three_pl_billing.read` (`partner.read`) | `GET/POST /api/v1/reports/3pl-storage-snapshot` | `P2` |
| `FN-FIN-009` | Tính cước dịch vụ kho 3PL (Phí lưu kho + Phí nhập + Phí xuất) & phát hành Bảng kê (`ThreePlBillingStatement`) | `M06` | Kế toán 3PL, Quản lý | `FULL_DEFAULT` | `BACKEND_ONLY_NEEDS_UI` | `three_pl_billing.manage` (`partner.update`) | `POST /api/v1/business-partners/3pl-billing/generate-statement` | `P2` |
| `FN-FIN-010` | Cổng thông tin đối tác gửi hàng 3PL (3PL Depositor Portal Summary) | `M06` | Chủ hàng 3PL, Quản lý | `FULL_DEFAULT` | `BACKEND_ONLY_NEEDS_UI` | `three_pl_billing.read` | `GET /api/v1/business-partners/3pl-depositor-portal` | `P2` |

---

## 8. Module `M07`: Dữ Liệu Nền, Danh Mục & Cấu Trúc Kho (`Master Data, Catalog & Warehouse Topology`)

| Mã CN (`Function ID`) | Tên chức năng (Vietnamese Name) | Module | Vai trò (`Actor`) | Chế độ (`Express / Full`) | Trạng thái (`Status`) | Mã quyền (`Permission Code`) | API Endpoint Chuẩn (`TO-BE` / `AS-IS`) | Ưu tiên |
|---|---|---|---|---|---|---|---|---|
| `FN-CAT-001` | Quản lý Sản phẩm (SKU Master Data: Loại theo dõi None/Lot/Serial, Hạn dùng, Nhiệt độ, Kích thước) | `M07` | Quản lý danh mục, Thủ kho | `BOTH` | `NEEDS_REFACTOR` | `product.read`, `product.create`, `product.update`, `product.deactivate` | `GET/POST/PUT /api/v1/products` | `P0` |
| `FN-CAT-002` | Quản lý Danh mục nhóm sản phẩm (`ProductCategories`) | `M07` | Quản lý danh mục | `BOTH` | `VERIFIED_COMPLETE` | `product_category.read`, `product_category.manage` | `GET/POST/PUT /api/v1/product-categories` | `P1` |
| `FN-CAT-003` | Quản lý Đơn vị tính (`Units`) & Hệ số quy đổi đa đơn vị (`ProductUom`: Thùng ↔ Hộp ↔ Cái) | `M07` | Quản lý danh mục | `BOTH` | `VERIFIED_COMPLETE` | `uom.read`, `uom.manage`, `product_uom.manage` | `GET/POST/PUT /api/v1/units`, `/api/v1/products/{id}/uoms` | `P0` |
| `FN-CAT-004` | Quản lý đa Mã vạch sản phẩm (`ProductBarcodes`) & tra cứu nhanh bằng súng quét | `M07` | Tất cả vai trò kho | `BOTH` | `VERIFIED_COMPLETE` | `product_barcode.manage`, `product.read` | `GET/POST /api/v1/product-barcodes`, `GET /api/v1/product-barcode-lookup/{code}` | `P0` |
| `FN-CAT-005` | Ánh xạ SKU nội bộ với SKU các sàn TMĐT (Shopee, TikTok Shop, Lazada — `MarketplaceShop`) | `M07` | Vận hành sàn, Quản lý | `BOTH` | `VERIFIED_COMPLETE` | `product.update` | `GET/POST /api/v1/products/channel-mappings` | `P1` |
| `FN-CAT-006` | Quản lý Định mức Combo/Bundle & Lệnh sản xuất đóng gói/tháo dỡ bộ (`KitWorkOrder`) | `M07` | Quản lý kho, Đóng gói VAS | `FULL_DEFAULT` | `PARTIAL` | `product.update`, `packing.execute` | `GET/POST /api/v1/products/kit-work-orders`, `POST .../{id}/complete` | `P1` |
| `FN-CAT-007` | Quản lý Đối tác kinh doanh (Nhà cung cấp, Khách hàng, Chủ hàng 3PL — `BusinessPartners`) | `M07` | Mua hàng, Kế toán, Quản lý | `BOTH` | `VERIFIED_COMPLETE` | `partner.read`, `partner.create`, `partner.update`, `partner.deactivate` | `GET/POST/PUT /api/v1/business-partners` | `P0` |
| `FN-CAT-008` | Quản lý Danh mục Kho hàng (`Warehouses`) & cấu hình múi giờ/trạng thái | `M07` | Admin, Quản lý | `BOTH` | `VERIFIED_COMPLETE` | `warehouse.read`, `warehouse.manage` | `GET/POST/PUT /api/v1/warehouses` | `P0` |
| `FN-CAT-009` | Quản lý Cấu trúc vị trí kho 5 cấp (`Zone → Aisle → Rack → Level → Bin`) & tải trọng tối đa | `M07` | Quản lý kho, Admin | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `location.read`, `location.manage` | `GET/POST/PUT /api/v1/warehouses/{id}/locations` | `P0` |
| `FN-CAT-010` | In tem mã vạch vị trí kệ hàng loạt (Location Barcode Labels) | `M07` | Quản lý kho, Thủ kho | `BOTH` | `VERIFIED_COMPLETE` | `location.read` | `GET /api/v1/warehouses/{id}/locations` (Print View) | `P1` |
| `FN-CAT-011` | Bản đồ kho trực quan 2D (`WarehouseMap`) & Bản đồ nhiệt công suất sử dụng kệ (Utilization Heatmap) | `M07` | Quản lý kho, Điều phối | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `location.read` | `GET /api/v1/warehouses/{id}/map-utilization` | `P1` |
| `FN-CAT-012` | Quản lý Lịch làm việc, Ca vận hành & Giờ chốt đơn (`WarehouseCalendar` & Cut-off SLA) | `M07` | Quản lý kho | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `warehouse.read`, `warehouse_calendar.manage` | `GET/POST/PUT /api/v1/warehouse-calendars` | `P2` |

---

## 9. Module `M08`: Quản Trị Quyền (IAM), Phê Duyệt & Hệ Thống SaaS (`Governance`)

| Mã CN (`Function ID`) | Tên chức năng (Vietnamese Name) | Module | Vai trò (`Actor`) | Chế độ (`Express / Full`) | Trạng thái (`Status`) | Mã quyền (`Permission Code`) | API Endpoint Chuẩn (`TO-BE` / `AS-IS`) | Ưu tiên |
|---|---|---|---|---|---|---|---|---|
| `FN-GOV-001` | Đăng nhập đa thuê bao (Multi-Tenant Login), Làm mới Token đơn luồng & Đăng xuất an toàn | `M08` | Tất cả người dùng | `BOTH` | `VERIFIED_COMPLETE` | `(Public / Authenticated)` | `POST /api/v1/auth/login`, `/refresh`, `/logout`, `GET /api/v1/auth/me` | `P0` |
| `FN-GOV-002` | Quản lý Người dùng, Khóa/Mở khóa tài khoản, Ép đổi mật khẩu & Thu hồi phiên (`UserSession`) | `M08` | Admin | `BOTH` | `NEEDS_REFACTOR` | `user.read`, `user.manage`, `permission.read` | `GET/POST /api/v1/permissions/users/{id}/toggle-lock`, `/terminate-sessions` | `P0` |
| `FN-GOV-003` | Quản lý Vai trò (`Roles`) & Ma trận phân quyền chi tiết theo từng nghiệp vụ (`RolePermission`) | `M08` | Admin | `BOTH` | `NEEDS_REFACTOR` | `role.read`, `role.manage`, `permission.assign` | `GET/POST/PUT /api/v1/permissions/roles`, `GET .../catalog` | `P0` |
| `FN-GOV-004` | Phân quyền phạm vi kho truy cập cho từng người dùng (`UserWarehouse` Scope Guard) | `M08` | Admin | `BOTH` | `NEEDS_REFACTOR` | `user_warehouse.read`, `user_warehouse.manage` | `GET/POST/DELETE /api/v1/permissions/users/{id}/warehouse-access` | `P0` |
| `FN-GOV-005` | Tra cứu Nhật ký kiểm toán phân quyền & bảo mật (`AuditLogs` — Immutable) | `M08` | Admin, Kiểm toán | `FULL_DEFAULT` | `NEEDS_REFACTOR` | `permission.read` | `GET /api/v1/permissions/audit-logs` | `P1` |
| `FN-GOV-006` | Trung tâm Phê duyệt tập trung theo SLA (Duyệt Phiếu nhập, Phiếu xuất, Kiểm kê, Sai lệch) | `M08` | Quản lý, Giám đốc kho | `FULL_DEFAULT` | `VERIFIED_COMPLETE` | `receipt.approve`, `export_receipt.approve`, `approval.reject` | `GET/POST /api/v1/approvals` | `P1` |
| `FN-GOV-007` | Trình thuật sĩ Thiết lập kho nhanh 5 bước (`OnboardingWizard`) & Tạo cấu trúc kệ mẫu 1-Click | `M08` | Chủ kho mới, Admin | `BOTH` | `VERIFIED_COMPLETE` | `permission.read` (hoặc Public `/register`) | `POST /api/v1/data-imports/onboarding-setup` | `P1` |
| `FN-GOV-008` | Nhập tồn kho đầu kỳ & danh mục hàng loạt bằng tệp Excel thật (`ClosedXML` + `IFormFile`) | `M08` | Admin, Kế toán, Thủ kho | `BOTH` | `VERIFIED_COMPLETE` | `inventory_movement.create`, `product.create` | `GET /api/v1/import-templates/{type}`, `POST /api/v1/data-imports/opening-stock` | `P0` |
| `FN-GOV-009` | Quản lý Gói cước thuê bao SaaS, Giám sát hạn mức sử dụng & Thanh toán QR tự động (`VietQR`) | `M08` | Chủ thuê bao, Admin | `BOTH` | `NEEDS_REFACTOR` | `permission.read` | `GET/POST /api/v1/saas-billing` | `P2` |
| `FN-GOV-010` | Công cụ Sao chép mã chẩn đoán kỹ thuật 1-Click gửi hỗ trợ Zalo (`CorrelationId` + `LastErrorCode`) | `M08` | Tất cả vai trò | `BOTH` | `VERIFIED_COMPLETE` | `(Authenticated)` | Topbar Help Popover (`MainLayout.tsx`) | `P1` |
