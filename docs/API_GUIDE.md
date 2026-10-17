# Tài liệu API KHO - ERP KHO

Tài liệu này đóng vai trò là hướng dẫn sử dụng và danh sách kiểm tra (Checklist) cho các API hiện có trong hệ thống ERP KHO.

## 1. Xác thực, Phân quyền & Idempotency (Authentication / Authorization / Idempotency)
- **Cơ chế xác thực**: Dùng JWT Bearer Token (`Authorization: Bearer <token>`).
- **Endpoints xác thực**:
  - `POST /api/Auth/login`: Đăng nhập, nhận JWT Token gắn với phiên `UserSessions`.
- **Roles & Policies**:
  - `Viewer`: Quyền xem báo cáo, tra cứu dữ liệu trong phạm vi kho được cấp (`UserWarehouses`).
  - `WarehouseStaff`: Quyền tạo chứng từ nháp (`Maker`) và thực thi tác nghiệp kho theo permission code.
  - `Manager`: Quản lý kho, có quyền phê duyệt chứng từ (`ApprovalPolicies.Checker`).
  - `Admin`: Quản trị hệ thống.
- **Nguyên tắc Maker–Checker (`ApprovalPolicies.Checker`)**:
  - Các endpoint phê duyệt (`approve`) yêu cầu chính sách `Checker` (`Admin` hoặc `Manager` có permission duyệt tương ứng) **và** bắt buộc tách biệt người tạo – người duyệt (`ApprovedBy != CreatedBy`).
- **Header `Idempotency-Key`**:
  - Các command thay đổi trạng thái/tồn kho (`POST`/`PUT` tạo phiếu, duyệt phiếu, xuất kho, điều chuyển, giữ hàng, đảo bút toán) yêu cầu header `Idempotency-Key` để chống gửi trùng; gửi lại cùng key với payload khác sẽ trả về `409 Conflict`.

## 2. Master Data & Warehouse Topology APIs (Dữ liệu nền & Cấu trúc kho)
- **Products, Categories & Barcodes**: `/api/Products`, `/api/product-categories`, `/api/products/{id}/barcodes`, `/api/product-barcodes/lookup`.
- **Warehouses, Structure, Locations & Calendar**: `/api/Warehouses`, `/api/WarehouseLocations`, `/api/warehouse-calendars/{warehouseId}` (lịch tuần, ca làm việc, ngày ngoại lệ).
- **Units & Business Partners**: `/api/Units`, `/api/BusinessPartners`.
- **Dock & Yard Management**: `/api/dock-yard/doors`, `/api/dock-yard/appointments`, `/api/dock-yard/yard-checkins` (lịch xe, check-in cổng, phân cửa kho, check-out rời bãi).

## 3. Inbound Planning, Import Receipt & Putaway APIs (Mua hàng, ASN, Nhập kho & Cất hàng)
Quy trình: Đơn mua hàng (`PurchaseOrders`) -> Thông báo giao hàng dự kiến (`Asns`) -> Tạo phiếu nhập (`ImportReceipts` ở `Draft`, không trùng lặp `ProductId`) -> Ghi nhận kiểm đếm (`Receive`) / xử lý chênh lệch (`Discrepancies`) / QC (`qc-disposition`) -> Duyệt hoàn tất (`Approve`) -> Ghi sổ tồn kho (`Post`) -> Cất hàng (`PutawayTasks`).
- **Purchase Orders (`/api/purchase-orders`)**: `GET`, `POST`, `PUT /api/purchase-orders/{id}`, `POST /api/purchase-orders/{id}/release|close|cancel`.
- **ASNs (`/api/asns`)**: `GET`, `POST`, `PUT /api/asns/{id}`, `POST /api/asns/{id}/confirm|receive|cancel`.
- **Import Receipts (`/api/ImportReceipts`)**:
  - `GET /api/ImportReceipts`, `GET /api/ImportReceipts/{id}`, `POST /api/ImportReceipts`.
  - `POST /api/ImportReceipts/{id}/receive`, `POST /api/ImportReceipts/{id}/qc-disposition`, `POST /api/ImportReceipts/{id}/discrepancies/*`.
  - `POST /api/ImportReceipts/{id}/approve` (policy `Checker`, `ApprovedBy != CreatedBy`), `POST /api/ImportReceipts/{id}/post` (chỉ cộng `AcceptedQuantity`/`DamagedQuantity` chuẩn vào tồn kho đúng 1 lần; `DoorRejectedQuantity` không bao giờ cộng vào `OnHand`), `PUT /api/ImportReceipts/{id}/cancel`.
- **Putaway Tasks (`/api/putaway-tasks`)**: `GET /api/putaway-tasks`, `POST /api/putaway-tasks/{id}/assign|start|move|exception|resume|cancel`.

## 4. Outbound Demand, Export Receipt, Reservation, Allocation, Picking, Packing & Shipment APIs (Xuất kho toàn trình)
Quy trình: Đơn bán & Backorder (`SalesOrders` / `Backorders`) hoặc Phiếu xuất (`ExportReceipts`) -> Giữ hàng (`StockReservations`) -> Phân bổ vị trí (`StockAllocations`) -> Lấy hàng (`PickingTasks`) -> Đóng gói & Đơn vị xử lý (`PackingSessions` / `HandlingUnits`) -> Thực hiện giao hàng (`Shipments`).
- **Sales Orders & Backorders (`/api/sales-orders`, `/api/backorders`)**: Quản lý nhu cầu xuất, tạm giữ/giải phóng đơn bán, xử lý đơn thiếu hàng (`backorder.read` / `sales_order.read`).
- **Export Receipts (`/api/ExportReceipts`)**: `GET`, `POST /api/ExportReceipts`, `POST /api/ExportReceipts/{id}/approve|dispatch|cancel`.
- **Stock Reservations (`/api/stock-reservations` — Cô lập vòng đời theo `OWN-OOAD-004`)**:
  - `POST /api/stock-reservations`: Tạo giữ hàng thủ công (`SourceType = "Manual"`).
  - `POST /api/stock-reservations/{id}/release` & `POST /api/stock-reservations/expire`: Chỉ tác động lên `SourceType = "Manual"` (`SourceType = "ExportReceipt"` trả về `409 Conflict`).
- **Stock Allocations (`/api/stock-allocations`)**: `GET /api/stock-allocations`, `POST /api/stock-allocations` (gắn lượng giữ vào vị trí lấy hàng), `POST /api/stock-allocations/{id}/release|reallocate`.
- **Picking Tasks (`/api/picking-tasks`)**: `GET /api/picking-tasks`, `POST /api/picking-tasks/{id}/assign|start|pick|short-pick|override`.
- **Packing Sessions & Handling Units (`/api/packing-sessions`, `/api/handling-units`)**: Đóng gói lượng đã lấy vào thùng/pallet (`HU`), tách/gộp/đóng gói lại HU và mở lại phiên đóng gói có kiểm soát.
- **Shipments (`/api/shipments`)**: Điều phối giao hàng `READY -> STAGING -> LOADING -> LOADED -> DISPATCHED -> DELIVERED` (`stage`, `load`, `dispatch`, `confirm-delivery`).

## 5. Stocktakes, Stock Transfers & Inventory Reconciliation APIs (Kiểm kê, Điều chuyển & Đối soát)
- **Stocktakes (`/api/Stocktakes`)**:
  - `POST /api/Stocktakes/{id}/approve`: Phê duyệt phiếu kiểm kê (yêu cầu policy `Checker`, `ApprovedBy != CreatedBy`), kiểm tra `InventoryLock` (`409 INVENTORY_LOCKED`) và Stale Snapshot (`SC-P3-09`, `409 CONCURRENCY_CONFLICT`), cập nhật delta và sinh `InventoryTransaction` (`AdjustmentIncrease` / `AdjustmentDecrease`) bảo toàn `SUM(Ledger) == Balance`.
- **Stock Transfers (`/api/stock-transfers`)**:
  - Quản lý phiếu điều chuyển liên kho (`Draft -> Approved -> InTransit -> Received / Returned / Cancelled`), kiểm soát phiên bản nháp, xuất/nhận điều chuyển nguyên tử và hoàn trả kho nguồn (`return-reasons`).
- **Inventory Reconciliation (`/api/InventoryReconciliation`)**: Đối soát tồn kho giữa `InventoryStocks` và tổng đại số `InventoryTransactions` (`SUM(Ledger) == Balance`).

## 6. Inventory Control & Traceability APIs (Tồn kho, Khóa tồn, Di chuyển, Đảo bút toán & Truy vết)
- `GET /api/InventoryStocks/current`: Truy vấn tồn kho khả dụng hiện hành (`Status == Available`, vị trí khả dụng `IsActive && !IsBlocked && IsPickable`, lô chưa hết hạn, không bị khóa bởi `InventoryLock`).
- `GET /api/InventoryTransactions`: Truy vấn sổ cái giao dịch tồn kho bất biến (`Rule 2` append-only).
- `GET/POST /api/inventory-locks` & `POST /api/inventory-locks/{id}/release`: Tạo và giải phóng khóa tồn kho (`InventoryLock`).
- `GET/POST /api/inventory-movements` & `/api/inventory-control/*`: Di chuyển vị trí nội bộ (`Move`), đổi trạng thái tồn kho (`StatusChange`) và quản trị chính sách tồn kho.
- `GET/POST /api/inventory-reversals` & `GET /api/inventory-traceability/*`: Đảo giao dịch nội bộ bằng cặp bút toán `Reversal` + giao dịch hiệu chỉnh đối ứng (`CorrectiveTransactionId`), truy vết phả hệ Lô/Sê-ri/Chứng từ.

## 7. Approvals, Permissions, Account Security & Report APIs (Phê duyệt, Phân quyền, Bảo mật & Báo cáo)
- **Approvals (`/api/Approvals`)**: Hàng đợi phê duyệt tập trung theo SLA và phạm vi kho, hỗ trợ phê duyệt/từ chối kèm `Idempotency-Key`.
- **Permissions, Role Grants & Warehouse Scope (`/api/permissions`, `/api/users/{userId}/warehouse-access`)**: Tra cứu danh mục 86 mã quyền, cấp/thu hồi quyền theo vai trò với `rowVersion`, cấp/thu hồi phạm vi kho (`UserWarehouses`), tra cứu nhật ký kiểm toán (`/api/permissions/audit-logs`).
- **Account & Session Security (`/api/users/{userId}/security`, `/api/Auth/sessions`)**: Khóa/mở khóa tài khoản, đặt lại mật khẩu tạm, thu hồi toàn bộ phiên của người dùng (`user.manage`) và quản lý phiên cá nhân tự phục vụ (`GET/DELETE /api/Auth/sessions`).
- **Report APIs (`/api/Reports`)**:
  - `GET /api/Reports/inventory` & `GET /api/Reports/inventory/export` (`BaoCaoTonKho.xlsx`).
  - `GET /api/Reports/inventory-in-out-stock` & `GET /api/Reports/inventory-in-out-stock/export` (`BaoCaoXuatNhapTon.xlsx`).

## 8. Swagger/OpenAPI Export
- Để xem tài liệu OpenAPI trực tiếp khi chạy cục bộ (`dotnet run --project ERP.Api/ERP.Api.csproj`), truy cập `http://localhost:5000/swagger/v1/swagger.json`.

