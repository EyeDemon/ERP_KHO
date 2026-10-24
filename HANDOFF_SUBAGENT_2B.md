# HANDOFF REPORT — SUBAGENT 2B (`Wave 54`: `[B10, Sprint 2.7..2.9, Sprint 3.13..3.19]` & `GATE 3 VERIFICATION`)

- **Subagent ID:** `dbb94975-dff3-4f68-bc21-878ac3883b68`
- **Parent Conversation ID:** `16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb`
- **Role:** `Wave 54 Frontend Operations & UX Consolidation Implementer + Gate 3 Auditor (Subagent 2B)`
- **Status:** `DONE` (`100% PASS`)

---

## 1. Đã thay đổi gì (Danh sách File độc quyền & Tọa độ `File:Line`) — `[FACT]`

Tuân thủ tuyệt đối nguyên tắc **`Zero File Overlap`**, Subagent 2B chỉ chỉnh sửa 13 file nằm trong phạm vi độc quyền được giao (tuyệt đối không chạm vào `Dashboard.tsx`, `Permissions.tsx`, `MainLayout.tsx`, `AppRoutes.tsx`, `productionNavigation.ts` của Subagent 2A):

1. **[StockReservations.tsx](file:///d:/ERP_KHO/frontend/src/pages/StockReservations.tsx#L1-L265) & [StockAllocations.tsx](file:///d:/ERP_KHO/frontend/src/pages/StockAllocations.tsx#L1-L285) (`[Sprint 2.7]`):**
   - Gắn thanh chuyển đổi 2 Tab hợp nhất (`data-testid="reservation-allocation-tabs"`, hỗ trợ cả `<Link>` trong Router và `<a>` khi test độc lập qua `useInRouterContext()`):
     - `Tab 1: Giữ hàng tồn kho (/stock-reservations)` (`data-testid="tab-stock-reservations"`)
     - `Tab 2: Phân bổ vị trí kệ (/stock-allocations)` (`data-testid="tab-stock-allocations"`)
2. **[InventoryReconciliation.tsx](file:///d:/ERP_KHO/frontend/src/pages/InventoryReconciliation.tsx#L1-L725) & [InventoryTraceability.tsx](file:///d:/ERP_KHO/frontend/src/pages/InventoryTraceability.tsx#L1-L215) (`[Sprint 2.8]`):**
   - Gắn thanh chuyển đổi 2 Tab hợp nhất (`data-testid="reconciliation-traceability-tabs"`):
     - `Tab 1: Đối chiếu Tồn kho & Hàng đợi Outbox (/inventory-reconciliation)` (`data-testid="tab-inventory-reconciliation"`)
     - `Tab 2: Truy vết & Phả hệ Lô / Sê-ri (/inventory-traceability)` (`data-testid="tab-inventory-traceability"`)
3. **[Warehouses.tsx](file:///d:/ERP_KHO/frontend/src/pages/Warehouses.tsx#L1-L215), [WarehouseLocations.tsx](file:///d:/ERP_KHO/frontend/src/pages/WarehouseLocations.tsx#L1-L415) & [WarehouseMap.tsx](file:///d:/ERP_KHO/frontend/src/pages/WarehouseMap.tsx#L1-L220) (`[Sprint 2.9]`):**
   - Gắn thanh chuyển đổi 3 Tab hợp nhất (`data-testid="warehouse-topology-tabs"`):
     - `Tab 1: Danh mục Kho hàng (/warehouses)` (`data-testid="tab-warehouses"`)
     - `Tab 2: Cấu trúc & Mã vạch Vị trí Kệ (/warehouse-locations)` (`data-testid="tab-warehouse-locations"`)
     - `Tab 3: Bản đồ Nhiệt Kho 2D (/warehouse-map)` (`data-testid="tab-warehouse-map"`)
4. **[Shipments.tsx](file:///d:/ERP_KHO/frontend/src/pages/Shipments.tsx#L33-L42) & [Shipments.tsx](file:///d:/ERP_KHO/frontend/src/pages/Shipments.tsx#L335-L382) (`[B10]`):**
   - Việt hóa 100% các chuỗi lai Anh–Việt trên màn hình Lô hàng Vận chuyển:
     - `'Đang staging'` $\rightarrow$ `'Đang tập kết ra cửa xuất'` (`L35`)
     - `'Đang loading'` $\rightarrow$ `'Đang xếp hàng lên xe'` (`L35`)
     - `'Đã load'` $\rightarrow$ `'Đã xếp lên xe'` (`L35`)
     - `'Đã staging'` $\rightarrow$ `'Đã tập kết ra cửa xuất'` (`L40`)
     - `'Đã ship'` $\rightarrow$ `'Đã xuất giao'` (`L40`)
     - `'Bắt đầu Loading'` $\rightarrow$ `'Bắt đầu xếp hàng lên xe'` (`L338`)
     - `'Xác nhận LOADED'` $\rightarrow$ `'Xác nhận đã xếp lên xe'` (`L351`, `L378`)
5. **[PackingSessions.tsx](file:///d:/ERP_KHO/frontend/src/pages/PackingSessions.tsx#L102-L166) & [PackingSessions.tsx](file:///d:/ERP_KHO/frontend/src/pages/PackingSessions.tsx#L363-L505) (`[Sprint 3.13 & 3.19]`):**
   - Bổ sung khối **"⚡ Trạm Đóng Gói Hợp Nhất 1 Màn Hình (Quét Đơn • Gợi Ý Thùng • Ghi Hình SHA-256 • In Tem Nhiệt AWB)"** (`data-testid="unified-packing-station-card"`):
     - **Bước 1:** Ô quét nhanh Mã Đơn / Mã Phiếu Nhặt (`data-testid="unified-packing-scan-input"`) kiểm tra trạng thái hủy đơn giữa chừng (`CancelPendingPutback` / `Cancelled`) và hiển thị cảnh báo đỏ chặn đóng gói (`data-testid="unified-packing-cancelled-warning"`) nếu đơn bị hủy giữa chừng.
     - **Bước 2:** Gợi ý thùng carton tự động theo thể tích/trọng lượng (`data-testid="unified-carton-suggestion"`: `Thùng tiêu chuẩn S (20×15×10 cm)`, `Thùng trung M (30×20×15 cm)`, `Thùng lớn L (50×40×30 cm)`).
     - **Bước 3:** Tích hợp trực tiếp trạm ghi hình bằng chứng đóng gói (`SHA-256`) kèm mốc thời gian quét (`Watermark`) và nút ghi nhận nhanh (`data-testid="unified-packing-record-video-btn"`).
     - **Bước 4:** Khung xem trước Tem Nhiệt AWB chuẩn `100×150mm` (`<BarcodeSvg />` 1D Code-128 & 2D QR, sắp xếp theo kệ nhặt `A → Z`) kèm nút **"In ngay Tem Nhiệt AWB (100×150mm)"** (`data-testid="unified-packing-print-awb-btn"`) gọi `POST /api/v1/export-receipts/batch-print-awb`.
6. **[PickingTasks.tsx](file:///d:/ERP_KHO/frontend/src/pages/PickingTasks.tsx#L80-L116) & [PickingTasks.tsx](file:///d:/ERP_KHO/frontend/src/pages/PickingTasks.tsx#L474-L555) (`[Sprint 3.14 & 3.16]`):**
   - Hiển thị nhãn lộ trình tối ưu hình chữ S: **`"🧭 Lộ trình nhặt hàng tối ưu hình chữ S (Dãy → Kệ → Tầng → Ô: A → Z)"`** (`data-testid="s-shape-pick-route-banner"`) và sắp xếp các dòng lấy hàng theo `sourceLocationCode` tăng dần (`A → Z`).
   - Bổ sung nút **"Gợi ý Kệ Thay thế (FEFO)"** (`data-testid="suggest-alternate-bin-btn"`) gọi `GET /api/v1/picking-tasks/${taskId}/alternate-bins` và hiển thị danh sách kệ thay thế có tồn khả dụng theo chuẩn FEFO kèm nút 1-Click **"Chọn kệ thay thế"** tự động điền mã kệ vào ô quét.
7. **[MobileBarcodeScannerBar.tsx](file:///d:/ERP_KHO/frontend/src/components/MobileBarcodeScannerBar.tsx#L101-L155) & [MobileBarcodeScannerBar.tsx](file:///d:/ERP_KHO/frontend/src/components/MobileBarcodeScannerBar.tsx#L274-L296) (`[Sprint 3.17]`):**
   - Bổ sung nút bật/tắt **"Tự động chuyển bước sau khi quét (Auto-advance)"** (`data-testid="scanner-auto-advance-toggle"`, mặc định `true`) tự động chuyển `wizardStep` (`1 → 2 → 3`) và trả `focus` về ô nhập mã vạch ngay sau khi nhấn Enter hoặc quét xong.
8. **[PutawayTasks.tsx](file:///d:/ERP_KHO/frontend/src/pages/PutawayTasks.tsx#L56-L96) & [PutawayTasks.tsx](file:///d:/ERP_KHO/frontend/src/pages/PutawayTasks.tsx#L360-L393) (`[Sprint 3.15]`):**
   - Bổ sung khối **"Gợi ý Vị trí Kệ Cất hàng Thông minh"** (`data-testid="directed-putaway-suggestion"`) hiển thị kệ đề xuất (`BIN-A01-01`), lý do đề xuất (`"Cùng nhóm bảo quản Nhiệt độ thường • Ưu tiên FEFO • Còn 85% sức chứa"`) và nút 1-Click **"Áp dụng Kệ Gợi ý"** (`data-testid="apply-directed-putaway-btn"`) gọi `GET /api/v1/putaway-tasks/${taskId}/directed-suggestion` và tự động chọn vị trí đích.
9. **[ImportReceipts.tsx](file:///d:/ERP_KHO/frontend/src/pages/ImportReceipts.tsx#L150-L240), [ImportReceipts.tsx](file:///d:/ERP_KHO/frontend/src/pages/ImportReceipts.tsx#L618-L637) & [ImportReceipts.tsx](file:///d:/ERP_KHO/frontend/src/pages/ImportReceipts.tsx#L1044-L1110) (`[Top 8 & Top 13]`):**
   - Bổ sung bảng **"Mô phỏng & Xem trước Giá vốn Bình quân Gia quyền (MAC) trước khi Ghi sổ"** (`data-testid="import-mac-preview-panel"`) kèm nút **"Xem trước Giá vốn Bình quân (MAC) trước khi Ghi sổ"** (`data-testid="preview-mac-before-post-btn"`) gọi `GET /api/v1/import-receipts/${selectedId}/mac-preview`, bảo toàn cơ chế che giá vốn (`Ẩn theo phân quyền`) khi user không có quyền `inventory.cost.read`.
   - Bổ sung đồng hồ đếm ngược **"Hạn phản hồi khiếu nại sàn"** (`data-testid="return-dispute-deadline-banner"`, tính theo `createdAt + 48h`, ví dụ: `"Còn 28 giờ phản hồi Shopee/TikTok (Hạn 48h từ lúc nhận hoàn)"` hoặc `"Đã hoàn tất trước hạn"`).

---

## 2. Bằng chứng Kiểm chứng (`Gate 1 & Gate 3 Full Unfiltered Suite`) — `[FACT]`

1. **Kiểm tra Encoding (`scripts/check-encoding.ps1`):**
   - Lệnh: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-encoding.ps1`
   - Kết quả: `Exit Code 0` — `"Encoding check passed: all scanned files are valid UTF-8 and no mojibake markers were found."`
2. **Gate 3.1 — Kiểm tra Biên dịch Production Frontend (`npm --prefix frontend run build`):**
   - Lệnh: `npm --prefix frontend run build` (`tsc -b && vite build`)
   - Kết quả: `Exit Code 0` (`✓ built in 778ms`, `✓ 1711 modules transformed`, `0` lỗi TypeScript, `0` lỗi Vite bundle).
   - Xác nhận tách các lazy chunks độc lập:
     - `dist/assets/CostValuation-Dfd64YIB.js` (`7.57 kB | gzip: 3.07 kB`)
     - `dist/assets/CodReconciliation-D2CJlLU6.js` (`8.14 kB | gzip: 3.11 kB`)
     - `dist/assets/OnboardingWizard-Bf-kEWmB.js` (`8.25 kB | gzip: 3.11 kB`)
     - `dist/assets/HandoverManifests-C4jzC476.js` (`9.23 kB | gzip: 3.12 kB`)
     - `dist/assets/SaasBilling-DMTfgCF7.js` (`10.26 kB | gzip: 3.68 kB`)
3. **Gate 3.2 — Kiểm thử Toàn bộ Bộ Unit Test Vitest Không Lọc (`npm --prefix frontend test -- --run`):**
   - Lệnh: `npm --prefix frontend test -- --run` (`vitest run --no-file-parallelism --exclude=e2e/** --run`, tuyệt đối không dùng `--filter`)
   - Kết quả: `Exit Code 0` — **`Test Files 70 passed (70)`**, **`Tests 390 passed (390)`** (`100% PASS`, `Duration 171.99s`).

---

## 3. Những gì `NOT VERIFIED` — `[NOT VERIFIED]`
- Kiểm thử Live Browser E2E tích hợp toàn hệ thống (`Gate 4`) sẽ do Master Orchestrator điều phối.

## 4. Rủi ro tồn đọng — `[INFERENCE]`
- Không có rủi ro xung đột file (`Zero File Overlap` được giữ 100%). Toàn bộ `70/70` test files (`390/390` tests) trên Frontend đã vượt qua 100%.

## 5. Việc dở dang / Stub — `[FACT]`
- `0` việc dở dang, `0` `TODO`/`FIXME`, `0` stub giả.
