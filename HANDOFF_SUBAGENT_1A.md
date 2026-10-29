# HANDOFF REPORT — SUBAGENT 1A (WAVE 55: `[W55-01, W55-04]`)

- **Subagent ID:** `afc2f112-342d-4ad0-ad06-942866c3ad4e`
- **Role:** Subagent 1A — Backend API & QA Seed cho `/cost-valuation` (`[W55-01]`) và `/handover-manifests` (`[W55-04]`)
- **Status:** `DONE` (`0 Warning(s), 0 Error(s)` trên `dotnet build ERP.slnx -c Release`, `17/17 PASS` trên `Wave48To51FullClosureTests`, `check-encoding.ps1` `Exit Code 0`)

---

## 1. Đã thay đổi gì (Danh sách file & Tọa độ `File:Line`) — `[FACT]`

### 1.1. `[W55-01]` Bổ sung Định giá Tồn kho chi tiết theo SKU (`skuValuations`, `skuItems`, `items`) & RBAC Masking (`inventory.cost.read`)
- [`ERP.Api/Controllers/ReportsController.cs`](file:///d:/ERP_KHO/ERP.Api/Controllers/ReportsController.cs#L1069-L1285):
  - `L1073-1081`: Lấy `accessibleIds` từ `_warehouseAuth.GetAccessibleWarehouseIdsAsync` và kiểm tra quyền truy cập kho.
  - `L1094-1166`: Truy vấn `InventoryStocks.AsNoTracking().Include(s => s.Product).ThenInclude(p => p.Unit).Include(s => s.Warehouse).Where(s => s.Quantity > 0m)` (lọc theo `warehouseId` hoặc `accessibleIds`), gom nhóm theo từng SKU có tồn dương và tính trung thực từ DB:
    + `productId`, `productCode`, `productName`, `warehouseName`, `unitName`
    + `onHandQuantity` (và alias `onHandQty`)
    + `availableQuantity` (và alias `availableQty`)
    + `movingAverageCost` (và alias `movingAverageUnitCost`): trung bình có trọng số từ `InventoryStock.MovingAverageCost` (hoặc `Product.MovingAverageCost`) khi `canReadCost == true`, và trả `null` khi user không có quyền `inventory.cost.read` (`costHiddenByRbac == true`).
    + `totalValuationAmount` (và alias `totalInventoryValue`): `Math.Round(onHandQuantity * weightedMac, 0)` khi `canReadCost == true`, và trả `null` khi `costHiddenByRbac == true`.
  - `L1278-1280`: Trả về danh sách định giá dưới cả 3 khóa tương thích `skuValuations`, `skuItems`, `items`.

### 1.2. `[W55-04]` Chuẩn hóa Tổng số kiện (`TotalPackages >= 1`) & Nhãn trạng thái Tiếng Việt (`statusLabel`) cho Biên bản Bàn giao (`/handover-manifests`) + QA Seed
- [`ERP.Infrastructure/Services/HandoverManifestService.cs`](file:///d:/ERP_KHO/ERP.Infrastructure/Services/HandoverManifestService.cs#L64-L140):
  - `L93-124`: Trong `GetHandoverManifestsAsync`, tính `var packageCount = m.TotalPackages > 0 ? m.TotalPackages : (m.Lines?.Count > 0 ? m.Lines.Count : 1);` và trả đầy đủ `totalPackages = packageCount`, `totalParcels = packageCount`, `awbCount = packageCount` cùng `statusLabel = TranslateManifestStatusLabel(m.Status)`.
  - `L128-137`: Hàm `TranslateManifestStatusLabel` ánh xạ `"OPEN"` / `"DRAFT"` -> `"Đang mở nhận kiện"`, `"SEALED"` / `"DISPATCHED"` / `"COMPLETED"` -> `"Đã niêm phong & Bàn giao"`, `"CANCELLED"` -> `"Đã hủy biên bản"`.
  - `L357-362` & `L578-581`: Đảm bảo `SealHandoverManifestAsync` và `ConfirmCarrierHandoverAsync` luôn giữ `TotalPackages >= 1`.
- [`tools/ERP.QaSeed/Program.cs`](file:///d:/ERP_KHO/tools/ERP.QaSeed/Program.cs#L3758-L3872):
  - `L3759-3766`: Đảm bảo các sản phẩm chuẩn (`QA-E2E-001`, `QA-LOT-002`, ...) và `InventoryStocks` luôn có `MovingAverageCost > 0`.
  - `L3832-3870`: Seed `MAN-VTP-20261021-01` với `TotalPackages = 3` (kèm 3 dòng `HandoverManifestLines`) và `MAN-GHN-20261021-02` với `TotalPackages = 2` (kèm 2 dòng `HandoverManifestLines`), đồng thời cập nhật idempotent mọi bản ghi `HandoverManifests` có `TotalPackages <= 0` thành `Math.Max(1, manifestLinesCount)`.

### 1.3. Kiểm thử Backend (`Wave48To51FullClosureTests.cs`)
- [`ERP.Api.Tests/Wave48To51FullClosureTests.cs`](file:///d:/ERP_KHO/ERP.Api.Tests/Wave48To51FullClosureTests.cs#L1435-L1619):
  - Bổ sung `[Fact]` `Wave55_OwnerGrossProfit_ReturnsNonEmptySkuValuations_And_HandoverManifests_ReturnPositivePackageCounts` kiểm chứng:
    1. `ReportsController.GetOwnerGrossProfit` trả về `skuValuations` (`>= 2` SKU) với `onHandQuantity > 0`, `movingAverageCost > 0`, `totalValuationAmount == onHandQuantity * movingAverageCost` cho Admin/Manager, và ẩn `movingAverageCost == null` & `totalValuationAmount == null` cho `WarehouseStaff` không có quyền `inventory.cost.read`.
    2. `HandoverManifestsController.GetHandoverManifests` trả về `totalPackages >= 1` (`2` và `3`) và `statusLabel` Tiếng Việt (`"Đang mở nhận kiện"` và `"Đã niêm phong & Bàn giao"`).

---

## 2. Bằng chứng kiểm chứng (Verification Evidence) — `[FACT]`

1. **Encoding Gate (`scripts/check-encoding.ps1`):**
   - Lệnh: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-encoding.ps1`
   - Kết quả: `Encoding check passed: all scanned files are valid UTF-8 and no mojibake markers were found.` (`Exit Code 0`).
2. **Release Build (`ERP.slnx`):**
   - Lệnh: `dotnet build ERP.slnx -c Release`
   - Kết quả: `Build succeeded. 0 Warning(s), 0 Error(s)`.
3. **Backend Closure Tests (`Wave48To51FullClosureTests`):**
   - Lệnh: `dotnet test ERP.Api.Tests/ERP.Api.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~Wave48To51FullClosureTests"`
   - Kết quả: `Passed! - Failed: 0, Passed: 17, Skipped: 0, Total: 17, Duration: 31 s - ERP.Api.Tests.dll (net10.0)`.

---

## 3. Những gì `NOT VERIFIED` — `[NOT VERIFIED]`
- Chưa chạy `ERP.QaSeed --apply` trực tiếp lên SQL Server và kiểm tra trình duyệt thật (thuộc phạm vi Gate 4 của Master Orchestrator sau khi các Subagents bàn giao).

## 4. Rủi ro tồn đọng — `[INFERENCE]`
- Không có rủi ro xung đột file (`Zero File Overlap` được tuân thủ tuyệt đối).

## 5. Việc dở dang / Stub — `[FACT]`
- Không có (`0` `TODO`, `0` `FIXME`, `0` `NotImplementedException`).
