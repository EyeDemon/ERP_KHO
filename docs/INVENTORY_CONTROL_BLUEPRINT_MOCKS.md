# Mô phỏng chuyên biệt của bản thiết kế Kiểm soát tồn kho

**Trạng thái:** Tài liệu tham chiếu giao diện, chỉ đọc. Đã đối chiếu hệ thống thật ngày 08/10/2026.

Tài liệu mô tả các màn hình mô phỏng để đối chiếu nghiệp vụ. **Mô phỏng không phải là bằng chứng chức năng đã triển khai.** Trạng thái triển khai được quản lý trong `frontend/src/config/erpWmsBlueprint.ts`, đối chiếu với mã nguồn nhánh `feature/erp-wms-complete-ui-blueprint` và bản phát hành production đã xác minh. Notion là nguồn tài liệu chuẩn, chỉ đọc và không chỉnh sửa.

## Các chức năng đã có nền tảng trên hệ thống thật

- **INV-01 — Trình duyệt tồn kho:** Tra cứu nhóm tồn, lượng thực tế/đã giữ/khả dụng; trạng thái `foundation`.
- **INV-02 — Sổ cái tồn kho bất biến:** Ghi nhận biến động theo giao dịch; trạng thái `foundation`.
- **INV-03 — Dự phóng số dư tồn kho:** Số dư vận hành phục vụ truy vấn; trạng thái `foundation`.
- **INV-04 — Bộ máy tính khả dụng:** Kiểm tra điều kiện của nhóm tồn; trạng thái `foundation`.
- **INV-05 — Trạng thái tồn kho:** Lượng theo trạng thái, điều kiện giữ/phân bổ/lấy hàng và chặn/mở có kiểm soát; trạng thái `foundation`.
- **INV-06 — Lô / Sê-ri / Hạn dùng:** Truy tìm định danh, hạn dùng, nguyên tắc FEFO và chống trùng số sê-ri; trạng thái `foundation`.
- **INV-07 — Khóa / đóng băng tồn kho:** Phạm vi và loại khóa, tác động của các khóa chồng lấp tới công việc; trạng thái `foundation`.
- **INV-08 — Di chuyển vị trí nội bộ:** Chuyển hàng trong cùng kho, kiểm tra khóa/sức chứa, bảo toàn số lượng và ghi sổ cái `MOVE`; trạng thái `foundation`.
- **INV-09 — Đảo giao dịch:** Giao dịch gốc bất biến, giao dịch hiệu chỉnh và dấu mốc đảo liên kết có cấu trúc; chống đảo lặp bằng ràng buộc cơ sở dữ liệu. Hiện chỉ bao phủ di chuyển nội bộ và đổi trạng thái tồn kho; trạng thái `foundation`.
- **INV-10 — Truy vết & phả hệ tồn kho:** Tra cứu theo Sản phẩm/Lô/Sê-ri/Tham chiếu trong phạm vi kho được phép; hiển thị nhóm tồn hiện tại, dòng thời gian sổ cái và chuỗi Gốc → Hiệu chỉnh → Dấu đảo; trạng thái `foundation`.
- **INV-11 — Toàn vẹn & đối chiếu tồn kho:** Phát hiện số dư khớp/lệch ở chế độ chỉ đọc; trạng thái `foundation`.

## Các giới hạn chưa hoàn thiện

**INV-09** chưa bao phủ đầy đủ nghiệp vụ đảo phiếu nhập, giao hàng, điều chuyển, điều chỉnh, hàng trả lại và tiêu hủy; chính sách chặn đảo khi có phụ thuộc hạ nguồn vẫn chưa hoàn tất.

**INV-10** chưa có phả hệ xuyên suốt từ Nhận hàng → Kiểm tra chất lượng → Di chuyển → Lấy hàng → Giao hàng → Trả hàng/Thu hồi, điều phối thu hồi đầy đủ hoặc chuỗi theo Chủ sở hữu/Đơn vị xử lý hàng.

Mức `foundation` được xác nhận từ PR #29, CI sau hợp nhất #575 và bản triển khai production `dpl_Ha1vNBRkxFyhuHp3tk3BkvS4AeYS` trên commit `b9d926ef8d068b57d7ec26e7148e031a685fe477`. Không nâng thành `live` chỉ vì giao diện mô phỏng có sẵn.

## Nguyên tắc toàn vẹn

- Không sửa hoặc xóa giao dịch sổ cái đã ghi.
- Đảo giao dịch tạo bản ghi mới có dấu và lưu liên kết kiểm toán.
- Trạng thái, lô, sê-ri và việc mở khóa không được bỏ qua kiểm tra quyền và điều kiện tồn kho.
- Chuỗi truy vết thiếu liên kết phải hiển thị là **chưa đầy đủ**, không được tự suy đoán.
- Di chuyển vị trí nội bộ phải bảo toàn tổng tồn thực tế trong kho.

## Ranh giới giữa mô phỏng và hệ thống thật

Các màn hình mô phỏng này không gửi yêu cầu thay đổi dữ liệu production, không ghi cơ sở dữ liệu và không tạo giao dịch sổ cái. Không được hiển thị kết quả thay đổi giả như thể đã xử lý trên hệ thống thật.

## Tiêu chuẩn giao diện

Các màn hình tuân thủ `ui-ux-pro-max` và `design-system/erp-wms/MASTER.md`: nhãn rõ ràng bằng tiếng Việt, trạng thái có chữ thay vì chỉ có màu, bảng dữ liệu thích ứng trong vùng chứa, số liệu dễ đọc, điều hướng bằng bàn phím, thông báo lỗi và hướng phục hồi minh bạch, hỗ trợ chế độ giảm chuyển động. Không sử dụng emoji làm biểu tượng giao diện có tính cấu trúc.
