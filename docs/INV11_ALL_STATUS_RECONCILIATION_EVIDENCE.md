# INV-11 — Đối chiếu trạng thái tồn kho bằng SQL và Ledger (chỉ đọc)

Mốc triển khai: 10/10/2026. Phạm vi PR #31 Draft. Đây là phần mở rộng của API điều tra đã có; **không tạo thao tác sửa số dư tồn kho**.

## Phạm vi và nguồn bằng chứng

- `GET /api/InventoryReconciliation/investigation` giữ tham số warehouseId/productId, eventAnchorId, eventBeforeId, limit và quyền `inventory_ledger.read`. Mỗi lần truy vấn phải vượt qua `EnsureWarehouseAccessAsync(warehouseId)` trước khi đọc chi tiết.
- Mốc Ledger ID được chọn từ **tất cả trạng thái** của cặp kho–sản phẩm, thay vì chỉ `AVAILABLE`. Cờ `ledgerHasEventsAfterAnchor` theo dõi mọi giao dịch sau mốc trong phạm vi cặp. Ledger trang sự kiện `AVAILABLE` vẫn giữ keyset paging như hợp đồng trước; tổng/đếm trang này không đồng nghĩa lịch sử đủ 8 trạng thái.
- `statusBreakdown[]` trả đủ 8 trạng thái (`Available`, `QcHold`, `Quarantine`, `Damaged`, `Rejected`, `Blocked`, `Expired`, `RecallBlocked`) với số bucket, tổng tồn và reserved **hiện tại**, biến động Ledger trực tiếp, lượng chuyển trạng thái vào/ra, số Ledger kỳ vọng và chênh lệch **tham khảo**.
- SQL tổng hợp `InventoryStocks` theo `Status` và `InventoryTransactions` theo `InventoryStatus / TransactionType / FromInventoryStatus / ToInventoryStatus`, lọc chính xác warehouseId/productId và `Id <= anchor`. Chỉ materialize các nhóm theo enum, **không tải toàn bộ Ledger hoặc bucket** lên ứng dụng.
- `StatusChange` hợp lệ được diễn giải **-Quantity** ở `FromInventoryStatus` và **+Quantity** ở `ToInventoryStatus`, luôn bảo toàn tổng toàn kho. `Move` và `Reversal` marker có dấu 0; `Import/Export/Ship/TransferIn/TransferOut/Adjustment` dùng canonical `TransactionType.ApplySign`. Không quy `StatusChange` thành lượng nhập mới.
- `allStatusCurrentQuantity`, `allStatusReservedQuantity`, `allStatusExpectedQuantity`, `allStatusDifference` là tổng của mọi trạng thái. Cặp chỉ có bucket/sự kiện `QcHold` hay `Quarantine` không bị trả 404 chỉ vì `AVAILABLE` rỗng.
- Khi gặp `TransferAdjustment`, loại giao dịch enum lạ, `StatusChange` thiếu nguồn/đích hoặc khai báo sai trạng thái/chiều âm, đặt `unclassifiedLedgerEventCount > 0`; **mọi ExpectedQuantity/Difference theo trạng thái và tổng toàn trạng thái = null**, giao diện hiển thị **Chưa xác định**, không được sử dụng như nguồn để điều chỉnh tồn.
- Số Ledger `AVAILABLE` kế thừa trên màn hình là thống kê loại sự kiện cũ, **không bao gồm điều chuyển trạng thái**; nếu không phân loại được loại có dấu thì cờ `availableLedgerExpectedIsPartial` được bật, chỉ trả phần sự kiện nhận diện được. Giao diện phải cảnh báo số này không đủ để sửa tồn.

## An toàn và giới hạn

- Dữ liệu tồn đang là **hiện tại**; Ledger được **cố định tại anchor**. Hai nguồn không phải snapshot cùng thời điểm; chênh lệch chỉ hỗ trợ điều tra. Status-only breakdown không chứng minh nguồn cấp lô/sê-ri, owner/HU hoặc lịch sử thay đổi từng bucket.
- Việc thiếu `unclassifiedLedgerEventCount` **không phải** bằng chứng rằng lịch sử đầy đủ hoặc đủ điều kiện tái dựng số dư. Dữ liệu lịch sử từ trước khi triển khai đủ Ledger/StatusChange, commit-gap identity, reconciliation của từng location/lot/serial/owner/HU có thể khiến kết quả chưa đáng tin để điều chỉnh.
- Không tạo `POST/PATCH`, không ghi `InventoryStock`, `InventoryTransaction`, `AuditLog`, không đảo chứng từ, không phê duyệt hay tự động rebuild. Để remediation cần quyền riêng, review/approval, snapshot/fingerprint nhất quán, transaction serializable/locking, audit, idempotency, rollback và QA staging.

## Bằng chứng cần PASS

- Application/InMemory: Available 12, chuyển 3 sang QC thì Available 9/QC 3; tổng 12 không đổi; các trạng thái khác 0. Thêm sự kiện `TransferAdjustment` và StatusChange thiếu nguồn → kết quả trạng thái không xác định; anchor cũ không bị pha lẫn. Cặp chỉ có QC vẫn phải truy vấn được. Unsupported type tại Available không gây lỗi 500, bật cờ partial.
- SQL Server: fixture stock 10 với Ledger Import 10; gọi **InventoryStatusService.ChangeAsync** thật chuyển 3 sang QC_HOLD, xác nhận Available 7 + QC 3 = 10 và không double count. Thêm legacy TransferAdjustment ghi lùi ngày, giữ anchor cũ có expected 10, anchor mới có unclassified=1 và expected=null.
- API: serialization `statusBreakdown`/số lượng/chỉ báo camelCase; giao diện tiếng Việt có bảng 8 trạng thái, quan hệ chuyển vào/ra, tổng tồn và cảnh báo không thể kết luận; chạy các bài regression trước đó.
- Chỉ nhận CI PASS khi đúng SHA có backend build, SQL/Application integration, API, frontend lint/Vitest/build đều xanh. Preview Vercel READY là demo, **không phải staging backend SQL/API hoặc browser QA**.
- PR #31 giữ Draft, không merge; Notion chỉ đọc, không nâng System Blueprint thành live.

## Chưa xong

Phân trang bucket theo từng status/location/lot/serial với snapshot nhất quán; owner/HU custody, full historical provenance, controlled remediation và approval, INV-09 native document reversals, Return/Recall và nghiệm thu trình duyệt/staging. INV-11 vẫn `foundation`.
