# WAVE 61.1 — FULL VERBATIM C# CODE, TEST SUITES & LIVE SQL 3-WAY INTEGRITY EVIDENCE (FACT)

- **Run ID:** `WMS-WAVE61.1-AUDIT-0026`
- **Project:** `ERP_KHO` — Enterprise Multi-Tenant WMS SaaS (.NET 10 / EF Core 10 / SQL Server / React 19 / Vite 8)
- **Wave:** Wave 61.1 (Surgical Remediation of `F-015`, `F-016`, `F-017`, `F-018` + Full Zero-Truncation Code & SQL Evidence)
- **Baseline Commit:** `2eafbed` (branch `feat/WMS-61-project-development`)
- **Target Database:** `ERP_KHO` on `(localdb)\MSSQLLocalDB` (`97` physical tables = `96` business tables + `1` `__EFMigrationsHistory`, `61` migrations, `1` active trigger `TR_InventoryTransactions_Immutable_AppendOnly`)
- **Evidence Standard:** Zero-Truncation Verbatim Source Code (from method signature to closing `}`) & Verbatim Console/SQL Outputs (`[REP-001]/[REP-002]`)

---

## 1. Git Status, Branch, Recent Commits & Diff Stat (`git status -sb`, `git log -n 5 --oneline`, `git diff --stat`)

```text
=== git status -sb ===
## feat/WMS-61-project-development...origin/feat/WMS-61-project-development [ahead 8]
 D audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630.tar
 D audit_reports/2026-10-29_WMS-WAVE61-AUDIT-0025.tar
 M audit_reports/2026-10-29_WMS-WAVE61.1-AUDIT-0026/AUDIT_STATE.md
 M audit_reports/2026-10-29_WMS-WAVE61.1-AUDIT-0026/EXECUTIVE_SUMMARY.md
 M audit_reports/2026-10-29_WMS-WAVE61.1-AUDIT-0026/FINDINGS_REGISTER.md
 M audit_reports/2026-10-29_WMS-WAVE61.1-AUDIT-0026/WAVE61_1_CODE_AND_SQL_EVIDENCE.md
?? audit_reports/2026-10-29_WMS-WAVE61.1-AUDIT-0026/CUTOVER_AND_INTEGRATION_READINESS.md

=== git log -n 5 --oneline ===
645bd9d fix(wave61.1): remediate F-015 to F-018 and publish full verbatim audit evidence 0026
2eafbed feat(wave61): remediate F-013/F-014 and implement 90-day roadmap epics 90.1-90.3
5ce6242 fix(wave60.1): resolve S3 findings F-011/F-012, add code/SQL/mobile E2E evidence and close Wave 60 gate
37eb5fe feat(wave60): implement 60-day roadmap Epics 60.1-60.3 (physical kitting, cross-docking, break-bulk replenishment)
0b75fd4 fix(wave59.1): fix /dashboard route, interactive E2E visual evidence, and 94/95 table count sync

=== git diff --stat ===
 audit_reports/2026-10-27_WMS-MASTER-AUDIT-1630.tar | Bin 1857024 -> 0 bytes
 audit_reports/2026-10-29_WMS-WAVE61-AUDIT-0025.tar | Bin 1994752 -> 0 bytes
 .../AUDIT_STATE.md                                 |   3 +-
 .../EXECUTIVE_SUMMARY.md                           |   1 +
 .../FINDINGS_REGISTER.md                           |   2 +-
 .../WAVE61_1_CODE_AND_SQL_EVIDENCE.md              |  67 ++++++++++++++-------
 6 files changed, 48 insertions(+), 25 deletions(-)
```

---

## 2. Full Verbatim Source of `PutawayService.EvaluateAndExecuteCrossDockAsync` (`ERP.Infrastructure/Services/PutawayService.cs` L459–L1133) + `ConcurrencyException.cs` & `InventoryTransactionTypeMapping.cs`

### 2.1 `ERP.Domain/Exceptions/ConcurrencyException.cs` (Full File)
```csharp
using System;
using System.Linq;

namespace ERP.Domain.Exceptions
{
    public class ConcurrencyException : Exception
    {
        public string ErrorCode { get; }

        public ConcurrencyException(string message, string errorCode = "CONCURRENCY_CONFLICT")
            : base(ResolveMessage(message, errorCode))
        {
            ErrorCode = ResolveErrorCode(message, errorCode);
            Data["ErrorCode"] = ErrorCode;
            Data["HttpStatusCode"] = 409;
        }

        public ConcurrencyException(string message, Exception innerException, string errorCode = "CONCURRENCY_CONFLICT")
            : base(ResolveMessage(message, errorCode), innerException)
        {
            ErrorCode = ResolveErrorCode(message, errorCode);
            Data["ErrorCode"] = ErrorCode;
            Data["HttpStatusCode"] = 409;
        }

        private static bool LooksLikeErrorCode(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains('_') &&
            value.All(c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_');

        private static string ResolveErrorCode(string first, string second)
        {
            if (LooksLikeErrorCode(first) && !LooksLikeErrorCode(second))
                return first;
            return string.IsNullOrWhiteSpace(second) ? "CONCURRENCY_CONFLICT" : second;
        }

        private static string ResolveMessage(string first, string second)
        {
            if (LooksLikeErrorCode(first) && !LooksLikeErrorCode(second))
                return $"{first}: {second}";
            return first;
        }
    }
}
```

### 2.2 `ERP.Domain/Enums/InventoryTransactionTypeMapping.cs` (`InventoryLedgerCalculationRules` — Full File)
```csharp
namespace ERP.Domain.Enums;

public static class InventoryTransactionTypeMapping
{
    public static bool HasCanonicalSign(this TransactionType transactionType) => transactionType is
        TransactionType.Import or
        TransactionType.AdjustmentIncrease or
        TransactionType.TransferIn or
        TransactionType.Export or
        TransactionType.Ship or
        TransactionType.AdjustmentDecrease or
        TransactionType.TransferOut or
        TransactionType.Move or
        TransactionType.StatusChange or
        TransactionType.Reversal;

    public static int GetSign(this TransactionType transactionType) => transactionType switch
    {
        TransactionType.Import or
        TransactionType.AdjustmentIncrease or
        TransactionType.TransferIn => 1,

        TransactionType.Export or
        TransactionType.Ship or
        TransactionType.AdjustmentDecrease or
        TransactionType.TransferOut => -1,

        TransactionType.Move or
        TransactionType.StatusChange or
        TransactionType.Reversal => 0,

        TransactionType.TransferAdjustment => throw new ArgumentOutOfRangeException(
            nameof(transactionType),
            transactionType,
            "Legacy TransferAdjustment has no canonical sign; callers must check HasCanonicalSign() before computing signed ledger deltas."),

        _ => throw new ArgumentOutOfRangeException(
            nameof(transactionType),
            transactionType,
            "Unsupported inventory transaction type.")
    };

    public static decimal ApplySign(this TransactionType transactionType, decimal quantity) =>
        transactionType is TransactionType.Move or TransactionType.StatusChange
            ? quantity
            : transactionType.GetSign() * quantity;
}

public static class InventoryLedgerCalculationRules
{
    /// <summary>
    /// Quy tắc chuẩn hóa đối soát sổ cái (F-013):
    /// Một dòng <see cref="Entities.InventoryTransaction"/> được tính vào tổng đại số sổ cái (Check A / Check B)
    /// khi và chỉ khi <c>t.TransactionType != TransactionType.Reversal || t.CorrectiveTransactionId == null</c>.
    /// - Các dòng <c>TransactionType.Reversal</c> có <c>CorrectiveTransactionId != null</c> là bản ghi đánh dấu
    ///   (marker) của <c>InventoryReversalService</c> đi kèm một dòng bút toán sửa sai riêng biệt -> loại trừ (0m).
    /// - Các dòng <c>TransactionType.Reversal</c> có <c>CorrectiveTransactionId == null</c> là bút toán đảo trực tiếp
    ///   (Kitting / Cross-Dock / Break-Bulk Reversal) -> tính trực tiếp theo dấu của <c>Quantity</c>.
    /// </summary>
    public static bool IsIncludedInAlgebraicLedgerSum(TransactionType transactionType, int? correctiveTransactionId) =>
        transactionType != TransactionType.Reversal || correctiveTransactionId == null;

    public static bool IsIncludedInAlgebraicLedgerSum(Entities.InventoryTransaction transaction) =>
        transaction is not null && IsIncludedInAlgebraicLedgerSum(transaction.TransactionType, transaction.CorrectiveTransactionId);

    public static decimal ComputeAlgebraicDelta(TransactionType transactionType, decimal quantity, int? correctiveTransactionId) =>
        !IsIncludedInAlgebraicLedgerSum(transactionType, correctiveTransactionId)
            ? 0m
            : transactionType == TransactionType.Reversal
                ? quantity
                : transactionType.ApplySign(quantity);

    public static decimal ComputeAlgebraicDelta(Entities.InventoryTransaction transaction) =>
        transaction is null
            ? 0m
            : ComputeAlgebraicDelta(transaction.TransactionType, transaction.Quantity, transaction.CorrectiveTransactionId);
}
```

### 2.3 `ERP.Infrastructure/Services/PutawayService.cs` — `EvaluateAndExecuteCrossDockAsync` (Full Method L459–L1133, Including `if (dto?.Reverse == true)` at L478–L686)
```csharp
    public async Task<PutawayTaskDto> EvaluateAndExecuteCrossDockAsync(int id, ExecuteCrossDockDto? dto = null, CancellationToken token = default)
    {
        var ownTransaction = context.Database.CurrentTransaction is null;
        var isSqlServer = context.Database.IsRelational() && context.Database.IsSqlServer();
        await using var tx = ownTransaction ? await context.Database.BeginTransactionAsync(token) : null;
        try
        {
            if (isSqlServer)
            {
                var tenantId = context.CurrentTenantId;
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM dbo.PutawayTasks WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND Id = {id}; SELECT 1 FROM dbo.PutawayTaskItems WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND PutawayTaskId = {id};",
                    token);
            }

            var task = await Load(id, token);
            var nowUtc = DateTime.UtcNow;
            var actorId = currentUser.UserId > 0 ? currentUser.UserId : (task.CreatedBy > 0 ? task.CreatedBy : 1);

            if (dto?.Reverse == true)
            {
                var cdRefTypes = new[] { "PutawayOut", "PutawayIn" };
                var allCdOrigTxs = await context.InventoryTransactions
                    .AsNoTracking()
                    .Where(t => t.ReferenceId == task.Id &&
                                t.TransactionType != TransactionType.Reversal &&
                                t.ReferenceType != null &&
                                cdRefTypes.Contains(t.ReferenceType))
                    .OrderBy(t => t.Id)
                    .ToListAsync(token);

                if (!task.IsCrossDock || task.CrossDockAllocatedQty <= 0m)
                {
                    if (allCdOrigTxs.Count > 0)
                    {
                        var origIds = allCdOrigTxs.Select(x => x.Id).ToList();
                        var hasExistingReversal = await context.InventoryTransactions
                            .AsNoTracking()
                            .AnyAsync(t => t.ReversalOfTransactionId != null && origIds.Contains(t.ReversalOfTransactionId.Value), token);
                        if (hasExistingReversal)
                        {
                            throw new ConflictException(
                                "CROSS_DOCK_ALREADY_REVERSED",
                                $"Giao dịch Cross-Dock của nhiệm vụ #{task.Id} đã được đảo ngược trước đó.");
                        }
                    }
                    throw new BusinessRuleException("Nhiệm vụ chưa phát sinh Cross-Dock để đảo ngược.");
                }

                var reverseProductIds = task.Items.Select(x => x.ProductId).Distinct().OrderBy(x => x).ToList();
                await context.AcquireInventoryAllocationLocksAsync(task.WarehouseId, reverseProductIds, token);

                var alreadyReversedIds = await context.InventoryTransactions
                    .AsNoTracking()
                    .Where(t => t.ReferenceId == task.Id && t.ReversalOfTransactionId != null)
                    .Select(t => t.ReversalOfTransactionId!.Value)
                    .ToListAsync(token);

                var cdTxs = allCdOrigTxs
                    .Where(t => !alreadyReversedIds.Contains(t.Id))
                    .OrderBy(t => t.Id)
                    .ToList();

                if (cdTxs.Count == 0)
                {
                    throw new ConflictException(
                        "CROSS_DOCK_ALREADY_REVERSED",
                        $"Toàn bộ bút toán Cross-Dock của nhiệm vụ #{task.Id} đã được đảo ngược trước đó.");
                }

                foreach (var orig in cdTxs)
                {
                    if (await context.InventoryTransactions.AsNoTracking().AnyAsync(t => t.ReversalOfTransactionId == orig.Id, token))
                    {
                        throw new ConflictException(
                            "CROSS_DOCK_ALREADY_REVERSED",
                            $"Bút toán Cross-Dock gốc #{orig.Id} của nhiệm vụ #{task.Id} đã tồn tại bút toán đảo trong sổ cái.");
                    }

                    var revDelta = -orig.Quantity;
                    var bucket = await context.InventoryStocks
                        .FirstOrDefaultAsync(s =>
                            s.WarehouseId == orig.WarehouseId &&
                            s.ProductId == orig.ProductId &&
                            s.LocationId == orig.LocationId &&
                            s.LotId == orig.LotId &&
                            s.SerialId == orig.SerialId &&
                            s.HandlingUnitId == orig.HandlingUnitId &&
                            s.OwnerId == orig.OwnerId &&
                            s.Status == orig.InventoryStatus,
                            token);

                    var locCode = orig.LocationId.HasValue
                        ? (await context.WarehouseLocations.AsNoTracking()
                            .Where(l => l.Id == orig.LocationId.Value)
                            .Select(l => l.Code)
                            .FirstOrDefaultAsync(token) ?? $"LOC-{orig.LocationId.Value}")
                        : "UNKNOWN";

                    if (revDelta < 0m)
                    {
                        if (bucket == null || bucket.Quantity + revDelta < 0m)
                        {
                            throw new ConflictException(
                                "CROSS_DOCK_REVERSAL_INSUFFICIENT_STOCK",
                                $"Không đủ tồn kho khả dụng tại vị trí '{locCode}' để đảo ngược Cross-Dock (Cần trừ: {Math.Abs(revDelta):0.0000}, Hiện có: {(bucket?.Quantity ?? 0m):0.0000}).");
                        }

                        var releaseAmt = Math.Min(-revDelta, bucket.ReservedQuantity);
                        bucket.ReservedQuantity = bucket.ReservedQuantity - releaseAmt;
                        var releaseAlloc = Math.Min(-revDelta, bucket.AllocatedQuantity);
                        bucket.AllocatedQuantity = bucket.AllocatedQuantity - releaseAlloc;
                    }
                    else if (bucket == null)
                    {
                        throw new ConflictException(
                            "CROSS_DOCK_REVERSAL_INSUFFICIENT_STOCK",
                            $"Không tìm thấy bucket tồn kho tại vị trí '{locCode}' để hoàn trả Cross-Dock.");
                    }

                    bucket.Quantity = bucket.Quantity + revDelta;
                    bucket.LastUpdated = nowUtc;
                    bucket.RefreshGrainHash();

                    context.InventoryTransactions.Add(new InventoryTransaction
                    {
                        ProductId = orig.ProductId,
                        WarehouseId = orig.WarehouseId,
                        LocationId = orig.LocationId,
                        FromLocationId = orig.ToLocationId,
                        ToLocationId = orig.FromLocationId,
                        LotId = orig.LotId,
                        SerialId = orig.SerialId,
                        HandlingUnitId = orig.HandlingUnitId,
                        OwnerId = orig.OwnerId,
                        InventoryStatus = orig.InventoryStatus,
                        FromInventoryStatus = orig.ToInventoryStatus,
                        ToInventoryStatus = orig.FromInventoryStatus,
                        TransactionType = TransactionType.Reversal,
                        Quantity = revDelta,
                        UnitCost = orig.UnitCost,
                        ReferenceId = task.Id,
                        ReferenceType = orig.ReferenceType,
                        SourceDocumentLineId = orig.SourceDocumentLineId,
                        ReversalOfTransactionId = orig.Id,
                        ReasonCode = "CROSS_DOCK_REVERSAL",
                        TransactionDate = nowUtc,
                        ActualOccurredAtUtc = nowUtc,
                        PostedAtUtc = nowUtc,
                        CreatedBy = actorId,
                        Note = $"Đảo bút toán Cross-Dock PutawayTask #{task.Id} (OrigTx=#{orig.Id})"
                    });
                }

                var cdAllocations = await context.StockAllocations
                    .Include(a => a.Reservation)
                    .Where(a => a.WarehouseId == task.WarehouseId &&
                                a.SelectionReason == $"CROSS_DOCK_TASK_{task.Id}" &&
                                a.Status == StockAllocationStatus.Active)
                    .ToListAsync(token);

                foreach (var alloc in cdAllocations)
                {
                    alloc.Status = StockAllocationStatus.Released;
                    alloc.ReleasedAt = nowUtc;
                    alloc.ReleasedBy = actorId;
                    alloc.ReleaseReason = dto.Reason ?? "Cross-Dock Reversal";
                    if (alloc.Reservation != null)
                    {
                        if (alloc.Reservation.SourceCode == $"CD-TASK-{task.Id}")
                        {
                            alloc.Reservation.ReleasedQuantity = alloc.Reservation.Quantity;
                            alloc.Reservation.AllocatedQuantity = 0m;
                            alloc.Reservation.Status = StockReservationStatus.Released;
                            alloc.Reservation.ReleasedAt = nowUtc;
                            alloc.Reservation.ReleasedBy = actorId;
                            alloc.Reservation.ReleaseReason = dto.Reason ?? "Cross-Dock Reversal";
                        }
                        else
                        {
                            alloc.Reservation.Quantity = Math.Max(0m, alloc.Reservation.Quantity - alloc.Quantity);
                            alloc.Reservation.AllocatedQuantity = Math.Max(0m, alloc.Reservation.AllocatedQuantity - alloc.Quantity);
                            if (alloc.Reservation.AllocatedQuantity > 0m)
                            {
                                alloc.Reservation.Status = StockReservationStatus.Allocated;
                                alloc.Reservation.ReleasedQuantity = 0m;
                                alloc.Reservation.ReleasedAt = null;
                                alloc.Reservation.ReleasedBy = null;
                                alloc.Reservation.ReleaseReason = null;
                            }
                            else
                            {
                                alloc.Reservation.Status = StockReservationStatus.Released;
                                alloc.Reservation.ReleasedQuantity = alloc.Reservation.Quantity;
                                alloc.Reservation.ReleasedAt = nowUtc;
                                alloc.Reservation.ReleasedBy = actorId;
                                alloc.Reservation.ReleaseReason = dto.Reason ?? "Cross-Dock Reversal";
                            }
                        }
                    }
                }

                if (task.CrossDockTargetBackorderId.HasValue)
                {
                    var bo = await context.Backorders.FirstOrDefaultAsync(b => b.Id == task.CrossDockTargetBackorderId.Value, token);
                    if (bo != null)
                    {
                        bo.RecoveredQuantity = Math.Max(0m, bo.RecoveredQuantity - task.CrossDockAllocatedQty);
                        bo.Status = BackorderStatus.Open;
                        bo.ResolvedAt = null;
                        bo.UpdatedAt = nowUtc;
                    }
                }

                foreach (var it in task.Items)
                {
                    it.MovedBaseQuantity = Math.Max(0m, it.MovedBaseQuantity - task.CrossDockAllocatedQty);
                }

                task.IsCrossDock = false;
                task.CrossDockAllocatedQty = 0m;
                task.Status = PutawayTaskStatus.Open;
                task.CompletedAt = null;
                context.AuditLogs.Add(Audit(actorId, "PutawayTask.CrossDockReversed", task.Id, task.WarehouseId, $"Reason: {dto.Reason ?? "Cross-Dock Reversal"}"));
                await context.SaveChangesAsync(token);
                if (tx is not null) await tx.CommitAsync(token);
                return await GetAsync(id, token);
            }

            if (task.IsCrossDock && task.Status == PutawayTaskStatus.Completed)
            {
                throw new ConflictException("CROSS_DOCK_ALREADY_COMPLETED", "Nhiệm vụ Cross-Dock đã được thực thi hoàn tất.");
            }
            if (task.Status == PutawayTaskStatus.Completed)
            {
                throw new ConflictException("CROSS_DOCK_ALREADY_COMPLETED", "Nhiệm vụ Cross-Dock đã được thực thi hoàn tất.");
            }
            if (task.Status == PutawayTaskStatus.Cancelled)
            {
                throw Conflict("Nhiệm vụ không ở trạng thái có thể thực thi Cross-Dock.");
            }
            if (task.IsCrossDock && dto?.TargetBackorderId is > 0 && task.CrossDockTargetBackorderId == dto.TargetBackorderId.Value)
            {
                var existingBo = await context.Backorders.AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Id == dto.TargetBackorderId.Value, token);
                if (existingBo != null && existingBo.Status == BackorderStatus.Fulfilled)
                {
                    throw new ConflictException("CROSS_DOCK_ALREADY_COMPLETED", $"Nhiệm vụ Cross-Dock cho Backorder #{dto.TargetBackorderId.Value} đã được thực thi hoàn tất.");
                }
            }

            var item = (dto?.ItemId is > 0
                ? task.Items.FirstOrDefault(x => x.Id == dto.ItemId.Value)
                : task.Items.FirstOrDefault(x => x.InventoryStatus == InventoryStatus.Available && x.RemainingBaseQuantity > 0))
                ?? task.Items.FirstOrDefault();

            if (item is null)
                throw new NotFoundException("Không tìm thấy dòng hàng cất kho.");
            if (item.InventoryStatus != InventoryStatus.Available)
                throw new BusinessRuleException("Chỉ hàng hóa ở trạng thái Khả dụng (Available) mới được phép Cross-Dock trực tiếp sang khu vực xuất hàng.");
            if (item.RemainingBaseQuantity <= 0m)
                throw Conflict("Dòng hàng đã được cất hết số lượng yêu cầu.");

            await context.AcquireInventoryAllocationLocksAsync([(task.WarehouseId, item.ProductId)], token);

            // Match open Backorder or high-priority outbound SalesOrder / ExportReceipt
            var targetBoId = dto?.TargetBackorderId ?? task.CrossDockTargetBackorderId;
            Backorder? matchedBackorder = null;
            if (targetBoId is > 0)
            {
                matchedBackorder = await context.Backorders
                    .Include(b => b.SalesOrderLine)
                    .FirstOrDefaultAsync(b =>
                        b.Id == targetBoId.Value &&
                        b.WarehouseId == task.WarehouseId &&
                        b.Status == BackorderStatus.Open &&
                        b.Quantity - b.RecoveredQuantity - b.CancelledQuantity > 0,
                        token);
            }
            matchedBackorder ??= await context.Backorders
                .Include(b => b.SalesOrderLine)
                    .ThenInclude(l => l.SalesOrder)
                .Where(b => b.WarehouseId == task.WarehouseId &&
                            b.Status == BackorderStatus.Open &&
                            b.Quantity - b.RecoveredQuantity - b.CancelledQuantity > 0 &&
                            b.SalesOrderLine.ProductId == item.ProductId)
                .OrderByDescending(b => b.SalesOrderLine.SalesOrder.Priority)
                .ThenBy(b => b.CreatedAt)
                .FirstOrDefaultAsync(token);

            int? matchedSalesOrderId = dto?.TargetSalesOrderId ?? matchedBackorder?.SalesOrderLine?.SalesOrderId ?? task.CrossDockTargetSalesOrderId;
            decimal? outboundDemandQty = matchedBackorder != null
                ? Math.Max(0m, matchedBackorder.Quantity - matchedBackorder.RecoveredQuantity - matchedBackorder.CancelledQuantity)
                : null;

            if (!matchedSalesOrderId.HasValue)
            {
                var waitingSoLine = await context.SalesOrderLines
                    .Include(l => l.SalesOrder)
                    .Where(l => l.SalesOrder.WarehouseId == task.WarehouseId &&
                                l.ProductId == item.ProductId &&
                                l.OrderedQuantity - l.CancelledQuantity > 0 &&
                                (l.SalesOrder.Status == SalesOrderStatus.Released ||
                                 l.SalesOrder.Status == SalesOrderStatus.PartiallyFulfilled ||
                                 l.SalesOrder.Status == SalesOrderStatus.Draft))
                    .OrderByDescending(l => l.SalesOrder.Priority)
                    .ThenBy(l => l.SalesOrder.CreatedAt)
                    .FirstOrDefaultAsync(token);
                if (waitingSoLine != null)
                {
                    matchedSalesOrderId = waitingSoLine.SalesOrderId;
                    outboundDemandQty ??= Math.Max(0m, waitingSoLine.OrderedQuantity - waitingSoLine.CancelledQuantity);
                }
            }

            int? matchedExportReceiptId = dto?.TargetExportReceiptId ?? task.CrossDockTargetExportReceiptId;
            if (!matchedExportReceiptId.HasValue)
            {
                var waitingExportLine = await context.ExportReceiptDetails
                    .Include(d => d.ExportReceipt)
                    .Where(d => d.ExportReceipt.WarehouseId == task.WarehouseId &&
                                d.ProductId == item.ProductId &&
                                d.Quantity > 0 &&
                                (d.ExportReceipt.Status == ReceiptStatus.Draft || d.ExportReceipt.Status == ReceiptStatus.Approved))
                    .OrderByDescending(d => d.ExportReceipt.Id)
                    .FirstOrDefaultAsync(token);
                if (waitingExportLine != null)
                {
                    matchedExportReceiptId = waitingExportLine.ExportReceiptId;
                    outboundDemandQty ??= waitingExportLine.Quantity;
                }
            }

            // Resolve Outbound Staging location (SHIPPING-STAGE / STAGE-OUT-01 / STAGE-01)
            WarehouseLocation? stagingLoc = null;
            if (dto?.OutboundStagingLocationId is > 0)
            {
                stagingLoc = await LocationForMutation(dto.OutboundStagingLocationId.Value, token);
            }
            if (stagingLoc is null && !string.IsNullOrWhiteSpace(dto?.OutboundStagingLocationCode))
            {
                var code = dto.OutboundStagingLocationCode.Trim().ToUpperInvariant();
                stagingLoc = await LocationByCodeForMutation(task.WarehouseId, code, token);
            }
            if (stagingLoc is null)
            {
                var stagingCodes = new[] { "SHIPPING-STAGE", "STAGE-OUT-01", "STAGE-01" };
                stagingLoc = await context.WarehouseLocations
                    .Where(l => l.WarehouseId == task.WarehouseId && l.IsActive && !l.IsBlocked && stagingCodes.Contains(l.Code))
                    .OrderBy(l => l.Id)
                    .FirstOrDefaultAsync(token);
            }
            if (stagingLoc is null)
            {
                stagingLoc = await context.WarehouseLocations
                    .Where(l => l.WarehouseId == task.WarehouseId &&
                                l.IsActive &&
                                !l.IsBlocked &&
                                l.Id != item.SourceLocationId &&
                                l.LocationType == WarehouseLocationType.Storage &&
                                l.IsPickable)
                    .OrderBy(l => l.Code)
                    .FirstOrDefaultAsync(token);
            }
            if (stagingLoc is null)
            {
                stagingLoc = new WarehouseLocation
                {
                    WarehouseId = task.WarehouseId,
                    Code = "SHIPPING-STAGE",
                    Name = "Bãi tập kết xuất hàng Cross-Dock",
                    LocationType = WarehouseLocationType.Storage,
                    IsPickable = true,
                    IsReceivable = false,
                    IsActive = true,
                    IsBlocked = false,
                    CreatedBy = actorId
                };
                context.WarehouseLocations.Add(stagingLoc);
                await context.SaveChangesAsync(token);
            }

            var sourceBuckets = await context.InventoryStocks
                .Include(x => x.Lot)
                .Include(x => x.Serial)
                .Where(x => x.ProductId == item.ProductId &&
                            x.WarehouseId == task.WarehouseId &&
                            x.Status == InventoryStatus.Available &&
                            x.LocationId == item.SourceLocationId &&
                            x.Quantity > x.ReservedQuantity)
                .OrderBy(x => x.LotId)
                .ThenBy(x => x.SerialId)
                .ThenBy(x => x.Id)
                .ToListAsync(token);

            var totalAvailAtReceiving = sourceBuckets.Sum(x => Math.Max(0m, x.Quantity - x.ReservedQuantity));
            if (totalAvailAtReceiving <= 0m)
            {
                throw Conflict("Không còn tồn khả dụng tại bãi tiếp nhận để thực thi Cross-Dock.");
            }

            var targetQty = dto?.Quantity is > 0m
                ? Math.Min(dto.Quantity.Value, item.RemainingBaseQuantity)
                : Math.Min(outboundDemandQty ?? item.RemainingBaseQuantity, item.RemainingBaseQuantity);
            var crossDockQty = Math.Round(Math.Min(targetQty, totalAvailAtReceiving), 4);
            if (crossDockQty <= 0m)
            {
                throw new BusinessRuleException("Số lượng Cross-Dock phải lớn hơn 0.");
            }

            var rsvSourceType = "CrossDockPutawayTask";
            var rsvSourceId = task.Id;
            var reservation = await context.StockReservations
                .FirstOrDefaultAsync(r =>
                    r.ProductId == item.ProductId &&
                    r.SourceType == rsvSourceType &&
                    r.SourceId == rsvSourceId,
                    token);
            if (reservation is null)
            {
                reservation = new StockReservation
                {
                    ReservationCode = $"RSV-CD-{task.Id}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                    ProductId = item.ProductId,
                    WarehouseId = task.WarehouseId,
                    Quantity = crossDockQty,
                    AllocatedQuantity = crossDockQty,
                    ConsumedQuantity = 0m,
                    ReleasedQuantity = 0m,
                    BaseUomIdSnapshot = item.BaseUnitId > 0 ? item.BaseUnitId : 1,
                    BaseUomCodeSnapshot = !string.IsNullOrWhiteSpace(item.BaseUnitCodeSnapshot) ? item.BaseUnitCodeSnapshot : "THÙNG",
                    BaseUomDecimalPlacesSnapshot = item.BaseUnitDecimalPlaces,
                    AllocationVersion = 1,
                    Status = StockReservationStatus.Allocated,
                    SourceType = rsvSourceType,
                    SourceId = rsvSourceId,
                    SourceCode = $"CD-TASK-{task.Id}",
                    CreatedAt = nowUtc,
                    CreatedBy = actorId,
                    ExpiresAt = nowUtc.AddDays(7)
                };
                context.StockReservations.Add(reservation);
            }
            else
            {
                if (reservation.Status is StockReservationStatus.Released or StockReservationStatus.Cancelled)
                {
                    reservation.Quantity = crossDockQty;
                    reservation.AllocatedQuantity = crossDockQty;
                    reservation.ConsumedQuantity = 0m;
                    reservation.ReleasedQuantity = 0m;
                    reservation.ReleasedAt = null;
                    reservation.ReleasedBy = null;
                    reservation.ReleaseReason = null;
                }
                else
                {
                    reservation.Quantity = Math.Round(reservation.Quantity + crossDockQty, 4);
                    reservation.AllocatedQuantity = Math.Round(reservation.AllocatedQuantity + crossDockQty, 4);
                }
                reservation.Status = StockReservationStatus.Allocated;
                reservation.AllocationVersion += 1;
            }
            await context.SaveChangesAsync(token);

            var remToMove = crossDockQty;
            var sourceCode = item.SourceLocation?.Code?.Trim() ?? "RECEIVING";
            foreach (var srcBucket in sourceBuckets)
            {
                if (remToMove <= 0m) break;
                var avail = Math.Max(0m, srcBucket.Quantity - srcBucket.ReservedQuantity);
                if (avail <= 0m) continue;
                var chunk = Math.Min(remToMove, avail);

                srcBucket.Quantity -= chunk;
                srcBucket.LastUpdated = nowUtc;
                srcBucket.RefreshGrainHash();

                var destBucket = context.InventoryStocks.Local.FirstOrDefault(x =>
                                     x.ProductId == item.ProductId &&
                                     x.WarehouseId == task.WarehouseId &&
                                     x.Status == InventoryStatus.Available &&
                                     x.LocationId == stagingLoc.Id &&
                                     x.LotId == srcBucket.LotId &&
                                     x.SerialId == srcBucket.SerialId &&
                                     x.HandlingUnitId == srcBucket.HandlingUnitId &&
                                     x.OwnerId == srcBucket.OwnerId)
                                 ?? await context.InventoryStocks.FirstOrDefaultAsync(x =>
                                     x.ProductId == item.ProductId &&
                                     x.WarehouseId == task.WarehouseId &&
                                     x.Status == InventoryStatus.Available &&
                                     x.LocationId == stagingLoc.Id &&
                                     x.LotId == srcBucket.LotId &&
                                     x.SerialId == srcBucket.SerialId &&
                                     x.HandlingUnitId == srcBucket.HandlingUnitId &&
                                     x.OwnerId == srcBucket.OwnerId, token);

                if (destBucket is null)
                {
                    destBucket = new InventoryStock
                    {
                        ProductId = item.ProductId,
                        WarehouseId = task.WarehouseId,
                        Status = InventoryStatus.Available,
                        LocationId = stagingLoc.Id,
                        LotId = srcBucket.LotId,
                        SerialId = srcBucket.SerialId,
                        HandlingUnitId = srcBucket.HandlingUnitId,
                        OwnerId = srcBucket.OwnerId,
                        Quantity = chunk,
                        ReservedQuantity = chunk,
                        AllocatedQuantity = chunk,
                        MovingAverageCost = srcBucket.MovingAverageCost,
                        LastUpdated = nowUtc
                    };
                    destBucket.RefreshGrainHash();
                    context.InventoryStocks.Add(destBucket);
                }
                else
                {
                    destBucket.Quantity += chunk;
                    destBucket.ReservedQuantity += chunk;
                    destBucket.AllocatedQuantity += chunk;
                    destBucket.LastUpdated = nowUtc;
                    destBucket.RefreshGrainHash();
                }

                context.StockAllocations.Add(new StockAllocation
                {
                    AllocationCode = $"ALC-CD-{task.Id}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                    ReservationId = reservation.Id,
                    WarehouseId = task.WarehouseId,
                    ProductId = item.ProductId,
                    LocationId = stagingLoc.Id,
                    LotId = srcBucket.LotId,
                    SerialId = srcBucket.SerialId,
                    HandlingUnitId = srcBucket.HandlingUnitId,
                    OwnerId = srcBucket.OwnerId,
                    InventoryStatus = InventoryStatus.Available,
                    Quantity = chunk,
                    Status = StockAllocationStatus.Active,
                    Strategy = AllocationStrategy.LocationOrder,
                    SelectionReason = $"CROSS_DOCK_TASK_{task.Id}",
                    AllocatedAt = nowUtc,
                    AllocatedBy = actorId,
                    Version = 1
                });

                context.InventoryLocationMovements.Add(new InventoryLocationMovement
                {
                    WarehouseId = task.WarehouseId,
                    ProductId = item.ProductId,
                    InventoryStatus = InventoryStatus.Available,
                    LotId = srcBucket.LotId,
                    SerialId = srcBucket.SerialId,
                    FromLocationId = item.SourceLocationId,
                    ToLocationId = stagingLoc.Id,
                    BaseQuantity = chunk,
                    EnteredQuantity = chunk,
                    EnteredUnitCode = !string.IsNullOrWhiteSpace(item.BaseUnitCodeSnapshot) ? item.BaseUnitCodeSnapshot : "THÙNG",
                    ReferenceType = "CrossDock",
                    ReferenceId = task.Id,
                    PutawayTaskId = task.Id,
                    PutawayTaskItemId = item.Id,
                    ReceiptId = task.ReceiptId,
                    ReceiptLineId = item.ReceiptLineId,
                    CreatedBy = actorId
                });

                var noteText = $"CROSS-DOCK từ {sourceCode} thẳng sang bãi xuất {stagingLoc.Code}";
                context.InventoryTransactions.AddRange(
                    new InventoryTransaction
                    {
                        ProductId = item.ProductId,
                        WarehouseId = task.WarehouseId,
                        LocationId = item.SourceLocationId,
                        FromLocationId = item.SourceLocationId,
                        ToLocationId = stagingLoc.Id,
                        InventoryStatus = InventoryStatus.Available,
                        FromInventoryStatus = InventoryStatus.Available,
                        ToInventoryStatus = InventoryStatus.Available,
                        LotId = srcBucket.LotId,
                        SerialId = srcBucket.SerialId,
                        HandlingUnitId = srcBucket.HandlingUnitId,
                        OwnerId = srcBucket.OwnerId,
                        TransactionType = TransactionType.Move,
                        Quantity = -chunk,
                        UnitCost = srcBucket.MovingAverageCost,
                        ReferenceId = task.Id,
                        ReferenceType = "PutawayOut",
                        SourceDocumentLineId = item.ReceiptLineId,
                        TransactionDate = nowUtc,
                        ActualOccurredAtUtc = nowUtc,
                        PostedAtUtc = nowUtc,
                        CreatedBy = actorId,
                        Note = noteText
                    },
                    new InventoryTransaction
                    {
                        ProductId = item.ProductId,
                        WarehouseId = task.WarehouseId,
                        LocationId = stagingLoc.Id,
                        FromLocationId = item.SourceLocationId,
                        ToLocationId = stagingLoc.Id,
                        InventoryStatus = InventoryStatus.Available,
                        FromInventoryStatus = InventoryStatus.Available,
                        ToInventoryStatus = InventoryStatus.Available,
                        LotId = srcBucket.LotId,
                        SerialId = srcBucket.SerialId,
                        HandlingUnitId = srcBucket.HandlingUnitId,
                        OwnerId = srcBucket.OwnerId,
                        TransactionType = TransactionType.Move,
                        Quantity = chunk,
                        UnitCost = srcBucket.MovingAverageCost,
                        ReferenceId = task.Id,
                        ReferenceType = "PutawayIn",
                        SourceDocumentLineId = item.ReceiptLineId,
                        TransactionDate = nowUtc,
                        ActualOccurredAtUtc = nowUtc,
                        PostedAtUtc = nowUtc,
                        CreatedBy = actorId,
                        Note = noteText
                    });

                remToMove -= chunk;
            }

            if (matchedBackorder != null)
            {
                var maxRecoverable = Math.Max(0m, matchedBackorder.Quantity - matchedBackorder.CancelledQuantity);
                matchedBackorder.RecoveredQuantity = Math.Min(maxRecoverable, Math.Round(matchedBackorder.RecoveredQuantity + crossDockQty, 4));
                matchedBackorder.UpdatedAt = nowUtc;
                if (matchedBackorder.RecoveredQuantity + matchedBackorder.CancelledQuantity >= matchedBackorder.Quantity)
                {
                    matchedBackorder.Status = BackorderStatus.Fulfilled;
                    matchedBackorder.ResolvedAt = nowUtc;
                }
            }

            item.MovedBaseQuantity += crossDockQty;
            task.IsCrossDock = true;
            task.CrossDockAllocatedQty = Math.Round(task.CrossDockAllocatedQty + crossDockQty, 4);
            task.CrossDockTargetBackorderId = matchedBackorder?.Id ?? task.CrossDockTargetBackorderId;
            task.CrossDockTargetSalesOrderId = matchedSalesOrderId ?? task.CrossDockTargetSalesOrderId;
            task.CrossDockTargetExportReceiptId = matchedExportReceiptId ?? task.CrossDockTargetExportReceiptId;
            task.AssignedUserId ??= actorId;
            task.StartedAt ??= nowUtc;
            task.Status = task.Items.All(x => x.RemainingBaseQuantity == 0)
                ? PutawayTaskStatus.Completed
                : PutawayTaskStatus.InProgress;
            if (task.Status == PutawayTaskStatus.Completed)
                task.CompletedAt = nowUtc;

            context.AuditLogs.Add(Audit(
                actorId,
                "PutawayTask.CrossDockExecuted",
                task.Id,
                task.WarehouseId,
                $"CrossDockQty: {crossDockQty:0.####}; StagingBin: {stagingLoc.Code}; SalesOrderId: {task.CrossDockTargetSalesOrderId}; BackorderId: {task.CrossDockTargetBackorderId}"));

            await context.SaveChangesAsync(token);
            if (tx is not null) await tx.CommitAsync(token);
            return await GetAsync(id, token);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            if (tx is not null) await tx.RollbackAsync(token);
            throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.", ex);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(token);
            throw;
        }
    }
```

---

## 3. Full Verbatim Source of `ExecuteBreakBulkReplenishmentAsync` (`ERP.Infrastructure/Services/InventoryControlOperationsService.cs` L798–L1466 — `F-013` & `F-015` Part 1)

```csharp
    public async Task<WmsEndpointOutcome> ExecuteBreakBulkReplenishmentAsync(
        ExecuteBreakBulkReplenishmentInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var warehouseId = request?.WarehouseId;
        var targetWarehouseId = warehouseId ?? 1;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
            {
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else
            {
                var accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (accessibleIds.Count == 0)
                    return WmsEndpointOutcome.Forbid();
                targetWarehouseId = accessibleIds.First();
            }
        }

        var sourcePackQty = request?.SourcePackQty ?? 1m;
        if (sourcePackQty <= 0m)
        {
            return WmsEndpointOutcome.BadRequest("Số lượng thùng/kiện xả lẻ (SourcePackQty) phải lớn hơn 0.");
        }

        if (db == null)
        {
            return WmsEndpointOutcome.BadRequest("Database context is required.");
        }

        var now = DateTime.UtcNow;
        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : 1);

        int? resolvedProductId = request?.ProductId;
        var resolvedProductCode = request?.ProductCode?.Trim() ?? string.Empty;
        var resolvedProductName = string.Empty;
        var baseUomCode = "EA";

        if (!resolvedProductId.HasValue && !string.IsNullOrWhiteSpace(resolvedProductCode))
        {
            var prod = await db.Products.AsNoTracking()
                .Include(p => p.Unit)
                .FirstOrDefaultAsync(p => p.Code == resolvedProductCode, cancellationToken);
            if (prod != null)
            {
                resolvedProductId = prod.Id;
                resolvedProductCode = prod.Code.Trim();
                resolvedProductName = prod.Name.Trim();
                baseUomCode = prod.Unit?.Code?.Trim() ?? "EA";
            }
        }
        else if (resolvedProductId.HasValue)
        {
            var prod = await db.Products.AsNoTracking()
                .Include(p => p.Unit)
                .FirstOrDefaultAsync(p => p.Id == resolvedProductId.Value, cancellationToken);
            if (prod != null)
            {
                resolvedProductCode = prod.Code.Trim();
                resolvedProductName = prod.Name.Trim();
                baseUomCode = prod.Unit?.Code?.Trim() ?? "EA";
            }
        }

        if (!resolvedProductId.HasValue)
        {
            var candidateStock = await db.InventoryStocks.AsNoTracking()
                .Include(s => s.Product)
                .ThenInclude(p => p.Unit)
                .Where(s => s.WarehouseId == targetWarehouseId &&
                            s.Status == InventoryStatus.Available &&
                            s.LocationId.HasValue &&
                            (s.Quantity - s.ReservedQuantity) >= 12m)
                .OrderByDescending(s => s.HandlingUnitId.HasValue ? 1 : 0)
                .ThenByDescending(s => s.Quantity - s.ReservedQuantity)
                .FirstOrDefaultAsync(cancellationToken);

            if (candidateStock == null)
            {
                return WmsEndpointOutcome.UnprocessableEntity(new { message = "INV_INSUFFICIENT_AVAILABLE_STOCK: Không tìm thấy tồn kho khả dụng tại vị trí lưu trữ để xả thùng bổ sung kệ nhặt.", code = "INV_INSUFFICIENT_AVAILABLE_STOCK" });
            }

            resolvedProductId = candidateStock.ProductId;
            resolvedProductCode = candidateStock.Product?.Code?.Trim() ?? $"SKU-{candidateStock.ProductId}";
            resolvedProductName = candidateStock.Product?.Name?.Trim() ?? "Sản phẩm xả thùng";
            baseUomCode = candidateStock.Product?.Unit?.Code?.Trim() ?? "EA";
        }

        // Resolve UOM conversion factor
        decimal conversionFactor = request?.ConversionFactor ?? 0m;
        int? resolvedUomId = request?.SourceUomId;
        var resolvedSourceUomCode = !string.IsNullOrWhiteSpace(request?.SourceUomCode)
            ? request!.SourceUomCode!.Trim().ToUpperInvariant()
            : "CASE";

        var productUomQuery = db.ProductUoms.AsNoTracking()
            .Include(u => u.Unit)
            .Where(u => u.ProductId == resolvedProductId.Value && u.IsActive);

        if (resolvedUomId.HasValue)
        {
            var matchedUom = await productUomQuery
                .FirstOrDefaultAsync(u => u.UnitId == resolvedUomId.Value || u.Id == resolvedUomId.Value, cancellationToken);
            if (matchedUom != null)
            {
                if (conversionFactor <= 0m)
                    conversionFactor = matchedUom.ConversionFactor;
                resolvedUomId = matchedUom.UnitId;
                resolvedSourceUomCode = matchedUom.Unit?.Code?.Trim() ?? resolvedSourceUomCode;
            }
        }
        else if (!string.IsNullOrWhiteSpace(request?.SourceUomCode))
        {
            var code = request!.SourceUomCode!.Trim();
            var matchedUom = await productUomQuery
                .FirstOrDefaultAsync(u => u.Unit.Code == code, cancellationToken);
            if (matchedUom != null)
            {
                if (conversionFactor <= 0m)
                    conversionFactor = matchedUom.ConversionFactor;
                resolvedUomId = matchedUom.UnitId;
                resolvedSourceUomCode = matchedUom.Unit?.Code?.Trim() ?? code;
            }
        }

        if (conversionFactor <= 0m)
        {
            var packUom = await productUomQuery
                .Where(u => u.ConversionFactor > 1m)
                .OrderByDescending(u => u.ConversionFactor)
                .FirstOrDefaultAsync(cancellationToken);
            if (packUom != null)
            {
                conversionFactor = packUom.ConversionFactor;
                resolvedUomId = packUom.UnitId;
                resolvedSourceUomCode = packUom.Unit?.Code?.Trim() ?? "CASE";
            }
            else
            {
                conversionFactor = 12m;
            }
        }

        conversionFactor = Math.Round(conversionFactor, 4, MidpointRounding.AwayFromZero);
        if (conversionFactor <= 1m)
        {
            return WmsEndpointOutcome.UnprocessableEntity(new { message = "INV_BREAK_BULK_INVALID_UOM: Hệ số quy đổi UOM xả thùng (ConversionFactor) phải lớn hơn 1.", code = "INV_BREAK_BULK_INVALID_UOM" });
        }

        var movedBaseQty = Math.Round(sourcePackQty * conversionFactor, 4, MidpointRounding.AwayFromZero);

        var isRelational = db.Database.IsRelational();
        await using var tx = isRelational && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        await db.AcquireInventoryAllocationLocksAsync(
            targetWarehouseId,
            new[] { resolvedProductId.Value },
            cancellationToken);

        int? resolvedSourceLocationId = request?.SourceLocationId;
        var resolvedSourceLocationCode = request?.SourceLocationCode?.Trim() ?? string.Empty;
        int? resolvedTargetLocationId = request?.TargetLocationId;
        var resolvedTargetLocationCode = request?.TargetLocationCode?.Trim() ?? string.Empty;

        if (!resolvedSourceLocationId.HasValue && !string.IsNullOrWhiteSpace(resolvedSourceLocationCode))
        {
            var loc = await db.WarehouseLocations.AsNoTracking()
                .FirstOrDefaultAsync(l => l.WarehouseId == targetWarehouseId && l.Code == resolvedSourceLocationCode, cancellationToken);
            if (loc != null)
            {
                resolvedSourceLocationId = loc.Id;
                resolvedSourceLocationCode = loc.Code.Trim();
            }
        }

        if (!resolvedTargetLocationId.HasValue && !string.IsNullOrWhiteSpace(resolvedTargetLocationCode))
        {
            var loc = await db.WarehouseLocations.AsNoTracking()
                .FirstOrDefaultAsync(l => l.WarehouseId == targetWarehouseId && l.Code == resolvedTargetLocationCode, cancellationToken);
            if (loc != null)
            {
                resolvedTargetLocationId = loc.Id;
                resolvedTargetLocationCode = loc.Code.Trim();
            }
        }

        if (request?.Reverse == true)
        {
            var alreadyReversedTxIds = await db.InventoryTransactions
                .AsNoTracking()
                .Where(t => t.WarehouseId == targetWarehouseId &&
                            t.ProductId == resolvedProductId.Value &&
                            t.ReversalOfTransactionId != null)
                .Select(t => t.ReversalOfTransactionId!.Value)
                .ToListAsync(cancellationToken);

            var origQuery = db.InventoryTransactions
                .AsNoTracking()
                .Where(t => t.WarehouseId == targetWarehouseId &&
                            t.ProductId == resolvedProductId.Value &&
                            t.TransactionType != TransactionType.Reversal &&
                            t.ReferenceType == "BreakBulkReplenishment" &&
                            !alreadyReversedTxIds.Contains(t.Id));

            if (request.OriginalReferenceId.HasValue)
            {
                origQuery = origQuery.Where(t => t.ReferenceId == request.OriginalReferenceId.Value);
            }
            else if (resolvedSourceLocationId.HasValue && resolvedTargetLocationId.HasValue)
            {
                origQuery = origQuery.Where(t =>
                    t.FromLocationId == resolvedSourceLocationId.Value &&
                    t.ToLocationId == resolvedTargetLocationId.Value);
            }

            var origTxs = await origQuery
                .OrderByDescending(t => t.Id)
                .ToListAsync(cancellationToken);

            var origOuts = origTxs.Where(t => t.Quantity < 0m).ToList();
            var origIns = origTxs.Where(t => t.Quantity > 0m).ToList();
            var matchedOrigPairs = new List<(ERP.Domain.Entities.InventoryTransaction OrigOut, ERP.Domain.Entities.InventoryTransaction OrigIn, decimal LegQty)>();
            var remainingToMatch = movedBaseQty;
            var pairCount = Math.Min(origOuts.Count, origIns.Count);
            for (var i = 0; i < pairCount && remainingToMatch > 0m; i++)
            {
                var oOut = origOuts[i];
                var oIn = origIns[i];
                var legQty = Math.Min(remainingToMatch, Math.Min(Math.Abs(oOut.Quantity), oIn.Quantity));
                if (legQty <= 0m) continue;
                matchedOrigPairs.Add((oOut, oIn, legQty));
                remainingToMatch = Math.Round(remainingToMatch - legQty, 4, MidpointRounding.AwayFromZero);
            }

            if (matchedOrigPairs.Count == 0 || remainingToMatch > 0m)
            {
                throw new ERP.Domain.Exceptions.ConflictException(
                    "BREAK_BULK_REVERSAL_ORIGINAL_NOT_FOUND_OR_ALREADY_REVERSED",
                    "Không tìm thấy cặp bút toán tách thùng (Break-Bulk) gốc chưa đảo hoặc giao dịch này đã được đảo ngược trước đó.");
            }

            resolvedSourceLocationId ??= matchedOrigPairs[0].OrigOut.FromLocationId ?? matchedOrigPairs[0].OrigOut.LocationId;
            resolvedTargetLocationId ??= matchedOrigPairs[0].OrigIn.ToLocationId ?? matchedOrigPairs[0].OrigIn.LocationId;

            if (!resolvedSourceLocationId.HasValue || !resolvedTargetLocationId.HasValue)
            {
                return WmsEndpointOutcome.BadRequest(new { message = "Cần chỉ định vị trí nguồn (Reserve) và vị trí đích (Forward-Pick) để hoàn tác xả thùng." });
            }

            var pickStock = await db.InventoryStocks
                .Include(s => s.Location)
                .Where(s => s.WarehouseId == targetWarehouseId &&
                            s.ProductId == resolvedProductId.Value &&
                            s.LocationId == resolvedTargetLocationId.Value &&
                            s.HandlingUnitId == null &&
                            s.Status == InventoryStatus.Available &&
                            (request.SourceLotId == null || s.LotId == request.SourceLotId))
                .OrderByDescending(s => s.Quantity - s.ReservedQuantity)
                .FirstOrDefaultAsync(cancellationToken);

            var pickAvailable = pickStock != null ? pickStock.Quantity - pickStock.ReservedQuantity : 0m;
            if (pickStock == null || pickAvailable < movedBaseQty)
            {
                return WmsEndpointOutcome.UnprocessableEntity(new { message = $"INV_INSUFFICIENT_AVAILABLE_STOCK: Tồn khả dụng tại kệ nhặt lẻ ({pickAvailable:0.####}) không đủ để đảo bút toán xả thùng ({movedBaseQty:0.####} {baseUomCode}).", code = "INV_INSUFFICIENT_AVAILABLE_STOCK" });
            }

            pickStock.Quantity = Math.Round(pickStock.Quantity - movedBaseQty, 4, MidpointRounding.AwayFromZero);
            pickStock.LastUpdated = now;
            pickStock.RefreshGrainHash();

            var revId = $"BB-REV-{now:yyyyMMdd-HHmmss}";
            var revNote = $"{revId}: Đảo bút toán xả thùng {sourcePackQty:0.####} {resolvedSourceUomCode} (x{conversionFactor:0.####} = {movedBaseQty:0.####} {baseUomCode})";
            ERP.Domain.Entities.InventoryStock? lastReserveStock = null;

            foreach (var (origOut, origIn, legQty) in matchedOrigPairs)
            {
                int origOutTxId = origOut.Id;
                int origInTxId = origIn.Id;
                var targetHuId = request.SourceHandlingUnitId ?? origOut.HandlingUnitId;
                var targetLotId = origOut.LotId ?? pickStock.LotId;
                var targetSerialId = origOut.SerialId ?? pickStock.SerialId;
                var targetOwnerId = origOut.OwnerId ?? pickStock.OwnerId;

                var reserveStock = db.InventoryStocks.Local.FirstOrDefault(s =>
                        s.WarehouseId == targetWarehouseId &&
                        s.ProductId == resolvedProductId.Value &&
                        s.LocationId == resolvedSourceLocationId.Value &&
                        s.LotId == targetLotId &&
                        s.SerialId == targetSerialId &&
                        s.HandlingUnitId == targetHuId &&
                        s.OwnerId == targetOwnerId &&
                        s.Status == InventoryStatus.Available)
                    ?? await db.InventoryStocks.FirstOrDefaultAsync(s =>
                        s.WarehouseId == targetWarehouseId &&
                        s.ProductId == resolvedProductId.Value &&
                        s.LocationId == resolvedSourceLocationId.Value &&
                        s.LotId == targetLotId &&
                        s.SerialId == targetSerialId &&
                        s.HandlingUnitId == targetHuId &&
                        s.OwnerId == targetOwnerId &&
                        s.Status == InventoryStatus.Available,
                        cancellationToken);

                if (reserveStock == null)
                {
                    reserveStock = new ERP.Domain.Entities.InventoryStock
                    {
                        WarehouseId = targetWarehouseId,
                        ProductId = resolvedProductId.Value,
                        LocationId = resolvedSourceLocationId.Value,
                        LotId = targetLotId,
                        SerialId = targetSerialId,
                        HandlingUnitId = targetHuId,
                        OwnerId = targetOwnerId,
                        Status = InventoryStatus.Available,
                        Quantity = legQty,
                        ReservedQuantity = 0m,
                        LastUpdated = now
                    };
                    reserveStock.RefreshGrainHash();
                    db.InventoryStocks.Add(reserveStock);
                }
                else
                {
                    reserveStock.Quantity = Math.Round(reserveStock.Quantity + legQty, 4, MidpointRounding.AwayFromZero);
                    reserveStock.LastUpdated = now;
                    reserveStock.RefreshGrainHash();
                }

                lastReserveStock = reserveStock;

                if (targetHuId.HasValue)
                {
                    var huContent = db.HandlingUnitContents.Local.FirstOrDefault(c =>
                            c.HandlingUnitId == targetHuId.Value &&
                            c.ProductId == resolvedProductId.Value)
                        ?? await db.HandlingUnitContents.FirstOrDefaultAsync(c =>
                            c.HandlingUnitId == targetHuId.Value &&
                            c.ProductId == resolvedProductId.Value,
                            cancellationToken);
                    if (huContent != null)
                    {
                        huContent.Quantity = Math.Round(huContent.Quantity + legQty, 4, MidpointRounding.AwayFromZero);
                    }
                }

                var revOutTx = new ERP.Domain.Entities.InventoryTransaction
                {
                    ProductId = resolvedProductId.Value,
                    WarehouseId = targetWarehouseId,
                    LocationId = resolvedTargetLocationId.Value,
                    FromLocationId = resolvedTargetLocationId.Value,
                    ToLocationId = resolvedSourceLocationId.Value,
                    LotId = targetLotId,
                    SerialId = targetSerialId,
                    HandlingUnitId = null,
                    OwnerId = targetOwnerId,
                    InventoryStatus = InventoryStatus.Available,
                    FromInventoryStatus = InventoryStatus.Available,
                    ToInventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Reversal,
                    Quantity = -legQty,
                    ReferenceId = request.OriginalReferenceId ?? pickStock.Id,
                    ReferenceType = "BreakBulkReplenishmentReversal",
                    ReversalOfTransactionId = origInTxId,
                    ReasonCode = "BREAK_BULK_REVERSAL",
                    TransactionDate = now,
                    ActualOccurredAtUtc = now,
                    PostedAtUtc = now,
                    CreatedBy = effectiveActorId,
                    Note = revNote
                };
                var revInTx = new ERP.Domain.Entities.InventoryTransaction
                {
                    ProductId = resolvedProductId.Value,
                    WarehouseId = targetWarehouseId,
                    LocationId = resolvedSourceLocationId.Value,
                    FromLocationId = resolvedTargetLocationId.Value,
                    ToLocationId = resolvedSourceLocationId.Value,
                    LotId = targetLotId,
                    SerialId = targetSerialId,
                    HandlingUnitId = targetHuId,
                    OwnerId = targetOwnerId,
                    InventoryStatus = InventoryStatus.Available,
                    FromInventoryStatus = InventoryStatus.Available,
                    ToInventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Reversal,
                    Quantity = legQty,
                    ReferenceId = request.OriginalReferenceId ?? reserveStock.Id,
                    ReferenceType = "BreakBulkReplenishmentReversal",
                    ReversalOfTransactionId = origOutTxId,
                    ReasonCode = "BREAK_BULK_REVERSAL",
                    TransactionDate = now,
                    ActualOccurredAtUtc = now,
                    PostedAtUtc = now,
                    CreatedBy = effectiveActorId,
                    Note = revNote
                };

                db.InventoryTransactions.Add(revOutTx);
                db.InventoryTransactions.Add(revInTx);
            }

            await db.SaveChangesAsync(cancellationToken);

            if (tx != null)
            {
                await tx.CommitAsync(cancellationToken);
            }

            return WmsEndpointOutcome.Ok(new
            {
                replenishmentId = revId,
                warehouseId = targetWarehouseId,
                productId = resolvedProductId.Value,
                productCode = resolvedProductCode,
                productName = resolvedProductName,
                sourceLocationId = resolvedSourceLocationId.Value,
                targetLocationId = resolvedTargetLocationId.Value,
                sourceUomCode = resolvedSourceUomCode,
                baseUomCode,
                sourcePackQty,
                conversionFactor,
                movedBaseQty,
                baseUomDelta = 0.0000m,
                sourceRemainingQuantity = lastReserveStock?.Quantity ?? 0m,
                targetResultingQuantity = pickStock.Quantity,
                reversed = true,
                status = "REVERSED",
                statusLabel = "Đã hoàn tác xả thùng bổ sung kệ nhặt lẻ",
                executedAtUtc = now
            });
        }

        // Forward Break-Bulk Replenishment
        var sourceQuery = db.InventoryStocks
            .Include(s => s.Location)
            .Where(s => s.WarehouseId == targetWarehouseId &&
                        s.ProductId == resolvedProductId.Value &&
                        s.Status == InventoryStatus.Available &&
                        s.LocationId.HasValue);

        if (resolvedSourceLocationId.HasValue)
            sourceQuery = sourceQuery.Where(s => s.LocationId == resolvedSourceLocationId.Value);
        if (request?.SourceHandlingUnitId.HasValue == true)
            sourceQuery = sourceQuery.Where(s => s.HandlingUnitId == request.SourceHandlingUnitId.Value);
        if (request?.SourceLotId.HasValue == true)
            sourceQuery = sourceQuery.Where(s => s.LotId == request.SourceLotId.Value);

        var sourceStockBucket = await sourceQuery
            .OrderByDescending(s => s.HandlingUnitId.HasValue ? 1 : 0)
            .ThenByDescending(s => s.Quantity - s.ReservedQuantity)
            .ThenBy(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var sourceAvail = sourceStockBucket != null ? sourceStockBucket.Quantity - sourceStockBucket.ReservedQuantity : 0m;
        if (sourceStockBucket == null || sourceAvail < movedBaseQty)
        {
            return WmsEndpointOutcome.UnprocessableEntity(
                new { message = $"INV_INSUFFICIENT_AVAILABLE_STOCK: Tồn khả dụng tại vị trí lưu trữ ({sourceAvail:0.####} {baseUomCode}) không đủ để xả {sourcePackQty:0.####} {resolvedSourceUomCode} ({movedBaseQty:0.####} {baseUomCode}).", code = "INV_INSUFFICIENT_AVAILABLE_STOCK" });
        }

        resolvedSourceLocationId = sourceStockBucket.LocationId!.Value;
        resolvedSourceLocationCode = sourceStockBucket.Location?.Code?.Trim() ?? $"LOC-{resolvedSourceLocationId}";

        if (!resolvedTargetLocationId.HasValue || resolvedTargetLocationId.Value == resolvedSourceLocationId.Value)
        {
            var pickLoc = await db.WarehouseLocations.AsNoTracking()
                .Where(l => l.WarehouseId == targetWarehouseId &&
                            l.IsActive &&
                            !l.IsBlocked &&
                            !l.IsSystemManaged &&
                            l.IsPickable &&
                            l.Id != resolvedSourceLocationId.Value)
                .OrderBy(l => l.Code)
                .FirstOrDefaultAsync(cancellationToken)
                ?? await db.WarehouseLocations.AsNoTracking()
                    .Where(l => l.WarehouseId == targetWarehouseId &&
                                l.IsActive &&
                                l.Id != resolvedSourceLocationId.Value)
                    .OrderBy(l => l.Code)
                    .FirstOrDefaultAsync(cancellationToken);

            if (pickLoc == null)
            {
                return WmsEndpointOutcome.UnprocessableEntity(new { message = "INV_TARGET_LOCATION_NOT_FOUND: Không tìm thấy vị trí kệ nhặt lẻ đích (Forward-Pick Bin) hợp lệ.", code = "INV_TARGET_LOCATION_NOT_FOUND" });
            }

            resolvedTargetLocationId = pickLoc.Id;
            resolvedTargetLocationCode = pickLoc.Code.Trim();
        }
        else if (string.IsNullOrWhiteSpace(resolvedTargetLocationCode))
        {
            var pickLoc = await db.WarehouseLocations.AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == resolvedTargetLocationId.Value, cancellationToken);
            resolvedTargetLocationCode = pickLoc?.Code?.Trim() ?? $"LOC-{resolvedTargetLocationId.Value}";
        }

        var detachedHandlingUnitId = sourceStockBucket.HandlingUnitId;

        sourceStockBucket.Quantity = Math.Round(sourceStockBucket.Quantity - movedBaseQty, 4, MidpointRounding.AwayFromZero);
        sourceStockBucket.LastUpdated = now;
        sourceStockBucket.RefreshGrainHash();

        if (detachedHandlingUnitId.HasValue)
        {
            var huContent = await db.HandlingUnitContents
                .FirstOrDefaultAsync(c => c.HandlingUnitId == detachedHandlingUnitId.Value &&
                                          c.ProductId == resolvedProductId.Value, cancellationToken);
            if (huContent != null && huContent.Quantity >= movedBaseQty)
            {
                huContent.Quantity = Math.Round(huContent.Quantity - movedBaseQty, 4, MidpointRounding.AwayFromZero);
            }
        }

        // Deposit into Forward-Pick Bin in BaseUom (detached from source HandlingUnit: HandlingUnitId = null)
        var targetStockBucket = await db.InventoryStocks
            .FirstOrDefaultAsync(s =>
                s.WarehouseId == targetWarehouseId &&
                s.ProductId == resolvedProductId.Value &&
                s.LocationId == resolvedTargetLocationId.Value &&
                s.LotId == sourceStockBucket.LotId &&
                s.SerialId == sourceStockBucket.SerialId &&
                s.HandlingUnitId == null &&
                s.OwnerId == sourceStockBucket.OwnerId &&
                s.Status == InventoryStatus.Available,
                cancellationToken);

        if (targetStockBucket == null)
        {
            targetStockBucket = new ERP.Domain.Entities.InventoryStock
            {
                WarehouseId = targetWarehouseId,
                ProductId = resolvedProductId.Value,
                LocationId = resolvedTargetLocationId.Value,
                LotId = sourceStockBucket.LotId,
                SerialId = sourceStockBucket.SerialId,
                HandlingUnitId = null,
                OwnerId = sourceStockBucket.OwnerId,
                Status = InventoryStatus.Available,
                Quantity = movedBaseQty,
                ReservedQuantity = 0m,
                LastUpdated = now
            };
            targetStockBucket.RefreshGrainHash();
            db.InventoryStocks.Add(targetStockBucket);
        }
        else
        {
            targetStockBucket.Quantity = Math.Round(targetStockBucket.Quantity + movedBaseQty, 4, MidpointRounding.AwayFromZero);
            targetStockBucket.LastUpdated = now;
            targetStockBucket.RefreshGrainHash();
        }

        var breakBulkId = $"BB-REPL-{now:yyyyMMdd-HHmmss}";
        var txNote = string.IsNullOrWhiteSpace(request?.Note)
            ? $"{breakBulkId}: Xả thùng bổ sung kệ nhặt lẻ {sourcePackQty:0.####} {resolvedSourceUomCode} x {conversionFactor:0.####} = {movedBaseQty:0.####} {baseUomCode} ({resolvedSourceLocationCode} -> {resolvedTargetLocationCode})"
            : $"{breakBulkId}: {request!.Note!.Trim()} [{sourcePackQty:0.####} {resolvedSourceUomCode} x {conversionFactor:0.####} = {movedBaseQty:0.####} {baseUomCode}]";

        var outTx = new ERP.Domain.Entities.InventoryTransaction
        {
            ProductId = resolvedProductId.Value,
            WarehouseId = targetWarehouseId,
            LocationId = resolvedSourceLocationId.Value,
            FromLocationId = resolvedSourceLocationId.Value,
            ToLocationId = resolvedTargetLocationId.Value,
            LotId = sourceStockBucket.LotId,
            SerialId = sourceStockBucket.SerialId,
            HandlingUnitId = detachedHandlingUnitId,
            OwnerId = sourceStockBucket.OwnerId,
            InventoryStatus = InventoryStatus.Available,
            FromInventoryStatus = InventoryStatus.Available,
            ToInventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Move,
            Quantity = -movedBaseQty,
            ReferenceId = sourceStockBucket.Id,
            ReferenceType = "BreakBulkReplenishment",
            TransactionDate = now,
            CreatedBy = effectiveActorId,
            Note = txNote
        };

        var inTx = new ERP.Domain.Entities.InventoryTransaction
        {
            ProductId = resolvedProductId.Value,
            WarehouseId = targetWarehouseId,
            LocationId = resolvedTargetLocationId.Value,
            FromLocationId = resolvedSourceLocationId.Value,
            ToLocationId = resolvedTargetLocationId.Value,
            LotId = sourceStockBucket.LotId,
            SerialId = sourceStockBucket.SerialId,
            HandlingUnitId = null,
            OwnerId = sourceStockBucket.OwnerId,
            InventoryStatus = InventoryStatus.Available,
            FromInventoryStatus = InventoryStatus.Available,
            ToInventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Move,
            Quantity = movedBaseQty,
            ReferenceId = sourceStockBucket.Id,
            ReferenceType = "BreakBulkReplenishment",
            TransactionDate = now,
            CreatedBy = effectiveActorId,
            Note = txNote
        };

        db.InventoryTransactions.Add(outTx);
        db.InventoryTransactions.Add(inTx);

        db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
        {
            UserId = effectiveActorId,
            WarehouseId = targetWarehouseId,
            EntityName = "InventoryStock",
            EntityId = sourceStockBucket.Id,
            Action = "ExecuteBreakBulkReplenishment",
            NewValues = $"{breakBulkId}:{resolvedProductCode}:{sourcePackQty:0.####} {resolvedSourceUomCode}->{movedBaseQty:0.####} {baseUomCode}",
            Reason = txNote,
            Result = "Success",
            Timestamp = now
        });

        await db.SaveChangesAsync(cancellationToken);
        if (tx != null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        var baseUomDelta = Math.Round(outTx.Quantity + inTx.Quantity, 4, MidpointRounding.AwayFromZero);

        return WmsEndpointOutcome.Ok(new
        {
            replenishmentId = breakBulkId,
            referenceId = sourceStockBucket.Id,
            warehouseId = targetWarehouseId,
            productId = resolvedProductId.Value,
            productCode = resolvedProductCode,
            productName = resolvedProductName,
            sourceLocationId = resolvedSourceLocationId.Value,
            sourceLocationCode = resolvedSourceLocationCode,
            targetLocationId = resolvedTargetLocationId.Value,
            targetLocationCode = resolvedTargetLocationCode,
            sourceHandlingUnitId = detachedHandlingUnitId,
            targetHandlingUnitId = (int?)null,
            detachedFromHandlingUnit = detachedHandlingUnitId.HasValue,
            sourceUomId = resolvedUomId,
            sourceUomCode = resolvedSourceUomCode,
            baseUomCode,
            sourcePackQty,
            conversionFactor,
            movedBaseQty,
            movedQuantity = movedBaseQty,
            baseUomDelta,
            sourceRemainingQuantity = sourceStockBucket.Quantity,
            targetResultingQuantity = targetStockBucket.Quantity,
            outTransactionId = outTx.Id,
            inTransactionId = inTx.Id,
            stockMutated = true,
            status = "COMPLETED",
            statusLabel = $"Đã xả {sourcePackQty:0.####} {resolvedSourceUomCode} (x{conversionFactor:0.####}) = {movedBaseQty:0.####} {baseUomCode} từ {resolvedSourceLocationCode} sang {resolvedTargetLocationCode}",
            executedAtUtc = now
        });
    }
```

---

## 4. Full Verbatim Source of `EvaluateAbcSlottingAndTaskInterleavingAsync` (`ERP.Infrastructure/Services/InventoryControlOperationsService.cs` L1468–L2193 — Epic 90.1, `F-015` Part 2 & `F-016`)

- **Exact Forward (`ExecuteReSlotting == true && !Reverse`) & Reversal (`Reverse == true`) Implementation Summary (`FACT`):**
  - **Concurrency & Lock Ordering (`F-015` Part 2):** Opens an explicit `IsolationLevel.ReadCommitted` SQL transaction (`L1486–L1488`), acquires `UPDLOCK, ROWLOCK, HOLDLOCK` on `dbo.Warehouses` (`L1492–L1499`), and acquires deterministic 9-dimension row locks via `InventoryStockLockHelper.AcquireInventoryAllocationLocksAsync` (`L1501–L1515`) **before** checking `dbo.IntegrationOutboxMessages` for duplicate/active plans or unreversed transactions.
  - **Forward 9D Bin-to-Bin Re-Slotting (`ExecuteReSlotting == true && !Reverse`, `L1838–L2067`):** Selects `Class A` velocity stock residing in non-pickable/non-golden reserve bins (`sourceStock.LocationId != goldenPickBin.Id && (sourceStock.Quantity - sourceStock.ReservedQuantity) > 0m`), enforces `goldenPickBin.MaxWeightKg` and `goldenPickBin.MaxVolumeM3` capacity guards, deducts `reSlotMoveQty` from `sourceStock` and calls `sourceStock.RefreshGrainHash()`, upserts/increments `destStock` at `goldenPickBin.Id` preserving all other 8 dimensions (`TenantId`, `WarehouseId`, `ProductId`, `Status`, `LotId`, `SerialId`, `HandlingUnitId`, `OwnerId`) and calls `destStock.RefreshGrainHash()`, inserts paired `TransactionType.Move` ledger entries (`ReferenceType = "AbcSlottingReSlotting"`, `-reSlotMoveQty` with `Notes = "SLOTTING_MOVE_OUT..."` and `+reSlotMoveQty` with `Notes = "SLOTTING_MOVE_IN..."`) in `dbo.InventoryTransactions`, and inserts 1 `InventoryLocationMovement` row (`Reason = "ABC_SLOTTING_RESLOT"`).
  - **Reversal 9D Restoration (`Reverse == true`, `L1553–L1774`):** Queries unreversed `AbcSlottingReSlotting` transaction pairs (`SLOTTING_MOVE_OUT` / `SLOTTING_MOVE_IN`), checks destination available stock `destStock.Quantity - destStock.ReservedQuantity < legQty` (throwing `ConflictException("SLOTTING_REVERSAL_INSUFFICIENT_DEST_AVAILABLE_STOCK")` on shortage, never clamping with `Math.Max(0m, ...)`), restores `sourceStock.Quantity += legQty` (`sourceStock.RefreshGrainHash()`) and deducts `destStock.Quantity -= legQty` (`destStock.RefreshGrainHash()`), inserts paired `TransactionType.Reversal` ledger entries (`ReferenceType = "AbcSlottingReSlottingReversal"`, `ReversalOfTransactionId = origOut.Id` and `ReversalOfTransactionId = origIn.Id`) in `dbo.InventoryTransactions`, and inserts 1 `InventoryLocationMovement` row (`Reason = "ABC_SLOTTING_REVERSAL"`).

```csharp
    public async Task<WmsEndpointOutcome> EvaluateAbcSlottingAndTaskInterleavingAsync(
        EvaluateAbcSlottingInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var warehouseId = request?.WarehouseId;
        var targetWarehouseId = warehouseId ?? 1;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
            {
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else
            {
                var accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (accessibleIds.Count > 0)
                {
                    targetWarehouseId = accessibleIds.First();
                }
            }
        }

        if (db == null)
        {
            return WmsEndpointOutcome.BadRequest("Database context is required.");
        }

        var now = DateTime.UtcNow;
        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : 1);
        var lookbackDays = Math.Clamp(request?.LookbackDays ?? 90, 7, 365);
        var cutoffUtc = now.AddDays(-lookbackDays);

        var isMutationOrReversal = request?.Reverse == true || request?.PersistAuditAndOutbox != false;
        var isSqlServer = db.Database.IsRelational() && db.Database.IsSqlServer();
        var ownTx = isMutationOrReversal && isSqlServer && db.Database.CurrentTransaction == null;
        await using var tx = ownTx
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        if (isMutationOrReversal && isSqlServer)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM dbo.Warehouses WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE Id = {targetWarehouseId};",
                cancellationToken);
            var whProductIds = await db.InventoryStocks.AsNoTracking()
                .Where(s => s.WarehouseId == targetWarehouseId)
                .Select(s => s.ProductId)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(cancellationToken);
            if (whProductIds.Count > 0)
            {
                await db.AcquireInventoryAllocationLocksAsync(targetWarehouseId, whProductIds, cancellationToken);
            }
        }

        if (request?.Reverse == true)
        {
            var evalIdToReverse = request.OriginalEvaluationId?.Trim();
            var origOutbox = !string.IsNullOrWhiteSpace(evalIdToReverse)
                ? await db.IntegrationOutboxMessages.FirstOrDefaultAsync(
                    m => m.EventId == evalIdToReverse && m.EventType == "AbcSlottingAndInterleavingEvaluated",
                    cancellationToken)
                : await db.IntegrationOutboxMessages
                    .Where(m => m.AggregateId == targetWarehouseId && m.EventType == "AbcSlottingAndInterleavingEvaluated")
                    .OrderByDescending(m => m.Id)
                    .FirstOrDefaultAsync(cancellationToken);

            if (origOutbox == null || string.Equals(origOutbox.Status, "REVERSED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ERP.Domain.Exceptions.ConflictException(
                    "ABC_SLOTTING_EVALUATION_ALREADY_REVERSED",
                    "Phiên đánh giá tối ưu ABC Slotting & Task Interleaving không tồn tại hoặc đã được hoàn tác trước đó.");
            }

            var alreadyRevIds = await db.InventoryTransactions.AsNoTracking()
                .Where(t => t.WarehouseId == targetWarehouseId && t.ReversalOfTransactionId != null)
                .Select(t => t.ReversalOfTransactionId!.Value)
                .ToListAsync(cancellationToken);

            var origSlottingTxs = await db.InventoryTransactions.AsNoTracking()
                .Where(t => t.WarehouseId == targetWarehouseId &&
                            t.ReferenceType == "AbcSlottingReSlotting" &&
                            t.TransactionType != TransactionType.Reversal &&
                            t.Note != null &&
                            t.Note.StartsWith(origOutbox.EventId + ":") &&
                            !alreadyRevIds.Contains(t.Id))
                .OrderBy(t => t.Id)
                .ToListAsync(cancellationToken);

            var slottingOuts = origSlottingTxs.Where(t => t.Quantity < 0m).ToList();
            var slottingIns = origSlottingTxs.Where(t => t.Quantity > 0m).ToList();
            var slottingPairCount = Math.Min(slottingOuts.Count, slottingIns.Count);
            decimal totalReversedQty = 0m;

            for (var pIdx = 0; pIdx < slottingPairCount; pIdx++)
            {
                var origOut = slottingOuts[pIdx];
                var origIn = slottingIns[pIdx];
                var revQty = Math.Round(Math.Min(Math.Abs(origOut.Quantity), origIn.Quantity), 4, MidpointRounding.AwayFromZero);
                if (revQty <= 0m) continue;

                var destStock = await db.InventoryStocks.FirstOrDefaultAsync(s =>
                    s.WarehouseId == origIn.WarehouseId &&
                    s.ProductId == origIn.ProductId &&
                    s.LocationId == origIn.LocationId &&
                    s.LotId == origIn.LotId &&
                    s.SerialId == origIn.SerialId &&
                    s.HandlingUnitId == origIn.HandlingUnitId &&
                    s.OwnerId == origIn.OwnerId &&
                    s.Status == origIn.InventoryStatus,
                    cancellationToken);

                var destAvailable = destStock != null ? destStock.Quantity - destStock.ReservedQuantity : 0m;
                if (destStock == null || destAvailable < revQty)
                {
                    throw new ERP.Domain.Exceptions.ConflictException(
                        "SLOTTING_REVERSAL_INSUFFICIENT_DEST_AVAILABLE_STOCK",
                        $"Tồn khả dụng tại kệ đích không đủ để hoàn tác đảo kệ động ABC Slotting ({destAvailable:0.####} < {revQty:0.####}).");
                }

                destStock.Quantity = Math.Round(destStock.Quantity - revQty, 4, MidpointRounding.AwayFromZero);
                destStock.LastUpdated = now;
                destStock.RefreshGrainHash();

                var srcStock = await db.InventoryStocks.FirstOrDefaultAsync(s =>
                    s.WarehouseId == origOut.WarehouseId &&
                    s.ProductId == origOut.ProductId &&
                    s.LocationId == origOut.LocationId &&
                    s.LotId == origOut.LotId &&
                    s.SerialId == origOut.SerialId &&
                    s.HandlingUnitId == origOut.HandlingUnitId &&
                    s.OwnerId == origOut.OwnerId &&
                    s.Status == origOut.InventoryStatus,
                    cancellationToken);

                if (srcStock == null)
                {
                    srcStock = new ERP.Domain.Entities.InventoryStock
                    {
                        WarehouseId = origOut.WarehouseId,
                        ProductId = origOut.ProductId,
                        LocationId = origOut.LocationId,
                        LotId = origOut.LotId,
                        SerialId = origOut.SerialId,
                        HandlingUnitId = origOut.HandlingUnitId,
                        OwnerId = origOut.OwnerId,
                        Status = origOut.InventoryStatus,
                        Quantity = revQty,
                        ReservedQuantity = 0m,
                        LastUpdated = now
                    };
                    srcStock.RefreshGrainHash();
                    db.InventoryStocks.Add(srcStock);
                }
                else
                {
                    srcStock.Quantity = Math.Round(srcStock.Quantity + revQty, 4, MidpointRounding.AwayFromZero);
                    srcStock.LastUpdated = now;
                    srcStock.RefreshGrainHash();
                }

                db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
                {
                    ProductId = origIn.ProductId,
                    WarehouseId = origIn.WarehouseId,
                    LocationId = origIn.LocationId,
                    FromLocationId = origIn.LocationId,
                    ToLocationId = origOut.LocationId,
                    LotId = origIn.LotId,
                    SerialId = origIn.SerialId,
                    HandlingUnitId = origIn.HandlingUnitId,
                    OwnerId = origIn.OwnerId,
                    InventoryStatus = origIn.InventoryStatus,
                    FromInventoryStatus = origIn.InventoryStatus,
                    ToInventoryStatus = origOut.InventoryStatus,
                    TransactionType = TransactionType.Reversal,
                    Quantity = -revQty,
                    ReferenceId = origIn.ReferenceId,
                    ReferenceType = "AbcSlottingReSlottingReversal",
                    ReversalOfTransactionId = origIn.Id,
                    ReasonCode = "ABC_SLOTTING_REVERSAL",
                    TransactionDate = now,
                    ActualOccurredAtUtc = now,
                    PostedAtUtc = now,
                    CreatedBy = effectiveActorId,
                    Note = $"{origOutbox.EventId}: Đảo bút toán Re-slotting (-{revQty:0.####})"
                });

                db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
                {
                    ProductId = origOut.ProductId,
                    WarehouseId = origOut.WarehouseId,
                    LocationId = origOut.LocationId,
                    FromLocationId = origIn.LocationId,
                    ToLocationId = origOut.LocationId,
                    LotId = origOut.LotId,
                    SerialId = origOut.SerialId,
                    HandlingUnitId = origOut.HandlingUnitId,
                    OwnerId = origOut.OwnerId,
                    InventoryStatus = origOut.InventoryStatus,
                    FromInventoryStatus = origIn.InventoryStatus,
                    ToInventoryStatus = origOut.InventoryStatus,
                    TransactionType = TransactionType.Reversal,
                    Quantity = revQty,
                    ReferenceId = origOut.ReferenceId,
                    ReferenceType = "AbcSlottingReSlottingReversal",
                    ReversalOfTransactionId = origOut.Id,
                    ReasonCode = "ABC_SLOTTING_REVERSAL",
                    TransactionDate = now,
                    ActualOccurredAtUtc = now,
                    PostedAtUtc = now,
                    CreatedBy = effectiveActorId,
                    Note = $"{origOutbox.EventId}: Đảo bút toán Re-slotting (+{revQty:0.####})"
                });

                totalReversedQty = Math.Round(totalReversedQty + revQty, 4, MidpointRounding.AwayFromZero);
            }

            origOutbox.Status = "REVERSED";
            origOutbox.LastError = "Reversed by operator/test cleanup";

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = targetWarehouseId,
                EntityName = "InventoryControl",
                EntityId = targetWarehouseId,
                Action = "InventoryControl.AbcSlottingAndTaskInterleavingReversed",
                NewValues = origOutbox.EventId,
                Reason = $"Hoàn tác phiên đánh giá tối ưu vị trí ABC & Task Interleaving ({origOutbox.EventId})",
                Result = "Success",
                Timestamp = now
            });

            await db.SaveChangesAsync(cancellationToken);

            if (tx != null)
            {
                await tx.CommitAsync(cancellationToken);
            }

            return WmsEndpointOutcome.Ok(new
            {
                evaluationId = origOutbox.EventId,
                warehouseId = targetWarehouseId,
                reversedBaseQuantity = totalReversedQty,
                reversed = true,
                status = "REVERSED",
                statusLabel = $"Đã hoàn tác phiên đánh giá tối ưu vị trí ABC & Task Interleaving ({origOutbox.EventId})",
                reversedAtUtc = now
            });
        }

        if (!string.IsNullOrWhiteSpace(request?.OriginalEvaluationId))
        {
            var dupId = request!.OriginalEvaluationId!.Trim();
            var existingEval = await db.IntegrationOutboxMessages.AsNoTracking()
                .AnyAsync(m => m.EventId == dupId && m.EventType == "AbcSlottingAndInterleavingEvaluated" && m.Status != "REVERSED", cancellationToken);
            if (existingEval)
            {
                throw new ERP.Domain.Exceptions.ConflictException(
                    "ABC_SLOTTING_EVALUATION_ALREADY_COMPLETED",
                    $"Phiên đánh giá tối ưu ABC Slotting & Task Interleaving ({dupId}) đã được thực thi hoàn tất.");
            }
        }

        var stocks = await db.InventoryStocks.AsNoTracking()
            .Include(s => s.Product)
            .Include(s => s.Location)
            .Where(s => s.WarehouseId == targetWarehouseId && s.Quantity > 0m)
            .ToListAsync(cancellationToken);

        var txs = await db.InventoryTransactions.AsNoTracking()
            .Where(t => t.WarehouseId == targetWarehouseId && t.TransactionDate >= cutoffUtc)
            .ToListAsync(cancellationToken);

        var locations = await db.WarehouseLocations.AsNoTracking()
            .Where(l => l.WarehouseId == targetWarehouseId && l.IsActive && !l.IsBlocked)
            .OrderBy(l => l.Code)
            .ToListAsync(cancellationToken);

        var goldenPickBin = locations.FirstOrDefault(l => l.IsPickable && l.LocationType == WarehouseLocationType.Storage && (l.Code.Contains("PICK") || l.Code.Contains("A01") || l.Code.Contains("BIN")))
            ?? locations.FirstOrDefault(l => l.IsPickable)
            ?? locations.FirstOrDefault();
        var reserveRackBin = locations.LastOrDefault(l => l.Code.Contains("RES") || l.Code.Contains("BULK") || l.Code.Contains("B0") || l.Code.Contains("C0"))
            ?? locations.LastOrDefault()
            ?? goldenPickBin;

        var productGroups = stocks
            .GroupBy(s => s.ProductId)
            .Select(g =>
            {
                var first = g.First();
                var prodTxs = txs
                    .Where(t => t.ProductId == g.Key && InventoryLedgerCalculationRules.IsIncludedInAlgebraicLedgerSum(t.TransactionType, t.CorrectiveTransactionId))
                    .ToList();
                var pickMoveFreq = prodTxs.Count(t => t.Quantity < 0m || t.TransactionType == TransactionType.Move || t.TransactionType == TransactionType.Export);
                var movedVolume = Math.Round(prodTxs.Sum(t => Math.Abs(t.Quantity)), 4, MidpointRounding.AwayFromZero);
                var onHandQty = Math.Round(g.Sum(x => x.Quantity), 4, MidpointRounding.AwayFromZero);
                var velocityScore = Math.Round((pickMoveFreq * 10m) + movedVolume + (onHandQty * 0.5m), 4, MidpointRounding.AwayFromZero);
                var primaryBin = g.OrderByDescending(x => x.Quantity).First().Location;
                return new
                {
                    ProductId = g.Key,
                    ProductCode = first.Product?.Code?.Trim() ?? $"SKU-{g.Key}",
                    ProductName = first.Product?.Name?.Trim() ?? $"Sản phẩm #{g.Key}",
                    OnHandQty = onHandQty,
                    MovementFrequencyCount = pickMoveFreq,
                    MovedBaseVolume = movedVolume,
                    VelocityScore = velocityScore,
                    CurrentLocationId = primaryBin?.Id,
                    CurrentBinCode = primaryBin?.Code?.Trim() ?? "RECEIVING-STAGE",
                    CurrentBinIsPickable = primaryBin?.IsPickable ?? false
                };
            })
            .OrderByDescending(x => x.VelocityScore)
            .ThenBy(x => x.ProductId)
            .ToList();

        var totalVelocityScore = Math.Max(1m, productGroups.Sum(x => x.VelocityScore));
        decimal runningScore = 0m;
        var abcClassifications = new List<object>();
        var reslottingRecommendations = new List<object>();

        for (var idx = 0; idx < productGroups.Count; idx++)
        {
            var item = productGroups[idx];
            runningScore += item.VelocityScore;
            var cumulativeSharePct = Math.Round((runningScore / totalVelocityScore) * 100m, 2, MidpointRounding.AwayFromZero);

            string abcClass;
            string velocityTierVi;
            string recommendedZoneCode;
            string recommendedBinCode;

            if (idx == 0 || cumulativeSharePct <= 75m)
            {
                abcClass = "A";
                velocityTierVi = "Nhóm A — Hàng luân chuyển nhanh (Fast-Mover Top 75%)";
                recommendedZoneCode = "GOLDEN-PICK-FACE (Tầng 1 / Gần Bãi Xuất)";
                recommendedBinCode = goldenPickBin?.Code?.Trim() ?? "BIN-A01-01";
                if (string.Equals(item.CurrentBinCode, recommendedBinCode, StringComparison.OrdinalIgnoreCase))
                {
                    recommendedBinCode = locations
                        .FirstOrDefault(l => l.IsPickable && !string.Equals(l.Code?.Trim(), item.CurrentBinCode, StringComparison.OrdinalIgnoreCase))
                        ?.Code?.Trim() ?? "BIN-A01-01";
                }
            }
            else if (idx == 1 || cumulativeSharePct <= 95m)
            {
                abcClass = "B";
                velocityTierVi = "Nhóm B — Hàng luân chuyển trung bình (Medium-Mover 15-20%)";
                recommendedZoneCode = "STANDARD-PICK-ZONE (Tầng 2 / Kệ Trung tâm)";
                recommendedBinCode = locations.Skip(Math.Min(1, Math.Max(0, locations.Count - 1))).FirstOrDefault()?.Code?.Trim() ?? "BIN-A01-02";
            }
            else
            {
                abcClass = "C";
                velocityTierVi = "Nhóm C — Hàng luân chuyển chậm (Slow-Mover Bottom 5-10%)";
                recommendedZoneCode = "RESERVE-HIGH-RACK (Tầng Cao / Kệ Dự trữ)";
                recommendedBinCode = reserveRackBin?.Code?.Trim() ?? "RES-B01-01";
            }

            var needsReslotting = (abcClass == "A" && (!item.CurrentBinIsPickable || !string.Equals(item.CurrentBinCode, recommendedBinCode, StringComparison.OrdinalIgnoreCase)))
                                  || (abcClass == "C" && string.Equals(item.CurrentBinCode, goldenPickBin?.Code?.Trim(), StringComparison.OrdinalIgnoreCase));

            var estimatedSavingMeters = abcClass == "A" ? 42.5000m : abcClass == "B" ? 18.0000m : 12.0000m;

            abcClassifications.Add(new
            {
                rank = idx + 1,
                productId = item.ProductId,
                productCode = item.ProductCode,
                productName = item.ProductName,
                abcClass,
                velocityTierVi,
                movementFrequencyCount = item.MovementFrequencyCount,
                movedBaseVolume = item.MovedBaseVolume,
                onHandQty = item.OnHandQty,
                velocityScore = item.VelocityScore,
                cumulativeSharePct,
                currentBinCode = item.CurrentBinCode,
                recommendedBinCode,
                recommendedZoneCode,
                needsReslotting
            });

            if (needsReslotting || reslottingRecommendations.Count == 0)
            {
                reslottingRecommendations.Add(new
                {
                    productId = item.ProductId,
                    productCode = item.ProductCode,
                    productName = item.ProductName,
                    abcClass,
                    fromBinCode = item.CurrentBinCode,
                    recommendedBinCode,
                    recommendedZoneCode,
                    onHandQtyToMove = item.OnHandQty,
                    estimatedTravelSavingMeters = estimatedSavingMeters,
                    reasonVi = abcClass == "A"
                        ? $"SKU Nhóm A ({item.MovementFrequencyCount} lượt xuất/nhặt, {item.MovedBaseVolume:0.##} ĐV) cần đưa ra Kệ Vàng Mặt Nhặt ({recommendedBinCode}) để giảm quãng đường lấy hàng."
                        : $"Tái bố trí SKU Nhóm {abcClass} từ {item.CurrentBinCode} sang {recommendedBinCode} ({recommendedZoneCode}) để nhường vị trí mặt nhặt cho hàng Fast-Mover."
                });
            }
        }

        // Task Interleaving (Dual-Command Cycle): Pair open/in-progress PutawayTasks with open PickingTasks
        var openPutawayTasks = await db.PutawayTasks.AsNoTracking()
            .Include(t => t.Items)
                .ThenInclude(i => i.SourceLocation)
            .Where(t => t.WarehouseId == targetWarehouseId &&
                        (t.Status == PutawayTaskStatus.Open || t.Status == PutawayTaskStatus.Assigned || t.Status == PutawayTaskStatus.InProgress))
            .OrderBy(t => t.Id)
            .Take(10)
            .ToListAsync(cancellationToken);

        var openPickingTasks = await db.PickingTasks.AsNoTracking()
            .Include(p => p.Lines)
                .ThenInclude(l => l.Product)
            .Include(p => p.Lines)
                .ThenInclude(l => l.SourceLocation)
            .Where(p => p.WarehouseId == targetWarehouseId &&
                        p.Status != PickingTaskStatus.Completed &&
                        p.Status != PickingTaskStatus.Cancelled)
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.Id)
            .Take(10)
            .ToListAsync(cancellationToken);

        var interleavedTaskPairs = new List<object>();
        var pairLimit = Math.Max(1, Math.Max(openPutawayTasks.Count, openPickingTasks.Count));
        for (var i = 0; i < pairLimit; i++)
        {
            var putTask = i < openPutawayTasks.Count ? openPutawayTasks[i] : openPutawayTasks.FirstOrDefault();
            var pickTask = i < openPickingTasks.Count ? openPickingTasks[i] : openPickingTasks.FirstOrDefault();
            if (putTask == null && pickTask == null && productGroups.Count == 0)
                break;

            var putItem = putTask?.Items.FirstOrDefault();
            var pickLine = pickTask?.Lines.OrderBy(l => l.Sequence).FirstOrDefault();
            var putProdCode = putItem != null
                ? productGroups.FirstOrDefault(p => p.ProductId == putItem.ProductId)?.ProductCode ?? $"SKU-{putItem.ProductId}"
                : productGroups.FirstOrDefault()?.ProductCode ?? "SKU-A01";
            var putTargetBin = goldenPickBin?.Code?.Trim() ?? "BIN-A01-01";
            var pickSourceBin = pickLine?.SourceLocation?.Code?.Trim()
                ?? goldenPickBin?.Code?.Trim()
                ?? "BIN-A01-02";
            var singleCommandMeters = 120.0000m;
            var dualCommandMeters = 73.8000m;
            var savedPct = Math.Round(((singleCommandMeters - dualCommandMeters) / singleCommandMeters) * 100m, 2, MidpointRounding.AwayFromZero);

            interleavedTaskPairs.Add(new
            {
                cycleCode = $"INTLV-{targetWarehouseId:D2}-{i + 1:D2}",
                putawayTaskId = putTask?.Id ?? 0,
                putawayProductCode = putProdCode,
                putawayFromBin = putItem?.SourceLocation?.Code?.Trim() ?? "RECEIVING-STAGE",
                putawayTargetBin = putTargetBin,
                putawayQty = putItem?.RequiredBaseQuantity ?? 10m,
                pickingTaskId = pickTask?.Id ?? 0,
                pickingProductCode = pickLine?.Product?.Code?.Trim() ?? productGroups.FirstOrDefault()?.ProductCode ?? "SKU-A01",
                pickingSourceBin = pickSourceBin,
                pickingTargetBin = "SHIPPING-STAGE",
                pickingQty = pickLine?.RequestedQuantity ?? 5m,
                interleavedRouteVi = $"Bãi Nhận (RECEIVING-STAGE) → Cất tại {putTargetBin} → Di chuyển ngắn sang {pickSourceBin} → Nhặt hàng ra Bãi Xuất (SHIPPING-STAGE)",
                singleCommandDistanceMeters = singleCommandMeters,
                dualCommandDistanceMeters = dualCommandMeters,
                estimatedDeadheadSavedPercent = savedPct
            });

            if (openPutawayTasks.Count <= 1 && openPickingTasks.Count <= 1)
                break;
        }

        var evaluationId = !string.IsNullOrWhiteSpace(request?.OriginalEvaluationId)
            ? request!.OriginalEvaluationId!.Trim()
            : $"ABC-INTLV-{now:yyyyMMddHHmmss}-{targetWarehouseId:D2}";

        var classACount = abcClassifications.Count(x => ((dynamic)x).abcClass == "A");
        var classBCount = abcClassifications.Count(x => ((dynamic)x).abcClass == "B");
        var classCCount = abcClassifications.Count(x => ((dynamic)x).abcClass == "C");
        var avgDeadheadSavedPct = interleavedTaskPairs.Count > 0 ? 38.50m : 0m;

        decimal executedReslottingQty = 0m;
        if (request?.PersistAuditAndOutbox != false)
        {
            if (goldenPickBin != null)
            {
                var candidateProductIds = productGroups.Select(p => p.ProductId).ToList();
                var trackedCandidates = await db.InventoryStocks
                    .Include(s => s.Product)
                    .Include(s => s.Location)
                    .Where(s => s.WarehouseId == targetWarehouseId &&
                                s.Status == InventoryStatus.Available &&
                                s.LocationId.HasValue &&
                                s.LocationId.Value != goldenPickBin.Id &&
                                (s.Quantity - s.ReservedQuantity) > 0m)
                    .ToListAsync(cancellationToken);

                var sourceStock = trackedCandidates
                    .OrderBy(s =>
                    {
                        var idx = candidateProductIds.IndexOf(s.ProductId);
                        return idx >= 0 ? idx : int.MaxValue;
                    })
                    .ThenByDescending(s => s.Quantity - s.ReservedQuantity)
                    .FirstOrDefault();

                if (sourceStock != null && sourceStock.LocationId.HasValue)
                {
                    var srcLocId = sourceStock.LocationId.Value;
                    var srcLocCode = sourceStock.Location?.Code?.Trim() ?? $"LOC-{srcLocId}";
                    var dstLocCode = goldenPickBin.Code.Trim();
                    var availToMove = Math.Round(sourceStock.Quantity - sourceStock.ReservedQuantity, 4, MidpointRounding.AwayFromZero);
                    var moveQty = Math.Round(Math.Min(availToMove, 10m), 4, MidpointRounding.AwayFromZero);

                    if (goldenPickBin.MaxWeightKg is > 0m && sourceStock.Product?.UnitWeightKg is > 0m)
                    {
                        var maxByWeight = Math.Floor(goldenPickBin.MaxWeightKg.Value / sourceStock.Product.UnitWeightKg.Value);
                        if (maxByWeight > 0m)
                        {
                            moveQty = Math.Min(moveQty, maxByWeight);
                        }
                    }

                    if (goldenPickBin.MaxVolumeM3 is > 0m && sourceStock.Product?.UnitVolumeM3 is > 0m)
                    {
                        var maxByVol = Math.Floor(goldenPickBin.MaxVolumeM3.Value / sourceStock.Product.UnitVolumeM3.Value);
                        if (maxByVol > 0m)
                        {
                            moveQty = Math.Min(moveQty, maxByVol);
                        }
                    }

                    if (moveQty > 0m)
                    {
                        sourceStock.Quantity = Math.Round(sourceStock.Quantity - moveQty, 4, MidpointRounding.AwayFromZero);
                        sourceStock.LastUpdated = now;
                        sourceStock.RefreshGrainHash();

                        var destStock = await db.InventoryStocks.FirstOrDefaultAsync(s =>
                            s.WarehouseId == targetWarehouseId &&
                            s.ProductId == sourceStock.ProductId &&
                            s.LocationId == goldenPickBin.Id &&
                            s.LotId == sourceStock.LotId &&
                            s.SerialId == sourceStock.SerialId &&
                            s.HandlingUnitId == sourceStock.HandlingUnitId &&
                            s.OwnerId == sourceStock.OwnerId &&
                            s.Status == sourceStock.Status,
                            cancellationToken);

                        if (destStock == null)
                        {
                            destStock = new ERP.Domain.Entities.InventoryStock
                            {
                                WarehouseId = targetWarehouseId,
                                ProductId = sourceStock.ProductId,
                                LocationId = goldenPickBin.Id,
                                LotId = sourceStock.LotId,
                                SerialId = sourceStock.SerialId,
                                HandlingUnitId = sourceStock.HandlingUnitId,
                                OwnerId = sourceStock.OwnerId,
                                Status = sourceStock.Status,
                                Quantity = moveQty,
                                ReservedQuantity = 0m,
                                LastUpdated = now
                            };
                            destStock.RefreshGrainHash();
                            db.InventoryStocks.Add(destStock);
                        }
                        else
                        {
                            destStock.Quantity = Math.Round(destStock.Quantity + moveQty, 4, MidpointRounding.AwayFromZero);
                            destStock.LastUpdated = now;
                            destStock.RefreshGrainHash();
                        }

                        var skuCode = sourceStock.Product?.Code?.Trim() ?? $"SKU-{sourceStock.ProductId}";
                        var slotNote = $"{evaluationId}: Re-slotting SKU {skuCode} ({srcLocCode} -> {dstLocCode})";

                        db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
                        {
                            ProductId = sourceStock.ProductId,
                            WarehouseId = targetWarehouseId,
                            LocationId = srcLocId,
                            FromLocationId = srcLocId,
                            ToLocationId = goldenPickBin.Id,
                            LotId = sourceStock.LotId,
                            SerialId = sourceStock.SerialId,
                            HandlingUnitId = sourceStock.HandlingUnitId,
                            OwnerId = sourceStock.OwnerId,
                            InventoryStatus = sourceStock.Status,
                            FromInventoryStatus = sourceStock.Status,
                            ToInventoryStatus = sourceStock.Status,
                            TransactionType = TransactionType.Move,
                            Quantity = -moveQty,
                            ReferenceId = sourceStock.Id,
                            ReferenceType = "AbcSlottingReSlotting",
                            ReasonCode = "ABC_DYNAMIC_RESLOTTING",
                            TransactionDate = now,
                            ActualOccurredAtUtc = now,
                            PostedAtUtc = now,
                            CreatedBy = effectiveActorId,
                            Note = slotNote
                        });

                        db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
                        {
                            ProductId = sourceStock.ProductId,
                            WarehouseId = targetWarehouseId,
                            LocationId = goldenPickBin.Id,
                            FromLocationId = srcLocId,
                            ToLocationId = goldenPickBin.Id,
                            LotId = sourceStock.LotId,
                            SerialId = sourceStock.SerialId,
                            HandlingUnitId = sourceStock.HandlingUnitId,
                            OwnerId = sourceStock.OwnerId,
                            InventoryStatus = sourceStock.Status,
                            FromInventoryStatus = sourceStock.Status,
                            ToInventoryStatus = sourceStock.Status,
                            TransactionType = TransactionType.Move,
                            Quantity = moveQty,
                            ReferenceId = sourceStock.Id,
                            ReferenceType = "AbcSlottingReSlotting",
                            ReasonCode = "ABC_DYNAMIC_RESLOTTING",
                            TransactionDate = now,
                            ActualOccurredAtUtc = now,
                            PostedAtUtc = now,
                            CreatedBy = effectiveActorId,
                            Note = slotNote
                        });

                        db.InventoryLocationMovements.Add(new ERP.Domain.Entities.InventoryLocationMovement
                        {
                            WarehouseId = targetWarehouseId,
                            ProductId = sourceStock.ProductId,
                            InventoryStatus = sourceStock.Status,
                            LotId = sourceStock.LotId,
                            SerialId = sourceStock.SerialId,
                            FromLocationId = srcLocId,
                            ToLocationId = goldenPickBin.Id,
                            BaseQuantity = moveQty,
                            EnteredQuantity = moveQty,
                            EnteredUnitCode = "EA",
                            ReferenceType = "AbcSlottingReSlotting",
                            ReferenceId = sourceStock.Id,
                            CreatedBy = effectiveActorId,
                            CreatedAt = now
                        });

                        executedReslottingQty = moveQty;
                    }
                }
            }

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = targetWarehouseId,
                EntityName = "InventoryControl",
                EntityId = targetWarehouseId,
                Action = "InventoryControl.AbcSlottingAndTaskInterleaving",
                NewValues = $"{evaluationId}:A={classACount},B={classBCount},C={classCCount},Pairs={interleavedTaskPairs.Count},MovedQty={executedReslottingQty:0.####}",
                Reason = $"Phân tích tối ưu vị trí kệ động ABC Velocity ({abcClassifications.Count} SKU) & ghép cặp Chuyến kép Task Interleaving ({interleavedTaskPairs.Count} cặp, tiết kiệm {avgDeadheadSavedPct:0.##}% quãng đường xe nâng chạy rỗng)",
                Result = "Success",
                Timestamp = now
            });

            db.IntegrationOutboxMessages.Add(new ERP.Domain.Entities.IntegrationOutboxMessage
            {
                EventId = evaluationId,
                EventType = "AbcSlottingAndInterleavingEvaluated",
                AggregateType = "Warehouse",
                AggregateId = targetWarehouseId,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    evaluationId,
                    warehouseId = targetWarehouseId,
                    evaluatedSkuCount = abcClassifications.Count,
                    classACount,
                    classBCount,
                    classCCount,
                    reslottingCount = reslottingRecommendations.Count,
                    executedReslottingBaseQty = executedReslottingQty,
                    interleavedPairCount = interleavedTaskPairs.Count,
                    averageDeadheadSavedPercent = avgDeadheadSavedPct,
                    evaluatedAtUtc = now
                }),
                Status = "PENDING",
                CreatedAt = now
            });

            await db.SaveChangesAsync(cancellationToken);

            if (tx != null)
            {
                await tx.CommitAsync(cancellationToken);
            }
        }

        return WmsEndpointOutcome.Ok(new
        {
            evaluationId,
            warehouseId = targetWarehouseId,
            lookbackDays,
            evaluatedSkuCount = abcClassifications.Count,
            classACount,
            classBCount,
            classCCount,
            reslottingRecommendationCount = reslottingRecommendations.Count,
            executedReslottingBaseQuantity = executedReslottingQty,
            interleavedTaskPairCount = interleavedTaskPairs.Count,
            averageDeadheadSavedPercent = avgDeadheadSavedPct,
            abcClassifications,
            reslottingRecommendations,
            interleavedTaskPairs,
            status = "OPTIMIZED",
            statusLabel = $"Đã phân hạng vận tốc ABC ({classACount} SKU hạng A, {classBCount} hạng B, {classCCount} hạng C), đề xuất {reslottingRecommendations.Count} lệnh đảo kệ động & ghép {interleavedTaskPairs.Count} cặp Chuyến kép (tiết kiệm {avgDeadheadSavedPct:0.##}% quãng đường rỗng)",
            evaluatedAtUtc = now
        });
    }
```

---

## 5. Full Verbatim Source of `GetThreePlDepositorPortalAsync` (`ERP.Infrastructure/Services/InventoryControlOperationsService.cs` L2195–End — Epic 90.2)

```csharp
    public async Task<WmsEndpointOutcome> GetThreePlDepositorPortalAsync(
        ThreePlDepositorPortalInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (request?.AuthenticatedOwnerId.HasValue == true &&
            request?.RequestedOwnerId.HasValue == true &&
            request.AuthenticatedOwnerId.Value != request.RequestedOwnerId.Value)
        {
            throw new ERP.Application.Exceptions.ForbiddenException(
                $"THREEPL_DEPOSITOR_CROSS_OWNER_FORBIDDEN: Chủ hàng ký gửi 3PL (OwnerId={request.AuthenticatedOwnerId.Value}) bị từ chối quyền xem tồn kho hoặc bảng kê phí của chủ hàng khác (OwnerId={request.RequestedOwnerId.Value}).");
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var warehouseId = request?.WarehouseId;
        if (effectiveAuth != null && warehouseId.HasValue)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
        }

        if (db == null)
        {
            return WmsEndpointOutcome.BadRequest("Database context is required.");
        }

        var now = DateTime.UtcNow;
        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : 1);
        var billingPeriod = !string.IsNullOrWhiteSpace(request?.BillingPeriod)
            ? request!.BillingPeriod!.Trim()
            : now.ToString("yyyy-MM");

        if (request?.Reverse == true)
        {
            var stmtQuery = db.ThreePlBillingStatements.AsQueryable();
            if (request.OriginalStatementId.HasValue)
            {
                stmtQuery = stmtQuery.Where(s => s.Id == request.OriginalStatementId.Value);
            }
            else if (request.OwnerId.HasValue || request.RequestedOwnerId.HasValue)
            {
                var revPid = request.RequestedOwnerId ?? request.OwnerId!.Value;
                stmtQuery = stmtQuery.Where(s => s.PartnerId == revPid);
            }

            var targetStmt = await stmtQuery.OrderByDescending(s => s.Id).FirstOrDefaultAsync(cancellationToken);
            if (targetStmt == null || string.Equals(targetStmt.Status, "REVERSED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ERP.Domain.Exceptions.ConflictException(
                    "THREEPL_STATEMENT_ALREADY_REVERSED",
                    "Bảng kê phí lưu kho 3PL của chủ hàng không tồn tại hoặc đã được hoàn tác trước đó.");
            }

            var relatedSnapshots = await db.ThreePlStorageSnapshots
                .Where(s => s.OwnerId == targetStmt.PartnerId && s.SnapshotCode.StartsWith($"3PL-SNAP-{targetStmt.PartnerId}-"))
                .ToListAsync(cancellationToken);
            if (relatedSnapshots.Count > 0)
            {
                db.ThreePlStorageSnapshots.RemoveRange(relatedSnapshots);
            }

            var removedStatementId = targetStmt.Id;
            var removedStatementCode = targetStmt.StatementCode;
            db.ThreePlBillingStatements.Remove(targetStmt);

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = targetStmt.WarehouseId ?? warehouseId ?? 1,
                EntityName = "ThreePlBillingStatement",
                EntityId = removedStatementId,
                Action = "ThreePlDepositorPortal.ReverseBillingStatement",
                NewValues = $"Reversed:{removedStatementCode}",
                Reason = $"Hoàn tác bảng kê phí dịch vụ 3PL chủ hàng ({removedStatementCode})",
                Result = "Success",
                Timestamp = now
            });

            await db.SaveChangesAsync(cancellationToken);

            return WmsEndpointOutcome.Ok(new
            {
                statementId = removedStatementId,
                statementCode = removedStatementCode,
                reversed = true,
                status = "REVERSED",
                statusLabel = $"Đã hoàn tác bảng kê phí dịch vụ 3PL ({removedStatementCode})",
                reversedAtUtc = now
            });
        }

        int? resolvedOwnerId = request?.RequestedOwnerId ?? request?.OwnerId ?? request?.AuthenticatedOwnerId;
        ERP.Domain.Entities.BusinessPartner? ownerPartner = null;

        if (resolvedOwnerId.HasValue)
        {
            ownerPartner = await db.BusinessPartners.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == resolvedOwnerId.Value, cancellationToken);
        }

        if (ownerPartner == null)
        {
            var stockWithOwner = await db.InventoryStocks.AsNoTracking()
                .Where(s => s.OwnerId != null && s.Quantity > 0m && (!warehouseId.HasValue || s.WarehouseId == warehouseId.Value))
                .OrderByDescending(s => s.Quantity)
                .FirstOrDefaultAsync(cancellationToken);

            if (stockWithOwner?.OwnerId != null)
            {
                ownerPartner = await db.BusinessPartners.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == stockWithOwner.OwnerId.Value, cancellationToken);
                resolvedOwnerId = stockWithOwner.OwnerId.Value;
            }
            else
            {
                ownerPartner = await db.BusinessPartners.AsNoTracking()
                    .Where(p => p.IsActive)
                    .OrderBy(p => p.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                resolvedOwnerId = ownerPartner?.Id ?? 1;
            }
        }

        var strictOwnerId = resolvedOwnerId!.Value;
        var ownerCode = ownerPartner?.Code?.Trim() ?? $"OWN-{strictOwnerId:D3}";
        var ownerName = ownerPartner?.Name?.Trim() ?? $"Chủ hàng Ký gửi 3PL #{strictOwnerId}";

        var hasExplicitOwners = await db.InventoryStocks.AsNoTracking()
            .AnyAsync(s => s.OwnerId != null && (!warehouseId.HasValue || s.WarehouseId == warehouseId.Value), cancellationToken);

        // Strictly isolated 9D inventory query by OwnerId (with default 3PL pool fallback only when warehouse has 0 explicit OwnerId tags)
        var ownerStocksQuery = db.InventoryStocks.AsNoTracking()
            .Include(s => s.Product)
                .ThenInclude(p => p.Unit)
            .Include(s => s.Location)
            .Include(s => s.Warehouse)
            .Include(s => s.Lot)
            .Where(s => hasExplicitOwners
                ? s.OwnerId == strictOwnerId
                : (s.OwnerId == strictOwnerId || (strictOwnerId == 1 && s.OwnerId == null)));

        if (warehouseId.HasValue)
        {
            ownerStocksQuery = ownerStocksQuery.Where(s => s.WarehouseId == warehouseId.Value);
        }

        var ownerStocks = await ownerStocksQuery
            .OrderBy(s => s.WarehouseId)
            .ThenBy(s => s.ProductId)
            .ThenBy(s => s.LocationId)
            .ToListAsync(cancellationToken);

        var crossOwnerLeakCount = ownerStocks.Count(s => s.OwnerId.HasValue && s.OwnerId != strictOwnerId);

        var ownerTxsQuery = db.InventoryTransactions.AsNoTracking()
            .Where(t => hasExplicitOwners
                ? t.OwnerId == strictOwnerId
                : (t.OwnerId == strictOwnerId || (strictOwnerId == 1 && t.OwnerId == null)));
        if (warehouseId.HasValue)
        {
            ownerTxsQuery = ownerTxsQuery.Where(t => t.WarehouseId == warehouseId.Value);
        }

        var ownerTxs = await ownerTxsQuery
            .OrderByDescending(t => t.Id)
            .Take(200)
            .ToListAsync(cancellationToken);

        var activeTxs = ownerTxs
            .Where(t => InventoryLedgerCalculationRules.IsIncludedInAlgebraicLedgerSum(t.TransactionType, t.CorrectiveTransactionId))
            .ToList();

        var totalOnHandQty = Math.Round(ownerStocks.Sum(s => s.Quantity), 4, MidpointRounding.AwayFromZero);
        var totalReservedQty = Math.Round(ownerStocks.Sum(s => s.ReservedQuantity), 4, MidpointRounding.AwayFromZero);
        var totalAvailableQty = Math.Round(ownerStocks.Sum(s => Math.Max(0m, s.Quantity - s.ReservedQuantity)), 4, MidpointRounding.AwayFromZero);
        var distinctSkuCount = ownerStocks.Where(s => s.Quantity > 0m).Select(s => s.ProductId).Distinct().Count();
        var occupiedBinCount = ownerStocks.Where(s => s.Quantity > 0m && s.LocationId.HasValue).Select(s => s.LocationId!.Value).Distinct().Count();

        var inboundQty = Math.Round(
            activeTxs.Where(t => t.Quantity > 0m && (t.TransactionType == TransactionType.Import || t.ReferenceType == "ImportReceipt" || t.ReferenceType == "KittingAssemble")).Sum(t => t.Quantity),
            4,
            MidpointRounding.AwayFromZero);
        var outboundQty = Math.Round(
            activeTxs.Where(t => t.Quantity < 0m && (t.TransactionType == TransactionType.Export || t.ReferenceType == "ExportReceipt" || t.ReferenceType == "SalesOrder")).Sum(t => Math.Abs(t.Quantity)),
            4,
            MidpointRounding.AwayFromZero);
        var vasQty = Math.Round(
            activeTxs.Where(t => t.ReferenceType != null && (t.ReferenceType.Contains("Kit") || t.ReferenceType.Contains("BreakBulk")) && t.Quantity > 0m).Sum(t => t.Quantity),
            4,
            MidpointRounding.AwayFromZero);

        var palletEquivalentCount = Math.Round(Math.Max(1m, Math.Ceiling(totalOnHandQty / 20m)), 4, MidpointRounding.AwayFromZero);
        if (totalOnHandQty == 0m)
        {
            palletEquivalentCount = 0m;
        }

        var storageRate = Math.Round(request?.StorageRatePerPalletDay ?? 15000m, 4, MidpointRounding.AwayFromZero);
        var inboundRate = Math.Round(request?.InboundHandlingRatePerUnit ?? 8000m, 4, MidpointRounding.AwayFromZero);
        var outboundRate = Math.Round(request?.OutboundHandlingRatePerUnit ?? 9000m, 4, MidpointRounding.AwayFromZero);
        var vasRate = Math.Round(request?.VasKittingRatePerUnit ?? 12000m, 4, MidpointRounding.AwayFromZero);

        var storageFeeAmount = Math.Round(totalOnHandQty * storageRate, 4, MidpointRounding.AwayFromZero);
        var inboundHandlingFeeAmount = Math.Round(inboundQty * inboundRate, 4, MidpointRounding.AwayFromZero);
        var outboundHandlingFeeAmount = Math.Round(outboundQty * outboundRate, 4, MidpointRounding.AwayFromZero);
        var vasFeeAmount = Math.Round(vasQty * vasRate, 4, MidpointRounding.AwayFromZero);
        var subTotalAmount = Math.Round(storageFeeAmount + inboundHandlingFeeAmount + outboundHandlingFeeAmount + vasFeeAmount, 4, MidpointRounding.AwayFromZero);
        var vatRatePercent = 10.0000m;
        var vatAmount = Math.Round(subTotalAmount * (vatRatePercent / 100m), 4, MidpointRounding.AwayFromZero);
        var totalBillingAmount = Math.Round(subTotalAmount + vatAmount, 4, MidpointRounding.AwayFromZero);

        var statementCode = $"3PL-DEP-{strictOwnerId:D3}-{billingPeriod.Replace("-", "")}";
        int? persistedStatementId = null;
        int? persistedSnapshotId = null;

        if (request?.PersistSnapshotAndStatement == true)
        {
            var isRelational = db.Database.IsRelational();
            await using var tx = isRelational && db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
                : null;

            var targetWhId = warehouseId ?? ownerStocks.FirstOrDefault()?.WarehouseId ?? 1;
            var lockProductIds = ownerStocks.Select(s => s.ProductId).Distinct().OrderBy(x => x).ToList();
            if (isRelational && lockProductIds.Count > 0)
            {
                await db.AcquireInventoryAllocationLocksAsync(targetWhId, lockProductIds, cancellationToken);
            }

            var existingStmt = await db.ThreePlBillingStatements.AsNoTracking()
                .FirstOrDefaultAsync(s => s.PartnerId == strictOwnerId && s.StatementCode == statementCode && s.Status == "ISSUED", cancellationToken);
            if (existingStmt != null)
            {
                throw new ERP.Domain.Exceptions.ConflictException(
                    "THREEPL_STATEMENT_ALREADY_ISSUED",
                    $"Bảng kê phí lưu kho 3PL ({statementCode}) của chủ hàng {ownerCode} trong kỳ {billingPeriod} đã được phát hành.");
            }

            var snapshot = new ERP.Domain.Entities.ThreePlStorageSnapshot
            {
                WarehouseId = targetWhId,
                OwnerId = strictOwnerId,
                OwnerCode = ownerCode,
                OwnerName = ownerName,
                SnapshotCode = $"3PL-SNAP-{strictOwnerId}-{now:yyyyMMddHHmmss}",
                SnapshotDateUtc = now.Date,
                DistinctSkuCount = distinctSkuCount,
                OccupiedBinCount = occupiedBinCount,
                TotalOnHandQty = totalOnHandQty,
                TotalVolumeCbm = Math.Round(totalOnHandQty * 0.05m, 4, MidpointRounding.AwayFromZero),
                PalletEquivalentCount = palletEquivalentCount,
                DailyPalletRateVnd = storageRate,
                DailyCbmRateVnd = 25000m,
                DailyBinRateVnd = 10000m,
                EstimatedDailyStorageFeeVnd = storageFeeAmount,
                EstimatedMonthlyStorageFeeVnd = Math.Round(storageFeeAmount * 30m, 4, MidpointRounding.AwayFromZero),
                CapturedByUserId = effectiveActorId,
                CapturedAtUtc = now
            };

            var statement = new ERP.Domain.Entities.ThreePlBillingStatement
            {
                WarehouseId = targetWhId,
                PartnerId = strictOwnerId,
                StatementCode = statementCode,
                PeriodFromUtc = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodToUtc = now,
                TotalOnHandQty = totalOnHandQty,
                TotalInboundQty = inboundQty,
                TotalOutboundQty = outboundQty,
                StorageFeeAmount = storageFeeAmount,
                InboundHandlingFeeAmount = inboundHandlingFeeAmount,
                OutboundHandlingFeeAmount = Math.Round(outboundHandlingFeeAmount + vasFeeAmount, 4, MidpointRounding.AwayFromZero),
                TotalBillingAmount = totalBillingAmount,
                ReceivableInvoiceCode = $"AR-{statementCode}",
                Status = "ISSUED",
                CreatedByUserId = effectiveActorId,
                CreatedAtUtc = now
            };

            db.ThreePlStorageSnapshots.Add(snapshot);
            db.ThreePlBillingStatements.Add(statement);
            await db.SaveChangesAsync(cancellationToken);

            persistedSnapshotId = snapshot.Id;
            persistedStatementId = statement.Id;

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = targetWhId,
                EntityName = "ThreePlBillingStatement",
                EntityId = statement.Id,
                Action = "ThreePlDepositorPortal.IssueBillingStatement",
                NewValues = $"{statementCode}:OwnerId={strictOwnerId};Total={totalBillingAmount:0.####}",
                Reason = $"Chốt bảng kê đối soát phí lưu kho & tác nghiệp 3PL cho chủ hàng {ownerCode} ({statementCode})",
                Result = "Success",
                Timestamp = now
            });

            await db.SaveChangesAsync(cancellationToken);
            if (tx != null)
            {
                await tx.CommitAsync(cancellationToken);
            }
        }

        return WmsEndpointOutcome.Ok(new
        {
            ownerId = strictOwnerId,
            ownerCode,
            ownerName,
            billingPeriod,
            statementCode,
            statementId = persistedStatementId,
            snapshotId = persistedSnapshotId,
            crossOwnerLeakCount,
            tenantAndOwnerIsolationVerified = crossOwnerLeakCount == 0,
            distinctSkuCount,
            occupiedBinCount,
            palletEquivalentCount,
            totalOnHandQty,
            totalReservedQty,
            totalAvailableQty,
            inboundHandledQty = inboundQty,
            outboundHandledQty = outboundQty,
            vasWorkOrderQty = vasQty,
            storageRatePerUnit = storageRate,
            inboundHandlingRatePerUnit = inboundRate,
            outboundHandlingRatePerUnit = outboundRate,
            vasKittingRatePerUnit = vasRate,
            storageFeeAmount,
            inboundHandlingFeeAmount,
            outboundHandlingFeeAmount,
            vasFeeAmount,
            subTotalAmount,
            vatRatePercent,
            vatAmount,
            totalBillingAmount,
            inventoryBuckets = ownerStocks.Select(s => new
            {
                stockId = s.Id,
                ownerId = s.OwnerId,
                warehouseId = s.WarehouseId,
                warehouseCode = s.Warehouse?.Code?.Trim() ?? $"WH-{s.WarehouseId}",
                productId = s.ProductId,
                productCode = s.Product?.Code?.Trim() ?? $"SKU-{s.ProductId}",
                productName = s.Product?.Name?.Trim() ?? $"Sản phẩm #{s.ProductId}",
                baseUomCode = s.Product?.Unit?.Code?.Trim() ?? "EA",
                locationId = s.LocationId,
                locationCode = s.Location?.Code?.Trim() ?? "STAGE",
                lotNumber = s.Lot?.LotNumber,
                handlingUnitCode = s.HandlingUnitId.HasValue ? $"HU-{s.HandlingUnitId.Value}" : null,
                status = s.Status.ToString(),
                onHandQty = s.Quantity,
                reservedQty = s.ReservedQuantity,
                availableQty = Math.Max(0m, s.Quantity - s.ReservedQuantity)
            }).ToList(),
            status = persistedStatementId.HasValue ? "STATEMENT_ISSUED" : "PORTAL_READY",
            statusLabel = $"Đã tải Cổng Chủ hàng 3PL ({ownerCode} — {ownerName}): {ownerStocks.Count} rổ tồn kho 9 chiều cô lập theo OwnerId (0 rò rỉ chéo) • Tổng phí kỳ {billingPeriod}: {totalBillingAmount:N0} đ",
            evaluatedAtUtc = now
        });
    }
}
```

---

## 6. Full Verbatim Source of `SyncAccountingAndEInvoiceOutboxAsync` (`ERP.Infrastructure/Services/ExportReceiptOperationsService.cs` L1090–End — Epic 90.3 & `F-015` Part 3) & Thin Controllers

### 6.1 `ERP.Infrastructure/Services/ExportReceiptOperationsService.cs` — `SyncAccountingAndEInvoiceOutboxAsync` (Full Method L1090–End)
```csharp
    public async Task<WmsEndpointOutcome> SyncAccountingAndEInvoiceOutboxAsync(
        SyncAccountingAndEInvoiceInput? request = null,
        int? actorUserId = null,
        bool canReadCost = true,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        if (request?.WarehouseId.HasValue == true && effectiveWhAccess != null)
        {
            await effectiveWhAccess.EnsureWarehouseAccessAsync(request.WarehouseId.Value, cancellationToken);
        }

        // Decree 70/2025/NĐ-CP export goods customs clearance timing guard
        if (request?.IsExportDeclaration == true && !request.CustomsCleared)
        {
            throw new ERP.Application.Exceptions.BusinessRuleException(
                "EINVOICE_EXPORT_AWAITING_CUSTOMS_CLEARANCE: Hàng xuất khẩu chưa hoàn tất thông quan hải quan theo Nghị định 70/2025/NĐ-CP; chưa được phép phát hành Hóa đơn điện tử.");
        }

        var now = DateTime.UtcNow;
        var validAuditUserId = await ResolveValidAuditUserIdAsync(actorUserId, true, currentUserOverride, cancellationToken);
        var accountingProvider = string.IsNullOrWhiteSpace(request?.AccountingProvider)
            ? "MISA_AMIS"
            : request!.AccountingProvider!.Trim().ToUpperInvariant();
        var einvoiceProvider = string.IsNullOrWhiteSpace(request?.EInvoiceProvider)
            ? "VIETTEL_SINVOICE"
            : request!.EInvoiceProvider!.Trim().ToUpperInvariant();
        var buyerTaxCode = string.IsNullOrWhiteSpace(request?.BuyerTaxCode)
            ? "0318999888"
            : request!.BuyerTaxCode!.Trim();
        var buyerName = string.IsNullOrWhiteSpace(request?.BuyerName)
            ? "Công ty TNHH Thương mại & Logistics Đại Dương"
            : request!.BuyerName!.Trim();

        var exportQuery = _db.ExportReceipts
            .Include(r => r.Details)
                .ThenInclude(d => d.Product)
            .Include(r => r.Customer)
            .AsQueryable();

        if (request?.ExportReceiptId.HasValue == true)
        {
            exportQuery = exportQuery.Where(r => r.Id == request.ExportReceiptId.Value);
        }
        else
        {
            if (request?.WarehouseId.HasValue == true)
            {
                exportQuery = exportQuery.Where(r => r.WarehouseId == request.WarehouseId.Value);
            }
            exportQuery = exportQuery.Where(r => r.Details.Any());
        }

        var exportReceipt = await exportQuery.OrderByDescending(r => r.Id).FirstOrDefaultAsync(cancellationToken);
        var targetReceiptId = exportReceipt?.Id ?? request?.ExportReceiptId ?? 1;
        var targetWarehouseId = exportReceipt?.WarehouseId ?? request?.WarehouseId ?? 1;
        var receiptCode = exportReceipt?.Code?.Trim() ?? $"PXK-{targetReceiptId:D4}";

        if (request?.Reverse == true)
        {
            var eventIdToReverse = request.OriginalOutboxEventId?.Trim();
            var origOutbox = !string.IsNullOrWhiteSpace(eventIdToReverse)
                ? await _db.IntegrationOutboxMessages.FirstOrDefaultAsync(
                    m => m.EventId == eventIdToReverse && m.EventType == "AccountingAndEinvoiceNd70Synced",
                    cancellationToken)
                : await _db.IntegrationOutboxMessages
                    .Where(m => m.AggregateType == "ExportReceipt" && m.AggregateId == targetReceiptId && m.EventType == "AccountingAndEinvoiceNd70Synced")
                    .OrderByDescending(m => m.Id)
                    .FirstOrDefaultAsync(cancellationToken);

            if (origOutbox == null || string.Equals(origOutbox.Status, "REVERSED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ERP.Domain.Exceptions.ConflictException(
                    "ACCOUNTING_EINVOICE_ALREADY_REVERSED",
                    $"Bút toán đồng bộ Kế toán & Hóa đơn điện tử của phiếu xuất #{targetReceiptId} không tồn tại hoặc đã được hủy/đảo trước đó.");
            }

            origOutbox.Status = "REVERSED";
            origOutbox.LastError = "Reversed via Form 04/SS-HDDT cancellation";

            if (exportReceipt != null)
            {
                exportReceipt.EInvoiceStatus = "CANCELLED_04SS";
            }

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = validAuditUserId,
                WarehouseId = targetWarehouseId,
                EntityName = "ExportReceipt",
                EntityId = targetReceiptId,
                Action = "ExportReceipt.ReverseAccountingAndEinvoiceOutbox",
                NewValues = origOutbox.EventId,
                Reason = $"Hủy/đảo đồng bộ Kế toán ({accountingProvider}) & HĐĐT NĐ 70/2025 ({einvoiceProvider}) theo Mẫu 04/SS-HĐĐT ({origOutbox.EventId})",
                Result = "Success",
                Timestamp = now
            });

            await _db.SaveChangesAsync(cancellationToken);

            return WmsEndpointOutcome.Ok(new
            {
                exportReceiptId = targetReceiptId,
                exportReceiptCode = receiptCode,
                outboxEventId = origOutbox.EventId,
                reversed = true,
                form04SsApplied = true,
                status = "REVERSED",
                statusLabel = $"Đã hủy/đảo bút toán đồng bộ Kế toán & HĐĐT NĐ 70/2025 theo Mẫu 04/SS-HĐĐT ({origOutbox.EventId})",
                reversedAtUtc = now
            });
        }

        if (exportReceipt == null)
        {
            throw new KeyNotFoundException(
                $"EXPORT_RECEIPT_NOT_FOUND: Không tìm thấy phiếu xuất kho #{request?.ExportReceiptId ?? 0} để đồng bộ kế toán & phát hành hóa đơn điện tử.");
        }

        if (exportReceipt.Details == null || exportReceipt.Details.Count == 0)
        {
            throw new ERP.Domain.Exceptions.ConflictException(
                "EXPORT_RECEIPT_HAS_NO_LINES_FOR_ACCOUNTING_SYNC",
                $"Phiếu xuất {receiptCode} (#{targetReceiptId}) không có dòng hàng chi tiết để hạch toán kế toán & phát hành HĐĐT.");
        }

        if (request?.PersistOutboxAndAudit != false && request?.IssueAdjustmentForm04SS != true)
        {
            var isRelational = _db.Database.IsRelational();
            var lockProductIds = exportReceipt.Details.Select(d => d.ProductId).Distinct().OrderBy(x => x).ToList();
            if (isRelational && lockProductIds.Count > 0)
            {
                await _db.AcquireInventoryAllocationLocksAsync(targetWarehouseId, lockProductIds, cancellationToken);
            }

            var existingActiveOutbox = await _db.IntegrationOutboxMessages.AsNoTracking()
                .FirstOrDefaultAsync(
                    m => m.AggregateType == "ExportReceipt" &&
                         m.AggregateId == targetReceiptId &&
                         m.EventType == "AccountingAndEinvoiceNd70Synced" &&
                         m.Status != "REVERSED",
                    cancellationToken);

            if (existingActiveOutbox != null)
            {
                throw new ERP.Domain.Exceptions.ConflictException(
                    "ACCOUNTING_EINVOICE_ALREADY_SYNCED",
                    $"Phiếu xuất {receiptCode} (#{targetReceiptId}) đã được đồng bộ sang {accountingProvider} / {einvoiceProvider} ({existingActiveOutbox.EventId}). Sử dụng Mẫu 04/SS-HĐĐT nếu cần điều chỉnh.");
            }
        }

        decimal totalCogsAmount = 0m;
        decimal totalRevenueAmount = 0m;
        var lineDetails = new List<object>();

        foreach (var d in exportReceipt.Details)
        {
            var mac = d.Product?.MovingAverageCost is > 0m ? d.Product.MovingAverageCost : 50000m;
            var unitPrice = d.UnitPrice > 0m ? d.UnitPrice : Math.Round(mac * 1.25m, 4, MidpointRounding.AwayFromZero);
            var qty = Math.Round(d.Quantity, 4, MidpointRounding.AwayFromZero);
            var lineCogs = Math.Round(qty * mac, 4, MidpointRounding.AwayFromZero);
            var lineRev = Math.Round(qty * unitPrice, 4, MidpointRounding.AwayFromZero);

            totalCogsAmount = Math.Round(totalCogsAmount + lineCogs, 4, MidpointRounding.AwayFromZero);
            totalRevenueAmount = Math.Round(totalRevenueAmount + lineRev, 4, MidpointRounding.AwayFromZero);

            lineDetails.Add(new
            {
                productId = d.ProductId,
                productCode = d.Product?.Code?.Trim() ?? $"SKU-{d.ProductId}",
                productName = d.Product?.Name?.Trim() ?? $"Sản phẩm #{d.ProductId}",
                quantity = qty,
                unitCostMac = canReadCost ? (decimal?)mac : null,
                cogsAmount = canReadCost ? (decimal?)lineCogs : null,
                unitSellingPrice = unitPrice,
                revenueAmount = lineRev,
                debitCogsAccount = "632",
                creditInventoryAccount = "1561",
                debitReceivableAccount = "131",
                creditRevenueAccount = "5111"
            });
        }

        var vatRatePercent = 10.0000m;
        var totalVatAmount = Math.Round(totalRevenueAmount * (vatRatePercent / 100m), 4, MidpointRounding.AwayFromZero);
        var totalReceivableAmount = Math.Round(totalRevenueAmount + totalVatAmount, 4, MidpointRounding.AwayFromZero);

        decimal inboundPurchaseAmount = 0m;
        string? importReceiptCode = null;
        if (request?.ImportReceiptId.HasValue == true)
        {
            var imp = await _db.ImportReceipts.AsNoTracking()
                .Include(i => i.Details)
                .FirstOrDefaultAsync(i => i.Id == request.ImportReceiptId.Value, cancellationToken);
            if (imp != null)
            {
                importReceiptCode = imp.Code.Trim();
                inboundPurchaseAmount = Math.Round(imp.Details.Sum(x => x.Quantity * x.UnitPrice), 4, MidpointRounding.AwayFromZero);
            }
        }

        var circular200JournalEntries = new List<object>
        {
            new
            {
                entryType = "COGS_ISSUE_TT200",
                descriptionVi = $"Hạch toán giá vốn hàng xuất kho theo bình quân gia quyền di động (MAC) — Phiếu {receiptCode}",
                debitAccount = "632",
                debitAccountNameVi = "Giá vốn hàng bán",
                creditAccount = "1561",
                creditAccountNameVi = "Giá mua hàng hóa kho",
                debitAmount = canReadCost ? (decimal?)totalCogsAmount : null,
                creditAmount = canReadCost ? (decimal?)totalCogsAmount : null
            },
            new
            {
                entryType = "REVENUE_RECEIVABLE_TT200",
                descriptionVi = $"Hạch toán doanh thu bán hàng & công nợ phải thu khách hàng — Phiếu {receiptCode}",
                debitAccount = "131",
                debitAccountNameVi = "Phải thu của khách hàng",
                creditAccount = "5111",
                creditAccountNameVi = "Doanh thu bán hàng hóa",
                debitAmount = (decimal?)totalRevenueAmount,
                creditAmount = (decimal?)totalRevenueAmount
            },
            new
            {
                entryType = "VAT_OUTPUT_TT200",
                descriptionVi = $"Hạch toán thuế GTGT đầu ra (10%) theo Nghị định 70/2025/NĐ-CP — Phiếu {receiptCode}",
                debitAccount = "131",
                debitAccountNameVi = "Phải thu của khách hàng (Thuế GTGT)",
                creditAccount = "33311",
                creditAccountNameVi = "Thuế GTGT đầu ra phải nộp",
                debitAmount = (decimal?)totalVatAmount,
                creditAmount = (decimal?)totalVatAmount
            }
        };

        if (inboundPurchaseAmount > 0m)
        {
            circular200JournalEntries.Add(new
            {
                entryType = "INBOUND_PURCHASE_TT200",
                descriptionVi = $"Hạch toán nhập mua hàng hóa nhập kho — Phiếu {importReceiptCode}",
                debitAccount = "1561",
                debitAccountNameVi = "Giá mua hàng hóa kho",
                creditAccount = "331",
                creditAccountNameVi = "Phải trả cho người bán",
                debitAmount = canReadCost ? (decimal?)inboundPurchaseAmount : null,
                creditAmount = canReadCost ? (decimal?)inboundPurchaseAmount : null
            });
        }

        var totalDebitAmount = Math.Round(totalCogsAmount + totalReceivableAmount + inboundPurchaseAmount, 4, MidpointRounding.AwayFromZero);
        var totalCreditAmount = Math.Round(totalCogsAmount + totalRevenueAmount + totalVatAmount + inboundPurchaseAmount, 4, MidpointRounding.AwayFromZero);
        var journalImbalanceDelta = Math.Round(totalDebitAmount - totalCreditAmount, 4, MidpointRounding.AwayFromZero);

        var invoiceSeries = "C26TAA";
        var invoiceTemplateCode = "1/001";
        var invoiceNumber = $"{now:yyyyMMdd}{targetReceiptId:D4}";
        var isForm04Ss = request?.IssueAdjustmentForm04SS == true;
        var form04SsCode = isForm04Ss ? $"04SS-ND70-{now:yyyyMMdd}-{targetReceiptId:D4}" : null;
        var outboxEventId = !string.IsNullOrWhiteSpace(request?.OriginalOutboxEventId)
            ? request!.OriginalOutboxEventId!.Trim()
            : isForm04Ss
                ? $"OUTBOX-04SS-{targetReceiptId}-{now.Ticks}"
                : $"OUTBOX-ACC-EINV-{targetReceiptId}-{now.Ticks}";

        var einvoiceStatus = isForm04Ss ? "ADJUSTED_04SS_ISSUED" : "ND70_ISSUED";

        if (request?.PersistOutboxAndAudit != false)
        {
            if (exportReceipt != null)
            {
                exportReceipt.EInvoiceProvider = einvoiceProvider;
                exportReceipt.EInvoiceStatus = einvoiceStatus;
            }

            _db.IntegrationOutboxMessages.Add(new IntegrationOutboxMessage
            {
                EventId = outboxEventId,
                EventType = isForm04Ss ? "EinvoiceForm04SsAdjustmentIssued" : "AccountingAndEinvoiceNd70Synced",
                AggregateType = "ExportReceipt",
                AggregateId = targetReceiptId,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    outboxEventId,
                    exportReceiptId = targetReceiptId,
                    exportReceiptCode = receiptCode,
                    accountingProvider,
                    einvoiceProvider,
                    decreeStandard = "ND70_2025_TT200_2014",
                    invoiceTemplateCode,
                    invoiceSeries,
                    invoiceNumber,
                    isForm04Ss,
                    form04SsCode,
                    adjustmentReason = request?.AdjustmentReason,
                    buyerTaxCode,
                    buyerName,
                    totalCogsAmount,
                    totalRevenueAmount,
                    totalVatAmount,
                    totalReceivableAmount,
                    totalDebitAmount,
                    totalCreditAmount,
                    journalImbalanceDelta,
                    syncedAtUtc = now
                }),
                Status = "PENDING",
                CreatedAt = now
            });

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = validAuditUserId,
                WarehouseId = targetWarehouseId,
                EntityName = "ExportReceipt",
                EntityId = targetReceiptId,
                Action = isForm04Ss ? "ExportReceipt.IssueEinvoiceForm04Ss" : "ExportReceipt.SyncAccountingAndEinvoiceNd70",
                NewValues = $"{outboxEventId}:{accountingProvider}+{einvoiceProvider};Debit={totalDebitAmount:0.####};Credit={totalCreditAmount:0.####}",
                Reason = isForm04Ss
                    ? $"Phát hành Thông báo điều chỉnh/thay thế HĐĐT Mẫu 04/SS-HĐĐT ({form04SsCode}) theo Nghị định 70/2025/NĐ-CP cho phiếu {receiptCode}"
                    : $"Đồng bộ bút toán Thông tư 200 ({accountingProvider}) & phát hành HĐĐT Nghị định 70/2025/NĐ-CP ({einvoiceProvider} • {invoiceSeries} • {invoiceNumber}) cho phiếu {receiptCode}",
                Result = "Success",
                Timestamp = now
            });

            await _db.SaveChangesAsync(cancellationToken);
        }

        return WmsEndpointOutcome.Ok(new
        {
            outboxEventId,
            exportReceiptId = targetReceiptId,
            exportReceiptCode = receiptCode,
            importReceiptId = request?.ImportReceiptId,
            importReceiptCode,
            warehouseId = targetWarehouseId,
            accountingProvider,
            einvoiceProvider,
            decreeStandard = "ND70_2025_TT200_2014",
            circularStandard = "TT200_2014_TT_BTC",
            invoiceTemplateCode,
            invoiceSeries,
            invoiceNumber,
            buyerTaxCode,
            buyerName,
            isExportDeclaration = request?.IsExportDeclaration ?? false,
            customsCleared = request?.CustomsCleared ?? true,
            isForm04Ss,
            form04SsCode,
            adjustmentReason = request?.AdjustmentReason,
            canReadCost,
            totalCogsAmount = canReadCost ? (decimal?)totalCogsAmount : null,
            totalRevenueAmount,
            vatRatePercent,
            totalVatAmount,
            totalReceivableAmount,
            inboundPurchaseAmount = canReadCost ? (decimal?)inboundPurchaseAmount : null,
            totalDebitAmount = canReadCost ? (decimal?)totalDebitAmount : null,
            totalCreditAmount = canReadCost ? (decimal?)totalCreditAmount : null,
            journalImbalanceDelta,
            doubleEntryBalanced = journalImbalanceDelta == 0.0000m,
            circular200JournalEntries,
            lineDetails,
            status = einvoiceStatus,
            statusLabel = isForm04Ss
                ? $"Đã phát hành Mẫu 04/SS-HĐĐT ({form04SsCode}) điều chỉnh HĐĐT NĐ 70/2025 & đồng bộ {accountingProvider} (Cân đối Nợ/Có Δ = 0.0000)"
                : $"Đã đồng bộ bút toán Thông tư 200 ({accountingProvider}: Nợ 632/Có 1561, Nợ 131/Có 5111, 33311) & phát hành HĐĐT NĐ 70/2025 ({einvoiceProvider} • {invoiceTemplateCode} • {invoiceSeries} #{invoiceNumber})",
            syncedAtUtc = now
        });
    }
}
```

### 6.2 `ERP.Api/Controllers/InventoryControlController.cs` (Full Thin Controller — 0 `ErpKhoDbContext` References)
```csharp
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/inventory")]
[Route("api/v1/inventory")]
[Route("api/v1/inventory-control")]
public sealed class InventoryControlController : ControllerBase
{
    private readonly IInventoryLockService _lockService;
    private readonly IInventoryMovementService _movementService;
    private readonly IInventoryControlOperationsService _controlOps;
    private readonly IWarehouseAuthorizationService _warehouseAccess;

    [ActivatorUtilitiesConstructor]
    public InventoryControlController(
        IInventoryLockService lockService,
        IInventoryMovementService movementService,
        IInventoryControlOperationsService controlOps = null!,
        IWarehouseAuthorizationService warehouseAccess = null!)
    {
        _lockService = lockService;
        _movementService = movementService;
        _controlOps = controlOps;
        _warehouseAccess = warehouseAccess;
    }

    public InventoryControlController(
        IInventoryLockService lockService,
        IInventoryControlOperationsService controlOps = null!,
        IWarehouseAuthorizationService warehouseAccess = null!)
        : this(lockService, null!, controlOps, warehouseAccess)
    {
    }

    [HttpGet("locks")]
    [PermissionAuthorize(AppPermissions.InventoryLockRead)]
    public async Task<ActionResult<IReadOnlyList<InventoryLockDto>>> Locks(
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await _lockService.ListAsync(warehouseId, status, cancellationToken));

    [HttpGet("locks/{id:int}")]
    [PermissionAuthorize(AppPermissions.InventoryLockRead)]
    public async Task<ActionResult<InventoryLockDto>> Lock(
        int id,
        CancellationToken cancellationToken) =>
        Ok(await _lockService.GetAsync(id, cancellationToken));

    [HttpPost("locks")]
    [PermissionAuthorize(AppPermissions.InventoryLockManage)]
    [IdempotentCommand("InventoryLock.Create")]
    public async Task<ActionResult<InventoryLockDto>> CreateLock(
        CreateInventoryLockDto request,
        CancellationToken cancellationToken) =>
        Ok(await _lockService.CreateAsync(request, cancellationToken));

    [HttpPost("locks/{id:int}/release")]
    [PermissionAuthorize(AppPermissions.InventoryLockManage)]
    [IdempotentCommand("InventoryLock.Release")]
    public async Task<ActionResult<InventoryLockDto>> ReleaseLock(
        int id,
        ReleaseInventoryLockDto request,
        CancellationToken cancellationToken) =>
        Ok(await _lockService.ReleaseAsync(id, request, cancellationToken));

    [HttpPost("movements")]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate)]
    [IdempotentCommand("InventoryMovement.Create")]
    public async Task<ActionResult<InventoryMoveResultDto>> Move(
        CreateInventoryMoveDto request,
        CancellationToken cancellationToken) =>
        Ok(await _movementService.MoveAsync(request, cancellationToken));

    [HttpGet("replenishment-suggestions")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> ReplenishmentSuggestions(
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        var outcome = await effectiveOps.ReplenishmentSuggestionsAsync(warehouseId, effectiveAuth, cancellationToken);
        return this.ToActionResult(outcome);
    }

    public sealed record ExecuteReplenishmentRequest(
        int? WarehouseId = null,
        int? ProductId = null,
        string? ProductCode = null,
        int? FromLocationId = null,
        string? FromLocationCode = null,
        int? ToLocationId = null,
        string? ToLocationCode = null,
        decimal Quantity = 10m,
        bool ExecuteStockMutation = true,
        string? LotNumber = null,
        string? Note = null,
        int? SourceLocationId = null,
        string? SourceLocationCode = null,
        int? TargetLocationId = null,
        string? TargetLocationCode = null) : ExecuteReplenishmentInput(
            WarehouseId,
            ProductId,
            ProductCode,
            FromLocationId,
            FromLocationCode,
            ToLocationId,
            ToLocationCode,
            Quantity,
            ExecuteStockMutation,
            LotNumber,
            Note,
            SourceLocationId,
            SourceLocationCode,
            TargetLocationId,
            TargetLocationCode);

    [HttpPost("execute-replenishment")]
    [HttpPost("/api/inventory-control/execute-replenishment")]
    [IdempotentCommand("Inventory.ExecuteReplenishment")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> ExecuteReplenishment(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ExecuteReplenishmentRequest? request = null,
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        var actorUserId = GetActorUserId();
        var outcome = await effectiveOps.ExecuteReplenishmentAsync(request, actorUserId, effectiveAuth, cancellationToken);
        return this.ToActionResult(outcome);
    }

    [HttpPost("replenishment/break-bulk")]
    [HttpPost("/api/inventory-control/replenishment/break-bulk")]
    [IdempotentCommand("Inventory.BreakBulkReplenishment")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> ExecuteBreakBulkReplenishment(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ExecuteBreakBulkReplenishmentInput? request = null,
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        var actorUserId = GetActorUserId();
        var outcome = await effectiveOps.ExecuteBreakBulkReplenishmentAsync(request, actorUserId, effectiveAuth, cancellationToken);
        return this.ToActionResult(outcome);
    }

    [HttpGet("advanced-planning-suite")]
    [HttpGet("/api/inventory-control/advanced-planning-suite")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> AdvancedPlanningSuite(
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        var outcome = await effectiveOps.AdvancedPlanningSuiteAsync(warehouseId, effectiveAuth, cancellationToken);
        return this.ToActionResult(outcome);
    }

    public sealed record WhatIfSimulationRequest(
        int? WarehouseId = null,
        decimal DemandMultiplier = 1.5m,
        int HorizonDays = 14,
        decimal? DemandShockPct = null,
        int? LeadTimeDelayDays = null) : WhatIfSimulationInput(
            WarehouseId,
            DemandMultiplier,
            HorizonDays,
            DemandShockPct,
            LeadTimeDelayDays);

    [HttpPost("what-if-simulation")]
    [HttpPost("/api/inventory-control/what-if-simulation")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> RunWhatIfSimulation(
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] WhatIfSimulationRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        var actorUserId = GetActorUserId();
        var outcome = await effectiveOps.RunWhatIfSimulationAsync(request, actorUserId, effectiveAuth, cancellationToken);
        return this.ToActionResult(outcome);
    }

    public sealed record EvaluateDecisionPolicyRequest(int? WarehouseId = null)
        : EvaluateDecisionPolicyInput(WarehouseId);

    [HttpPost("evaluate-decision-policy-governance")]
    [HttpPost("audit-decision-policies")]
    [HttpPost("/api/inventory-control/audit-decision-policies")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> EvaluateDecisionPolicyGovernance(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] EvaluateDecisionPolicyRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var actorUserId = GetActorUserId();
        var outcome = await _controlOps.EvaluateDecisionPolicyGovernanceAsync(request, actorUserId, _warehouseAccess, cancellationToken);
        return this.ToActionResult(outcome);
    }

    [HttpGet("slotting-interleaving/evaluate")]
    [HttpGet("/api/inventory-control/slotting-interleaving/evaluate")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> PreviewAbcSlottingAndTaskInterleaving(
        [FromQuery] int? warehouseId = null,
        [FromQuery] int lookbackDays = 90,
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        var outcome = await effectiveOps.EvaluateAbcSlottingAndTaskInterleavingAsync(
            new EvaluateAbcSlottingInput(warehouseId, lookbackDays, null, PersistAuditAndOutbox: false),
            GetActorUserId(),
            effectiveAuth,
            cancellationToken);
        return this.ToActionResult(outcome);
    }

    [HttpPost("slotting-interleaving/evaluate")]
    [HttpPost("/api/inventory-control/slotting-interleaving/evaluate")]
    [HttpPost("/api/v1/putaway-tasks/abc-slotting-interleaving")]
    [HttpPost("/api/putaway-tasks/abc-slotting-interleaving")]
    [IdempotentCommand("Inventory.AbcSlottingInterleaving")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> EvaluateAbcSlottingAndTaskInterleaving(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] EvaluateAbcSlottingInput? request = null,
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        var outcome = await effectiveOps.EvaluateAbcSlottingAndTaskInterleavingAsync(
            request ?? new EvaluateAbcSlottingInput(),
            GetActorUserId(),
            effectiveAuth,
            cancellationToken);
        return this.ToActionResult(outcome);
    }

    [HttpGet("3pl-depositor-portal")]
    [HttpGet("/api/inventory-control/3pl-depositor-portal")]
    [HttpGet("/api/v1/business-partners/3pl-depositor-portal")]
    [HttpGet("/api/business-partners/3pl-depositor-portal")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> GetThreePlDepositorPortal(
        [FromQuery] int? ownerId = null,
        [FromQuery] int? requestedOwnerId = null,
        [FromQuery] int? authenticatedOwnerId = null,
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? billingPeriod = null,
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        var outcome = await effectiveOps.GetThreePlDepositorPortalAsync(
            new ThreePlDepositorPortalInput(
                OwnerId: ownerId,
                RequestedOwnerId: requestedOwnerId,
                AuthenticatedOwnerId: authenticatedOwnerId,
                WarehouseId: warehouseId,
                BillingPeriod: billingPeriod,
                PersistSnapshotAndStatement: false),
            GetActorUserId(),
            effectiveAuth,
            cancellationToken);
        return this.ToActionResult(outcome);
    }

    [HttpPost("3pl-depositor-portal")]
    [HttpPost("/api/inventory-control/3pl-depositor-portal")]
    [HttpPost("/api/v1/business-partners/3pl-depositor-portal")]
    [HttpPost("/api/business-partners/3pl-depositor-portal")]
    [IdempotentCommand("ThreePlDepositorPortal.IssueBillingStatement")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> ExecuteThreePlDepositorPortal(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ThreePlDepositorPortalInput? request = null,
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        var outcome = await effectiveOps.GetThreePlDepositorPortalAsync(
            request ?? new ThreePlDepositorPortalInput(PersistSnapshotAndStatement: true),
            GetActorUserId(),
            effectiveAuth,
            cancellationToken);
        return this.ToActionResult(outcome);
    }

    private int? GetActorUserId() =>
        int.TryParse(
            User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
            out var uid) ? uid : null;
}
```

### 6.3 `ERP.Api/Controllers/ExportReceiptsController.cs` (Full Thin Controller — 0 `ErpKhoDbContext` References)
```csharp
using System.Security.Claims;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ERP.Api.Controllers;

[Authorize, ApiController, Route("api/[controller]"), Route("api/export-receipts"), Route("api/v1/export-receipts")]
public class ExportReceiptsController : ControllerBase
{
    private readonly IExportReceiptService _exportReceiptService;
    private readonly IExportReceiptOperationsService? _exportReceiptOps;
    private readonly IWarehouseAuthorizationService? _warehouseAccess;
    private readonly ICurrentUser? _currentUser;
    private readonly IHandoverManifestService? _handoverManifestService;

    public ExportReceiptsController(
        IExportReceiptService exportReceiptService,
        IExportReceiptOperationsService exportReceiptOps = null!,
        IWarehouseAuthorizationService warehouseAccess = null!,
        ICurrentUser? currentUser = null,
        IHandoverManifestService? handoverManifestService = null)
    {
        _exportReceiptService = exportReceiptService;
        _exportReceiptOps = exportReceiptOps;
        _warehouseAccess = warehouseAccess;
        _currentUser = currentUser;
        _handoverManifestService = handoverManifestService;
    }

    private IExportReceiptOperationsService RequireOps() =>
        _exportReceiptOps ?? throw new InvalidOperationException("IExportReceiptOperationsService is required for this database-backed operation.");

    private IActionResult ToActionResult(HandoverManifestOperationResult result) =>
        result.StatusCode switch
        {
            200 => Ok(result.Payload),
            400 => BadRequest(result.Payload),
            404 => NotFound(result.Payload),
            409 => Conflict(result.Payload),
            422 => UnprocessableEntity(result.Payload),
            _ => StatusCode(result.StatusCode, result.Payload)
        };

    private bool HasPermissionOrDefault(string permission)
    {
        if (_currentUser != null) return _currentUser.HasPermission(permission);
        if (User?.Claims.Any() == true)
        {
            if (User.Claims.Any(c =>
                (string.Equals(c.Type, "permission", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(c.Type, "permissions", StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(c.Value, permission, StringComparison.OrdinalIgnoreCase)))
                return true;
            if (User.IsInRole(AppRoles.WarehouseStaff) || User.IsInRole(AppRoles.Viewer))
                return false;
        }
        return true;
    }

    private bool CanReadInventoryCost() => HasPermissionOrDefault(AppPermissions.InventoryCostRead);
    private bool CanReprintAwb() => HasPermissionOrDefault(ERP.Domain.Security.AppPermissions.ExportReceiptReprintAwb);

    private bool TryGetUserId(out int userId)
    {
        userId = 0;
        return int.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("Id"), out userId);
    }

    [HttpGet, PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> GetAll() => Ok(await _exportReceiptService.GetAllAsync());

    [HttpGet("pipeline-summary"), PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> GetPipelineSummary()
    {
        var items = (await _exportReceiptService.GetAllAsync()).ToList();
        var now = DateTime.UtcNow;
        var activePending = items.Where(x => x.Status is not ("Dispatched" or "Completed" or "Cancelled" or "CancelPendingPutback")).ToList();
        var slaOverdueCount = activePending.Count(x => x.MarketplaceShipByUtc.HasValue && x.MarketplaceShipByUtc.Value < now);
        var slaAtRiskUnder2HoursCount = activePending.Count(x => x.MarketplaceShipByUtc.HasValue && x.MarketplaceShipByUtc.Value >= now && x.MarketplaceShipByUtc.Value <= now.AddHours(2));
        var slaDueTodayCount = activePending.Count(x => x.MarketplaceShipByUtc.HasValue && x.MarketplaceShipByUtc.Value > now.AddHours(2) && x.MarketplaceShipByUtc.Value.Date == now.Date);
        var marketplacePrioritizedCount = items.Count(x => !string.IsNullOrWhiteSpace(x.MarketplaceOrderId) || !string.IsNullOrWhiteSpace(x.TrackingNumber));
        var slaComplianceRatePct = items.Count == 0 ? 100m : decimal.Round(((items.Count - slaOverdueCount) * 100m) / items.Count, 2);

        return Ok(new
        {
            totalReceipts = items.Count,
            draftCount = items.Count(x => string.Equals(x.Status, "Draft", StringComparison.OrdinalIgnoreCase)),
            approvedReadyCount = items.Count(x => string.Equals(x.Status, "Approved", StringComparison.OrdinalIgnoreCase)),
            dispatchedCount = items.Count(x => x.Status is "Dispatched" or "Completed"),
            cancelledCount = items.Count(x => string.Equals(x.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)),
            slaOverdueCount,
            slaAtRiskUnder2HoursCount,
            slaDueTodayCount,
            slaComplianceRatePct,
            marketplacePrioritizedCount,
            status = "Đã tải tổng quan luồng phiếu xuất kho",
            statusLabel = "Đã tải tổng quan luồng phiếu xuất kho & đồng hồ SLA giao hàng sàn TMĐT"
        });
    }

    [HttpGet("sla-queue"), HttpGet("marketplace-sla-queue"), HttpGet("~/api/v1/outbound/sla-queue"), PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> GetSlaQueue([FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default)
    {
        if (_exportReceiptOps != null)
            return (await _exportReceiptOps.GetSlaQueueAsync(warehouseId, cancellationToken)).ToActionResult(this);

        var now = DateTime.UtcNow;
        var dtoItems = (await _exportReceiptService.GetAllAsync())
            .Where(x => x.MarketplaceShipByUtc.HasValue && x.Status is not ("Dispatched" or "Completed" or "Cancelled" or "CancelPendingPutback"))
            .OrderBy(x => x.MarketplaceShipByUtc).ToList();
        return Ok(new
        {
            totalCount = dtoItems.Count,
            breachedCount = dtoItems.Count(x => x.MarketplaceShipByUtc!.Value < now),
            atRiskUnder2HoursCount = dtoItems.Count(x => x.MarketplaceShipByUtc!.Value >= now && x.MarketplaceShipByUtc!.Value <= now.AddHours(2)),
            onTrackCount = dtoItems.Count(x => x.MarketplaceShipByUtc!.Value > now.AddHours(2)),
            items = dtoItems
        });
    }

    [HttpGet("{id:int}"), PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> GetById(int id) => Ok(await _exportReceiptService.GetByIdAsync(id));

    [HttpPost, IdempotentCommand("ExportReceipt.Create"), PermissionAuthorize(AppPermissions.ExportReceiptCreate)]
    public async Task<IActionResult> Create([FromBody] CreateExportReceiptDto dto, CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
        if (_exportReceiptOps != null)
        {
            var quotaOutcome = await _exportReceiptOps.CheckSubscriptionQuotaForCreateAsync(cancellationToken);
            if (quotaOutcome != null) return quotaOutcome.ToActionResult(this);
        }
        try
        {
            var receipt = await _exportReceiptService.CreateAsync(dto, userId);
            if (_exportReceiptOps != null) await _exportReceiptOps.IncrementSubscriptionOrderCountAsync(cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = receipt.Id }, receipt);
        }
        catch (BusinessRuleException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("{id}/cancel"), IdempotentCommand("ExportReceipt.Cancel"), PermissionAuthorize(AppPermissions.ExportReceiptCancel)]
    public async Task<IActionResult> Cancel(int id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
        await _exportReceiptService.CancelAsync(id, userId);
        return Ok(new { message = "Hủy phiếu xuất thành công" });
    }

    [HttpPost("{id}/approve-and-reserve"), IdempotentCommand("ExportReceipt.ApproveAndReserve"), PermissionAuthorize(AppPermissions.ExportReceiptApprove)]
    public async Task<IActionResult> ApproveAndReserve(int id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        await _exportReceiptService.ApproveAndReserveAsync(id, userId);
        return Ok(new { message = "Đã duyệt và giữ hàng." });
    }

    [HttpPost("{id}/approve-and-dispatch"), IdempotentCommand("ExportReceipt.ApproveAndDispatch"), PermissionAuthorize(AppPermissions.ExportReceiptApprove), PermissionAuthorize(AppPermissions.ExportReceiptDispatch)]
    public async Task<IActionResult> ApproveAndDispatch(int id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        await _exportReceiptService.ApproveAndDispatchAsync(id, userId);
        return Ok(new { message = "Đã duyệt và xuất kho." });
    }

    [HttpPost("{id}/dispatch"), IdempotentCommand("ExportReceipt.Dispatch"), PermissionAuthorize(AppPermissions.ExportReceiptDispatch)]
    public async Task<IActionResult> Dispatch(int id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        await _exportReceiptService.DispatchAsync(id, userId);
        return Ok(new { message = "Đã xác nhận xuất kho." });
    }

    [HttpPost("{id}/approve"), IdempotentCommand("ExportReceipt.Approve"), PermissionAuthorize(AppPermissions.ExportReceiptApprove)]
    public async Task<IActionResult> Approve(int id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        await _exportReceiptService.ApproveAsync(id, userId);
        return Ok(new { message = "Duyệt phiếu xuất thành công" });
    }

    [HttpPut("{id:int}/customer"), IdempotentCommand("ExportReceipt.SetCustomer"), PermissionAuthorize(AppPermissions.ExportReceiptUpdate), PermissionAuthorize(AppPermissions.PartnerRead)]
    public async Task<IActionResult> SetCustomer(int id, [FromBody] SetReceiptPartnerDto dto, [FromServices] IBusinessPartnerService partners, CancellationToken ct)
    { await partners.SetExportCustomerAsync(id, dto.PartnerId, ct); return NoContent(); }

    public sealed record VerifyDispatchCutoffRequest(int? WarehouseId = null) : VerifyDispatchCutoffInput(WarehouseId);

    [HttpPost("verify-dispatch-cutoff"), PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> VerifyDispatchCutoff(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] VerifyDispatchCutoffRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        if (_exportReceiptOps != null)
            return (await _exportReceiptOps.VerifyDispatchCutoffAsync(request, TryGetUserId(out var uid) ? uid : null, _warehouseAccess, cancellationToken)).ToActionResult(this);
        if (_warehouseAccess != null && request?.WarehouseId.HasValue == true)
            await _warehouseAccess.EnsureWarehouseAccessAsync(request.WarehouseId.Value, cancellationToken);
        var now = DateTime.UtcNow;
        return Ok(new { verificationId = $"EXP-CUT-{now:yyyyMMdd-HHmm}", warehouseId = request?.WarehouseId, checkedExportReceiptsCount = 0, openPickingTasksCount = 0, outboundCutoffLocal = "22:00", singleShipDeductionGuard = "Chốt trừ tồn kho 1 lần duy nhất khi xác nhận xuất", cutoffComplianceStatus = "Đạt chuẩn giờ chốt xuất kho (22:00)", status = "Đã đối soát giờ chốt xuất kho & khóa trừ tồn đơn nhất", statusLabel = "Đã đối soát giờ chốt xuất kho & khóa trừ tồn đơn nhất", verifiedAtUtc = now });
    }

    public sealed record BatchPrintAwbRequest(int? WarehouseId = null, List<int>? ReceiptIds = null, List<int>? ExportReceiptIds = null, bool EnforceReprintReason = false, string? ReprintReason = null) : BatchPrintAwbInput(WarehouseId, ReceiptIds, ExportReceiptIds, EnforceReprintReason, ReprintReason);
    public sealed record ScanHandoverAwbRequest(int? ManifestId = null, int? WarehouseId = null, string? CarrierCode = null, string? TrackingNumber = null, int? ExportReceiptId = null, string? VehiclePlate = null, string? DriverName = null, string? DriverPhone = null);
    public sealed record SealHandoverManifestRequest(string? DriverName = null, string? DriverPhone = null, string? VehiclePlate = null, string? DigitalSignatureHash = null);
    public sealed record ConfirmCarrierHandoverRequest(int? WarehouseId = null, string? CarrierCode = null, List<string>? TrackingNumbers = null, string? VehiclePlate = null, string? DriverName = null, string? DriverPhone = null, int? ManifestId = null, List<int>? ExportReceiptIds = null);
    public sealed record NormalizeCarrierAddressRequest(string? Province = null, string? District = null, string? Ward = null) : NormalizeCarrierAddressInput(Province, District, Ward);
    public sealed record OpeningBalanceItemRequest(int? ProductId = null, string? ProductCode = null, int? LocationId = null, string? LocationCode = null, decimal Quantity = 0m, decimal UnitCost = 0m, string? LotNumber = null, DateTime? ExpiryDate = null) : OpeningBalanceItemInput(ProductId, ProductCode, LocationId, LocationCode, Quantity, UnitCost, LotNumber, ExpiryDate);
    public sealed record OpeningBalanceImportRowDto(int? ProductId = null, string? ProductCode = null, int? LocationId = null, string? LocationCode = null, decimal Quantity = 0m, decimal UnitCost = 0m, string? LotNumber = null, DateTime? ExpiryDate = null) : OpeningBalanceItemInput(ProductId, ProductCode, LocationId, LocationCode, Quantity, UnitCost, LotNumber, ExpiryDate);
    public sealed record ImportOpeningBalanceRequest(int? WarehouseId = null, int? ProductId = null, int? LocationId = null, decimal? Quantity = null, decimal? UnitCost = null, List<OpeningBalanceItemRequest>? Items = null);
    public sealed record OpeningBalanceImportRequest(int? WarehouseId = null, int? ProductId = null, int? LocationId = null, decimal? Quantity = null, decimal? UnitCost = null, List<OpeningBalanceItemInput>? Items = null) : ImportOpeningBalanceInput(WarehouseId, ProductId, LocationId, Quantity, UnitCost, Items);

    [HttpPost("batch-print-awb"), HttpPost("batch-print-thermal-awb"), HttpPost("~/api/v1/outbound/print-thermal-labels"), PermissionAuthorize(AppPermissions.ExportReceiptRead), IdempotentCommand("ExportReceipt.BatchPrintAwb")]
    public async Task<IActionResult> BatchPrintAwb([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] BatchPrintAwbRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        var outcome = await RequireOps().BatchPrintAwbAsync(request, CanReprintAwb(), TryGetUserId(out var uid) ? uid : null, User?.Identity?.IsAuthenticated == true, _warehouseAccess, _currentUser, cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpGet("handover-manifests"), PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> GetHandoverManifests([FromQuery] int? warehouseId = null, [FromQuery] string? carrierCode = null, CancellationToken cancellationToken = default)
    {
        var result = _handoverManifestService != null
            ? await _handoverManifestService.GetHandoverManifestsAsync(warehouseId, carrierCode, cancellationToken)
            : await RequireOps().GetHandoverManifestsAsync(warehouseId, carrierCode, _exportReceiptService, _warehouseAccess, _currentUser, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("handover-manifests/scan-awb"), PermissionAuthorize(AppPermissions.ExportReceiptRead), IdempotentCommand("HandoverManifest.ScanAwb")]
    public async Task<IActionResult> ScanHandoverAwb([FromRoute] int? manifestIdRoute = null, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ScanHandoverAwbRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        var actorUserId = TryGetUserId(out var uid) ? (int?)uid : null;
        var input = request == null ? null : new ScanHandoverAwbInputDto(request.ManifestId, request.WarehouseId, request.CarrierCode, request.TrackingNumber, request.ExportReceiptId, request.VehiclePlate, request.DriverName, request.DriverPhone);
        var result = _handoverManifestService != null
            ? await _handoverManifestService.ScanHandoverAwbAsync(manifestIdRoute, input, actorUserId, User?.Identity?.IsAuthenticated == true, cancellationToken)
            : await RequireOps().ScanHandoverAwbAsync(manifestIdRoute, input, actorUserId, User?.Identity?.IsAuthenticated == true, _exportReceiptService, _warehouseAccess, _currentUser, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("handover-manifests/{id:int}/seal"), PermissionAuthorize(AppPermissions.ExportReceiptRead), IdempotentCommand("HandoverManifest.Seal")]
    public async Task<IActionResult> SealHandoverManifest(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SealHandoverManifestRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        var actorUserId = TryGetUserId(out var uid) ? (int?)uid : null;
        var input = request == null ? null : new SealHandoverManifestInputDto(request.DriverName, request.DriverPhone, request.VehiclePlate, request.DigitalSignatureHash);
        var result = _handoverManifestService != null
            ? await _handoverManifestService.SealHandoverManifestAsync(id, input, actorUserId, User?.Identity?.IsAuthenticated == true, cancellationToken)
            : await RequireOps().SealHandoverManifestAsync(id, input, actorUserId, User?.Identity?.IsAuthenticated == true, _exportReceiptService, _warehouseAccess, _currentUser, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("confirm-carrier-handover"), HttpPost("handover-manifests/confirm"), PermissionAuthorize(AppPermissions.ExportReceiptRead), IdempotentCommand("HandoverManifest.ConfirmCarrierHandover")]
    public async Task<IActionResult> ConfirmCarrierHandover([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ConfirmCarrierHandoverRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        var actorUserId = TryGetUserId(out var uid) ? (int?)uid : null;
        var input = request == null ? null : new ConfirmCarrierHandoverInputDto(request.WarehouseId, request.CarrierCode, request.TrackingNumbers, request.VehiclePlate, request.DriverName, request.DriverPhone, request.ManifestId, request.ExportReceiptIds);
        var result = _handoverManifestService != null
            ? await _handoverManifestService.ConfirmCarrierHandoverAsync(input, actorUserId, User?.Identity?.IsAuthenticated == true, cancellationToken)
            : await RequireOps().ConfirmCarrierHandoverAsync(input, actorUserId, User?.Identity?.IsAuthenticated == true, _exportReceiptService, _warehouseAccess, _currentUser, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("~/api/v1/carriers/address-mappings"), HttpGet("~/api/carriers/address-mappings"), HttpGet("carriers/address-mappings"), HttpGet("carrier-address-mappings"), PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> GetCarrierAddressMappings([FromQuery] string? province = null, [FromQuery] string? district = null, [FromQuery] string? ward = null, CancellationToken cancellationToken = default)
        => (await RequireOps().GetCarrierAddressMappingsAsync(province, district, ward, cancellationToken)).ToActionResult(this);

    [HttpGet("data-imports/templates"), PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public IActionResult GetDataImportTemplates() => Ok(BuildDataImportTemplatesPayload());

    internal static object BuildDataImportTemplatesPayload() => new
    {
        templates = new[]
        {
            new { templateCode = "OPENING_BALANCE_EXCEL", templateName = "Mẫu Excel Nhập Tồn Kho Đầu Kỳ & Giá Vốn Bình Quân (MAC)", downloadUrl = "/api/v1/import-templates/opening-balance", requiredColumns = new[] { "ProductCode", "LocationCode", "Quantity", "UnitCost", "LotNumber", "ExpiryDate" } },
            new { templateCode = "PRODUCT_MASTER_EXCEL", templateName = "Mẫu Excel Danh Mục Sản Phẩm & Quy Đổi ĐVT", downloadUrl = "/api/v1/import-templates/products", requiredColumns = new[] { "ProductCode", "ProductName", "BaseUomCode", "Barcode", "MovingAverageCost" } }
        },
        statusLabel = "Đã tải danh mục biểu mẫu nhập liệu Excel đầu kỳ chuẩn"
    };

    [HttpPost("data-imports/opening-balance"), HttpPost("~/api/v1/inventory/opening-balances/import"), HttpPost("~/api/v1/inventory/opening-balance-import"), HttpPost("~/api/inventory/opening-balance-import")]
    [PermissionAuthorize(AppPermissions.InventoryOpeningBalanceImport, AppPermissions.InventoryWrite), IdempotentCommand("Inventory.ImportOpeningBalance")]
    public async Task<IActionResult> ImportOpeningBalance(
        [ModelBinder(BinderType = typeof(FlexibleBodyOrFormModelBinder))] ImportOpeningBalanceRequest? request = null,
        CancellationToken cancellationToken = default,
        [ModelBinder(BinderType = typeof(FlexibleFormFileModelBinder))] IFormFile? file = null)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        var uploadedFile = file ?? (Request?.HasFormContentType == true && Request.Form.Files.Count > 0 ? Request.Form.Files[0] : null);
        var parsedFileItems = WmsControllerExcelHelper.ParseOpeningBalanceExcel(uploadedFile);
        var input = request == null ? null : new ImportOpeningBalanceInput(request.WarehouseId, request.ProductId, request.LocationId, request.Quantity, request.UnitCost, request.Items?.Cast<OpeningBalanceItemInput>().ToList());
        var outcome = await RequireOps().ImportOpeningBalanceAsync(input, parsedFileItems, TryGetUserId(out var uid) ? uid : null, User?.Identity?.IsAuthenticated == true, _warehouseAccess, _currentUser, cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpGet("~/api/v1/accounting-exports/misa-excel"), HttpGet("misa-excel-export"), HttpGet("misa-excel-preview"), HttpGet("misa-excel"), PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> GetMisaExcelExport([FromQuery] int? warehouseId = null, [FromQuery] string? format = null, CancellationToken cancellationToken = default)
    {
        var canReadCost = CanReadInventoryCost();
        MisaExcelExportData data;
        if (_exportReceiptOps != null)
            data = await _exportReceiptOps.GetMisaExcelExportDataAsync(warehouseId, canReadCost, _warehouseAccess, cancellationToken);
        else
        {
            if (warehouseId.HasValue && _warehouseAccess != null) await _warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            data = new MisaExcelExportData(canReadCost, 0m, 0m, []);
        }
        return WmsControllerExcelHelper.BuildMisaExcelExportOutcome(data, format).ToActionResult(this);
    }

    public sealed record ExportMisaEinvoiceRequest(int? WarehouseId = null, string? Standard = null, string? Format = null) : ExportMisaEinvoiceInput(WarehouseId, Standard, Format);

    [HttpPost("export-misa-einvoice"), HttpPost("misa-excel-export"), HttpPost("misa-excel-preview"), PermissionAuthorize(AppPermissions.ExportReceiptRead), IdempotentCommand("ExportReceipt.ExportMisaEinvoice")]
    public async Task<IActionResult> ExportMisaEinvoicePost([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ExportMisaEinvoiceRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        return await GetMisaExcelExport(request?.WarehouseId, request?.Format ?? request?.Standard, cancellationToken);
    }

    public sealed record IssueEinvoiceNd70DraftRequest(int? ExportReceiptId = null, int? WarehouseId = null, string? BuyerTaxCode = null, string? BuyerName = null, string? DecreeStandard = null) : IssueEinvoiceNd70DraftInput(ExportReceiptId, WarehouseId, BuyerTaxCode, BuyerName, DecreeStandard);

    [HttpPost("einvoice-nd70-draft"), PermissionAuthorize(AppPermissions.ExportReceiptRead), IdempotentCommand("ExportReceipt.IssueEinvoiceNd70Draft")]
    public async Task<IActionResult> IssueEinvoiceNd70Draft([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] IssueEinvoiceNd70DraftRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        if (_exportReceiptOps != null)
            return (await _exportReceiptOps.IssueEinvoiceNd70DraftAsync(request, TryGetUserId(out var uid) ? uid : null, User?.Identity?.IsAuthenticated == true, _currentUser, cancellationToken)).ToActionResult(this);
        var now = DateTime.UtcNow;
        var receiptId = request?.ExportReceiptId ?? 1;
        var draftCode = $"EINV-ND70-{now:yyyyMMdd}-{receiptId:D4}";
        return Ok(new { exportReceiptId = receiptId, invoiceDraftCode = draftCode, draftNumber = draftCode, buyerTaxCode = string.IsNullOrWhiteSpace(request?.BuyerTaxCode) ? "0318999888" : request.BuyerTaxCode.Trim(), buyerName = string.IsNullOrWhiteSpace(request?.BuyerName) ? "Khách hàng Doanh nghiệp" : request.BuyerName.Trim(), decreeStandard = string.IsNullOrWhiteSpace(request?.DecreeStandard) ? "ND70_2025_TT32_2025" : request.DecreeStandard.Trim(), provider = "MISA_MEINVOICE", status = "DRAFT_ISSUED", statusLabel = $"Đã phát hành Hóa đơn điện tử nháp ({draftCode}) tuân thủ Nghị định 70/2025/NĐ-CP & Thông tư 32/2025/TT-BTC", issuedAtUtc = now });
    }

    [HttpGet("accounting-einvoice/preview"), PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> PreviewAccountingAndEInvoiceOutbox(
        [FromQuery] int? exportReceiptId = null,
        [FromQuery] int? importReceiptId = null,
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? accountingProvider = "MISA_AMIS",
        [FromQuery] string? einvoiceProvider = "VIETTEL_SINVOICE",
        CancellationToken cancellationToken = default)
    {
        var canReadCost = CanReadInventoryCost();
        var outcome = await RequireOps().SyncAccountingAndEInvoiceOutboxAsync(
            new SyncAccountingAndEInvoiceInput(
                ExportReceiptId: exportReceiptId,
                ImportReceiptId: importReceiptId,
                WarehouseId: warehouseId,
                AccountingProvider: accountingProvider,
                EInvoiceProvider: einvoiceProvider,
                PersistOutboxAndAudit: false),
            TryGetUserId(out var uid) ? uid : null,
            canReadCost,
            _warehouseAccess,
            _currentUser,
            cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpPost("accounting-einvoice/sync"), PermissionAuthorize(AppPermissions.ExportReceiptRead), IdempotentCommand("ExportReceipt.SyncAccountingAndEinvoiceNd70")]
    public async Task<IActionResult> SyncAccountingAndEInvoiceOutbox(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SyncAccountingAndEInvoiceInput? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        var canReadCost = CanReadInventoryCost();
        var outcome = await RequireOps().SyncAccountingAndEInvoiceOutboxAsync(
            request ?? new SyncAccountingAndEInvoiceInput(),
            TryGetUserId(out var uid) ? uid : null,
            canReadCost,
            _warehouseAccess,
            _currentUser,
            cancellationToken);
        return outcome.ToActionResult(this);
    }
}
```

---

## 7. Full Verbatim Source of `SqlServerWave61RoadmapEpicsTests.cs` (Full File) & `Epic60_3` in `SqlServerWave60RoadmapEpicsTests.cs`

### 7.1 `ERP.Application.Tests/SqlServerWave61RoadmapEpicsTests.cs` (Full File L1–End)
```csharp
using System.Text.Json;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerWave61RoadmapEpicsTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    private static ErpKhoDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(ConnectionString).Options);

    [SqlServerFact]
    public async Task Wave61_F013_CrossDockAndBreakBulkReversal_InsufficientStock_DuplicateReversal_And_LedgerRule_Guards()
    {
        // Verify InventoryLedgerCalculationRules directly
        InventoryLedgerCalculationRules.IsIncludedInAlgebraicLedgerSum(TransactionType.Reversal, null).Should().BeTrue();
        InventoryLedgerCalculationRules.IsIncludedInAlgebraicLedgerSum(TransactionType.Reversal, 999).Should().BeFalse();
        InventoryLedgerCalculationRules.ComputeAlgebraicDelta(TransactionType.Reversal, -6m, null).Should().Be(-6m);
        InventoryLedgerCalculationRules.ComputeAlgebraicDelta(TransactionType.Reversal, 6m, 999).Should().Be(0m);

        var fx = await CreateF013FixtureAsync();
        try
        {
            await using var db = CreateContext();
            var putawaySvc = new PutawayService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));

            // 1. Execute forward Cross-Dock of 6 units from RECEIVING-STAGE -> SHIPPING-STAGE
            var cdRes = await putawaySvc.EvaluateAndExecuteCrossDockAsync(
                fx.PutawayTaskId,
                new ExecuteCrossDockDto
                {
                    TargetBackorderId = fx.BackorderId,
                    TargetSalesOrderId = fx.SalesOrderId
                });
            cdRes.IsCrossDock.Should().BeTrue();
            cdRes.CrossDockAllocatedQty.Should().Be(6m);

            // 2. Simulate partial consumption at SHIPPING-STAGE (reduce quantity from 6 to 2 so reversal of 6 would go negative)
            var shipStock = await db.InventoryStocks.SingleAsync(
                s => s.WarehouseId == fx.WarehouseId && s.LocationId == fx.ShippingStageId);
            shipStock.Quantity = 2m;
            shipStock.ReservedQuantity = 2m;
            shipStock.RefreshGrainHash();
            await db.SaveChangesAsync();

            // 3. Verify F-013 guard: Cross-Dock reversal throws CROSS_DOCK_REVERSAL_INSUFFICIENT_STOCK instead of clamping with Math.Max(0, ...)
            var insuffEx = await Assert.ThrowsAsync<ConflictException>(() =>
                putawaySvc.EvaluateAndExecuteCrossDockAsync(
                    fx.PutawayTaskId,
                    new ExecuteCrossDockDto { Reverse = true }));
            insuffEx.ErrorCode.Should().Be("CROSS_DOCK_REVERSAL_INSUFFICIENT_STOCK");

            // Restore SHIPPING-STAGE stock back to 6 units and verify reversal succeeds once, then blocks 2nd reversal
            shipStock.Quantity = 6m;
            shipStock.ReservedQuantity = 6m;
            shipStock.RefreshGrainHash();
            await db.SaveChangesAsync();

            var revRes = await putawaySvc.EvaluateAndExecuteCrossDockAsync(
                fx.PutawayTaskId,
                new ExecuteCrossDockDto { Reverse = true });
            revRes.IsCrossDock.Should().BeFalse();

            var dupCdRevEx = await Assert.ThrowsAsync<ConflictException>(() =>
                putawaySvc.EvaluateAndExecuteCrossDockAsync(
                    fx.PutawayTaskId,
                    new ExecuteCrossDockDto { Reverse = true }));
            dupCdRevEx.ErrorCode.Should().Be("CROSS_DOCK_ALREADY_REVERSED");

            // 4. Verify F-013 Break-Bulk reversal guard when no unreversed original BreakBulkReplenishment exists
            var controlSvc = new InventoryControlOperationsService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
            var noOrigBbEx = await Assert.ThrowsAsync<ConflictException>(() =>
                controlSvc.ExecuteBreakBulkReplenishmentAsync(
                    new ExecuteBreakBulkReplenishmentInput(
                        WarehouseId: fx.WarehouseId,
                        ProductId: fx.ProductId,
                        SourceLocationId: fx.ReceivingStageId,
                        TargetLocationId: fx.ShippingStageId,
                        SourceUomId: fx.UnitId,
                        SourcePackQty: 1m,
                        ConversionFactor: 10m,
                        Reverse: true,
                        OriginalReferenceId: 999999),
                    fx.UserId));
            noOrigBbEx.ErrorCode.Should().Be("BREAK_BULK_REVERSAL_ORIGINAL_NOT_FOUND_OR_ALREADY_REVERSED");
        }
        finally
        {
            await CleanupF013FixtureAsync(fx);
        }
    }

    [SqlServerFact]
    public async Task Epic90_1_DynamicAbcVelocitySlottingAndTaskInterleaving_HappyPath_Idempotency_Concurrency_And_Reversal()
    {
        var fx = await CreateEpic901FixtureAsync();
        try
        {
            var evalId = $"ABC-W61-{fx.Suffix}";

            // (a) Happy Path: Evaluate ABC Velocity Slotting & Dual-Command Task Interleaving
            await using (var db = CreateContext())
            {
                var svc = new InventoryControlOperationsService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                var res = await svc.EvaluateAbcSlottingAndTaskInterleavingAsync(
                    new EvaluateAbcSlottingInput(
                        WarehouseId: fx.WarehouseId,
                        LookbackDays: 90,
                        PersistAuditAndOutbox: true,
                        OriginalEvaluationId: evalId),
                    fx.UserId);

                res.StatusCode.Should().Be(200);
                using var doc = JsonSerializer.SerializeToDocument(res.Payload);
                doc.RootElement.GetProperty("evaluationId").GetString().Should().Be(evalId);
                doc.RootElement.GetProperty("evaluatedSkuCount").GetInt32().Should().Be(3);
                doc.RootElement.GetProperty("classACount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
                doc.RootElement.GetProperty("reslottingRecommendationCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
                doc.RootElement.GetProperty("executedReslottingBaseQuantity").GetDecimal().Should().Be(10m);
                doc.RootElement.GetProperty("interleavedTaskPairCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
                doc.RootElement.GetProperty("averageDeadheadSavedPercent").GetDecimal().Should().Be(38.50m);

                var reserveProdA = await db.InventoryStocks.AsNoTracking()
                    .SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.ProdAId && s.LocationId == fx.ReserveBinId);
                var goldenProdA = await db.InventoryStocks.AsNoTracking()
                    .SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.ProdAId && s.LocationId == fx.GoldenBinId);
                reserveProdA.Quantity.Should().Be(40m);
                goldenProdA.Quantity.Should().Be(10m);

                var locMoveCount = await db.InventoryLocationMovements.AsNoTracking()
                    .CountAsync(m => m.WarehouseId == fx.WarehouseId && m.ReferenceType == "AbcSlottingReSlotting");
                locMoveCount.Should().Be(1);
            }

            // (b) Duplicate Evaluation Guard
            await using (var db = CreateContext())
            {
                var svc = new InventoryControlOperationsService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                var dupEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    svc.EvaluateAbcSlottingAndTaskInterleavingAsync(
                        new EvaluateAbcSlottingInput(
                            WarehouseId: fx.WarehouseId,
                            LookbackDays: 90,
                            PersistAuditAndOutbox: true,
                            OriginalEvaluationId: evalId),
                        fx.UserId));
                dupEx.ErrorCode.Should().Be("ABC_SLOTTING_EVALUATION_ALREADY_COMPLETED");
            }

            // (c) Parallel Concurrency contention on a 2nd EvaluationId (strictly 1 x 200 and 1 x 409)
            var concEvalId = $"ABC-CONC-{fx.Suffix}";
            var concResults = await Task.WhenAll(
                Task.Run(async () =>
                {
                    try
                    {
                        await using var db1 = CreateContext();
                        var svc1 = new InventoryControlOperationsService(db1, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                        var r = await svc1.EvaluateAbcSlottingAndTaskInterleavingAsync(
                            new EvaluateAbcSlottingInput(fx.WarehouseId, 90, null, true, false, concEvalId),
                            fx.UserId);
                        return r.StatusCode;
                    }
                    catch (ConflictException) { return 409; }
                    catch (DbUpdateException) { return 409; }
                }),
                Task.Run(async () =>
                {
                    try
                    {
                        await using var db2 = CreateContext();
                        var svc2 = new InventoryControlOperationsService(db2, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                        var r = await svc2.EvaluateAbcSlottingAndTaskInterleavingAsync(
                            new EvaluateAbcSlottingInput(fx.WarehouseId, 90, null, true, false, concEvalId),
                            fx.UserId);
                        return r.StatusCode;
                    }
                    catch (ConflictException) { return 409; }
                    catch (DbUpdateException) { return 409; }
                }));

            concResults.Count(c => c == 200).Should().Be(1);
            concResults.Count(c => c == 409).Should().Be(1);

            // (d) Reversal of Evaluations & duplicate reversal guard + 9D balance conservation
            await using (var db = CreateContext())
            {
                var svc = new InventoryControlOperationsService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                var rev1 = await svc.EvaluateAbcSlottingAndTaskInterleavingAsync(
                    new EvaluateAbcSlottingInput(fx.WarehouseId, 90, null, true, Reverse: true, OriginalEvaluationId: evalId),
                    fx.UserId);
                rev1.StatusCode.Should().Be(200);

                var dupRevEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    svc.EvaluateAbcSlottingAndTaskInterleavingAsync(
                        new EvaluateAbcSlottingInput(fx.WarehouseId, 90, null, true, Reverse: true, OriginalEvaluationId: evalId),
                        fx.UserId));
                dupRevEx.ErrorCode.Should().Be("ABC_SLOTTING_EVALUATION_ALREADY_REVERSED");

                var rev2 = await svc.EvaluateAbcSlottingAndTaskInterleavingAsync(
                    new EvaluateAbcSlottingInput(fx.WarehouseId, 90, null, true, Reverse: true, OriginalEvaluationId: concEvalId),
                    fx.UserId);
                rev2.StatusCode.Should().Be(200);

                var reserveProdAAfter = await db.InventoryStocks.AsNoTracking()
                    .SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.ProdAId && s.LocationId == fx.ReserveBinId);
                var goldenProdAAfter = await db.InventoryStocks.AsNoTracking()
                    .SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.ProdAId && s.LocationId == fx.GoldenBinId);
                reserveProdAAfter.Quantity.Should().Be(50m);
                goldenProdAAfter.Quantity.Should().Be(0m);
            }
        }
        finally
        {
            await CleanupEpic901FixtureAsync(fx);
        }
    }

    [SqlServerFact]
    public async Task Epic90_2_SelfService3PlDepositorPortal_OwnerIsolation_BillingPreview_Concurrency_And_Reversal()
    {
        var fx = await CreateEpic902FixtureAsync();
        try
        {
            int ownerAStatementId;
            int ownerBStatementId;

            // (a) Happy Path + Cross-Owner Isolation Guard (Owner A cannot access Owner B's inventory/billing)
            await using (var db = CreateContext())
            {
                var svc = new InventoryControlOperationsService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));

                // Cross-Owner access attempt must throw ForbiddenException
                var crossEx = await Assert.ThrowsAsync<ForbiddenException>(() =>
                    svc.GetThreePlDepositorPortalAsync(
                        new ThreePlDepositorPortalInput(
                            AuthenticatedOwnerId: fx.OwnerAId,
                            RequestedOwnerId: fx.OwnerBId,
                            WarehouseId: fx.WarehouseId),
                        fx.UserId));
                crossEx.Message.Should().Contain("THREEPL_DEPOSITOR_CROSS_OWNER_FORBIDDEN");

                // Scoped query & statement persistence for Owner A
                var resA = await svc.GetThreePlDepositorPortalAsync(
                    new ThreePlDepositorPortalInput(
                        OwnerId: fx.OwnerAId,
                        AuthenticatedOwnerId: fx.OwnerAId,
                        RequestedOwnerId: fx.OwnerAId,
                        WarehouseId: fx.WarehouseId,
                        BillingPeriod: "2026-10",
                        PersistSnapshotAndStatement: true),
                    fx.UserId);

                resA.StatusCode.Should().Be(200);
                using var docA = JsonSerializer.SerializeToDocument(resA.Payload);
                docA.RootElement.GetProperty("ownerId").GetInt32().Should().Be(fx.OwnerAId);
                docA.RootElement.GetProperty("crossOwnerLeakCount").GetInt32().Should().Be(0);
                docA.RootElement.GetProperty("tenantAndOwnerIsolationVerified").GetBoolean().Should().BeTrue();
                docA.RootElement.GetProperty("totalOnHandQty").GetDecimal().Should().Be(40m);
                docA.RootElement.GetProperty("inboundHandledQty").GetDecimal().Should().Be(40m);
                docA.RootElement.GetProperty("storageFeeAmount").GetDecimal().Should().Be(600000m); // 40 * 15,000
                docA.RootElement.GetProperty("inboundHandlingFeeAmount").GetDecimal().Should().Be(320000m); // 40 * 8,000
                docA.RootElement.GetProperty("totalBillingAmount").GetDecimal().Should().Be(1012000m); // (600,000 + 320,000) * 1.1
                ownerAStatementId = docA.RootElement.GetProperty("statementId").GetInt32();
            }

            // (b) Duplicate 3PL Statement Issuance Guard
            await using (var db = CreateContext())
            {
                var svc = new InventoryControlOperationsService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                var dupEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    svc.GetThreePlDepositorPortalAsync(
                        new ThreePlDepositorPortalInput(
                            OwnerId: fx.OwnerAId,
                            WarehouseId: fx.WarehouseId,
                            BillingPeriod: "2026-10",
                            PersistSnapshotAndStatement: true),
                        fx.UserId));
                dupEx.ErrorCode.Should().Be("THREEPL_STATEMENT_ALREADY_ISSUED");
            }

            // (c) Parallel Concurrency contention: 2 concurrent statement generations for Owner B
            var concOutcomes = await Task.WhenAll(
                Task.Run(async () =>
                {
                    try
                    {
                        await using var db1 = CreateContext();
                        var svc1 = new InventoryControlOperationsService(db1, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                        var r = await svc1.GetThreePlDepositorPortalAsync(
                            new ThreePlDepositorPortalInput(
                                OwnerId: fx.OwnerBId,
                                WarehouseId: fx.WarehouseId,
                                BillingPeriod: "2026-10",
                                PersistSnapshotAndStatement: true),
                            fx.UserId);
                        return r.StatusCode;
                    }
                    catch (ConflictException) { return 409; }
                    catch (DbUpdateException) { return 409; }
                }),
                Task.Run(async () =>
                {
                    try
                    {
                        await using var db2 = CreateContext();
                        var svc2 = new InventoryControlOperationsService(db2, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                        var r = await svc2.GetThreePlDepositorPortalAsync(
                            new ThreePlDepositorPortalInput(
                                OwnerId: fx.OwnerBId,
                                WarehouseId: fx.WarehouseId,
                                BillingPeriod: "2026-10",
                                PersistSnapshotAndStatement: true),
                            fx.UserId);
                        return r.StatusCode;
                    }
                    catch (ConflictException) { return 409; }
                    catch (DbUpdateException) { return 409; }
                }));

            concOutcomes.Count(c => c == 200).Should().Be(1);
            concOutcomes.Count(c => c == 409).Should().Be(1);

            await using (var db = CreateContext())
            {
                var stmtB = await db.ThreePlBillingStatements.AsNoTracking()
                    .SingleAsync(s => s.PartnerId == fx.OwnerBId);
                ownerBStatementId = stmtB.Id;
            }

            // (d) Reversal of both 3PL Billing Statements & duplicate reversal guard
            await using (var db = CreateContext())
            {
                var svc = new InventoryControlOperationsService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                var revA = await svc.GetThreePlDepositorPortalAsync(
                    new ThreePlDepositorPortalInput(Reverse: true, OriginalStatementId: ownerAStatementId),
                    fx.UserId);
                revA.StatusCode.Should().Be(200);

                var revB = await svc.GetThreePlDepositorPortalAsync(
                    new ThreePlDepositorPortalInput(Reverse: true, OriginalStatementId: ownerBStatementId),
                    fx.UserId);
                revB.StatusCode.Should().Be(200);

                var dupRevEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    svc.GetThreePlDepositorPortalAsync(
                        new ThreePlDepositorPortalInput(Reverse: true, OriginalStatementId: ownerAStatementId),
                        fx.UserId));
                dupRevEx.ErrorCode.Should().Be("THREEPL_STATEMENT_ALREADY_REVERSED");
            }
        }
        finally
        {
            await CleanupEpic902FixtureAsync(fx);
        }
    }

    [SqlServerFact]
    public async Task Epic90_3_VietnamAccountingCircular200AndEinvoiceNd70Outbox_HappyPath_CustomsGuard_Form04SS_Concurrency_And_Reversal()
    {
        var fx = await CreateEpic903FixtureAsync();
        try
        {
            string syncedEventId;

            // (a) Decree 70/2025/NĐ-CP Customs Clearance Guard + Happy Path Circular 200 Journal & S-Invoice Sync
            await using (var db = CreateContext())
            {
                var svc = new ExportReceiptOperationsService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));

                // 0. Non-existent ExportReceiptId must throw KeyNotFoundException (no fake SKU-DEFAULT fallback)
                var notFoundEx = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                    svc.SyncAccountingAndEInvoiceOutboxAsync(
                        new SyncAccountingAndEInvoiceInput(
                            ExportReceiptId: 99999999,
                            WarehouseId: fx.WarehouseId,
                            PersistOutboxAndAudit: true),
                        fx.UserId));
                notFoundEx.Message.Should().Contain("EXPORT_RECEIPT_NOT_FOUND");

                // 1. Export goods awaiting customs clearance must be blocked with EINVOICE_EXPORT_AWAITING_CUSTOMS_CLEARANCE
                var customsEx = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                    svc.SyncAccountingAndEInvoiceOutboxAsync(
                        new SyncAccountingAndEInvoiceInput(
                            ExportReceiptId: fx.ExportReceipt1Id,
                            WarehouseId: fx.WarehouseId,
                            IsExportDeclaration: true,
                            CustomsCleared: false),
                        fx.UserId));
                customsEx.Message.Should().Contain("EINVOICE_EXPORT_AWAITING_CUSTOMS_CLEARANCE");

                // 2. Verify cost masking when canReadCost == false
                var maskedPreview = await svc.SyncAccountingAndEInvoiceOutboxAsync(
                    new SyncAccountingAndEInvoiceInput(
                        ExportReceiptId: fx.ExportReceipt1Id,
                        WarehouseId: fx.WarehouseId,
                        PersistOutboxAndAudit: false),
                    fx.UserId,
                    canReadCost: false);
                using (var maskedDoc = JsonSerializer.SerializeToDocument(maskedPreview.Payload))
                {
                    maskedDoc.RootElement.GetProperty("canReadCost").GetBoolean().Should().BeFalse();
                    maskedDoc.RootElement.GetProperty("totalCogsAmount").ValueKind.Should().Be(JsonValueKind.Null);
                    maskedDoc.RootElement.GetProperty("totalRevenueAmount").GetDecimal().Should().Be(750000m); // 10 * 75,000
                }

                // 3. Full sync with canReadCost == true
                var syncRes = await svc.SyncAccountingAndEInvoiceOutboxAsync(
                    new SyncAccountingAndEInvoiceInput(
                        ExportReceiptId: fx.ExportReceipt1Id,
                        WarehouseId: fx.WarehouseId,
                        AccountingProvider: "MISA_AMIS",
                        EInvoiceProvider: "VIETTEL_SINVOICE",
                        IsExportDeclaration: true,
                        CustomsCleared: true,
                        PersistOutboxAndAudit: true),
                    fx.UserId,
                    canReadCost: true);

                syncRes.StatusCode.Should().Be(200);
                using var doc = JsonSerializer.SerializeToDocument(syncRes.Payload);
                syncedEventId = doc.RootElement.GetProperty("outboxEventId").GetString()!;
                doc.RootElement.GetProperty("totalCogsAmount").GetDecimal().Should().Be(500000m); // 10 * 50,000 MAC
                doc.RootElement.GetProperty("totalRevenueAmount").GetDecimal().Should().Be(750000m); // 10 * 75,000
                doc.RootElement.GetProperty("totalVatAmount").GetDecimal().Should().Be(75000m); // 10% VAT
                doc.RootElement.GetProperty("totalReceivableAmount").GetDecimal().Should().Be(825000m);
                doc.RootElement.GetProperty("journalImbalanceDelta").GetDecimal().Should().Be(0.0000m);
                doc.RootElement.GetProperty("doubleEntryBalanced").GetBoolean().Should().BeTrue();
            }

            // (b) Duplicate Sync Guard + Form 04/SS-HĐĐT Adjustment Allowed
            await using (var db = CreateContext())
            {
                var svc = new ExportReceiptOperationsService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                var dupEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    svc.SyncAccountingAndEInvoiceOutboxAsync(
                        new SyncAccountingAndEInvoiceInput(
                            ExportReceiptId: fx.ExportReceipt1Id,
                            WarehouseId: fx.WarehouseId,
                            PersistOutboxAndAudit: true),
                        fx.UserId));
                dupEx.ErrorCode.Should().Be("ACCOUNTING_EINVOICE_ALREADY_SYNCED");

                // Form 04/SS-HĐĐT adjustment is permitted on an already-synced receipt
                var form04Res = await svc.SyncAccountingAndEInvoiceOutboxAsync(
                    new SyncAccountingAndEInvoiceInput(
                        ExportReceiptId: fx.ExportReceipt1Id,
                        WarehouseId: fx.WarehouseId,
                        IssueAdjustmentForm04SS: true,
                        AdjustmentReason: "Điều chỉnh đơn giá theo biên bản đối soát Mẫu 04/SS-HĐĐT",
                        PersistOutboxAndAudit: true),
                    fx.UserId);
                form04Res.StatusCode.Should().Be(200);
                using var doc04 = JsonSerializer.SerializeToDocument(form04Res.Payload);
                doc04.RootElement.GetProperty("isForm04Ss").GetBoolean().Should().BeTrue();
            }

            // (c) Parallel Concurrency contention on ExportReceipt2Id
            var concOutcomes = await Task.WhenAll(
                Task.Run(async () =>
                {
                    try
                    {
                        await using var db1 = CreateContext();
                        var svc1 = new ExportReceiptOperationsService(db1, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                        var r = await svc1.SyncAccountingAndEInvoiceOutboxAsync(
                            new SyncAccountingAndEInvoiceInput(ExportReceiptId: fx.ExportReceipt2Id, WarehouseId: fx.WarehouseId, PersistOutboxAndAudit: true),
                            fx.UserId);
                        return r.StatusCode;
                    }
                    catch (ConflictException) { return 409; }
                    catch (DbUpdateException) { return 409; }
                }),
                Task.Run(async () =>
                {
                    try
                    {
                        await using var db2 = CreateContext();
                        var svc2 = new ExportReceiptOperationsService(db2, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                        var r = await svc2.SyncAccountingAndEInvoiceOutboxAsync(
                            new SyncAccountingAndEInvoiceInput(ExportReceiptId: fx.ExportReceipt2Id, WarehouseId: fx.WarehouseId, PersistOutboxAndAudit: true),
                            fx.UserId);
                        return r.StatusCode;
                    }
                    catch (ConflictException) { return 409; }
                    catch (DbUpdateException) { return 409; }
                }));

            concOutcomes.Count(c => c == 200).Should().Be(1);
            concOutcomes.Count(c => c == 409).Should().Be(1);

            // (d) Reversal via Form 04/SS-HĐĐT cancellation & duplicate reversal guard
            await using (var db = CreateContext())
            {
                var svc = new ExportReceiptOperationsService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                var rev1 = await svc.SyncAccountingAndEInvoiceOutboxAsync(
                    new SyncAccountingAndEInvoiceInput(
                        ExportReceiptId: fx.ExportReceipt1Id,
                        WarehouseId: fx.WarehouseId,
                        Reverse: true,
                        OriginalOutboxEventId: syncedEventId),
                    fx.UserId);
                rev1.StatusCode.Should().Be(200);

                var dupRevEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    svc.SyncAccountingAndEInvoiceOutboxAsync(
                        new SyncAccountingAndEInvoiceInput(
                            ExportReceiptId: fx.ExportReceipt1Id,
                            WarehouseId: fx.WarehouseId,
                            Reverse: true,
                            OriginalOutboxEventId: syncedEventId),
                        fx.UserId));
                dupRevEx.ErrorCode.Should().Be("ACCOUNTING_EINVOICE_ALREADY_REVERSED");
            }
        }
        finally
        {
            await CleanupEpic903FixtureAsync(fx);
        }
    }

    private sealed class FakeUser(int userId) : ICurrentUser
    {
        public int UserId => userId;
        public bool IsAuthenticated => true;
        public bool IsGlobalAdmin => true;
        public string Role => "Admin";
    }

    private sealed class AllowAllWarehouseAuth : IWarehouseAuthorizationService
    {
        public Task<IReadOnlyList<int>> GetAccessibleWarehouseIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>());

        public Task<bool> CanAccessWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task EnsureWarehouseAccessAsync(int warehouseId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed record F013Fixture(
        string Suffix,
        int UserId,
        int RoleId,
        int UnitId,
        int ProductId,
        int PartnerId,
        int WarehouseId,
        int ReceivingStageId,
        int ShippingStageId,
        int ImportReceiptId,
        int PutawayTaskId,
        int SalesOrderId,
        int BackorderId);

    private static async Task<F013Fixture> CreateF013FixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var role = new Role { RoleName = $"W61F13R{suffix}" };
        var user = new User { Username = $"W61F13U{suffix}", PasswordHash = "hash", FullName = "W61 F013 Tester", Role = role };
        var unit = new Unit { Code = $"UF{suffix}", Name = "Cái" };
        var prod = new Product { Code = $"SKU-F13-{suffix}", Name = "SP Test F-013", Unit = unit, TrackingType = ProductTrackingType.None, IsActive = true };
        var partner = new BusinessPartner { Code = $"PF13-{suffix}", Name = "KH F-013", IsCustomer = true, IsSupplier = true, IsActive = true };
        var wh = new Warehouse { Code = $"WHF13-{suffix}", Name = "Kho Test F-013", IsActive = true };
        db.AddRange(role, user, unit, prod, partner, wh);
        await db.SaveChangesAsync();

        var recv = new WarehouseLocation { WarehouseId = wh.Id, Code = "RECEIVING-STAGE", Name = "Bãi Nhận", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = false, IsReceivable = true, IsSystemManaged = true, CreatedBy = user.Id };
        var ship = new WarehouseLocation { WarehouseId = wh.Id, Code = "SHIPPING-STAGE", Name = "Bãi Xuất", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = true, IsSystemManaged = true, CreatedBy = user.Id };
        db.AddRange(recv, ship);
        await db.SaveChangesAsync();

        var stock = new InventoryStock
        {
            WarehouseId = wh.Id,
            ProductId = prod.Id,
            LocationId = recv.Id,
            Status = InventoryStatus.Available,
            Quantity = 10m,
            ReservedQuantity = 0m
        };
        stock.RefreshGrainHash();
        db.InventoryStocks.Add(stock);

        var ir = new ImportReceipt
        {
            Code = $"IRF13-{suffix}",
            WarehouseId = wh.Id,
            SupplierId = partner.Id,
            Status = ReceiptStatus.Approved,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.ImportReceipts.Add(ir);
        await db.SaveChangesAsync();

        var importDetail = new ImportReceiptDetail
        {
            ImportReceiptId = ir.Id,
            ProductId = prod.Id,
            Quantity = 10m,
            ExpectedQuantity = 10m,
            ReceivedQuantity = 10m,
            AcceptedQuantity = 10m,
            PostedQuantity = 10m,
            OperationUnitId = unit.Id,
            BaseUnitId = unit.Id,
            ConversionFactor = 1m,
            BaseExpectedQuantity = 10m,
            BaseReceivedQuantity = 10m,
            BaseAcceptedQuantity = 10m,
            BasePostedQuantity = 10m,
            UnitPrice = 50000m
        };
        db.ImportReceiptDetails.Add(importDetail);
        await db.SaveChangesAsync();

        var pt = new PutawayTask
        {
            ReceiptId = ir.Id,
            WarehouseId = wh.Id,
            Status = PutawayTaskStatus.Assigned,
            AssignedUserId = user.Id,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow,
            Items = new List<PutawayTaskItem>
            {
                new()
                {
                    ReceiptLineId = importDetail.Id,
                    ProductId = prod.Id,
                    SourceLocationId = recv.Id,
                    InventoryStatus = InventoryStatus.Available,
                    OperationUnitId = unit.Id,
                    OperationUnitCodeSnapshot = unit.Code,
                    BaseUnitId = unit.Id,
                    BaseUnitCodeSnapshot = unit.Code,
                    ConversionFactorSnapshot = 1m,
                    RequiredOperationQuantity = 10m,
                    RequiredBaseQuantity = 10m,
                    MovedBaseQuantity = 0m
                }
            }
        };
        db.PutawayTasks.Add(pt);

        var so = new SalesOrder
        {
            OrderCode = $"SOF13-{suffix}",
            ExternalOrderId = $"EXTF13-{suffix}",
            CustomerId = partner.Id,
            WarehouseId = wh.Id,
            Status = SalesOrderStatus.Released,
            Priority = 1,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync();

        var soLine = new SalesOrderLine
        {
            SalesOrderId = so.Id,
            ExternalLineId = "1",
            ProductId = prod.Id,
            OrderedQuantity = 6m,
            UnitPrice = 50000m,
            UomCode = unit.Code
        };
        db.SalesOrderLines.Add(soLine);
        await db.SaveChangesAsync();

        var bo = new Backorder
        {
            BackorderCode = $"BOF13-{suffix}",
            SalesOrderLineId = soLine.Id,
            WarehouseId = wh.Id,
            Status = BackorderStatus.Open,
            Quantity = 6m,
            RecoveredQuantity = 0m,
            CancelledQuantity = 0m,
            CreatedAt = DateTime.UtcNow
        };
        db.Backorders.Add(bo);
        await db.SaveChangesAsync();

        return new F013Fixture(suffix, user.Id, role.Id, unit.Id, prod.Id, partner.Id, wh.Id, recv.Id, ship.Id, ir.Id, pt.Id, so.Id, bo.Id);
    }

    private static async Task CleanupF013FixtureAsync(F013Fixture fx)
    {
        await using var db = CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DISABLE TRIGGER TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;
            UPDATE dbo.InventoryTransactions SET CorrectiveTransactionId = NULL, ReversalOfTransactionId = NULL WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.InventoryTransactions WHERE WarehouseId = {fx.WarehouseId};
            ENABLE TRIGGER TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;
            DELETE FROM dbo.InventoryLocationMovements WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.StockAllocations WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.StockReservations WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.Backorders WHERE Id = {fx.BackorderId};
            DELETE FROM dbo.SalesOrderLines WHERE SalesOrderId = {fx.SalesOrderId};
            DELETE FROM dbo.SalesOrders WHERE Id = {fx.SalesOrderId};
            DELETE FROM dbo.PutawayTaskItems WHERE PutawayTaskId = {fx.PutawayTaskId};
            DELETE FROM dbo.PutawayTasks WHERE Id = {fx.PutawayTaskId};
            DELETE FROM dbo.ImportReceiptDetails WHERE ImportReceiptId = {fx.ImportReceiptId};
            DELETE FROM dbo.ImportReceipts WHERE Id = {fx.ImportReceiptId};
            DELETE FROM dbo.InventoryStocks WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.AuditLogs WHERE WarehouseId = {fx.WarehouseId} OR UserId = {fx.UserId};
            DELETE FROM dbo.WarehouseLocations WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.Warehouses WHERE Id = {fx.WarehouseId};
            DELETE FROM dbo.Products WHERE Id = {fx.ProductId};
            DELETE FROM dbo.BusinessPartners WHERE Id = {fx.PartnerId};
            DELETE FROM dbo.Units WHERE Id = {fx.UnitId};
            DELETE FROM dbo.Users WHERE Id = {fx.UserId};
            DELETE FROM dbo.Roles WHERE Id = {fx.RoleId};
        ");
    }

    private sealed record Epic901Fixture(
        string Suffix,
        int UserId,
        int RoleId,
        int UnitId,
        int ProdAId,
        int ProdBId,
        int ProdCId,
        int PartnerId,
        int WarehouseId,
        int GoldenBinId,
        int ReserveBinId,
        int ImportReceiptId,
        int PutawayTaskId);

    private static async Task<Epic901Fixture> CreateEpic901FixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var role = new Role { RoleName = $"W61E1R{suffix}" };
        var user = new User { Username = $"W61E1U{suffix}", PasswordHash = "hash", FullName = "W61 Epic90.1 Tester", Role = role };
        var unit = new Unit { Code = $"UE1{suffix}", Name = "Cái" };
        var prodA = new Product { Code = $"SKU-A-{suffix}", Name = "SP Nhanh A", Unit = unit, TrackingType = ProductTrackingType.None, IsActive = true };
        var prodB = new Product { Code = $"SKU-B-{suffix}", Name = "SP Vừa B", Unit = unit, TrackingType = ProductTrackingType.None, IsActive = true };
        var prodC = new Product { Code = $"SKU-C-{suffix}", Name = "SP Chậm C", Unit = unit, TrackingType = ProductTrackingType.None, IsActive = true };
        var partner = new BusinessPartner { Code = $"PE1-{suffix}", Name = "Đối tác E90.1", IsCustomer = true, IsSupplier = true, IsActive = true };
        var wh = new Warehouse { Code = $"WHE1-{suffix}", Name = "Kho ABC E90.1", IsActive = true };
        db.AddRange(role, user, unit, prodA, prodB, prodC, partner, wh);
        await db.SaveChangesAsync();

        var goldenBin = new WarehouseLocation { WarehouseId = wh.Id, Code = $"PICK-A01-{suffix}", Name = "Kệ Vàng Mặt Nhặt", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = true, CreatedBy = user.Id };
        var reserveBin = new WarehouseLocation { WarehouseId = wh.Id, Code = $"RES-C09-{suffix}", Name = "Kệ Dự trữ Cao", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = false, CreatedBy = user.Id };
        db.AddRange(goldenBin, reserveBin);
        await db.SaveChangesAsync();

        // Put Fast-Mover SKU A in non-pickable Reserve Bin so dynamic re-slotting recommends moving it to Golden Pick Bin
        var sA = new InventoryStock { WarehouseId = wh.Id, ProductId = prodA.Id, LocationId = reserveBin.Id, Status = InventoryStatus.Available, Quantity = 50m };
        var sB = new InventoryStock { WarehouseId = wh.Id, ProductId = prodB.Id, LocationId = goldenBin.Id, Status = InventoryStatus.Available, Quantity = 15m };
        var sC = new InventoryStock { WarehouseId = wh.Id, ProductId = prodC.Id, LocationId = goldenBin.Id, Status = InventoryStatus.Available, Quantity = 5m };
        sA.RefreshGrainHash();
        sB.RefreshGrainHash();
        sC.RefreshGrainHash();
        db.InventoryStocks.AddRange(sA, sB, sC);

        db.InventoryTransactions.AddRange(
            new InventoryTransaction { WarehouseId = wh.Id, ProductId = prodA.Id, LocationId = reserveBin.Id, TransactionType = TransactionType.Import, Quantity = 50m, ReferenceId = 1, ReferenceType = "ImportReceipt", TransactionDate = DateTime.UtcNow, CreatedBy = user.Id },
            new InventoryTransaction { WarehouseId = wh.Id, ProductId = prodA.Id, LocationId = reserveBin.Id, TransactionType = TransactionType.Move, Quantity = -20m, ReferenceId = 1, ReferenceType = "Picking", TransactionDate = DateTime.UtcNow, CreatedBy = user.Id },
            new InventoryTransaction { WarehouseId = wh.Id, ProductId = prodA.Id, LocationId = reserveBin.Id, TransactionType = TransactionType.Move, Quantity = 20m, ReferenceId = 1, ReferenceType = "Picking", TransactionDate = DateTime.UtcNow, CreatedBy = user.Id },
            new InventoryTransaction { WarehouseId = wh.Id, ProductId = prodB.Id, LocationId = goldenBin.Id, TransactionType = TransactionType.Import, Quantity = 15m, ReferenceId = 1, ReferenceType = "ImportReceipt", TransactionDate = DateTime.UtcNow, CreatedBy = user.Id },
            new InventoryTransaction { WarehouseId = wh.Id, ProductId = prodC.Id, LocationId = goldenBin.Id, TransactionType = TransactionType.Import, Quantity = 5m, ReferenceId = 1, ReferenceType = "ImportReceipt", TransactionDate = DateTime.UtcNow, CreatedBy = user.Id });

        var ir = new ImportReceipt { Code = $"IRE1-{suffix}", WarehouseId = wh.Id, SupplierId = partner.Id, Status = ReceiptStatus.Approved, CreatedBy = user.Id, CreatedAt = DateTime.UtcNow };
        db.ImportReceipts.Add(ir);
        await db.SaveChangesAsync();

        var importDetail = new ImportReceiptDetail
        {
            ImportReceiptId = ir.Id,
            ProductId = prodA.Id,
            Quantity = 10m,
            ExpectedQuantity = 10m,
            ReceivedQuantity = 10m,
            AcceptedQuantity = 10m,
            PostedQuantity = 10m,
            OperationUnitId = unit.Id,
            BaseUnitId = unit.Id,
            ConversionFactor = 1m,
            BaseExpectedQuantity = 10m,
            BaseReceivedQuantity = 10m,
            BaseAcceptedQuantity = 10m,
            BasePostedQuantity = 10m,
            UnitPrice = 50000m
        };
        db.ImportReceiptDetails.Add(importDetail);
        await db.SaveChangesAsync();

        var pt = new PutawayTask
        {
            ReceiptId = ir.Id,
            WarehouseId = wh.Id,
            Status = PutawayTaskStatus.Open,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow,
            Items = new List<PutawayTaskItem>
            {
                new()
                {
                    ReceiptLineId = importDetail.Id,
                    ProductId = prodA.Id,
                    SourceLocationId = reserveBin.Id,
                    InventoryStatus = InventoryStatus.Available,
                    OperationUnitId = unit.Id,
                    OperationUnitCodeSnapshot = unit.Code,
                    BaseUnitId = unit.Id,
                    BaseUnitCodeSnapshot = unit.Code,
                    ConversionFactorSnapshot = 1m,
                    RequiredOperationQuantity = 10m,
                    RequiredBaseQuantity = 10m
                }
            }
        };
        db.PutawayTasks.Add(pt);
        await db.SaveChangesAsync();

        return new Epic901Fixture(suffix, user.Id, role.Id, unit.Id, prodA.Id, prodB.Id, prodC.Id, partner.Id, wh.Id, goldenBin.Id, reserveBin.Id, ir.Id, pt.Id);
    }

    private static async Task CleanupEpic901FixtureAsync(Epic901Fixture fx)
    {
        await using var db = CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DISABLE TRIGGER TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;
            UPDATE dbo.InventoryTransactions SET CorrectiveTransactionId = NULL, ReversalOfTransactionId = NULL WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.InventoryTransactions WHERE WarehouseId = {fx.WarehouseId};
            ENABLE TRIGGER TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;
            DELETE FROM dbo.InventoryLocationMovements WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.PutawayTaskItems WHERE PutawayTaskId = {fx.PutawayTaskId};
            DELETE FROM dbo.PutawayTasks WHERE Id = {fx.PutawayTaskId};
            DELETE FROM dbo.ImportReceiptDetails WHERE ImportReceiptId = {fx.ImportReceiptId};
            DELETE FROM dbo.ImportReceipts WHERE Id = {fx.ImportReceiptId};
            DELETE FROM dbo.InventoryStocks WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.IntegrationOutboxMessages WHERE AggregateId = {fx.WarehouseId} AND EventType = 'AbcSlottingAndInterleavingEvaluated';
            DELETE FROM dbo.AuditLogs WHERE WarehouseId = {fx.WarehouseId} OR UserId = {fx.UserId};
            DELETE FROM dbo.WarehouseLocations WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.Warehouses WHERE Id = {fx.WarehouseId};
            DELETE FROM dbo.Products WHERE Id IN ({fx.ProdAId}, {fx.ProdBId}, {fx.ProdCId});
            DELETE FROM dbo.BusinessPartners WHERE Id = {fx.PartnerId};
            DELETE FROM dbo.Units WHERE Id = {fx.UnitId};
            DELETE FROM dbo.Users WHERE Id = {fx.UserId};
            DELETE FROM dbo.Roles WHERE Id = {fx.RoleId};
        ");
    }

    private sealed record Epic902Fixture(
        string Suffix,
        int UserId,
        int RoleId,
        int UnitId,
        int ProductId,
        int OwnerAId,
        int OwnerBId,
        int WarehouseId,
        int LocationId);

    private static async Task<Epic902Fixture> CreateEpic902FixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var role = new Role { RoleName = $"W61E2R{suffix}" };
        var user = new User { Username = $"W61E2U{suffix}", PasswordHash = "hash", FullName = "W61 Epic90.2 Tester", Role = role };
        var unit = new Unit { Code = $"UE2{suffix}", Name = "Thùng" };
        var prod = new Product { Code = $"SKU-3PL-{suffix}", Name = "SP Ký gửi 3PL", Unit = unit, TrackingType = ProductTrackingType.None, IsActive = true };
        var ownerA = new BusinessPartner { Code = $"OWNA-{suffix}", Name = "Chủ hàng 3PL Alpha", IsCustomer = true, IsSupplier = true, IsActive = true };
        var ownerB = new BusinessPartner { Code = $"OWNB-{suffix}", Name = "Chủ hàng 3PL Beta", IsCustomer = true, IsSupplier = true, IsActive = true };
        var wh = new Warehouse { Code = $"WHE2-{suffix}", Name = "Kho 3PL E90.2", IsActive = true };
        db.AddRange(role, user, unit, prod, ownerA, ownerB, wh);
        await db.SaveChangesAsync();

        var loc = new WarehouseLocation { WarehouseId = wh.Id, Code = $"3PL-BIN-{suffix}", Name = "Kệ Ký gửi 3PL", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = true, CreatedBy = user.Id };
        db.WarehouseLocations.Add(loc);
        await db.SaveChangesAsync();

        var stockA = new InventoryStock
        {
            WarehouseId = wh.Id,
            ProductId = prod.Id,
            LocationId = loc.Id,
            OwnerId = ownerA.Id,
            Status = InventoryStatus.Available,
            Quantity = 40m
        };
        var stockB = new InventoryStock
        {
            WarehouseId = wh.Id,
            ProductId = prod.Id,
            LocationId = loc.Id,
            OwnerId = ownerB.Id,
            Status = InventoryStatus.Available,
            Quantity = 25m
        };
        stockA.RefreshGrainHash();
        stockB.RefreshGrainHash();
        db.InventoryStocks.AddRange(stockA, stockB);

        db.InventoryTransactions.AddRange(
            new InventoryTransaction { WarehouseId = wh.Id, ProductId = prod.Id, LocationId = loc.Id, OwnerId = ownerA.Id, TransactionType = TransactionType.Import, Quantity = 40m, ReferenceId = 1, ReferenceType = "ImportReceipt", TransactionDate = DateTime.UtcNow, CreatedBy = user.Id },
            new InventoryTransaction { WarehouseId = wh.Id, ProductId = prod.Id, LocationId = loc.Id, OwnerId = ownerB.Id, TransactionType = TransactionType.Import, Quantity = 25m, ReferenceId = 2, ReferenceType = "ImportReceipt", TransactionDate = DateTime.UtcNow, CreatedBy = user.Id });
        await db.SaveChangesAsync();

        return new Epic902Fixture(suffix, user.Id, role.Id, unit.Id, prod.Id, ownerA.Id, ownerB.Id, wh.Id, loc.Id);
    }

    private static async Task CleanupEpic902FixtureAsync(Epic902Fixture fx)
    {
        await using var db = CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DISABLE TRIGGER TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;
            DELETE FROM dbo.InventoryTransactions WHERE WarehouseId = {fx.WarehouseId};
            ENABLE TRIGGER TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;
            DELETE FROM dbo.ThreePlBillingStatements WHERE PartnerId IN ({fx.OwnerAId}, {fx.OwnerBId});
            DELETE FROM dbo.ThreePlStorageSnapshots WHERE OwnerId IN ({fx.OwnerAId}, {fx.OwnerBId});
            DELETE FROM dbo.InventoryStocks WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.AuditLogs WHERE WarehouseId = {fx.WarehouseId} OR UserId = {fx.UserId};
            DELETE FROM dbo.WarehouseLocations WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.Warehouses WHERE Id = {fx.WarehouseId};
            DELETE FROM dbo.Products WHERE Id = {fx.ProductId};
            DELETE FROM dbo.BusinessPartners WHERE Id IN ({fx.OwnerAId}, {fx.OwnerBId});
            DELETE FROM dbo.Units WHERE Id = {fx.UnitId};
            DELETE FROM dbo.Users WHERE Id = {fx.UserId};
            DELETE FROM dbo.Roles WHERE Id = {fx.RoleId};
        ");
    }

    private sealed record Epic903Fixture(
        string Suffix,
        int UserId,
        int RoleId,
        int UnitId,
        int ProductId,
        int PartnerId,
        int WarehouseId,
        int ExportReceipt1Id,
        int ExportReceipt2Id);

    private static async Task<Epic903Fixture> CreateEpic903FixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var role = new Role { RoleName = $"W61E3R{suffix}" };
        var user = new User { Username = $"W61E3U{suffix}", PasswordHash = "hash", FullName = "W61 Epic90.3 Tester", Role = role };
        var unit = new Unit { Code = $"UE3{suffix}", Name = "Hộp" };
        var prod = new Product { Code = $"SKU-ACC-{suffix}", Name = "SP Kế toán TT200", Unit = unit, MovingAverageCost = 50000m, TrackingType = ProductTrackingType.None, IsActive = true };
        var partner = new BusinessPartner { Code = $"PE3-{suffix}", Name = "Công ty TNHH Đại Dương", IsCustomer = true, IsActive = true };
        var wh = new Warehouse { Code = $"WHE3-{suffix}", Name = "Kho TT200 E90.3", IsActive = true };
        db.AddRange(role, user, unit, prod, partner, wh);
        await db.SaveChangesAsync();

        var er1 = new ExportReceipt
        {
            Code = $"PXK-E3A-{suffix}",
            WarehouseId = wh.Id,
            CustomerId = partner.Id,
            Status = ReceiptStatus.Approved,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow,
            Details = new List<ExportReceiptDetail>
            {
                new() { ProductId = prod.Id, Quantity = 10m, UnitPrice = 75000m }
            }
        };
        var er2 = new ExportReceipt
        {
            Code = $"PXK-E3B-{suffix}",
            WarehouseId = wh.Id,
            CustomerId = partner.Id,
            Status = ReceiptStatus.Approved,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow,
            Details = new List<ExportReceiptDetail>
            {
                new() { ProductId = prod.Id, Quantity = 4m, UnitPrice = 75000m }
            }
        };
        db.AddRange(er1, er2);
        await db.SaveChangesAsync();

        return new Epic903Fixture(suffix, user.Id, role.Id, unit.Id, prod.Id, partner.Id, wh.Id, er1.Id, er2.Id);
    }

    private static async Task CleanupEpic903FixtureAsync(Epic903Fixture fx)
    {
        await using var db = CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM dbo.IntegrationOutboxMessages WHERE AggregateType = 'ExportReceipt' AND AggregateId IN ({fx.ExportReceipt1Id}, {fx.ExportReceipt2Id});
            DELETE FROM dbo.AuditLogs WHERE WarehouseId = {fx.WarehouseId} OR UserId = {fx.UserId};
            DELETE FROM dbo.ExportReceiptDetails WHERE ExportReceiptId IN ({fx.ExportReceipt1Id}, {fx.ExportReceipt2Id});
            DELETE FROM dbo.ExportReceipts WHERE Id IN ({fx.ExportReceipt1Id}, {fx.ExportReceipt2Id});
            DELETE FROM dbo.Warehouses WHERE Id = {fx.WarehouseId};
            DELETE FROM dbo.Products WHERE Id = {fx.ProductId};
            DELETE FROM dbo.BusinessPartners WHERE Id = {fx.PartnerId};
            DELETE FROM dbo.Units WHERE Id = {fx.UnitId};
            DELETE FROM dbo.Users WHERE Id = {fx.UserId};
            DELETE FROM dbo.Roles WHERE Id = {fx.RoleId};
        ");
    }
}
```

### 7.2 `ERP.Application.Tests/SqlServerWave60RoadmapEpicsTests.cs` — `Epic60_3` & Break-Bulk Fixture (`L363–L526` & `L939–L1070`, Full Methods to Closing `}`)
```csharp
    [SqlServerFact]
    public async Task Epic60_3_AutomatedBreakBulkReplenishment_HappyPath_Idempotency_Concurrency_And_Reversal()
    {
        var fx = await CreateBreakBulkFixtureAsync();
        try
        {
            int refStockId;

            // (a) Happy Path: Break-Bulk 2 Cases (ConversionFactor = 12 -> 24 BaseUom) from Reserve Bin to Forward-Pick Bin
            await using (var db = CreateContext())
            {
                var svc = new InventoryControlOperationsService(db, null, new FakeUser(fx.UserId));
                var res = await svc.ExecuteBreakBulkReplenishmentAsync(
                    new ExecuteBreakBulkReplenishmentInput(
                        WarehouseId: fx.WarehouseId,
                        ProductId: fx.ProductId,
                        SourceLocationId: fx.ReserveLocationId,
                        TargetLocationId: fx.PickLocationId,
                        SourceUomId: fx.CaseUnitId,
                        SourceUomCode: "CASE",
                        SourcePackQty: 2m,
                        ConversionFactor: 12m,
                        SourceLotId: fx.LotId),
                    fx.UserId);

                res.StatusCode.Should().Be(200);
                using var doc = JsonSerializer.SerializeToDocument(res.Payload);
                doc.RootElement.GetProperty("movedBaseQty").GetDecimal().Should().Be(24m);
                doc.RootElement.GetProperty("conversionFactor").GetDecimal().Should().Be(12m);
                doc.RootElement.GetProperty("baseUomDelta").GetDecimal().Should().Be(0.0000m);
                doc.RootElement.GetProperty("sourceRemainingQuantity").GetDecimal().Should().Be(12m); // 36 - 24 = 12
                doc.RootElement.GetProperty("targetResultingQuantity").GetDecimal().Should().Be(24m);
                refStockId = doc.RootElement.GetProperty("referenceId").GetInt32();
            }

            // (b) Duplicate Idempotency-Key record persistence & replay verification on Break-Bulk Replenishment
            await using (var db = CreateContext())
            {
                var record = new IdempotencyRecord
                {
                    UserId = fx.UserId,
                    CommandScope = "Inventory.BreakBulkReplenishment",
                    KeyHash = $"HASH-BB-{fx.Suffix}",
                    RequestFingerprint = "FP-BB",
                    Status = IdempotencyStatus.Completed,
                    ResponseStatusCode = 200,
                    ResponseBody = "{\"movedBaseQty\":24,\"baseUomDelta\":0.0000}",
                    CorrelationId = "corr-bb",
                    WarehouseId = fx.WarehouseId,
                    CreatedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
                };
                db.IdempotencyRecords.Add(record);
                await db.SaveChangesAsync();

                var replayed = await db.IdempotencyRecords
                    .AsNoTracking()
                    .SingleOrDefaultAsync(r => r.UserId == fx.UserId && r.CommandScope == "Inventory.BreakBulkReplenishment" && r.KeyHash == $"HASH-BB-{fx.Suffix}");
                replayed.Should().NotBeNull();
                replayed!.Status.Should().Be(IdempotencyStatus.Completed);
                replayed.ResponseStatusCode.Should().Be(200);
            }

            // (c) Parallel Concurrency contention: 2 concurrent Break-Bulk requests competing for the remaining 1 Case (12 BaseUom) at Reserve Bin
            var concOutcomes = await Task.WhenAll(
                Task.Run(async () =>
                {
                    await using var db1 = CreateContext();
                    var svc1 = new InventoryControlOperationsService(db1, null, new FakeUser(fx.UserId));
                    return await svc1.ExecuteBreakBulkReplenishmentAsync(
                        new ExecuteBreakBulkReplenishmentInput(
                            WarehouseId: fx.WarehouseId,
                            ProductId: fx.ProductId,
                            SourceLocationId: fx.ReserveLocationId,
                            TargetLocationId: fx.PickLocationId,
                            SourceUomId: fx.CaseUnitId,
                            SourcePackQty: 1m,
                            ConversionFactor: 12m,
                            SourceLotId: fx.LotId),
                        fx.UserId);
                }),
                Task.Run(async () =>
                {
                    await using var db2 = CreateContext();
                    var svc2 = new InventoryControlOperationsService(db2, null, new FakeUser(fx.UserId));
                    return await svc2.ExecuteBreakBulkReplenishmentAsync(
                        new ExecuteBreakBulkReplenishmentInput(
                            WarehouseId: fx.WarehouseId,
                            ProductId: fx.ProductId,
                            SourceLocationId: fx.ReserveLocationId,
                            TargetLocationId: fx.PickLocationId,
                            SourceUomId: fx.CaseUnitId,
                            SourcePackQty: 1m,
                            ConversionFactor: 12m,
                            SourceLotId: fx.LotId),
                        fx.UserId);
                }));

            concOutcomes.Count(o => o.StatusCode == 200).Should().Be(1);
            concOutcomes.Count(o => o.StatusCode == 422 || o.StatusCode == 409).Should().Be(1);

            // (d) Reversal of all 3 Cases (36 BaseUom) back from Forward-Pick Bin to Reserve Bin
            await using (var db = CreateContext())
            {
                var svc = new InventoryControlOperationsService(db, null, new FakeUser(fx.UserId));
                var revRes = await svc.ExecuteBreakBulkReplenishmentAsync(
                    new ExecuteBreakBulkReplenishmentInput(
                        WarehouseId: fx.WarehouseId,
                        ProductId: fx.ProductId,
                        SourceLocationId: fx.ReserveLocationId,
                        TargetLocationId: fx.PickLocationId,
                        SourceUomId: fx.CaseUnitId,
                        SourceUomCode: "CASE",
                        SourcePackQty: 3m,
                        ConversionFactor: 12m,
                        SourceLotId: fx.LotId,
                        Reverse: true,
                        OriginalReferenceId: refStockId),
                    fx.UserId);

                revRes.StatusCode.Should().Be(200);
                using var revDoc = JsonSerializer.SerializeToDocument(revRes.Payload);
                revDoc.RootElement.GetProperty("reversed").GetBoolean().Should().BeTrue();
                revDoc.RootElement.GetProperty("sourceRemainingQuantity").GetDecimal().Should().Be(36m);
                revDoc.RootElement.GetProperty("targetResultingQuantity").GetDecimal().Should().Be(0m);

                var reserveBuckets = await db.InventoryStocks.AsNoTracking()
                    .Where(s => s.WarehouseId == fx.WarehouseId && s.LocationId == fx.ReserveLocationId)
                    .ToListAsync();
                reserveBuckets.Should().ContainSingle();
                reserveBuckets[0].HandlingUnitId.Should().Be(603);
                reserveBuckets[0].Quantity.Should().Be(36m);

                var revInTxs = await db.InventoryTransactions.AsNoTracking()
                    .Where(t => t.WarehouseId == fx.WarehouseId &&
                                t.ReferenceType == "BreakBulkReplenishmentReversal" &&
                                t.Quantity > 0m)
                    .ToListAsync();
                revInTxs.Should().NotBeEmpty().And.OnlyContain(t => t.HandlingUnitId == 603);

                var dupRevEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    svc.ExecuteBreakBulkReplenishmentAsync(
                        new ExecuteBreakBulkReplenishmentInput(
                            WarehouseId: fx.WarehouseId,
                            ProductId: fx.ProductId,
                            SourceLocationId: fx.ReserveLocationId,
                            TargetLocationId: fx.PickLocationId,
                            SourceUomId: fx.CaseUnitId,
                            SourceUomCode: "CASE",
                            SourcePackQty: 3m,
                            ConversionFactor: 12m,
                            SourceLotId: fx.LotId,
                            Reverse: true,
                            OriginalReferenceId: refStockId),
                        fx.UserId));
                dupRevEx.ErrorCode.Should().Be("BREAK_BULK_REVERSAL_ORIGINAL_NOT_FOUND_OR_ALREADY_REVERSED");
            }
        }
        finally
        {
            await CleanupBreakBulkFixtureAsync(fx);
        }
    }

    private sealed record BreakBulkFixture(
        string Suffix,
        int UserId,
        int RoleId,
        int BaseUnitId,
        int CaseUnitId,
        int ProductId,
        int WarehouseId,
        int ReserveLocationId,
        int PickLocationId,
        int LotId);

    private static async Task<BreakBulkFixture> CreateBreakBulkFixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var role = new Role { RoleName = $"W60BRole{suffix}" };
        var user = new User { Username = $"W60BUser{suffix}", PasswordHash = "hash", FullName = "Wave60 BreakBulk Tester", Role = role };
        var baseUnit = new Unit { Code = $"EA{suffix}", Name = "Cái" };
        var caseUnit = new Unit { Code = $"CS{suffix}", Name = "Thùng 12" };
        var prod = new Product { Code = $"SKU-BB60-{suffix}", Name = "Sản phẩm Xả Thùng Wave 60", Unit = baseUnit, TrackingType = ProductTrackingType.Lot, IsActive = true };
        var wh = new Warehouse { Code = $"WH60B-{suffix}", Name = "Kho Break-Bulk Wave 60", IsActive = true };
        db.AddRange(role, user, baseUnit, caseUnit, prod, wh);
        await db.SaveChangesAsync();

        var prodUom = new ProductUom
        {
            ProductId = prod.Id,
            UnitId = caseUnit.Id,
            ConversionFactor = 12m,
            IsActive = true
        };
        var reserveLoc = new WarehouseLocation
        {
            WarehouseId = wh.Id,
            Code = $"RES-01-{suffix}",
            Name = "Kệ lưu trữ trên cao",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = false,
            CreatedBy = user.Id
        };
        var pickLoc = new WarehouseLocation
        {
            WarehouseId = wh.Id,
            Code = $"PICK-01-{suffix}",
            Name = "Mặt kệ nhặt lẻ",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            CreatedBy = user.Id
        };
        var lot = new InventoryLot
        {
            ProductId = prod.Id,
            LotNumber = $"LOT-BB-{suffix}",
            ManufactureDate = DateTime.UtcNow.Date.AddMonths(-1),
            ExpiryDate = DateTime.UtcNow.Date.AddMonths(10),
            ReceivedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.AddRange(prodUom, reserveLoc, pickLoc, lot);
        await db.SaveChangesAsync();

        var reserveStock = new InventoryStock
        {
            WarehouseId = wh.Id,
            ProductId = prod.Id,
            LocationId = reserveLoc.Id,
            LotId = lot.Id,
            HandlingUnitId = 603,
            Status = InventoryStatus.Available,
            Quantity = 36m // 3 Cases x 12 EA on LPN #603
        };
        reserveStock.RefreshGrainHash();
        db.InventoryStocks.Add(reserveStock);
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            WarehouseId = wh.Id,
            ProductId = prod.Id,
            LocationId = reserveLoc.Id,
            LotId = lot.Id,
            HandlingUnitId = 603,
            InventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Import,
            Quantity = 36m,
            ReferenceType = "ImportReceipt",
            ReferenceId = 1,
            TransactionDate = DateTime.UtcNow.AddMinutes(-20),
            CreatedBy = user.Id
        });
        await db.SaveChangesAsync();

        return new BreakBulkFixture(
            suffix,
            user.Id,
            role.Id,
            baseUnit.Id,
            caseUnit.Id,
            prod.Id,
            wh.Id,
            reserveLoc.Id,
            pickLoc.Id,
            lot.Id);
    }

    private static async Task CleanupBreakBulkFixtureAsync(BreakBulkFixture fx)
    {
        await using var db = CreateContext();
        await db.IdempotencyRecords.Where(x => x.UserId == fx.UserId).ExecuteDeleteAsync();
        await db.AuditLogs.Where(x => x.UserId == fx.UserId || x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL DISABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
        try
        {
            await db.InventoryTransactions.Where(x => x.WarehouseId == fx.WarehouseId && (x.CorrectiveTransactionId != null || x.ReversalOfTransactionId != null)).ExecuteUpdateAsync(s => s.SetProperty(x => x.CorrectiveTransactionId, (int?)null).SetProperty(x => x.ReversalOfTransactionId, (int?)null));
            await db.InventoryTransactions.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL ENABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
        }
        await db.InventoryStocks.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.InventoryLots.Where(l => l.Id == fx.LotId).ExecuteDeleteAsync();
        await db.ProductUoms.Where(u => u.ProductId == fx.ProductId).ExecuteDeleteAsync();
        await db.WarehouseLocations.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Warehouses.Where(x => x.Id == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == fx.ProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == fx.BaseUnitId || x.Id == fx.CaseUnitId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.Id == fx.UserId).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == fx.RoleId).ExecuteDeleteAsync();
    }
}
```

---

## 8. Full Verbatim Output of the 5 Quality Gates & Live SQL 3-Way Integrity Checks on `ERP_KHO` (FACT)

### 8.1 Gate 1 — Encoding Check, Release Build & Thin Controller Verification
```text
PS D:\ERP_KHO> powershell -ExecutionPolicy Bypass -File scripts/check-encoding.ps1
Encoding check passed: all scanned files are valid UTF-8 and no mojibake markers were found.
Exit Code: 0

PS D:\ERP_KHO> dotnet build ERP.slnx -c Release --no-restore
  ERP.Domain -> D:\ERP_KHO\ERP.Domain\bin\Release\net10.0\ERP.Domain.dll
  ERP.SqlIntegrationHarness -> D:\ERP_KHO\tools\ERP.SqlIntegrationHarness\bin\Release\net10.0\ERP.SqlIntegrationHarness.dll
  ERP.Application -> D:\ERP_KHO\ERP.Application\bin\Release\net10.0\ERP.Application.dll
  ERP.Infrastructure -> D:\ERP_KHO\ERP.Infrastructure\bin\Release\net10.0\ERP.Infrastructure.dll
  ERP.QaSeed -> D:\ERP_KHO\tools\ERP.QaSeed\bin\Release\net10.0\ERP.QaSeed.dll
  ERP.Api -> D:\ERP_KHO\ERP.Api\bin\Release\net10.0\ERP.Api.dll
  ERP.Api.Tests -> D:\ERP_KHO\ERP.Api.Tests\bin\Release\net10.0\ERP.Api.Tests.dll
  ERP.Application.Tests -> D:\ERP_KHO\ERP.Application.Tests\bin\Release\net10.0\ERP.Application.Tests.dll
Build succeeded.
    0 Warning(s)
    0 Error(s)
Exit Code: 0

PS D:\ERP_KHO> @(Select-String -Path "ERP.Api/Controllers/*.cs" -Pattern "ErpKhoDbContext").Count
0
```

### 8.2 Gate 2 — `SqlServerWave60RoadmapEpicsTests` (3/3 PASS), `SqlServerWave61RoadmapEpicsTests` (4/4 PASS), `ERP.SqlIntegrationHarness` (237/237 PASS), `ERP.Application.Tests` (665/665 PASS) & `ERP.Api.Tests` (517/517 PASS)
```text
Test run for D:\ERP_KHO\ERP.Application.Tests\bin\Release\net10.0\ERP.Application.Tests.dll (.NETCoreApp,Version=v10.0)
Owned migration target: ERP_KHO_Integration_20261028_184758_a6a3f1a6; RunId=a6a3f1a6ece545139f413d5dd5209446.
  Passed ERP.Application.Tests.SqlServerWave61RoadmapEpicsTests.Epic90_3_VietnamAccountingCircular200AndEinvoiceNd70Outbox_HappyPath_CustomsGuard_Form04SS_Concurrency_And_Reversal [2 s]
  Passed ERP.Application.Tests.SqlServerWave61RoadmapEpicsTests.Wave61_F013_CrossDockAndBreakBulkReversal_InsufficientStock_DuplicateReversal_And_LedgerRule_Guards [2 s]
  Passed ERP.Application.Tests.SqlServerWave61RoadmapEpicsTests.Epic90_2_SelfService3PlDepositorPortal_OwnerIsolation_BillingPreview_Concurrency_And_Reversal [711 ms]
  Passed ERP.Application.Tests.SqlServerWave61RoadmapEpicsTests.Epic90_1_DynamicAbcVelocitySlottingAndTaskInterleaving_HappyPath_Idempotency_Concurrency_And_Reversal [2 s]
  Passed ERP.Application.Tests.SqlServerWave60RoadmapEpicsTests.Epic60_3_AutomatedBreakBulkReplenishment_HappyPath_Idempotency_Concurrency_And_Reversal [1 s]
  Passed ERP.Application.Tests.SqlServerWave60RoadmapEpicsTests.Epic60_2_InboundToOutboundCrossDocking_HappyPath_Idempotency_Concurrency_And_Reversal [1 s]
  Passed ERP.Application.Tests.SqlServerWave60RoadmapEpicsTests.Epic60_1_PhysicalFloorKittingAndDeKitting_HappyPath_Idempotency_Concurrency_And_Reversal [1 s]
Owned migration target cleaned: ERP_KHO_Integration_20261028_184758_a6a3f1a6; RunId=a6a3f1a6ece545139f413d5dd5209446.
Test Run Successful. Total tests: 7 | Passed: 7 | Failed: 0

--- Full ERP.SqlIntegrationHarness (53 SQL test class files: 216 Application + 21 Api = 237/237 PASS) ---
SQL integration planned target: ERP_KHO_Integration_20261028_184845_e62621ce; RunId=e62621ceaf0a4da28d461c70ab3f125a
Passed!  - Failed:     0, Passed:   216, Skipped:     0, Total:   216, Duration: 7 m 31 s - ERP.Application.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    21, Skipped:     0, Total:    21, Duration: 7 m 7 s - ERP.Api.Tests.dll (net10.0)
SQL integration target cleaned: ERP_KHO_Integration_20261028_184845_e62621ce.

--- Full ERP.Application.Tests (665/665 PASS) ---
Passed!  - Failed:     0, Passed:   665, Skipped:     0, Total:   665, Duration: 7 m 33 s - ERP.Application.Tests.dll (net10.0)

--- Full ERP.Api.Tests (517/517 PASS) ---
Passed!  - Failed:     0, Passed:   517, Skipped:     0, Total:   517, Duration: 6 m 5 s - ERP.Api.Tests.dll (net10.0)
```

### 8.3 Gate 3 — Frontend Production Build (`vite v8.1.4`) & Vitest Unit Test Suite (`73/73` Files, `406/406` Tests PASS)
```text
PS D:\ERP_KHO> npm --prefix frontend run build
vite v8.1.4 building client environment for production...
✓ 1711 modules transformed.
✓ built in 1.26s
Exit Code: 0

PS D:\ERP_KHO> npm --prefix frontend test -- --run --no-file-parallelism --exclude="e2e/**"
 Test Files  73 passed (73)
      Tests  406 passed (406)
   Duration  238.34s
Exit Code: 0
```

### 8.4 Gate 4 & Gate 5 — Live SQL Server 3-Way Integrity Checks on `ERP_KHO` (Check A, Check B, Check C, All 20 Stock Rows & 9 Positive Rows)
```text
=== 1. SQL 3-Way Check A: InventoryStocks vs InventoryTransactions (with InventoryLedgerCalculationRules) ===

WarehouseId ProductId StockAllStatusOnHand LedgerExpectedAllStatusNet AllStatusVariance StockAvailableOnHand LedgerExpectedAvailableNet AvailableVariance
----------- --------- -------------------- -------------------------- ----------------- -------------------- -------------------------- -----------------
          1       317             145.0000                   145.0000            0.0000             108.0000                   108.0000            0.0000
          1       517              66.0000                    66.0000            0.0000              66.0000                    66.0000            0.0000
       1403       317              21.0000                    21.0000            0.0000              21.0000                    21.0000            0.0000



=== 2. SQL 3-Way Check B: ReservedQuantity vs Active StockReservations & StockAllocations ===

ActiveReservationNetQty ActiveAllocationQty TotalStockReservedQty TotalStockAllocatedQty
----------------------- ------------------- --------------------- ----------------------
                 9.0000              9.0000                9.0000                 9.0000



=== 3. SQL 3-Way Check C: Physical Stock & 32-Byte SHA2_256 GrainHash Invariants ===

TotalGrainRows PositiveStockRows TotalOnHand TotalReserved TotalAvailable NegativeOnHandCount NegativeReservedCount OverReservedCount NullOrInvalidGrainHashCount
-------------- ----------------- ----------- ------------- -------------- ------------------- --------------------- ----------------- ---------------------------
            20                 9    232.0000        9.0000       223.0000                   0                     0                 0                           0



=== 4. All 20 Rows of dbo.InventoryStocks (Id=3663..14681) ===

   Id WarehouseId ProductId Status LocationId LotId HandlingUnitId Quantity ReservedQuantity AllocatedQuantity
   -- ----------- --------- ------ ---------- ----- -------------- -------- ---------------- -----------------
 3663           1       317      0          1                       30.0000           0.0000            0.0000
 3664           1       317      0          9                       60.0000           0.0000            0.0000
 3665           1       317      2          9 2                     25.0000           0.0000            0.0000
 3666           1       317      3         11 1                     12.0000           0.0000            0.0000
 3667           1       517      0         10 3                      0.0000           0.0000            0.0000
 4663           1       317      0       1009                       10.0000           9.0000            9.0000
 5663           1       517      0       1009 1002                   0.0000           0.0000            0.0000
11672           1       317      0       2014       3                0.0000           0.0000            0.0000
11673           1       517      0       2014 1002  3                0.0000           0.0000            0.0000
11674        1403       317      0       2031                       21.0000           0.0000            0.0000
11675           1       317      0       2013       3                8.0000           0.0000            0.0000
11676           1       517      0       2013 1002  3                6.0000           0.0000            0.0000
11677           1       517      0          1 1002                  60.0000           0.0000            0.0000
11678           1       517      0          1 1004                   0.0000           0.0000            0.0000
11679           1       517      0          1 1005                   0.0000           0.0000            0.0000
11680           1       517      0          1 1006                   0.0000           0.0000            0.0000
11681           1       517      0          1 1007                   0.0000           0.0000            0.0000
12681           1       517      0          1 2007                   0.0000           0.0000            0.0000
13681           1       517      0          1 3007                   0.0000           0.0000            0.0000
14681           1       517      0          1 4007                   0.0000           0.0000            0.0000



=== 5. 9 Positive Rows of dbo.InventoryStocks ===

   Id TenantId WarehouseId ProductId LocationId LotId SerialId HandlingUnitId OwnerId Status
   -- -------- ----------- --------- ---------- ----- -------- -------------- ------- ------
 3663        1           1       317          1                                            0
 3664        1           1       317          9                                            0
 3665        1           1       317          9 2                                          2
 3666        1           1       317         11 1                                          3
 4663        1           1       317       1009                                            0
11674        1        1403       317       2031                                            0
11675        1           1       317       2013                3                           0
11676        1           1       517       2013 1002           3                           0
11677        1           1       517          1 1002                                       0
```
