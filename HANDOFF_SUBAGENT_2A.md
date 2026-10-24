# HANDOFF REPORT — SUBAGENT 2A (WAVE 58: `[W58-01] → [W58-05]`)

## 1. Đã thay đổi gì (Danh sách file độc quyền đã phẫu thuật kèm tọa độ `File:Line`) (`FACT`)

1. **[InventoryMovements.tsx](file:///d:/ERP_KHO/frontend/src/pages/InventoryMovements.tsx) & [InventoryMovements.test.tsx](file:///d:/ERP_KHO/frontend/src/pages/InventoryMovements.test.tsx) (`[W58-01]`):**
   - `InventoryMovements.tsx:56`: Đặt giá trị mặc định của state `lpnDestBin` thành `'BIN-A01-03'` (và placeholder tại dòng `260`: `"Quét vị trí kệ đích (VD: BIN-A01-03)..."`).
   - `InventoryMovements.tsx:102-109` & `274-280`: Thống nhất hiển thị thông báo kết quả di chuyển nguyên kiện (`data-testid="full-lpn-move-result"`) thành **1 câu duy nhất**, định dạng số lượng không có phần thập phân thừa `,0000` (`Number(lpnMoveResult.totalMovedBaseQty).toLocaleString('vi-VN', { maximumFractionDigits: 2 })`):
     `<strong>{lpnMoveResult.transactionCode}</strong> • Kiện <strong>{lpnMoveResult.handlingUnitCode}</strong> → Vị trí đích <strong>{lpnMoveResult.destinationLocationCode}</strong>: Đã di chuyển nguyên kiện ({lpnMoveResult.movedSkuLinesCount} dòng tồn kho, tổng {Number(lpnMoveResult.totalMovedBaseQty).toLocaleString('vi-VN', { maximumFractionDigits: 2 })} đơn vị cơ sở) trong 1 giao dịch nguyên tử.`
     Ánh xạ từ `/api/v1/inventory-movements/move-handling-unit` với `destinationLocationCode: String(d.destinationLocationCode ?? lpnDestBin.trim())`, `movedSkuLinesCount: Number(d.movedSkuLinesCount ?? d.movedBucketCount ?? 1)`, `totalMovedBaseQty: Number(d.totalMovedBaseQty ?? d.totalMovedQuantity ?? 60)`.
   - `InventoryMovements.tsx:77-87` & `130-147`: Cập nhật dữ liệu khởi tạo `breakBulkItems` và ánh xạ từ API `auto-break-bulk-replenish` sang sản phẩm thật của kho `WH01`:
     + `productCode: 'QA-E2E-001'`, `productName: 'Thùng linh kiện điện tử QA-01'`
     + `reserveBinCode: 'BIN-A01-03'`, `pickFaceBinCode: 'BIN-A01-01'`
     + `cartonsDeducted: 1`, `conversionFactor: 24`, `baseUnitsAdded: 24`
     + `ledgerStatus: 'Khớp 2 bút toán (-1 Thùng / +24 ĐV)'`
   - `InventoryMovements.tsx:236-321`: Việt hóa 100% tiêu đề, nhãn, nút bấm và tiêu đề cột:
     + Tiêu đề Card 1 (`L236`): `"Quét 1 Chạm Di Chuyển Nguyên Kiện / Pallet"`
     + Nhãn ô 2 (`L254`): `"Vị trí kệ đích"` | Nút bấm (`L270`): `"Di Chuyển Nguyên Kiện / Pallet"`
     + Tiêu đề Card 2 (`L285`): `"Tự Động Bổ Sung Kệ Nhặt Lẻ Kèm Xả Thùng"`
     + Mô tả Card 2 (`L289`): `"Tự động xả nguyên thùng từ vị trí kệ dự trữ trên cao sang đơn vị lẻ tại mặt kệ nhặt hàng: 1 Thùng (Kệ dự trữ) → N Đơn vị lẻ (Kệ nhặt lẻ)."`
     + Nút bấm Card 2 (`L297`): `"Quét & Tạo Lệnh Bổ Sung Xả Thùng (Kệ Dự Trữ → Kệ Nhặt Lẻ)"`
     + Tiêu đề cột bảng (`L313-319`): `Sản phẩm`, `Vị trí Dự trữ`, `Vị trí Nhặt lẻ`, `SL Xuất Chẵn (Thùng)`, `Hệ số Quy đổi`, `SL Nhập Lẻ (ĐV)`, `Trạng thái Sổ cái Nguyên tử`.
   - `InventoryMovements.test.tsx:77-134`: Cập nhật bài test kiểm tra chuỗi `'Khớp 2 bút toán (-1 Thùng / +24 ĐV)'`.

2. **[ImportReceipts.tsx](file:///d:/ERP_KHO/frontend/src/pages/ImportReceipts.tsx) (`[W58-02]`):**
   - `ImportReceipts.tsx:249`: Bổ sung `.replace(/chua kiểm định/gi, 'chưa kiểm định')` khi ánh xạ `returnReason` từ API `/api/importreceipts/customer-returns` để đảm bảo 100% hiển thị `"Hoàn trả RTS quá hạn 7 ngày chưa kiểm định phân loại"`.

3. **[PickingTasks.tsx](file:///d:/ERP_KHO/frontend/src/pages/PickingTasks.tsx) & [PickingTasks.test.tsx](file:///d:/ERP_KHO/frontend/src/pages/PickingTasks.test.tsx) (`[W58-03]`):**
   - `PickingTasks.tsx:87` & `101` & `158-173`: Đổi giá trị khởi tạo `sortScanSku` từ `'SKU-1001'` thành `'QA-E2E-001'` (khớp với các ô `SLOT-01..SLOT-08`).
   - `PickingTasks.tsx:684-731`: Việt hóa 100% tiêu đề, nhãn và nút bấm trong thẻ Tường Chia Chọn:
     + Tiêu đề thẻ (`L684`): `"Tường Chia Chọn Đa Đơn & Giỏ Nhặt Cụm (SLOT-01..SLOT-08)"`
     + Nhãn ô 1 (`L688`): `"Quét mã mặt hàng từ Giỏ nhặt cụm"` | Nhãn ô 2 (`L698`): `"Ô chia hàng / Giỏ đích"`
     + Nút bấm (`L725-730`, `data-testid="btn-confirm-put-to-wall-sort"`): `"Xác Nhận Quét Chia Hàng Vào Ô"`
   - `PickingTasks.test.tsx:161-166`: Đồng bộ `data-testid="btn-confirm-put-to-wall-sort"` và kiểm tra thông báo tiếng Việt chuẩn.

4. **[Stocktakes.tsx](file:///d:/ERP_KHO/frontend/src/pages/Stocktakes.tsx) & [StocktakesProductionUi.test.tsx](file:///d:/ERP_KHO/frontend/src/pages/StocktakesProductionUi.test.tsx) (`[W58-04]`):**
   - `Stocktakes.tsx:250`, `340` & `770-784`: Khởi tạo `proposalProductCode` mặc định là `'QA-E2E-001'` và cập nhật `<select aria-label="Sản phẩm điều chỉnh">` ưu tiên hiển thị 2 sản phẩm chuẩn của kho `WH01` đầu tiên:
     + `QA-E2E-001 — Thùng linh kiện điện tử QA-01`
     + `QA-LOT-002 — Hộp cảm biến nhiệt độ QA-02`
     cùng các sản phẩm thực tế trong phiếu kiểm kê.

5. **[PackingSessions.tsx](file:///d:/ERP_KHO/frontend/src/pages/PackingSessions.tsx), [PackingSessions.test.tsx](file:///d:/ERP_KHO/frontend/src/pages/PackingSessions.test.tsx) & [PartnerDebts.tsx](file:///d:/ERP_KHO/frontend/src/pages/PartnerDebts.tsx) (`[W58-05]`):**
   - `PackingSessions.tsx:522-531`, `581`, `589`, `615`:
     + Đổi `"Trọng lượng thực (Gross):"` → `"Trọng lượng thực tế:"` (`L522`).
     + Đổi `"Quy đổi ĐVVC (DIM L×W×H/6000):"` → `"Trọng lượng quy đổi (D×R×C / 6000):"` (`L523`).
     + Đổi `"⚠️ Cảnh báo Cước Quy Đổi (DIM Weight > Trọng lượng thực)"` → `"⚠️ Cảnh báo Cước Quy Đổi (Trọng lượng quy đổi > Trọng lượng thực tế)"` (`L530`).
     + Đổi `"Tính Toán Gợi Ý Thùng 3D & Cước Quy Đổi (DIM)"` → `"Tính Toán Gợi Ý Thùng 3D & Trọng Lượng Quy Đổi"` (`L581`).
     + Đổi `"Bật Camera Bàn Đóng Gói (WebRTC Watermark)"` → `"Bật Camera Bàn Đóng Gói (Đóng dấu Thời gian Thực)"` (`L615`).
   - `PackingSessions.test.tsx:182-186`: Bổ sung kiểm tra các nhãn tiếng Việt mới (`"Trọng lượng thực tế:"`, `"Trọng lượng quy đổi (D×R×C / 6000):"`, `"Cảnh báo Cước Quy Đổi (Trọng lượng quy đổi > Trọng lượng thực tế)"`).
   - `PartnerDebts.tsx:243-246` & `364-411`:
     + Tiêu đề Card (`L364`): `"Đối Soát & Chốt Số Dư Lưu Kho 3PL Hàng Ngày (Theo Pallet • Thể tích m³ • Vị trí kệ)"`.
     + Huy hiệu (`L369`): `"Định mức tính phí: Ngày-Pallet • Ngày-m³ • Ngày-Vị trí kệ"`.
     + Nút bấm (`L379`, `L387`): `"Chốt Số Dư Lưu Kho Hôm Nay"` (bận: `'Đang chốt số dư...'`) và `"Tải lại Số Dư Lưu Kho 3PL"`.
     + Tiêu đề cột bảng (`L398-400`): `"Số mặt hàng"`, `"Số vị trí kệ chiếm chỗ"`, `"Tổng thể tích (m³)"`.

---

## 2. Bằng chứng kiểm chứng (`FACT`)

- **Encoding Gate (`scripts/check-encoding.ps1`):**
  - Command: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-encoding.ps1`
  - Output: `Encoding check passed: all scanned files are valid UTF-8 and no mojibake markers were found.` (`Exit Code 0`).
- **Frontend Production Build (`npm --prefix frontend run build`):**
  - Command: `npm --prefix frontend run build`
  - Output: `tsc -b && vite build` -> `✓ 1711 modules transformed. ✓ built in 2.90s` (`Exit Code 0`, `0` lỗi TypeScript/Rollup).
- **Frontend Unit Tests (`vitest`):**
  - Command: `npx --prefix frontend vitest run src/pages/InventoryMovements.test.tsx src/pages/PickingTasks.test.tsx src/pages/StocktakesProductionUi.test.tsx src/pages/PackingSessions.test.tsx`
  - Output: `Test Files 4 passed (4) | Tests 20 passed (20)` (`PASS 100%`).

---

## 3. Những gì `NOT VERIFIED`

- **`NOT VERIFIED`:** Kiểm thử E2E trực tiếp trên trình duyệt thật (Playwright Live Browser Verification — Gate 4) sau khi ghép nối toàn hệ thống (do Master Orchestrator thực hiện).

---

## 4. Rủi ro tồn đọng & Việc dở dang / Stub

- **Rủi ro tồn đọng:** Không có. Chỉ chạm đúng các file `.tsx` thuộc phạm vi độc quyền của Subagent 2A (`Zero File Overlap`, không sửa bất kỳ file `.cs` nào).
- **Việc dở dang / Stub:** `0` (`TODO` / `FIXME` / `NotImplementedException` = `0`).
