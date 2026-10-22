# HANDOFF REPORT — SUBAGENT 2A (React 19 Frontend UI & FormData Engineer `[P0-02]`) — WAVE 53 ĐỢT 1

## 1. Tóm tắt hoàn thành nhiệm vụ
- **Trạng thái:** `COMPLETED` (100% PASS)
- **Kiểm tra Build (`npm --prefix frontend run build`):** `PASS` (`tsc -b && vite build` — 0 lỗi TypeScript, 1702 modules transformed)
- **Kiểm tra Encoding (`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-encoding.ps1`):** `PASS` (Exit Code `0`, UTF-8 chuẩn, không có mojibake/U+FFFD)
- **Tuân thủ phạm vi:** Chỉ chỉnh sửa 11 file `.ts`/`.tsx` được cấp quyền, không chạm bất kỳ file `.cs` hay `.test.tsx` nào.

---

## 2. Danh sách 11 file đã sửa & Tọa độ `File:Line` chính xác

### 2.1. `frontend/src/services/apiClient.ts` (`L168-L179`)
- Trong `apiClient.interceptors.response.use` (nhánh lỗi):
  - Trích xuất `lastErrorCode` từ `errData.code ?? errData.errorCode ?? error.response?.status ?? 'ERR_NETWORK'`.
  - Trích xuất `lastCorrelationId` từ `error.response?.headers?.['x-correlation-id'] ?? errData.correlationId ?? errData.traceId ?? ''`.
  - Lưu cả `lastErrorCode` và `lastCorrelationId` vào `window.localStorage` để thanh chẩn đoán Zalo 1-Click đọc trực tiếp.

### 2.2. `frontend/src/layouts/MainLayout.tsx` (`L236-L245`)
- Trong nút `"Sao chép Chẩn đoán Lỗi 1-Click gửi Zalo"`:
  - Đọc `localStorage.getItem('lastErrorCode') || 'NONE'` và `localStorage.getItem('lastCorrelationId') || 'N/A'`.
  - Đưa `lastErrorCode` và `lastCorrelationId` vào chuỗi JSON sao chép clipboard và chuỗi thông báo `diagCopiedNotice`.

### 2.3. `frontend/src/pages/Dashboard.tsx` (`L245-L254`, `L309-L355`, `L504-L560`)
- **Xóa số giả khởi tạo (`L245-L254`):** Đặt toàn bộ `ownerFinance` khởi tạo bằng `0` (`netRevenue: 0`, `cogsAmount: 0`, `grossProfit: 0`, `grossMarginPct: 0`, `costHiddenByRbac: false`, `pendingCodAmount: 0`, `receivedCodAmount: 0`, `codWarningCount: 0`), loại bỏ hoàn toàn các số giả `142500000`, `84900000`, `57600000`, `40.42`, `38200000`, `114500000`.
- **Ánh xạ JSON thực tế (`L309-L355`, `L504-L560`):**
  - Gọi `/api/v1/reports/owner-gross-profit` và ánh xạ `grossRevenue ?? netRevenue`, `movingAverageCogs ?? totalCogs`, `netPocketProfit ?? estimatedGrossProfit`, `netMarginPct ?? grossMarginPercent`, đồng thời kiểm tra `costHiddenByRbac === true` để hiển thị trạng thái ẩn giá vốn/lợi nhuận theo phân quyền RBAC.
  - Gọi `/api/v1/reports/cod-shipping-reconciliation` (kèm fallback `/api/v1/reports/cod-reconciliation`) và ánh xạ `totalExpectedCod ?? pendingCodAmount`, `totalActualCod ?? receivedFromCarrierAmount`, `discrepancyCount ?? feeDiscrepancyWarnings ?? discrepancies.length`.
  - Chuẩn hóa URL `/api/v1/permissions/saas/tenants/provision` và `/api/v1/reports/onboarding-wizard/quick-start`.

### 2.4. `frontend/src/pages/ExportReceipts.tsx` (`L161-L170`, `L206-L224`, `L566-L607`, `L704-L741`)
- **Xóa mã vận đơn giả (`L161-L170`, `L704-L741`):** Đặt `handoverTrackingInput` khởi tạo bằng `''`, `awbLabels` khởi tạo bằng `[]`, loại bỏ hoàn toàn `'GHN84920188'` và `'VTP99201122'`.
- **In Hàng Loạt Nhãn Vận Đơn (`L206-L224`):** Gọi `POST /api/v1/export-receipts/batch-print-awb`, đọc danh sách từ `res?.data?.labels ?? res?.data?.items ?? res?.data` và ánh xạ `trackingNumber -> awbCode`, `carrierCode -> carrierName`, `pickingSummaryFooter -> pickSequenceFooter`, `isReprintWatermark / printCount > 1 -> isReprint`, `watermarkText`.

### 2.5. `frontend/src/pages/Products.tsx` (`L180-L295`)
- **Bundle Components (`L180-L236`):** Chuẩn hóa URL `GET` và `POST` sang `/api/v1/products/${parentId}/bundle-components`, đọc danh sách từ `res?.data?.items ?? res?.data?.components ?? res?.data`.
- **Promotion Gift Rules (`L238-L295`):** Chuẩn hóa URL `GET` và `POST` sang `/api/v1/sales-orders/promotion-gift-rules`, đọc danh sách từ `res?.data?.items ?? res?.data?.rules ?? res?.data`.

### 2.6. `frontend/src/pages/Inventory.tsx` (`L560-L638`)
- Thêm state `selectedOpeningFile` (`File | null`) tại `L560` và cập nhật trong `onChange` của `<input type="file" />` (`L621-L630`).
- Trong `handleOpeningBalanceImport` (`L585-L613`): Tạo `new FormData()`, đính kèm `formData.append('file', selectedOpeningFile)`, `warehouseId`, `fileName` và gọi `POST /api/v1/inventory/opening-balances/import` với header `multipart/form-data`.

### 2.7. `frontend/src/pages/OutboundDemand.tsx` (`L275-L332`, `L414-L423`)
- Thêm state `selectedOrderExcelFile` (`File | null`) tại `L275` và cập nhật trong `onChange` của `<input type="file" />` (`L414-L423`).
- Trong `submitMarketplaceExcelImport` (`L302-L332`): Tạo `new FormData()`, đính kèm `formData.append('file', selectedOrderExcelFile)`, `channel`, `warehouseId`, `fileName` và gọi `POST /api/v1/sales-orders/import-marketplace-excel` với header `multipart/form-data`.

### 2.8. `frontend/src/pages/Permissions.tsx` (`L178-L287`, `L374-L405`, `L666-L695`)
- **Support Impersonation (`L192-L273`):** Chuẩn hóa 3 URL sang `GET /api/v1/security/support-impersonation/sessions`, `POST /api/v1/security/support-impersonation/sessions`, và `POST /api/v1/security/support-impersonation/${sessionId}/revoke`.
- **SaaS Subscription Billing (`L179-L190`, `L374-L405`, `L666-L695`):**
  - Thêm state `billingSummary` khởi tạo bằng `0`.
  - Gọi `GET /api/v1/permissions/saas/subscription-billing` trong `load()` và ánh xạ `activeWarehouses`, `maxWarehouses`, `activeUsers`, `maxUsers`, `activeProducts`, `maxProducts`, `currentMonthOrderCount`, `monthlyOrderQuota`.
  - Thay thế hoàn toàn các chuỗi số giả `2 / 20 kho`, `4 / 100 người dùng`, `6 / 10.000 mặt hàng` bằng dữ liệu thực từ `billingSummary`.

### 2.9. `frontend/src/pages/PackingSessions.tsx` (`L95-L102`, `L199-L277`, `L607-L635`)
- Thêm state `selectedVideoFile` (`File | null`) tại `L95` và `<input type="file" accept="video/*" />` tại `L611-L624`.
- Chuẩn hóa URL sang `POST /api/v1/packing-sessions/video-evidence` (hỗ trợ gửi `FormData` kèm `videoFile` khi có tệp chọn) và `GET /api/v1/packing-sessions/video-evidence`.

### 2.10. `frontend/src/pages/WarehouseLocations.tsx` (`L547-L586`) & `frontend/src/pages/PutawayTasks.tsx` (`L174-L191`)
- Nối nút `"In Mã vạch Vị trí Kệ"` gọi API `POST /api/v1/warehouses/locations/barcode-sheet` và cập nhật danh sách mã vị trí kệ từ response.
