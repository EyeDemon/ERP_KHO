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

## 2. Master Data APIs (Dữ liệu nền tảng)
- **Products** (`/api/Products`), **Warehouses** (`/api/Warehouses`), **WarehouseLocations** (`/api/WarehouseLocations`), **Units** (`/api/Units`), **BusinessPartners** (`/api/BusinessPartners`).

## 3. Import Receipt APIs (Nhập kho)
Quy trình: Tạo phiếu nhập (`Draft`, không cho phép trùng lặp `ProductId` trong cùng một phiếu) -> Ghi nhận kiểm đếm/QC -> Duyệt phiếu (`Approve`) -> Putaway.
- `GET /api/ImportReceipts`: Lấy danh sách phiếu nhập.
- `GET /api/ImportReceipts/{id}`: Lấy chi tiết phiếu.
- `POST /api/ImportReceipts`: Tạo phiếu nhập kho (`Draft`). Trả về `400 BadRequest` nếu trùng `ProductId` trong danh sách dòng chi tiết.
- `POST /api/ImportReceipts/{id}/approve`: Duyệt phiếu (yêu cầu policy `Checker`, `ApprovedBy != CreatedBy`), chỉ cộng `AcceptedQuantity` vào tồn kho `OnHand` (`DoorRejectedQuantity` không bao giờ cộng vào `OnHand`).
- `PUT /api/ImportReceipts/{id}/cancel`: Hủy phiếu.

## 4. Export Receipt & Stock Reservation APIs (Xuất kho & Giữ hàng)
Quy trình: Tạo phiếu xuất (`Draft`, không cho phép trùng lặp `ProductId`) -> Duyệt phiếu (`Approve`, tạo giữ hàng `SourceType = "ExportReceipt"`) -> Xuất kho thực tế (`Dispatch`, tiêu thụ giữ hàng và trừ `OnHand` một lần duy nhất) hoặc Hủy phiếu (`Cancel`, giải phóng giữ hàng).
- `GET /api/ExportReceipts` / `GET /api/ExportReceipts/{id}`: Tra cứu phiếu xuất.
- `POST /api/ExportReceipts`: Tạo phiếu xuất kho (`Draft`). Trả về `400 BadRequest` nếu trùng `ProductId`.
- `POST /api/ExportReceipts/{id}/approve`: Duyệt phiếu (yêu cầu policy `Checker`), giữ hàng khả dụng (`Available = EligibleOnHand - Reserved`).
- `POST /api/ExportReceipts/{id}/dispatch`: Xuất kho thực tế, tiêu thụ reservation của phiếu xuất và ghi 1 bút toán `InventoryTransaction` (`Export`).
- `POST /api/ExportReceipts/{id}/cancel`: Hủy phiếu xuất và giải phóng reservation gắn với phiếu.
- **Stock Reservations (`/api/stock-reservations` — Cô lập vòng đời theo `OWN-OOAD-004`)**:
  - `POST /api/stock-reservations`: Tạo giữ hàng thủ công (`SourceType = "Manual"`).
  - `POST /api/stock-reservations/{id}/release`: Giải phóng giữ hàng thủ công. **Chỉ áp dụng cho `SourceType = "Manual"`**; nếu gọi lên reservation có `SourceType = "ExportReceipt"` sẽ trả về `409 Conflict` (`CONCURRENCY_CONFLICT`).
  - `POST /api/stock-reservations/expire`: Quét giải phóng các giữ hàng hết hạn (`ExpiresAt <= UtcNow`), chỉ tác động lên `SourceType = "Manual"`.

## 5. Stocktakes & Inventory Reconciliation APIs (Kiểm kê & Đối soát)
- `POST /api/Stocktakes/{id}/approve`: Phê duyệt phiếu kiểm kê (yêu cầu policy `Checker`, `ApprovedBy != CreatedBy`).
  - Khóa dòng tồn kho mục tiêu và kiểm tra `InventoryLock`: nếu đang bị khóa trả về `409 Conflict` (`INVENTORY_LOCKED`).
  - Kiểm tra Stale Snapshot (`SC-P3-09`): nếu `currentStock.Quantity != detail.SystemQuantity` (tồn kho đã biến động sau thời điểm chụp snapshot lúc tạo nháp), từ chối duyệt với `409 Conflict` (`CONCURRENCY_CONFLICT`).
  - Khi hợp lệ: cập nhật tồn kho theo delta (`Quantity += diff`) và sinh bút toán `InventoryTransaction` (`AdjustmentIncrease` / `AdjustmentDecrease`) khớp tuyệt đối với `Math.Abs(diff)` để bảo toàn `SUM(Ledger) == Balance`.
- `GET /api/InventoryReconciliation`: Đối soát tồn kho giữa `InventoryStocks` và tổng đại số `InventoryTransactions` (`SUM(Ledger) == Balance`).

## 6. Inventory Control APIs (Tồn kho, Khóa tồn, Đảo bút toán & Lịch sử)
- `GET /api/InventoryStocks/current`: Truy vấn tồn kho khả dụng hiện hành (`Status == Available`, vị trí khả dụng `IsActive && !IsBlocked && IsPickable`, lô chưa hết hạn, và loại trừ các bucket đang có `InventoryLock` active qua `.UnlockedAt(UtcNow)`).
- `GET /api/InventoryTransactions`: Truy vấn sổ cái giao dịch tồn kho bất biến (`Rule 2` append-only).
- `POST /api/inventory-locks` & `POST /api/inventory-locks/{id}/release`: Tạo và giải phóng khóa tồn kho (`InventoryLock`).
- `POST /api/inventory-reversals`: Đảo giao dịch nội bộ (`Move` / `StatusChange`) bằng cặp bút toán `Reversal` + giao dịch hiệu chỉnh đối ứng (`CorrectiveTransactionId`), không sửa/xóa dòng gốc.

## 7. Report APIs (Báo cáo & Xuất file)
- **JSON endpoints**:
  - `GET /api/Reports/inventory`: Lấy dữ liệu tồn kho theo ngày (chỉ tính các giao dịch có dấu chuẩn `HasCanonicalSign()`).
  - `GET /api/Reports/inventory-in-out-stock`: Lấy báo cáo Xuất-Nhập-Tồn.
- **Excel Export endpoints**:
  - `GET /api/Reports/inventory/export`: Xuất file `BaoCaoTonKho.xlsx`.
  - `GET /api/Reports/inventory-in-out-stock/export`: Xuất file `BaoCaoXuatNhapTon.xlsx`.

## 8. Swagger/OpenAPI Export
- Để xem tài liệu OpenAPI trực tiếp khi chạy cục bộ (`dotnet run --project ERP.Api/ERP.Api.csproj`), truy cập `http://localhost:5000/swagger/v1/swagger.json`.

