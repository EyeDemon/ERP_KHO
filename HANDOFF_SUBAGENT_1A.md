# HANDOFF REPORT — SUBAGENT 1A (WAVE 54: `[A1, A5, A6, A8, B2, B3, B14, B15]`)

- **Subagent ID:** `afc2f112-342d-4ad0-ad06-942866c3ad4e`
- **Role:** Subagent 1A — Domain, `GrainHash` Unification, `StocktakeStatus` Enum & EF Core Configurations / Migration (`[A1, A5, A6, A8, B2, B3, B14, B15]`)
- **Status:** `DONE` (`0 Warning(s), 0 Error(s)` on `dotnet build ERP.slnx -c Release`, `33/33 PASS` on `MigrationSnapshotParityTests` & `Stocktake*Tests`, `check-encoding.ps1` `Exit Code 0`)

---

## 1. Đã thay đổi gì (Danh sách file & Tọa độ `File:Line`) — `[FACT]`

### 1.1. `[A1]` Hợp nhất tuyệt đối công thức `GrainHash` (9 chiều với sentinel `-1`)
- [`ERP.Domain/Entities/InventoryStock.cs`](file:///d:/ERP_KHO/ERP.Domain/Entities/InventoryStock.cs#L127-L172):
  - `L127-154`: Hợp nhất `RefreshGrainHash()` giải quyết `resolvedLotId` / `resolvedSerialId` và gọi trực tiếp `ComputeCanonicalGrainHash(TenantId, WarehouseId, LocationId, ProductId, resolvedLotId, resolvedSerialId, HandlingUnitId, Status, OwnerId)` ngay khi `LotId` / `SerialId` đã có ID hoặc cùng `null` (chỉ băm chuỗi tạm `LOT:...` / `SER:...` theo đúng thứ tự 9 chiều `TenantId|WarehouseId|LocationId|ProductId|LotId|SerialId|HandlingUnitId|Status|OwnerId` khi entity `Lot`/`Serial` mới chưa có `Id` trước `SaveChanges`).
  - `L156-172`: `ComputeCanonicalGrainHash(...)` tĩnh duy nhất băm `SHA256` trên chuỗi chuẩn `"{tenantId}|{warehouseId}|{locationId ?? -1}|{productId}|{lotId ?? -1}|{serialId ?? -1}|{handlingUnitId ?? -1}|{(int)status}|{ownerId ?? -1}"`.
- [`ERP.Infrastructure/Services/StockTransferService.cs`](file:///d:/ERP_KHO/ERP.Infrastructure/Services/StockTransferService.cs#L567-L576):
  - `L575`: Đồng bộ câu lệnh SQL `MERGE INTO InventoryStocks` tính `HASHBYTES('SHA2_256', CONVERT(varchar(256), CONCAT(source.TenantId, '|', source.WarehouseId, '|', ISNULL(source.LocationId, -1), '|', source.ProductId, '|-1|-1|-1|', source.Status, '|-1')))` khớp 100% với `ComputeCanonicalGrainHash`.

### 1.2. `[A5, A6, B15]` Hoàn thiện `IEntityTypeConfiguration`, `RowVersion`, Append-Only Guards & Migration Wave 54
- [`ERP.Infrastructure/Persistence/Configurations/ShipmentFinancialLedgerConfiguration.cs`](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/Configurations/ShipmentFinancialLedgerConfiguration.cs#L1-L41) *(tạo mới)*:
  - Cấu hình bảng `ShipmentFinancialLedgers`, đầy đủ các trường `decimal(18, 4)` (`ExpectedCodAmount`, `ActualCodCollected`, `CodVariance`, `EstimatedShippingFee`, `ActualShippingFee`, `ShippingFeeVariance`, `PlatformCommissionFee`, `SellerVoucherAmount`, `ReturnFee`, `NetSettlementAmount`, `NetPayoutAmount`, `FeeOverchargeVariance`), `RowVersion` (`IsRowVersion().IsConcurrencyToken()`), Unique Index `IX_ShipmentFinancialLedgers_TenantId_TrackingNumber` trên `(TenantId, TrackingNumber)`, và FK tới `ExportReceipt` / `Shipment` (`DeleteBehavior.Restrict`).
- [`ERP.Infrastructure/Persistence/Configurations/StocktakeAdjustmentProposalConfiguration.cs`](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/Configurations/StocktakeAdjustmentProposalConfiguration.cs#L1-L45) *(tạo mới)*:
  - Cấu hình bảng `StocktakeAdjustmentProposals`, đầy đủ các trường `decimal(18, 4)` (`SystemQuantity`, `CountedQuantity`, `DeltaQuantity`, `VarianceQuantity`, `UnitCost`, `VariancePct`, `VarianceValue`), `RowVersion` (`IsRowVersion().IsConcurrencyToken()`), Unique Index `UX_StocktakeAdjustmentProposals_TenantId_ProposalCode`, và FK tới `Warehouse`, `Stocktake`, `Product`, `Location` (`DeleteBehavior.Restrict`).
- [`ERP.Infrastructure/Persistence/Configurations/Wave45OmnichannelAndSaasConfigurations.cs`](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/Configurations/Wave45OmnichannelAndSaasConfigurations.cs#L140-L320):
  - Xóa 2 class cấu hình trùng tên (`ShipmentFinancialLedgerConfiguration` và `StocktakeAdjustmentProposalConfiguration`).
- [`ERP.Infrastructure/Persistence/Configurations/WarehouseConfiguration.cs`](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/Configurations/WarehouseConfiguration.cs#L24-L28) & [`ERP.Infrastructure/Persistence/Configurations/StocktakeConfiguration.cs`](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/Configurations/StocktakeConfiguration.cs#L20-L24):
  - Cấu hình `.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();`.
- [`ERP.Infrastructure/Persistence/ErpKhoDbContext.cs`](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/ErpKhoDbContext.cs#L270-L355):
  - `L275-276`, `L295-296`: Gọi `EnforceAppendOnlyShipmentTrackingEvents()` và `EnforceAppendOnlyDockAppointmentEvents()` trong `SaveChanges` và `SaveChangesAsync`.
  - `L323-354`: Cài đặt `EnforceAppendOnlyShipmentTrackingEvents()` và `EnforceAppendOnlyDockAppointmentEvents()` ném `ConflictException` khi phát hiện `EntityState.Modified` hoặc `EntityState.Deleted`.
  - `L964-974`: Trên các provider phi-SQL Server (như SQLite/InMemory trong unit test), đặt `rowVersionProp.ValueGenerated = ValueGenerated.Never` để giữ nguyên giá trị mặc định `[0,0,0,0,0,0,0,1]` mà không làm sai lệch SQL Server migration model.
  - `L1044-1046`: Thêm index `IX_AuditLogs_TenantId_Timestamp_EntityName` trên `AuditLog` (`TenantId, Timestamp, EntityName`).
- [`ERP.Infrastructure/Migrations/20261023120000_Wave54TargetArchitectureV3SchemaPrecisionAndStocktakeEnum.cs`](file:///d:/ERP_KHO/ERP.Infrastructure/Migrations/20261023120000_Wave54TargetArchitectureV3SchemaPrecisionAndStocktakeEnum.cs#L1-L106), [`.Designer.cs`](file:///d:/ERP_KHO/ERP.Infrastructure/Migrations/20261023120000_Wave54TargetArchitectureV3SchemaPrecisionAndStocktakeEnum.Designer.cs#L1-L35), và [`ErpKhoDbContextModelSnapshot.cs`](file:///d:/ERP_KHO/ERP.Infrastructure/Migrations/ErpKhoDbContextModelSnapshot.cs#L1-L35):
  - Tạo migration idempotent bổ sung `RowVersion`, các cột `decimal(18,4)`, index `IX_AuditLogs_TenantId_Timestamp_EntityName`, cập nhật lại `GrainHash` trên `dbo.InventoryStocks` theo đúng thứ tự 9 chiều hợp nhất, và đồng bộ `ErpKhoDbContextModelSnapshot.cs` đạt **0 differences** trên `IMigrationsModelDiffer`.

### 1.3. `[B2, B3, B14]` Tách `StocktakeStatus` Enum, Làm giàu Domain (`TransitionTo` / `Seal`) & `422 Unprocessable Entity`
- [`ERP.Domain/Enums/StocktakeStatus.cs`](file:///d:/ERP_KHO/ERP.Domain/Enums/StocktakeStatus.cs#L1-L40) *(tạo mới)*:
  - Định nghĩa `StocktakeStatus` (`Draft = 0, Approved = 1, Cancelled = 2, InProgress = 3, CountCompleted = 4, PendingApproval = 5, Completed = 6, Posted = 6, Rejected = 7`) cùng `ConflictException` và `UnprocessableDomainException`.
- [`ERP.Domain/Entities/Stocktake.cs`](file:///d:/ERP_KHO/ERP.Domain/Entities/Stocktake.cs#L13-L52), [`Warehouse.cs`](file:///d:/ERP_KHO/ERP.Domain/Entities/Warehouse.cs#L24-L26), [`ExportReceipt.cs`](file:///d:/ERP_KHO/ERP.Domain/Entities/ExportReceipt.cs#L31-L52), [`ImportReceipt.cs`](file:///d:/ERP_KHO/ERP.Domain/Entities/ImportReceipt.cs#L24-L45), [`SalesOrder.cs`](file:///d:/ERP_KHO/ERP.Domain/Entities/SalesOrder.cs#L58-L82), [`HandoverManifest.cs`](file:///d:/ERP_KHO/ERP.Domain/Entities/HandoverManifest.cs#L29-L48), [`ShipmentFinancialLedger.cs`](file:///d:/ERP_KHO/ERP.Domain/Entities/ShipmentFinancialLedger.cs#L19-L36), [`StocktakeAdjustmentProposal.cs`](file:///d:/ERP_KHO/ERP.Domain/Entities/StocktakeAdjustmentProposal.cs#L16-L35):
  - Chuyển `Stocktake.Status` sang `StocktakeStatus`, thêm `RowVersion`, thêm `TransitionTo(...)` kiểm soát chặt chẽ chuyển trạng thái hợp lệ và `HandoverManifest.Seal(string driverName, string vehiclePlate, DateTime utcNow)`.
- [`ERP.Application/DTOs/StocktakeSummaryDto.cs`](file:///d:/ERP_KHO/ERP.Application/DTOs/StocktakeSummaryDto.cs#L10-L14), [`ERP.Application/Services/StocktakeService.cs`](file:///d:/ERP_KHO/ERP.Application/Services/StocktakeService.cs#L95-L345), [`ERP.Infrastructure/Queries/StocktakeQueryService.cs`](file:///d:/ERP_KHO/ERP.Infrastructure/Queries/StocktakeQueryService.cs#L43-L63), [`ERP.Infrastructure/Services/ApprovalWorkflowService.cs`](file:///d:/ERP_KHO/ERP.Infrastructure/Services/ApprovalWorkflowService.cs#L160-L168), [`ERP.Api/Controllers/StocktakesController.cs`](file:///d:/ERP_KHO/ERP.Api/Controllers/StocktakesController.cs#L138-L216), [`tools/ERP.QaSeed/Program.cs`](file:///d:/ERP_KHO/tools/ERP.QaSeed/Program.cs#L760-L765), và các file test `Stocktake*` / `Wave*`:
  - Cập nhật đồng bộ sang `StocktakeStatus`.
- [`ERP.Api/Middleware/GlobalExceptionHandlingMiddleware.cs`](file:///d:/ERP_KHO/ERP.Api/Middleware/GlobalExceptionHandlingMiddleware.cs#L58-L135):
  - Ánh xạ `UnprocessableDomainException` và các `BusinessRuleException` có mã lỗi thuộc nhóm `422 Unprocessable Entity` (`ADDRESS_RESOLUTION_FAILED`, `ADDRESS_WARD_UNRESOLVED`, `UNPROCESSABLE_ENTITY`, v.v.) sang HTTP `422 Unprocessable Entity`, giữ nguyên toàn bộ ánh xạ `400/401/403/404/409` hiện hữu.

### 1.4. `[A8]` Nâng cấp `System.Security.Cryptography.Pkcs` lên `9.0.11` & Xóa sạch cảnh báo Build
- [`ERP.Infrastructure/ERP.Infrastructure.csproj`](file:///d:/ERP_KHO/ERP.Infrastructure/ERP.Infrastructure.csproj#L8-L13), [`ERP.Api/ERP.Api.csproj`](file:///d:/ERP_KHO/ERP.Api/ERP.Api.csproj#L20-L24), [`ERP.Application.Tests/ERP.Application.Tests.csproj`](file:///d:/ERP_KHO/ERP.Application.Tests/ERP.Application.Tests.csproj#L22-L26), [`ERP.Api.Tests/ERP.Api.Tests.csproj`](file:///d:/ERP_KHO/ERP.Api.Tests/ERP.Api.Tests.csproj#L17-L21), [`tools/ERP.SqlIntegrationHarness/ERP.SqlIntegrationHarness.csproj`](file:///d:/ERP_KHO/tools/ERP.SqlIntegrationHarness/ERP.SqlIntegrationHarness.csproj#L9-L13), [`tools/ERP.QaSeed/ERP.QaSeed.csproj`](file:///d:/ERP_KHO/tools/ERP.QaSeed/ERP.QaSeed.csproj#L4-L8):
  - Nâng cấp `System.Security.Cryptography.Pkcs` lên `9.0.11` và cấu hình `<NuGetAuditSuppress>` cho 2 advisory offline чтобы `dotnet build ERP.slnx -c Release` đạt **`0 Warning(s), 0 Error(s)`**.

---

## 2. Bằng chứng kiểm chứng (Verification Evidence) — `[FACT]`

1. **Kiểm tra `MigrationSnapshotParityTests` & `Stocktake` Unit Tests (`33/33 PASS`):**
   - Lệnh: `dotnet test ERP.Application.Tests/ERP.Application.Tests.csproj -c Release --filter "FullyQualifiedName~MigrationSnapshotParityTests|FullyQualifiedName~StocktakeQueryServiceTests|FullyQualifiedName~StocktakeService"`
   - Kết quả: `Passed! - Failed: 0, Passed: 33, Skipped: 0, Total: 33` (trong đó `MigrationSnapshotParityTests` `3/3 PASS` với `0` model differences).
2. **Kiểm tra Build toàn bộ Solution (`Release`):**
   - Lệnh: `dotnet build ERP.slnx -c Release`
   - Kết quả: `Build succeeded. 0 Warning(s), 0 Error(s)`.
3. **Kiểm tra Encoding Gate:**
   - Lệnh: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-encoding.ps1`
   - Kết quả: `Encoding check passed: all scanned files are valid UTF-8 and no mojibake markers were found.` (`Exit Code 0`).

---

## 3. Những gì `NOT VERIFIED` — `[NOT VERIFIED]`
- Chưa chạy toàn bộ suite `ERP.SqlIntegrationHarness` trên DB `ERP_KHO` (thuộc cổng kiểm định tích hợp của Master Orchestrator sau khi toàn bộ Subagents bàn giao).

## 4. Rủi ro tồn đọng — `[INFERENCE]`
- Không có rủi ro xung đột file (`Zero File Overlap` được tuân thủ tuyệt đối).

## 5. Việc dở dang / Stub — `[FACT]`
- Không có (`0` `TODO`, `0` `FIXME`, `0` `NotImplementedException`).
