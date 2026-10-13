# INV-11 — Phân quyền kho và đối soát sổ cái an toàn

Ngày: 09/10/2026. Nhánh PR #31 Draft, không tự nâng Blueprint `live`.

## Nghiệp vụ thật đã thay đổi

- `GET /api/InventoryReconciliation` và `GET /api/InventoryReconciliation/warehouses` yêu cầu **đã đăng nhập** và `inventory_ledger.read`; không còn cấp xem dữ liệu đối soát cho mọi vai trò chỉ vì có phiên đăng nhập.
- Backend reject `warehouseId <= 0` hoặc `productId <= 0` trước truy vấn phân quyền/SQL, không coi số âm/0 là bỏ lọc. Khi chỉ định kho, bắt buộc `EnsureWarehouseAccessAsync(warehouseId)`; kho không có quyền hoặc không tồn tại trả 404 fail-closed (bao gồm trường hợp global admin truy kho không tồn tại).
- Khi không chỉ định kho, danh sách kho được quyền được lấy từ `IWarehouseAuthorizationService` và áp **trước** Union giữa cặp Stock/Ledger. Cả bước lấy tồn hiện tại lẫn bước tính Ledger tổng hợp đều tái sử dụng `stockQuery/transactionQuery` đã được lọc quyền, không đọc dữ liệu thừa của kho không được cấp quyền.
- Endpoint danh sách kho trả ID/mã/tên từ các kho người dùng có quyền, không truy cập endpoint `/api/warehouses` (quyền master data khác và có thể rộng hơn). Nếu kết quả API bị lỗi, sai kiểu, trùng ID hoặc thiếu mã/tên, giao diện xóa selector, khóa lựa chọn và hiển thị thông báo lỗi.
- UI tiếng Việt kiểm tra ID sản phẩm là số nguyên dương an toàn cả khi nhập số phân số, âm, chữ, vượt safe integer. Form `noValidate` cho phép JS hiển thị lỗi/focus trước HTTP, backend vẫn là biên bảo mật cuối.
- UI phân biệt rõ thông báo 403 (thiếu quyền Ledger), 404 (kho không tồn tại/không được phân quyền), lỗi kết nối. Báo cáo vẫn chỉ đọc, **không** tự sửa bất biến Ledger/Balance hay công bố đã có controlled rebuild.

## Regression và nghiệm thu

- Unit/InMemory: ID sai fail trước warehouse authorization, mặc định chỉ các kho có quyền ở cả count+totals, kho chỉ định không quyền bị NotFound, kho có quyền được tính bình thường, danh sách kho theo quyền.
- SQL Server: tạo kho thực không gán quyền và ledger thật của sản phẩm cùng fixture, xác minh không rò kho ngoài quyền ở selector/kết quả, từ chối kho ngoài quyền và ID kho không tồn tại.
- API: kiểm tra `Authorize` và `PermissionAuthorize(inventory_ledger.read)`, danh sách kho được trả đúng hợp đồng, báo cáo cũ không đổi schema.
- Vitest: selector đúng phạm vi, lỗi dữ liệu bất thường, ID phân số/không an toàn fail trước tìm kiếm, thông báo lỗi 403/404.
- **Chỉ ghi PASS khi đúng commit chạy CI Application SQL/API/Vitest/lint/build thành công**. Chưa có bằng chứng browser QA tương tác và staging backend SQL/API thực; PR giữ Draft, Notion chỉ đọc.

## Chưa triển khai

- Chưa có rebuild projection, repair transaction/approval/audit/idempotency, lineage owner/HU theo thời điểm và đối soát từng bucket owner/location/lot/serial. INV-11 tiếp tục là `foundation`; cần thiết kế riêng quy trình remediation có approval, quyền tối thiểu, preview chênh lệch, khóa SQL an toàn, audit và rollback trước khi cho phép tác động tồn kho.
