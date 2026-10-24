# HANDOFF REPORT — SUBAGENT 4A (`6422df5d-bfc7-41fe-94d9-ebff5172e411`)
**Wave 56–57 (`DEPTH-01..07`, `W56-P0-01..04`, `W56-P1-01..04`, `W57-P2-01..02`) — QA Seed, Backend Tests & Frontend Vitest Suite**

---

## 1. `[SKILL ROUTING & ACTIVATION CHECK]`
- **Role:** Role 8 — QA Automation, Reviewer & Debugging (`test-driven-development`, `vitest`, `verification-before-completion`)
- **Skills Activated:** `test-driven-development`, `vitest`, `verification-before-completion`
- **Core Rules Applied:** `PROJECT_RULES.md` (5-Layer Discipline, UTF-8 no-BOM Gate, Zero File Overlap), `rules-testing.md` (`[TST-001]`, `[TST-002]`), `rules-agent-workflow.md` (`[REP-001]`, `[REP-002]`).

---

## 2. Đã Thay Đổi Gì (Exclusive File Ownership — 8 Owned Files) (`FACT`)

1. **[`tools/ERP.QaSeed/Program.cs`](file:///d:/ERP_KHO/tools/ERP.QaSeed/Program.cs)** (`Lines 421–672`):
   - Mở rộng dữ liệu `ERP.QaSeed` cho Wave 56–57 và thực thi thành công lệnh `dotnet run --project tools/ERP.QaSeed/ERP.QaSeed.csproj -c Release -- --apply` (`Exit Code 0`) trên toàn bộ các instance `ERP_KHO` cục bộ (`(localdb)\MSSQLLocalDB` và `localhost`):
     - `ProductUoms`: Seed quy đổi `THUNG24` (1 Thùng = 24 Cái) cho `QA-E2E-001` và `QA-LOT-002` phục vụ Break-Bulk Replenishment (`W57-P2-01`).
     - `HandlingUnits` & `InventoryStocks`: Seed Pallet LPN `PALLET-W56-001` (`SSCC: 008938505000990012`) chứa các dòng tồn kho đa sản phẩm phục vụ 1-Scan Full-LPN Move (`W56-P1-04`).
     - `ProductBundleComponents`: Seed định mức BOM cho `QA-E2E-001` để kiểm thử lắp ráp & xả bộ `DEKIT_DISASSEMBLE` (`W56-P1-03`).
     - `StocktakeDetails`: Seed phiếu kiểm kê có dòng chênh lệch nhỏ (`< 1%` & `<= 500.000đ`) và dòng chênh lệch lớn (`> 1%` hoặc `> 500.000đ`) phục vụ `evaluate-auto-approval` (`W56-P0-01`).
     - `AuditLogs`: Seed dữ liệu đối chiếu 3 chiều PO - ASN - GRN có cờ `REQUIRE_PROCUREMENT_APPROVAL` (> 5% over-receipt) (`W56-P0-02`), cấu hình khấu trừ thuế sàn TMĐT 1,5% (`1% GTGT + 0,5% TNCN` theo Nghị định `70/2025/NĐ-CP`) + phí cố định `3.000đ/đơn` (`W56-P0-03`, `W56-P0-04`), gợi ý thùng 3D Cartonization & DIM Weight (`W56-P1-02`), và 8 ô chia hàng `Put-to-Wall` (`SLOT-01..SLOT-08`) (`W57-P2-02`).

2. **[`ERP.Api.Tests/Wave48To51FullClosureTests.cs`](file:///d:/ERP_KHO/ERP.Api.Tests/Wave48To51FullClosureTests.cs)** (`Lines 1620–2488`):
   - Bổ sung 6 bài kiểm thử Backend `[Fact]` kiểm chứng toàn diện các endpoint Wave 56–57:
     1. `Wave56_StocktakeEvaluateAutoApproval_And_CountAttempts_WorkCorrectly`: Kiểm chứng `POST /api/v1/stocktakes/{id}/evaluate-auto-approval` và `GET /api/v1/stocktakes/{id}/count-attempts-and-auto-approval` phân loại chính xác dòng chênh lệch `< 1%` & `<= 500.000đ` (`AUTO_APPROVE_ELIGIBLE`) và dòng vượt ngưỡng (`REQUIRES_WAREHOUSE_DIRECTOR_APPROVAL`).
     2. `Wave56_ThreeWayMatchPoAsnGrn_Enforces5PercentOverReceiptTolerance`: Kiểm chứng `GET /api/v1/import-receipts/three-way-match-po-asn` gắn trạng thái `OVER_TOLERANCE_BLOCKED`, `requiresProcurementApproval = true` và `overReceiptTolerancePct = 5.0m` khi thực nhận vượt > 5% PO.
     3. `Wave56_CodAndOwnerGrossProfit_IncludeWithheldTax15PctAndFixed3000Fee`: Kiểm chứng `GET /api/v1/reports/cod-shipping-reconciliation` và `GET /api/v1/reports/owner-gross-profit` tính chính xác thuế sàn TMĐT khấu trừ tại nguồn 1,5% (`1% GTGT + 0,5% TNCN` theo Nghị định `70/2025/NĐ-CP`), phí cố định `3.000đ/đơn` và lợi nhuận ròng sau thuế (`netProfitAfterWithheldTax`).
     4. `Wave56_PackingRecommendCarton3D_And_DekitDisassemble_ExecuteCorrectly`: Kiểm chứng `POST /api/v1/packing-sessions/{id}/recommend-carton` (tính thể tích 3D, trọng lượng thực, trọng lượng quy đổi DIM) và `POST /api/v1/packing-sessions/kitting-vas` với `OperationMode = "DEKIT_DISASSEMBLE"` (trừ tồn KIT cha, cộng hoàn tồn thành phần con vào `InventoryStocks` và sinh cặp bút toán `InventoryTransactions` nguyên tử `VAS_DEKIT`).
     5. `Wave56_MoveHandlingUnitLpn_And_AutoBreakBulkReplenish_ExecuteAtomically`: Kiểm chứng `POST /api/v1/inventory/movements/move-handling-unit` (di chuyển nguyên Pallet/LPN đa SKU trong 1 lần quét) và `POST /api/v1/inventory/movements/auto-break-bulk-replenish` (xả 1 thùng = 24 đơn vị cơ sở từ kệ dự trữ sang kệ nhặt lẻ).
     6. `Wave56_PutToWallSlots_And_SortValidation_EnforceSlotMatch`: Kiểm chứng `GET /api/v1/picking-tasks/{id}/put-to-wall-slots` (trả về 8 ô `SLOT-01..SLOT-08`) và `POST /api/v1/picking-tasks/{id}/put-to-wall-sort` (quét đúng ô trả `200 OK`, quét sai ô trả `422 PUT_TO_WALL_WRONG_SLOT`).

3. **[`frontend/src/pages/StocktakesProductionUi.test.tsx`](file:///d:/ERP_KHO/frontend/src/pages/StocktakesProductionUi.test.tsx)** (`Lines 87–164`):
   - Bổ sung test `evaluates stocktake auto-approval threshold (<1% & <=500.000đ) and renders recount round 1/2 table`.

4. **[`frontend/src/pages/CodReconciliation.test.tsx`](file:///d:/ERP_KHO/frontend/src/pages/CodReconciliation.test.tsx)** (`Lines 150–195`):
   - Bổ sung test `renders Decree 70/2025/NĐ-CP 1.5% withheld tax (1% VAT + 0.5% PIT) and 3,000 VND fixed order processing fee breakdown`.

5. **[`frontend/src/pages/CostValuation.test.tsx`](file:///d:/ERP_KHO/frontend/src/pages/CostValuation.test.tsx)** (`Lines 183–221`):
   - Bổ sung test `renders Decree 70/2025/NĐ-CP 1.5% withheld tax, 3,000 VND fixed infrastructure fee, and net profit after tax`.

6. **[`frontend/src/pages/PackingSessions.test.tsx`](file:///d:/ERP_KHO/frontend/src/pages/PackingSessions.test.tsx)** (`Lines 144–202`):
   - Bổ sung test `calculates 3D cartonization & DIM weight and executes DEKIT_DISASSEMBLE kitting mode`.

7. **[`frontend/src/pages/InventoryMovements.test.tsx`](file:///d:/ERP_KHO/frontend/src/pages/InventoryMovements.test.tsx)** (`Lines 74–136`):
   - Bổ sung test `executes 1-scan full-LPN pallet move and auto break-bulk replenishment with carton-to-base conversion`.

8. **[`frontend/src/pages/PickingTasks.test.tsx`](file:///d:/ERP_KHO/frontend/src/pages/PickingTasks.test.tsx)** (`Lines 110–167`):
   - Bổ sung test `loads 8 Put-to-Wall sortation slots (SLOT-01..SLOT-08) and validates barcode scan into slot`.

---

## 3. Bằng Chứng Kiểm Chứng (Verification Evidence) (`FACT`)

- **Gate 1 — Encoding Check:**
  - Command: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-encoding.ps1`
  - Output: `Encoding check passed: all scanned files are valid UTF-8 and no mojibake markers were found.` (`Exit Code 0`)
- **Gate 2 — QA Seed & Backend Tests (`ERP.Api.Tests`):**
  - Command: `dotnet run --project tools/ERP.QaSeed/ERP.QaSeed.csproj -c Release -- --apply`
  - Output: `[ERP.QaSeed] Completed successfully on all reachable ERP_KHO instances.` (`Exit Code 0`)
  - Command: `dotnet test ERP.Api.Tests/ERP.Api.Tests.csproj -c Release --filter "FullyQualifiedName~Wave48To51FullClosureTests"`
  - Output: `Passed! - Failed: 0, Passed: 24, Skipped: 0, Total: 24` (bao gồm 6 bài test `Wave56_*` mới đều PASS 100%).
- **Gate 3 — Frontend Build & Vitest Suite:**
  - Command: `npm --prefix frontend test -- --run src/pages/StocktakesProductionUi.test.tsx src/pages/CodReconciliation.test.tsx src/pages/CostValuation.test.tsx src/pages/PackingSessions.test.tsx src/pages/InventoryMovements.test.tsx src/pages/PickingTasks.test.tsx`
  - Output: `Test Files 6 passed (6) | Tests 27 passed (27)` (`Exit Code 0`).

---

## 4. Những Gì `NOT VERIFIED` / Rủi Ro Tồn Đọng / Việc Dở Dang (`FACT`)
- **Những gì `NOT VERIFIED`:** Toàn bộ phạm vi của Subagent 4A đã được kiểm chứng thực tế bằng lệnh chạy thật (`FACT`), không có mục nào `NOT VERIFIED`.
- **Rủi ro tồn đọng:** `0` (Tuân thủ tuyệt đối `Zero File Overlap`, chỉ chạm đúng 8 file được giao).
- **Việc dở dang / Stub:** `0` (Không có `TODO`, `FIXME`, `NotImplementedException` hay stub).
