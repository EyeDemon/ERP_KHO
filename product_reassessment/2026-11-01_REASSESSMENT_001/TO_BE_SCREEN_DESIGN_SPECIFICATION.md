# TO-BE SCREEN DESIGN SPECIFICATION — Đặc Tả Thiết Kế Chi Tiết Từng Màn Hình & Kế Hoạch Tách God-Components

**Document ID:** Deliverable K (`TO_BE_SCREEN_DESIGN_SPECIFICATION.md`)  
**Run ID:** `2026-11-01_REASSESSMENT_001`  
**Target Viewports:** Desktop `1440x900` (`0px` horizontal page overflow) & Mobile/PDA `390x844`  
**Compliance Rules:** `FE-001..007`, `SEC-001..002`, `API-001..004`, `CON-001..002`, Skill `vite-react-best-practices` (No file `> 500 LOC`)

---

## 1. Quy Chuẩn Chung Áp Dụng Xuyên Suốt 12 Màn Hình Cốt Lõi

### 1.1 Chuẩn 5 Trạng Thái Giao Diện Bắt Buộc (`FE-004`)
Mọi màn hình thao tác dữ liệu trong tài liệu này bắt buộc cài đặt đủ 5 trạng thái chuẩn hóa qua component `<UiStateBoundary>` và `<UiErrorBanner>`:
1. **`Loading`:** Hiển thị khung xương (`Skeleton`) cho bảng/thẻ KPI; khi đang gửi lệnh (`isSubmitting === true`), khóa cứng nút Submit (`disabled`), đổi nhãn thành `"Đang xử lý..."` và giữ nguyên `Idempotency-Key` (`FE-005`).
2. **`Empty State`:** Sử dụng `<UiEmptyState title="..." detail="..." />` kèm nút hành động gợi ý đầu tiên (ví dụ: `"Tạo phiếu nhập đầu tiên"` hoặc `"Xóa bộ lọc tìm kiếm"`).
3. **`422 Business Error` (`Unprocessable Entity`):** Hiển thị `<UiErrorBanner tone="warning" />` đọc trực tiếp mã `errorCode` từ Server (ví dụ: `INV_INSUFFICIENT_AVAILABLE_STOCK`, `PICK_WRONG_LOT`, `PACK_QUANTITY_EXCEEDS_PICKED`), nêu rõ hướng dẫn khắc phục bằng tiếng Việt và đính kèm mã `correlationId`.
4. **`409 Conflict` (`Concurrency / Idempotency / State Conflict`):** Hiển thị `<UiErrorBanner tone="danger" />` với thông báo từ `CONFLICT_ERROR_MESSAGES[errorCode]` (ví dụ: `CONCURRENCY_CONFLICT`, `INVALID_STATE_TRANSITION`, `ORDER_CANCELLED_PUTBACK_REQUIRED`) kèm nút **`[🔄 Tải lại dữ liệu mới nhất]`** (`data-testid="btn-reload-conflict"`).
5. **`403 Forbidden`:** Tự động kích hoạt đồng bộ lại `GET /api/v1/auth/me`; hiển thị thông báo `"Tài khoản chưa được cấp quyền [Mã quyền] hoặc phạm vi kho [WarehouseId]"` và ẩn/vô hiệu hóa các nút vượt quyền (`FE-006`).

---

## 2. Đặc Tả Chi Tiết 12 Không Gian Làm Việc Cốt Lõi (`SCR-01` → `SCR-12`)

---

### `SCR-01`: Trung Tâm Điều Hành & Tháp Kiểm Soát Đa Kho (`Operational Command Center`)
- **Mục đích:** Điểm vào trung tâm cung cấp cái nhìn thời gian thực về sức khỏe tồn kho, hàng đợi tác vụ sàn kho, cảnh báo ngoại lệ SLA và hiệu quả tài chính chủ hàng.
- **Đối tượng sử dụng (`Persona`):** Quản lý kho, Chủ doanh nghiệp, Thủ kho trưởng, Kế toán.
- **Route & File nguồn:** `/` và `/dashboard` — [`Dashboard.tsx`](file:///d:/ERP_KHO/frontend/src/pages/Dashboard.tsx) (`904 LOC` → tách còn `260 LOC` container).
- **Mẫu bố cục (`Layout Pattern`):** `Command Center Grid` (Thanh tìm kiếm toàn cục → Dải 6 KPI → Lưới 2 cột: Hàng đợi Ngoại lệ & Tác vụ | Tháp điều khiển Đa kho & Tài chính).

#### Wireframe ASCII (`Desktop 1440x900` & `PDA 390x844`)
```text
[DESKTOP 1440x900]
+--------------------------------------------------------------------------------------------------+
| TRUNG TÂM ĐIỀU HÀNH KHO                                    [🔍 Tìm nhanh SKU, PO, AWB, Vị trí...] |
+--------------------------------------------------------------------------------------------------+
| [Kho hoạt động: 3] [SKU: 1.240] [Nhóm tồn: 4.820] [Tác vụ mở: 18] [Ngoại lệ: 3 ⚠️] [Lãi gộp: Ẩn/Hiện]|
+------------------------------------------------+-------------------------------------------------+
| CẢNH BÁO NGOẠI LỆ VẬN HÀNH (3)                 | THÁP ĐIỀU KHIỂN ĐA KHO (MULTI-WAREHOUSE TOWER)  |
| [!] Lệch nhận hàng PO-2026-09 (WH01) [Xác nhận]| Kho   | Tồn thực tế | Đã giữ | Chờ cất | Chờ lấy|
| [!] Đơn SO-882 hủy chờ Putback       [Xử lý]   | WH01  |      14.500 |  1.200 |       8 |     12 |
|------------------------------------------------| WH02  |       6.320 |    450 |       2 |      4 |
| HÀNG ĐỢI TÁC VỤ SÀN KHO (TASK ENGINE)          |-------------------------------------------------|
| [Tất cả (18)] [Cất hàng (6)] [Lấy hàng (12)]   | TÀI CHÍNH & ĐỐI SOÁT COD (Chỉ hiện khi có quyền)|
| • PUT-102: Cất 50 Thùng Sữa -> A-01-02 [Mở]    | Doanh thu thuần | Giá vốn WAC | Lãi gộp | COD chờ|
+------------------------------------------------+-------------------------------------------------+

[MOBILE / PDA 390x844]
+---------------------------------------+
| ERP WMS • WH01              [⚡ PDA]  |
| [🔍 Quét mã bất kỳ để tra cứu...] [📷]|
| +-----------------+ +---------------+ |
| | 📥 1. NHẬN HÀNG | | 📦 2. CẤT HÀNG| |
| +-----------------+ +---------------+ |
| +-----------------+ +---------------+ |
| | 🛒 3. LẤY HÀNG  | | 🚚 4. BÀN GIAO| |
| +-----------------+ +---------------+ |
| +-----------------+ +---------------+ |
| | 🔄 5. CHUYỂN/RMA| | 📋 6. KIỂM KÊ | |
| +-----------------+ +---------------+ |
+---------------------------------------+
```

- **Bảng dữ liệu & Cột số học (`tabular-nums`):**
  - Bảng *Tháp điều khiển Đa kho*: `Mã kho` (Left, Mono), `Tồn thực tế (OnHand)` (Right, `tabular-nums`), `Đã giữ (Reserved)` (Right, `tabular-nums`), `Khả dụng (Available)` (Right, `tabular-nums`), `Tác vụ Cất/Lấy` (Right, `tabular-nums`), `Trạng thái SLA` (Badge).
- **Phân quyền (`Permission Gating`):** Khối **Tài chính Chủ hàng (Doanh thu, Giá vốn WAC, Lãi gộp, % Biên lãi)** chỉ gọi API và chỉ render khi `hasPermission('inventory.cost.read') === true`.
- **Kế hoạch tách God-Component (`Dashboard.tsx` 904 LOC → 5 files `< 260 LOC`):**
  - `pages/Dashboard.tsx` (`220 LOC`)
  - `components/dashboard/DashboardControlTowerTable.tsx` (`180 LOC`)
  - `components/dashboard/DashboardFinanceSummaryCard.tsx` (`170 LOC`)
  - `components/dashboard/DashboardTaskQueueCard.tsx` (`190 LOC`)
  - Giữ nguyên `DashboardMetrics.tsx`, `DashboardExceptionAlerts.tsx`, `DashboardRecentActivity.tsx`.

---

### `SCR-02`: Bàn Tiếp Nhận, Kiểm Định QC & Phiếu Nhập Kho (`ImportReceipts.tsx` — Decomposed from 1,991 LOC)
- **Mục đích:** Quản lý trọn vẹn vòng đời Nhập kho (15 trạng thái): Tạo phiếu nháp → Tiếp nhận hàng thực tế theo dòng → Gán Lô/Sê-ri/Hạn sử dụng → Kiểm định chất lượng (QC) → Giải quyết sai lệch thừa/thiếu/hỏng → Phê duyệt → Ghi sổ nguyên tử vào Sổ cái & Tồn kho 9 chiều → In phiếu.
- **Đối tượng sử dụng:** Nhân viên nhận hàng, Nhân viên QC, Thủ kho, Quản lý kho.
- **Route & File nguồn:** `/import-receipts` — [`ImportReceipts.tsx`](file:///d:/ERP_KHO/frontend/src/pages/ImportReceipts.tsx) (`1,991 LOC` — **God Component lớn thứ 2 hệ thống**).
- **Mẫu bố cục:** `Master-Detail Split Workbench + Slide-over Action Drawers`.

#### Wireframe ASCII (`Desktop 1440x900` & `PDA 390x844`)
```text
[DESKTOP 1440x900]
+--------------------------------------------------------------------------------------------------+
| NHẬP KHO / PHIẾU NHẬP & KIỂM ĐỊNH QC                 [+ Tạo Phiếu Nhập] [🖨️ In Phiếu] [⚡ Express]|
| Tabs: [Phiếu nhập kho (42)] [Đơn mua PO (/purchase-orders)] [ASN dự kiến (/asns)] [Cổng bãi]     |
+--------------------------------------------------------------------------------------------------+
| [🔍 Quét/Tìm Mã phiếu, PO, ASN, NCC...] [Kho: Tất cả v] [Trạng thái: Tất cả v] [Từ ngày - Đến ngày]|
+--------------------------------------------------------------------------------------------------+
| Mã Phiếu     | Nguồn PO/ASN | Nhà Cung Cấp | Kho  | SL Dự Kiến | SL Thực Nhận | Đơn Giá* | Trạng Thái|
| PNK-2026-081 | ASN-2026-19  | Cty Sữa VN   | WH01 |      500,0 |        495,0 | 120.000đ | Chờ xử lý |
+--------------------------------------------------------------------------------------------------+
| CHI TIẾT BÀN TIẾP NHẬN & KIỂM ĐỊNH: PNK-2026-081                       [Duyệt] [Ghi sổ Tồn kho]  |
| [🔊 Quét Barcode SP / Lô để nhận hàng: ___________________________] [Chế độ: Tự động cộng +1]    |
| Mã SKU  | Tên Sản Phẩm | ĐVT   | SL Đặt | Đã Nhận | Đạt QC | Hỏng/Từ chối | Lô / Hạn Dùng | Thao tác|
| SKU-001 | Sữa tươi 1L  | Thùng |  500,0 |   495,0 |  490,0 |          5,0 | LOT26A (12/27)| [Nhận/QC]|
+--------------------------------------------------------------------------------------------------+

[MOBILE / PDA 390x844]
+---------------------------------------+
| 📥 NHẬN HÀNG: PNK-2026-081   [Ghi sổ] |
| [🔊 Quét Barcode SP / Lô...     ] [📷]|
|---------------------------------------|
| SKU-001 • Sữa tươi tiệt trùng 1L      |
| Đặt: 500 | Đã nhận: 495 | Lệch: -5    |
| Lô: [LOT26A    ] HSD: [2027-12-31]    |
| SL Nhận: [-][  495  ][+] [Xác nhận]   |
+---------------------------------------+
```

- **Cột dữ liệu chuẩn (`tabular-nums`):**
  - `SL Dự kiến`, `SL Thực nhận`, `SL Đạt QC`, `SL Hỏng/Từ chối`, `Hệ số quy đổi ĐVT`, `Đơn giá nhập (VND)` và `Thành tiền (VND)` đều căn phải (`className="ui-cell-numeric"`).
- **Tương tác Máy quét Barcode:**
  - Quét mã vạch sản phẩm trên `ScanInputBar` tự động tìm dòng khớp `ProductBarcode`, cuộn tới dòng đó và mở nhanh ô nhập `SL Thực nhận + Lô/Sê-ri/HSD` (`ReceiptInventoryIdentityEditor`).
- **Phân quyền (`Permission Gating`):**
  - Cột `Đơn giá nhập` và `Thành tiền` bị ẩn hoàn toàn trên DOM nếu `!hasPermission('inventory.cost.read')`.
  - Nút `[Nhận hàng]` cần `receipt.receive`; `[Kiểm định QC]` cần `quality_inspection.execute`; `[Xử lý sai lệch]` cần `receiving_discrepancy.resolve`; `[Phê duyệt]` cần `receipt.approve`; `[Ghi sổ Tồn kho]` cần `receipt.post`.
- **Kế hoạch tách God-Component (`ImportReceipts.tsx` 1,991 LOC → 6 files `< 350 LOC`):**
  1. `pages/ImportReceipts.tsx` (`280 LOC` — State điều phối, Tabs & KPI strip)
  2. `modules/inbound/ImportReceiptListTable.tsx` (`310 LOC` — Bảng danh sách + bộ lọc phân trang)
  3. `modules/inbound/ImportReceiptFormDrawer.tsx` (`340 LOC` — Drawer tạo/sửa phiếu nháp & chọn dòng PO/ASN)
  4. `modules/inbound/ReceivingLineWorksheet.tsx` (`350 LOC` — Bàn tiếp nhận quét mã vạch & gán Lot/Serial/Expiry)
  5. `modules/inbound/InboundQcInspectionPanel.tsx` (`320 LOC` — Kiểm định QC đầu vào & phân loại chất lượng)
  6. `modules/inbound/ReceivingDiscrepancyDrawer.tsx` (`290 LOC` — Giải quyết sai lệch Thừa/Thiếu/Từ chối tại cửa kho)

---

### `SCR-03`: Bàn Điều Phối Cất Hàng Thông Minh & Cross-Dock (`PutawayTasks.tsx`)
- **Mục đích:** Điều hướng nhân viên kho cất hàng từ khu vực tiếp nhận (`STAGING-IN`) lên đúng ô kệ lưu trữ tối ưu dựa trên thuật toán chấm điểm 5 yếu tố và 4 chốt chặn an toàn (Dung tích, Khóa kiểm kê, Trạng thái kệ, Nhiệt độ).
- **Đối tượng sử dụng:** Nhân viên kho (PDA), Thủ kho.
- **Route & File nguồn:** `/putaway-tasks` — [`PutawayTasks.tsx`](file:///d:/ERP_KHO/frontend/src/pages/PutawayTasks.tsx) (`659 LOC` → tách thành 3 component `< 300 LOC`).
- **Mẫu bố cục:** `Scanner-First Workbench + AI Bin Suggestion Card`.

#### Wireframe ASCII (`Desktop 1440x900` & `PDA 390x844`)
```text
[DESKTOP 1440x900]
+--------------------------------------------------------------------------------------------------+
| CẤT HÀNG LÊN KỆ (DIRECTED PUTAWAY & CROSS-DOCK)                      [Phân tích ABC-XYZ Slotting]|
+--------------------------------------------------------------------------------------------------+
| [Chờ thực hiện: 12] [Đang cất: 4] [Cất trả hàng hủy (Putback): 2 ⚠️] [Gợi ý Cross-Dock: 1 ⚡]     |
+---------------------------------------------------+----------------------------------------------+
| DANH SÁCH NHIỆM VỤ CẤT HÀNG                       | TRẠM QUÉT CẤT HÀNG & ĐỀ XUẤT VỊ TRÍ ĐÍCH     |
| Mã NV   | SKU     | SL Cất | Từ Vị Trí | Đích Gợi Ý| Nhiệm vụ: PUT-2026-104 (50 Thùng Sữa 1L)     |
| PUT-104 | SKU-001 |   50,0 | STAGE-IN  | A-01-02-01| ⭐ Đề xuất #1: A-01-02-01 (Điểm: 96/100)     |
| PUT-105 | SKU-009 |   12,0 | STAGE-IN  | CROSS-DOCK|   Lý do: Cùng Lô FEFO + Gần mặt lấy hàng     |
|                                                   | [🔊 Quét Mã Vị Trí Đích (Bin): ____________] |
|                                                   | [🔊 Quét Mã Sản Phẩm / HU: ________________] |
|                                                   | [✓ Xác nhận Cất hàng Hoàn tất] [Báo Ngoại lệ]|
+---------------------------------------------------+----------------------------------------------+
```

- **Mã lỗi máy đọc được (`FE-003`):** Bắt chính xác `PUTAWAY_WRONG_BIN` (quét nhầm kệ không khớp vị trí đích được duyệt) và `PUTAWAY_WRONG_PRODUCT` (quét nhầm sản phẩm).

---

### `SCR-04`: Điều Phối Nhu Cầu Xuất, Giữ Hàng, Phân Bổ & Phiếu Xuất (`OutboundDemand` + `ExportReceipts`)
- **Mục đích:** Quản lý tập trung luồng cầu hàng hóa (Đơn bán SO, Đơn thiếu hàng Backorder), Phiếu xuất kho, Giữ hàng (`Reservations`) và Phân bổ vị trí kệ (`Allocations`).
- **Đối tượng sử dụng:** Điều phối viên xuất kho, CSKH, Thủ kho, Quản lý kho.
- **Route & File nguồn:** `/export-receipts` ([`ExportReceipts.tsx`](file:///d:/ERP_KHO/frontend/src/pages/ExportReceipts.tsx) — `1,417 LOC`), `/backorders` ([`OutboundDemand.tsx`](file:///d:/ERP_KHO/frontend/src/pages/OutboundDemand.tsx) — `1,021 LOC`), `/stock-reservations` (`342 LOC`), `/stock-allocations` (`395 LOC`).
- **Mẫu bố cục:** `Tabbed Outbound Control Workspace + Reallocation Candidate Drawer`.

#### Wireframe ASCII (`Desktop 1440x900`)
```text
+--------------------------------------------------------------------------------------------------+
| ĐIỀU PHỐI XUẤT KHO & PHÂN BỔ TỒN                      [+ Tạo Phiếu Xuất] [+ Tạo Đơn Bán SO]     |
| Tabs: [Phiếu xuất (/export-receipts)] [Đơn bán & Backorder (/backorders)] [Giữ hàng] [Phân bổ kệ]|
+--------------------------------------------------------------------------------------------------+
| [Tổng phiếu: 128] [Chờ duyệt: 9] [Đã giữ hàng: 34] [Thiếu hàng Backorder: 5] [Đã xuất kho: 80]   |
+--------------------------------------------------------------------------------------------------+
| Mã Chứng Từ  | Khách Hàng / Kênh | ĐVVC / Mã AWB | Kho  | Tổng SL | Trạng Thái Giữ | Trạng Thái  |
| PXK-2026-210 | Shopee Mall Official| GHN-8829102 | WH01 |    24,0 | Đã phân bổ kệ  | Sẵn sàng lấy|
+--------------------------------------------------------------------------------------------------+
```

- **Kế hoạch tách God-Component (`ExportReceipts.tsx` 1,417 LOC & `OutboundDemand.tsx` 1,021 LOC):**
  1. `pages/ExportReceipts.tsx` (`290 LOC`)
  2. `modules/outbound/ExportReceiptFormDrawer.tsx` (`360 LOC`)
  3. `modules/outbound/ExportDispatchDialog.tsx` (`280 LOC`)
  4. `modules/outbound/SalesOrderDemandTable.tsx` (`340 LOC`)
  5. `modules/outbound/BackorderRecoveryPanel.tsx` (`310 LOC`)

---

### `SCR-05`: Bàn Điều Phối Đợt Lấy Hàng (Wave) & Quét Lấy Hàng PDA (`PickingTasks.tsx`)
- **Mục đích:** Tổ chức lấy hàng theo cụm đơn (Wave Picking) hoặc theo đơn lẻ; hướng dẫn nhân viên kho đi theo lộ trình kệ tối ưu và quét xác nhận đúng `Vị trí + Sản phẩm + Lô FEFO`.
- **Đối tượng sử dụng:** Nhân viên lấy hàng (PDA), Giám sát xuất kho.
- **Route & File nguồn:** `/picking-tasks` — [`PickingTasks.tsx`](file:///d:/ERP_KHO/frontend/src/pages/PickingTasks.tsx) (`858 LOC` → tách còn `320 LOC` + `WaveManagementPanel.tsx` + `PdaPickScannerCard.tsx`).
- **Xử lý lỗi chuẩn (`FE-003`):** Hiển thị cảnh báo tức thời cho `PICK_WRONG_LOCATION`, `PICK_WRONG_PRODUCT`, `PICK_WRONG_LOT`, và mở Drawer xử lý `PICK_SHORT` (Phân bổ lại sang kệ khác / Chuyển phần thiếu sang Backorder).

---

### `SCR-06`: Trạm Đóng Gói, Đơn Vị Xử Lý (HU / SSCC) & Lắp Ráp Bộ Kit (`PackingSessions.tsx`)
- **Mục đích:** Kiểm đếm hàng đã lấy, đóng vào Thùng/Pallet (`HandlingUnit`), gợi ý kích thước hộp carton 3D, ghi hình camera bằng chứng đóng gói (`PackingVideoEvidence`), in tem nhiệt AWB `100x150mm` và thực thi lệnh lắp ráp/tháo dỡ bộ (`KitWorkOrder`).
- **Đối tượng sử dụng:** Nhân viên đóng gói, Nhân viên VAS.
- **Route & File nguồn:** `/packing-sessions` — [`PackingSessions.tsx`](file:///d:/ERP_KHO/frontend/src/pages/PackingSessions.tsx) (`1,087 LOC`).

#### Wireframe ASCII (`Desktop 1440x900` — Trạm Đóng Gói 2 Cột)
```text
+--------------------------------------------------------------------------------------------------+
| TRẠM ĐÓNG GÓI, ĐƠN VỊ XỬ LÝ (HU) & LẮP RÁP BỘ KIT          [🎥 Đang ghi hình bằng chứng: REC 00:42]|
| Tabs: [1. Bàn đóng gói (Workbench)] [2. Sổ đăng ký Thùng/Pallet (HU)] [3. Lắp ráp Combo/Kit VAS] |
+-------------------------------------------------+------------------------------------------------+
| CÁC DÒNG HÀNG ĐÃ LẤY CHỜ ĐÓNG GÓI               | ĐƠN VỊ XỬ LÝ (HANDLING UNIT / THÙNG CARTON)    |
| [🔊 Quét Barcode SP để bỏ vào thùng: _________] | Thùng đang mở: HU-2026-0092 (SSCC: 00893...)   |
| SKU     | Tên SP         | Đã Lấy | Đã Đóng Gói | Gợi ý hộp 3D: Hộp Carton B (30x20x15cm - 2,4kg)|
| SKU-001 | Sữa tươi 1L    |   10,0 |        10,0 | • SKU-001 x 10 (Lô LOT26A)                     |
| SKU-002 | Bánh quy bơ    |    5,0 |         3,0 | [🔒 Niêm phong Thùng HU] [🖨️ In Tem AWB 100x150]|
+-------------------------------------------------+------------------------------------------------+
```

- **Kế hoạch tách God-Component (`PackingSessions.tsx` 1,087 LOC → 4 files `< 320 LOC`):**
  1. `pages/PackingSessions.tsx` (`240 LOC`)
  2. `modules/outbound/PackingStationWorkbench.tsx` (`320 LOC`)
  3. `modules/outbound/HandlingUnitRegistryTab.tsx` (`280 LOC`)
  4. `modules/outbound/KitWorkOrderVasTab.tsx` (`290 LOC` — kết nối trực tiếp API `/api/v1/products/kit-work-orders`)

---

### `SCR-07`: Thực Hiện Giao Vận & Quét Biên Bản Bàn Giao Bưu Tá (`Shipments` + `HandoverManifests`)
- **Mục đích:** Quản lý xếp hàng lên xe tải (`Staging → Loading → Dispatched → Delivered/POD`) và quét mã vận đơn `AWB` tốc độ cao bàn giao cho bưu tá (GHN, GHTK, ViettelPost, SPX, J&T), tự động chặn đơn khách vừa hủy trên sàn TMĐT.
- **Đối tượng sử dụng:** Nhân viên bàn giao cửa xuất, Điều phối giao vận.
- **Route & File nguồn:** `/handover-manifests` (`373 LOC`) & `/shipments` (`732 LOC`).
- **Tương tác Máy quét Barcode:** Khi quét mã `AWB` vào ô `ScanInputBar`, nếu đơn hàng ở trạng thái `CancelPendingPutback`, hệ thống trả HTTP `409` kèm mã `ORDER_CANCELLED_PUTBACK_REQUIRED`, phát âm thanh cảnh báo lỗi và hiển thị hộp thoại đỏ yêu cầu tách kiện hàng ra khỏi bao tải bưu tá để đưa đi `Putback`.

---

### `SCR-08`: Trạm Tiếp Nhận Hàng Hoàn, Kiểm Định RMA & Xử Lý Định Đoạt (`CustomerReturns.tsx` — **[NEW UI SCREEN]**)
- **Mục đích:** Giải quyết khoảng trống nghiệp vụ số 1 hiện tại (`GAP-RET-01`): Cung cấp màn hình chuyên biệt cho luồng Hàng hoàn khách trả (`CustomerReturn` & `CustomerReturnLine`) từ tiếp nhận kiện hoàn vào khu Biệt trữ (`QUARANTINED`), kiểm định phân loại tình trạng (`GOOD`, `DAMAGED_BOX`, `DEFECTIVE`) đến thực thi 4 hướng định đoạt (`RESTOCK_AVAILABLE`, `REFURBISH_REPACK`, `RTV_VENDOR`, `SCRAP_DISPOSAL`).
- **Đối tượng sử dụng:** Nhân viên nhận hàng hoàn, Nhân viên QC, Thủ kho, Kế toán.
- **Route mới:** `/customer-returns` — File mới `frontend/src/pages/CustomerReturns.tsx` (`~340 LOC`) + `modules/returns/ReturnDispositionDrawer.tsx` (`~260 LOC`).
- **Mẫu bố cục:** `3-Stage Reverse Logistics Workbench (Intake Scan → Inspection Grading → Disposition Execution)`.

#### Wireframe ASCII (`Desktop 1440x900` & `PDA 390x844`)
```text
[DESKTOP 1440x900]
+--------------------------------------------------------------------------------------------------+
| HÀNG HOÀN & LOGISTICS NGƯỢC / TRẠM TIẾP NHẬN & KIỂM ĐỊNH RMA          [+ Tiếp nhận Kiện Hoàn Mới]|
| Tabs: [1. Biệt trữ chờ kiểm định (8)] [2. Đã kiểm định chờ xử lý (5)] [3. Lịch sử RTV & Tân trang]|
+--------------------------------------------------------------------------------------------------+
| [Chờ kiểm định: 8] [Nhập lại kho (Restock): 42] [Chờ tân trang thay vỏ: 6] [Chờ xuất trả NCC: 4] |
+--------------------------------------------------------------------------------------------------+
| [🔊 Quét Mã Vận Đơn Hoàn (Tracking AWB) / Mã Đơn Bán SO / Mã RMA: ____________________] [Tiếp nhận]|
+--------------------------------------------------------------------------------------------------+
| Mã RMA         | Mã Vận Đơn Hoàn | Đơn Gốc    | Kho  | Vị Trí Biệt Trữ | SL Hoàn | Tình Trạng | Xử Lý|
| RMA-20261101-01| GHN-RET-99201   | SO-2026-88 | WH01 | RET-QUAR-01     |     2,0 | Vỡ vỏ hộp  | [Định đoạt]|
| RMA-20261101-02| SPX-RET-44109   | SO-2026-91 | WH01 | RET-QUAR-02     |     1,0 | Nguyên vẹn | [Nhập lại]|
+--------------------------------------------------------------------------------------------------+

[DRAWER ĐỊNH ĐOẠT HÀNG HOÀN (DISPOSITION DRAWER - 560px)]
+------------------------------------------------------------------+
| KIỂM ĐỊNH & ĐỊNH ĐOẠT KIỆN HOÀN: RMA-20261101-01             [✕] |
+------------------------------------------------------------------+
| Sản phẩm: SKU-001 • Sữa tươi tiệt trùng 1L (SL: 2,0 Thùng)       |
| Vị trí biệt trữ hiện tại: RET-QUAR-01                            |
| 1. Phân loại tình trạng vật lý (Condition Grading):              |
|    ( ) GOOD - Nguyên vẹn, đủ tem mác                             |
|    (•) DAMAGED_BOX - Hàng tốt nhưng móp/rách vỏ hộp ngoài        |
|    ( ) DEFECTIVE - Lỗi kỹ thuật / Hết hạn / Hư hỏng bên trong    |
| 2. Quyết định định đoạt tồn kho (Disposition Action):            |
|    [•] REFURBISH_REPACK (Tân trang thay vỏ hộp -> Nhập khả dụng) |
|        Vật tư bao bì xuất kèm: [PKG-BOX-01 (Hộp carton) v] SL:[2]|
|        Vị trí đích sau tân trang: [A-01-02-01 (Kệ khả dụng) v]   |
|    [ ] RESTOCK_AVAILABLE (Chuyển thẳng về Tồn khả dụng)          |
|    [ ] RTV_VENDOR (Chuyển khu RTV chờ xuất trả Nhà cung cấp)     |
|    [ ] SCRAP_DISPOSAL (Xuất hủy hàng hỏng - Yêu cầu Quản lý duyệt)|
| Ghi chú kiểm định: [Thay 2 vỏ thùng carton mới đạt chuẩn QC]     |
+------------------------------------------------------------------+
|                           [Hủy bỏ]  [✓ Thực thi & Ghi sổ Sổ cái] |
+------------------------------------------------------------------+
```

- **Bảng dữ liệu & Cột số học (`tabular-nums`):**
  - `SL Hoàn` (Right, `tabular-nums`), `SL Bao bì tiêu hao` (Right, `tabular-nums`), `Đơn giá vốn hoàn (UnitCost)` (Right, `tabular-nums`, ẩn nếu thiếu `inventory.cost.read`).
- **Tích hợp API thật (`FACT`):**
  - Danh sách: `GET /api/v1/customer-returns` (tương thích `/api/ImportReceipts/customer-returns`)
  - Tiếp nhận quét mã: `POST /api/v1/customer-returns/intake` (`/api/ImportReceipts/rma-intake-check`)
  - Định đoạt & Ghi sổ: `POST /api/v1/customer-returns/{id}/dispose` (`/api/ImportReceipts/execute-rtv-refurbish` với `ExecuteStockMutation = true`).

---

### `SCR-09`: Tồn Kho 360, Truy Vết Phả Hệ & Đối Chiếu Sổ Cái (`Inventory` + `Traceability` + `Reconciliation`)
- **Mục đích:** Cung cấp bức tranh tồn kho 9 chiều chính xác tuyệt đối, truy vết ngược/xuôi theo Lô/Sê-ri (kèm mô phỏng Cây thu hồi `Recall Tree`), và đối chiếu tự động giữa bảng Số dư (`InventoryStocks`) với Sổ cái bất biến (`InventoryTransactions`).
- **Đối tượng sử dụng:** Thủ kho, Kế toán kho, Kiểm toán nội bộ, Quản lý chất lượng.
- **Route & File nguồn:** `/inventory` (`1,394 LOC`), `/inventory-reconciliation` (`1,307 LOC`), `/inventory-traceability` (`719 LOC`), `/inventory-movements` (`~800 LOC`), `/inventory-locks` (`~700 LOC`), `/inventory-reversals` (`~500 LOC`).
- **Kế hoạch tách God-Component (`Inventory.tsx` 1,394 LOC & `InventoryReconciliation.tsx` 1,307 LOC):**
  - Tách `Inventory.tsx` thành 6 tab-components độc lập trong `modules/inventory/`: `StockBalancesTab.tsx` (`260 LOC`), `LedgerTransactionsTab.tsx` (`250 LOC`), `InOutPeriodReportTab.tsx` (`240 LOC`), `ExpiryAgingFefoTab.tsx` (`230 LOC`), `OperationalVelocityTab.tsx` (`240 LOC`), kết hợp `InventoryBuckets.tsx` (`317 LOC`).
  - Tách `InventoryReconciliation.tsx` thành: `ReconciliationSummaryTable.tsx` (`310 LOC`), `SkuDiscrepancyInvestigator.tsx` (`350 LOC`), và `WhatIfSimulationPanel.tsx` (`280 LOC`).

---

### `SCR-10`: Bàn Kiểm Kê Chu Kỳ & Duyệt Chênh Lệch (`Stocktakes.tsx` — Decomposed from 1,568 LOC)
- **Mục đích:** Tổ chức kiểm kê định kỳ hoặc kiểm kê mù (`Blind Count`), hỗ trợ đếm 2 vòng độc lập (`Attempt 1 & Attempt 2`), tính giá trị chênh lệch theo giá vốn WAC, kiểm tra chính sách tự động duyệt (`Auto-Approval Preview`) và ghi sổ điều chỉnh tồn kho nguyên tử.
- **Đối tượng sử dụng:** Nhân viên kiểm kê (PDA), Thủ kho, Quản lý kho, Kế toán.
- **Route & File nguồn:** `/stocktakes` — [`Stocktakes.tsx`](file:///d:/ERP_KHO/frontend/src/pages/Stocktakes.tsx) (`1,568 LOC`).
- **Kế hoạch tách God-Component (`Stocktakes.tsx` 1,568 LOC → 5 files `< 340 LOC`):**
  1. `pages/Stocktakes.tsx` (`260 LOC`)
  2. `modules/inventory/StocktakeListTable.tsx` (`290 LOC`)
  3. `modules/inventory/StocktakeCreateModal.tsx` (`280 LOC`)
  4. `modules/inventory/StocktakeCountingWorksheet.tsx` (`340 LOC` — ẩn cột `SL Sổ sách` khi `isBlindCount === true`)
  5. `modules/inventory/StocktakeVarianceApprovalDrawer.tsx` (`310 LOC` — ẩn cột `Giá trị lệch (VND)` nếu không có `inventory.cost.read`)

---

### `SCR-11`: Dữ Liệu Nền, Cấu Trúc Vị Trí 5 Cấp & Bản Đồ Nhiệt Kho 2D (`Products` + `WarehouseLocations` + `WarehouseMap`)
- **Mục đích:** Quản lý danh mục SKU (Đa ĐVT, Mã vạch, Ánh xạ sàn TMĐT, Combo/Kit) và sơ đồ không gian vật lý của kho (`Zone → Aisle → Rack → Level → Bin`) kèm bản đồ nhiệt công suất lấp đầy kệ.
- **Đối tượng sử dụng:** Quản lý kho, Quản trị dữ liệu nền.
- **Route & File nguồn:** `/products` (`1,304 LOC`), `/warehouse-structure` (`879 LOC`), `/warehouse-map` (`489 LOC`).
- **Kế hoạch tách God-Component (`Products.tsx` 1,304 LOC → 4 files `< 350 LOC`):**
  1. `pages/Products.tsx` (`220 LOC`)
  2. `modules/catalog/ProductMasterTab.tsx` (`350 LOC`)
  3. `modules/catalog/CategoryAndUomConversionTab.tsx` (`320 LOC`)
  4. `modules/catalog/ChannelSkuMappingAndBundleTab.tsx` (`340 LOC`)

---

### `SCR-12`: Trung Tâm Quản Trị Quyền (IAM), Phạm Vi Kho & Bảo Mật (`Permissions.tsx` — Decomposed from 2,280 LOC)
- **Mục đích:** Quản trị toàn diện Người dùng, Bảo mật phiên đăng nhập, Vai trò (Roles), Ma trận 110+ Quyền hạn (Permission Matrix), Phạm vi Kho được phép truy cập (`Warehouse Scope`) và Nhật ký kiểm toán phân quyền (`AuditLogs`).
- **Đối tượng sử dụng:** Quản trị viên hệ thống (`Admin`).
- **Route & File nguồn:** `/permissions` — [`Permissions.tsx`](file:///d:/ERP_KHO/frontend/src/pages/Permissions.tsx) (`2,280 LOC` — **God Component lớn nhất hệ thống**).

#### Wireframe ASCII (`Desktop 1440x900`)
```text
+--------------------------------------------------------------------------------------------------+
| QUẢN TRỊ HỆ THỐNG / PHÂN QUYỀN (IAM) & BẢO MẬT PHIÊN                     [+ Tạo Người Dùng Mới]  |
| Tabs: [1. Người dùng & Phiên] [2. Vai trò & Ma trận Quyền] [3. Phạm vi Kho] [4. Nhật ký Kiểm toán]|
+--------------------------------------------------------------------------------------------------+
| [Người dùng hoạt động: 24] [Tài khoản bị khóa: 1] [Phiên đăng nhập mở: 31] [Vai trò RBAC: 5]     |
+---------------------------------------+----------------------------------------------------------+
| DANH SÁCH VAI TRÒ (ROLES)             | MA TRẬN PHÂN QUYỀN THEO PHÂN HỆ: [Quản lý kho (Manager)] |
| • Quản trị viên (Admin)       [112/112| [🔍 Tìm nhanh mã quyền hoặc tên nghiệp vụ...]            |
| • Quản lý kho (Manager) ●     [ 84/112| Nhóm Nghiệp Vụ   | Xem (Read) | Tạo/Sửa | Duyệt | Ghi Sổ |
| • Kế toán kho (Accountant)    [ 38/112| Phiếu nhập kho   |    [✓]     |   [✓]   |  [✓]  |  [✓]   |
| • Nhân viên kho (Staff)       [ 29/112| Giá vốn & Lãi gộp|    [✓]     |   [—]   |  [—]  |  [—]   |
| • Người xem (Viewer)          [ 18/112| Hàng hoàn (RMA)  |    [✓]     |   [✓]   |  [✓]  |  [✓]   |
| [+ Tạo Vai trò mới]                   |                               [💾 Lưu Ma trận Phân quyền]|
+---------------------------------------+----------------------------------------------------------+
```

- **Kế hoạch tách God-Component (`Permissions.tsx` 2,280 LOC → 6 files `< 380 LOC`):**
  1. `pages/Permissions.tsx` (`210 LOC` — Shell, Security KPIs & Tab Router)
  2. `modules/governance/UsersAndSessionsTab.tsx` (`380 LOC` — Danh sách user, khóa tài khoản, ép đổi mật khẩu, ngắt phiên)
  3. `modules/governance/RolePermissionMatrixTab.tsx` (`380 LOC` — Ma trận checkbox phân nhóm theo 8 Module)
  4. `modules/governance/UserWarehouseScopeTab.tsx` (`310 LOC` — Cấp/thu hồi quyền truy cập từng kho `WH01`, `WH02`...)
  5. `modules/governance/PermissionAuditLogTab.tsx` (`290 LOC` — Bảng tra cứu lịch sử thay đổi quyền)
  6. `modules/governance/SecurityOverviewTab.tsx` (`260 LOC` — Biểu đồ & cảnh báo bảo mật tài khoản)

---

## 3. Tổng Hợp Kế Hoạch Xóa Bỏ Toàn Bộ God-Components (`> 500 LOC`)

| STT | File Hiện Tại (`AS-IS`) | Số Dòng Hiện Tại | Mục Tiêu Sau Tách (`TO-BE`) | Số Sub-Components Tách Ra |
|---|---|---:|---:|---:|
| 1 | `pages/Permissions.tsx` | `2,280 LOC` | `210 LOC` (Container) | 5 Tab Components (`260–380 LOC` mỗi file) |
| 2 | `pages/ImportReceipts.tsx` | `1,991 LOC` | `280 LOC` (Container) | 5 Domain Components (`290–350 LOC` mỗi file) |
| 3 | `pages/Stocktakes.tsx` | `1,568 LOC` | `260 LOC` (Container) | 4 Domain Components (`280–340 LOC` mỗi file) |
| 4 | `pages/SaasBilling.tsx` | `1,455 LOC` | `240 LOC` (Container) | 4 Billing Components (`PlanSelector`, `VietQrModal`, `UsageQuotaBar`, `InvoiceHistoryTable`) |
| 5 | `pages/ExportReceipts.tsx` | `1,417 LOC` | `290 LOC` (Container) | 3 Domain Components (`280–360 LOC` mỗi file) |
| 6 | `pages/Inventory.tsx` | `1,394 LOC` | `230 LOC` (Container) | 5 Tab Components (`230–260 LOC` mỗi file) |
| 7 | `pages/InventoryReconciliation.tsx` | `1,307 LOC` | `250 LOC` (Container) | 3 Investigator Components (`280–350 LOC` mỗi file) |
| 8 | `pages/DockYard.tsx` | `1,305 LOC` | `260 LOC` (Container) | 3 Components (`AppointmentBoard`, `GateCheckInDrawer`, `YardSlotGrid`) |
| 9 | `pages/Products.tsx` | `1,304 LOC` | `220 LOC` (Container) | 3 Tab Components (`320–350 LOC` mỗi file) |
| 10 | `pages/PackingSessions.tsx` | `1,087 LOC` | `240 LOC` (Container) | 3 Tab Components (`280–320 LOC` mỗi file) |
| 11 | `pages/BusinessPartners.tsx` | `1,062 LOC` | `250 LOC` (Container) | 3 Components (`PartnerList`, `PartnerFormDrawer`, `ThreePlContractTab`) |
| 12 | `pages/OutboundDemand.tsx` | `1,021 LOC` | `240 LOC` (Container) | 2 Components (`SalesOrderDemandTable`, `BackorderRecoveryPanel`) |
| 13 | `pages/StockTransfers.tsx` | `1,018 LOC` | `270 LOC` (Container) | 2 Components (`StockTransferList`, `TransferReceiveReturnDrawer`) |
