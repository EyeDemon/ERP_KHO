# BÁO CÁO XÁC NHẬN ĐỒNG BỘ TÀI LIỆU THIẾT KẾ TRÊN NOTION (`NOTION_Q4_2026_SPEC_UPDATE_REPORT`)

- **Ngày thực hiện cập nhật & kiểm chứng:** `24/10/2026`
- **Không gian tài liệu gốc trên Notion:** `ERP WMS – Tài liệu Thiết kế Kỹ thuật Hệ thống` (`Trang gốc 3de558fb-6174-80b9-8a4a-e4b7694b3c63`)
- **Cơ sở phê duyệt:** Phần 5 (`NOTION SPEC UPGRADE BLUEPRINT`) trong báo cáo [`AUDIT_2026_BENCHMARK_VS_NOTION_AND_CODE.md`](file:///d:/ERP_KHO/AUDIT_2026_BENCHMARK_VS_NOTION_AND_CODE.md)
- **Tuân thủ Kỷ luật & Vùng cấm (`PROJECT_RULES.md`):**
  - Bảo toàn 100% nội dung gốc (`Append / Insert Only — Zero Data Loss`).
  - Bảo toàn 100% các bất biến lõi đã đóng băng (`OD-01..OD-14` & `GAP-P0/P1`).
  - Tuân thủ tuyệt đối Vùng cấm Blacklist: **Không mở, không đọc, không tìm kiếm, không sửa, không trích dẫn** 4 trang `OWN-OOAD-001..004`.

---

## I. BẢNG ĐỐI CHIẾU & XÁC NHẬN CẬP NHẬT 7 HẠNG MỤC SPEC TRÊN NOTION (CHUẨN Q4/2026)

| STT | Mã Spec & Tên Trang | Notion Page ID | Mục Mới Đã Bổ Sung | Trạng Thái Kiểm Chứng (`VERIFIED 100%`) |
| :---: | :--- | :--- | :--- | :--- |
| **1** | **`Spec 37` — ERP WMS – 37. Handling Unit, Packing & Consolidation Engine Specification** | `3de558fb-6174-8107-ade5-e201ac42abd6` | - **`§5A. 1-Scan Full-LPN / Handling Unit Atomic Move (Chuẩn Q4/2026)`**<br>- **`§10A. 3D Volumetric & Weight Cartonization Engine & DIM Weight Guard (Chuẩn Q4/2026)`** | **VERIFIED 100%** (`notion-fetch` xác nhận lúc `2026-10-24T15:57:59.300Z` — nguyên vẹn 100% các mục `§1..§14` và Phụ lục). |
| **2** | **`Spec 39` — ERP WMS – 39. Warehouse Transfer & In-Transit Inventory Specification**<br>*(Đồng bộ kèm `Spec 44` — Replenishment & Min-Max Planning & `Spec 37 §5A`)* | - `3de558fb-6174-8141-8156-e041d3afd350` (`Spec 39`)<br>- `3de558fb-6174-8105-9f6e-e78eccb0d5f6` (`Spec 44`) | - **`§6A. 1-Scan Full-LPN / Handling Unit Atomic Move (Chuẩn Q4/2026)`**<br>- **`§8A. Auto Break-Bulk Pick-Face Replenishment (Bổ sung Kệ Nhặt Lẻ Kèm Xả Thùng — Chuẩn Q4/2026)`** | **VERIFIED 100%** (`notion-fetch` xác nhận `Spec 39` lúc `2026-10-24T15:58:02.257Z` & `Spec 44` lúc `2026-10-24T15:58:04.869Z` — nguyên vẹn 100% nội dung gốc). |
| **3** | **`Spec 40` — ERP WMS – 40. Cycle Count & Inventory Adjustment Specification** | `3de558fb-6174-818d-af9a-c5bb08999819` | - **`§5A. Multi-Attempt Count History Schema (Attempt 1 / Attempt 2) & 8-State StocktakeStatus Alignment (Chuẩn Q4/2026)`**<br>- **`§8A. Auto-Approval API Contract (< 1% AND <= 500.000 VNĐ — Chuẩn Q4/2026)`** | **VERIFIED 100%** (`notion-fetch` xác nhận lúc `2026-10-24T15:58:07.849Z` — nguyên vẹn 100% các mục `§1..§15` và Phụ lục). |
| **4** | **`Spec 43` — ERP WMS – 43. Customer Returns, RMA & Disposition Specification** | `3de558fb-6174-8196-8070-c0602ae69ba7` | - **`§9A. Return-In-Transit Aging (>7 Days) & 48h Marketplace Dispute Tracker (Chuẩn Q4/2026)`** | **VERIFIED 100%** (`notion-fetch` xác nhận lúc `2026-10-24T15:58:10.512Z` — nguyên vẹn 100% các mục `§1..§14`). |
| **5** | **`Spec 45` — ERP WMS – 45. Wave, Batch & Cluster Picking Specification** | `3de558fb-6174-81af-af2b-f91c5b79b523` | - **`§10A. Put-to-Wall & Cluster Tote Slot Sortation Contract (Chuẩn Q4/2026)`** | **VERIFIED 100%** (`notion-fetch` xác nhận lúc `2026-10-24T15:59:25.923Z` — nguyên vẹn 100% các mục `§1..§15`). |
| **6** | **`Spec 52` — ERP WMS – 52. Kitting, Bundling & Assembly Execution Specification** | `3de558fb-6174-8195-9e01-e7a7af2cc509` | - **`§7A. Post-Campaign De-Kitting / Bundle Disassembly Engine & Omnichannel SKU Mapping Workbench (Chuẩn Q4/2026)`** | **VERIFIED 100%** (`notion-fetch` xác nhận lúc `2026-10-24T15:59:57.384Z` — nguyên vẹn 100% các mục `§1..§17`). |
| **7** | **`Spec 283` — 283. SaaS Subscription, Usage Metering, VietQR Auto-Billing & 3-Way COD Reconciliation Specification** | `400558fb-6174-81f5-aedb-c43bcf5c3749` | - **`§7.3. Cấu trúc Biểu Phí Sàn TMĐT 2026, Khấu Trừ Thuế Tại Nguồn 1,5% (Nghị định 70/2025/NĐ-CP) & 3PL Daily Storage Occupancy Snapshot (Chuẩn Q4/2026)`** | **VERIFIED 100%** (`notion-fetch` xác nhận lúc `2026-10-24T15:59:31.148Z` — nguyên vẹn 100% các mục `§1..§7.2` và Phụ lục). |

---

## II. TÓM TẮT NỘI DUNG KỸ THUẬT 5 LỚP (`LAYER 1 → LAYER 4`) ĐÃ CHUẨN HÓA TRÊN NOTION

1. **`Spec 37` (`3de558fb-6174-8107-ade5-e201ac42abd6`):**
   - **`§5A`:** Đặc tả lệnh di chuyển nguyên Pallet/Khay `POST /api/v1/inventory-movements/move-handling-unit` với khóa 2 tầng 9 chiều (`AcquireInventoryAllocationLocksAsync`), cập nhật `RefreshGrainHash()` và cặp bút toán `-Q / +Q` cho mọi dòng `InventoryStock` thuộc `HandlingUnitId`.
   - **`§10A`:** Đặc tả công thức tính thể tích 3D $\text{TotalItemVolumeM3} = \sum (\text{Qty}_i \times \text{Product.UnitVolumeM3}_i)$, trọng lượng tịnh $\text{TotalNetWeightKg} = \sum (\text{Qty}_i \times \text{Product.UnitWeightKg}_i)$, hệ số lấp đầy $\le 85\%$, trọng lượng cả bì $\text{GrossWeightKg} = \text{TotalNetWeightKg} + \text{TareWeightKg}$, trọng lượng quy đổi thể tích ĐVVC $\text{DimWeightKg} = \frac{L \times W \times H}{6000}$, cờ cảnh báo `isDimWeightWarning` và hợp đồng API `POST /api/v1/packing-sessions/{id}/recommend-carton`.
2. **`Spec 39` (`3de558fb-6174-8141-8156-e041d3afd350`) & `Spec 44` (`3de558fb-6174-8105-9f6e-e78eccb0d5f6`):**
   - **`§6A`:** Chuẩn hóa quy trình `1-Scan Full-LPN / Handling Unit Atomic Move` (`POST /api/v1/inventory-movements/move-handling-unit`).
   - **`§8A`:** Chuẩn hóa công thức tự động bổ sung kệ nhặt lẻ kèm xả thùng (`Auto Break-Bulk Pick-Face Replenishment`): tính $\text{CaseCountToBreak} = \lceil \text{RawDeficitBaseQty} / \text{ConversionFactor} \rceil$, rút nguyên thùng từ vị trí `RESERVE` và quy đổi sang đơn vị cơ sở tại `PICK_FACE`.
3. **`Spec 40` (`3de558fb-6174-818d-af9a-c5bb08999819`):**
   - **`§5A`:** Chuẩn hóa bảng ánh xạ đầy đủ 8 trạng thái `StocktakeStatus` (`0: Nháp` $\rightarrow$ `7: Từ chối duyệt`) đồng nhất giữa Backend Enum và `Stocktakes.tsx`, cùng cấu trúc lưu vết `CountAttempt1Qty`, `CountAttempt2Qty`, `CountAttemptCount`.
   - **`§8A`:** Đặc tả hợp đồng API `POST /api/v1/stocktakes/{id}/evaluate-auto-approval` và `POST /api/v1/stocktakes/{id}/record-recount` với ngưỡng tự động duyệt kép `|VariancePct| < 1%` VÀ `|VarianceValue| <= 500.000 VNĐ`.
4. **`Spec 43` (`3de558fb-6174-8196-8070-c0602ae69ba7`):**
   - **`§9A`:** Đặc tả công thức tính tuổi nợ hàng hoàn đang đi đường (`returnAgingDays`), cờ cảnh báo đỏ `isOverdueInTransit` khi quá **7 ngày** chưa về kho, và đồng hồ đếm ngược **48 giờ** khiếu nại đền bù sàn (`disputeRemainingHours`) gắn với `PackingVideoEvidences` trên `GET /api/v1/import-receipts/customer-returns`.
5. **`Spec 45` (`3de558fb-6174-81af-af2b-f91c5b79b523`):**
   - **`§10A`:** Đặc tả quy trình chia chọn đa đơn tại tường chia hàng (`Put-to-Wall`) và xe đẩy nhiều giỏ (`Cluster Cart`): gán ô `SlotCode` (`SLOT-01..SLOT-08`) và hợp đồng API `GET /api/v1/picking-tasks/{id}/put-to-wall-slots` & `POST /api/v1/picking-tasks/{id}/put-to-wall-sort`.
6. **`Spec 52` (`3de558fb-6174-8195-9e01-e7a7af2cc509`):**
   - **`§7A`:** Đặc tả lệnh xả bộ hàng lắp ráp sau chiến dịch Mega Sale (`operationMode = "DEKIT_DISASSEMBLE"` trên `POST /api/v1/packing-sessions/execute-kitting-vas`) và hợp đồng API `GET/POST /api/v1/products/channel-sku-mappings` cho bàn làm việc ánh xạ SKU đa sàn trên `/products`.
7. **`Spec 283` (`400558fb-6174-81f5-aedb-c43bcf5c3749`):**
   - **`§7.3`:** Đặc tả công thức bóc tách **Thuế sàn TMĐT khấu trừ tại nguồn `1,5%` (`1,0% GTGT + 0,5% TNCN` theo Nghị định 70/2025/NĐ-CP)**, phí xử lý đơn cố định (`3.000 VNĐ/đơn`), công thức chụp **Snapshot Chiếm chỗ Lưu kho 3PL Hàng ngày (`GET/POST /api/v1/reports/3pl-storage-snapshot`: `Pallet-Day`, `CBM-Day`, `Bin-Day`)** và quy tắc DI Clean Architecture cho `SaasBillingController` & `DataImportsController`.
