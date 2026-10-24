# HANDOFF REPORT — SUBAGENT 4A (`Zero-Cheat Test & SQL Harness Engineer [P2-01]`)
**Wave:** Wave 54 (Đợt 3 — Kỷ luật Kiểm thử Thực chất, 7 Bài Test Bắt buộc Wave 54 & Vitest cho 5 Màn hình Mới)  
**Status:** `COMPLETED (100% PASS)`

---

## 1. Đã thay đổi gì (Danh sách file & Tọa độ `File:Line` — `FACT`)

### A. Bổ sung 7 bài kiểm thử thực chất vào `ERP.Api.Tests/Wave48To51FullClosureTests.cs` (`L953-L1434`)
1. `Wave54_GrainHash_RefreshAndComputeCanonical_ProduceIdentical32ByteSha256Hash` (`L953-L1007`):
   - Kiểm chứng `stock.RefreshGrainHash()` và `InventoryStock.ComputeCanonicalGrainHash(...)` trên đủ 9 chiều định danh trả về mảng `byte[32]` (`SHA-256`) trùng khớp tuyệt đối (`SequenceEqual == true`), và sinh băm khác biệt khi thay đổi `LocationId` hoặc `OwnerId`.
2. `Wave54_ExportReceiptsAndManifest_NoNullDbFake_RequiresAuthActor_DispatchesViaDomainServiceWithLedgerAndOutbox` (`L1009-L1119`):
   - Kiểm chứng `HandoverManifestService` ném `InvalidOperationException` khi `db == null`, ném `UnauthorizedAccessException` khi `isAuthenticated == true` nhưng thiếu `actorUserId` hợp lệ, và khi niêm phong (`SealHandoverManifestAsync`) thực sự gọi `IExportReceiptService.DispatchAsync`, chuyển `HandoverManifest` sang `SEALED`, chuyển `ExportReceipt` sang `Dispatched`, trừ `InventoryStocks.Quantity`, ghi `InventoryTransactions` (`Quantity = -5m`), `IntegrationOutboxMessages` (`ExportReceiptDispatched`) và `AuditLogs`.
3. `Wave54_ShipmentFinancialLedgerAndStocktakeProposal_HaveExplicitPrecisionAndRowVersion` (`L1121-L1153`):
   - Kiểm chứng metadata EF Core của `ShipmentFinancialLedger` và `StocktakeAdjustmentProposal` có toàn bộ cột `decimal` đạt `GetPrecision() == 18` & `GetScale() == 4`, đồng thời `RowVersion` có `IsConcurrencyToken == true`.
4. `Wave54_StocktakeStatus_DedicatedEnumAndDomainTransitionGuards_WorkCorrectly` (`L1155-L1182`):
   - Kiểm chứng `Stocktake.Status` dùng enum chuyên biệt `StocktakeStatus`, chuyển từ `Draft -> Approved` thành công, và chặn chuyển lùi `Approved -> Draft` bằng `ConflictException` (`ErrorCode == "DOC_INVALID_STATE_TRANSITION"`), đồng thời kiểm chứng `UnprocessableDomainException` giữ đúng `ErrorCode`.
5. `Wave54_SplitControllers_HandoverManifests_DataImports_SaasBilling_WorkOnCanonicalV1Routes` (`L1184-L1285`):
   - Kiểm chứng 3 Controller tách mới (`HandoverManifestsController`, `DataImportsController`, `SaasBillingController`) hoạt động trên `ErpKhoDbContext` thật (`ScanHandoverAwb`, `GetHandoverManifests`, `GetDataImportTemplates`, `ImportOpeningBalance`, `GetSubscription`, `GenerateVietQrInvoice`).
6. `Wave54_PickingPathSort_And_AlternateBinSuggestion_ReturnSortedPickableBins` (`L1287-L1364`):
   - Kiểm chứng `PickingTasksController.GetAlternateBins` loại bỏ vị trí gốc đang thiếu hàng và lô hết hạn, sắp xếp các kệ thay thế khả dụng theo đúng thứ tự ưu tiên FEFO (`ExpiryDate` tăng dần) rồi đến mã vị trí (`Location.Code`).
7. `Wave54_ImportReceiptMacPreview_CalculatesExactWeightedAverageDelta` (`L1366-L1433`):
   - Kiểm chứng `ImportReceiptsController.GetMacPreview` tính chính xác giá vốn bình quân gia quyền di động (`oldQuantity = 10`, `oldMovingAverageCost = 100.000`, `incomingQuantity = 10`, `receiptUnitCost = 150.000`, `projectedMovingAverageCost = 125.000`, `macDelta = 25.000`, `macDeltaPct = 25%`, `projectedInventoryValuation = 2.500.000`).

### B. Tạo mới 5 file Vitest cho 5 màn hình mới & cập nhật 2 file Vitest hiện hữu (`FACT`)
8. `frontend/src/pages/SaasBilling.test.tsx` (`L1-L124`):
   - Kiểm chứng tải hạn mức thuê bao 3 nấc từ `/api/v1/permissions/saas/subscription-billing`, tạo hóa đơn VietQR (`/api/v1/permissions/saas/subscription-billing/create-vietqr-invoice`), xác nhận Webhook (`/api/v1/permissions/saas/subscription-billing/webhook-vietqr`), và hiển thị `role="alert"` kèm `errorCode` + `correlationId` khi API trả lỗi `500`.
9. `frontend/src/pages/CodReconciliation.test.tsx` (`L1-L134`):
   - Kiểm chứng hiển thị sổ cái đối soát COD 3 chiều từ `/api/v1/reports/cod-shipping-reconciliation`, tải lên tệp `File` nhị phân qua `FormData` (`multipart/form-data`), và hiển thị `role="alert"` khi API trả lỗi `422 INVALID_CARRIER_STATEMENT`.
10. `frontend/src/pages/OnboardingWizard.test.tsx` (`L1-L134`):
    - Kiểm chứng trạng thái 5 bước (`/api/v1/permissions/onboarding-wizard/status`), cấp phát thuê bao (`/api/v1/permissions/saas/tenants/provision`), kích hoạt nhanh 5 bước (`/api/v1/reports/onboarding-wizard/quick-start`), tải lên tệp `.xlsx` tồn đầu kỳ qua `FormData` (`/api/v1/data-imports/opening-balance`), và hiển thị `role="alert"` khi API trả lỗi `422 TENANT_CODE_DUPLICATE`.
11. `frontend/src/pages/HandoverManifests.test.tsx` (`L1-L120`):
    - Kiểm chứng tải danh sách biên bản bàn giao (`/api/v1/handover-manifests`), quét AWB hợp lệ (`/api/v1/handover-manifests/scan-awb`), xác nhận niêm phong bàn giao kèm biển số xe & tên tài xế (`/api/v1/handover-manifests/confirm-handover`), và hiển thị cảnh báo đỏ (`data-testid="handover-critical-alert"`) khi gặp `409 ORDER_CANCELLED_PUTBACK_REQUIRED`.
12. `frontend/src/pages/CostValuation.test.tsx` (`L1-L130`):
    - Kiểm chứng tải báo cáo lãi gộp & định giá tồn kho bình quân gia quyền (`/api/v1/reports/owner-gross-profit`), kết xuất bảng kê `.xlsx` MISA AMIS (`/api/v1/export-receipts/misa-excel-export?format=xlsx`), phát hành HĐĐT nháp NĐ 70/2025 (`/api/v1/export-receipts/einvoice-nd70-draft`), ẩn giá vốn khi gặp `403 Forbidden` (`data-testid="rbac-cost-hidden-banner"`), và hiển thị `role="alert"` khi cổng HĐĐT trả lỗi `500`.
13. `frontend/src/pages/Dashboard.test.tsx` (`L69-L162`):
    - Cập nhật mock & kỳ vọng thông báo đối soát COD động (`Đã đối soát 23/25 vận đơn (92%), phát hiện 2 cảnh báo lệch phí/chậm hoàn.`) khớp với logic thực chất của `Dashboard.tsx`.
14. `frontend/src/pages/Permissions.test.tsx` (`L1-L12`):
    - Bọc `<MemoryRouter>` cho helper `render` trong `Permissions.test.tsx` để hỗ trợ `<Link to="/saas-billing">` mới được bổ sung trên trang `Permissions.tsx`.

---

## 2. Bằng chứng kiểm chứng (`FACT`)

- **Encoding Gate:**
  - `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-encoding.ps1` -> **PASS (`Exit Code 0`, `Encoding check passed.`)**
- **Backend Wave 48–54 Closure Suite (16 tests):**
  - `dotnet test ERP.Api.Tests/ERP.Api.Tests.csproj -c Release --filter "FullyQualifiedName~Wave48To51FullClosureTests"` -> **PASS (`Passed: 16, Failed: 0, Skipped: 0, Total: 16`)**
- **Full Frontend Vitest Suite (All 70 test files):**
  - `npm --prefix frontend test -- --run` -> **PASS (`Test Files: 70 passed (70)`, `Tests: 390 passed (390)`, `Exit Code 0`)**

---

## 3. Những gì `NOT VERIFIED` & Lưu ý cho Master Orchestrator
- Khi chạy thử `npm --prefix frontend run build` (`tsc -b`), trình biên dịch TypeScript báo 2 lỗi biến `errorMessage` chưa khai báo ở 2 file sản xuất nằm ngoài phạm vi sở hữu của Subagent 4A:
  1. `frontend/src/pages/ImportReceipts.tsx(195,16): error TS2304: Cannot find name 'errorMessage'.` (state trong component là `error`)
  2. `frontend/src/pages/PickingTasks.tsx(102,16): error TS2304: Cannot find name 'errorMessage'.` (state trong component là `error`)
- Tuân thủ nghiêm ngặt quy tắc `Exclusive File Ownership`, Subagent 4A **không tự ý chạm vào 2 file `.tsx` sản xuất này** và báo cáo tọa độ chính xác để Master Orchestrator điều phối xử lý.

## 4. Rủi ro tồn đọng (`INFERENCE`)
- Không có rủi ro tồn đọng trong phạm vi các file test của Subagent 4A.

## 5. Việc dở dang / Stub
- Không có (`0 TODO`, `0 FIXME`, `0 NotImplementedException`, `0 Stub`).
