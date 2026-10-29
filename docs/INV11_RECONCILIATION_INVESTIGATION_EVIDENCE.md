# INV-11 — Hồ sơ điều tra chênh lệch tồn kho/ledger (đọc thật)

Ngày triển khai: 09/10/2026. Phạm vi: PR #31 Draft.

## Chức năng thực

- `GET /api/InventoryReconciliation/investigation?warehouseId=<id>&productId=<id>&eventAnchorId=<optional>&limit=50` dùng `inventory_ledger.read` trên cả class `InventoryReconciliationController`. Chỉ nhận kho và sản phẩm cụ thể, không có endpoint điều tra rộng toàn bộ kho.
- Trước truy vấn chi tiết: xác nhận ID kho/sản phẩm dương, mốc sự kiện không âm, limit 1–100; gọi `EnsureWarehouseAccessAsync`. Kho không thuộc quyền, kho/sản phẩm không có thật và cặp không có bucket/sự kiện đều thất bại có kiểm soát; không lộ dữ liệu từ kho khác.
- Kết quả từ DB thật gồm cặp kho/sản phẩm, tổng bucket trạng thái `AVAILABLE` hiện tại, tổng Ledger `AVAILABLE` có dấu theo `TransactionType.ApplySign`, chênh lệch, số event và bucket, mốc Ledger ID, **tối đa 100 bucket và 1–100 events** kèm cờ giới hạn, Lot/Serial/Location, tham chiếu và ID sự kiện.
- `eventAnchorId`: lần đầu lấy ID cao nhất trong Ledger của cặp đó; lần truy vấn sau với cùng anchor sẽ không thêm các sự kiện được ghi muộn hơn vào danh sách/đếm/tổng Ledger. Các sự kiện được sắp theo ID giảm dần, không theo `TransactionDate` vốn có thể bị backdate.
- **Cảnh báo quan trọng:** số dư và bucket hiện tại không phải snapshot tại mốc lịch sử. Nếu Ledger tiến lên sau mốc hoặc tồn kho biến động giữa các truy vấn SQL thì chênh lệch là thông tin *tham khảo điều tra*, không được dùng như giá trị đầu vào của lệnh sửa tồn.
- React work center dùng UI primitives đã có, nút `Xem bằng chứng` trên từng cặp, bảng bucket/ledger, loading/error/empty/close & focus restore; khi đổi trang/bộ lọc/unmount, invalidate request cũ, không hiển thị bằng chứng hết phạm vi. Blueprint demo chỉ trình bày dữ liệu mô phỏng và **khóa nút tải bằng chứng thật**.

## Tính bất biến & nguyên tắc an toàn

- Không tạo `POST/PATCH` hoặc service sửa, không ghi InventoryStock, InventoryTransaction, AuditLog hay cập nhật trạng thái chứng từ.
- Không giả định `AVAILABLE` đại diện cho mọi trạng thái tồn; chưa bao phủ owner, HU, trạng thái khác, lịch sử tại thời điểm trước eventAnchorId hay nguồn Receipt/QC của từng lô.
- Chưa đủ điều kiện để build/rebuild projection hoặc sửa chênh lệch tự động. Trước remediation thật phải có: chính sách nghiệp vụ, phê duyệt và role riêng, transaction locking/serializable, idempotency, kiểm tra fingerprint của cặp trước và sau, audit bất biến và rollback.
- Tránh tuyên bố INV-11 đã live. Giữ trạng thái `foundation` cho đến nghiệm thu browser thực và staging backend SQL/API, chưa merge PR.

## Bằng chứng QA / cổng kiểm thử

- Application/InMemory: validation fail trước quyền; kho không được phép 404; tổng signed Ledger 6 sự kiện và bucket tồn; pagination 2 sự kiện; anchor không thay đổi sau event mới; missing pair và product fail closed.
- Application/SQL Server: fixture kho/sản phẩm/lô/vị trí thật, Ledger Import + AdjustmentIncrease; chứng minh số dư hiện tại khác expected và chi tiết bucket/lô; bổ sung Export có thời gian bất kỳ, anchor giữ kết quả cũ trong khi lần query mới thấy sự kiện mới; limit >100 bị từ chối.
- API: route `investigation`, quyền `inventory_ledger.read`, trả DTO chính xác. Vitest: mở/đóng, khôi phục focus, không hiển thị trong demo, chống response cũ khi đổi bộ lọc, lỗi quyền 403.
- **Chỉ công nhận PASS khi GitHub Actions chạy toàn bộ trên chính commit** (backend build, SQL/Application tests, API, frontend lint/Vitest/build) và Vercel Preview đúng SHA READY. CI PASS không thay thế staging/backend & browser QA.
