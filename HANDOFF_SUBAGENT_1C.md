# HANDOFF REPORT — SUBAGENT 1C (Security RBAC, Outbox, Stocktake & Jobs Engineer)

## 1. Phạm vi File đã thực thi (Đúng 12 file trong ranh giới độc quyền)

1. `ERP.Domain/Security/AppPermissions.cs` *(tạo mới)*
2. `ERP.Infrastructure/Services/TokenService.cs`
3. `ERP.Api/Authorization/HttpCurrentUser.cs`
4. `ERP.Infrastructure/Services/InboundPlanningService.cs`
5. `ERP.Infrastructure/Services/PartnerDebtService.cs`
6. `ERP.Infrastructure/Services/ReceiptInboundPlanningIntegration.cs`
7. `ERP.Api/Controllers/InventoryReconciliationController.cs`
8. `ERP.Api/Controllers/StocktakesController.cs`
9. `ERP.Api/Hosting/BuyerPiiRetentionAnonymizerJob.cs`
10. `ERP.Api/Hosting/IntegrationOutboxDispatcherJob.cs` *(tạo mới)*
11. `ERP.Api/Middleware/SupportImpersonationMiddleware.cs` *(tạo mới)*
12. `ERP.Api/Program.cs`

---

## 2. Chi tiết triển khai theo từng hạng mục & Tọa độ `File:Line`

### Nhiệm vụ 1: Khép kín Phân quyền Giá vốn (`OD-05`) & Quyền In lại AWB (`OD-13`)

- **`ERP.Domain/Security/AppPermissions.cs:1-49`**
  - Định nghĩa hằng số chuẩn hóa ở tầng Domain:
    - `InventoryCostRead = "inventory.cost.read"` (`Line 8`)
    - `ExportReceiptReprintAwb = "export_receipt.reprint_awb"` (`Line 9`)
  - Định nghĩa `Definitions` (`Lines 11-18`), `All` (`Lines 20-27`) và `GetDefaultPermissionsForRole(string? roleName)` (`Lines 29-46`):
    - `Admin`: Toàn bộ quyền (`All`).
    - `Manager`: `[InventoryRead, InventoryCostRead, ExportReceiptReprintAwb]`.
    - `Accountant`: `[InventoryRead, InventoryCostRead]`.
    - `WarehouseStaff` & `Viewer`: Chỉ `[InventoryRead]` (tuyệt đối không có `inventory.cost.read` và không có `export_receipt.reprint_awb`).

- **`ERP.Infrastructure/Services/TokenService.cs:17-166`**
  - Bổ sung tham số tùy chọn `ErpKhoDbContext? db = null` vào constructor (`Lines 20-24`) để tương thích ngược với unit test và hỗ trợ DI truy vấn DB thực.
  - Trong `GenerateAccessToken(User user)` (`Lines 26-84`) và `ResolvePermissionCodes(User user, string? roleName)` (`Lines 86-166`):
    - Kết hợp danh mục quyền mặc định theo Role với dữ liệu thực tế từ `user.Role.Permissions` và bảng `RolePermissions`/`Permissions` trong `ErpKhoDbContext`.
    - Đính kèm từng quyền vào JWT qua claim `new Claim("permission", permissionCode)` (`Lines 68-71`).
    - Chặn cứng `inventory.cost.read` đối với `WarehouseStaff` và `Viewer` trừ khi được gán tường minh trên thực thể (`Lines 154-163`).

- **`ERP.Api/Authorization/HttpCurrentUser.cs:29-125`**
  - Loại bỏ hoàn toàn shortcut cứng `Role is "Admin" or "Manager" or "Accountant"` cho mọi permission.
  - Tra cứu trực tiếp `Users` / `Roles` / `Permissions` / `RolePermissions` từ `ErpKhoDbContext` trong `HttpContext.RequestServices` (`Lines 49-107`) để mọi thay đổi thu hồi/cấp quyền trong DB có hiệu lực tức thì.
  - Chặn ngay `inventory.cost.read` trả về `false` đối với `WarehouseStaff` và `Viewer` (`Lines 109-114`).
  - Fallback an toàn về `AppPermissions.GetDefaultPermissionsForRole(effectiveRole)` kết hợp `User.Claims` khi chạy unit test không có DB (`Lines 116-124`).

- **`ERP.Infrastructure/Services/InboundPlanningService.cs:14-56` & `Lines 431-463`**
  - Inject `ICurrentUser? currentUser = null` (`Lines 14-23`) và kiểm tra `CanReadCost()` (`_currentUser.HasPermission(AppPermissions.InventoryCostRead)`).
  - Trong `ListPurchaseOrdersAsync` (`Lines 34-55`) và `Map(PurchaseOrder x)` (`Lines 431-463`): Khi `!canReadCost`, tự động che (mask) `UnitPrice = 0m`, `LineSubTotal = 0m`, `LineTaxAmount = 0m`, `LineTotal = 0m`, `SubTotalAmount = 0m`, `TaxAmount = 0m`, `TotalAmount = 0m`.

---

### Nhiệm vụ 2: Khử Kịch bản Giả & Ghi Đúp Transactional Outbox (`OD-10` & `OD-12`)

- **`ERP.Api/Controllers/InventoryReconciliationController.cs:282-531`**
  - **Xóa bỏ hoàn toàn** khối lệnh tự động `AddRange` + `SaveChangesAsync` 4 bản ghi giả `OUTBOX-GL-001..004` trong `GET /api/InventoryReconciliation/outbox-messages`.
  - **Xóa bỏ hoàn toàn** biểu thức giả tạo `? "DISPATCHED" : "DISPATCHED"`, thay bằng `NormalizeOutboxStatus(m.Status)` (`Lines 457-475`) trả đúng trạng thái thực tế trong DB (`"DISPATCHED"`, `"FAILED"`, `"PENDING"`).
  - Truy vấn thực tế từ `_db.IntegrationOutboxMessages.AsNoTracking().Where(x => x.TenantId == tenantId)` hỗ trợ lọc theo `warehouseId`, `status`, `eventType` (`Lines 389-454`).
  - Trong `DispatchPendingOutboxMessages` (`POST /api/InventoryReconciliation/outbox-messages/dispatch` và `dispatch-pending`, `Lines 328-381` & `Lines 400-426`): Quét các bản ghi `PENDING`/`Pending`/`RETRYING`, ký `HMACSHA256`, cập nhật `Status = "DISPATCHED"`, `PublishedAt = now`, `RetryCount += 1` và lưu DB thật.

- **`ERP.Infrastructure/Services/PartnerDebtService.cs:384-518`** & **`ERP.Infrastructure/Services/ReceiptInboundPlanningIntegration.cs:217-292`**
  - Thống nhất chỉ ghi **1 bản ghi Outbox duy nhất** khi post `ImportReceipt` với khóa chuẩn `$"VALUATION-IMP-{tenantId}-{receipt.Id}"`.
  - Kiểm tra chống trùng trên cả `context.IntegrationOutboxMessages.Local` và `context.IntegrationOutboxMessages` trong DB trước khi thêm mới.
  - Tính đúng `subTotal`, `taxAmount`, `totalAmount` từ `receipt.Details` (`acceptedQty * line.UnitPrice`) cùng chi tiết từng dòng (`lines`) trong `PayloadJson` (loại bỏ hoàn toàn `totalAmount = 0m` cứng).

---

### Nhiệm vụ 3: Sửa Lỗ hổng Bỏ qua Phê duyệt Kiểm kê & Đọc Dữ liệu Thật (`OD-14`)

- **`ERP.Api/Controllers/StocktakesController.cs:48-176`**
  - Trong `GetReasonCodesAndAdjustments`: Truy vấn trực tiếp từ bảng `_db.StocktakeAdjustmentProposals.AsNoTracking().Where(p => p.TenantId == tenantId)` (`Lines 93-107`) thay vì đọc từ `AuditLogs`.
  - Kiểm tra `CanReadCost()` (`Lines 43-45`, `Lines 135-136`): Nếu user không có quyền `inventory.cost.read`, ẩn `unitCost = null` và `varianceValue = null`.

- **`ERP.Api/Controllers/StocktakesController.cs:402-509`**
  - Trong `CreateAdjustmentProposal`: Sửa `var autoApproved = isAutoApproveEligible;` (`Line 403`) — tuyệt đối **không** cho phép `request?.ExecuteImmediately == true` vượt rào tự động duyệt khi chênh lệch vượt ngưỡng `1%` / `500,000 VND`.
  - Khi `!autoApproved`, giữ nguyên `Status = "PENDING_APPROVAL"` (`Line 426`), `ApprovedBy = null`, `ApprovedAtUtc = null` và không gọi `ExecuteProposalAdjustmentCoreAsync` cho tới khi `Admin`/`Manager` gọi `ApproveAdjustmentProposal` (`Lines 511-650`).

- **`ERP.Api/Controllers/StocktakesController.cs:652-675`**
  - Trong `ExecuteProposalAdjustmentCoreAsync`: Gọi `await _db.AcquireInventoryAllocationLocksAsync(warehouseId, new[] { productId }, cancellationToken);` (`Line 672`) ngay trong transaction trước khi đọc và cập nhật `InventoryStock.Quantity`.

---

### Nhiệm vụ 4: Background Jobs & Support Impersonation Middleware

- **`ERP.Api/Hosting/IntegrationOutboxDispatcherJob.cs:1-139`** *(tạo mới)*
  - `BackgroundService` định kỳ quét các bản ghi `IntegrationOutboxMessages` có `Status == "PENDING" | "Pending" | "RETRYING"`, ký `HMACSHA256` (`ComputeHmacSha256Hex`, `Lines 132-138`), cập nhật `Status = "DISPATCHED"`, `PublishedAt = DateTime.UtcNow`, `RetryCount += 1`, hỗ trợ retry exponential backoff và `FAILED` sau tối đa 5 lần thử.

- **`ERP.Api/Middleware/SupportImpersonationMiddleware.cs:1-89`** *(tạo mới)*
  - Kiểm tra header `X-Support-Impersonation-Token` / `X-Support-Session-Code`.
  - Đối chiếu phiên trong `SupportImpersonationSessions` (`Status == "ACTIVE"`, `!RevokedAtUtc.HasValue`, `ExpiresAtUtc > DateTime.UtcNow`). Nếu không hợp lệ hoặc hết hạn, trả về `401 Unauthorized` (`IMPERSONATION_SESSION_INVALID_OR_EXPIRED`, `Lines 54-64`).
  - Gắn `context.Items["ImpersonatedBySupportUserId"] = session.SupportAgentUserId` (`Line 66`).
  - Nếu `session.ReadOnlyScope == true` và HTTP method là `POST`, `PUT`, `PATCH`, `DELETE`, chặn ngay với `403 Forbidden` và mã lỗi `IMPERSONATION_READ_ONLY_ENFORCED` (`Lines 69-79`).

- **`ERP.Api/Hosting/BuyerPiiRetentionAnonymizerJob.cs:58-142`**
  - Mở rộng `RunSingleSweepAsync` để ngoài `ExportReceipts` còn tự động ẩn danh hóa thông tin PII quá hạn (>90 ngày) trên `SalesOrders` (`CancellationReason`) và `CustomerReturns` (`ReturnReason`), nhóm theo `TenantId` và ghi `AuditLog` tuân thủ Nghị định 13/2023/NĐ-CP.

- **`ERP.Api/Program.cs:209-211` & `Lines 310-314`**
  - Đăng ký `builder.Services.AddHostedService<ERP.Api.Hosting.IntegrationOutboxDispatcherJob>();` (`Line 211`).
  - Đăng ký `app.UseMiddleware<ERP.Api.Middleware.SupportImpersonationMiddleware>();` (`Line 312`).

---

## 3. Kết quả Kiểm tra Encoding Gate

- Lệnh thực thi: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-encoding.ps1`
- Kết quả: **PASS (Exit Code 0)** — Toàn bộ file UTF-8 hợp lệ, không có mojibake.
