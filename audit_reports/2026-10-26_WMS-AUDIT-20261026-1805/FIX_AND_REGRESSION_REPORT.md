# FIX AND REGRESSION REPORT (`WMS-AUDIT-20261026-1805`)

- **Audit Run ID:** `WMS-AUDIT-20261026-1805`
- **Total Defects Remediated:** `6 / 6` (`2` `P1`, `4` `P3`)
- **Regression Status:** `100% PASS_VERIFIED` (Verified in Live Browser + Automated Test Suites)

---

## 1. Remediated Files & Exact Line Coordinates

| File Path | Lines Modified | Finding IDs | Summary of Change |
|---|---|---|---|
| `d:\ERP_KHO\ERP.Api\Infrastructure\IdempotentCommandFilter.cs` | `250-314` | `FND-DASH-001` | Added `IsNonPayloadArgument(object? value)` to exclude DI services (`DbContext`, `IWarehouseAuthorizationService`, `IRequestMetadata`, `IBusinessPartnerService`, `IProductCatalogService`, `IFormFile`, `Stream`, and `ERP.Infrastructure`/`ERP.Application.Services` namespaces) from `Fingerprint` JSON serialization and `TrackedWarehouses` reflection. |
| `d:\ERP_KHO\ERP.Api\Controllers\ReportsController.cs` | `211-218`, `310-315`, `333-339` | `FND-DASH-003` | Required `match.Value.Any(char.IsDigit)` in `ExtractAuditReferenceCode` so `"PUT-TO-WALL"` is not extracted as a fake Putaway Task reference code; mapped `"PutToWallSortation"` to `"Chia chọn đơn hàng theo ô tường"` under `"Xuất kho & điều phối"`. |
| `d:\ERP_KHO\frontend\src\pages\Dashboard.tsx` | `288-292` | `FND-DASH-003` | Added Vietnamese entity label mapping `PutToWallSortation: 'Chia chọn đơn hàng theo ô tường'`. |
| `d:\ERP_KHO\frontend\src\pages\Products.tsx` | `593-597` | `FND-MD-001` | Added Vietnamese translation for `STORAGE_POLICY_VERIFIED` (`'Đã xác minh chính sách bảo quản & phân vùng nhiệt độ/nguy hại'`). |
| `d:\ERP_KHO\frontend\src\pages\Permissions.tsx` | `944-948` | `FND-MD-001` | Added Vietnamese translation for `ZERO_PRIVILEGE_ESCALATION_VERIFIED` (`'Đã xác minh không leo thang đặc quyền (ZERO_PRIVILEGE_ESCALATION_VERIFIED)'`). |
| `d:\ERP_KHO\frontend\src\pages\WarehouseLocations.tsx` | `551-554` | `FND-MD-002` | Replaced English subtitle `(Receiving / Staging / QC / Scrap)` with `(Tiếp nhận / Tập kết / Kiểm định / Hủy bỏ)`. |
| `d:\ERP_KHO\frontend\src\pages\OutboundDemand.tsx` | `596-610` | `FND-OUT-001` | Added `position: 'relative', zIndex: 2, marginBottom: 4` to the action container wrapping `[data-testid="btn-evaluate-dom-split"]` so sticky table headers never overlap it on scroll. |

---

## 2. Before / After Regression Verification Matrix

| Finding ID | Regression Test ID | Pre-Fix Evidence / Symptom | Post-Fix Live Browser Verification | Final Status |
|---|---|---|---|---|
| `FND-DASH-001` | `TC-DASH-001-05` | `POST /api/reports/operational-dashboard/exceptions/IR-QA-001/acknowledge` returned `500 Internal Server Error` (`NotSupportedException` serializing `MethodBase` on `ErpKhoDbContext`). | Re-clicked `"Tiếp nhận xử lý"` on `/` (`SCR-DASH-001`): returned `200 OK`, button transitioned to disabled `"Đã tiếp nhận"`. Screenshot: `evidence/screenshots/TC-DASH-001-05_07_regression_after.png`. | `PASS_VERIFIED` |
| `FND-DASH-002` | `TC-DASH-001-07` | `POST /api/sales-orders/marketplace-shops/sync-inventory` returned `500 Internal Server Error` due to stale `2026-10-22` `ERP.Api.exe` binary. | Hot-swapped freshly built `2026-10-26` binaries into `ERP.Api\bin\Debug\net10.0\` and re-clicked `"Đồng Bộ Tồn Kho Đa Sàn Tức Thì"`: returned `200 OK` (`Đã đồng bộ tồn kho khả dụng tức thì (SYNC-MKT-20261026113142) sang 6 gian hàng`). Screenshot: `evidence/screenshots/TC-DASH-001-05_07_regression_after.png`. | `PASS_VERIFIED` |
| `FND-DASH-003` | `TC-DASH-001-02` | Dashboard recent activity feed showed `PutToWallSortation (PUT-TO-WALL) • Tiếp nhận & cất hàng`. | Reloaded `/` (`SCR-DASH-001`): activity row now renders `Chia chọn đơn hàng theo ô tường • Xuất kho & điều phối`. Screenshot: `evidence/screenshots/TC-DASH-001-05_07_regression_after.png`. | `PASS_VERIFIED` |
| `FND-MD-001` | `TC-MD-001-03` & `TC-ADM-004-03` | Raw `STORAGE_POLICY_VERIFIED` on `/products` and `ZERO_PRIVILEGE_ESCALATION_VERIFIED` on `/permissions`. | Verified on `/products` (`CAT-POL-20261026-1220 • Đã xác minh chính sách bảo quản & phân vùng nhiệt độ/nguy hại`, `evidence/screenshots/TC-MD-001-03_regression_after.png`) and `/permissions` (`Đã xác minh không leo thang đặc quyền (ZERO_PRIVILEGE_ESCALATION_VERIFIED)`, `evidence/screenshots/TC-ADM-004-03_regression_after.png`). | `PASS_VERIFIED` |
| `FND-MD-002` | `TC-MD-005-02` | 3rd tab on `/warehouse-structure` showed `Vị trí hệ thống (Receiving / Staging / QC / Scrap)`. | Verified on `/warehouse-structure`: tab renders `Vị trí hệ thống (Tiếp nhận / Tập kết / Kiểm định / Hủy bỏ)`. Screenshot: `evidence/screenshots/TC-MD-005-02_regression_after.png`. | `PASS_VERIFIED` |
| `FND-OUT-001` | `TC-OUT-001-04` | Sticky `<th>` intercepted pointer events on `btn-evaluate-dom-split` when scrolled on `/backorders`. | Verified on `/backorders`: `document.elementFromPoint` at button center returned `BUTTON` (`isBtnOrChild: true`), click returned `200 OK` (`ATP-ROUTE-20261026-1221`). Screenshot: `evidence/screenshots/TC-OUT-001-04_regression_after.png`. | `PASS_VERIFIED` |
