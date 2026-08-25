# Tài liệu API KHO - ERP KHO

Tài liệu này đóng vai trò là hướng dẫn sử dụng và danh sách kiểm tra (Checklist) cho các API hiện có trong hệ thống ERP KHO. 

## 1. Xác thực và Phân quyền (Authentication / Authorization)
- **Cơ chế**: Dùng JWT Token.
- **Header**: Truyền Authorization: Bearer <token> vào request.
- **Endpoints**:
  - POST /api/Auth/login: Đăng nhập, nhận JWT Token. 
- **Roles**:
  - Viewer: Quyền xem báo cáo, tra cứu dữ liệu.
  - WarehouseStaff: Quyền thao tác nghiệp vụ kho (tạo/duyệt một số luồng cơ bản).
  - Manager: Quản lý cấp cao.
  - Admin: Có quyền cao nhất.
- **Phân nhóm Role (thường gặp trong source)**:
  - Admin, Manager, Viewer: Các endpoints dạng đọc/báo cáo (read/report style).
  - Admin, Manager, WarehouseStaff: Các endpoints thao tác nghiệp vụ (warehouse operations).
  - Admin, Manager: Các endpoints thay đổi dữ liệu nền tảng (master-data mutation).

## 2. Master Data APIs (Dữ liệu nền tảng)
Dùng để quản lý các danh mục.
- **Products**:
  - GET /api/Products: Lấy danh sách sản phẩm.
  - GET /api/Products/{id}: Lấy chi tiết.
  - POST /api/Products: Tạo mới.
  - PUT /api/Products/{id}: Cập nhật.
  - DELETE /api/Products/{id}: Xóa.
- **Warehouses** & **Units**: Tương tự Products. (vd: /api/Warehouses, /api/Units).

## 3. Import Receipt APIs (Nhập kho)
Quy trình: Tạo phiếu nhập (Draft) -> Thêm chi tiết phiếu -> Duyệt phiếu (Approve).
- GET /api/ImportReceipts: Lấy danh sách phiếu nhập.
- GET /api/ImportReceipts/{id}: Lấy chi tiết phiếu.
- POST /api/ImportReceipts: Tạo phiếu nhập kho (trạng thái ban đầu Draft).
- POST /api/ImportReceipts/{id}/approve: Duyệt phiếu (Yêu cầu role Admin/Manager/WarehouseStaff), cập nhật tồn kho.
- PUT /api/ImportReceipts/{id}/cancel: Hủy phiếu.

## 4. Export Receipt APIs (Xuất kho)
Quy trình: Tạo phiếu xuất (Draft) -> Kiểm tra tồn kho -> Duyệt phiếu (Approve).
- GET /api/ExportReceipts: Lấy danh sách phiếu xuất.
- GET /api/ExportReceipts/{id}: Lấy chi tiết phiếu.
- POST /api/ExportReceipts: Tạo phiếu xuất kho.
- POST /api/ExportReceipts/{id}/approve: Duyệt phiếu, xuất trừ tồn kho.
- POST /api/ExportReceipts/{id}/cancel: Hủy phiếu.

## 5. Stocktakes & Inventory Reconciliation APIs (Kiểm kê)
- POST /api/Stocktakes/{id}/approve: Phê duyệt phiếu kiểm kê, sẽ tự động sinh giao dịch điều chỉnh (AdjustmentIncrease/AdjustmentDecrease).
- GET /api/InventoryReconciliation: Đối soát tồn kho (trả về danh sách chênh lệch). Các tham số: warehouseId, productId, keyword, page, pageSize.

## 6. Inventory APIs (Tồn kho & Lịch sử)
- **Tồn kho hiện hành**:
  - GET /api/InventoryStocks/current: Query lấy tổng hợp tồn kho có thể filter theo warehouseId, productId, keyword...
- **Lịch sử giao dịch**:
  - GET /api/InventoryTransactions: Truy vấn lịch sử giao dịch (nhập, xuất, điều chỉnh) có phân trang.

## 7. Report APIs (Báo cáo & Xuất file)
- **JSON endpoints**:
  - GET /api/Reports/inventory: Lấy dữ liệu tồn kho theo ngày.
  - GET /api/Reports/inventory-in-out-stock: Lấy báo cáo Xuất-Nhập-Tồn.
- **Excel Export endpoints**:
  - GET /api/Reports/inventory/export: Xuất file BaoCaoTonKho.xlsx.
  - GET /api/Reports/inventory-in-out-stock/export: Xuất file BaoCaoXuatNhapTon.xlsx.

## 8. Swagger/OpenAPI Export
- Việc xuất file Swagger JSON trực tiếp không được thực hiện tự động vì việc chạy ứng dụng (dotnet run) có thể yêu cầu cấu hình runtime hợp lệ (như chuỗi kết nối Database LocalDB).
- Nếu cần file Swagger JSON, người lập trình có thể khởi chạy ứng dụng cục bộ (dotnet run --project ERP.Api/ERP.Api.csproj) và gọi địa chỉ http://localhost:5000/swagger/v1/swagger.json.
- Danh sách Checklist thủ công phía trên là nguồn tham chiếu (fallback) chính thức cho các endpoints đã triển khai trong hệ thống.
