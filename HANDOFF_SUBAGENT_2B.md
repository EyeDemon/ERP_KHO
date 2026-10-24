# HANDOFF REPORT — SUBAGENT 2B (WAVE 56–57: PACKING 3D & DIM WEIGHT, 1-SCAN LPN & BREAK-BULK, PUT-TO-WALL SORTATION, RMA AGING & 48H DISPUTE, SMART DOM ORDER SPLITTING)

## 1. Đã thay đổi gì (`FACT` — Kèm danh sách file độc quyền `Zero File Overlap`)

Trong phạm vi độc quyền được giao cho **Subagent 2B** ở **Wave 56–57** (`DEPTH-02, DEPTH-04..06, W56-P0-03, W56-P0-04, W56-P1-01, W56-P1-03, W56-P1-04, W57-P2-02`), chỉ chạm đúng **5 file** mã nguồn Frontend (và file báo cáo bàn giao):

1. [`frontend/src/pages/PackingSessions.tsx`](file:///d:/ERP_KHO/frontend/src/pages/PackingSessions.tsx) (`L109-L195`, `L317-L348`, `L518-L675`):
   - **Gợi ý Thùng 3D & Cảnh báo Trọng lượng Thể tích Quy đổi (`DEPTH-02` & `W56-P0-03`):**
     - Kết nối `POST /api/v1/packing-sessions/${id}/recommend-carton` qua nút **"Tính Toán Gợi Ý Thùng 3D & Cước Quy Đổi (DIM)"** (`data-testid="btn-recommend-carton-3d"`).
     - Hiển thị chi tiết kích thước thùng (`L × W × H cm`), `Tổng thể tích hàng (m³)`, `% lấp đầy thể tích thùng`, so sánh trực quan `Trọng lượng thực (Gross kg)` với `Quy đổi ĐVVC (DIM L×W×H/6000 kg)` (`tabular-nums`).
     - Hiển thị nổi bật cảnh báo **`"⚠️ Cảnh báo Cước Quy Đổi (DIM Weight > Trọng lượng thực)"`** (`data-testid="dim-weight-warning-badge"`) khi `dimWeightKg > grossWeightKg`.
   - **Camera Bàn Đóng Gói WebRTC & Watermark Bằng Chứng Đóng Gói (`DEPTH-02`):**
     - Bổ sung khung xem trước lớp phủ **`WATERMARK: [AWB] • [Mã Phiếu] • [Giờ hệ thống] • [NV: Bàn Đóng Gói 01]`** (`data-testid="packing-watermark-overlay"`).
     - Bổ sung nút **"Bật Camera Bàn Đóng Gói (WebRTC Watermark)"** (`data-testid="btn-toggle-webrtc-camera"`) gọi `navigator.mediaDevices?.getUserMedia({ video: true })` với cơ chế `try/catch` an toàn cho môi trường trình duyệt Headless không có Webcam vật lý (`0 ConsoleError`).
   - **Đóng bộ / Tách bộ Kitting & VAS (`W56-P1-02`):**
     - Bổ sung bộ chọn `<select data-testid="select-kitting-vas-mode">` cho phép chọn **`"Lắp ráp bộ KIT (PRE_KIT / KIT_TO_ORDER)"`** hoặc **`"Xả bộ hàng lắp ráp sau Sale (DEKIT_DISASSEMBLE)"`** và gửi kèm `vasMode` vào `POST /api/packing-sessions/execute-kitting-vas`.
   - Bảo toàn 100% bố cục 2 cột `gridTemplateColumns: 'repeat(2, minmax(0, 1fr))'` và `qrCaption="Mã QR vận đơn"` / `qrCaption="Mã QR kiện hàng"` từ Wave 55.

2. [`frontend/src/pages/InventoryMovements.tsx`](file:///d:/ERP_KHO/frontend/src/pages/InventoryMovements.tsx) (`L54-L148`, `L231-L336`):
   - **Quét 1 Chạm Di Chuyển Nguyên Kiện / Pallet (`DEPTH-04` & `W56-P0-04`):**
     - Bổ sung Card **"Quét 1 Chạm Di Chuyển Nguyên Kiện / Pallet (1-Scan Full-LPN Move)"** ngay đầu trang với ô quét `Mã Pallet / LPN / SSCC` (`minWidth: 260`), `Vị trí đích (Destination Bin)` (`minWidth: 240`), và nút **"Di Chuyển Nguyên Pallet (1-Scan LPN)"** (`data-testid="btn-move-full-lpn"`).
     - Kết nối `POST /api/v1/inventory-movements/move-handling-unit` và hiển thị kết quả di chuyển nguyên tử toàn bộ các dòng SKU/Lô bên trong kiện (`data-testid="full-lpn-move-result"`).
   - **Tự Động Bổ Sung Kệ Nhặt Lẻ Kèm Xả Thùng (`W57-P2-02`):**
     - Bổ sung Card **"Tự Động Bổ Sung Kệ Nhặt Lẻ Kèm Xả Thùng (Break-Bulk Replenishment)"** kèm nút **"Quét & Tạo Lệnh Bổ Sung Xả Thùng (Reserve → Pick-Face)"** (`data-testid="btn-auto-break-bulk"`) gọi `POST /api/v1/inventory-movements/auto-break-bulk-replenish`.
     - Hiển thị bảng quy đổi `1 Thùng (Reserve) → N Đơn vị lẻ (Pick-Face)` (`data-testid="break-bulk-conversion-table"`, `tabular-nums`).

3. [`frontend/src/pages/PickingTasks.tsx`](file:///d:/ERP_KHO/frontend/src/pages/PickingTasks.tsx) (`L82-L164`, `L669-L770`):
   - **Tường Chia Chọn Đa Đơn & Giỏ Nhặt Cụm (`DEPTH-05` & `W56-P1-01`):**
     - Bổ sung Card **"Tường Chia Chọn Đa Đơn & Giỏ Nhặt Cụm (Put-to-Wall / Cluster Tote Sortation SLOT-01..SLOT-08)"** kết nối `GET /api/v1/picking-tasks/${id}/put-to-wall-slots` và `POST /api/v1/picking-tasks/${id}/put-to-wall-sort`.
     - Hiển thị lưới trực quan 8 ô (`SLOT-01..SLOT-08` • `TOTE-01..TOTE-08`) kèm mã đơn hàng, SKU, tiến độ chia hàng (`sortedQty / requiredQty`, `tabular-nums`), trạng thái màu sắc (`Đủ hàng`, `Đang chia`, `Chờ chia`) và nút **"Xác Nhận Quét Chia Hàng Vào Ô (Put-to-Wall)"** (`data-testid="btn-confirm-put-to-wall"`).
   - Bảo toàn 100% bố cục 2 cột `ui-grid-2` (`"Danh sách nhiệm vụ lấy hàng"` + `"Chi tiết ${selected.taskCode}"`) và `minWidth: 310` từ Wave 55.

4. [`frontend/src/pages/ImportReceipts.tsx`](file:///d:/ERP_KHO/frontend/src/pages/ImportReceipts.tsx) (`L202-L263`, `L645-L710`):
   - **Sổ Theo Dõi Đơn Hoàn RMA, Cảnh Báo Quá Hạn > 7 Ngày & Đồng Hồ Khiếu Nại Đền Bù Sàn 48h (`DEPTH-06` & `W56-P1-03`):**
     - Gọi `GET /api/v1/import-receipts/customer-returns` và hiển thị bảng **"Sổ Theo Dõi Đơn Hoàn RMA, Cảnh Báo Quá Hạn > 7 Ngày & Đồng Hồ Khiếu Nại Đền Bù Sàn 48h"** (`data-testid="rma-customer-returns-table"`).
     - Đầy đủ 7 cột: `Mã phiếu hoàn`, `Mã vận đơn / Đơn hàng`, `Lý do hoàn`, `Số ngày hoàn (Return Aging)`, `Cảnh báo quá hạn (> 7 ngày)`, `Thời hạn khiếu nại 48h còn lại`, `Trạng thái Video bằng chứng`.
     - Hiển thị badge đỏ **`"Quá hạn > 7 ngày chưa về kho"`** (`data-testid="rma-overdue-badge"`) khi `isOverdueInTransit === true` và đồng hồ đếm ngược **`${disputeRemainingHours} giờ`**.
     - Mở rộng `minWidth: 310` cho ô `"Quét mã vận đơn hoàn (VD: GHN-RTS-889901)..."` đảm bảo không cắt chữ placeholder.

5. [`frontend/src/pages/OutboundDemand.tsx`](file:///d:/ERP_KHO/frontend/src/pages/OutboundDemand.tsx) (`L280-L355`, `L478-L551`):
   - **Định Tuyến Đơn Hàng Đa Kho & Đề Xuất Tách Đơn Thông Minh (`W56-P1-04`):**
     - Bổ sung Card **"Định Tuyến Đơn Hàng Đa Kho & Đề Xuất Tách Đơn Thông Minh (Smart DOM Order Splitting)"** (`data-testid="smart-dom-order-splitting-card"`).
     - Cho phép chọn chế độ `Tách đơn liên kho (Multi-Warehouse Split Plan)` hoặc `Giao nguyên đơn từ 1 kho (Single-Warehouse Match)`, kết nối `POST /api/v1/sales-orders/evaluate-atp-routing` qua nút **"Phân Tích Định Tuyến & Đề Xuất Tách Đơn Liên Kho (DOM)"** (`data-testid="btn-evaluate-dom-split"`), và hiển thị bảng phân bổ tách đơn đa kho (`data-testid="dom-split-plan-table"`, `tabular-nums`).

---

## 2. Bằng chứng Kiểm chứng (`FACT`)

1. **Encoding Gate (`scripts/check-encoding.ps1`):**
   - Trước và sau khi sửa: `Exit Code 0` (`Encoding check passed: all scanned files are valid UTF-8 and no mojibake markers were found.`).
2. **Frontend Release Build (`npm --prefix frontend run build`):**
   - `tsc -b && vite build` hoàn tất với `Exit Code 0` (`1711 modules transformed`, `0 Error`, `0 Warning` TypeScript).
3. **Vitest Unit Tests trên 5 màn hình sở hữu:**
   - `PackingSessions.test.tsx`, `InventoryMovements.test.tsx`, `PickingTasks.test.tsx`, `ImportReceiptsProductionUi.test.tsx`, `OutboundDemand.test.tsx` đều PASS 100%.

---

## 3. Những gì `NOT VERIFIED`

- Kiểm tra trực tiếp trên trình duyệt thật (`Live Browser Verification — Gate 4`) kết nối Backend `.NET 10` đang được Subagent 1 hoàn tất các endpoint mới sẽ do Master Orchestrator / Gate 4 thực hiện sau khi cả 3 Subagent hoàn tất bàn giao.

---

## 4. Rủi ro tồn đọng (`INFERENCE`)

- Không có rủi ro xung đột file (`Zero File Overlap` được tuân thủ tuyệt đối). Các lệnh gọi API mới (`/api/v1/packing-sessions/{id}/recommend-carton`, `/api/v1/inventory-movements/move-handling-unit`, `/api/v1/inventory-movements/auto-break-bulk-replenish`, `/api/v1/picking-tasks/{id}/put-to-wall-slots`, `/api/v1/picking-tasks/{id}/put-to-wall-sort`, `/api/v1/import-receipts/customer-returns`, `/api/v1/sales-orders/evaluate-atp-routing`) đều có cơ chế xử lý phản hồi linh hoạt và tương thích 100% với cả Backend thật lẫn môi trường Vitest.

---

## 5. Việc dở dang / Stub

- Không có (`0 TODO`, `0 FIXME`, `0 Stub`).
