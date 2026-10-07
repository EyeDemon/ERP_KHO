# Inventory Control Blueprint Domain Mocks

Trạng thái: **THAM CHIẾU UI BLUEPRINT — trạng thái production đồng bộ ngày 2026-10-08**

Tài liệu này mô tả các panel mock chuyên biệt chỉ dành cho Blueprint. Bản thân tài liệu **không quyết định mức trưởng thành production**. Trạng thái production lấy từ nhánh tích hợp đã xác minh và `frontend/src/config/erpWmsBlueprint.ts`; Notion tiếp tục là tài liệu nghiệp vụ/đặc tả chuẩn ở chế độ chỉ đọc.

## Các panel capability chuyên biệt

Blueprint Kiểm soát tồn kho gồm các panel mock chỉ đọc theo từng capability:

### Các nền tảng production

- **INV-05 — Inventory Status**
  - số lượng theo trạng thái;
  - điều kiện được giữ hàng/phân bổ/lấy hàng;
  - rào chắn giữ/chặn/mở.
  - Mức trưởng thành production: `foundation`.

- **INV-06 — Lot / Serial / Expiry**
  - tìm định danh lô/sê-ri;
  - ngữ cảnh hạn dùng / FEFO;
  - bảo vệ chống trùng định danh.
  - Mức trưởng thành production: `foundation`.

- **INV-07 — Inventory Locks / Freeze**
  - phạm vi khóa;
  - loại khóa / semantics khi nhiều khóa chồng lấp;
  - ảnh hưởng tới công việc đang mở.
  - Mức trưởng thành production: `foundation`.

- **INV-08 — Internal Location Move**
  - di chuyển vị trí trong cùng kho;
  - tương thích khóa/sức chứa/lưu trữ;
  - sổ cái MOVE và bảo toàn số lượng.
  - Mức trưởng thành production: `foundation`.

- **INV-09 — Đảo giao dịch**
  - giao dịch sổ cái gốc bất biến;
  - giao dịch hiệu chỉnh + dấu mốc đảo;
  - liên kết cấu trúc Gốc / Hiệu chỉnh / Dấu đảo;
  - ràng buộc cơ sở dữ liệu chống đảo lặp;
  - phạm vi production hiện hỗ trợ Di chuyển vị trí nội bộ và Đổi trạng thái tồn kho.
  - Mức trưởng thành production: `foundation`.

- **INV-10 — Truy vết & phả hệ tồn kho**
  - truy theo Sản phẩm / Lô / Sê-ri / Tham chiếu trong phạm vi kho được phép;
  - nhóm tồn hiện tại + dòng thời gian sổ cái bất biến;
  - phép chiếu chuỗi đảo có cấu trúc;
  - phả hệ và điều phối Return/Recall đầy đủ vẫn chưa hoàn tất.
  - Mức trưởng thành production: `foundation`.

## Các nền tảng production khác

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

## Ranh giới runtime

Các panel chuyên biệt này chỉ thuộc Blueprint:

- không gọi API mutation production;
- không ghi vào cơ sở dữ liệu;
- không thay đổi sổ cái hoặc tồn kho;
- không được hiển thị lệnh production thành công giả;
- không được nâng maturity capability chỉ vì có mock.

INV-05 đến INV-10 hiện đều có nền tảng production thật ở phần hệ thống tương ứng. INV-09 và INV-10 chỉ được nâng trạng thái sau khi đã merge, CI sau merge xanh và có bằng chứng deployment production READY; các khoảng trống chuẩn đã nêu giữ chúng ở `foundation`, không phải `live`.

## UI/UX

Các màn hình tuân theo định hướng UI UX Pro Max của dự án:

- bố cục vận hành phẳng, tối giản;
- trạng thái có chữ mang nghĩa, không chỉ dựa vào màu;
- bảng dữ liệu responsive trong vùng chứa;
- trạng thái số dễ đọc;
- không dùng emoji làm icon cấu trúc;
- an toàn với chế độ giảm chuyển động;
- cảnh báo và hướng phục hồi rõ ràng.
