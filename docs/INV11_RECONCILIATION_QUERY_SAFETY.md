# INV-11 — An toàn phân trang và ranh giới đối chiếu tồn kho

## Thay đổi hệ thống thật

- `GET /api/InventoryReconciliation`: chuẩn hóa `pageIndex >= 1`, `1 <= pageSize <= 100` trước SQL.
- Tính `Skip` bằng `long` để ngăn tràn Int32; nếu trang đã ngoài `TotalRecords`, trả kết quả rỗng cùng tổng bản ghi chính xác, không phát query SQL sai offset.
- Duy trì thứ tự truy vấn xác định theo `WarehouseId, ProductId`; chuẩn hóa từ khóa bằng `Trim` và `ToLowerInvariant` trước khi tìm kiếm.
- Không thêm thao tác ghi dữ liệu, không tự sửa Balance hoặc Ledger.
- Hiển thị rõ trên giao diện: reconciliation hiện chỉ bao phủ `AVAILABLE` theo kho / sản phẩm. Đây chưa phải kiểm tra toàn bộ chiều Owner/HU/Reserved/Allocated hoặc quy trình rebuild/remediation theo Notion Spec 82.

## Kiểm thử

- Unit/InMemory: `pageIndex=int.MaxValue`, `pageSize=int.MaxValue` không tràn và trả trang rỗng; `pageIndex<1`, `pageSize=0` được chuẩn hóa.
- Unit/InMemory: tìm kiếm loại bỏ khoảng trắng và không phân biệt chữ hoa/chữ thường.
- Vitest: thông báo giới hạn phạm vi xuất hiện cả trên bản xem trước chỉ đọc.

## Nghiệm thu

Bảo toàn phân loại `INV-11 = foundation`; không nâng `live` vì chưa có snapshot/rebuild/remediation/approval/audit đầy đủ. Chỉ nhận CI đúng HEAD, browser QA và staging API/SQL là cổng riêng. Notion chỉ đọc; nhãn UX/UI tiếng Việt và tên code/API kỹ thuật giữ tiếng Anh.
