# EXECUTIVE PRODUCT REASSESSMENT — ERP_KHO WMS SaaS

**Document ID:** Deliverable A (`EXECUTIVE_PRODUCT_REASSESSMENT.md`)  
**Run ID:** `2026-11-01_REASSESSMENT_001`  
**Baseline Commit:** `6e0a333390c1db6222e55ae3945b7ce33a56bef6` (`feat/WMS-61-project-development`)  
**Audit Date:** 2026-11-01  
**Audit Mode:** `READ-ONLY SYSTEM AUDIT + TO-BE DESIGN PROPOSAL`  
**Status:** `PHASE_7_COMPLETED — ALL 20 DELIVERABLES (A–T) VERIFIED`

---

## 1. Tóm Tắt Điều Hành (`Executive Verdict`)

### 1.1 Đánh Giá Tổng Thể (`Overall Readiness Scorecard`)
Sau khi rà soát toàn diện mã nguồn thực tế (`60` file Entity / `87` lớp Domain, `42` API Controllers, `60` Application Interfaces, `47` Infrastructure Services, `96` DbSets, `61` EF Core Migrations, `37+` Routes / `45+` trang React 19 + TypeScript) và đối chiếu với **23 hệ thống ERP/WMS thương mại & mã nguồn mở** cùng bộ Spec Notion chính thức (loại trừ vùng cấm `OWN-OOAD-001..004`), Hội đồng Đánh giá Độc lập đưa ra kết luận:

> **`ERP_KHO` sở hữu lõi kỹ thuật tồn kho (`9-Dimension Inventory Engine`, `SHA256 GrainHash`, `2-Tier Deterministic Locking`, `6 Append-Only Ledgers`) vượt trội hơn hầu hết các phần mềm WMS tầm trung và tương đương nhóm Enterprise Tier-1 (SAP EWM / D365 WMS). Tuy nhiên, để vận hành thực tế an toàn và mượt mà cho khách hàng trả phí đầu tiên (ICP: 1 kho, 5–30 nhân viên, bán hàng đa kênh Shopee/Lazada/TikTok Shop & phân phối B2B), hệ thống BẮT BUỘC phải xử lý 5 điểm chặn `P0` về Bảo mật SaaS, Quy trình Hàng hoàn (RMA), Chuẩn hóa RBAC và Trải nghiệm Máy quét/Giao diện trước khi Go-Live.**

| Trụ cột Đánh giá (`Assessment Pillar`) | Điểm số (`Score / 100`) | Xếp hạng (`Verdict`) | Tóm tắt Bằng chứng Cốt lõi (`FACT`) |
|---|:---:|---|---|
| **1. Độ chính xác Tồn kho & Sổ cái (`Data Integrity & 9D Ledger`)** | **94 / 100** | `AHEAD OF MARKET` | Định danh 9 chiều + `GrainHash` (`SHA256`), khóa 2 tầng (`sp_getapplock` + `UPDLOCK, ROWLOCK, REPEATABLEREAD`), 6 sổ cái bất biến (`Append-Only`), đối soát 3 chiều (`Check A/B`). Điểm trừ nhỏ: bảng `CustomerReturns` và `KitWorkOrders` chưa có `RowVersion` và `BaseUom` snapshot. |
| **2. Kiến trúc Clean Architecture & Concurrency (`Backend Architecture`)** | **84 / 100** | `STRONG WITH TECH DEBT` | `ERP.Domain` thuần khiết (`0` dependency). Có `[IdempotentCommand]`, `X-Correlation-ID`, Global Query Filter + Cross-Tenant FK Guard. Điểm trừ: 3 God Controllers (`ImportReceipts`, `ExportReceipts`, `Permissions`) ôm đồm endpoint chéo module và còn hàm `FallbackWhenNoOpsAsync` khi test không tiêm service phụ. |
| **3. Độ phủ Nghiệp vụ Kho & Tài chính VN (`Business Capabilities`)** | **82 / 100** | `PARITY / AHEAD (EXCEPT RMA UI)` | Rất mạnh ở Putaway 5 yếu tố, Wave/Cluster Picking, Packing Video Evidence, Handover Manifest AWB, Đối soát COD 3 chiều (NĐ 70/2025), Giá vốn bình quân gia quyền (MAC) & xuất MISA `.xlsx`. Điểm thiếu lớn nhất: **Chưa có màn hình Hàng hoàn / RMA (`/customer-returns`)** dù Backend đã có bảng & API. |
| **4. Bảo mật SaaS & Phân quyền RBAC (`Security & RBAC`)** | **62 / 100** | `NEEDS P0 REMEDIATION` | Có tách quyền `inventory.cost.read` ẩn giá vốn trên Server DTO và lọc `WarehouseScope`. **Nhưng có 2 lỗ hổng `P0` nghiêm trọng:** (1) Lưu TOTP 2FA trên RAM (`ConcurrentDictionary`) + nhận `Base32Secret` từ request body (`FND-001`), (2) `[AllowAnonymous]` đè lên `[PermissionAuthorize]` tại `ProvisionSaaSTenant` (`FND-002`), cùng 12+ lệnh `POST` dùng quyền `.read` + `!IsInRole("Viewer")` (`FND-004`). |
| **5. Phù hợp Quy trình Vận hành Thực tế (`Operational Fit for ICP`)** | **74 / 100** | `OVERENGINEERED IN PARTS` | Máy trạng thái `ReceiptStatus` (15 trạng thái) và `ShipmentStatus` (12 trạng thái) cùng luồng xuất tách rời 4 màn hình gây nhiều bước thao tác cho kho 5–10 người nếu không có nút gộp `Express 1-Step`. |
| **6. Giao diện UX/UI & Máy quét PDA (`UX/UI & Scanner Ergonomics`)** | **70 / 100** | `FRICTION / NEEDS REFACTOR` | Đã có chế độ `Express vs Full` và `/pda/*`, nhưng Sidebar phẳng 36+ mục gây ngợp, 9 trang React là God Component (`1,000–2,280 LOC`), và trình nghe súng quét Barcode chưa bắt phím toàn cục (HID Wedge) hay ký tự phân tách `GS1-128 FNC1` (`\x1d`). |

---

## 2. Danh Mục Đầy Đủ 20 Tài Liệu Bàn Giao (`Deliverables A → T`) & 4 Báo Cáo Kiểm Toán Chuyên Sâu

Toàn bộ tài liệu nằm tại thư mục độc lập [`d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/):

| Mã | Tên File Tài Liệu (Clickable Link) | Quy mô | Nội dung Chính |
|---|---|---:|---|
| **A** | [`EXECUTIVE_PRODUCT_REASSESSMENT.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/EXECUTIVE_PRODUCT_REASSESSMENT.md) | ~22 KB | Báo cáo tổng hợp điều hành, bảng điểm 6 trụ cột, tổng hợp 24 phát hiện (`FND-001..024`), 8 quyết định trình Owner (`ODP-001..008`) và lộ trình 4 giai đoạn. |
| **B** | [`CURRENT_STATE_SYSTEM_MAP.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/CURRENT_STATE_SYSTEM_MAP.md) | 107.2 KB | Bản đồ kiến trúc AS-IS toàn diện: 96 DbSets, 42 Controllers (kèm bảng kê từng endpoint), 60 Interfaces, 47 Services, 37+ Routes, 22 Enums, 5 Background Jobs. |
| **C** | [`COMPETITIVE_SOURCE_INVENTORY.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/COMPETITIVE_SOURCE_INVENTORY.md) | 38.5 KB | Hồ sơ nghiên cứu 23 hệ thống tham chiếu: **13 hệ thống thương mại** (`Oracle`, `D365`, `SAP EWM`, `Manhattan`, `Blue Yonder`, `Infor`, `Körber`, `NetSuite`, `Odoo`, `ERPNext`, `Logiwa`, `ShipHero`, `Extensiv`) & **10 repo mã nguồn mở** (`OpenBoxes`, `OCA WMS`, `ERPNext`, `OpenWMS`, `Odoo CE`, `GreaterWMS`, `ModernWMS`, `Indotalent`, `Sentry`, `Fleetbase`). |
| **D** | [`COMPETITIVE_CAPABILITY_MATRIX.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/COMPETITIVE_CAPABILITY_MATRIX.md) | 28.4 KB | Ma trận so sánh 25 chiều năng lực giữa `ERP_KHO` với 13 hệ thống thương mại và 10 hệ thống mã nguồn mở (`AHEAD`, `PARITY`, `BEHIND`, `MISSING`, `OVERENGINEERED_FOR_ICP`). |
| **E** | [`COMPETITIVE_GAP_ANALYSIS.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/COMPETITIVE_GAP_ANALYSIS.md) | 55.3 KB | Phân tích chuyên sâu **26 khoảng trống (`GAP-001` → `GAP-026`)** phủ kín 17 danh mục Gap bắt buộc. |
| **F** | [`AS_IS_BUSINESS_PROCESS_MAP.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/AS_IS_BUSINESS_PROCESS_MAP.md) | 78.5 KB | Bản đồ quy trình nghiệp vụ hiện tại (AS-IS) cho đủ **12 quy trình kho & tài chính** kèm sơ đồ `flowchart TD`, `stateDiagram-v2` và phân tích điểm nghẽn. |
| **G** | [`TO_BE_BUSINESS_PROCESS_BLUEPRINT.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/TO_BE_BUSINESS_PROCESS_BLUEPRINT.md) | 83.1 KB | Thiết kế tái cấu trúc quy trình TO-BE (12 quy trình) với đủ 16 mục bắt buộc/quy trình, hỗ trợ song song **Express 1-Step Mode** và **Full Multi-Step Mode**. |
| **H** | [`TO_BE_MODULE_ARCHITECTURE.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/TO_BE_MODULE_ARCHITECTURE.md) | 25.9 KB | Kiến trúc Modular Monolith 8 Bounded Contexts (`M01 ControlTower` → `M08 Governance`), tách `M05 Returns (RMA)` thành module hạng nhất và hợp nhất `AppPermissions`. |
| **I** | [`TO_BE_FUNCTION_CATALOG.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/TO_BE_FUNCTION_CATALOG.md) | 37.2 KB | Danh mục **132 chức năng nghiệp vụ (`FN-CTL-001` → `FN-GOV-010`)** gắn mã quyền RBAC, route `/api/v1/`, chế độ `Express/Full` và độ ưu tiên. |
| **J** | [`TO_BE_INFORMATION_ARCHITECTURE.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/TO_BE_INFORMATION_ARCHITECTURE.md) | 18.8 KB | Kiến trúc thông tin & điều hướng mới: thu gọn Sidebar từ **36+ mục xuống 18 Workspaces** (11 trong Express Mode) bằng **URL-Synced Sub-Tabs**, giữ 100% URL cũ không gãy test. |
| **K** | [`TO_BE_SCREEN_DESIGN_SPECIFICATION.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/TO_BE_SCREEN_DESIGN_SPECIFICATION.md) | 33.6 KB | Đặc tả thiết kế chi tiết **12 màn hình Workbench cốt lõi (`SCR-01` → `SCR-12`)**, Wireframe ASCII (`1440x900` & `390x844`), chuẩn 5 trạng thái UI và kế hoạch tách 13 God-Components (`< 380 LOC/file`). |
| **L** | [`TO_BE_DESIGN_SYSTEM.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/TO_BE_DESIGN_SYSTEM.md) | 16.2 KB | Hệ thống Design Token 3 tầng (`Primitive → Semantic → Component`), chuẩn chữ Tiếng Việt + `tabular-nums`, độ tương phản WCAG AA (`>= 4.8:1`), 6 component kho vận mới & mẫu in tem nhiệt `100x150mm`. |
| **M** | [`DATA_API_ARCHITECTURE_IMPACT.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/DATA_API_ARCHITECTURE_IMPACT.md) | 20.6 KB | Đánh giá tác động Schema DB (Expand-and-Contract cho `UserTotpEnrollments`, `CustomerReturns`, `KitWorkOrders`), chuẩn hóa dual-route `/api/v1/` và loại bỏ `FallbackWhenNoOpsAsync`. |
| **N** | [`SAAS_SECURITY_AND_PERMISSION_REVIEW.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/SAAS_SECURITY_AND_PERMISSION_REVIEW.md) | 18.9 KB | Báo cáo kiểm toán bảo mật SaaS, cô lập đa Tenant, ma trận phân quyền RBAC + SoD (Maker/Checker/Poster) và phương án vá 7 lỗ hổng (`SEC-F01` → `SEC-F07`). |
| **O** | [`REPORTING_AND_ANALYTICS_BLUEPRINT.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/REPORTING_AND_ANALYTICS_BLUEPRINT.md) | 13.4 KB | Thiết kế hệ thống báo cáo vận hành, Tháp điều khiển, XNT (`sp_GetInventoryInOutReport`), Đối soát COD, 3PL Billing, MISA `.xlsx` và tối ưu hiệu năng truy vấn SQL. |
| **P** | [`PRODUCT_ROADMAP.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/PRODUCT_ROADMAP.md) | 11.7 KB | Lộ trình phát triển 4 giai đoạn (`Phase 0` → `Phase 3`) bám sát ICP Việt Nam và danh sách các tính năng chủ động cắt giảm (YAGNI). |
| **Q** | [`ACCEPTANCE_AND_REGRESSION_PLAN.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/ACCEPTANCE_AND_REGRESSION_PLAN.md) | 11.9 KB | Kế hoạch kiểm thử nghiệm thu, ma trận 4 kịch bản tồn kho bắt buộc (`[TST-001]`) và 5 Cổng Chất lượng (`Gate 1 → Gate 5`). |
| **R** | [`FINDINGS_REGISTER.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/FINDINGS_REGISTER.md) | 56.0 KB | Sổ đăng ký toàn bộ **24 phát hiện kiểm toán (`FND-001` → `FND-024`)** kèm tọa độ `File:Line` chính xác, mức độ nghiêm trọng và hướng xử lý. |
| **S** | [`OWNER_DECISION_PROPOSALS.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/OWNER_DECISION_PROPOSALS.md) | 11.7 KB | **8 Đề xuất Quyết định Kiến trúc & Sản phẩm (`ODP-001` → `ODP-008`)** gắn nhãn `PENDING DECISION` trình Project Owner phê duyệt. |
| **T** | [`AUDIT_STATE.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/AUDIT_STATE.md) | ~9 KB | Hồ sơ trạng thái kiểm toán, nhật ký hoàn thành 7 Phases và biên bản tuân thủ Guardrails. |
| *Sup. 1* | [`BACKEND_AUDIT.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/BACKEND_AUDIT.md) | 35.3 KB | Báo cáo kiểm toán chi tiết tầng Backend (`.NET 10`, `EF Core`, `SQL Server`, `Controllers`, `Services`). |
| *Sup. 2* | [`FRONTEND_AUDIT.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/FRONTEND_AUDIT.md) | 45.5 KB | Báo cáo kiểm toán chi tiết tầng Frontend (`React 19`, Routes, State, Components, Scanner, RBAC UI). |
| *Sup. 3* | [`COMPETITIVE_COMMERCIAL_RESEARCH.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/COMPETITIVE_COMMERCIAL_RESEARCH.md) | 54.0 KB | Báo cáo nghiên cứu sâu 13 hệ thống WMS/ERP thương mại. |
| *Sup. 4* | [`COMPETITIVE_OPENSOURCE_RESEARCH.md`](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/COMPETITIVE_OPENSOURCE_RESEARCH.md) | 48.7 KB | Báo cáo nghiên cứu sâu 10 repository WMS/ERP mã nguồn mở. |

---

## 3. Tóm Tắt Các Phát Hiện Quan Trọng Nhất (`Top P0 & P1 Findings — FACT`)

Chi tiết đầy đủ 24 phát hiện nằm trong [FINDINGS_REGISTER.md](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/FINDINGS_REGISTER.md). Dưới đây là các phát hiện trọng yếu nhất có bằng chứng `File:Line`:

### 3.1 Nhóm `P0` — Chặn Go-Live (Bảo mật, Toàn vẹn Dữ liệu & Nghiệp vụ Cốt lõi ICP)
1. **`FND-001` (`P0 Security` — Lỗ hổng xác thực 2FA TOTP nghiêm trọng):**
   - Tại [`AccountSecurityController.cs:122`](file:///d:/ERP_KHO/ERP.Api/Controllers/AccountSecurityController.cs#L122), trạng thái đăng ký 2FA TOTP đang lưu trong biến tĩnh trên RAM (`ConcurrentDictionary<int, TotpEnrollmentState> TotpEnrollments`), mất sạch khi restart API.
   - Nghiêm trọng hơn, tại [`AccountSecurityController.cs:216-219, 577-584`](file:///d:/ERP_KHO/ERP.Api/Controllers/AccountSecurityController.cs#L216-L219), API nhận `request.Base32Secret` / `request.SecretKey` từ chính JSON body của client gửi lên để tự kiểm tra mã OTP, và nếu trống thì fallback về chuỗi băm tĩnh đoán trước được `SHA256("ERP_WMS_RFC6238_TOTP_SECRET:{userId}:{username}")` (dòng `589–594`).
2. **`FND-002` (`P0 Security` — Lộ endpoint khởi tạo Tenant không cần xác thực):**
   - Tại [`PermissionsController.cs:128-144`](file:///d:/ERP_KHO/ERP.Api/Controllers/PermissionsController.cs#L128-L144), action `ProvisionSaaSTenant` gắn đồng thời `[AllowAnonymous]` và `[PermissionAuthorize(AppPermissions.PermissionRead)]`. Do [`PermissionAuthorizeAttribute.cs:61-64`](file:///d:/ERP_KHO/ERP.Api/Authorization/PermissionAuthorizeAttribute.cs#L61-L64) bỏ qua kiểm tra khi thấy `IAllowAnonymous`, bất kỳ request ẩn danh nào cũng gọi được API tạo mới `Company`, `User`, `Role`, `Warehouse` trong DB.
3. **`FND-003` (`P0 Product Gap` — Thiếu màn hình Hàng hoàn / RMA cho nhà bán hàng TMĐT):**
   - Trong vận hành Shopee/TikTok Shop/Lazada tại Việt Nam, tỷ lệ hoàn hàng giao thất bại chiếm `10%–25%`. Mặc dù Backend đã có thực thể [`CustomerReturn`](file:///d:/ERP_KHO/ERP.Domain/Entities/CustomerReturnEntities.cs#L5-L40) và 3 API RMA nằm nhờ trong [`ImportReceiptsController.cs:148-200`](file:///d:/ERP_KHO/ERP.Api/Controllers/ImportReceiptsController.cs#L148-L200), nhưng Frontend **hoàn toàn chưa có route hay màn hình `/customer-returns`** để nhân viên kho quét mã vận đơn hoàn, phân loại tình trạng (`GOOD`, `DAMAGED_BOX`, `DEFECTIVE`) và quyết định xử lý (`RESTOCK_AVAILABLE`, `REFURBISH_REPACK`, `RTV_VENDOR`, `SCRAP_DISPOSAL`).
4. **`FND-004` (`P0 RBAC` — Dùng quyền `.read` + `!IsInRole("Viewer")` cho các API `POST` biến đổi dữ liệu):**
   - Hơn 12 endpoint `POST` thay đổi dữ liệu tại [`ImportReceiptsController.cs:155, 195`](file:///d:/ERP_KHO/ERP.Api/Controllers/ImportReceiptsController.cs#L155), [`ExportReceiptsController.cs:202, 226, 243, 255, 267, 328, 337, 375`](file:///d:/ERP_KHO/ERP.Api/Controllers/ExportReceiptsController.cs#L202), [`ReportsController.cs:140`](file:///d:/ERP_KHO/ERP.Api/Controllers/ReportsController.cs#L140), và [`SaasBillingController.cs:41, 57`](file:///d:/ERP_KHO/ERP.Api/Controllers/SaasBillingController.cs#L41) đang dùng `[PermissionAuthorize(AppPermissions.*Read)]` kèm câu lệnh thủ công `if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();`, vi phạm quy tắc `[SEC-001]`.
5. **`FND-005` (`P0 RBAC` — Chặn nhầm vai trò Kế toán trên báo cáo Xuất-Nhập-Tồn):**
   - Tại [`ReportsController.cs:301, 323`](file:///d:/ERP_KHO/ERP.Api/Controllers/ReportsController.cs#L301-L323), endpoint `GET /api/v1/reports/inventory-in-out-stock` và `/export` gắn cứng `[Authorize(Roles = AppRoles.AdminManagerOrViewer)]`, khiến vai trò `Accountant` (Kế toán kho) và `WarehouseStaff` dù được cấp quyền `report.inventory.read` vẫn bị chặn `403 Forbidden`.

### 3.2 Nhóm `P1` — Kiến trúc, Hiệu năng, Quy trình & Trải nghiệm Sàn kho
6. **`FND-006` (`P1 Architecture`):** Phân mảnh 2 file `AppPermissions.cs` ([`ERP.Domain/Security/AppPermissions.cs`](file:///d:/ERP_KHO/ERP.Domain/Security/AppPermissions.cs) chỉ có 5 hằng số so với ~85 hằng số tại [`ERP.Api/Authorization/AppPermissions.cs`](file:///d:/ERP_KHO/ERP.Api/Authorization/AppPermissions.cs)), và gộp chung 3 hằng số khác nhau (`InventoryMovementCreate`, `InventoryWrite`, `InventoryOpeningBalanceImport`) vào cùng một chuỗi `"inventory_movement.create"`.
7. **`FND-007` & `FND-008` (`P1 Clean Architecture & Anti-Fake`):** Các controller lớn (`ImportReceiptsController`, `ExportReceiptsController`, `PermissionsController`) chứa nhiều endpoint đặt sai module và vẫn duy trì tham số constructor tùy chọn `= null` kèm hàm `FallbackWhenNoOpsAsync` trả kết quả giả lập khi chạy unit test không truyền service phụ.
8. **`FND-010` (`P1 Performance`):** `GET /api/v1/import-receipts` và `GET /api/v1/export-receipts` gọi `GetAllAsync()` nạp toàn bộ chứng từ vào RAM trước khi phân trang, và các hàm tổng hợp pipeline đọc toàn bộ bảng rồi `.GroupBy()` trên bộ nhớ C#.
9. **`FND-011` & `FND-012` (`P1 Frontend & IA`):** 9 trang Frontend vượt `1,000–2,280 LOC` (`Permissions.tsx` 2,280 dòng, `ImportReceipts.tsx` 1,991 dòng, `Stocktakes.tsx` 1,568 dòng...) và thanh Sidebar hiển thị 36+ mục phẳng gây quá tải nhận thức.
10. **`FND-013` (`P1 Scanner UX`):** Thiếu trình lắng nghe sự kiện bàn phím toàn cục (`HID Keyboard-Wedge`) cho súng bắn mã vạch laser công nghiệp và chưa xử lý ký tự `ASCII 29 (\x1d - FNC1)` khi quét mã `GS1-128` biến thiên độ dài.

---

## 4. Tóm Tắt Phương Án Thiết Kế TO-BE & Lộ Trình Hoàn Thiện

### 4.1 Kiến trúc & Quy trình TO-BE Đề xuất
1. **8 Module Nghiệp vụ Rõ Ranh giới (`Deliverable H`):** Tổ chức lại thành `M01 ControlTower`, `M02 Inbound`, `M03 Outbound`, `M04 Inventory Engine`, `M05 Returns (RMA - Module mới)`, `M06 Finance & 3PL`, `M07 Catalog & Topology`, `M08 IAM & Governance`.
2. **Cơ chế Độ phức tạp Thích ứng (`Express 1-Step` vs `Full Multi-Step` — `Deliverable G`):**
   - Kho nhỏ (5–10 nhân viên) dùng chế độ **Express 1-Step**: Nhận hàng & Ghi sổ thẳng vào kệ lưu trữ trong 1 bước (`POST /api/v1/import-receipts/{id}/express-post`), hoặc Duyệt & Xuất kho nhanh không bắt buộc qua Dock/Yard hay Wave phức tạp, nhưng **vẫn đi qua 100% `InventoryEngine` 9 chiều và khóa dòng chuẩn**.
   - Kho lớn (15–30+ nhân viên) bật **Full Mode**: Đầy đủ ASN → Dock → Nhận hàng vào `RCV-01` → QC → Xử lý lệch → Putaway có chỉ dẫn; và SO → Giữ chỗ → Phân bổ FEFO → Wave/Cluster Pick → Pack + Video → Handover Manifest AWB.
3. **Cấu trúc Điều hướng Hợp nhất (`36+ mục → 18 Workspaces` — `Deliverable J & K`):**
   - Gom các màn hình con liên quan vào thanh **URL-Synced Workspace Sub-Tabs** ngay đầu trang, giữ nguyên 100% đường dẫn URL hiện hữu để không phá vỡ bất kỳ bài test E2E/Vitest nào.
   - Bổ sung màn hình **`/customer-returns` (`SCR-08`)** cho luồng Hàng hoàn TMĐT và nâng cấp chế độ **PDA Sàn kho (`/pda/*`, `390x844`)** với 6 ô tác vụ lớn + HID Barcode Wedge toàn cục.

### 4.2 Tám Quyết Định Cần Project Owner Phê Duyệt (`PENDING DECISION — Deliverable S`)
Trước khi bắt tay vào viết code ở giai đoạn thực thi, Project Owner cần xem xét và chốt **8 đề xuất (`ODP-001` → `ODP-008`)** trong [OWNER_DECISION_PROPOSALS.md](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/OWNER_DECISION_PROPOSALS.md):
- **`ODP-001` (`P0`):** Lưu trữ khóa bí mật TOTP 2FA mã hóa xuống bảng SQL `UserTotpEnrollments` và khóa bảo mật cho API `ProvisionSaaSTenant`.
- **`ODP-002` (`P0`):** Chuẩn hóa 100% mã quyền RBAC `<resource>.<action>`, xóa bỏ kiểm tra tên Role cứng (`!IsInRole("Viewer")` / `AdminManagerOrViewer`) và hợp nhất 1 file `AppPermissions.cs` duy nhất tại `ERP.Domain`.
- **`ODP-003` (`P0`):** Xây dựng màn hình Hàng hoàn TMĐT (`/customer-returns`) và tách `CustomerReturnsController` (`/api/v1/customer-returns`) + Migration nâng cấp bảng `CustomerReturns`.
- **`ODP-004` (`P1`):** Tách các API đặt sai chỗ khỏi `ImportReceiptsController`, `ExportReceiptsController`, `PermissionsController` (giữ dual-route tương thích ngược) và xóa bỏ `FallbackWhenNoOpsAsync`.
- **`ODP-005` (`P1`):** Áp dụng nút gộp **Express 1-Step** cho kho nhỏ dựa trên `WarehousePolicy` mà không bỏ qua bất biến tồn kho 9 chiều.
- **`ODP-006` (`P1`):** Thu gọn Sidebar từ 36+ mục xuống 18 Workspaces kèm URL-Synced Sub-Tabs và tách 9 trang God-Component (`>1,000 LOC`) thành các module `< 380 LOC`.
- **`ODP-007` (`P1`):** Nâng cấp kiến trúc PDA Scanner (HID Keyboard-Wedge toàn cục + ký tự `GS1-128 FNC1 \x1d` + phản hồi âm thanh/rung).
- **`ODP-008` (`P2`):** Lộ trình chuyển đổi từ Adapter mô phỏng nội bộ sang kết nối HTTP/Webhook thật cho sàn TMĐT (Shopee/TikTok Shop), hãng vận chuyển (GHN/GHTK/ViettelPost/J&T) và HĐĐT NĐ 70/2025.
