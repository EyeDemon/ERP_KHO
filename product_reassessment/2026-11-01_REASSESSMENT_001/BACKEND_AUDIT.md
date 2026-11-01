# BÁO CÁO KIỂM TOÁN KIẾN TRÚC & HIỆN TRẠNG BACKEND (`BACKEND_AUDIT.md`)

- **Đợt đánh giá:** `2026-11-01_REASSESSMENT_001`
- **Phạm vi kiểm toán:** Toàn bộ 4 tầng Backend (`ERP.Domain`, `ERP.Application`, `ERP.Infrastructure`, `ERP.Api`) và 3 project kiểm thử/công cụ (`ERP.Application.Tests`, `ERP.Api.Tests`, `tools/ERP.SqlIntegrationHarness`) trên mã nguồn thực tế tại `d:\ERP_KHO`.
- **Trạng thái kiểm chứng (`Evidence Classification`):**
  - **`FACT`**: Đã đọc trực tiếp mã nguồn tại tọa độ `File:Line` bằng công cụ `read_file` và đối chiếu cấu trúc project.
  - **`INFERENCE`**: Suy luận kỹ thuật có căn cứ trực tiếp từ hành vi mã nguồn/kiến trúc.
  - **`NOT VERIFIED`**: Các hành vi phụ thuộc dịch vụ bên thứ ba thật ngoài mạng internet (cổng API thật của GHN/GHTK/ViettelPost/J&T, Shopee/TikTok Shop Open Platform, cơ quan thuế GDT/MISA meInvoice).

---

## 1. Kiến trúc Phân tầng & Chiều Phụ thuộc (`Clean Architecture Compliance`)

### 1.1 Sơ đồ Phụ thuộc Project (`FACT`)
Kiểm tra trực tiếp các tệp `.csproj` và `ERP.slnx` ([ERP.slnx](file:///d:/ERP_KHO/ERP.slnx#L1-L12)):

| Tầng / Project | Target Framework | Project References | Package References Chính | Đánh giá Clean Architecture |
|---|---|---|---|---|
| [ERP.Domain.csproj](file:///d:/ERP_KHO/ERP.Domain/ERP.Domain.csproj#L1-L9) | `net10.0` | **0** (Không phụ thuộc project nào) | **0** (Thuần C# POCO, Enums, Exceptions, Domain Interfaces) | **ĐẠT 100% (`ARCH-001`)** |
| [ERP.Application.csproj](file:///d:/ERP_KHO/ERP.Application/ERP.Application.csproj#L1-L17) | `net10.0` | `ERP.Domain` | `FluentValidation` (`11.11.0`) | **ĐẠT 100% (`ARCH-001`)** |
| [ERP.Infrastructure.csproj](file:///d:/ERP_KHO/ERP.Infrastructure/ERP.Infrastructure.csproj#L1-L25) | `net10.0` | `ERP.Application` (bắc cầu `ERP.Domain`) | `Dapper` (`2.1.79`), `Microsoft.EntityFrameworkCore.SqlServer` (`10.0.9`), `System.Security.Cryptography.Pkcs` (`9.0.11`), `FrameworkReference: Microsoft.AspNetCore.App` | **ĐẠT (`ARCH-001`)** |
| [ERP.Api.csproj](file:///d:/ERP_KHO/ERP.Api/ERP.Api.csproj#L1-L31) | `net10.0` | `ERP.Application`, `ERP.Infrastructure` | `ClosedXML` (`0.105.0`), `Microsoft.AspNetCore.Authentication.JwtBearer` (`10.0.9`), `Serilog.AspNetCore` (`10.0.0`), `Swashbuckle.AspNetCore` (`10.2.3`) | **ĐẠT (`ARCH-001`)** |

### 1.2 Mô hình Dịch vụ Hai Tầng (`Dual-Service Architecture Pattern`) (`FACT`)
Hệ thống **không sử dụng MediatR/CQRS Request Handlers**, mà tổ chức theo mô hình **Application Services + Infrastructure Operations Services**:
1. **Nhóm Core Domain/Application Services (`ERP.Application/Services/` — 9 class):**
   - [AuthService.cs](file:///d:/ERP_KHO/ERP.Application/Services/AuthService.cs), [ProductService.cs](file:///d:/ERP_KHO/ERP.Application/Services/ProductService.cs), [ProductCatalogService.cs](file:///d:/ERP_KHO/ERP.Application/Services/ProductCatalogService.cs), [WarehouseService.cs](file:///d:/ERP_KHO/ERP.Application/Services/WarehouseService.cs), [UnitService.cs](file:///d:/ERP_KHO/ERP.Application/Services/UnitService.cs), [ImportReceiptService.cs](file:///d:/ERP_KHO/ERP.Application/Services/ImportReceiptService.cs), [ExportReceiptService.cs](file:///d:/ERP_KHO/ERP.Application/Services/ExportReceiptService.cs), [StocktakeService.cs](file:///d:/ERP_KHO/ERP.Application/Services/StocktakeService.cs), [ReportService.cs](file:///d:/ERP_KHO/ERP.Application/Services/ReportService.cs).
   - Các class này giao tiếp qua Repository (`IImportReceiptRepository`, `IExportReceiptRepository`, `IInventoryStockRepository`, `IInventoryTransactionRepository`, `IUnitOfWork`).
2. **Nhóm Transactional Domain Services trong `ERP.Infrastructure/Services/` (45+ class):**
   - Xử lý trực tiếp trên `ErpKhoDbContext` kèm khóa dòng 9 chiều (`AcquireInventoryAllocationLocksAsync`): [PutawayService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/PutawayService.cs), [PickingService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/PickingService.cs), [PackingService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/PackingService.cs), [ShipmentService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/ShipmentService.cs), [StockReservationService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/StockReservationService.cs), [StockAllocationService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/StockAllocationService.cs), [StockTransferService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/StockTransferService.cs) (7 partial files), [InventoryStatusService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/InventoryStatusService.cs), [InventoryLockService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/InventoryLockService.cs), [InventoryMovementService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/InventoryMovementService.cs), [InventoryReversalService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/InventoryReversalService.cs), [ReceivingDiscrepancyService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/ReceivingDiscrepancyService.cs), [InboundPlanningService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/InboundPlanningService.cs), [SalesOrderBackorderService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/SalesOrderBackorderService.cs), [DockYardService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/DockYardService.cs), [PartnerDebtService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/PartnerDebtService.cs), [HandoverManifestService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/HandoverManifestService.cs), [SaasBillingService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/SaasBillingService.cs), [DataImportService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/DataImportService.cs).
3. **Nhóm `*OperationsService` & `WmsGovernanceOperationsService` (`FACT`):**
   - Để loại bỏ `ErpKhoDbContext` khỏi các API Controller (`ARCH-003`), dự án đã tách các thao tác truy vấn/đối soát/vận hành mở rộng sang 11 dịch vụ `*OperationsService`, trong đó lớn nhất là:
     - [WmsGovernanceOperationsService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/WmsGovernanceOperationsService.cs#L1-L3675) (**3.675 dòng** — xử lý các endpoint quản trị, chẩn đoán, đánh giá SLA, 3PL billing, cross-dock, yard, ASN, PO, điều chuyển, thu hồi lô).
     - [ReportOperationsService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/ReportOperationsService.cs#L1-L2778) (**2.778 dòng** — xử lý dashboard, control tower, BI snapshot, GL journal, đối soát COD, 3PL storage snapshot, gross profit, onboarding wizard, trung tâm cảnh báo).
     - [ExportReceiptOperationsService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/ExportReceiptOperationsService.cs#L1-L1492) (**1.492 dòng** — xử lý SLA queue, hạn ngạch SaaS khi tạo phiếu xuất, in tem nhiệt AWB `100x150mm` `%PDF-1.4`, tồn đầu kỳ, xuất Excel MISA, phát hành HĐĐT NĐ 70/2025 & Mẫu `04/SS-HĐĐT`).

---

## 2. Kiểm toán Tầng Cơ sở Dữ liệu, Đa Tenant, Khóa Đồng thời & Sổ cái Bất biến (`Layer 1 & Layer 2`)

### 2.1 Quy mô Schema & Migrations (`FACT`)
- **DbSets:** **96 `DbSet<T>`** khai báo trong [ErpKhoDbContext.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/ErpKhoDbContext.cs#L141-L236).
- **Entity Files:** **60 file `.cs`** trong `ERP.Domain/Entities/` định nghĩa **87 lớp thực thể nghiệp vụ**.
- **EF Core Configurations:** **49 file `IEntityTypeConfiguration<T>`** trong `ERP.Infrastructure/Persistence/Configurations/`, được nạp tự động qua `modelBuilder.ApplyConfigurationsFromAssembly(typeof(ErpKhoDbContext).Assembly)` ([ErpKhoDbContext.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/ErpKhoDbContext.cs#L755)).
- **EF Core Migrations:** **61 bản migration chính thống** trong `ERP.Infrastructure/Migrations/` (từ `20260709111901_InitialCreate` đến `20261028100852_Wave60RoadmapEpics601To603`), được bảo vệ bởi bài test [MigrationSnapshotParityTests.cs](file:///d:/ERP_KHO/ERP.Application.Tests/Persistence/MigrationSnapshotParityTests.cs).
- **Dapper vs EF Core:** Dapper (`2.1.79`) chỉ được sử dụng tại 1 nơi duy nhất là [ReportRepository.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Repositories/ReportRepository.cs#L54-L66) để gọi Stored Procedure báo cáo Nhập-Xuất-Tồn `sp_GetInventoryInOutReport`. Mọi truy vấn và giao dịch còn lại đều đi qua EF Core (`ErpKhoDbContext`).

### 2.2 Bất biến Tồn kho 9 Chiều (`9D Canonical Grain`) & `GrainHash` (`FACT`)
- Thực thể [InventoryStock.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/InventoryStock.cs#L6-L172) và cấu hình [InventoryStockConfiguration.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/Configurations/InventoryStockConfiguration.cs#L9-L58) thực thi đầy đủ **9 chiều định danh tồn kho**:
  1. `TenantId` (`int`, mặc định `1`)
  2. `WarehouseId` (`int`)
  3. `LocationId` (`int?`, cột tính toán lưu trữ `CanonicalLocationId = COALESCE([LocationId], 0)`)
  4. `ProductId` (`int`)
  5. `LotId` (`int?`, `CanonicalLotId = COALESCE([LotId], 0)`)
  6. `SerialId` (`int?`, `CanonicalSerialId = COALESCE([SerialId], 0)`)
  7. `HandlingUnitId` (`int?`, `CanonicalHandlingUnitId = COALESCE([HandlingUnitId], 0)`)
  8. `Status` (`InventoryStatus` enum: `Available=0, QcHold=1, Quarantine=2, Damaged=3, Rejected=4, Blocked=5, Expired=6, RecallBlocked=7`)
  9. `OwnerId` (`int?`, `CanonicalOwnerId = COALESCE([OwnerId], 0)`)
- **Unique Index chống trùng NULL (`DB-003`):** Chỉ mục duy nhất `IX_InventoryStocks_CanonicalBucket` trên 9 cột (`TenantId, ProductId, WarehouseId, Status, CanonicalLocationId, CanonicalLotId, CanonicalSerialId, CanonicalHandlingUnitId, CanonicalOwnerId`) với `.IsUnique().HasFilter(null)` ([InventoryStockConfiguration.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/Configurations/InventoryStockConfiguration.cs#L31-L43)).
- **Check Constraint chống tồn âm (`DB-002`, `INV-006`):** `CK_InventoryStocks_Reservation`: `[ReservedQuantity] >= 0 AND [AllocatedQuantity] >= 0 AND [LockedQuantity] >= 0 AND [Quantity] >= [ReservedQuantity] AND [Quantity] >= [AllocatedQuantity] + [LockedQuantity]` ([InventoryStockConfiguration.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/Configurations/InventoryStockConfiguration.cs#L20)).
- **Mã băm `GrainHash` (`SHA256` 32 bytes):** Hàm `InventoryStock.ComputeCanonicalGrainHash` ([InventoryStock.cs](file:///d:/ERP_KHO/ERP.Domain/Entities/InventoryStock.cs#L157-L172)) tự động băm chuỗi định chuẩn `"{tenantId}|{warehouseId}|{locationId ?? -1}|{productId}|{lotId ?? -1}|{serialId ?? -1}|{handlingUnitId ?? -1}|{(int)status}|{ownerId ?? -1}"` mỗi khi bất kỳ thuộc tính chiều nào thay đổi và được cưỡng bức tính lại trong `ErpKhoDbContext.RefreshTrackedInventoryStockGrainHashes()` trước khi `SaveChanges` ([ErpKhoDbContext.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/ErpKhoDbContext.cs#L261-L275)).

### 2.3 Cơ chế Khóa Dòng 2 Tầng (`2-Tier Deterministic 9D Locking`) (`FACT`)
Tại [ErpKhoDbContext.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/ErpKhoDbContext.cs#L40-L115), phương thức `AcquireInventoryAllocationLocksAsync(IEnumerable<(int WarehouseId, int ProductId)> scopes, CancellationToken)` thực thi khóa 2 tầng chống Race Condition và Deadlock:
1. **Sắp xếp khóa định chuẩn (`CON-003`):** `.Distinct().OrderBy(x => x.WarehouseId).ThenBy(x => x.ProductId)`.
2. **Tầng 1 (`sp_getapplock`):** Khóa tài nguyên mức ứng dụng `ERP:InventoryAllocation:{tenantId}:{warehouseId}:{productId}` với `@LockMode = N'Exclusive'`, `@LockOwner = N'Transaction'`, `@LockTimeout = 15000` (15 giây) — chặn cả trường hợp dòng `InventoryStocks` chưa tồn tại (chống Phantom Insert).
3. **Tầng 2 (`UPDLOCK, ROWLOCK, REPEATABLEREAD`):** Truy vấn `SELECT Id FROM dbo.InventoryStocks WITH (INDEX(IX_InventoryStocks_CanonicalBucket), UPDLOCK, ROWLOCK, REPEATABLEREAD)` sắp xếp theo đủ 9 chiều tăng dần.
4. **Bắt lỗi Deadlock `1205`:** Tự động bắt `SqlException { Number: 1205 }` và ném `DeadlockException` ([ErpKhoDbContext.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/ErpKhoDbContext.cs#L110-L113)).

### 2.4 Cô lập Đa Tenant (`Multi-Tenant Isolation`) (`FACT`)
Thực thi tại [ErpKhoDbContext.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/ErpKhoDbContext.cs#L277-L633) và [ErpKhoDbContext.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/ErpKhoDbContext.cs#L802-L825):
- **Tầng Đọc (Global Query Filter):** Mọi thực thể cài đặt `ITenantEntity` đều được gắn `HasQueryFilter(e => this.IsSystemTenantBypass || e.TenantId == this.CurrentTenantId)` ([ErpKhoDbContext.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/ErpKhoDbContext.cs#L808)).
- **Tầng Ghi (`EnforceMultiTenantIsolation` / `EnforceMultiTenantIsolationAsync`):**
  - Tự động gán `TenantId = activeTenantId` khi thêm mới (`EntityState.Added`).
  - Chặn sửa/xóa bản ghi của tenant khác (`EntityState.Modified` / `Deleted`) bằng `ForbiddenException`.
  - **Kiểm tra khóa ngoại liên Tenant (`Cross-Tenant FK Verification`):** Thu thập toàn bộ FK của các thực thể đang `Added`/`Modified` (`CollectEntryForeignKeyChecks`, dòng `382–495`) và truy vấn gom nhóm (`BatchLookupPrincipalTenantIdsAsync`, dòng `497–618`) để chặn tuyệt đối hành vi gắn ID kho, sản phẩm, đối tác hoặc chứng từ của Tenant A vào bản ghi của Tenant B.

### 2.5 Sáu Sổ cái Bất biến (`6 Immutable Append-Only Ledgers`) (`FACT`)
Trước mọi lệnh `SaveChanges` / `SaveChangesAsync` ([ErpKhoDbContext.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Persistence/ErpKhoDbContext.cs#L242-L248) và `L635-L739`), `ErpKhoDbContext` cưỡng bức chỉ cho phép `INSERT` (chặn `Modified` và `Deleted` bằng `InvalidOperationException` / `BusinessRuleException`) đối với 6 bảng sổ cái:
1. `AuditLogs` (`EnforceAppendOnlyAuditLogs`, dòng `635–644`)
2. `InventoryTransactions` (`EnforceAppendOnlyInventoryLedger`, dòng `646–674` — chỉ cho phép cập nhật duy nhất trường `CorrectiveTransactionId` một lần từ `null` sang ID bút toán đảo trên dòng `TransactionType.Reversal`)
3. `InventoryLocationMovements` (`EnforceAppendOnlyLocationMovements`, dòng `676–701` — chỉ cho phép gán `ReferenceId = entry.Entity.Id` ngay sau khi sinh khóa chính)
4. `PartnerDebtLedgers` (`EnforceAppendOnlyPartnerDebtLedger`, dòng `703–713`)
5. `ShipmentTrackingEvents` (`EnforceAppendOnlyShipmentTrackingEvents`, dòng `715–726`)
6. `DockAppointmentEvents` (`EnforceAppendOnlyDockAppointmentEvents`, dòng `728–739`)

---

## 3. Kiểm toán Tầng API Controllers, RBAC, Idempotency & Middleware (`Layer 3`)

### 3.1 Quy ước Route Song song (`Dual-Route Convention`) (`FACT`)
- Toàn bộ **42 API Controllers** trong `ERP.Api/Controllers/` đều hỗ trợ **đồng thời** cả 2 chuẩn đường dẫn:
  - Chuẩn mới `/api/v1/<kebab-case-plural>` (ví dụ: `[Route("api/v1/import-receipts")]`, `[Route("api/v1/export-receipts")]`, `[Route("api/v1/inventory-control")]`).
  - Chuẩn legacy `/api/[controller]` hoặc `/api/<kebab-case>` (ví dụ: `[Route("api/[controller]")]` trên `ImportReceiptsController`, `ExportReceiptsController`, `ProductsController`, `WarehousesController`, `StocktakesController`, `ReportsController`).
- Điều này giúp toàn bộ các lời gọi API hỗn hợp từ Frontend (`frontend/src/services/*.ts`) đều định tuyến thành công 100% mà không gặp lỗi `404`.

### 3.2 Cơ chế Idempotency (`IdempotentCommandFilter`) (`FACT`)
- Được cài đặt tại [IdempotentCommandFilter.cs](file:///d:/ERP_KHO/ERP.Api/Infrastructure/IdempotentCommandFilter.cs#L17-L382) và gắn qua `[IdempotentCommand("<Scope>")]` trên các endpoint POST/PUT/DELETE thay đổi dữ liệu.
- **Cách hoạt động:**
  1. Bắt buộc header `Idempotency-Key` (tối đa 128 ký tự; trả `400 Bad Request` nếu thiếu hoặc quá dài — dòng `57–68`).
  2. Tính `KeyHash = SHA256(rawKey)` và `RequestFingerprint = SHA256(commandScope + canonicalJsonArguments + queryString)` (dòng `70–74`, `304–315`).
  3. Mở SQL Transaction với mức cô lập `IsolationLevel.Serializable` cho hầu hết các scope kho vận (`RequiresSerializableIsolation`, dòng `79–81`, `325–355`) và chèn bản ghi `IdempotencyRecord` ở trạng thái `Processing` (hết hạn sau 30 ngày).
  4. Nếu trùng khóa (`SqlException 2601/2627` hoặc Deadlock `1205`):
     - Nếu `RequestFingerprint` khác hoặc `Status != Completed` -> trả `409 Conflict` kèm mã `IDEMPOTENCY_CONFLICT` (dòng `102–115`).
     - Nếu khớp `RequestFingerprint` và `Status == Completed` -> kiểm tra lại quyền truy cập kho (`ReauthorizeWarehouses`, dòng `116`, `243–247`) và phát lại (`Replay`) nguyên vẹn `ResponseStatusCode` + `ResponseBody` đã lưu (dòng `135`).

### 3.3 Cơ chế Phân quyền RBAC & Bảo vệ Giá vốn (`FACT`)
- **Filter kiểm quyền DB trực tiếp:** [PermissionAuthorizeAttribute.cs](file:///d:/ERP_KHO/ERP.Api/Authorization/PermissionAuthorizeAttribute.cs#L41-L85) truy vấn `db.Users.AsNoTracking()` kiểm tra `u.IsActive`, `u.LockoutEnd`, và `u.Role.Permissions.Any(rp => _permissions.Contains(rp.Permission.Code))` trên mỗi request.
- **Bảo vệ giá vốn (`inventory.cost.read`):**
  - Được kiểm tra tại:
    - [InventoryStocksController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/InventoryStocksController.cs#L24-L32): `MaskCostIfForbidden` gán `MovingAverageCost = null` nếu `!currentUser.HasPermission("inventory.cost.read")`.
    - [InventoryTransactionsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/InventoryTransactionsController.cs#L39-L47): `MaskCostIfForbidden` gán `UnitCost = null`.
    - [ExportReceiptsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ExportReceiptsController.cs#L298) & [ExportReceiptOperationsService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/ExportReceiptOperationsService.cs#L1027-L1030): Ẩn các cột `UnitCost`, `DonGiaVonMac`, `CostAmount`, `ThanhTienGiaVon` khi xuất Excel MISA nếu người dùng không có quyền `inventory.cost.read`.
    - [ReportsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ReportsController.cs#L140-L154): Chặn `403 Forbidden` trên báo cáo lợi nhuận gộp (`owner-gross-profit`) nếu thiếu quyền `inventory.cost.read` hoặc thuộc vai trò `WarehouseStaff`/`Viewer`.

---

## 4. Danh sách Phát hiện Kiểm toán Trọng yếu, Lỗ hổng & Nợ Kỹ thuật (`Forensic Findings & Gaps`)

Dưới đây là **10 phát hiện kỹ thuật quan trọng nhất** có bằng chứng `File:Line` cụ thể:

### [FINDING-BE-01] [CRITICAL] TOTP 2FA lưu trạng thái trong biến `static ConcurrentDictionary` trên RAM và dùng khóa bí mật tất định khi fallback (`FACT`)
- **Tọa độ:** [AccountSecurityController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/AccountSecurityController.cs#L122) và [AccountSecurityController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/AccountSecurityController.cs#L589-L595).
- **Bằng chứng mã nguồn:**
  - Dòng `122`: `private static readonly ConcurrentDictionary<int, (byte[] SecretBytes, string Base32Secret, string[] RecoveryCodes, string Status)> TotpEnrollments = new();`
  - Dòng `589–595`:
    ```csharp
    private static byte[] GetTotpSecretBytes(int userId, string username)
    {
        if (TotpEnrollments.TryGetValue(userId, out var state) && state.SecretBytes is { Length: > 0 })
            return state.SecretBytes;
        return SHA256.HashData(Encoding.UTF8.GetBytes($"ERP_WMS_RFC6238_TOTP_SECRET:{userId}:{username.Trim().ToLowerInvariant()}"));
    }
    ```
- **Tác động:**
  1. Toàn bộ trạng thái đăng ký 2FA TOTP (`TotpEnrollments`) nằm trong bộ nhớ RAM của tiến trình web — bị mất trắng khi khởi động lại server (`iisreset` / container restart) và không hoạt động khi chạy nhiều instance.
  2. Nguy hiểm hơn: Khi `TotpEnrollments` chưa có mục của `userId` (ví dụ sau khi server khởi động lại), `GetTotpSecretBytes` tự động sinh khóa TOTP bằng hàm băm tĩnh `SHA256("ERP_WMS_RFC6238_TOTP_SECRET:{userId}:{username}")` — bất kỳ ai biết `userId` và `username` đều có thể tính trước mã OTP 6 số!

### [FINDING-BE-02] [HIGH] Tích hợp Hãng vận chuyển (GHN, GHTK, ViettelPost, J&T), Sàn TMĐT (Shopee, TikTok Shop) và Outbox Dispatcher chỉ là bộ mô phỏng nội bộ (`Local Deterministic Simulation`), chưa gọi HTTP ra mạng thật (`FACT`)
- **Tọa độ:**
  - [VietnamCarrierAdapters.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/Carriers/VietnamCarrierAdapters.cs#L85-L320) (`GhnCarrierAdapter`, `GhtkCarrierAdapter`, `ViettelPostCarrierAdapter`, `JtExpressCarrierAdapter`)
  - [MarketplaceChannelAdapters.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/Marketplaces/MarketplaceChannelAdapters.cs#L60-L264) (`ShopeeChannelAdapter`, `TikTokShopChannelAdapter`)
  - [IntegrationOutboxDispatcherJob.cs](file:///d:/ERP_KHO/ERP.Api/Hosting/IntegrationOutboxDispatcherJob.cs#L84-L122)
- **Bằng chứng mã nguồn:**
  - Trong `VietnamCarrierAdapters.cs`, phương thức `BookWaybill(...)` là hàm đồng bộ (`synchronous`), tự sinh mã vận đơn theo mẫu (`$"GHN-{warehouseId:D2}-{DateTime.UtcNow:yyMMdd}-{refId:D5}"`), tự tính phí theo công thức bậc thang nội bộ và trả về chuỗi JSON mô phỏng mà không hề sử dụng `HttpClient`.
  - Trong `MarketplaceChannelAdapters.cs`, `ExchangeAuthCode` và `RefreshAccessToken` chỉ băm chuỗi SHA256 nội bộ (`$"ENC_SHOPEE_AT_{normShop}_{accessDigest}"`), không gọi OAuth API của Shopee hay TikTok Shop.
  - Trong `IntegrationOutboxDispatcherJob.cs` (dòng `102–121`), worker lấy các bản ghi `IntegrationOutboxMessages` có `Status == "PENDING"`, gọi `_ = carrierRegistry.ResolveAdapter("GHN")` hoặc `_ = channelRegistry.ResolveAdapter("SHOPEE")` rồi bỏ qua biến kết quả và gán thẳng `msg.Status = "DISPATCHED"`! Ngoài ra, dòng `84` dùng secret fallback cứng `"ERP_KHO_OUTBOX_SIGNING_SECRET_2026"`.

### [FINDING-BE-03] [HIGH] Phát hành Hóa đơn Điện tử NĐ 70/2025 & Ký số XML là mô phỏng nội bộ (Self-Signed RSA Ephemeral Certificate) (`FACT`)
- **Tọa độ:**
  - [ExportReceiptsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ExportReceiptsController.cs#L313-L390) (`IssueEInvoice`)
  - [WmsGovernanceOperationsService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/WmsGovernanceOperationsService.cs#L3630-L3674) (`ComputeRsaXmlSignatureDigest`)
  - [ExportReceiptOperationsService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/ExportReceiptOperationsService.cs#L1043-L1491) (`IssueEinvoiceNd70DraftAsync`, `SyncAccountingAndEInvoiceOutboxAsync`)
- **Bằng chứng mã nguồn:**
  - Khi gọi `POST /api/v1/export-receipts/{id}/issue-einvoice`, hệ thống sinh chuỗi XML `<HDon><DLHDon Id="ID1">...</DLHDon></HDon>` và ký bằng chứng thư RSA 2048-bit tự sinh tạm thời trong bộ nhớ (`using var rsa = RSA.Create(2048); var req = new CertificateRequest("CN=ERP_KHO_VN_TAX_COMPLIANCE_SIGNER", ...)` tại `WmsGovernanceOperationsService.cs:3640-3644`), thay vì kết nối USB Token / Cloud HSM thật hoặc gọi API của MISA meInvoice / Viettel S-Invoice / VNPT.

### [FINDING-BE-04] [HIGH] Lỗi `GET` gây hiệu ứng phụ ghi Database (`GET Side-Effect Mutation`) & Thông tin tài khoản ngân hàng cá nhân hardcode trong `SaasBillingService` (`FACT`)
- **Tọa độ:** [SaasBillingService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/SaasBillingService.cs#L12-L16) và [SaasBillingService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/SaasBillingService.cs#L249-L285).
- **Bằng chứng mã nguồn:**
  - Dòng `12–16`: Hardcode thông tin tài khoản ngân hàng mặc định trong mã nguồn:
    ```csharp
    public const string DefaultVietQrBankCode = "TPB";
    public const string DefaultVietQrBankName = "TPBank";
    public const string DefaultVietQrBankBin = "970423";
    public const string DefaultVietQrAccountNo = "98418102005";
    public const string DefaultVietQrAccountName = "HUYNH PHI VAN";
    ```
  - Dòng `260–278`: Trong hàm `GetSubscriptionAsync` (phục vụ `GET /api/v1/saas-billing/subscription`), nếu tenant chưa có bản ghi `TenantSubscription`, hàm `GET` tự động `INSERT` một bản ghi mới với con số giả định `CurrentMonthOrderCount = simulatedUsageOrders ?? 420` (420 đơn hàng!) và gọi `await _db.SaveChangesAsync(cancellationToken)`.

### [FINDING-BE-05] [HIGH] Background Jobs (`SoftReservationExpiryJob`, `DailyInventoryIntegrityJob`, `ThreePlDailySnapshotJob`) bỏ qua khóa 9 chiều hoặc đóng cứng `TenantId = 1` (`FACT`)
- **Tọa độ:**
  1. [SoftReservationExpiryJob.cs](file:///d:/ERP_KHO/ERP.Api/Hosting/SoftReservationExpiryJob.cs#L69-L105): Khi giải phóng `ReservedQuantity` của các lệnh giữ chỗ hết hạn (`StockReservationStatus.Expired`), worker trừ trực tiếp `bucket.ReservedQuantity -= deduct` trên `db.InventoryStocks` mà **không mở Transaction** và **không gọi `AcquireInventoryAllocationLocksAsync`** (`sp_getapplock` + `UPDLOCK`), tạo nguy cơ Race Condition với luồng xuất kho/phân bổ đang chạy song song.
  2. [DailyInventoryIntegrityJob.cs](file:///d:/ERP_KHO/ERP.Api/Hosting/DailyInventoryIntegrityJob.cs#L53-L55): Gọi cứng `db.UseTenant(1)` và chỉ kiểm tra trang đầu tiên (`pageNumber: 1, pageSize: 100`). Các tenant từ `TenantId >= 2` và các bucket từ dòng 101 trở đi không được worker kiểm tra định kỳ.
  3. [ThreePlDailySnapshotJob.cs](file:///d:/ERP_KHO/ERP.Api/Hosting/ThreePlDailySnapshotJob.cs#L51-L90): Gọi cứng `db.UseTenant(1)` và đóng cứng đơn giá lưu kho `dailyPalletRateVnd = 18000m`, `dailyCbmRateVnd = 15000m`, `dailyBinRateVnd = 5000m`.

### [FINDING-BE-06] [MEDIUM] Truy vấn EF Core đồng bộ (`Synchronous Blocking DB Queries`) bên trong `HttpCurrentUser.HasPermission` (`FACT`)
- **Tọa độ:** [HttpCurrentUser.cs](file:///d:/ERP_KHO/ERP.Api/Authorization/HttpCurrentUser.cs#L49-L108).
- **Bằng chứng mã nguồn:**
  - Phương thức `bool HasPermission(string permissionCode)` thực hiện tới **4 câu truy vấn EF Core đồng bộ liên tiếp** (`db.Users...FirstOrDefault()`, `db.Roles...FirstOrDefault()`, `db.Permissions...FirstOrDefault()`, `db.RolePermissions...Any()`) ngay trên luồng xử lý HTTP mỗi khi Controller hoặc Service kiểm tra quyền (ví dụ kiểm tra `inventory.cost.read` trong vòng lặp hoặc kiểm tra `export_receipt.reprint_awb`), gây nguy cơ nghẽn ThreadPool dưới tải cao.

### [FINDING-BE-07] [MEDIUM] Phân mảnh Danh mục Quyền (`AppPermissions` bị tách làm 2 nơi) & Dùng quyền `*Read` kèm `IsInRole(Viewer)` cho các Endpoint Ghi (`FACT`)
- **Tọa độ:**
  - [ERP.Domain/Security/AppPermissions.cs](file:///d:/ERP_KHO/ERP.Domain/Security/AppPermissions.cs#L1-L39) chỉ khai báo 5 hằng số quyền (`users.manage`, `roles.manage`, `inventory.read`, `inventory.cost.read`, `export_receipt.reprint_awb`).
  - [ERP.Api/Authorization/AppPermissions.cs](file:///d:/ERP_KHO/ERP.Api/Authorization/AppPermissions.cs#L1-L27) khai báo **95 mã quyền** thực tế dùng cho các Controller.
  - Nhiều endpoint biến đổi dữ liệu (`POST`) lại gắn `[PermissionAuthorize(AppPermissions.*Read)]` kết hợp câu lệnh thủ công `if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();`:
    - `POST /api/v1/dock-yard/crossdock-dispatch` ([DockYardController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/DockYardController.cs#L186-L195)) gắn `DockRead`.
    - `POST /api/v1/asns/{id}/pre-allocate-staging` ([AsnsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/AsnsController.cs#L58-L67)) gắn `AsnRead`.
    - `POST /api/v1/purchase-orders/expedite-overdue` ([PurchaseOrdersController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/PurchaseOrdersController.cs#L120-L129)) gắn `PurchaseOrderRead`.
    - `POST /api/v1/inventory-control/replenishment/execute` ([InventoryControlController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/InventoryControlController.cs#L24-L33)) gắn `InventoryRead`.
    - `POST /api/v1/packing-sessions/kitting-vas` ([PackingSessionsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/PackingSessionsController.cs#L128-L138)) gắn `PackingRead`.
    - `POST /api/v1/picking-tasks/release-wave` ([PickingTasksController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/PickingTasksController.cs#L140-L150)) gắn `PickingRead`.
    - `POST /api/v1/products/{id}/uoms` ([ProductsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ProductsController.cs#L187-L197)) gắn `ProductRead`.

### [FINDING-BE-08] [MEDIUM] Gắn chồng `[AllowAnonymous]` và `[PermissionAuthorize]` trên cùng một Action (`FACT`)
- **Tọa độ:**
  - [PermissionsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/PermissionsController.cs#L133-L134) (`POST /api/v1/permissions/saas-tenants/provision`)
  - [PermissionsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/PermissionsController.cs#L224-L225) (`POST /api/v1/permissions/saas-billing/vietqr-webhook`)
  - [ShipmentsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ShipmentsController.cs#L271-L272) (`POST /api/v1/shipments/carrier-webhook`)
- **Phân tích:** Do `PermissionAuthorizationFilter.OnAuthorizationAsync` ([PermissionAuthorizeAttribute.cs](file:///d:/ERP_KHO/ERP.Api/Authorization/PermissionAuthorizeAttribute.cs#L61-L64)) kiểm tra `IAllowAnonymous` và `return` sớm, attribute `[PermissionAuthorize]` trên các endpoint này thực chất bị vô hiệu hóa hoàn toàn. Đặc biệt, `POST /api/v1/permissions/saas-tenants/provision` cho phép gọi không cần xác thực nếu không truyền `User` claim!

### [FINDING-BE-09] [LOW/MEDIUM] Logic Nghiệp vụ / Sinh PDF / Phân tích GS1-128 & Chuỗi Fallback `"*-QA-001"` nằm trong API Controllers (`FACT`)
- **Tọa độ:**
  - [ProductBarcodeLookupController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ProductBarcodeLookupController.cs#L172-L273): Bộ phân tích mã vạch `TryParseGs1128` (100 dòng) nằm trực tiếp trong Controller.
  - [PutawayTasksController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/PutawayTasksController.cs#L236-L296): Hàm sinh luồng nhị phân `%PDF-1.4` (`BuildLocationSheetPdf14Bytes`) nằm trực tiếp trong Controller.
  - [BusinessPartnersController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/BusinessPartnersController.cs#L267-L319): Hàm `ComputeDefault3PlBillingFallback` nằm trực tiếp trong Controller.
  - Các chuỗi mặc định mang tên QA (`"ASN-QA-001"`, `"SO-QA-001"`, `"QA-E2E-001"`, `"BIN-A01-03"`, `"IR-QA-{id:D3}"`) xuất hiện trong [AsnsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/AsnsController.cs#L77), [SalesOrdersController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/SalesOrdersController.cs#L35), [DockYardController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/DockYardController.cs#L128-L142), [InventoryMovementsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/InventoryMovementsController.cs#L73), và [ReportOperationsService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/ReportOperationsService.cs#L320-L333).

### [FINDING-BE-10] [LOW] Bất đối xứng Đường dẫn & Màn hình giữa Backend và Frontend (`Cross-Layer Parity`) (`FACT`)
Đối chiếu trực tiếp giữa 42 Backend Controllers và [FRONTEND_AUDIT.md](file:///d:/ERP_KHO/product_reassessment/2026-11-01_REASSESSMENT_001/FRONTEND_AUDIT.md) ([AppRoutes.tsx](file:///d:/ERP_KHO/frontend/src/routes/AppRoutes.tsx#L77-L134)):
1. **Endpoint Backend đã có đầy đủ nhưng Frontend KHÔNG có trang/component gọi tới (`API_ONLY`):**
   - `AccountSecurityController` (`POST /api/v1/account-security/totp/enroll`, `verify`, `disable`, `challenge`, `status`) — Không có màn hình cài đặt 2FA hay bước nhập OTP thứ 2 trên `Login.tsx` (chỉ có hiển thị danh sách và gọi `unlock` / `revoke-sessions` từ `Permissions.tsx`).
   - `CustomerReturns` (`POST /api/v1/import-receipts/customer-returns`, `GET /api/v1/import-receipts/customer-returns`) — Đã có bảng `CustomerReturns`, `CustomerReturnLines` và endpoint trong `ImportReceiptsController`, nhưng Frontend chưa có màn hình Quản lý Hàng bán bị trả lại (RMA).
   - `DataImportsController` & `ImportTemplatesController` (`POST /api/v1/data-imports/{entityType}/validate`, `/commit`, `GET /api/v1/import-templates/{entityType}`) — Backend có bộ import 2 bước (`validate` + `commit`) cho `products`, `suppliers`, `customers`, nhưng Frontend hiện chỉ gọi `POST /api/v1/inventory-control/opening-balance/import` và `GET /api/v1/inventory-control/opening-balance/template` từ `OnboardingWizard.tsx` và `Inventory.tsx`.
2. **Xung đột chồng lấp Route:**
   - Cùng route `POST /api/v1/saas-billing/vietqr-webhook` được khai báo ở **cả 2 Controller**: [SaasBillingController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/SaasBillingController.cs#L101) (gọi `SaasBillingService.HandleVietQrWebhookAsync`) và [PermissionsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/PermissionsController.cs#L223) (`[HttpPost("/api/v1/permissions/saas-billing/vietqr-webhook")]`).

---

## 5. Tổng hợp Độ phủ Kiểm thử Tự động Backend (`Layer 5 Test Audit`) (`FACT`)

Kiểm tra trực tiếp toàn bộ cây thư mục test trong solution [ERP.slnx](file:///d:/ERP_KHO/ERP.slnx):

| Project Kiểm thử | Số File `.cs` | `[Fact]` | `[Theory]` | `[SqlServerFact]` | `[ApprovalSqlServerFact]` | Tổng số Test Methods | Đặc điểm Kiểm chứng |
|---|---:|---:|---:|---:|---:|---:|---|
| `ERP.Application.Tests` | 95 | 341 | 38 | 193 | 0 | **572** | Kiểm thử Unit + SQL Server thật cho Import/Export/Stocktake/Putaway/Reconciliation/Lock/Move/Transfer và Parity giữa `ErpKhoDbContext` & `MigrationSnapshot`. |
| `ERP.Api.Tests` | 39 | 226 | 30 | 0 | 19 | **275** | Kiểm thử Controller, RBAC Metadata (28 tests), Reports (58 tests), Multi-Tenant HTTP Auth, JWT Revocation, Wave 44–51 Omnichannel & SaaS. |
| `tools/ERP.SqlIntegrationHarness` | 2 | — | — | *(Chạy 193)* | *(Chạy 19)* | *(Điều phối **212** SQL tests)* | Tạo DB tạm có gắn bảng đánh dấu `OwnedDatabasePolicy`, chạy toàn bộ 212 bài test `[SqlServerFact]`/`[ApprovalSqlServerFact]` trên SQL Server thật rồi tự dọn dẹp DB. |
| **TỔNG CỘNG BACKEND** | **136** | **567** | **68** | **193** | **19** | **847** | Kết hợp cùng **72 file Vitest** (`.test.ts`/`.test.tsx`) trên Frontend (`frontend/src/`). |
