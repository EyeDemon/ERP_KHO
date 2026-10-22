# HANDOFF REPORT — SUBAGENT 4A (`Zero-Cheat Test & SQL Harness Engineer [P2-01]`)
**Wave:** Wave 53 (Đợt 3 — Kiểm thử Tự động Thực chất, Cổng Kiến trúc Không Ngoại lệ & Seed QA Thật)  
**Status:** `COMPLETED (100% PASS)`

---

## 1. Đã thay đổi gì (Danh sách file & Tọa độ `File:Line` — `FACT`)

### A. Xóa bỏ mảng loại trừ `Wave44CanonicalTables` & đồng bộ Bootstrap Migration
1. `ERP.Application.Tests/MigrationSnapshotParityTests.cs` (`L1-L91`):
   - Đã xóa hoàn toàn mảng `Wave44CanonicalTables` (11 bảng) và điều kiện lọc `.Where(t => !Wave44CanonicalTables.Contains(t))` trong `AllMigrations_HaveMatchingDesignerFile_AndModelSnapshotMatchesRuntimeModel` (`L28-L55`).
   - Toàn bộ 3 bài kiểm thử (`MigrationHistory_ContainsInitialAndEnterpriseConstraintMigrations`, `AllMigrations_HaveMatchingDesignerFile_AndModelSnapshotMatchesRuntimeModel`, `RuntimeModel_HasNoPendingModelChanges_AgainstSnapshot`) kiểm tra 100% bảng trong `ErpKhoDbContext` đối chiếu với `ErpKhoDbContextModelSnapshot` không còn bất kỳ ngoại lệ nào.
2. `ERP.Api.Tests/Helpers/PermissionMigrationBootstrap.cs` (`L35-L42`):
   - Đã gỡ bỏ lời gọi `db.EnsureWave44SchemaSyncedIfNeeded();` để chỉ sử dụng `db.Database.Migrate();` chính thống.

### B. Tách file `ReportsControllerTests.cs` (God Test File) & bổ sung 8 bài kiểm thử thực chất Wave 53
3. `ERP.Api.Tests/ReportsControllerTests.cs` (`L1-L3005`):
   - Giữ nguyên toàn bộ các bài kiểm thử cốt lõi của `ReportsController` (Waves 40–43) và tách phần kiểm thử tích hợp của Waves 44–51 sang 2 file chuyên biệt nhằm giảm kích thước file xuống ~3.005 dòng.
4. `ERP.Api.Tests/Wave44OmnichannelAndSaasTests.cs` (`L1-L1473`):
   - Chứa các bài kiểm thử tích hợp của Waves 44–46 (`Wave44_OmnichannelMarketplaceWms_EndToEnd_CoversAll10Gaps`, `Wave45_VietnamSmeShopOwnerFinance_EndToEnd_CoversAll4Gaps`, `Wave46_MultiTenantSaaSBilling_EndToEnd_CoversAll3Gaps`).
5. `ERP.Api.Tests/Wave48To51FullClosureTests.cs` (`L1-L947`):
   - Chứa `Wave48To50_FullFiveLayerClosure_VerifiesRealTables_Locking_Rbac_BinaryExports_And_MarketplaceExcel` (`L31-L557`) cùng **8 bài kiểm thử thực chất Wave 53** (`L559-L946`):
     1. `Wave53_OD04_MarketplaceOrderPush_DecrementsAvailableStock_CreatesInventoryReservation_And_ConcurrentCallsDoNotOversell` (`L560-L629`): Kiểm chứng `POST /api/v1/omnichannel/orders/push` giảm `AvailableQuantity`, tăng `ReservedQuantity`, tạo bản ghi `InventoryReservations` thật và chạy `Task.WhenAll` 2 luồng đồng thời không bán lố tồn kho.
     2. `Wave53_OD05_HandoverManifestConfirm_TransitionsExportReceiptToDispatched_And_DeductsInventoryStock` (`L632-L674`): Kiểm chứng `POST /api/v1/handover-manifests/{id}/confirm` chuyển phiếu xuất sang `Dispatched`, trừ tồn kho thật và sinh bút toán `InventoryTransactions`.
     3. `Wave53_OD08_CreateRmaWithGoodCondition_RestoresInventoryStock_And_CreatesInboundLedgerTransaction` (`L677-L716`): Kiểm chứng `POST /api/v1/rma-returns` với `condition = "GOOD"` cộng lại `InventoryStocks.Quantity`, sinh bút toán `InventoryTransactions` (`QuantityChange > 0`) và ghi `PutawayLocationId`.
     4. `Wave53_OD11_CreateCodRemittance_OnMatchedDispatchedReceipt_IncreasesCustomerPaidAmount` (`L719-L775`): Kiểm chứng `POST /api/v1/cod-remittances` khớp mã vận đơn cập nhật `ExportReceipts.PaidAmount`, giảm dư nợ phải thu và ghi bút toán `DebtTransactions`.
     5. `Wave53_OD10_And_OD12_RoleWithoutInventoryCostRead_Receives403OnOwnerGrossProfit_And_MaskedCostsOnValuationAndVelocity` (`L778-L828`): Kiểm chứng nhân viên kho không có quyền `inventory.cost.read` nhận `403 Forbidden` tại `GET /api/v1/reports/owner-gross-profit` và nhận `UnitCost = null, TotalValue = null` tại `GET /api/v1/reports/inventory-valuation` & `GET /api/v1/reports/sku-velocity-abc`.
     6. `Wave53_ZeroFakeRevenue_FreshTenantWithNoSales_ReturnsAllZerosOnOwnerGrossProfitAndCodReconciliation` (`L831-L868`): Kiểm chứng tenant mới chưa có giao dịch trả về đúng `0` trên cả `owner-gross-profit` và `cod-shipping-reconciliation`.
     7. `Wave53_OD13_TenantProvisioning_RespectsRequestedPlanCode_And_DoesNotInsertFakeSubscriptionsOnGet` (`L871-L907`): Kiểm chứng `POST /api/v1/permissions/saas/tenants/provision` tạo đúng gói `PRO` (`MaxWarehouses = 5, MaxUsers = 30, MaxSkus = 10000, MonthlyPriceVnd = 3500000`) và `GET /api/v1/permissions/saas/subscription-billing` trên tenant chưa có gói trả về mảng rỗng `[]`.
     8. `Wave53_OD15_OpeningBalanceImport_UsesDeterministicGrainLock_And_CreatesValidGrainHashAndLedger` (`L910-L945`): Kiểm chứng `POST /api/v1/inventory/opening-balances/import` tạo `InventoryStocks` có `GrainHash` hợp lệ (32 bytes) và `InventoryTransactions` khớp số lượng.

### C. Làm sạch & chuẩn hóa `tools/ERP.QaSeed/Program.cs`
6. `tools/ERP.QaSeed/Program.cs` (`L65-L600`):
   - Đã xóa bỏ hoàn toàn hàm DDL thô `EnsureWave44CanonicalSchemaAsync`.
   - Khử trùng lặp an toàn tài khoản `qa_admin` ở các tenant phụ trước khi chạy `db.Database.MigrateAsync()` (`L68-L80`) để đảm bảo `UX_Users_TenantId_Username` sạch tuyệt đối trên mọi SQL Server instance.
   - Bổ sung phiếu xuất `ER-QA-002` ở trạng thái `Dispatched` (`TrackingNumber = "SPXVN059999999"`, `CarrierCode = "SPX Express"`) (`L339-L361`).
   - Bổ sung đầy đủ dữ liệu thật cho `TenantId = 1` trên toàn bộ các bảng Wave 44–51 (`MarketplaceShops` với `SHOPEE-OFFICIAL-01` & `TIKTOK-MALL-01`, `ChannelSkuMappings`, `HandoverManifests`, `RmaReturnOrders`, `PackingVideoEvidences`, `ProductBundleComponents`, `PromotionGiftRules`, `SupplierPayableInvoices`, `CodRemittanceBatches`, `MarketplaceFeeLedgers`, `SaasTenantSubscriptions`, `SupportImpersonationSessions`) (`L454-L578`).
   - Gọi `stockItem.RefreshGrainHash()` cho toàn bộ dòng `InventoryStocks` trước khi commit transaction (`L580-L586`).

### D. Cập nhật 7 file Vitest Frontend khớp 100% API thật & `FormData` nhị phân
7. `frontend/src/pages/Dashboard.test.tsx` (`L47-L115`):
   - Mock chính xác `/api/v1/reports/owner-gross-profit`, `/api/v1/reports/cod-shipping-reconciliation`, `/api/v1/permissions/saas/tenants/provision` với đúng tên trường DTO Backend và kiểm chứng không còn con số giả lập cứng (`148.500.000`, `36.800.000`).
8. `frontend/src/pages/ExportReceiptsProductionUi.test.tsx` (`L285-L343`):
   - Kiểm chứng luồng `POST /api/v1/export-receipts/batch-print-awb` trả về `labels` (`trackingNumber`, `carrierCode`, `pickingSummaryFooter`, `isReprintWatermark`) và hiển thị tem nhiệt AWB.
9. `frontend/src/pages/Products.test.tsx` (`L147-L182`):
   - Kiểm chứng gọi đúng `POST` và `GET` tới `/api/v1/products/1/bundle-components` và `/api/v1/sales-orders/promotion-gift-rules`.
10. `frontend/src/pages/Permissions.test.tsx` (`L113-L220`):
    - Kiểm chứng hạn mức động (`2 / 3 kho`, `11 / 20 người dùng`, `420 / 5.000 mặt hàng`) từ `GET /api/v1/permissions/saas/subscription-billing` và các route `/api/v1/security/support-impersonation/sessions` & `/api/v1/security/support-impersonation/501/revoke`.
11. `frontend/src/pages/PackingSessions.test.tsx` (`L108-L140`):
    - Kiểm chứng gọi đúng `POST /api/v1/packing-sessions/video-evidence` và `GET /api/v1/packing-sessions/video-evidence`.
12. `frontend/src/pages/Inventory.test.tsx` (`L1-L94`):
    - Kiểm chứng chọn file `.xlsx` thật (`new File(..., 'opening.xlsx')`) và gửi `FormData` (`formData.get('file') instanceof File`) tới `POST /api/v1/inventory/opening-balances/import`, kèm tải biểu mẫu `/api/v1/import-templates/opening-balance`.
13. `frontend/src/pages/OutboundDemand.test.tsx` (`L115-L151`):
    - Kiểm chứng chọn file `.xlsx` thật (`new File(..., 'orders.xlsx')`) và gửi `FormData` (`formData.get('file') instanceof File`) tới `POST /api/v1/sales-orders/import-marketplace-excel`.

---

## 2. Bằng chứng kiểm chứng (`FACT`)

- **Encoding Check:**
  - `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-encoding.ps1` -> **PASS (`Exit Code 0`, `Encoding check passed.`)**.
- **Release Build Solution:**
  - `dotnet build ERP.slnx -c Release` -> **PASS (`0 Warning(s)`, `0 Error(s)`)**.
- **Migration & Snapshot Parity Tests:**
  - `dotnet test ERP.Application.Tests/ERP.Application.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~MigrationSnapshotParityTests"` -> **PASS (`Passed: 3, Failed: 0, Total: 3`)**.
  - `$env:ERP_KHO_SQLSERVER_ADMIN_CONNECTION="Server=.\SQLEXPRESS;Database=master;Integrated Security=True;TrustServerCertificate=True"; $env:ERP_KHO_SQLSERVER_TEST_FILTER="FullyQualifiedName~MigrationSnapshotParityTests"; dotnet run --project tools/ERP.SqlIntegrationHarness/ERP.SqlIntegrationHarness.csproj -c Release --no-build` -> **PASS (`Passed: 3, Failed: 0, Total: 3`, `Exit Code 0`)**.
- **API Integration Tests (Reports, Wave 44–46, Wave 48–53):**
  - `dotnet test ERP.Api.Tests/ERP.Api.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~Wave44OmnichannelAndSaasTests|FullyQualifiedName~Wave48To51FullClosureTests|FullyQualifiedName~ReportsControllerTests"` -> **PASS (`Passed: 73, Failed: 0, Total: 73`)**.
- **QA Seed Execution:**
  - `dotnet run --project tools/ERP.QaSeed/ERP.QaSeed.csproj -c Release -- --apply` -> **PASS (`Exit Code 0` trên cả 3 SQL Server instances: `localhost`, `.\SQLEXPRESS`, `(localdb)\MSSQLLocalDB`)**.
- **Frontend Vitest & Production Build:**
  - `npm --prefix frontend test -- --run src/pages/Dashboard.test.tsx src/pages/ExportReceiptsProductionUi.test.tsx src/pages/Products.test.tsx src/pages/Permissions.test.tsx src/pages/PackingSessions.test.tsx src/pages/Inventory.test.tsx src/pages/OutboundDemand.test.tsx` -> **PASS (`7 passed (7)`, `41 passed (41)`, `Exit Code 0`)**.
  - `npm --prefix frontend run build` -> **PASS (`Exit Code 0`)**.

---

## 3. Những gì `NOT VERIFIED`
- Kiểm thử E2E trực tiếp trên trình duyệt thật (Playwright Live Browser Verification — Gate 4) sẽ được thực hiện ở bước nghiệm thu tích hợp toàn hệ thống của Master Orchestrator.

## 4. Rủi ro tồn đọng (`INFERENCE`)
- Không có rủi ro tồn đọng trong phạm vi file của Subagent 4A.

## 5. Việc dở dang / Stub
- Không có (`0 TODO`, `0 FIXME`, `0 NotImplementedException`, `0 Stub`).
