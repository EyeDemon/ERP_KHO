# BÁO CÁO KIỂM TOÁN TOÀN DIỆN UX/UI & QA THỰC CHIẾN — HỆ THỐNG ERP WMS THƯƠNG MẠI ĐIỆN TỬ SAAS (`ERP_KHO v3.0`)

- **Ngày kiểm toán:** `2026-10-25`
- **Môi trường kiểm thử thực tế:**
  - Frontend SPA (React 19 + Vite 8): `http://localhost:5173`
  - Backend API (.NET 10 + SQL Server `ERP_KHO`): `http://localhost:5260`
  - Tài liệu Đặc tả Kỹ thuật đối chiếu (Notion):
    - Trang gốc: `ERP WMS — Tài liệu Thiết kế Kỹ thuật Hệ thống` (`3de558fb617480b98a4ae4b7694b3c63`)
    - Đặc tả ngôn ngữ thiết kế: `Spec 217 — ERP/WMS Design System & Visual Language Specification` (`3de558fb61748111bfe1e8311acd997d`)
    - Danh mục màn hình & độ phủ: `Spec 229 — Screen Inventory & Coverage Matrix` (`3de558fb617481e683aeca0f8def3e96`)
    - *(Tuân thủ tuyệt đối lệnh cấm: Không mở/đọc/trích dẫn 4 trang `OWN-OOAD-001..004`)*
- **Khung nhìn (Viewports) đã đo tự động & chụp ảnh:**
  1. **Desktop Quản lý / Chủ shop / Kế toán:** `1440×900`
  2. **Tablet Giám sát / Điều vận:** `768×1024`
  3. **Thiết bị cầm tay / Súng quét PDA Nhân viên kho:** `390×844`
- **Tệp dữ liệu đo tự động đính kèm:**
  - JSON chi tiết 36 màn hình: [ux_audit_metrics.json](file:///d:/ERP_KHO/ux_audit_metrics.json) (Bản sao Artifact: [ux_audit_metrics.json](file:///C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb/ux_audit_metrics.json))
  - CSV tổng hợp điểm & lỗi: [ux_audit_summary.csv](file:///d:/ERP_KHO/ux_audit_summary.csv) (Bản sao Artifact: [ux_audit_summary.csv](file:///C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb/ux_audit_summary.csv))

---

## BƯỚC 0: DANH MỤC TOÀN BỘ MÀN HÌNH (`MASTER SCREEN INVENTORY` — 36 MÀN HÌNH / 5 ĐỢT)

| Đợt | Nhóm Phân Hệ | STT | Đường dẫn (`Route`) | Tên màn hình trên UI | Vai trò chính |
|---|---|---|---|---|---|
| **Đợt 1** | **Xác thực, Thiết lập & Tổng quan** | 1 | `/login` | Đăng nhập hệ thống | Public / Mọi vai trò |
| **Đợt 1** | **Xác thực, Thiết lập & Tổng quan** | 2 | `/onboarding-wizard` | Thiết lập kho nhanh 5 bước | Chủ shop / Admin Tenant / Người mới |
| **Đợt 1** | **Xác thực, Thiết lập & Tổng quan** | 3 | `/` | Tổng quan vận hành (Chế độ Đầy đủ & Rút gọn) | Chủ shop / Quản lý kho / Nhân viên kho |
| **Đợt 2** | **Nhập kho (`Inbound`)** | 4 | `/dock-yard` | Cổng sân bãi & cửa kho | Quản lý kho / Điều vận |
| **Đợt 2** | **Nhập kho (`Inbound`)** | 5 | `/purchase-orders` | Đơn mua (`PO`) | Quản lý kho / Mua hàng |
| **Đợt 2** | **Nhập kho (`Inbound`)** | 6 | `/asns` | ASN dự kiến | Quản lý kho / Nhập kho |
| **Đợt 2** | **Nhập kho (`Inbound`)** | 7 | `/import-receipts` | Phiếu nhập kho & Trạm Nhận hàng/RMA | Nhân viên kho PDA / Quản lý kho |
| **Đợt 2** | **Nhập kho (`Inbound`)** | 8 | `/putaway-tasks` | Cất hàng (`Putaway`) | Nhân viên kho PDA / Quản lý kho |
| **Đợt 3** | **Xuất kho & Đơn hàng (`Outbound`)** | 9 | `/backorders` | Đơn bán & đơn thiếu hàng | Quản lý kho / CSKH |
| **Đợt 3** | **Xuất kho & Đơn hàng (`Outbound`)** | 10 | `/export-receipts` | Phiếu xuất kho | Quản lý kho / Nhân viên kho |
| **Đợt 3** | **Xuất kho & Đơn hàng (`Outbound`)** | 11 | `/stock-reservations` | Giữ hàng (`Reservations`) | Quản lý kho |
| **Đợt 3** | **Xuất kho & Đơn hàng (`Outbound`)** | 12 | `/stock-allocations` | Phân bổ tồn (`Allocations` FEFO/FIFO) | Quản lý kho |
| **Đợt 3** | **Xuất kho & Đơn hàng (`Outbound`)** | 13 | `/picking-tasks` | Lấy hàng (`Picking`) | Nhân viên kho PDA |
| **Đợt 3** | **Xuất kho & Đơn hàng (`Outbound`)** | 14 | `/packing-sessions` | Đóng gói & đơn vị xử lý (`HU`) | Nhân viên đóng gói / PDA |
| **Đợt 3** | **Xuất kho & Đơn hàng (`Outbound`)** | 15 | `/shipments` | Thực hiện giao hàng | Điều vận / Quản lý kho |
| **Đợt 3** | **Xuất kho & Đơn hàng (`Outbound`)** | 16 | `/handover-manifests` | Biên bản bàn giao bưu tá | Nhân viên kho PDA / Điều vận |
| **Đợt 4** | **Tồn kho & Kiểm kê (`Inventory`)** | 17 | `/inventory` | Báo cáo tồn kho (6 Tabs chuyên sâu) | Quản lý kho / Kế toán |
| **Đợt 4** | **Tồn kho & Kiểm kê (`Inventory`)** | 18 | `/inventory-locks` | Khóa / đóng băng tồn kho | Quản lý kho / QA |
| **Đợt 4** | **Tồn kho & Kiểm kê (`Inventory`)** | 19 | `/inventory-movements` | Di chuyển vị trí nội bộ | Nhân viên kho PDA / Quản lý kho |
| **Đợt 4** | **Tồn kho & Kiểm kê (`Inventory`)** | 20 | `/inventory-reversals` | Đảo giao dịch tồn kho | Quản lý kho / Kế toán |
| **Đợt 4** | **Tồn kho & Kiểm kê (`Inventory`)** | 21 | `/inventory-traceability` | Truy vết & phả hệ tồn kho (`Lot/Serial`) | Quản lý kho / QA |
| **Đợt 4** | **Tồn kho & Kiểm kê (`Inventory`)** | 22 | `/inventory-reconciliation` | Đối chiếu tồn kho & Sổ cái | Quản lý kho / Kế toán |
| **Đợt 4** | **Tồn kho & Kiểm kê (`Inventory`)** | 23 | `/stocktakes` | Kiểm kê kho | Nhân viên kho PDA / Quản lý kho |
| **Đợt 4** | **Tồn kho & Kiểm kê (`Inventory`)** | 24 | `/stock-transfers` | Điều chuyển kho | Quản lý kho / Nhân viên kho |
| **Đợt 5** | **Tài chính, Gói cước & Quản trị** | 25 | `/cod-reconciliation` | Đối soát COD 3 chiều | Chủ shop / Kế toán |
| **Đợt 5** | **Tài chính, Gói cước & Quản trị** | 26 | `/cost-valuation` | Giá vốn & kết xuất MISA | Chủ shop / Kế toán |
| **Đợt 5** | **Tài chính, Gói cước & Quản trị** | 27 | `/partner-debts` | Công nợ `AP / AR` | Kế toán / Chủ shop |
| **Đợt 5** | **Tài chính, Gói cước & Quản trị** | 28 | `/saas-billing` | Gói cước SaaS & VietQR | Chủ shop / Admin Tenant |
| **Đợt 5** | **Tài chính, Gói cước & Quản trị** | 29 | `/permissions` | Quản trị quyền `RBAC` & 2FA | Admin Tenant |
| **Đợt 5** | **Tài chính, Gói cước & Quản trị** | 30 | `/approvals` | Hàng đợi phê duyệt | Quản lý kho / Chủ shop |
| **Đợt 5** | **Danh mục gốc (`Master Data`)** | 31 | `/products` | Sản phẩm (`Master SKU`) | Quản lý kho / Chủ shop |
| **Đợt 5** | **Danh mục gốc (`Master Data`)** | 32 | `/warehouses` | Kho hàng | Admin Tenant / Quản lý kho |
| **Đợt 5** | **Danh mục gốc (`Master Data`)** | 33 | `/warehouse-structure` | Cấu trúc vị trí kho (`Zone/Aisle/Rack/Bin`) | Quản lý kho |
| **Đợt 5** | **Danh mục gốc (`Master Data`)** | 34 | `/warehouse-map` | Bản đồ kho & Bản đồ nhiệt (`2D/Heatmap`) | Quản lý kho |
| **Đợt 5** | **Danh mục gốc (`Master Data`)** | 35 | `/warehouse-calendar` | Lịch & ca kho | Quản lý kho |
| **Đợt 5** | **Danh mục gốc (`Master Data`)** | 36 | `/units` & `/business-partners` | Đơn vị tính (`UOM`) & Đối tác (`NCC/Khách/3PL`) | Quản lý kho / Kế toán |

---

## PHẦN I: ĐÁNH GIÁ CHI TIẾT TỪNG ĐỢT MÀN HÌNH (THEO ĐÚNG 8 MỤC CHUẨN)

### ĐỢT 1: XÁC THỰC, THIẾT LẬP KHO NHANH & TỔNG QUAN VẬN HÀNH (`/login`, `/onboarding-wizard`, `/`)

![Màn hình Đăng nhập 1440x900](/C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb/login_1440x900_default.png)
![Màn hình Thiết lập kho nhanh 1440x900](/C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb/onboarding_wizard_1440x900_default.png)
![Màn hình Tổng quan vận hành 1440x900](/C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb/dashboard_1440x900_default.png)
![Màn hình Tổng quan vận hành 390x844 PDA](/C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb/dashboard_390x844_default.png)

#### 1. Tóm tắt & 3 vấn đề lớn nhất của Đợt 1
- **Điểm trung bình Đợt 1:** `/login` đạt **`3.46 / 5.0`**, `/onboarding-wizard` đạt **`2.85 / 5.0`**, `/` (Dashboard) đạt **`2.46 / 5.0`**.
- **3 vấn đề nghiêm trọng nhất:**
  1. **`[🔴 Nghiêm trọng] E6 & W3.3.8`:** Màn `/login` điền sẵn tài khoản `qa_admin` và mật khẩu `QaAdmin@123456!` ngay trong state khởi tạo của bản thương mại ([Login.tsx](file:///d:/ERP_KHO/frontend/src/pages/Login.tsx#L14-L15)), lộ danh sách tenant nội bộ (`TENANT-1`, `TENANT-2`) và thiếu nút Ẩn/Hiện mật khẩu (`Show/Hide Password`).
  2. **`[🔴 Nghiêm trọng] E6, L5 & N8`:** Màn `/` (Tổng quan vận hành — Chế độ Đầy đủ) dài gấp **`3.15 lần`** chiều cao màn hình desktop (`2837px / 900px`) và gấp **`6.48 lần`** trên PDA (`5469px / 844px`); nhồi nhét cả biểu mẫu cấu hình liên kết SKU sàn TMĐT (điền sẵn chuỗi test `"QA-E2E-001"` tại [Dashboard.tsx](file:///d:/ERP_KHO/frontend/src/pages/Dashboard.tsx#L295)) và thẻ `"Trạng thái môi trường"` ở cuối trang.
  3. **`[🔴 Nghiêm trọng] E8 & L2`:** Màn `/onboarding-wizard` ([OnboardingWizard.tsx](file:///d:/ERP_KHO/frontend/src/pages/OnboardingWizard.tsx)) hiển thị cả 5 bước dưới dạng 5 thẻ mở sẵn cùng lúc kèm dữ liệu mẫu cứng (`"Kho Trung tâm Quận 7"`, `"TENANT-SME-2026"`), các nút `"Chuyển sang Bước 2/3/4"` chỉ đổi biến state cục bộ chứ không khóa/mở tuần tự hay lưu trạng thái thật xuống server, không có nút `"Bỏ qua để làm sau"`.

#### 2. Phạm vi kiểm thử Đợt 1
- **Đã kiểm tra:** `/login`, `/onboarding-wizard`, `/` (cả 2 chế độ `"Rút gọn (3 KPI)"` và `"Đầy đủ"`) trên `1440×900`, `768×1024`, `390×844`; kiểm tra luồng điều hướng bàn phím (`Tab`), tương phản màu sắc, bảng KPI và nút hành động.
- **Chưa kiểm tra:** Luồng quên mật khẩu qua email SMTP thật (chưa có nút Quên mật khẩu trên `/login`).

#### 3. Bảng điểm 13 Tiêu chí (Thang 1–5) — Đợt 1

| Tiêu chí | `/login` | `/onboarding-wizard` | `/` (Dashboard) | Nhận xét cốt lõi & Mã quy tắc |
|---|---:|---:|---:|---|
| 1. Phân cấp thị giác | 4 | 3 | 2 | Dashboard Full có `14` thẻ `h2/h3` cạnh tranh ngang nhau (`G1`, `L1`). |
| 2. Bố cục, khoảng trắng, mật độ, độ dài trang | 4 | 4 | 2 | Dashboard dài `3.15x` desktop (`2837px`), `6.48x` mobile (`5469px`) (`L5`). |
| 3. Màu sắc & tương phản | 4 | 4 | 4 | Nền sáng `#f8fafc`, chữ `#0f172a`, badge đạt tương phản WCAG AA (`W1.4.3`). |
| 4. Typography | 4 | 3 | 2 | Dashboard có `59` phần tử chữ `< 12px` (`11px` uppercase label); một số `<th>` cột số căn trái (`E4`). |
| 5. Nhất quán | 4 | 3 | 3 | Hệ thống thẻ `.ui-card` và `.ui-btn` đồng bộ, nhưng kích thước chữ badge không đều (`S1`). |
| 6. UX writing | 3 | 3 | 3 | Còn sót chữ `"Choose File"` tiếng Anh trên `/onboarding-wizard` (`V1`). |
| 7. Khả năng dùng | 3 | 2 | 3 | `/login` thiếu nút Hiện mật khẩu (`W3.3.8`); Wizard 5 bước không dẫn dắt từng bước thật (`E8`). |
| 8. Phù hợp thao tác kho trên PDA (`390×844`) | 3 | 2 | 2 | Topbar mobile cao `156px` (3 dòng), không có Bottom Nav (`M3`, `M4`), `97` nút `< 48px` (`M1`). |
| 9. Truy cập & responsive | 3 | 3 | 2 | Dashboard có `6` link hành động cao `15px–18px` (`< 24px`, vi phạm `W2.5.8`); Sidebar ẩn trên mobile vẫn nhận `Tab` (`W2.4.3`). |
| 10. Độ tin cậy & chuyên nghiệp thương mại | 2 | 2 | 2 | Lộ `qa_admin`, `QA-E2E-001`, `IR-QA-001`, `TENANT-SME-2026`, nút `"Sao chép Chẩn đoán Lỗi 1-Click gửi Zalo"` (`E6`). |
| 11. Trực quan hóa dữ liệu | 3 | 3 | 3 | Có thanh tiến độ công suất khu vực và phễu đơn hàng, nhưng thiếu biểu đồ xu hướng theo ngày/giờ (`E1`). |
| 12. Hiệu năng cảm nhận | 4 | 3 | 3 | Chuyển chế độ Rút gọn/Đầy đủ tức thì, nhưng khi tải lần đầu chỉ hiện `<p>Đang tải...</p>` (`L3`). |
| 13. Dư thừa & trùng lặp | 4 | 2 | 1 | Dashboard nhét cả form Cấu hình liên kết SKU sàn TMĐT (trùng chức năng tích hợp) và bảng Trạng thái môi trường (`N8`). |
| **Điểm trung bình (`/ 5.0`)** | **3.46** | **2.85** | **2.46** | |

#### 4. Ma trận tuân thủ Đợt 1 (Chỉ liệt kê ngoại lệ)
- **`N8` (Thiết kế tối giản): Không đạt** — Dashboard Full hiển thị `14` thẻ cùng lúc, gồm cả form liên kết SKU sàn TMĐT và bảng thông số môi trường kỹ thuật.
- **`W2.5.8` (Mục tiêu chạm tối thiểu `24×24px`): Không đạt** — На Dashboard có `6` liên kết `"Thực thi →"` (`60×15px`) và `"Xử lý →"` (`42×18px`) cao dưới `24px`.
- **`W2.4.3` (Thứ tự lấy nét bàn phím): Không đạt** — Trên khung nhìn `390×844`, thanh menu trái (`.app-sidebar`) dù ẩn ngoài màn hình (`left: -260px`) vẫn nằm trong luồng `Tab` trước nội dung chính.
- **`W3.3.8` (Xác thực dễ tiếp cận): Không đạt** — Ô mật khẩu trên `/login` không có nút Ẩn/Hiện mật khẩu.
- **`M1`, `M3`, `M4` (Chuẩn thiết bị cầm tay PDA): Không đạt** — `89/97` nút cao `36px` (`< 44px/48px`), thanh đầu trang mobile chiếm `156px` chiều dọc, thiếu thanh điều hướng đáy.
- **`E4` (Chuẩn bảng số liệu): Một phần** — Trong bảng `"Trung tâm Điều hành Kho phân tán"` ([Dashboard.tsx](file:///d:/ERP_KHO/frontend/src/pages/Dashboard.tsx)), các ô `<td>` số lượng đã căn phải nhưng tiêu đề cột `<th>` (`Tồn vật lý`, `Khả dụng`, `Đang giữ`, `Công suất`) vẫn căn trái.
- **`E6`, `E8` (Chuẩn thương mại SaaS & Onboarding): Không đạt** — Rò rỉ chuỗi `qa_admin`, `QA-E2E-001`, `IR-QA-001`, `MẶC ĐỊNH`, `TENANT-SME-2026`; Wizard không lưu tiến độ từng bước.
- **`V1` (100% Tiếng Việt): Một phần** — Ô `<input type="file">` trên `/onboarding-wizard` hiện chữ `"Choose File / No file chosen"` của trình duyệt.
- **Tổng kết tuân thủ Đợt 1:**
  - **Đạt (34 mã):** `N1, N2, N4, N5, N6, N7, N9, S1, S2, S3, S4, S5, S6, S7, S8, W1.1.1, W1.3.1, W1.4.1, W1.4.3, W1.4.4, W1.4.10, W1.4.11, W2.1.1, W2.1.2, W2.4.7, W2.5.3, W3.3.1, W3.3.2, G2, G3, G5, G6, L4, V4`.
  - **Không áp dụng (4 mã):** `M5, M6, E7, V3`.

#### 5. Số liệu đo tự động Đợt 1 (Trích xuất từ Script IIFE)
- **`/login` (`1440×900` / `390×844`):** `pageHeightRatio`: `1.00` / `1.00`; `h1Count`: `1`; `under24Count`: `0`; `under44Count`: `4`; `smallTextUnder12px`: `1`; `leakedStrings`: `["qa_admin (prefilled)", "TENANT-1"]`.
- **`/onboarding-wizard` (`1440×900` / `390×844`):** `pageHeightRatio`: `1.00` / `1.69`; `h1Count`: `1`; `under24Count`: `0`; `under44Count`: `45`; `under48Count`: `50`; `smallTextUnder12px`: `13`; `leakedStrings`: `["MẶC ĐỊNH", "TENANT-SME-2026", "Choose File"]`.
- **`/` Dashboard Full (`1440×900` / `390×844`):** `pageHeightRatio`: **`3.15`** (`2837px`) / **`6.48`** (`5469px`); `h1Count`: `1`; `under24Count`: **`6`**; `under44Count`: **`89`**; `under48Count`: **`97`**; `closePairsUnder8px`: **`38`**; `smallTextUnder12px`: **`59`**; `leakedStrings`: `["QA-E2E", "IR-QA-", "MẶC ĐỊNH"]`.

#### 6. Điểm tốt của Đợt 1
- Đã có nút chuyển chế độ **`"Rút gọn (3 KPI)"` / `"Đầy đủ"`** trên đầu màn hình Tổng quan (`/`), giúp giảm ngay chiều dài trang từ `3.15x` xuống `1.10x` khi ở chế độ Rút gọn (`L5`).
- Bảng màu sáng hiện đại (`#f8fafc` / `#ffffff` / `#0f172a`), độ tương phản chữ và huy hiệu trạng thái trên cả 3 màn hình đều đạt chuẩn WCAG AA (`0` lỗi tương phản tự động `W1.4.3`).
- Có liên kết bỏ qua điều hướng (`Skip to main content` — `"Bỏ qua điều hướng sang nội dung chính"`) hoạt động tốt khi nhấn phím `Tab` đầu tiên trên Desktop (`W2.4.1`).

#### 7. Vấn đề chi tiết Đợt 1

1. **`[🔴 Nghiêm trọng] E6 & W3.3.8 — Bắt buộc sửa` — Điền sẵn tài khoản/mật khẩu `qa_admin` trên màn Đăng nhập & thiếu nút Hiện/Ẩn mật khẩu**
   - **Bước tái hiện:** Mở trình duyệt ẩn danh, truy cập `http://localhost:5173/login`.
   - **Thực tế vs Mong đợi:** Ô Tên đăng nhập có sẵn giá trị `"qa_admin"`, ô Mật khẩu có sẵn `"QaAdmin@123456!"`, ô Mã thuê bao liệt kê `"TENANT-1 — Doanh nghiệp Mặc định"`, và không có nút con mắt để kiểm tra mật khẩu đang gõ. Mong đợi: Sản phẩm thương mại phải để trống (`""`) ô tài khoản/mật khẩu (hoặc chỉ hiện nút `"Điền nhanh tài khoản Demo"` khi bật biến môi trường `VITE_DEMO_MODE=true`), có nút `"Hiện/Ẩn mật khẩu"`.
   - **Bằng chứng:** Ảnh `login_1440x900_default.png` và [Login.tsx](file:///d:/ERP_KHO/frontend/src/pages/Login.tsx#L14-L15).
   - **Tác động:** Làm mất ngay niềm tin của khách hàng B2B về tính bảo mật của hệ thống ngay giây đầu tiên; người dùng dễ gõ sai mật khẩu trên điện thoại vì không xem lại được ký tự vừa nhập.
   - **Cách sửa & Tiêu chí nghiệm thu:** Khởi tạo `username = ''`, `password = ''` trong `Login.tsx`; thêm nút `<button type="button" aria-label="Hiện mật khẩu">` cạnh ô password. **Nghiệm thu:** Mở `/login` thấy 2 ô trống `100%`, bấm nút Hiện mật khẩu đổi `type="password"` sang `type="text"`.

2. **`[🔴 Nghiêm trọng] E6, N8 & L5 — Bắt buộc sửa` — Màn Tổng quan (`/`) ở chế độ Đầy đủ nhồi nhét form cấu hình SKU sàn TMĐT (chứa mã giả `"QA-E2E-001"`) và thẻ kỹ thuật `"Trạng thái môi trường"`**
   - **Bước tái hiện:** Đăng nhập vào `/`, bấm nút `"Đầy đủ"` ở góc phải tiêu đề, cuộn xuống nửa dưới trang.
   - **Thực tế vs Mong đợi:** Trang dài `2837px` (`3.15x` màn hình), bên dưới các bảng cảnh báo tồn kho lại đặt biểu mẫu `"Tích hợp Sàn TMĐT Đa kênh & Quy tắc Phân bổ Tồn kho"` có ô `SKU nội bộ` điền sẵn cứng `"QA-E2E-001"` ([Dashboard.tsx](file:///d:/ERP_KHO/frontend/src/pages/Dashboard.tsx#L295)) và cuối trang là thẻ `"Trạng thái môi trường"`. Mong đợi: Dashboard chỉ hiển thị chỉ số điều hành và cảnh báo cần xử lý; phần cấu hình mapping SKU phải tách sang trang cấu hình tích hợp riêng, và tuyệt đối không điền sẵn mã `"QA-E2E-001"`.
   - **Bằng chứng:** Ảnh `dashboard_1440x900_default.png`, `dashboard_390x844_default.png`, file `ux_audit_metrics.json`.
   - **Tác động:** Chủ shop và quản lý kho bị ngợp thông tin (`Hick's Law L1`), phải cuộn hơn 3 màn hình desktop (6.5 màn hình điện thoại) và thấy mã kiểm thử `"QA-E2E-001"` ngay trên trang chủ.
   - **Cách sửa & Tiêu chí nghiệm thu:** Xóa giá trị mặc định `'QA-E2E-001'` tại `Dashboard.tsx:295`, đóng gọn (collapse) hoặc chuyển khối Cấu hình Sàn TMĐT sang modal/trang riêng, ẩn thẻ `"Trạng thái môi trường"` trên bản thương mại. **Nghiệm thu:** Chiều cao trang `/` ở chế độ Đầy đủ trên `1440×900` giảm xuống `≤ 1800px` (`≤ 2.0x` viewport) và `leakedStrings` trên `/` bằng `0`.

3. **`[🟠 Lỗi nặng] W2.5.8 & E4 — Bắt buộc sửa` — Các liên kết hành động `"Thực thi →"`, `"Xử lý →"` trên Dashboard cao chỉ `15px–18px` và tiêu đề cột số `<th>` bị căn trái**
   - **Bước tái hiện:** Quan sát thẻ `"Hành động Khẩn cấp Hôm nay"` và bảng `"Trung tâm Điều hành Kho phân tán"` trên `/`.
   - **Thực tế vs Mong đợi:** 6 liên kết `"Thực thi →"` (`60×15px`) và `"Xử lý →"` (`42×18px`) là thẻ `<a>` thuần không có padding nên chiều cao dưới ngưỡng tối thiểu `24px` của WCAG 2.2 AA (`W2.5.8`); các cột số trong bảng kho phân tán có `<td>` căn phải nhưng `<th>` căn trái (`E4`).
   - **Bằng chứng:** `ux_audit_metrics.json` (`under24Count: 6`, `under24Samples`: `Thực thi → 60x15`, `Xử lý → 42x18`).
   - **Tác động:** Khó bấm chính xác bằng chuột hoặc ngón tay; bảng số liệu bị lệch trục mắt giữa tiêu đề cột và con số bên dưới.
   - **Cách sửa & Tiêu chí nghiệm thu:** Thêm class `.ui-btn .ui-btn--sm` (chiều cao tối thiểu `32px–36px`) cho các link hành động và thêm `className="text-right"` cho các thẻ `<th>` cột số lượng trong `Dashboard.tsx`. **Nghiệm thu:** Chạy lại script đo tự động trên `/` cho kết quả `under24Count === 0` và `misalignedNumHeadersCount === 0`.

---

### ĐỢT 2: PHÂN HỆ NHẬP KHO — INBOUND (`/dock-yard`, `/purchase-orders`, `/asns`, `/import-receipts`, `/putaway-tasks`)

![Màn hình Phiếu nhập kho 1440x900](/C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb/import_receipts_1440x900_default.png)
![Màn hình Phiếu nhập kho 390x844 PDA](/C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb/import_receipts_390x844_default.png)
![Màn hình Cất hàng Putaway 390x844 PDA](/C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb/chrome-suite8-mobile-putaway.png)

#### 1. Tóm tắt & 3 vấn đề lớn nhất của Đợt 2
- **Điểm trung bình Đợt 2:** `/dock-yard` (`2.92`), `/purchase-orders` (`2.92`), `/asns` (`2.77`), `/import-receipts` (`2.54`), `/putaway-tasks` (`2.85`).
- **3 vấn đề nghiêm trọng nhất:**
  1. **`[🔴 Nghiêm trọng] V2 & W1.4.10`:** Trên màn hình PDA `390×844` của `/import-receipts` ([ImportReceipts.tsx](file:///d:/ERP_KHO/frontend/src/pages/ImportReceipts.tsx)) và `/asns` ([AsnNotices.tsx](file:///d:/ERP_KHO/frontend/src/pages/AsnNotices.tsx)), thanh 5 thẻ KPI dùng inline style `gridTemplateColumns: 'repeat(5, minmax(0, 1fr))'` cứng khiến mỗi thẻ bị ép còn rộng **`55px`** trên điện thoại, làm chữ tiếng Việt bị bẻ vụn từng chữ cái theo chiều dọc (`"Tổn / g số / phiế / u / nhập"`, `"Đan / g / chờ / Kiể / m / định"`).
  2. **`[🔴 Nghiêm trọng] V1 & E6`:** Bảng `"Trạm Nhận Hàng Hoàn Trả & Xử Lý Hàng Hỏng (RMA)"` ở cuối màn `/import-receipts` hiển thị nguyên mã enum tiếng Anh thô `"CUSTOMER_RETURN"` trên cột Phân loại thay vì `"Khách trả hàng (RMA)"`, đồng thời lộ hàng loạt mã phiếu test `"IR-QA-001"`, `"IR-QA-002"`, `"QA-E2E-001"`.
  3. **`[🟠 Lỗi nặng] W4.1.2 & W2.5.8`:** Trên `/asns` có **`7` nút/ô điều khiển thiếu nhãn truy cập (`missingIconBtnLabel: 7`)**; trên `/import-receipts` và `/putaway-tasks`, ô checkbox chọn dòng trong bảng chỉ rộng **`16×16px`** (vi phạm ngưỡng tối thiểu `24×24px` của `W2.5.8` và rất khó chạm bằng ngón tay đeo găng trong kho `M1`).

#### 2. Phạm vi kiểm thử Đợt 2
- **Đã kiểm tra:** Toàn bộ 5 màn hình phân hệ Nhập kho (`/dock-yard`, `/purchase-orders`, `/asns`, `/import-receipts`, `/putaway-tasks`) trên cả Desktop `1440×900`, Tablet `768×1024` và PDA `390×844`.
- **Chưa kiểm tra:** Kết nối súng bắn mã vạch Bluetooth HID vật lý ngoài đời thực (đã kiểm chứng qua sự kiện bàn phím `Enter` và `autofocus` trên trình duyệt).

#### 3. Bảng điểm 13 Tiêu chí (Thang 1–5) — Đợt 2

| Tiêu chí | `/dock-yard` | `/purchase-orders` | `/asns` | `/import-receipts` | `/putaway-tasks` | Nhận xét cốt lõi & Mã quy tắc |
|---|---:|---:|---:|---:|---:|---|
| 1. Phân cấp thị giác | 3 | 3 | 3 | 2 | 3 | `/import-receipts` mở sẵn quá nhiều khối (KPI + Bộ lọc + Bảng phiếu + Form tạo + Trạm RMA) (`G1`). |
| 2. Bố cục, mật độ, độ dài trang | 3 | 4 | 3 | 2 | 4 | `/import-receipts` dài `2.14x` desktop, `4.70x` mobile (`3968px`) (`L5`). |
| 3. Màu sắc & tương phản | 4 | 4 | 4 | 4 | 4 | Tương phản chữ và trạng thái phiếu tốt (`W1.4.3`). |
| 4. Typography | 3 | 3 | 3 | 3 | 2 | `/putaway-tasks` có `30` nhãn `< 12px`; `/import-receipts` có `29` nhãn `< 12px` (`G4`). |
| 5. Nhất quán | 3 | 3 | 3 | 3 | 3 | Nút hành động và thẻ đồng bộ (`S1`). |
| 6. UX writing | 3 | 3 | 3 | 2 | 3 | Lộ chuỗi `"CUSTOMER_RETURN"` tiếng Anh trên `/import-receipts` (`V1`). |
| 7. Khả năng dùng | 3 | 3 | 3 | 3 | 3 | Có ô quét mã vạch nhanh, nhưng thiếu bộ lọc trạng thái dạng चिप (chips) 1-chạm (`E3`). |
| 8. Phù hợp thao tác PDA (`390×844`) | 2 | 2 | 2 | 2 | 2 | Lưới KPI 5 cột bị ép nát chữ trên `/import-receipts` & `/asns` (`V2`, `M1`, `M4`). |
| 9. Truy cập & responsive | 3 | 2 | 2 | 2 | 2 | Checkbox `16×16px` (`W2.5.8`); nút thiếu `aria-label` trên `/asns` (`7`), `/purchase-orders` (`1`) (`W4.1.2`). |
| 10. Độ tin cậy thương mại | 3 | 3 | 2 | 2 | 2 | Lộ dữ liệu `APT-QA-`, `PO-QA-`, `IR-QA-`, `QA-E2E`, huy hiệu `MẶC ĐỊNH` (`E6`). |
| 11. Trực quan hóa dữ liệu | 3 | 3 | 3 | 3 | 3 | Có sơ đồ trực quan cổng Dock và gợi ý vị trí Putaway (`E1`). |
| 12. Hiệu năng cảm nhận | 2 | 2 | 2 | 3 | 3 | Chuyển nhanh giữa `/dock-yard`, `/purchase-orders`, `/asns` dễ chạm ngưỡng `429 Too Many Requests` (`L3`). |
| 13. Dư thừa & trùng lặp | 3 | 3 | 3 | 2 | 3 | `/import-receipts` gộp cả danh sách phiếu nhập, trạm quét nhận hàng và trạm xử lý RMA trên cùng 1 trang dài (`N8`). |
| **Điểm trung bình (`/ 5.0`)** | **2.92** | **2.92** | **2.77** | **2.54** | **2.85** | |

#### 4. Ma trận tuân thủ Đợt 2 (Chỉ liệt kê ngoại lệ)
- **`W1.4.10` & `V2` (Không vỡ bố cục & không đứt từ tiếng Việt trên `390×844`): Không đạt** — Lưới KPI 5 cột trên `/import-receipts` và `/asns` giữ nguyên 5 cột trên màn hình rộng `390px` (`narrowMultiColGridsCount: 1`), làm chữ bị ngắt từng ký tự dọc.
- **`W2.5.8` (Mục tiêu chạm `≥ 24×24px`): Không đạt** — Ô checkbox chọn phiếu trên `/import-receipts` và `/putaway-tasks` chỉ đạt `16×16px` (`under24Count: 1`).
- **`W4.1.2` (Tên/Vai trò điều khiển): Không đạt** — Có nút/control không có tên truy cập trên `/asns` (`7`), `/purchase-orders` (`1`), `/import-receipts` (`1`), `/putaway-tasks` (`1`).
- **`V1` (100% Tiếng Việt): Không đạt** — Hiển thị `"CUSTOMER_RETURN"` trên `/import-receipts`.
- **`M1`, `M2`, `M3`, `M4`: Một phần / Không đạt** — Đã có ô nhập mã vạch lớn ở đầu trang, nhưng các nút phụ chỉ cao `36px`, không có Bottom Navigation cho nhân viên kho cầm PDA bằng 1 tay.
- **Tổng kết tuân thủ Đợt 2:**
  - **Đạt (33 mã):** `N1..N7, N9, S1..S8, W1.1.1, W1.3.1, W1.4.1, W1.4.3, W1.4.4, W1.4.11, W2.1.1, W2.1.2, W2.4.7, W2.5.3, W3.3.1, W3.3.2, G2, G3, G5, G6, V4`.
  - **Không áp dụng (3 mã):** `W3.3.8, E7, E8`.

#### 5. Số liệu đo tự động Đợt 2
- **`/dock-yard`:** `pageHeightRatio`: `1.35` (`1440w`) / `1.85` (`390w`); `under44Count`: `41`; `under48Count`: `46`; `smallTextUnder12px`: `8`.
- **`/purchase-orders`:** `pageHeightRatio`: `1.14` / `1.96`; `missingIconBtnLabel`: `1`; `under44Count`: `43`; `smallTextUnder12px`: `11`.
- **`/asns`:** `pageHeightRatio`: `1.74` / `3.09`; `missingIconBtnLabel`: **`7`**; `narrowMultiColGridsCount` (`390w`): **`1`** (`53px/cột`); `under44Count`: `55`.
- **`/import-receipts`:** `pageHeightRatio`: `2.14` (`1930px`) / `4.70` (`3968px`); `under24Count`: **`1`** (`16×16px`); `missingIconBtnLabel`: `1`; `narrowMultiColGridsCount` (`390w`): **`1`** (`55px/cột`); `smallTextUnder12px`: `29`; `leakedStrings`: `["QA-E2E", "IR-QA-", "MẶC ĐỊNH", "CUSTOMER_RETURN"]`.
- **`/putaway-tasks`:** `pageHeightRatio`: `1.05` / `2.12`; `under24Count`: **`1`** (`16×16px`); `missingIconBtnLabel`: `1`; `smallTextUnder12px`: `30`.

#### 6. Điểm tốt của Đợt 2
- Màn `/putaway-tasks` và `/import-receipts` đều bố trí **Trạm quét mã vạch (`Barcode Scan Station`)** ngay phía trên bảng dữ liệu kèm vector mã vạch `<BarcodeSvg />` sắc nét, đúng tinh thần ưu tiên súng quét của `Spec 217` (`M2`).
- Có luồng xác nhận (`window.confirm` / hộp thoại xác nhận) và thông báo trạng thái rõ ràng khi duyệt phiếu nhập hoặc hoàn tất nhiệm vụ cất hàng (`N5`, `S5`).

#### 7. Vấn đề chi tiết Đợt 2

1. **`[🔴 Nghiêm trọng] V2 & W1.4.10 — Bắt buộc sửa` — Thanh 5 thẻ KPI trên `/import-receipts` và `/asns` bị ép còn rộng `55px` trên màn hình PDA `390×844`, làm vỡ nát chữ tiếng Việt**
   - **Bước tái hiện:** Mở `/import-receipts` hoặc `/asns` ở khung nhìn `390×844` (PDA cầm tay).
   - **Thực tế vs Mong đợi:** 5 thẻ KPI (`Tổng số phiếu nhập`, `Đang chờ Kiểm định`, `Đã Duyệt / Cất hàng`, `Tổng SL Nhận`, `Cảnh báo Quá hạn`) nằm cứng trên 1 hàng ngang 5 cột (`55px/thẻ`), chữ tiếng Việt bị bẻ dọc từng chữ cái (`"Tổn / g số / phiế / u..."`). Mong đợi: Trên mobile `< 640px`, lưới KPI phải tự động chuyển về `2 cột` (`grid-template-columns: repeat(2, 1fr)`), chữ hiển thị nguyên từ rõ ràng.
   - **Bằng chứng:** Ảnh `import_receipts_390x844_default.png` (góc trên cùng) và `ux_audit_metrics.json` (`narrowMultiColGridsCount: 1`).
   - **Tác động:** Nhân viên kho dùng máy kiểm kho cầm tay PDA không thể đọc được số liệu KPI đầu trang.
   - **Cách sửa & Tiêu chí nghiệm thu:** Thay inline style `gridTemplateColumns: 'repeat(5, minmax(0, 1fr))'` trong [ImportReceipts.tsx](file:///d:/ERP_KHO/frontend/src/pages/ImportReceipts.tsx) và [AsnNotices.tsx](file:///d:/ERP_KHO/frontend/src/pages/AsnNotices.tsx) bằng class CSS `.ui-kpi-grid` (Desktop 5 cột, Tablet 3 cột, Mobile 2 cột). **Nghiệm thu:** Trên `390×844`, mỗi thẻ KPI rộng `≥ 160px`, `narrowMultiColGridsCount === 0`, không có từ tiếng Việt nào bị ngắt giữa chữ.

2. **`[🔴 Nghiêm trọng] V1 — Bắt buộc sửa` — Hiển thị nguyên mã enum tiếng Anh `"CUSTOMER_RETURN"` trong bảng RMA của màn `/import-receipts`**
   - **Bước tái hiện:** Mở `/import-receipts`, cuộn xuống bảng `"Trạm Nhận Hàng Hoàn Trả & Xử Lý Hàng Hỏng (RMA)"`.
   - **Thực tế vs Mong đợi:** Cột `Phân loại` hiển thị chuỗi thô `"CUSTOMER_RETURN"` cho các phiếu `RMA-20261025-...`. Mong đợi: Phải Việt hóa thành `"Khách trả hàng (RMA)"` hoặc `"Hoàn trả từ khách"` theo quy tắc `V1`.
   - **Bằng chứng:** Ảnh `import_receipts_1440x900_default.png` (phần cuối trang).
   - **Tác động:** Nhân viên kho phổ thông không hiểu thuật ngữ enum lập trình tiếng Anh.
   - **Cách sửa & Tiêu chí nghiệm thu:** Ánh xạ `CUSTOMER_RETURN -> 'Khách hoàn trả (RMA)'`, `DAMAGED_INBOUND -> 'Hàng lỗi khi nhập'` trong [ImportReceipts.tsx](file:///d:/ERP_KHO/frontend/src/pages/ImportReceipts.tsx). **Nghiệm thu:** Không còn chuỗi `CUSTOMER_RETURN` xuất hiện trên DOM.

---

### ĐỢT 3: PHÂN HỆ XUẤT KHO & ĐƠN HÀNG — OUTBOUND (`/backorders`, `/export-receipts`, `/stock-reservations`, `/stock-allocations`, `/picking-tasks`, `/packing-sessions`, `/shipments`, `/handover-manifests`)

![Màn hình Lấy hàng Picking 390x844 PDA](/C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb/chrome-suite8-mobile-picking.png)
![Màn hình Đóng gói Packing 390x844 PDA](/C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb/chrome-suite8-mobile-packing.png)

#### 1. Tóm tắt & 3 vấn đề lớn nhất của Đợt 3
- **Điểm trung bình Đợt 3:** Dao động từ **`2.62 / 5.0`** (`/packing-sessions`) đến **`3.00 / 5.0`** (`/handover-manifests`).
- **3 vấn đề nghiêm trọng nhất:**
  1. **`[🔴 Nghiêm trọng] W1.4.10 & V2`:** Màn `/stock-allocations` ([StockAllocations.tsx](file:///d:/ERP_KHO/frontend/src/pages/StockAllocations.tsx)) bị **tràn ngang toàn trang (`Horizontal Overflow`)** trên khung nhìn PDA `390×844` (`scrollWidth = 461px > clientWidth = 390px`, tràn `+71px` do cụm nút `.ui-inline-actions` và lưới KPI 4 cột rộng `69px/cột`).
  2. **`[🟠 Lỗi nặng] L5, G4 & W2.5.8`:** Hai màn hình tác nghiệp cốt lõi của nhân viên kho là `/picking-tasks` và `/packing-sessions` có tới **`67` và `75` phần tử chữ nhỏ `< 12px`** (mã vị trí bin phụ, nhãn metadata, chữ trong SVG barcode), chiều dài trang `/packing-sessions` trên PDA lên tới **`5.52x` (`4661px`)**, và ô checkbox chọn dòng chỉ đạt **`16×16px`** (`under24Count: 1`).
  3. **`[🟠 Lỗi nặng] V2 & W1.4.10`:** Màn `/shipments` ([Shipments.tsx](file:///d:/ERP_KHO/frontend/src/pages/Shipments.tsx)) dùng lưới 4 cột cố định trên `390×844` khiến mỗi thẻ chỉ rộng **`69px`**, chữ bị ép dọc khó đọc.

#### 2. Phạm vi kiểm thử Đợt 3
- **Đã kiểm tra:** Toàn bộ 8 màn hình phân hệ Xuất kho (`/backorders`, `/export-receipts`, `/stock-reservations`, `/stock-allocations`, `/picking-tasks`, `/packing-sessions`, `/shipments`, `/handover-manifests`) trên `1440×900`, `768×1024`, `390×844`.
- **Chưa kiểm tra:** In nhiệt trực tiếp ra máy in Zebra vật lý qua cổng USB/LAN (đã kiểm chứng chức năng xuất file tem nhiệt AWB `%PDF-1.4` 100×150mm thật từ API).

#### 3. Bảng điểm 13 Tiêu chí (Thang 1–5) — Đợt 3

| Tiêu chí | `/backorders` | `/export-receipts` | `/stock-reservations` | `/stock-allocations` | `/picking-tasks` | `/packing-sessions` | `/shipments` | `/handover-manifests` |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 1. Phân cấp thị giác | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 |
| 2. Bố cục, mật độ, độ dài | 3 | 3 | 3 | 3 | 3 | 2 | 4 | 4 |
| 3. Màu sắc & tương phản | 4 | 4 | 4 | 4 | 4 | 4 | 4 | 4 |
| 4. Typography | 3 | 3 | 3 | 3 | 2 | 2 | 3 | 3 |
| 5. Nhất quán | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 |
| 6. UX writing | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 |
| 7. Khả năng dùng | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 |
| 8. Phù hợp PDA (`390×844`) | 2 | 2 | 2 | 1 | 2 | 2 | 2 | 2 |
| 9. Truy cập & responsive | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 3 |
| 10. Độ tin cậy thương mại | 2 | 3 | 2 | 2 | 2 | 2 | 3 | 3 |
| 11. Trực quan hóa dữ liệu | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 |
| 12. Hiệu năng cảm nhận | 3 | 3 | 3 | 3 | 3 | 3 | 2 | 3 |
| 13. Dư thừa & trùng lặp | 2 | 3 | 2 | 2 | 2 | 2 | 3 | 3 |
| **Điểm TB (`/ 5.0`)** | **2.85** | **2.92** | **2.85** | **2.69** | **2.69** | **2.62** | **2.92** | **3.00** |

#### 4. Ma trận tuân thủ Đợt 3 (Chỉ liệt kê ngoại lệ)
- **`W1.4.10` (Reflow — Không tràn ngang ở `390px`): Không đạt** — Màn `/stock-allocations` bị tràn ngang toàn trang (`scrollWidth: 461px > 390px`, `horizontalOverflow: true`); màn `/stock-allocations` và `/shipments` có lưới 4 cột bị bóp còn `69px/cột` trên mobile (`V2`).
- **`W2.5.8` (Mục tiêu chạm `≥ 24×24px`): Không đạt** — Checkbox chọn dòng trên `/picking-tasks` và `/packing-sessions` chỉ đạt `16×16px`.
- **`W4.1.2` (Tên điều khiển): Không đạt** — `/picking-tasks` và `/packing-sessions` mỗi màn có `1` nút thiếu nhãn truy cập.
- **`G4` (Quy mô chữ tối thiểu): Không đạt** — `/picking-tasks` có `67` phần tử chữ `< 12px`, `/packing-sessions` có `75` phần tử chữ `< 12px`.
- **`M1`, `M3`, `M4`: Không đạt** — Nút bấm cao `36px` (`< 48px` chuẩn kho vận), không có thanh điều hướng đáy cho nhân viên lấy hàng/đóng gói.
- **Tổng kết tuân thủ Đợt 3:**
  - **Đạt (33 mã):** `N1..N7, N9, S1..S8, W1.1.1, W1.3.1, W1.4.1, W1.4.3, W1.4.4, W1.4.11, W2.1.1, W2.1.2, W2.4.7, W2.5.3, W3.3.1, W3.3.2, G2, G3, G5, G6, V1, V4`.
  - **Không áp dụng (3 mã):** `W3.3.8, E7, E8`.

#### 5. Số liệu đo tự động Đợt 3
- **`/stock-allocations` (`390×844`):** `horizontalOverflow`: **`true`** (`docScrollWidth: 461`, `clientWidth: 390`, tràn `+71px`); `narrowMultiColGridsCount`: `1` (`69px/cột`).
- **`/picking-tasks` (`1440×900` / `390×844`):** `pageHeightRatio`: `1.54` / `3.58`; `under24Count`: `1` (`16×16px`); `smallTextUnder12px`: **`67`**; `under48Count`: `55`.
- **`/packing-sessions` (`1440×900` / `390×844`):** `pageHeightRatio`: `2.41` (`2165px`) / **`5.52`** (`4661px`); `under24Count`: `1` (`16×16px`); `smallTextUnder12px`: **`75`**; `under48Count`: `72`.
- **`/shipments` (`390×844`):** `narrowMultiColGridsCount`: `1` (`69px/cột`); `under48Count`: `44`.

#### 6. Điểm tốt của Đợt 3
- Luồng `/packing-sessions` hỗ trợ tải lên video bằng chứng đóng gói (`multipart/form-data` thật) và xuất tem vận đơn nhiệt AWB `100×150mm` định dạng `%PDF-1.4` thật (`E2`).
- Màn `/picking-tasks` hiển thị rõ lộ trình lấy hàng theo vị trí Bin (`A1-01-01`) kèm mã vạch vector trực quan, giúp nhân viên kho đối chiếu bằng mắt và máy quét nhanh chóng.

#### 7. Vấn đề chi tiết Đợt 3

1. **`[🔴 Nghiêm trọng] W1.4.10 & V2 — Bắt buộc sửa` — Màn Phân bổ tồn (`/stock-allocations`) bị tràn ngang `+71px` trên màn hình PDA `390×844`**
   - **Bước tái hiện:** Mở `/stock-allocations` ở kích thước `390×844`, cuộn ngang màn hình.
   - **Thực tế vs Mong đợi:** Toàn bộ trang bị trượt ngang (`scrollWidth = 461px > 390px`) do thanh công cụ `.ui-inline-actions` chứa các nút hành động dài không cho phép xuống dòng (`flex-wrap: nowrap`) và lưới KPI 4 cột giữ nguyên 4 cột (`69px/cột`). Mong đợi: `scrollWidth === 390px` (`0px` tràn ngang toàn trang), chỉ cho phép cuộn ngang bên trong vùng `.ui-table-wrap`.
   - **Bằng chứng:** `ux_audit_metrics.json` (`/stock-allocations` mobile: `horizontalOverflow: true, docScrollWidth: 461`).
   - **Tác động:** Khiến toàn bộ khung giao diện bị lệch và lắc ngang khi nhân viên thao tác bằng một tay trên điện thoại.
   - **Cách sửa & Tiêu chí nghiệm thu:** Thêm `flex-wrap: wrap` cho `.ui-inline-actions` trong `production-ui.css` và chuyển lưới KPI 4 cột trong [StockAllocations.tsx](file:///d:/ERP_KHO/frontend/src/pages/StockAllocations.tsx) ও [Shipments.tsx](file:///d:/ERP_KHO/frontend/src/pages/Shipments.tsx) sang responsive 2 cột trên `< 640px`. **Nghiệm thu:** Trên `390×844`, `document.documentElement.scrollWidth <= 390` và `narrowMultiColGridsCount === 0`.

---

### ĐỢT 4: PHÂN HỆ TỒN KHO & KIỂM KÊ — INVENTORY (`/inventory`, `/inventory-locks`, `/inventory-movements`, `/inventory-reversals`, `/inventory-traceability`, `/inventory-reconciliation`, `/stocktakes`, `/stock-transfers`)

![Màn hình Kiểm kê kho Stocktakes 1440x900](/C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb/chrome-suite6-stocktakes.png)
![Màn hình Di chuyển vị trí nội bộ 1440x900](/C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb/chrome-suite6-inventory-movements.png)
![Màn hình Điều chuyển kho 390x844 PDA](/C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb/chrome-suite8-mobile-transfers.png)

#### 1. Tóm tắt & 3 vấn đề lớn nhất của Đợt 4
- **Điểm trung bình Đợt 4:** Dao động từ **`2.77 / 5.0`** (`/inventory-reconciliation`, `/stocktakes`) đến **`3.08 / 5.0`** (`/inventory-reversals`).
- **3 vấn đề nghiêm trọng nhất:**
  1. **`[🔴 Nghiêm trọng] E5 (Lệch cấu hình phân quyền Menu vs Route)`:** Trong [productionNavigation.ts](file:///d:/ERP_KHO/frontend/src/navigation/productionNavigation.ts#L38), mục menu `"Đối chiếu tồn kho"` (`/inventory-reconciliation`) được gán `access: 'always'` (ai đăng nhập cũng thấy trên thanh menu trái), nhưng trong [AppRoutes.tsx](file:///d:/ERP_KHO/frontend/src/routes/AppRoutes.tsx#L117) lại chặn bằng quyền `permission="inventory_ledger.read"`. Nhân viên không có quyền sổ cái thấy menu, bấm vào mới bị chặn bởi màn hình lỗi `Không có quyền truy cập`.
  2. **`[🔴 Nghiêm trọng] W4.1.2`:** Màn `/inventory-traceability` có **`5` điều khiển thiếu nhãn truy cập (`missingIconBtnLabel: 5`)** và màn `/inventory-reconciliation` có **`2` điều khiển thiếu nhãn (`missingIconBtnLabel: 2`)**.
  3. **`[🔴 Nghiêm trọng] V2 & W1.4.10`:** Màn `/stocktakes` ([Stocktakes.tsx](file:///d:/ERP_KHO/frontend/src/pages/Stocktakes.tsx)) dùng lưới KPI 4 cột cố định trên khung nhìn `390×844` khiến mỗi ô bị bóp còn rộng **`69px`**, chữ tiếng Việt bị ngắt vụn.

#### 2. Phạm vi kiểm thử Đợt 4
- **Đã kiểm tra:** Toàn bộ 8 màn hình phân hệ Tồn kho & Kiểm kê (bao gồm cả 6 tab con bên trong `/inventory`) trên `1440×900`, `768×1024`, `390×844`.
- **Chưa kiểm tra:** Hiệu năng truy vấn phả hệ Lot/Serial khi cây phả hệ vượt quá 10 tầng trung chuyển.

#### 3. Bảng điểm 13 Tiêu chí (Thang 1–5) — Đợt 4

| Tiêu chí | `/inventory` | `/inventory-locks` | `/inventory-movements` | `/inventory-reversals` | `/inventory-traceability` | `/inventory-reconciliation` | `/stocktakes` | `/stock-transfers` |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 1. Phân cấp thị giác | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 |
| 2. Bố cục, mật độ, độ dài | 3 | 4 | 4 | 4 | 4 | 3 | 3 | 4 |
| 3. Màu sắc & tương phản | 4 | 4 | 4 | 4 | 4 | 4 | 4 | 4 |
| 4. Typography | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 |
| 5. Nhất quán | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 |
| 6. UX writing | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 |
| 7. Khả năng dùng | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 |
| 8. Phù hợp PDA (`390×844`) | 2 | 2 | 2 | 3 | 3 | 2 | 2 | 2 |
| 9. Truy cập & responsive | 3 | 3 | 3 | 3 | 2 | 2 | 2 | 3 |
| 10. Độ tin cậy thương mại | 2 | 2 | 2 | 3 | 3 | 2 | 2 | 3 |
| 11. Trực quan hóa dữ liệu | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 |
| 12. Hiệu năng cảm nhận | 3 | 3 | 3 | 3 | 3 | 3 | 2 | 2 |
| 13. Dư thừa & trùng lặp | 2 | 3 | 3 | 3 | 2 | 2 | 3 | 3 |
| **Điểm TB (`/ 5.0`)** | **2.85** | **2.92** | **2.92** | **3.08** | **2.92** | **2.77** | **2.77** | **2.92** |

#### 4. Ma trận tuân thủ Đợt 4 (Chỉ liệt kê ngoại lệ)
- **`E5` (Đồng bộ phân quyền Menu & Route): Không đạt** — `/inventory-reconciliation` đặt `access: 'always'` trong menu nhưng yêu cầu `inventory_ledger.read` ở route.
- **`W4.1.2` (Tên điều khiển): Không đạt** — `/inventory-traceability` (`5` lỗi), `/inventory-reconciliation` (`2` lỗi).
- **`V2` & `W1.4.10` (Không vỡ chữ trên `390×844`): Không đạt** — Lưới KPI 4 cột trên `/stocktakes` bị ép còn `69px/cột` trên mobile.
- **`E2` (Bất biến sổ cái & Đảo bút toán): Đạt xuất sắc** — Màn `/inventory-reversals` thực thi đúng nguyên tắc tạo bút toán đảo (`Reversal`) thay vì sửa/xóa trực tiếp sổ cái.
- **Tổng kết tuân thủ Đợt 4:**
  - **Đạt (34 mã):** `N1..N7, N9, S1..S8, W1.1.1, W1.3.1, W1.4.1, W1.4.3, W1.4.4, W1.4.11, W2.1.1, W2.1.2, W2.4.7, W2.5.3, W2.5.8, W3.3.1, W3.3.2, G2, G3, G5, G6, E2, V1, V4`.
  - **Không áp dụng (3 mã):** `W3.3.8, E7, E8`.

#### 5. Số liệu đo tự động Đợt 4
- **`/inventory` (6 Tabs):** `pageHeightRatio`: `1.00` (`1440w`) / `1.92` (`390w`); `under24Count`: `0`; `under44Count`: `50`.
- **`/inventory-traceability`:** `missingIconBtnLabel`: **`5`**; `under44Count`: `47`.
- **`/inventory-reconciliation`:** `pageHeightRatio`: `1.62` / `3.33`; `missingIconBtnLabel`: **`2`**; `under44Count`: `46`.
- **`/stocktakes`:** `pageHeightRatio`: `1.19` / `3.07`; `narrowMultiColGridsCount` (`390w`): **`1`** (`69px/cột`).

#### 6. Điểm tốt của Đợt 4
- Màn `/inventory` gom gọn 6 báo cáo tồn kho chuyên sâu vào 6 Tab ngang (`Tồn kho hiện hành`, `Thẻ kho Sổ cái`, `Tuổi tồn & Hạn dùng FEFO`, `Định mức An toàn`, `Vòng quay & Hàng chậm luân chuyển`, `Tồn kho Ký gửi`) giúp giữ chiều cao trang ở đúng `1.00x` (`900px`) trên Desktop (`L5`).
- Màn `/inventory-reversals` tách bạch rõ ràng lịch sử bút toán gốc và bút toán đảo (`Reversal`), bảo vệ tính toàn vẹn kế toán kho (`E2`).

#### 7. Vấn đề chi tiết Đợt 4

1. **`[🔴 Nghiêm trọng] E5 — Bắt buộc sửa` — Mục menu `"Đối chiếu tồn kho"` hiển thị cho mọi vai trò (`access: 'always'`) nhưng Route lại chặn quyền `inventory_ledger.read`**
   - **Bước tái hiện:** Xem cấu hình tại [productionNavigation.ts](file:///d:/ERP_KHO/frontend/src/navigation/productionNavigation.ts#L38) và [AppRoutes.tsx](file:///d:/ERP_KHO/frontend/src/routes/AppRoutes.tsx#L117).
   - **Thực tế vs Mong đợi:** Menu cấu hình `access: 'always'`, còn Route yêu cầu `permission="inventory_ledger.read"`. Mong đợi: Menu phải dùng đúng `access: { type: 'permission', permission: 'inventory_ledger.read' }` để ẩn mục menu đối với tài khoản không có quyền (`FE-006`).
   - **Bằng chứng:** Đối chiếu trực tiếp `productionNavigation.ts:38` và `AppRoutes.tsx:117`.
   - **Tác động:** Người dùng không có quyền vẫn nhìn thấy mục menu, bấm vào nhận thông báo lỗi gây bức xúc.
   - **Cách sửa & Tiêu chí nghiệm thu:** Sửa dòng 38 của `productionNavigation.ts` thành `access: { type: 'permission', permission: 'inventory_ledger.read' }`. **Nghiệm thu:** Tài khoản không có quyền `inventory_ledger.read` không còn thấy mục `"Đối chiếu tồn kho"` trên sidebar.

---

### ĐỢT 5: TÀI CHÍNH, GÓI CƯỚC SAAS, QUẢN TRỊ & DANH MỤC GỐC (`/cod-reconciliation`, `/cost-valuation`, `/partner-debts`, `/saas-billing`, `/permissions`, `/approvals`, `/products`, `/warehouses`, `/warehouse-structure`, `/warehouse-map`, `/warehouse-calendar`, `/units`, `/business-partners`)

![Màn hình Gói cước SaaS & VietQR 1440x900](/C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb/chrome-suite7-saas-billing.png)
![Màn hình Quản trị quyền RBAC & 2FA 1440x900](/C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb/chrome-suite7-permissions.png)
![Màn hình Đối soát COD 3 chiều 1440x900](/C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb/chrome-suite7-cod-reconciliation.png)
![Màn hình Giá vốn & kết xuất MISA 1440x900](/C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb/chrome-suite7-cost-valuation.png)

#### 1. Tóm tắt & 3 vấn đề lớn nhất của Đợt 5 (Nghiêm trọng nhất toàn hệ thống)
- **Điểm trung bình Đợt 5:** Thấp nhất là **`/permissions` (`2.38 / 5.0`)**, **`/products` (`2.54 / 5.0`)**, **`/saas-billing` (`2.62 / 5.0`)**; cao nhất là `/warehouses`, `/warehouse-calendar`, `/units` (`3.00 / 5.0`).
- **3 vấn đề nghiêm trọng nhất (Chặn phát hành thương mại — Release Blockers):**
  1. **`[🔴 Nghiêm trọng] E7 & N8 (Lỗ hổng Nghiệp vụ Thuê bao Thương mại & Trùng lặp UI)`:** Trên cả 2 màn hình `/saas-billing` ([SaasBilling.tsx](file:///d:/ERP_KHO/frontend/src/pages/SaasBilling.tsx#L159-L191)) VÀ `/permissions` ([Permissions.tsx](file:///d:/ERP_KHO/frontend/src/pages/Permissions.tsx#L305-L332)), giao diện người dùng hiển thị nút bấm **`"Xác nhận Thanh toán Tự động (Webhook PayOS / SePay)"`** (`data-testid="btn-confirm-vietqr-webhook"`). Bất kỳ ai bấm nút này từ trình duyệt sẽ gửi thẳng `POST /api/v1/permissions/saas/subscription-billing/webhook-vietqr` với số tiền `1.490.000 đ` để tự nâng cấp gói cước mà không cần chuyển khoản thật. Hơn nữa, toàn bộ thẻ Thanh toán Gói cước SaaS bị nhân bản y hệt ở cả `/saas-billing` lẫn cuối trang `/permissions`.
  2. **`[🔴 Nghiêm trọng] SEC-003 & E6 (Phơi bày Khóa Bí Mật 2FA & Mã OTP 6 số trực tiếp trên màn hình)`:** Trên màn `/permissions` ([Permissions.tsx](file:///d:/ERP_KHO/frontend/src/pages/Permissions.tsx#L156-L161)), thẻ `"Xác thực Đa yếu tố Chuẩn RFC 6238"` in rõ mồn một chuỗi **`"Khóa Base32: JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP"`** và **`"Mã xác thực 6 chữ số: 893850"`** ngay phía trên ô nhập OTP (và tự điền sẵn `893850` vào ô input), kèm một mã vạch 1D vô nghĩa mang chữ `"TOTP-2FA-QA"` bên cạnh mã QR!
  3. **`[🔴 Nghiêm trọng] E5 (Lỗ hổng Phân quyền RBAC Frontend trên 2 màn Tài chính nhạy cảm)`:** Trong [AppRoutes.tsx](file:///d:/ERP_KHO/frontend/src/routes/AppRoutes.tsx#L128-L130) và [productionNavigation.ts](file:///d:/ERP_KHO/frontend/src/navigation/productionNavigation.ts#L43-L44), hai màn hình nhạy cảm bậc nhất của Chủ shop là **`/cost-valuation` (`Giá vốn & kết xuất MISA`)** và **`/cod-reconciliation` (`Đối soát COD 3 chiều`)** chỉ kiểm tra quyền `inventory.read` thay vì `inventory.cost.read`. Nhân viên kho chỉ có quyền xem tồn kho (`inventory.read`) vẫn thấy 2 menu này trên thanh điều hướng trái!

#### 2. Phạm vi kiểm thử Đợt 5
- **Đã kiểm tra:** Toàn bộ 13 màn hình Tài chính, Gói cước SaaS, Quản trị RBAC, Phê duyệt và Danh mục gốc trên `1440×900`, `768×1024`, `390×844`.
- **Chưa kiểm tra:** Cổng thanh toán ngân hàng thật bên ngoài môi trường local (đã tuân thủ quy tắc an toàn: không bấm kích hoạt thanh toán thật ra cổng ngoài).

#### 3. Bảng điểm 13 Tiêu chí (Thang 1–5) — Đợt 5

| Màn hình (`Route`) | C1 | C2 | C3 | C4 | C5 | C6 | C7 | C8 | C9 | C10 | C11 | C12 | C13 | **Điểm TB** |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `/cod-reconciliation` | 3 | 4 | 4 | 2 | 3 | 3 | 3 | 3 | 3 | 2 | 3 | 2 | 3 | **2.85** |
| `/cost-valuation` | 3 | 4 | 4 | 2 | 3 | 3 | 3 | 3 | 3 | 2 | 3 | 2 | 3 | **2.85** |
| `/partner-debts` | 3 | 3 | 4 | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 2 | 2 | **2.85** |
| `/saas-billing` | 3 | 3 | 4 | 2 | 3 | 3 | 3 | 3 | 3 | **1** | 3 | 2 | 2 | **2.62** |
| `/permissions` | 2 | 3 | 4 | 3 | 3 | 3 | 2 | 2 | 3 | **1** | 3 | 2 | **1** | **2.38** |
| `/approvals` | 3 | 4 | 4 | 3 | 3 | 3 | 3 | 2 | 2 | 3 | 3 | 2 | 3 | **2.85** |
| `/products` | 2 | 2 | 4 | 3 | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | **2.54** |
| `/warehouses` | 3 | 4 | 4 | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 2 | 3 | **3.00** |
| `/warehouse-structure` | 3 | 3 | 4 | 3 | 3 | 3 | 3 | 2 | 2 | 3 | 3 | 2 | 2 | **2.77** |
| `/warehouse-map` | 3 | 4 | 4 | 3 | 3 | 3 | 3 | 2 | 3 | 3 | 3 | 2 | 2 | **2.85** |
| `/warehouse-calendar` | 3 | 4 | 4 | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 2 | 3 | **3.00** |
| `/units` | 3 | 4 | 4 | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 2 | 3 | **3.00** |
| `/business-partners` | 3 | 3 | 4 | 2 | 3 | 3 | 3 | 2 | 2 | 3 | 3 | 2 | 2 | **2.69** |

#### 4. Ma trận tuân thủ Đợt 5 (Chỉ liệt kê ngoại lệ)
- **`E7` (Trải nghiệm Gói cước & Thanh toán SaaS): Không đạt nghiêm trọng** — Nút giả lập Webhook PayOS/SePay hiển thị công khai cho khách hàng tự bấm kích hoạt gói trả phí; thiếu bảng so sánh quyền lợi các gói (`Starter / Growth / Enterprise`) và thiếu lịch sử hóa đơn VAT.
- **`E5` (Phân quyền theo vai trò): Không đạt** — `/cost-valuation` và `/cod-reconciliation` kiểm tra sai quyền (`inventory.read` thay vì `inventory.cost.read`).
- **`E6` & `SEC-003` (Vệ sinh thương mại & Bảo mật): Không đạt nghiêm trọng** — Lộ khóa bí mật 2FA Base32, lộ mã OTP 6 số hiện hành, lộ mã vạch `"TOTP-2FA-QA"`, lộ mã `"ZALO-SUP-..."`.
- **`W2.5.8` (Mục tiêu chạm `≥ 24×24px`): Không đạt** — Trên `/products`, ô checkbox `"Kiểm soát hạn dùng"` chỉ rộng **`13×16px`** (`under24Count: 1`).
- **`V2` & `W1.4.10` (Không vỡ chữ trên `390×844`): Không đạt** — Lưới KPI trên `/approvals`, `/warehouse-structure`, `/business-partners` bị ép còn `69px/cột` trên mobile (`narrowMultiColGridsCount: 1`).
- **`L3` (Hiệu năng cảm nhận & Chống lỗi mạng): Không đạt** — Chuyển qua lại các màn Đợt 5 kích hoạt hàng loạt lỗi **`HTTP 429 Too Many Requests`** do gọi lặp API `subscription-billing` 4 lần/trang.
- **Tổng kết tuân thủ Đợt 5:**
  - **Đạt (31 mã):** `N1..N7, N9, S1..S8, W1.1.1, W1.3.1, W1.4.1, W1.4.3, W1.4.4, W1.4.11, W2.1.1, W2.1.2, W2.4.7, W2.5.3, W3.3.1, W3.3.2, G2, G3, G5, G6, V4`.
  - **Không áp dụng (2 mã):** `W3.3.8, E8`.

#### 5. Số liệu đo tự động Đợt 5
- **`/permissions`:** `under44Count`: `38`; `leakedStrings`: `["MẶC ĐỊNH", "TOTP-2FA-QA", "893850", "ZALO-SUP-"]`; Điểm C10 (Độ tin cậy thương mại): **`1 / 5`**, Điểm C13 (Dư thừa/Trùng lặp): **`1 / 5`**.
- **`/saas-billing`:** `pageHeightRatio`: `1.04` / `2.79`; `smallTextUnder12px`: `18`; Điểm C10: **`1 / 5`**.
- **`/products`:** `pageHeightRatio`: **`2.47`** (`2220px`) / **`5.54`** (`4677px`); `under24Count`: **`1`** (`13×16px`); `under44Count`: **`83`**; `under48Count`: **`86`**; `closePairsUnder8px`: **`39`**.
- **`/approvals`, `/warehouse-structure`, `/business-partners` (`390×844`):** `narrowMultiColGridsCount`: **`1`** (`69px/cột`).

#### 6. Điểm tốt của Đợt 5
- Màn `/cost-valuation` và `/cod-reconciliation` hỗ trợ xuất tệp `.xlsx` thật (`ClosedXML`) và đọc tệp đối soát qua `FormData` thật.
- Màn `/warehouse-map` trực quan hóa tốt sơ đồ kho 2D và bản đồ nhiệt mật độ lưu trữ theo từng vị trí Bin (`E1`).

#### 7. Vấn đề chi tiết Đợt 5

1. **`[🔴 Nghiêm trọng] E7 & N8 — Bắt buộc sửa` — Nút `"Xác nhận Thanh toán Tự động (Webhook PayOS / SePay)"` cho phép người dùng tự kích hoạt gói cước trả phí ngay trên UI & thẻ Gói cước bị lặp lại ở 2 màn hình**
   - **Bước tái hiện:** Mở `/saas-billing` (hoặc cuộn xuống cuối màn `/permissions`), quan sát khu vực `"Thanh toán Chuyển khoản Tức thời (VietQR Napas 247)"`.
   - **Thực tế vs Mong đợi:** Có nút bấm `"Xác nhận Thanh toán Tự động (Webhook PayOS / SePay)"` (`[ data-testid="btn-confirm-vietqr-webhook" ]`). Khi bấm vào, trình duyệt gọi thẳng endpoint webhook để cộng hạn mức và kích hoạt gói `GROWTH_SME` mà không cần chuyển khoản. Hơn nữa, cả khối giao diện này xuất hiện trùng lặp 100% ở cả `/saas-billing` lẫn `/permissions`. Mong đợi: Webhook thanh toán chỉ được gọi từ máy chủ cổng thanh toán (PayOS/SePay) có chữ ký HMAC; trên giao diện khách hàng chỉ được phép có nút `"Tôi đã chuyển khoản — Kiểm tra trạng thái giao dịch"` (gọi API đọc trạng thái đơn hàng), và xóa bỏ hoàn toàn khối Gói cước trùng lặp khỏi trang `/permissions`.
   - **Bằng chứng:** Ảnh `chrome-suite7-saas-billing.png`, `chrome-suite7-permissions.png`, [SaasBilling.tsx](file:///d:/ERP_KHO/frontend/src/pages/SaasBilling.tsx#L159-L191), [Permissions.tsx](file:///d:/ERP_KHO/frontend/src/pages/Permissions.tsx#L305-L332).
   - **Tác động:** Không thể bán thương mại nếu khách hàng tự bấm nút giả lập webhook để xài miễn phí gói trả tiền; làm trang Phân quyền RBAC bị rối loạn chức năng.
   - **Cách sửa & Tiêu chí nghiệm thu:** Xóa khối SaaS Billing khỏi `Permissions.tsx`; trên `SaasBilling.tsx`, ẩn nút giả lập webhook khỏi bản production (hoặc đổi thành nút kiểm tra trạng thái thanh toán chỉ đọc). **Nghiệm thu:** Trên `/permissions` không còn khối VietQR/Gói cước; trên `/saas-billing` không còn nút client tự gửi payload `paidAmountVnd` lên webhook.

2. **`[🔴 Nghiêm trọng] SEC-003 & E6 — Bắt buộc sửa` — Màn `/permissions` hiển thị công khai Khóa bí mật 2FA (`Base32`), mã OTP 6 chữ số hiện tại và mã vạch 1D `"TOTP-2FA-QA"`**
   - **Bước tái hiện:** Mở `/permissions`, nhìn vào thẻ `"Xác thực Đa yếu tố Chuẩn RFC 6238"` ở cột bên phải.
   - **Thực tế vs Mong đợi:** Giao diện in thẳng `"Khóa Base32: JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP"` và `"Mã xác thực 6 chữ số: 893850"` (đồng thời điền sẵn `893850` vào ô nhập mã xác thực), bên cạnh mã QR lại vẽ thêm một mã vạch sọc 1D có chữ `"TOTP-2FA-QA"`. Mong đợi: Chỉ hiển thị mã QR 2D khi người dùng bấm `"Thiết lập 2FA mới"`, khóa Base32 phải ẩn sau nút `"Hiện khóa thủ công"`, tuyệt đối không bao giờ hiển thị hay tự điền mã OTP 6 số hiện tại lên màn hình, và xóa bỏ mã vạch 1D `"TOTP-2FA-QA"` (ứng dụng Google/Microsoft Authenticator không quét mã vạch sọc 1D).
   - **Bằng chứng:** Ảnh `chrome-suite7-permissions.png` và [Permissions.tsx](file:///d:/ERP_KHO/frontend/src/pages/Permissions.tsx#L156-L161).
   - **Tác động:** Vô hiệu hóa hoàn toàn ý nghĩa bảo mật của 2FA và để lộ dấu vết hàng giả/test (`TOTP-2FA-QA`) trước mắt Admin khách hàng.
   - **Cách sửa & Tiêu chí nghiệm thu:** Xóa dòng hiển thị `currentTotpCode` và `<BarcodeSvg value="TOTP-2FA-QA" />` trong `Permissions.tsx`, để trống ô nhập OTP (`""`). **Nghiệm thu:** Mở `/permissions` không còn thấy mã 6 số hiện hành, không còn `"TOTP-2FA-QA"`, ô nhập OTP trống.

3. **`[🔴 Nghiêm trọng] E5 — Bắt buộc sửa` — Lộ menu và route `/cost-valuation` (Giá vốn) & `/cod-reconciliation` (Đối soát COD) cho người dùng chỉ có quyền `inventory.read`**
   - **Bước tái hiện:** Kiểm tra [AppRoutes.tsx](file:///d:/ERP_KHO/frontend/src/routes/AppRoutes.tsx#L128-L130) và [productionNavigation.ts](file:///d:/ERP_KHO/frontend/src/navigation/productionNavigation.ts#L43-L44).
   - **Thực tế vs Mong đợi:** Cả 2 file đang kiểm tra `permission: 'inventory.read'`. Theo quy tắc bảo mật `PROJECT_RULES` (Lớp 3 & Role 5), mọi dữ liệu giá vốn, lợi nhuận gộp và đối soát tài chính phải được bảo vệ bởi quyền **`inventory.cost.read`**.
   - **Bằng chứng:** `AppRoutes.tsx:128-130` và `productionNavigation.ts:43-44`.
   - **Tác động:** Nhân viên kho thông thường nhìn thấy menu Giá vốn & Đối soát COD của Chủ shop.
   - **Cách sửa & Tiêu chí nghiệm thu:** Đổi `inventory.read` thành `inventory.cost.read` tại `AppRoutes.tsx:128-130` và `productionNavigation.ts:43-44`. **Nghiệm thu:** Đăng nhập bằng tài khoản Nhân viên kho (không có quyền `inventory.cost.read`) sẽ ẩn hoàn toàn 2 mục menu này và chặn `403` nếu truy cập URL trực tiếp.

---

## PHẦN II: 7 MỤC TỔNG HỢP CUỐI BÁO CÁO (FINAL SYNTHESIS)

### 1. Các vấn đề hệ thống lặp lại (Nhóm theo Mã Quy Tắc)

| Nhóm Mã Quy Tắc | Mô tả vấn đề hệ thống lặp lại | Số màn hình bị ảnh hưởng | Mức độ |
|---|---|---:|---|
| **`E6` (Vệ sinh thương mại SaaS)** | Thanh đầu trang ([MainLayout.tsx](file:///d:/ERP_KHO/frontend/src/layouts/MainLayout.tsx#L277-L313)) luôn hiển thị nút `"Sao chép Chẩn đoán Lỗi 1-Click gửi Zalo"` và huy hiệu `"MẶC ĐỊNH"` trên toàn bộ 35 màn hình sau đăng nhập; dữ liệu seed mang tiền tố `"QA-E2E-001"`, `"IR-QA-001"`, `"PO-QA-"`, `"PACK-QA-"`, `"TOTP-2FA-QA"` xuất hiện dày đặc; `/login` điền sẵn `qa_admin`. | **36 / 36 màn hình** | 🔴 Nghiêm trọng |
| **`M1` & `W2.5.8` (Kích thước vùng chạm)** | Class `.ui-btn` trong [production-ui.css](file:///d:/ERP_KHO/frontend/src/styles/production-ui.css#L202) đặt `min-height: 36px` và `.app-nav-link` cao `36px`, khiến mọi màn hình có từ `38` đến `89` nút nhỏ hơn `44×44px` (`< 48×48px` trên PDA). Đặc biệt có **6 màn hình** (`/`, `/import-receipts`, `/putaway-tasks`, `/picking-tasks`, `/packing-sessions`, `/products`) có phần tử `< 24×24px` (checkbox `16×16px`, link `60×15px`). | **36 / 36 màn hình** (`6` màn vi phạm `< 24px`) | 🟠 Lỗi nặng |
| **`M3`, `M4` & `W2.4.3` (Điều hướng & Topbar trên PDA `390×844`)** | Trên khung nhìn `390×844`, thanh `.app-topbar` bị rớt xuống 3 dòng cao **`156px`** (vượt chuẩn `≤ 56px` của `M3`), **không có thanh điều hướng đáy (`Bottom Nav` — `M4`)** cho 4 tác vụ kho cốt lõi (`Nhận hàng`, `Cất hàng`, `Lấy hàng`, `Kiểm kê`), và `.app-sidebar` ẩn ngoài màn hình vẫn không có thuộc tính `inert` / `visibility: hidden` nên chiếm `32` lần bấm phím `Tab` vô hình (`W2.4.3`). | **35 / 36 màn hình** | 🟠 Lỗi nặng |
| **`V2` & `W1.4.10` (Vỡ lưới KPI & Tràn ngang trên Mobile `390×844`)** | Các lưới KPI dùng inline style `repeat(4, 1fr)` hoặc `repeat(5, 1fr)` không co về 2 cột trên mobile, ép chiều rộng mỗi thẻ còn `53px–69px` và bẻ gãy từ tiếng Việt theo chiều dọc; màn `/stock-allocations` tràn ngang `+71px` (`461px > 390px`). | **9 / 36 màn hình** (`/asns`, `/import-receipts`, `/stock-allocations`, `/shipments`, `/stocktakes`, `/approvals`, `/warehouse-structure`, `/business-partners`, `/`) | 🔴 Nghiêm trọng |
| **`L3` (Chống gọi lặp API / HTTP `429` & Skeleton Loading)** | Khi chuyển trang hoặc tải trang, nhiều component gọi lặp cùng một API (`4x /api/v1/permissions/saas/subscription-billing`, `4x /api/putaway-tasks/location-warehouses`) làm chạm ngưỡng Rate Limiter của Backend, gây ra **`105` lỗi console `HTTP 429 Too Many Requests`** trên 12+ màn hình; trạng thái đang tải chỉ hiện `<p>Đang tải...</p>` làm nhảy bố cục (`CLS`). | **15 / 36 màn hình** | 🟠 Lỗi nặng |
| **`G4` (Chữ nhỏ `< 12px`)** | Nhãn phụ `.ui-stat-note`, badge phụ và text bên trong SVG dùng cỡ chữ `10px–11px` (`0.68rem–0.72rem`), đặc biệt dày đặc trên `/packing-sessions` (`75`), `/picking-tasks` (`67`), `/` (`59`), `/putaway-tasks` (`30`), `/import-receipts` (`29`). | **35 / 36 màn hình** | 🟡 Lỗi nhẹ / Trung bình |
| **`W4.1.2` (Thiếu nhãn truy cập `aria-label` cho control)** | Một số nút/ô chọn trong bảng hoặc thanh lọc chưa gắn `aria-label` hoặc `<label>` tường minh. | **8 / 36 màn hình** (`/asns`: 7, `/inventory-traceability`: 5, `/inventory-reconciliation`: 2, `/purchase-orders`: 1, `/import-receipts`: 1, `/putaway-tasks`: 1, `/picking-tasks`: 1, `/packing-sessions`: 1) | 🟠 Lỗi nặng |

---

### 2. So sánh Đặc tả Notion (`Spec 217` & `Spec 229`) vs Thực tế Triển khai

| Hạng mục trong Đặc tả Notion | Yêu cầu của Đặc tả (`Spec 217` / `Spec 229`) | Thực tế triển khai trên `http://localhost:5173` | Đánh giá độ khớp |
|---|---|---|---|
| **Độ phủ danh mục màn hình (`Spec 229`)** | Đủ 36 màn hình bao phủ Inbound, Outbound, Inventory 9 chiều, Tài chính COD/Giá vốn, SaaS Billing, RBAC, Master Data. | Đã triển khai đủ **36/36 route** hoạt động thật, kết nối API `.NET 10` và DB `ERP_KHO` thật. | ✅ **Đạt 100% độ phủ route** |
| **Bảng màu & Tương phản (`Spec 217`)** | Nền sáng `#f8fafc`, bề mặt thẻ `#ffffff`, nút chính `#0f172a`, chữ đạt chuẩn WCAG 2.2 AA (`≥ 4.5:1`). | Đúng chuẩn token CSS trong `production-ui.css`; kiểm tra tự động `contrastFailCount = 0` trên cả 36 màn hình. | ✅ **Đạt 100%** |
| **Số học bảng biểu `tabular-nums` (`Spec 217`)** | Cột số lượng/tiền tệ dùng `font-variant-numeric: tabular-nums` và căn phải đồng bộ cả `<th>` và `<td>`. | `tabular-nums` đã áp dụng cho `.text-right`, nhưng trên `Dashboard.tsx` và một số bảng phụ, `<td>` căn phải còn `<th>` vẫn căn trái (`E4`). | ⚠️ **Đạt 85% (Cần căn phải `<th>`)** |
| **Thiết bị cầm tay PDA `390×844` (`Spec 217`)** | Nút chạm tối thiểu `44×44px` (`48×48px` cho nút quét kho), Topbar gọn `≤ 56px`, có Bottom Nav 4-5 mục, lưới KPI tự co về 1-2 cột. | `.ui-btn` mới cao `36px`, checkbox `16×16px`; Topbar mobile cao `156px`; chưa có Bottom Nav; 8 màn hình bị vỡ lưới KPI 4-5 cột trên mobile. | ❌ **Chưa đạt chuẩn PDA thực chiến** |
| **Bảo mật RBAC & Giá vốn (`Spec 17/25`)** | Ẩn giá vốn và đối soát tài chính theo quyền `inventory.cost.read`; đồng bộ quyền giữa Menu và Route. | Backend có kiểm tra quyền, nhưng Frontend `AppRoutes.tsx` & `productionNavigation.ts` dùng nhầm `inventory.read` cho `/cost-valuation` & `/cod-reconciliation`, và đặt `access: 'always'` cho `/inventory-reconciliation`. | ❌ **Sai lệch cấu hình RBAC ở Frontend** |
| **Thanh toán SaaS VietQR (`Spec 229`)** | Hiển thị mã VietQR, đối soát tự động qua Webhook từ cổng thanh toán phía Server. | Hiển thị nút bấm giả lập Webhook ngay trên giao diện khách hàng và nhân bản ở cả `/saas-billing` lẫn `/permissions`. | ❌ **Vi phạm nghiêm trọng `E7`** |

---

### 3. Thống kê tuân thủ theo 9 Nhóm Quy Tắc & Điểm Tổng Hợp Toàn Hệ Thống

- **Điểm trung bình toàn hệ thống (36 màn hình × 13 tiêu chí):** **`2.83 / 5.0`** (Tương đương **`56.6 / 100`** trên thang khắt khe thương mại hóa — ở mức *"Đã chạy đủ tính năng kỹ thuật nhưng cần dọn sạch dấu vết QA/Dev và tối ưu PDA trước khi thu phí khách hàng thật"*).

| Nhóm Quy Tắc | Tổng số mã | Số mã Đạt (`Pass`) | Số mã Một phần (`Partial`) | Số mã Không đạt (`Fail`) | Không áp dụng (`N/A`) | Tỷ lệ Đạt thực chất |
|---|---:|---:|---:|---:|---:|---:|
| **1. Nielsen (`N1–N10`)** | 10 | 7 (`N1,N2,N4..N7,N9`) | 2 (`N3, N10`) | 1 (`N8` — nhồi nhét/trùng lặp) | 0 | **70.0%** |
| **2. Shneiderman (`S1–S8`)** | 8 | 7 (`S1..S6, S8`) | 1 (`S7`) | 0 | 0 | **87.5%** |
| **3. WCAG 2.2 AA (`W`)** | 16 | 11 | 1 (`W1.4.4`) | 4 (`W1.4.10, W2.4.3, W2.5.8, W4.1.2`) | 0 | **68.8%** |
| **4. Thị giác & Gestalt (`G1–G8`)** | 8 | 5 (`G2,G3,G5,G6,G7`) | 2 (`G1, G8`) | 1 (`G4` — chữ `< 12px`) | 0 | **62.5%** |
| **5. Định luật UX (`L1–L8`)** | 8 | 4 (`L4,L6,L7,L8`) | 2 (`L1, L2`) | 2 (`L3` — lỗi 429/CLS, `L5` — trang dài) | 0 | **50.0%** |
| **6. Kho vận cầm tay PDA (`M1–M6`)** | 6 | 1 (`M2` — ô quét barcode) | 2 (`M5, M6`) | 3 (`M1` vùng chạm, `M3` topbar, `M4` bottom nav) | 0 | **16.7%** |
| **7. ISO 9241 & Norman (`I1–I3`)** | 3 | 2 (`I1, I3`) | 1 (`I2` — tốc độ thao tác PDA) | 0 | 0 | **66.7%** |
| **8. SaaS ERP/WMS Thương mại (`E1–E8`)** | 8 | 2 (`E1, E2`) | 2 (`E3, E4`) | 4 (`E5` RBAC UI, `E6` lộ QA, `E7` webhook, `E8` wizard) | 0 | **25.0%** |
| **9. Tiếng Việt & Microcopy (`V1–V4`)** | 4 | 2 (`V3, V4`) | 0 | 2 (`V1` sót tiếng Anh, `V2` vỡ từ trên mobile) | 0 | **50.0%** |

---

### 4. Top 7 Ưu Tiên Sửa Trước (Xếp hạng theo Mức Độ Nghiêm Trọng 🔴 → Quick Wins)

1. **`[Ưu tiên #1 — 🔴 Chặn phát hành | E7, SEC-003, N8]` Gỡ bỏ nút giả lập Webhook Thanh toán SaaS, xóa thẻ Gói cước trùng lặp trên `/permissions`, và ẩn Khóa bí mật 2FA + mã OTP 6 số hiện tại:**
   - **File cần sửa:** [SaasBilling.tsx](file:///d:/ERP_KHO/frontend/src/pages/SaasBilling.tsx), [Permissions.tsx](file:///d:/ERP_KHO/frontend/src/pages/Permissions.tsx).
   - **Tác động:** Chặn đứng rủi ro khách hàng tự bấm kích hoạt gói cước trả phí miễn phí và vá lỗ hổng phơi bày mã 2FA trực tiếp trên màn hình.
2. **`[Ưu tiên #2 — 🔴 Chặn phát hành | E5]` Đồng bộ phân quyền RBAC trên Frontend cho `/cost-valuation`, `/cod-reconciliation` và `/inventory-reconciliation`:**
   - **File cần sửa:** [AppRoutes.tsx](file:///d:/ERP_KHO/frontend/src/routes/AppRoutes.tsx#L117-L130), [productionNavigation.ts](file:///d:/ERP_KHO/frontend/src/navigation/productionNavigation.ts#L38-L44).
   - **Tác động:** Đảm bảo chỉ vai trò có quyền `inventory.cost.read` mới thấy menu Giá vốn & Đối soát COD, và chỉ người có `inventory_ledger.read` mới thấy menu Đối chiếu tồn kho (Quick Win — chỉ 6 dòng code).
3. **`[Ưu tiên #3 — 🔴 Chặn phát hành | E6, V1, W3.3.8]` Dọn sạch 100% chuỗi QA/Debug rò rỉ trên giao diện thương mại & thêm nút Hiện/Ẩn mật khẩu trên `/login`:**
   - **File cần sửa:** [Login.tsx](file:///d:/ERP_KHO/frontend/src/pages/Login.tsx), [MainLayout.tsx](file:///d:/ERP_KHO/frontend/src/layouts/MainLayout.tsx), [Dashboard.tsx](file:///d:/ERP_KHO/frontend/src/pages/Dashboard.tsx), [ImportReceipts.tsx](file:///d:/ERP_KHO/frontend/src/pages/ImportReceipts.tsx).
   - **Tác động:** Xóa prefill `qa_admin`/`QaAdmin@123456!`, xóa prefill `'QA-E2E-001'`, ẩn nút `"Sao chép Chẩn đoán Lỗi 1-Click gửi Zalo"` vào menu Trợ giúp kỹ thuật, dịch `"CUSTOMER_RETURN"` sang `"Khách hoàn trả (RMA)"`.
4. **`[Ưu tiên #4 — 🔴 Nghiêm trọng trên Mobile PDA | V2, W1.4.10]` Sửa toàn bộ lưới KPI 4–5 cột sang Responsive Grid và triệt tiêu tràn ngang `+71px` trên `/stock-allocations`:**
   - **File cần sửa:** [production-ui.css](file:///d:/ERP_KHO/frontend/src/styles/production-ui.css), [ImportReceipts.tsx](file:///d:/ERP_KHO/frontend/src/pages/ImportReceipts.tsx), [AsnNotices.tsx](file:///d:/ERP_KHO/frontend/src/pages/AsnNotices.tsx), [StockAllocations.tsx](file:///d:/ERP_KHO/frontend/src/pages/StockAllocations.tsx), [Shipments.tsx](file:///d:/ERP_KHO/frontend/src/pages/Shipments.tsx), [Stocktakes.tsx](file:///d:/ERP_KHO/frontend/src/pages/Stocktakes.tsx), [Approvals.tsx](file:///d:/ERP_KHO/frontend/src/pages/Approvals.tsx), [WarehouseStructure.tsx](file:///d:/ERP_KHO/frontend/src/pages/WarehouseStructure.tsx), [BusinessPartners.tsx](file:///d:/ERP_KHO/frontend/src/pages/BusinessPartners.tsx).
   - **Tác động:** Chấm dứt hoàn toàn hiện tượng cột KPI rộng `55px` làm bẻ gãy từng chữ cái tiếng Việt và chấm dứt tràn ngang màn hình trên PDA `390×844`.
5. **`[Ưu tiên #5 — 🟠 Lỗi nặng PDA & A11y | M1, M3, M4, W2.5.8, W2.4.3]` Nâng chiều cao nút/checkbox, thu gọn Topbar Mobile `≤ 56px`, thêm Bottom Navigation 4 mục trên PDA và gắn `inert` cho Sidebar khi đóng:**
   - **File cần sửa:** [production-ui.css](file:///d:/ERP_KHO/frontend/src/styles/production-ui.css), [MainLayout.tsx](file:///d:/ERP_KHO/frontend/src/layouts/MainLayout.tsx), [Dashboard.tsx](file:///d:/ERP_KHO/frontend/src/pages/Dashboard.tsx).
   - **Tác động:** Đảm bảo `0` phần tử `< 24×24px`, nút bấm đạt `40px–48px` trên PDA, có thanh điều hướng đáy tiện bấm bằng ngón cái và không bị kẹt 32 lần bấm `Tab` trong menu ẩn.
6. **`[Ưu tiên #6 — 🟠 Lỗi nặng Hiệu năng & Mạng | L3]` Khử gọi lặp API (`Deduplicate Requests`) để triệt tiêu lỗi `HTTP 429 Too Many Requests` và thay `<p>Đang tải...</p>` bằng Skeleton giữ chiều cao:**
   - **File cần sửa:** Các hook/service gọi `/api/v1/permissions/saas/subscription-billing` và cấu hình Rate Limiter / caching trên Frontend.
   - **Tác động:** Xóa bỏ hoàn toàn `105` lỗi `429` khi chuyển trang nhanh và chống giật cục giao diện (`CLS = 0`).
7. **`[Ưu tiên #7 — 🟠 Lỗi nặng A11y & Bảng số | W4.1.2, E4, G4]` Bổ sung `aria-label` cho 19 control trên 8 màn hình, căn phải toàn bộ `<th>` cột số và nâng cỡ chữ nhỏ nhất lên `≥ 12px`:**
   - **File cần sửa:** [AsnNotices.tsx](file:///d:/ERP_KHO/frontend/src/pages/AsnNotices.tsx), [InventoryTraceability.tsx](file:///d:/ERP_KHO/frontend/src/pages/InventoryTraceability.tsx), [InventoryReconciliation.tsx](file:///d:/ERP_KHO/frontend/src/pages/InventoryReconciliation.tsx), [Dashboard.tsx](file:///d:/ERP_KHO/frontend/src/pages/Dashboard.tsx), [production-ui.css](file:///d:/ERP_KHO/frontend/src/styles/production-ui.css).

---

### 5. Checklist Nghiệm Thu Đo Được (`Measurable Acceptance Criteria`)

- [ ] **AC-01 (Bảo mật & Gói cước — `E7`, `SEC-003`):** Mở `/permissions` và `/saas-billing`: `document.querySelector('[data-testid="btn-confirm-vietqr-webhook"]') === null` trên bản thương mại; không còn chuỗi `JBSWY3DPEHPK3PXP` hay `TOTP-2FA-QA` hiển thị lộ thiên; `/permissions` không còn chứa thẻ VietQR.
- [ ] **AC-02 (Phân quyền RBAC — `E5`):** Đăng nhập bằng tài khoản Nhân viên kho (chỉ có `inventory.read`, không có `inventory.cost.read` và `inventory_ledger.read`): không nhìn thấy 3 mục menu `Đối soát COD 3 chiều`, `Giá vốn & kết xuất MISA`, `Đối chiếu tồn kho`; truy cập trực tiếp URL nhận trạng thái chặn quyền `403`.
- [ ] **AC-03 (Vệ sinh thương mại & Tiếng Việt — `E6`, `V1`):** Chạy lại script kiểm toán trên toàn bộ 36 màn hình: `leakedStrings.length === 0` (không còn `qa_admin` điền sẵn, `QA-E2E-001`, `CUSTOMER_RETURN`, hay nút `Sao chép Chẩn đoán Lỗi 1-Click gửi Zalo` trên thanh header chính).
- [ ] **AC-04 (Responsive Mobile PDA `390×844` — `W1.4.10`, `V2`):** Trên toàn bộ 36 màn hình ở khung nhìn `390×844`: `horizontalOverflow === false` (`scrollWidth <= 390`), `narrowMultiColGridsCount === 0` (không có cột lưới nào hẹp dưới `140px`).
- [ ] **AC-05 (Vùng chạm & Điều hướng PDA — `W2.5.8`, `M1`, `M3`, `M4`, `W2.4.3`):** Trên toàn bộ 36 màn hình: `under24Count === 0`; trên khung nhìn `390×844`, chiều cao `.app-topbar <= 56px`, có thanh `.app-bottom-nav` cố định ở đáy màn hình với 4–5 tác vụ chính cao `≥ 48px`, và khi `.app-sidebar` đóng thì nhấn `Tab` nhảy thẳng từ nút Menu sang `#main-content`.
- [ ] **AC-06 (Nhãn truy cập & Bảng số — `W4.1.2`, `E4`, `G4`):** Chạy lại script kiểm toán: `missingIconBtnLabel === 0`, `unlabelledInputs === 0`, `misalignedNumHeadersCount === 0`, và `smallTextUnder12px === 0`.
- [ ] **AC-07 (Ổn định Mạng — `L3`):** Duyệt liên tiếp qua cả 36 màn hình: `0` request trả về mã lỗi `HTTP 429 Too Many Requests` hoặc `5xx` trên Network/Console.

---

### 6. File Dữ Liệu & Bằng Chứng Đính Kèm
1. **Dữ liệu đo tự động chi tiết (JSON):** [ux_audit_metrics.json](file:///d:/ERP_KHO/ux_audit_metrics.json) (Bản sao trong thư mục Artifact: [ux_audit_metrics.json](file:///C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb/ux_audit_metrics.json))
2. **Bảng tổng hợp điểm 13 tiêu chí & thống kê lỗi 36 màn hình (CSV):** [ux_audit_summary.csv](file:///d:/ERP_KHO/ux_audit_summary.csv) (Bản sao trong thư mục Artifact: [ux_audit_summary.csv](file:///C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb/ux_audit_summary.csv))
3. **Bộ ảnh chụp màn hình thực tế (`1440×900` & `390×844`):** Lưu trữ tại thư mục Artifact `C:\Users\van40\.gemini\antigravity\brain\16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb\` (`login_1440x900_default.png`, `onboarding_wizard_1440x900_default.png`, `dashboard_1440x900_default.png`, `dashboard_390x844_default.png`, `import_receipts_1440x900_default.png`, `import_receipts_390x844_default.png`, `chrome-suite6..8-*.png`).

---

### 7. Danh sách dữ liệu `UX-TEST-` đã tạo cần dọn dẹp
- **Số bản ghi `UX-TEST-` đã tạo trong Database:** **`0` bản ghi**.
- **Cam kết an toàn:** Toàn bộ quá trình kiểm toán tuân thủ nghiêm ngặt quy tắc **An toàn & Không phá hủy dữ liệu**: không xóa/khóa/đóng băng dữ liệu có sẵn, không hủy/đảo phiếu của người khác, không kích hoạt thanh toán thật hay gửi thông báo Zalo/Email ra ngoài.
