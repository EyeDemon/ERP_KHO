# Inventory Control bản thiết kế Domain Mocks

Trạng thái: **THAM CHIẾU GIAO DIỆN BẢN THIẾT KẾ — trạng thái hệ thống thật đồng bộ ngày 2026-10-08**

Tài liệu này mô tả các bảng mô phỏng chuyên biệt chỉ dành cho bản thiết kế. Bản thân tài liệu **không quyết định mức trưởng thành hệ thống thật**. Trạng thái production lấy từ nhánh tích hợp đã xác minh và `frontend/src/config/erpWmsbản thiết kế.ts`; Notion tiếp tục là tài liệu nghiệp vụ/đặc tả chuẩn ở chế độ chỉ đọc.

## Các bảng chức năng chuyên biệt

bản thiết kế Kiểm soát tồn kho gồm các bảng mô phỏng chỉ đọc theo từng capability:

### Các nền tảng hệ thống thật

- **INV-05 — Inventory Status**
  - số lượng theo trạng thái;
  - điều kiện được giữ hàng/phân bổ/lấy hàng;
  - rào chắn giữ/chặn/mở.
  - Mức trưởng thành hệ thống thật: `foundation`.

- **INV-06 — Lot / Serial / Expiry**
  - tìm định danh lô/sê-ri;
  - ngữ cảnh hạn dùng / FEFO;
  - bảo vệ chống trùng định danh.
  - Mức trưởng thành hệ thống thật: `foundation`.

- **INV-07 — Inventory Locks / Freeze**
  - phạm vi khóa;
  - loại khóa / semantics khi nhiều khóa chồng lấp;
  - ảnh hưởng tới công việc đang mở.
  - Mức trưởng thành hệ thống thật: `foundation`.

- **INV-08 — Internal Location Move**
  - di chuyển vị trí trong cùng kho;
  - tương thích khóa/sức chứa/lưu trữ;
  - sổ cái MOVE và bảo toàn số lượng.
  - Mức trưởng thành hệ thống thật: `foundation`.

- **INV-09 — Đảo giao dịch**
  - giao dịch sổ cái gốc bất biến;
  - giao dịch hiệu chỉnh + dấu mốc đảo;
  - liên kết cấu trúc Gốc / Hiệu chỉnh / Dấu đảo;
  - ràng buộc cơ sở dữ liệu chống đảo lặp;
  - phạm vi hệ thống thật hiện hỗ trợ Di chuyển vị trí nội bộ và Đổi trạng thái tồn kho.
  - Mức trưởng thành hệ thống thật: `foundation`.

- **INV-10 — Truy vết & phả hệ tồn kho**
  - truy theo Sản phẩm / Lô / Sê-ri / Tham chiếu trong phạm vi kho được phép;
  - nhóm tồn hiện tại + dòng thời gian sổ cái bất biến;
  - phép chiếu chuỗi đảo có cấu trúc;
  - phả hệ và điều phối Return/Recall đầy đủ vẫn chưa hoàn tất.
  - Mức trưởng thành hệ thống thật: `foundation`.

## Các nền tảng hệ thống thật khác

- **INV-01 — Inventory Browser** — `foundation` route.
- **INV-02 — Immutable Inventory Ledger** — `foundation`.
- **INV-03 — Balance Projection** — `foundation`.
- **INV-04 — Availability Engine** — `foundation`.
- **INV-11 — Integrity & Reconciliation** — `foundation` route.

## Các quy tắc toàn vẹn được thể hiện trên UI

- Bản ghi sổ cái đã ghi là bất biến.
- Thao tác trạng thái không được bỏ qua chính sách khóa/bảo mật.
- Định danh lô/sê-ri không được bỏ qua điều kiện hợp lệ của tồn kho.
- Đảo giao dịch tạo bản ghi mới có dấu; không bao giờ sửa/xóa dòng sổ cái lịch sử.
- Phả hệ thiếu liên kết nguồn/tương quan phải hiển thị là chưa đầy đủ thay vì tự suy diễn.
- Quy tắc di chuyển trong cùng kho tiếp tục thuộc INV-08 và bảo toàn tổng tồn thực tế của kho.

## Ranh giới môi trường chạy

Các bảng chuyên biệt này chỉ thuộc bản thiết kế:

- không gọi API thay đổi dữ liệu hệ thống thật;
- không ghi vào cơ sở dữ liệu;
- không thay đổi sổ cái hoặc tồn kho;
- không được hiển thị lệnh hệ thống thật thành công giả;
- không được nâng mức trưởng thành chức năng chỉ vì có mô phỏng.

INV-05 đến INV-10 hiện đều có nền tảng hệ thống thật ở phần hệ thống tương ứng. INV-09 và INV-10 chỉ được nâng trạng thái sau khi đã merge, CI sau merge xanh và có bằng chứng bản triển khai hệ thống thật ở trạng thái READY; các khoảng trống chuẩn đã nêu giữ chúng ở `foundation`, không phải `live`.

## UI/UX

Các màn hình tuân theo định hướng UI UX Pro Max của dự án:

- bố cục vận hành phẳng, tối giản;
- trạng thái có chữ mang nghĩa, không chỉ dựa vào màu;
- bảng dữ liệu thích ứng trong vùng chứa;
- trạng thái số dễ đọc;
- không dùng emoji làm icon cấu trúc;
- an toàn với chế độ giảm chuyển động;
- cảnh báo và hướng phục hồi rõ ràng.
