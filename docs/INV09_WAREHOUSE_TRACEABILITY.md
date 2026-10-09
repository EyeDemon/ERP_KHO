# INV-09 — Truy vết theo kho được cấp quyền

## Chế độ tìm kiếm

- Giao diện `/inventory-traceability` cho phép chỉ nhập **ID kho** để xem nhóm tồn kho hiện tại và các sự kiện sổ cái mới nhất của kho đó.
- Không nhập bất kỳ kho, sản phẩm, lô, sê-ri hoặc cặp chứng từ nào vẫn bị **từ chối**; không cho tìm kiếm mọi kho mà không có điều kiện.
- `GET /api/inventory/traceability?warehouseId=<id>&limit=200` sử dụng quyền `inventory_traceability.read`; backend phải xác minh quyền truy cập **kho được chỉ định**, không dựa vào tính năng lọc trên frontend.
- Có thể kết hợp kho với sản phẩm/lô/sê-ri hoặc cặp `referenceType` + `referenceId`. Hai trường chứng từ luôn phải đi cùng nhau.

## Phân biệt tồn hiện tại với lịch sử

- **Tra theo kho đơn lẻ:** lấy các nhóm tồn *trực tiếp* từ `InventoryStocks`, kể cả nhóm chưa có sự kiện mới hoặc sự kiện không còn trong cửa sổ kết quả; sự kiện sổ cái là dòng thời gian riêng.
- **Tra theo chứng từ mà không chọn sản phẩm/lô/sê-ri:** chỉ lấy nhóm tồn khớp chính xác định danh từ các sự kiện tham chiếu (sản phẩm/kho/lô/sê-ri), áp dụng điều kiện trước khi giới hạn kết quả SQL. Khi không có sự kiện tham chiếu thì không đoán ra tồn kho liên quan.
- **Tra theo sản phẩm/lô/sê-ri:** giữ bộ lọc hiện hành và quyền kho.
- Sự kiện gần nhất bị giới hạn bởi tham số `limit` (mặc định 200, tối đa 500); chuỗi đảo liên quan vẫn được nối khi cần để không đứt phả hệ.
- Nhóm tồn hiện tại giới hạn **500** dòng, đọc 501 để bật `bucketsTruncated`; giao diện cảnh báo rõ, không biến danh sách 500 thành tuyên bố toàn bộ kho đã được xem.
- Không cộng số lượng hoặc sửa sổ cái trong API này: toàn bộ thao tác **read-only**.

## Kiểm thử và nghiệm thu

- SQL Server: truy theo kho khi không có giao dịch sổ cái vẫn trả nhóm tồn hiện hữu; gọi kho không được phép trả NotFound; trường hợp 501 nhóm tồn bật cờ bị cắt.
- SQL Server: lọc theo chứng từ vẫn tìm đúng nhóm tồn sau vị trí thứ 500, không trả nhóm lô/serial không thuộc tham chiếu.
- API: truyền đúng `warehouseId` đến service, không bỏ hoặc mở rộng bộ lọc.
- Frontend: truy bằng kho đơn lẻ gửi request đúng, vẫn chặn yêu cầu trống.
- Chỉ công nhận PASS khi CI đúng HEAD thành công; trình duyệt và staging API/SQL thật cần nghiệm thu riêng. Vercel Preview demo READY không xác nhận backend staging.
- Notion là nguồn đặc tả **chỉ đọc**, không chỉnh sửa khi triển khai phần này.
