# INV-11 — Phân trang bằng chứng bucket (SQL keyset, chỉ đọc)

Mốc triển khai: 10/10/2026. Phạm vi nhánh PR #31 Inventory Control, không áp dụng sửa tồn kho.

## Hợp đồng và tính đúng đắn

- `bucketStatus` chọn chính xác một trong 8 tên enum: `Available`, `QcHold`, `Quarantine`, `Damaged`, `Rejected`, `Blocked`, `Expired`, `RecallBlocked`. Mặc định `Available` để giữ tương thích API cũ. Giá trị không đúng tên chuẩn (kể cả số enum, tên sai hoa/thường) bị chặn trước truy vấn hoặc kiểm tra quyền; phản hồi trả `bucketStatus` để client đối chiếu với request.
- `GET /api/InventoryReconciliation/investigation` bổ sung `bucketAnchorId` và `bucketAfterId`, đều là số nguyên. Mốc có thể là `0` khi chưa có bucket ở trạng thái được chọn; `bucketAfterId` chỉ hợp lệ khi có mốc dương và `0 < bucketAfterId < bucketAnchorId`.
- Lượt đầu tính `bucketAnchorId = MAX(InventoryStock.Id)` trong **đúng kho, sản phẩm và `bucketStatus` đang được chọn**. Mọi lượt sau giữ mốc cũ và truy vấn `Id <= bucketAnchorId AND Id > bucketAfterId`, `ORDER BY Id ASC`, `Take(101)` và trả tối đa **100 bucket/trang**. Không dùng OFFSET; không đọc toàn bộ hàng tồn vào bộ nhớ.
- Trả `bucketAnchorId`, `bucketAfterId`, `nextBucketAfterId`, `bucketHasRowsAfterAnchor` và `bucketsTruncated`. `bucketCount` chỉ đếm những hàng thuộc mốc bucket đã chọn, không đếm INSERT mới. `nextBucketAfterId` là ID bucket cuối trên trang hiện tại, chỉ xuất hiện khi còn hàng cũ.
- Quyền `inventory_ledger.read` vẫn ở controller, `EnsureWarehouseAccessAsync` chạy lại trên **mỗi lượt tải** trước mọi truy vấn chi tiết. Cặp kho/sản phẩm không được chuyển ngầm sang cặp khác.
- Đổi trạng thái bucket sẽ **xóa bucket cursor/anchor** và tải lại ở trạng thái mới; vẫn giữ mốc và trang Ledger. Client đối chiếu `bucketStatus` trong response và từ chối phản hồi sai trạng thái để ngăn nhầm dữ liệu thuộc scope khác.
- Bucket cursor **độc lập** với Ledger `eventAnchorId/eventBeforeId`: chuyển trang bucket giữ nguyên trang Ledger, và ngược lại. Đổi lọc/kho, đóng hồ sơ, unmount hoặc refresh mốc đều xóa cursor cũ; response đến muộn không được ghi đè hồ sơ mới.
- Không có endpoint sửa dữ liệu, không thêm migration, không mutate Ledger/Balance/Audit.

## Giới hạn không được diễn giải sai

- `bucketAnchorId` là **high-water ID**, chỉ ngăn INSERT mới chen vào các trang cũ. Nó **không** khóa hay chụp ảnh nhất quán quantity/status/reservation của các hàng hiện tại: một bucket đang có thể bị sửa, xóa hoặc đổi trạng thái giữa hai lượt. `bucketHasRowsAfterAnchor` chỉ báo có ID mới, không chứng minh dữ liệu cũ không thay đổi.
- Bucket có thể duyệt riêng theo 8 trạng thái, nhưng bảng sự kiện chi tiết trong cùng hồ sơ **vẫn chỉ trình bày Ledger AVAILABLE**; không suy diễn là đã có lịch sử chứng từ riêng được phân trang cho 8 trạng thái. Tổng current stock/8-status là số liệu ở thời điểm truy vấn; expected Ledger cố định theo mốc Ledger khác. Kết quả chỉ để điều tra. Chưa có snapshot transaction xuyên trang, owner/HU lineage, bucket lịch sử từng trạng thái hay workflow remediation có phê duyệt.
- Không dùng dữ liệu bucket trang 1 hoặc thống kê thô để tự động hiệu chỉnh hàng tồn; giao diện tiếp tục hiển thị các giới hạn này.

## Kiểm chứng cần có

- Application test: 106 bucket cùng scope -> 100 + 6 không trùng ID; INSERT mới ngoài mốc không lẫn vào trang hai; refresh đọc được ID mới; sai cursor bị chặn trước đọc/ủy quyền; quyền kho bị thu hồi phải chặn lượt sau.
- API test: forwarding, `bucketStatus` và camelCase JSON các trường cursor; invalid enum fail-closed trước quyền kho.
- React/Vitest: chuyển lựa chọn 8 trạng thái mà không trộn bucket, đi tới/lùi trang bucket, giữ đúng mốc Ledger, cảnh báo bucket mới, không hiển thị bằng chứng cũ khi lần tải trang sau trả 404.
- SQL Server integration: sau `InventoryStatusService.ChangeAsync` từ AVAILABLE sang QC_HOLD, xác thực bucket QC_HOLD theo vị trí/lô thực và Quarantine trống; mốc Ledger không đổi. CI bắt buộc .NET SQL/Application, API, frontend lint/Vitest/build và Chromium INV-11; Playwright Preview riêng cho frontend demo. Sau CI cần remote staging API JSON/SQL được cấp quyền để nghiệm thu đúng hệ thống triển khai.

**Trạng thái:** PR #31 giữ Draft. Không thay trạng thái Blueprint thành live, không thay Notion, không merge khi staging/owner acceptance còn thiếu.
