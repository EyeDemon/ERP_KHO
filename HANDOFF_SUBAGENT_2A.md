# HANDOFF REPORT — SUBAGENT 2A (WAVE 54 — TÁCH GOD COMPONENTS, 5 TRANG CHUYÊN BIỆT MỚI, CÂY MENU 6 NHÓM & `EXPRESS vs FULL WMS MODE` — `[A9, B7, B8, B9, B12, Sprint 1.2..1.5, Sprint 2.10..2.12]`)

## 1. Tóm tắt Kết quả Thực thi (`FACT`)
- **Trạng thái:** `COMPLETED` (100% PASS)
- **Kiểm tra Encoding (`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-encoding.ps1`):** `PASS` (`Exit Code 0` — tất cả file đều đạt chuẩn UTF-8 không BOM, không có mojibake / `U+FFFD`).
- **Kiểm tra Build (`npm --prefix frontend run build`):** `PASS` (`Exit Code 0` — `tsc -b && vite build`, 1711 modules transformed, 5 chunk `React.lazy` mới được tách riêng thành công).
- **Tuân thủ `Zero File Overlap`:** Chỉ tạo mới và chỉnh sửa các file nằm trong danh sách độc quyền của `Subagent 2A`, hoàn toàn không đụng chạm tới các file thuộc phạm vi của `Subagent 2B`.

---

## 2. Chi tiết Từng Hạng mục & Tọa độ `File:Line` Chính xác

### 2.1. `[A9, B8, B9]` Xóa `erpWmsMockData`, Sửa Thông báo Động COD & Tách 4 Sub-Components của `Dashboard.tsx`
- **`frontend/src/pages/Dashboard.tsx` (`L1-L760`):**
  - **`[B8]` (`L1-L16`):** Đã xóa hoàn toàn dòng `import { mockInventoryBalances, mockProducts, mockWarehouses, mockWorkCenters } from '../mocks/erpWmsMockData'` và `demoReconciliationRows`. Khởi tạo state bằng `0` / `[]` trung thực.
  - **`[A9]` (`L594-L625`):** Thay thế thông báo cứng bằng thông báo động dựa trên dữ liệu trả về thực tế từ API đối soát:
    `` `Đã đối soát ${matchedCount}/${totalCount} vận đơn (${matchRatePct}%), phát hiện ${discrepancyCount} cảnh báo lệch phí/chậm hoàn.` ``
  - **`[B9]` (`L439-L448`, `L455`, `L716-L740`):** Nhúng 4 sub-components đồng thời bảo toàn 100% các nút chức năng và `data-testid` hiện hữu:
    1. `frontend/src/components/dashboard/DashboardMetrics.tsx` (`L1-L87`): Hiển thị 4 thẻ KPI vận hành kho & 4 thẻ KPI Tài chính Chủ Shop (`data-testid="dashboard-owner-finance-metrics"`).
    2. `frontend/src/components/dashboard/DashboardQuickActions.tsx` (`L1-L39`): Điều hướng nhanh tới `/cod-reconciliation`, `/onboarding-wizard`, `/handover-manifests`, `/cost-valuation`, `/saas-billing`.
    3. `frontend/src/components/dashboard/DashboardExceptionAlerts.tsx` (`L1-L154`): Hàng đợi tác vụ kho đang mở & Trung tâm ngoại lệ vận hành.
    4. `frontend/src/components/dashboard/DashboardRecentActivity.tsx` (`L1-L78`): Nhật ký hoạt động vận hành gần đây.

### 2.2. `[Sprint 1.2, 1.3, 1.4]` Tạo 3 Trang Tách từ God Components
1. **`frontend/src/pages/SaasBilling.tsx` (`L1-L276`, route `/saas-billing`):**
   - Giao diện thuần Việt quản lý gói cước Thuê bao SaaS (`STARTER / GROWTH / ENTERPRISE`), thanh đo hạn mức 3 nấc (`Bình thường (<90%) / Cảnh báo 90% / Cho phép vượt 115% / Khóa chỉ đọc (>115%)`), đồng bộ số liệu thật từ `/api/v1/permissions/saas/subscription-billing` (fallback `/api/v1/saas-billing/subscription`), tạo hóa đơn thanh toán `VietQR` kèm `<BarcodeSvg />` và xác nhận thanh toán tự động qua Webhook PayOS / SePay.
   - Trên `frontend/src/pages/Permissions.tsx` (`L675-L700`), bổ sung banner điều hướng nhanh sang `/saas-billing` (`data-testid="permissions-saas-billing-banner"`, `data-testid="link-open-saas-billing"`) và giữ nguyên toàn bộ phần tử/test-id hiện có.
2. **`frontend/src/pages/CodReconciliation.tsx` (`L1-L243`, route `/cod-reconciliation`):**
   - Trang chuyên dụng cho Kế toán Kho đối soát Tiền thu hộ COD & Phí vận chuyển 3 chiều (`ShipmentFinancialLedgers`): tải dữ liệu từ `GET /api/v1/reports/cod-shipping-reconciliation`, upload bảng kê `.xlsx/.csv` của ĐVVC qua `FormData` lên `POST /api/v1/reports/cod-shipping-reconciliation`, hiển thị bảng chi tiết từng vận đơn (`TrackingNumber`, `ExpectedCod`, `ActualCod`, `ShippingFeeVariance`, `Status`) và thông báo động chính xác từ server.
3. **`frontend/src/pages/OnboardingWizard.tsx` (`L1-L246`, route `/onboarding-wizard`):**
   - Trang chuyên dụng Khởi tạo Thuê bao SaaS Tự phục vụ & Thiết lập Kho Nhanh 5 Bước trong 15 Phút (`/api/v1/permissions/saas/tenants/provision`, `/api/v1/permissions/onboarding-wizard/status`, `/api/v1/reports/onboarding-wizard/quick-start`) kèm tải biểu mẫu & upload file `.xlsx` Nhập Tồn Kho Đầu Kỳ qua `FormData` (`POST /api/v1/data-imports/opening-balance`).

### 2.3. `[Sprint 2.10, 2.11]` Tạo 2 Trang Chuyên biệt Mới (`HandoverManifests.tsx` & `CostValuation.tsx`)
1. **`frontend/src/pages/HandoverManifests.tsx` (`L1-L287`, route `/handover-manifests`):**
   - Hiển thị danh sách biên bản bàn giao từ `GET /api/v1/handover-manifests`, ô quét mã vận đơn AWB (`POST /api/v1/handover-manifests/scan-awb`), ô nhập bắt buộc **Biển số xe** (`vehiclePlate` khởi tạo `''`) và **Tên tài xế / Bưu tá** (`driverName` khởi tạo `''`), nút **"Niêm phong & Xuất kho Bàn giao Bưu tá"** (`POST /api/v1/handover-manifests/confirm-handover`), và cảnh báo đỏ tức thì khi phát hiện đơn hủy giữa chừng (`409 ORDER_CANCELLED_PUTBACK_REQUIRED`) hoặc quét trùng AWB (`409 DUPLICATE_AWB_SCAN`).
2. **`frontend/src/pages/CostValuation.tsx` (`L1-L234`, route `/cost-valuation` & `/misa-exports`):**
   - Hiển thị bảng định giá tồn kho theo Giá vốn Bình quân Gia quyền (`MovingAverageCost`), báo cáo lãi gộp chủ shop (`/api/v1/reports/owner-gross-profit`), kiểm tra ẩn giá vốn theo quyền `inventory.cost.read`, nút kết xuất bảng kê `.xlsx` chuẩn MISA AMIS (`/api/v1/export-receipts/misa-excel-export?format=xlsx`) và phát hành Hóa đơn điện tử nháp NĐ 70/2025 (`/api/v1/export-receipts/einvoice-nd70-draft`).

### 2.4. `[Sprint 2.12, B7, B12]` Cập nhật Cây Menu 6 Nhóm (`productionNavigation.ts`), Công tắc `Express vs Full WMS Mode` (`MainLayout.tsx`) & Phân quyền / Code-Splitting (`AppRoutes.tsx`)
- **`frontend/src/config/productionNavigation.ts` (`L1-L68`):**
  - Tổ chức điều hướng theo **6 nhóm nghiệp vụ chuẩn `Spec 216`** (`Tổng quan`, `Nhập kho`, `Xuất kho`, `Tồn kho`, `Tài chính`, `Quản trị`), giữ nguyên toàn bộ đường dẫn cũ và bổ sung `/handover-manifests`, `/cod-reconciliation`, `/cost-valuation`, `/saas-billing`, `/onboarding-wizard`.
- **`frontend/src/layouts/MainLayout.tsx` (`L48-L88`, `L99-L115`, `L162-L171`, `L242-L260`):**
  - Bổ sung công tắc chuyển đổi chế độ **`⚡ Chế độ Rút gọn (Express)` $\leftrightarrow$ `🏭 Chế độ Đầy đủ (Full WMS)`** (`data-testid="wms-complexity-mode-toggle"`) trên thanh Header, lưu vào `localStorage` (`wmsComplexityMode`, mặc định `'FULL'`).
- **`frontend/src/routes/AppRoutes.tsx` (`L42-L46`, `L78-L133`):**
  - **`[B7]`:** Bọc `<PermissionRoute permission="inventory.read">` cho `/stock-reservations`, `/stock-transfers`, `/handover-manifests`, `/cost-valuation`, `/misa-exports`, `/cod-reconciliation` và `<PermissionRoute permission="permission.read">` cho `/saas-billing`, `/onboarding-wizard`.
  - **`[B12]`:** Sử dụng `React.lazy(() => import(...))` kết hợp `<Suspense fallback={<div className="p-6 text-slate-600">Đang tải phân hệ...</div>}>` cho 5 trang chuyên biệt mới.
