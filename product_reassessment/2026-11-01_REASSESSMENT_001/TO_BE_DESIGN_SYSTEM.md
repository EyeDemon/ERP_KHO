# TO-BE DESIGN SYSTEM — Kiến Trúc Token 3 Tầng, Thư Viện Component Kho Vận & Quy Chuẩn In Ấn

**Document ID:** Deliverable L (`TO_BE_DESIGN_SYSTEM.md`)  
**Run ID:** `2026-11-01_REASSESSMENT_001`  
**Core Files Extended:** [`frontend/src/ui/production-ui.css`](file:///d:/ERP_KHO/frontend/src/ui/production-ui.css) & [`frontend/src/ui/ProductionUi.tsx`](file:///d:/ERP_KHO/frontend/src/ui/ProductionUi.tsx)  
**Target Standards:** WCAG 2.1 AA, `0px` Horizontal Overflow (`1440x900` & `390x844`), 100% Vietnamese Diacritic & `tabular-nums` Compliance

---

## 1. Kiến Trúc Design Token 3 Tầng (`Primitive → Semantic → Component`)

Để nâng cấp [`production-ui.css`](file:///d:/ERP_KHO/frontend/src/ui/production-ui.css#L1-L32) từ các biến phẳng thành hệ thống Design System chuẩn công nghiệp mà **không làm gãy bất kỳ class `.ui-*` hiện có nào**, toàn bộ token được tổ chức theo 3 tầng trong `frontend/src/ui/tokens.css` (được `@import` ở đầu `production-ui.css`):

```css
/* ==========================================================================
   LAYER 1: PRIMITIVE TOKENS (Bảng màu, khoảng cách & cỡ chữ nguyên bản)
   ========================================================================== */
:root {
  /* Slate (Neutral Structural Palette) */
  --p-slate-50:  #F8FAFC;
  --p-slate-100: #F1F5F9;
  --p-slate-200: #E2E8F0;
  --p-slate-300: #CBD5E1;
  --p-slate-500: #64748B;
  --p-slate-600: #475569;
  --p-slate-700: #334155;
  --p-slate-800: #1E293B;
  --p-slate-900: #0F172A;

  /* Industrial Blue (Primary Action & Focus) */
  --p-blue-50:   #EFF6FF;
  --p-blue-100:  #DBEAFE;
  --p-blue-600:  #2563EB;
  --p-blue-700:  #1D4ED8;
  --p-blue-800:  #1E40AF;
  --p-blue-900:  #1E3A8A;

  /* Emerald (Available Stock / Posted / Success) */
  --p-emerald-50:  #ECFDF5;
  --p-emerald-200: #A7F3D0;
  --p-emerald-600: #059669;
  --p-emerald-700: #047857;
  --p-emerald-900: #064E3B;

  /* Amber (Reserved / QC Hold / Near Expiry / Warning) */
  --p-amber-50:  #FFFBEB;
  --p-amber-100: #FEF3C7;
  --p-amber-500: #F59E0B;
  --p-amber-800: #92400E;
  --p-amber-900: #78350F;

  /* Rose / Red (Quarantine / Damaged / Expired / Conflict 409) */
  --p-red-50:    #FEF2F2;
  --p-red-200:   #FECACA;
  --p-red-600:   #DC2626;
  --p-red-700:   #B91C1C;
  --p-red-900:   #7F1D1D;

  /* Violet (In-Transit / Wave / 3PL / RMA Quarantine) */
  --p-violet-50:  #F5F3FF;
  --p-violet-200: #DDD6FE;
  --p-violet-700: #6D28D9;

  /* Typography Primitives */
  --p-font-sans: 'Inter', 'Be Vietnam Pro', 'Plus Jakarta Sans', ui-sans-serif, system-ui, -apple-system, 'Segoe UI', sans-serif;
  --p-font-mono: 'JetBrains Mono', 'SFMono-Regular', Consolas, 'Liberation Mono', monospace;

  /* Spacing & Radius Scale */
  --p-space-1: 4px;
  --p-space-2: 8px;
  --p-space-3: 12px;
  --p-space-4: 16px;
  --p-space-5: 24px;
  --p-space-6: 32px;
  --p-radius-sm: 6px;
  --p-radius-md: 8px;
  --p-radius-lg: 12px;
}

/* ==========================================================================
   LAYER 2: SEMANTIC TOKENS (Ánh xạ ngữ nghĩa vận hành kho & tương thích ngược)
   ========================================================================== */
:root {
  --ui-background:         var(--p-slate-50);
  --ui-foreground:         var(--p-slate-900);
  --ui-surface:            #FFFFFF;
  --ui-surface-muted:      var(--p-slate-100);
  --ui-primary:            var(--p-slate-700);
  --ui-primary-hover:      var(--p-slate-800);
  --ui-primary-foreground: #FFFFFF;
  --ui-brand-action:       var(--p-blue-800);
  --ui-brand-action-hover: var(--p-blue-900);
  --ui-secondary:          var(--p-slate-600);
  --ui-muted:              var(--p-slate-600);
  --ui-subtle:             var(--p-slate-500);
  --ui-border:             var(--p-slate-200);
  --ui-border-strong:      var(--p-slate-300);
  --ui-ring:               var(--p-blue-700);

  /* Semantic Statuses (All >= 4.5:1 Contrast Ratio) */
  --ui-success:            var(--p-emerald-700);
  --ui-success-soft:       var(--p-emerald-50);
  --ui-warning:            var(--p-amber-800);
  --ui-warning-soft:       var(--p-amber-50);
  --ui-danger:             var(--p-red-700);
  --ui-danger-soft:        var(--p-red-50);
  --ui-info:               #0369A1;
  --ui-info-soft:          #E0F2FE;
  --ui-quarantine:         var(--p-violet-700);
  --ui-quarantine-soft:    var(--p-violet-50);
}

/* ==========================================================================
   LAYER 3: COMPONENT TOKENS (Token chuyên biệt cho Bảng, Máy quét, Drawer)
   ========================================================================== */
:root {
  --table-header-bg:       var(--ui-surface-muted);
  --table-header-fg:       var(--ui-secondary);
  --table-row-hover-bg:    var(--p-slate-50);
  --table-cell-padding-y:  10px;
  --table-cell-padding-x:  12px;
  --input-min-height:      40px;
  --input-min-height-pda:  44px;
  --btn-min-height:        38px;
  --btn-min-height-pda:    44px;
  --drawer-width-md:       560px;
  --drawer-width-lg:       760px;
  --scanner-viewport-bg:   var(--p-slate-900);
}
```

---

## 2. Quy Chuẩn Typography Tiếng Việt & Số Học Kho Vận (`tabular-nums`)

### 2.1 Thang Cỡ Chữ & Chiều Cao Dòng (`Line-Height` chống cắt dấu Tiếng Việt)
Chữ tiếng Việt có dấu thanh chồng lên dấu mũ (`ể`, `ẫ`, `ự`, `ỗ`, `ắ`). Tuyệt đối không dùng `line-height < 1.25` kết hợp `overflow: hidden` trên tiêu đề hoặc nhãn:

| Cấp độ Typography | Class / Thẻ | Cỡ chữ (`Desktop` / `PDA`) | Độ đậm (`Weight`) | `Line-Height` | Quy tắc áp dụng |
|---|---|---|---|---|---|
| **Tiêu đề trang (`Page Title`)** | `.ui-page-header h1` | `20px` / `22px` | `650` | `1.3` | Hiển thị tên Không gian làm việc rõ ràng |
| **Tiêu đề khối (`Card Title`)** | `.ui-card-title`, `h2` | `16px` / `18px` | `650` | `1.35` | Phân tách các khu vực trong màn hình |
| **Tiêu đề phụ (`Subcard / Legend`)** | `.ui-subcard-title`, `legend` | `13.5px` / `15px` | `700` | `1.35` | Tiêu đề nhóm trường nhập liệu |
| **Văn bản thân bảng (`Body / Table`)** | `body`, `td` | `14px` / `16px` | `400–500` | `1.5` | Chuẩn đọc lâu không mỏi mắt cho nhân viên kho |
| **Tiêu đề cột bảng (`Table Header`)** | `thead th` | `12px` / `12px` | `700` | `1.35` | In hoa (`uppercase`), `letter-spacing: 0.025em`, `white-space: nowrap` |
| **Nhãn trường (`Field Label`)** | `label`, `.ui-toolbar-field > span` | `12px` / `14px` | `650` | `1.35` | Luôn đặt **phía trên** `<input>` / `<select>` |
| **Mã kỹ thuật (`Technical Code`)** | `code`, `.ui-code` | `13px` (`0.92em`) | `600` | `1.4` | Dùng `--p-font-mono` cho `SKU`, `Lot`, `Serial`, `Bin`, `AWB`, `CorrelationId` |

### 2.2 Kỷ Luật Hiển Thị Số Lượng & Tiền Tệ (`Numeric & Currency Formatting`)
1. **Bắt buộc `font-variant-numeric: tabular-nums` và `text-align: right`** (`.ui-cell-numeric`) cho mọi cột:
   - Số lượng tồn kho (`OnHand`, `Reserved`, `Available`, `Allocated`, `Damaged`) — hiển thị tối đa 4 chữ số thập phân có nghĩa theo chuẩn `decimal(19,4)`.
   - Đơn giá vốn bình quân (`WAC`), Đơn giá mua/bán, Thành tiền, Tiền thu hộ `COD`, Cước lưu kho `3PL`, Giá trị chênh lệch kiểm kê (`VND` định dạng `vi-VN` kèm ký hiệu `₫`).
2. **Không bao giờ ngắt dòng giữa con số và đơn vị:** Luôn đi kèm `white-space: nowrap`.

---

## 3. Bảng Màu Ngữ Nghĩa Cho Trạng Thái Kho & Chứng Từ (WCAG 2.1 AA)

Tất cả các cặp màu nền/chữ của `<UiBadge>` đều đạt tỷ lệ tương phản **>= 4.8:1** (vượt chuẩn WCAG 2.1 AA):

| Nhóm Nghiệp Vụ | Trạng Thái (`Status`) | Nhãn Tiếng Việt Chuẩn | Tone `<UiBadge>` | Màu Nền (`Background`) | Màu Chữ (`Foreground`) | Viền (`Border`) |
|---|---|---|---|---|---|---|
| **Tồn kho 9 chiều** | `Available` | `Khả dụng` | `success` | `#ECFDF5` | `#047857` (`5.4:1`) | `#A7F3D0` |
| | `Reserved` / `Allocated` | `Đã giữ hàng` / `Đã phân bổ` | `info` | `#E0F2FE` | `#0369A1` (`5.8:1`) | `#7DD3FC` |
| | `QcHold` | `Chờ kiểm định QC` | `warning` | `#FEF3C7` | `#78350F` (`7.2:1`) | `#F59E0B` |
| | `Quarantine` | `Đang biệt trữ (RMA/QC)` | `quarantine` *(Mới)* | `#F5F3FF` | `#6D28D9` (`6.1:1`) | `#DDD6FE` |
| | `Damaged` / `Locked` | `Hư hỏng` / `Đang khóa` | `danger` | `#FEF2F2` | `#B91C1C` (`5.9:1`) | `#FECACA` |
| **Hạn dùng FEFO** | `GOOD` (`> 90 ngày`) | `Hạn dùng tốt` | `success` | `#ECFDF5` | `#047857` | `#A7F3D0` |
| | `NEAR_EXPIRY` (`<= 90 ngày`) | `Sắp hết hạn (Ưu tiên FEFO)` | `warning` | `#FEF3C7` | `#78350F` | `#F59E0B` |
| | `EXPIRED` (`<= 0 ngày`) | `Đã hết hạn (Chặn xuất)` | `danger` | `#FEF2F2` | `#B91C1C` | `#FECACA` |
| **Chứng từ Kho** | `Draft` | `Nháp` | `neutral` | `#F1F5F9` | `#475569` (`5.1:1`) | `#CBD5E1` |
| | `Approved` / `InProgress` | `Đã duyệt` / `Đang thực hiện` | `info` | `#E0F2FE` | `#0369A1` | `#7DD3FC` |
| | `Posted` / `Completed` | `Đã ghi sổ` / `Hoàn tất` | `success` | `#ECFDF5` | `#047857` | `#A7F3D0` |
| | `Cancelled` / `Rejected` | `Đã hủy` / `Từ chối` | `danger` | `#FEF2F2` | `#B91C1C` | `#FECACA` |

---

## 4. Đặc Tả Các Component Mở Rộng Trong `ProductionUi.tsx`

Bên cạnh 13 component nền tảng hiện có trong [`ProductionUi.tsx`](file:///d:/ERP_KHO/frontend/src/ui/ProductionUi.tsx#L3-L281), bổ sung **6 Component Kho Vận Chuyên Dụng** để tái sử dụng thống nhất trên toàn bộ 12 màn hình:

### 4.1 `<UiScanInputBar>` — Thanh Quét Mã Vạch Đa Kênh (USB HID / PDA Broadcast / Camera)
- **Props Contract:**
  ```tsx
  export interface UiScanInputBarProps {
    stepLabel: string;                 // VD: "Bước 1/2: Quét Mã Vị Trí Kệ (Bin)"
    placeholder: string;               // VD: "Quét hoặc nhập mã vị trí (VD: A-01-02-01)..."
    expectedPrefix?: string;           // Cảnh báo sớm nếu quét nhầm loại mã
    autoAdvance?: boolean;             // Tự động chuyển sang bước kế tiếp sau khi quét đúng
    busy?: boolean;
    lastScanStatus?: 'idle' | 'success' | 'warning' | 'error';
    lastScanMessage?: string;
    onScanSubmit: (barcode: string) => void | Promise<void>;
    onOpenCamera?: () => void;
  }
  ```
- **Hành vi Chuẩn:**
  1. Tự động nhận diện luồng gõ phím tốc độ cao từ súng bắn mã vạch (`inter-keystroke < 35ms` kết thúc bằng `Enter`) để không bị mất ký tự khi người dùng chưa kịp click chuột vào ô input.
  2. Phát phản hồi âm thanh qua `AudioContext` (tiếng *Bíp 880Hz 80ms* khi `lastScanStatus === 'success'`; tiếng *Bíp trầm kép 220Hz 250ms* khi `error`) và rung thiết bị `navigator.vibrate(200)` trên PDA Android.

### 4.2 `<UiErrorBanner>` — Khung Thông Báo Lỗi Chuẩn Máy Đọc (`Spec 18` & `FE-003`)
- **Props Contract:**
  ```tsx
  export interface UiErrorBannerProps {
    error: unknown;                    // Đối tượng lỗi trả về từ apiClient
    fallbackMessage?: string;
    onRetryOrReload?: () => void;      // Bắt buộc truyền cho màn hình có xử lý 409 Conflict
    onDismiss?: () => void;
  }
  ```
- **Hành vi Chuẩn:**
  1. Tự động trích xuất `status`, `errorCode` (`extractConflictCode(error)`), và `correlationId` (`localStorage.getItem('lastCorrelationId')`).
  2. Nếu `status === 409`: Hiển thị hộp đỏ kèm nút **`[🔄 Tải lại dữ liệu mới nhất]`** gọi `onRetryOrReload()`.
  3. Nếu `status === 422`: Hiển thị hộp vàng/đỏ kèm mã `[errorCode]` và hướng dẫn nghiệp cụ thể.
  4. Luôn có nút phụ **`[📋 Sao chép mã chẩn đoán]`** để gửi đội hỗ trợ kỹ thuật qua Zalo.

### 4.3 `<UiDataGrid>` — Bảng Dữ Liệu Kho Vận Mật Độ Cao
- Tích hợp sẵn `<UiTableScroll>`, cố định dòng tiêu đề (`position: sticky; top: 0`), cố định cột hành động bên phải (`.ui-sticky-action`), tự động căn phải các cột khai báo `align: 'right'` với `tabular-nums`, và hiển thị thanh phân trang chuẩn (`pageNumber`, `pageSize` tối đa `100` theo `API-004`).

### 4.4 `<UiSplitWorkbench>` — Bố Cục Bàn Làm Việc 2 Cột Không Tràn Ngang
- Trên Desktop (`> 1024px`): Chia 2 cột tỷ lệ linh hoạt (`minmax(0, 1.3fr) minmax(380px, 0.9fr)`), đảm bảo `min-width: 0` ở cả 2 cột để bảng bên trong cuộn ngang cục bộ mà **không bao giờ đẩy tràn ngang trang (`0px` overflow)**.
- Trên Tablet/Mobile (`<= 1024px`): Tự động xếp chồng 1 cột (`grid-template-columns: minmax(0, 1fr)`).

### 4.5 `<UiStepper>` & `<UiStatusTimeline>`
- `<UiStepper>`: Thanh tiến trình ngang dùng cho 5 bước Onboarding, các bước Nhận hàng → QC → Ghi sổ, hoặc Kiểm kê Lần 1 → Lần 2 → Duyệt.
- `<UiStatusTimeline>`: Dòng thời gian dọc dùng cho Lịch sử hành trình giao hàng (`ShipmentTrackingEvents`), Lịch sử xe ra vào cổng (`DockAppointmentEvents`), và Truy vết phả hệ Lô/Sê-ri (`InventoryTraceability`).

---

## 5. Quy Chuẩn In Ấn Tem Nhiệt & Chứng Từ Kế Toán (`Print & Thermal Standards`)

### 5.1 Tem Nhiệt Vận Đơn AWB Chuẩn Thương Mại Điện Tử (`100mm x 150mm` / `4x6 inch`)
- **Định dạng luồng dữ liệu (`PROJECT_RULE` Layer 3 & Skill `pdf`):** Phải sinh luồng nhị phân `%PDF-1.4` thật từ Backend (hoặc vùng in `@page { size: 100mm 150mm; margin: 3mm }`), nghiêm cấm đổi đuôi chuỗi văn bản/HTML thành `.pdf` giả.
- **Bố cục chuẩn `100x150mm`:**
  1. **Header (`18mm`):** Logo ĐVVC + Mã phân tuyến chia chọn chữ cực lớn (`24pt Bold Mono`) + Nhãn `BẢN IN GỐC` (hoặc `BẢN IN LẠI LẦN N` nếu gọi qua quyền `export_receipt.reprint_awb`).
  2. **Khối Mã vạch AWB (`28mm`):** Mã vạch `Code128` độ phân giải cao + Mã `QR` góc phải + Chuỗi số AWB (`JetBrains Mono 14pt`).
  3. **Khối Người gửi / Người nhận (`38mm`):** Tên, SĐT (ẩn một phần nếu in bản lưu), Địa chỉ 3 cấp Tỉnh/Huyện/Xã.
  4. **Khối Thu hộ COD (`22mm`):** Khung viền đậm hiển thị số tiền thu hộ **`COD: 450.000 ₫`** (`tabular-nums 18pt Bold`).
  5. **Khối Hàng hóa & Chữ ký bưu tá (`44mm`):** Danh sách SKU + Số lượng + Ô chữ ký xác nhận của bưu tá và mã phiếu xuất nội bộ (`PXK-...`).

### 5.2 Tem Nhiệt Vị Trí Kệ (`Location Bin`) & Đơn Vị Xử Lý (`SSCC / HU`) (`100mm x 50mm`)
- Dùng cho dán thanh ngang kệ kho (`Zone-Aisle-Rack-Level-Bin`) và dán ngoài thùng/pallet (`HU-...`):
  - Cỡ chữ mã vị trí `A-01-02-01` đạt `32pt Bold Mono` để nhân viên lái xe nâng quét và đọc được từ khoảng cách **2,5 – 3 mét**.
  - Kèm mũi tên chỉ hướng tầng kệ (`↑ TẦNG TRÊN` / `↓ TẦNG DƯỚI`) và mã vạch `Code128` + `QR`.

### 5.3 Phiếu Nhập / Xuất / Kiểm Kê Chuẩn Kế Toán A4 (`@page { size: A4; margin: 10mm }`)
- Sử dụng [`ReceiptPrintPreview.tsx`](file:///d:/ERP_KHO/frontend/src/components/ReceiptPrintPreview.tsx) và [`UiPrintSignatures`](file:///d:/ERP_KHO/frontend/src/ui/ProductionUi.tsx#L262-L280):
  - Tự động ẩn toàn bộ Sidebar, Topbar, Bottom Nav, bộ lọc `.ui-toolbar` và các nút thao tác khi in (`@media print`).
  - Hiển thị đầy đủ Quốc hiệu / Thông tin Kho & Công ty, Mã vạch chứng từ, Bảng chi tiết dòng hàng (ẩn cột Đơn giá/Thành tiền nếu in bản dành cho Thủ kho không có quyền `inventory.cost.read`), Tổng cộng bằng số & bằng chữ, và **4 cột chữ ký bắt buộc** (`Người lập phiếu`, `Người giao / nhận hàng`, `Thủ kho`, `Quản lý kho / Kế toán`) không bao giờ bị cắt ngang trang (`page-break-inside: avoid`).
