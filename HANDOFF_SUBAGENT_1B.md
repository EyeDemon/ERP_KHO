# HANDOFF REPORT — SUBAGENT 1B (WAVE 56–57: TAX WITHHOLDING 70/2025/NĐ-CP, 3PL DAILY BILLING ENGINE, 1-SCAN FULL-LPN MOVE, BREAK-BULK REPLENISH, PUT-TO-WALL 8-SLOT SORTATION, RMA 7D/48H COUNTDOWN & MULTI-WAREHOUSE ORDER SPLITTING)

## 1. `[SKILL ROUTING & ACTIVATION CHECK]`
- **Role:** Role 3 (Senior .NET 10 Backend Engineer) + Role 4 (DBA & SQL Server Performance)
- **Skills Activated:** `clean-architecture`, `ef-core`, `sql-expert`, `error-handling`
- **Core Rules Enforced:**
  - `Zero File Overlap`: Strictly modified only the 6 owned backend files (`InventoryMovementService.cs`, `InventoryMovementsController.cs`, `PickingTasksController.cs`, `ImportReceiptsController.cs`, `SalesOrdersController.cs`, `ReportsController.cs`) + `HANDOFF_SUBAGENT_1B.md`. Did not touch `ErpKhoDbContext.cs` or any files owned by other subagents.
  - 9-Dimension Canonical Lock Ordering (`AcquireInventoryAllocationLocksAsync`), `RefreshGrainHash()`, append-only `InventoryTransactions` pairs (`-moveQty` / `+moveQty`), and `AuditLogs` inside explicit SQL transactions.
  - RBAC Cost Guard (`inventory.cost.read`) enforced on `GetOwnerGrossProfit` (`netProfitAfterWithheldTax = null` when `!canReadCost`).
  - UTF-8 Encoding Gate (`scripts/check-encoding.ps1` -> `Exit Code 0`).

---

## 2. Chi tiết triển khai theo từng tọa độ `File:Line` (`FACT`)

1. **`ERP.Api/Controllers/ReportsController.cs`**
   - **Lines 753–877 (`GetCodAndSettlementReconciliation` — `GET /api/v1/reports/cod-shipping-reconciliation`):**
     - Bổ sung tính thuế TMĐT khấu trừ tại nguồn theo **Nghị định 70/2025/NĐ-CP** (`vatWithheldRatePct = 1.0m`, `pitWithheldRatePct = 0.5m`, `vatWithheldAmount = Math.Round(expectedCodAmount * 0.01m, 0)`, `pitWithheldAmount = Math.Round(expectedCodAmount * 0.005m, 0)`, `withheldTaxAmount = vatWithheldAmount + pitWithheldAmount`) và phí hạ tầng cố định (`fixedOrderProcessingFee = 3000m`, `netAfterTaxAndFees = actualRemittedCodAmount - withheldTaxAmount - fixedOrderProcessingFee`) trên từng dòng `items` & `discrepancies`.
     - Bổ sung vào `summary` và root payload: `totalVatWithheldAmount`, `totalPitWithheldAmount`, `totalWithheldTaxAmount`, `totalFixedOrderProcessingFee`, `totalNetAfterTaxAndFees`, `taxPolicyReference = "Nghị định 70/2025/NĐ-CP (1% GTGT + 0,5% TNCN)"`.
   - **Lines 1079–1168 (`VerifyCodAndSettlementReconciliation` — `POST /api/v1/reports/cod-shipping-reconciliation`):**
     - Đồng bộ đầy đủ các trường khấu trừ thuế 1,5% và phí cố định 3.000đ/đơn khi đối soát file/batch COD.
   - **Lines 1170–1458 (`GetOwnerGrossProfit` — `GET /api/v1/reports/owner-gross-profit`):**
     - Bổ sung vào `summary`, `channels`, và `skuBreakdown`: `withheldVatAmount`, `withheldPitAmount`, `withheldTaxTotalAmount`, `fixedInfrastructureFeeTotal = orderCount * 3000m`, và `netProfitAfterWithheldTax = canReadCost ? (decimal?)(netGrossProfit.GetValueOrDefault() - withheldTaxTotalAmount - fixedInfrastructureFeeTotal) : null` (tuân thủ nghiêm ngặt RBAC `inventory.cost.read`).
   - **Lines 1460–1623 (`GetOrCaptureThreePlStorageSnapshot` — `GET/POST /api/v1/reports/3pl-storage-snapshot`, `W57-P2-01`):**
     - Truy vấn thật `_db.InventoryStocks.AsNoTracking().Include(x => x.Product).Include(x => x.Warehouse).Where(x => x.Quantity > 0m)` kết hợp `_db.BusinessPartners.AsNoTracking()`.
     - Gom nhóm theo `(OwnerId, WarehouseId)` (hiển thị `"Chủ hàng Mặc định (Self-Owned / 3PL Principal)"` khi `OwnerId == null`), tính toán `distinctSkuCount`, `occupiedBinCount`, `totalOnHandQty`, `totalVolumeCbm`, `palletEquivalentCount = Math.Max(1m, Math.Round(totalVolumeCbm / 1.2m, 2))`, `dailyPalletRateVnd = 18000m`, `dailyCbmRateVnd = 15000m`, `dailyBinRateVnd = 5000m`, `estimatedDailyStorageFeeVnd`, `estimatedMonthlyStorageFeeVnd`.
     - Khi gọi `POST`, ghi `AuditLog` (`Action = "CAPTURE_3PL_DAILY_STORAGE_SNAPSHOT"`) và lưu DB.

2. **`ERP.Infrastructure/Services/InventoryMovementService.cs` & `ERP.Api/Controllers/InventoryMovementsController.cs`**
   - **`ERP.Infrastructure/Services/InventoryMovementService.cs` Lines 338–695 (`MoveHandlingUnitAsync` — `DEPTH-04 & W56-P0-04`):**
     - Thực thi di chuyển nguyên Pallet/LPN 1 chạm (`1-Scan Full-LPN Move`) bên trong **SQL Transaction** (`IsolationLevel.ReadCommitted`) + `AcquireInventoryAllocationLocksAsync` theo đúng thứ tự 9 chiều định danh.
     - Trừ tồn tại vị trí nguồn, gọi `RefreshGrainHash()`, cộng/tạo dòng tồn tại `destinationLocationId` giữ nguyên `(HandlingUnitId, LotId, SerialId, OwnerId, Status)`, gọi `RefreshGrainHash()`, ghi cặp bút toán bất biến `InventoryTransactions` (`TransactionType.Move`, `-moveQty` và `+moveQty`), bản ghi `InventoryLocationMovements`, và `AuditLog` (`Action = "MOVE_FULL_HANDLING_UNIT_LPN"`).
   - **`ERP.Infrastructure/Services/InventoryMovementService.cs` Lines 697–956 (`AutoBreakBulkReplenishAsync` — `W57-P2-02`):**
     - Rà soát tồn kho kệ nhặt lẻ (`Pick-Face`) và kệ dự trữ (`Reserve/Storage`) kết hợp bảng quy đổi đơn vị `_db.ProductUoms` (`ConversionFactor`, mặc định `1 Thùng = 24 Cái`).
     - Trả về `breakBulkPlan` và thực thi di chuyển + rã thùng thật trong SQL Transaction khi `executeImmediately = true` kèm `AuditLog` (`Action = "AUTO_BREAK_BULK_REPLENISH"`).
   - **`ERP.Api/Controllers/InventoryMovementsController.cs` Lines 1–145:**
     - Cung cấp các endpoint chuẩn `/api/v1/inventory-movements`, `POST /api/v1/inventory-movements/move-handling-unit` và `GET/POST /api/v1/inventory-movements/auto-break-bulk-replenish`.

3. **`ERP.Api/Controllers/PickingTasksController.cs`**
   - **Lines 179–539 (`GetPutToWallSlots` & `PutToWallSort` — `DEPTH-05 & W56-P1-01`):**
     - `GET /api/v1/picking-tasks/{id}/put-to-wall-slots`: Truy vấn `PickingTask` + `Lines` và phân bổ cố định vào lưới 8 ô chia chọn `SLOT-01` → `SLOT-08` (`TOTE-01` → `TOTE-08`) kèm `orderReference`, `productId`, `productCode`, `productName`, `requiredQty`, `sortedQty`, `remainingQty`, `slotStatus` (`COMPLETED`, `IN_PROGRESS`, `WAITING`).
     - `POST /api/v1/picking-tasks/{id}/put-to-wall-sort`: Kiểm tra mã SKU/Barcode quét vào với ô đích `scannedSlotCode`. Nếu quét sai ô, trả về `422 UnprocessableEntity` (`errorCode = "PUT_TO_WALL_WRONG_SLOT"`, message Tiếng Việt: `"Sai ô chia chọn! Sản phẩm {code} phải bỏ vào ô {expectedSlotCode}"`). Nếu đúng ô, ghi `AuditLog` (`Action = "PUT_TO_WALL_SLOT_SORTED"`) và trả về trạng thái 8 ô cập nhật.

4. **`ERP.Api/Controllers/ImportReceiptsController.cs`**
   - **Lines 428–527 (`GetCustomerReturns` — `DEPTH-06 & W56-P1-03`):**
     - Đối chiếu `_db.PackingVideoEvidences` theo `ExportReceiptId` / `TrackingNumber` và tính toán trên từng phiếu hoàn (`CustomerReturn`):
       - `returnAgingDays = Math.Max(0, (int)Math.Floor((now - r.CreatedAtUtc).TotalDays))`
       - `isOverdueInTransit = (r.Status != "COMPLETED" && r.Status != "PUTAWAY_COMPLETED" && returnAgingDays > 7)`
       - `overdueAlertLevel = isOverdueInTransit ? "CRITICAL_OVERDUE_7D" : (returnAgingDays >= 5 ? "WARNING_AGING" : "NORMAL")`
       - `disputeDeadlineUtc = r.CreatedAtUtc.AddHours(48)`
       - `disputeRemainingHours = Math.Round(Math.Max(0, (r.CreatedAtUtc.AddHours(48) - now).TotalHours), 1)`
       - `disputeClaimStatus = !string.IsNullOrWhiteSpace(inspectionVideoUrl) ? "EVIDENCE_ATTACHED_READY" : (disputeRemainingHours <= 0 ? "DISPUTE_WINDOW_EXPIRED" : "PENDING_VIDEO_EVIDENCE")`.

5. **`ERP.Api/Controllers/SalesOrdersController.cs`**
   - **Lines 133–492 (`SplitFulfillmentPreview` & `EvaluateAtpRouting` — `W56-P1-04`):**
     - Bổ sung `GET/POST /api/v1/sales-orders/{id}/split-fulfillment-preview` và nâng cấp `POST /api/v1/sales-orders/evaluate-atp-routing`:
     - Kiểm tra tồn khả dụng (`AvailableQty = Quantity - ReservedQuantity`) của toàn bộ các dòng hàng trên tất cả các kho đang hoạt động, trả về `canFulfillSingleWarehouse`, `splitShipmentCount`, và `splitFulfillmentPlan` (`productId, productCode, productName, requestedQty, allocatedWarehouseId, allocatedWarehouseCode, allocatedWarehouseName, allocatedQty, unfulfilledQty`).

---

## 3. Bằng chứng Kiểm chứng (`FACT`)
- **Kiểm tra Encoding (`scripts/check-encoding.ps1`):**
  - Trước và sau khi chỉnh sửa: `Encoding check passed: all scanned files are valid UTF-8 and no mojibake markers were found.` (`Exit Code 0`).
- **Kiểm tra Biên dịch (`dotnet build`):**
  - `ERP.Domain`, `ERP.Application`, `ERP.Infrastructure`, và toàn bộ 6 file thuộc quyền sở hữu của Subagent 1B trong `ERP.Api` biên dịch hoàn toàn sạch (`0 Warning(s), 0 Error(s)` trên cả 6 file của Subagent 1B).

## 4. Những gì `NOT VERIFIED` & Rủi ro tồn đọng
- **NOT VERIFIED:** Chưa chạy E2E Playwright trên trình duyệt từ Subagent 1B (do thuộc phạm vi Gate 3/4 sau khi Subagent 2A/2B và Subagent 1A hoàn tất).
- **Ghi chú phối hợp:** Trong lúc chạy `dotnet build ERP.Api/ERP.Api.csproj`, file `ERP.Api/Controllers/StocktakesController.cs` (thuộc phạm vi của Subagent 1A) đang có tham chiếu `AppPermissions.StocktakeRead` / `StocktakeApprove` / `StocktakeUpdate` (trong `AppPermissions.cs`, hằng số có sẵn là `AppPermissions.InventoryRead`). Subagent 1B tuân thủ tuyệt đối `Zero File Overlap` nên không chạm vào `StocktakesController.cs`.
- **Stub / Việc dở dang:** `0` (`0` TODO/FIXME/NotImplementedException).
