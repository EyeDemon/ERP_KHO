# Browser QA là cổng bắt buộc — ERP/WMS

Ngày cập nhật: 10/10/2026. Phạm vi: PR #31 Draft, branch `part/inventory-control-real-reversal-query-20261008`.

## Bằng chứng Browser Use trên Preview trước khi sửa

**Run ID:** `d98238da-b92e-4a6e-b352-7a36524b31e9`. **Địa chỉ:** `https://erp-wms-blueprint-demo-et6cq17ou-bayuuandree99-8132.vercel.app/inventory-reconciliation`.

- **PASS (6):** mở root/system blueprint; click từ INV-11 về màn hình vận hành; nhập ID `1.5` hiển thị lỗi/aria-invalid và giữ focus; lọc ID `1001`/Kho TP.HCM; xóa lọc; xác minh ba hàng demo với 2 mismatch và tổng lệch tuyệt đối 6.
- **FAIL (2):** ba URL `/api/InventoryReconciliation/warehouses`, `/api/InventoryReconciliation/investigation`, `/api/health` trả `HTTP 200 text/html` (SPA fallback, không phải backend JSON); trên mobile 390px `document.scrollWidth=693` (tràn 303px).
- **BLOCKED (3):** phân trang dữ liệu demo 3 hàng không xuất hiện; nút `Xem bằng chứng` có label `Chỉ hệ thống thật` và disabled nên không thể click kiểm tra Ledger/status; không có đăng nhập thật hay RBAC server-side trong demo.
- **Kết luận:** Browser QA tương tác đã chạy trên frontend demo nhưng **staging backend/system acceptance = BLOCKED**. Không có POST/PUT/DELETE, không sửa tồn kho, không đoán thông tin đăng nhập.

## CI fail-closed cho trình duyệt

Workflow `.github/workflows/ci.yml` tiếp tục từ Test Application (SQL) → Test API → lint/Vitest/frontend build rồi mới thực hiện `Mandatory interactive Chromium browser QA (INV-11)` **cùng job**. Phải khởi động backend .NET, frontend Vite, SQL Server LocalDB QA được đánh dấu ownership; dùng Chromium Playwright `1.62.1` thật và bốn user kiểm thử được bootstrap an toàn. QA dùng dữ liệu từ phiếu nhập được post thực vào kho QA, không mock response.

Case `INV-11 interactive reconciliation and unauthorized route` kiểm:
1. Login UI tiếng Việt trên browser thật, GET danh sách kho có quyền.
2. Route `inventory-reconciliation` + nhập ID phân số `1.5`, focus không mất; nhập ID sản phẩm thật, gửi lọc.
3. Click nút `Xem bằng chứng`, GET API `200 JSON` đúng kho/sản phẩm và `isReadOnly`, render 8 trạng thái tồn.
4. Đóng hồ sơ, focus phục hồi, trang ở viewport 390px không tràn ngang toàn document (bảng tự cuộn trong container).
5. User chỉ có `receipt.read` không được vào route đối chiếu; GET API investigation phải trả `403`. Không có JS `pageerror`.
6. Ghi evidence JSON đã loại credential; `sourceHead` khớp `GITHUB_SHA`; case PASS đúng tên và duy nhất, nếu thiếu/failed thì **CI FAIL**. Cleanup SQL/tiến trình qua script kiểm tra ownership và artifact upload trong `finally/always()`.

Nếu Chromium không được cài, trình duyệt không thao tác được, SQL fixture sai, test không chạy hoặc thiếu evidence: **FAIL/BLOCKED**, không được đánh dấu CI xanh bằng cách bỏ case.

## Điều kiện nghiệm thu môi trường thật

CI Chromium với backend/SQL cô lập chưa đủ chứng nhận Vercel Preview hoặc staging. Trước khi merge PR hoặc công bố `live`, cần ít nhất một backend/API staging **đúng môi trường**, có URL trả JSON (không phải SPA fallback) và SQL staging, user QA đủ role/warehouse, QA browser tương tác ở URL đó:

- Đăng nhập/đăng xuất, quyền 403/404, bảo mật kho A/B, refresh phiên.
- Lọc, reset, phân trang Ledger `eventAnchorId` và keyset, đổi filter khi request chưa hoàn tất, 8 trạng thái, bucket, bản ghi không thể phân loại, đóng/focus.
- Mobile 390px và desktop, keyboard/ARIA, kiểm tra network và JS console, xác nhận không sửa immutable Ledger.
- Evidence run ID, SHA, browser version, timestamp, URL, actors và outcomes PASS/FAIL/BLOCKED, mọi lỗi đã được ghi PR.

**Các status khác nhau:** GitHub CI/Pipeline PASS ≠ Vercel READY ≠ Browser QA remote staging PASS ≠ toàn bộ ERP/WMS hoàn thiện. PR #31 luôn Draft khi còn case FAILED/BLOCKED. Không cập nhật Notion; không merge khi chưa nghiệm thu.
