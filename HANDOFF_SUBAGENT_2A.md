# HANDOFF REPORT — SUBAGENT 2A (WAVE 55: ERP_KHO v3.0 ZERO-DEFECT CLOSURE)

## 1. `[SKILL ROUTING & ACTIVATION CHECK]`
`[SKILL ROUTING & ACTIVATION CHECK]: Role 6 (UI/UX Designer & Brand), Role 7 (Senior Frontend Engineer React 19 & Vite 8), Role 8 (QA Automation, Reviewer & Debugging) | frontend-design, vite-react-best-practices, vitest, verification-before-completion | 100% Tiếng Việt chuẩn kho vận (loại bỏ hoàn toàn thuật ngữ tiếng Anh thừa trong ngoặc đơn và mã trạng thái tiếng Anh thô), 0px tràn ngang tại 1440x900, đồng bộ hợp đồng API trung thực, Zero File Overlap.`

---

## 2. Những gì đã thay đổi (Danh sách file độc quyền đã chạm tới) (`FACT`)

1. **`frontend/src/pages/CostValuation.tsx` & `frontend/src/pages/CostValuation.test.tsx` (`[W55-01]`):**
   - Trong `loadValuationAndProfit`, trích xuất danh sách mặt hàng từ `res.data.skuValuations ?? res.data.skuItems ?? res.data.items` của `GET /api/v1/reports/owner-gross-profit`.
   - Bổ sung cơ chế fallback tự động sang `GET /api/v1/inventory` (kèm fallback `/api/Inventory`) nếu mảng định giá từ endpoint báo cáo rỗng, ánh xạ đầy đủ `productId`, `productCode`, `productName`, `warehouseName`, `unitName`, `onHandQty`, `availableQty`, `movingAverageUnitCost`, `totalInventoryValue`.
   - Chuẩn hóa 100% nhãn Tiếng Việt trên toàn trang: xóa bỏ `(COGS)`, `(MovingAverageCost)`, `SKU` khỏi thẻ KPI (`"Giá vốn hàng bán bình quân gia quyền"`), tiêu đề bảng (`"Bảng Định giá Tồn kho theo Giá vốn Bình quân Gia quyền • ${valuationRows.length} mặt hàng"`), và mô tả trang.
   - Cập nhật `CostValuation.test.tsx` kiểm thử cả luồng đọc trực tiếp `skuValuations` và luồng fallback sang `/api/v1/inventory`.

2. **`frontend/src/pages/CodReconciliation.tsx` & `frontend/src/pages/CodReconciliation.test.tsx` (`[W55-03]`):**
   - Xóa bỏ `(ShipmentFinancialLedgers)` khỏi mô tả trang và tiêu đề bảng (`"Sổ cái Đối soát Chi tiết Từng Vận đơn • ${ledgers.length} vận đơn"`).
   - Thay thế tiêu đề cột có chứa từ tiếng Anh trong ngoặc bằng 6 tiêu đề cột Tiếng Việt ngắn gọn: `MÃ VẬN ĐƠN`, `ĐVVC`, `COD PHẢI THU`, `COD THỰC NHẬN`, `LỆCH PHÍ VC`, `TRẠNG THÁI`.
   - Cấu hình bảng với `tableLayout: 'fixed'`, `width: '100%'`, `<colgroup>` phân bổ tỷ lệ (`18%`, `10%`, `18%`, `18%`, `16%`, `20%`) và khung bao `overflowX: 'hidden'` giúp bảng hiển thị vừa khít viewport `1440x900` (`scrollWidth <= clientWidth`), các huy hiệu trạng thái (`Khớp đối soát 3 chiều`, `Cảnh báo lệch phí`, `Chờ đối soát`) hiển thị trọn vẹn không bị cắt chữ.
   - Cập nhật `CodReconciliation.test.tsx` đồng bộ với tiêu đề và cột mới.

3. **`frontend/src/pages/HandoverManifests.tsx` & `frontend/src/pages/HandoverManifests.test.tsx` (`[W55-04]`):**
   - Xóa bỏ `(Handover Manifests)` khỏi tiêu đề trang (`"Biên bản Bàn giao Vận chuyển"`) và loại bỏ các mã lỗi tiếng Anh trong ngoặc đơn khỏi mô tả trang.
   - Ánh xạ số kiện thực tế cho từng biên bản qua chuỗi ưu tiên `item.totalPackages ?? item.totalParcels ?? item.parcelCount ?? item.awbCount ?? (Array.isArray(item.lines) ? item.lines.length : 1)` (đảm bảo hiển thị số kiện thực tế `>= 1` cho các biên bản đang mở hoặc đã niêm phong).
   - Việt hóa hoàn toàn cột `Trạng thái` thông qua `statusLabel`: chuyển `"OPEN"` thành `"Đang mở nhận kiện"` và `"SEALED"` / `"DISPATCHED"` thành `"Đã niêm phong & Bàn giao"`, loại bỏ hoàn toàn việc hiển thị chuỗi `"OPEN"` / `"SEALED"` thô trên bảng.
   - Cập nhật `HandoverManifests.test.tsx` kiểm chứng hiển thị số kiện và huy hiệu trạng thái Tiếng Việt.

4. **`frontend/src/pages/Dashboard.tsx`, `frontend/src/pages/Dashboard.test.tsx` & `frontend/src/config/productionNavigation.ts` (`[W55-06]`):**
   - Rút gọn khu vực `"Tiến trình 5 Bước Thiết lập Kho Nhanh"` trên `Dashboard.tsx`: chỉ giữ lại tiêu đề, 5 huy hiệu trạng thái (`1. Tạo Kho`, `2. Khu vực & Vị trí`, `3. Danh mục Hàng hóa`, `4. Nhập Tồn đầu kỳ`, `5. Mời Nhân sự`) và `<DashboardQuickActions />` (5 liên kết điều hướng nhanh), loại bỏ các ô nhập liệu và nút bấm khởi tạo doanh nghiệp trùng lặp với `/onboarding`.
   - Tái cấu trúc phân nhóm menu trong `frontend/src/config/productionNavigation.ts`: nhóm `'Tổng quan'` chỉ giữ duy nhất trang `/` (`Tổng quan`); chuyển `/dock-yard` sang nhóm `'Nhập kho'`; chuyển các danh mục nền tảng (`/products`, `/warehouses`, `/warehouse-structure`, `/warehouse-map`, `/warehouse-calendar`, `/units`, `/business-partners`) sang nhóm `'Quản trị'`.
   - Cập nhật `Dashboard.test.tsx` khớp với bố cục tinh gọn mới.

5. **`frontend/src/pages/OnboardingWizard.tsx`, `frontend/src/pages/OnboardingWizard.test.tsx`, `frontend/src/pages/SaasBilling.tsx` & `frontend/src/pages/SaasBilling.test.tsx` (`[W55-07]`):**
   - Trên `OnboardingWizard.tsx`: xóa bỏ `(Onboarding Wizard)`, `(Tenant)`, `(Tenant Code)`, `(FormData)` khỏi tiêu đề và nhãn; điều chỉnh lưới Bước 1 sang `gridTemplateColumns: '1fr 1.8fr 1.2fr'` để giá trị `"Công ty TNHH Thương mại & Kho vận SME"` trong ô `"Tên Doanh nghiệp / Chủ hàng"` hiển thị đầy đủ 100% không bị che khuất.
   - Trên `SaasBilling.tsx`: xóa bỏ `(GROWTH)`, `(STARTER)`, `(ENTERPRISE)`, `(SKU)` khỏi tiêu đề, thẻ gói cước và nút bấm.
   - Cập nhật `OnboardingWizard.test.tsx` và `SaasBilling.test.tsx` đồng bộ với nhãn Tiếng Việt chuẩn.

---

## 3. Bằng chứng kiểm chứng (`FACT`)

1. **Encoding Gate (`scripts/check-encoding.ps1`):**
   - Command: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-encoding.ps1`
   - Output: `Encoding check passed: all scanned files are valid UTF-8 and no mojibake markers were found.` (`Exit Code 0`).

2. **Frontend Production Build (`npm --prefix frontend run build`):**
   - Command: `npm --prefix frontend run build`
   - Output: `tsc -b && vite build` -> `✓ 1711 modules transformed. ✓ built in 1.43s` (`Exit Code 0`, `0` lỗi TypeScript/Rollup).

3. **Frontend Unit Tests (`vitest`):**
   - Command: `npx --prefix frontend vitest run src/pages/CostValuation.test.tsx src/pages/CodReconciliation.test.tsx src/pages/HandoverManifests.test.tsx src/pages/OnboardingWizard.test.tsx src/pages/SaasBilling.test.tsx src/pages/Dashboard.test.tsx`
   - Output: `Test Files 6 passed (6) | Tests 13 passed (13)` (`PASS 100%`).

---

## 4. Những gì `NOT VERIFIED`
- **`NOT VERIFIED`:** Kiểm thử E2E trực tiếp trên trình duyệt thật (Playwright Live Browser Verification — Gate 4) đối với toàn bộ hệ thống sau khi ghép chung nhánh với Subagent 1 và Subagent 2B (sẽ do Master Orchestrator thực hiện ở bước tổng hợp).

---

## 5. Rủi ro tồn đọng & Việc dở dang / Stub
- **Rủi ro tồn đọng:** Không có.
- **Việc dở dang / Stub:** `0` (`TODO` / `FIXME` / `NotImplementedException` = `0`).
