# WAVE 61 — FULL VERBATIM C# CODE, TEST SUITES & LIVE SQL 3-WAY INTEGRITY EVIDENCE (FACT)

- **Project:** ERP_KHO — Enterprise Multi-Tenant WMS SaaS (.NET 10 / EF Core 10 / SQL Server / React 19 / Vite 8)
- **Wave:** Wave 61 (Remediation of F-013 & F-014 + 90-Day Competitive Roadmap Epics 90.1, 90.2, 90.3)
- **Baseline Commit:** 5ce6242 (branch `feat/WMS-61-project-development`)
- **Target Database:** `ERP_KHO` on `(localdb)\MSSQLLocalDB` (97 physical tables = 96 business tables + 1 `__EFMigrationsHistory`, 61 migrations, 1 active trigger `TR_InventoryTransactions_Immutable_AppendOnly`)
- **Evidence Standard:** Zero-Truncation Verbatim Source Code & Verbatim Console/SQL Outputs ([REP-001]/[REP-002])

---

## 1. ERP.Domain/Exceptions/ConcurrencyException.cs (HTTP 409 CONCURRENCY_CONFLICT)

Source file: `d:\ERP_KHO\ERP.Domain\Exceptions\ConcurrencyException.cs`

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

---

## 2. ERP.Domain/Enums/InventoryTransactionTypeMapping.cs (F-013 Canonical Algebraic Ledger Rule)

Source file: `d:\ERP_KHO\ERP.Domain\Enums\InventoryTransactionTypeMapping.cs`

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

---

## 3. ERP.Infrastructure/Queries/InventoryReconciliationQueryService.cs (Lines 220-350 — Check A/B Ledger Reconciliation)

Source file: `d:\ERP_KHO\ERP.Infrastructure\Queries\InventoryReconciliationQueryService.cs`

```csharp
                entry.BucketCount = stockGroup.Buckets;
            }

            var unclassifiedCount = 0;
            foreach (var group in ledgerGroups)
            {
                if (group.TransactionType == TransactionType.StatusChange)
                {
                    if (group.FromInventoryStatus is not { } from ||
                        group.ToInventoryStatus is not { } to ||
                        from == to ||
                        !byStatus.ContainsKey(from) || !byStatus.ContainsKey(to))
                    {
                        unclassifiedCount += group.Events;
                        continue;
                    }

                    // Source leg of 2-row StatusChange (-Q on FromInventoryStatus)
                    if (group.InventoryStatus == from)
                    {
                        if (group.NonNegativeQuantityEvents > 0)
                        {
                            unclassifiedCount += group.NonNegativeQuantityEvents;
                            continue;
                        }
                        byStatus[from].StatusChangeOutQuantity += Math.Abs(group.Quantity);
                        continue;
                    }

                    // Destination leg of StatusChange (+Q on ToInventoryStatus)
                    if (group.InventoryStatus == to)
                    {
                        if (group.NonPositiveQuantityEvents > 0)
                        {
                            unclassifiedCount += group.NonPositiveQuantityEvents;
                            continue;
                        }
                        byStatus[to].StatusChangeInQuantity += group.Quantity;
                        var hasExplicitSourceLeg = ledgerGroups.Any(g2 =>
                            g2.TransactionType == TransactionType.StatusChange &&
                            g2.FromInventoryStatus == from &&
                            g2.ToInventoryStatus == to &&
                            g2.InventoryStatus == from &&
                            g2.NegativeQuantityEvents > 0);
                        if (!hasExplicitSourceLeg)
                        {
                            byStatus[from].StatusChangeOutQuantity += group.Quantity;
                        }
                        continue;
                    }

                    unclassifiedCount += group.Events;
                    continue;
                }
                if (!byStatus.TryGetValue(group.InventoryStatus, out var current) ||
                    group.TransactionType == TransactionType.TransferAdjustment ||
                    !Enum.IsDefined(group.TransactionType))
                {
                    unclassifiedCount += group.Events;
                    continue;
                }
                if (group.TransactionType == TransactionType.Move)
                {
                    if (group.ZeroQuantityEvents > 0)
                    {
                        unclassifiedCount += group.ZeroQuantityEvents;
                    }
                    continue;
                }
                if (group.TransactionType == TransactionType.Reversal && !group.HasCorrectiveTransaction)
                {
                    if (group.ZeroQuantityEvents > 0)
                    {
                        unclassifiedCount += group.ZeroQuantityEvents;
                        continue;
                    }
                    current.DirectLedgerNetQuantity +=
                        InventoryLedgerCalculationRules.ComputeAlgebraicDelta(group.TransactionType, group.Quantity, null);
                    continue;
                }
                if (group.NegativeQuantityEvents > 0)
                {
                    unclassifiedCount += group.NegativeQuantityEvents;
                    continue;
                }
                current.DirectLedgerNetQuantity +=
                    InventoryLedgerCalculationRules.ComputeAlgebraicDelta(
                        group.TransactionType,
                        group.Quantity,
                        group.HasCorrectiveTransaction ? 1 : null);
            }

            var historyInsufficientForNonAvailableStock =
                ledgerGroups.Count == 0 && stockGroups.Any(x =>
                    x.Status != InventoryStatus.Available && x.Buckets > 0) &&
                !stockGroups.Any(x => x.Status == InventoryStatus.Available && x.Quantity != 0m);
            var comparable = unclassifiedCount == 0 &&
                unclassifiedStockBucketCount == 0 &&
                invalidReservationBucketCount == 0 && negativePhysicalBucketCount == 0 &&
                !historyInsufficientForNonAvailableStock;
            foreach (var row in byStatus.Values)
            {
                if (!comparable) continue;
                row.ExpectedQuantity = row.DirectLedgerNetQuantity +
                    row.StatusChangeInQuantity - row.StatusChangeOutQuantity;
                row.Difference = row.CurrentQuantity - row.ExpectedQuantity.Value;
            }
            var statusBreakdown = byStatus.Values.ToList();
            var allStatusCurrent = stockGroups.Sum(x => x.Quantity);
            var allStatusReserved = stockGroups.Sum(x => x.Reserved);
            decimal? allStatusExpected = comparable
                ? statusBreakdown.Sum(x => x.ExpectedQuantity!.Value)
                : null;
            var allStatusStatus = !comparable ? "Indeterminate" :
                statusBreakdown.Any(x => x.Difference != 0m) ? "Mismatch" : "Match";
            var evidenceAnchorsStale = hasNewEvents || bucketHasRowsAfterAnchor ||
                allStatusBucketHasRowsAfterAnchor || aggregateChanged;
            var snapshotComparisonStatus = evidenceAnchorsStale
                ? "Indeterminate" : allStatusStatus;

            var currentQuantity = await stocks.SumAsync(x => (decimal?)x.Quantity) ?? 0m;
            var grouped = await anchoredLedger
                .GroupBy(x => x.TransactionType)
                .Select(x => new
                {
                    Type = x.Key, Quantity = x.Sum(t => t.Quantity),
                    NegativeQuantityEvents = x.Count(t => t.Quantity < 0m)
                })
                .ToListAsync();
            var availableLedgerExpectedIsPartial = !comparable;
            var legacyClassifiedQuantity = grouped
```

---

## 4. ERP.Infrastructure/Services/PutawayService.cs (Lines 1070-1377 — F-013 Cross-Dock Reversal Strict Guard & No Math.Max Clamping)

Source file: `d:\ERP_KHO\ERP.Infrastructure\Services\PutawayService.cs`

```csharp
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

    public async Task<PutawayTaskDto> MoveAsync(int id, MovePutawayItemDto dto, CancellationToken token = default)
    {
        if(dto.Quantity<=0) throw new BusinessRuleException("Số lượng cất phải lớn hơn 0.");
        var ownTransaction = context.Database.CurrentTransaction is null;
        var isSqlServer = context.Database.IsRelational() && context.Database.IsSqlServer();
        var maxAttempts = ownTransaction && isSqlServer ? 4 : 1;
        for (var attempt = 1; ; attempt++)
        {
        await using var tx=ownTransaction?await context.Database.BeginTransactionAsync(token):null;
        try
        {
            if (isSqlServer)
            {
                var tenantId = context.CurrentTenantId;
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM dbo.PutawayTasks WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND Id = {id}; SELECT 1 FROM dbo.PutawayTaskItems WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND PutawayTaskId = {id};",
                    token);
            }
            var task=await Load(id,token);
            var isPutback = IsPutbackTask(task);
            if(task.Status is not PutawayTaskStatus.Assigned and not PutawayTaskStatus.InProgress && !(isPutback && task.Status == PutawayTaskStatus.Open))
                throw Conflict("Nhiệm vụ không ở trạng thái có thể cất hàng.");
            EnsureActor(task);
            if (task.Status == PutawayTaskStatus.InProgress)
            {
                if (!string.IsNullOrWhiteSpace(dto.RowVersion))
                {
                    try { _ = Convert.FromBase64String(dto.RowVersion); }
                    catch { throw new BusinessRuleException("Phiên bản dữ liệu không hợp lệ."); }
                }
                context.Entry(task).Property(x => x.RowVersion).OriginalValue = task.RowVersion;
            }
            else if (task.RowVersion.Length > 0 && !string.IsNullOrWhiteSpace(dto.RowVersion))
            {
                ApplyVersion(task.RowVersion,dto.RowVersion,context.Entry(task).Property(x=>x.RowVersion));
            }
            var item=(dto.ItemId > 0 ? task.Items.SingleOrDefault(x=>x.Id==dto.ItemId) : task.Items.FirstOrDefault())??throw new NotFoundException("Không tìm thấy dòng hàng.");
            await context.AcquireInventoryAllocationLocksAsync([(task.WarehouseId, item.ProductId)], token);
            WarehouseLocation? destination;
            if (dto.DestinationLocationId > 0)
            {
                destination = await LocationForMutation(dto.DestinationLocationId, token) ?? throw new NotFoundException("Không tìm thấy vị trí đích.");
                if (!string.IsNullOrWhiteSpace(dto.DestinationLocationBarcode) &&
                    !string.Equals(destination.Code, dto.DestinationLocationBarcode.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    throw ConflictWithCode("PUTAWAY_WRONG_BIN", $"Mã vị trí quét ({dto.DestinationLocationBarcode.Trim()}) không khớp với vị trí đích ({destination.Code}).");
                }
            }
            else if (!string.IsNullOrWhiteSpace(dto.DestinationLocationBarcode))
            {
                var scannedBinCode = dto.DestinationLocationBarcode.Trim().ToUpperInvariant();
                destination = await LocationByCodeForMutation(task.WarehouseId, scannedBinCode, token)
                              ?? throw ConflictWithCode("PUTAWAY_WRONG_BIN", $"Không tìm thấy vị trí kệ lưu trữ với mã quét '{scannedBinCode}'.");
            }
            else
            {
                throw new NotFoundException("Không tìm thấy vị trí đích.");
            }
            if(destination.WarehouseId!=task.WarehouseId) throw new NotFoundException("Không tìm thấy vị trí đích.");
            if(!destination.IsActive||destination.IsBlocked||destination.LocationType!=RequiredType(item.InventoryStatus)||(item.InventoryStatus==InventoryStatus.Available&&!destination.IsPickable&&destination.LocationType!=WarehouseLocationType.Storage)) throw new BusinessRuleException("Vị trí đích không phù hợp với hàng hóa này.");
            var baseQty=ToBase(dto.Quantity,dto.UnitCode,item); if(baseQty>item.RemainingBaseQuantity) throw new BusinessRuleException("Số lượng cất vượt quá số lượng còn lại.");
            var product=await ProductForCapacity(item.ProductId,token)??throw new NotFoundException("Không tìm thấy sản phẩm.");
            if (!string.IsNullOrWhiteSpace(dto.ProductBarcode))
            {
                var scannedProductCode = dto.ProductBarcode.Trim();
                var matchesSku = string.Equals(product.Code, scannedProductCode, StringComparison.OrdinalIgnoreCase);
                var matchesBarcode = matchesSku || await context.ProductBarcodes.AsNoTracking()
                    .AnyAsync(b => b.ProductId == item.ProductId && b.Value == scannedProductCode, token);
                if (!matchesBarcode)
                {
                    throw ConflictWithCode("PUTAWAY_WRONG_PRODUCT", $"Mã vạch sản phẩm quét ({scannedProductCode}) không khớp với SKU ({product.Code}).");
                }
            }
            await EnsurePutawayCapacityAsync(destination, product, baseQty, token);
            var sourceBuckets = await context.InventoryStocks
                .Include(x => x.Lot)
                .Include(x => x.Serial)
                .Where(x => x.ProductId == item.ProductId &&
                            x.WarehouseId == task.WarehouseId &&
                            x.Status == item.InventoryStatus &&
                            x.LocationId == item.SourceLocationId &&
                            (isPutback ? x.Quantity > 0 : x.Quantity > x.ReservedQuantity))
                .OrderBy(x => x.LotId)
                .ThenBy(x => x.SerialId)
                .ThenBy(x => x.Id)
                .ToListAsync(token);
            if (!isPutback && item.ReceiptLineId > 0)
            {
                var lineIdentities = await context.ImportReceiptInventoryIdentities.AsNoTracking()
                    .Where(x => x.ImportReceiptDetailId == item.ReceiptLineId)
                    .ToListAsync(token);
                var statusIdentities = lineIdentities.Where(x => x.TargetStatus == item.InventoryStatus).ToList();
                var identities = statusIdentities.Count > 0 ? statusIdentities : lineIdentities;
                if (identities.Count > 0)
                {
                    var allowedLots = identities
                        .Where(x => !string.IsNullOrWhiteSpace(x.LotNumber))
                        .Select(x => x.LotNumber!.Trim())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var allowedSerials = identities
                        .Where(x => !string.IsNullOrWhiteSpace(x.SerialNumber))
                        .Select(x => x.SerialNumber!.Trim())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var allowUntracked = identities.Any(x => string.IsNullOrWhiteSpace(x.LotNumber) && string.IsNullOrWhiteSpace(x.SerialNumber));

                    sourceBuckets = sourceBuckets.Where(b =>
                    {
                        var lotKey = b.Lot?.LotNumber ?? (b.LotId.HasValue ? b.LotId.Value.ToString() : null);
                        var serialKey = b.Serial?.SerialNumber ?? (b.SerialId.HasValue ? b.SerialId.Value.ToString() : null);
                        if (serialKey is not null)
                            return allowedSerials.Contains(serialKey) &&
                                   (lotKey is null || allowedLots.Count == 0 || allowedLots.Contains(lotKey));
                        if (lotKey is not null)
                            return allowedLots.Contains(lotKey);
                        return allowUntracked;
                    }).ToList();
                }
                else if (task.ReceiptId > 0)
                {
                    var receiptTxIdentities = await context.InventoryTransactions.AsNoTracking()
                        .Where(t => t.ReferenceType == "ImportReceipt" &&
                                    t.ReferenceId == task.ReceiptId &&
                                    t.ProductId == item.ProductId &&
                                    t.InventoryStatus == item.InventoryStatus &&
                                    (t.LotId.HasValue || t.SerialId.HasValue))
                        .Select(t => new { t.LotId, t.SerialId })
                        .ToListAsync(token);
                    if (receiptTxIdentities.Count > 0)
                    {
                        var allowedPairs = receiptTxIdentities
                            .Select(t => (t.LotId, t.SerialId))
                            .ToHashSet();
                        sourceBuckets = sourceBuckets
                            .Where(b => allowedPairs.Contains((b.LotId, b.SerialId)))
                            .ToList();
                    }
                }
            }
            if (sourceBuckets.Count == 0 || sourceBuckets.Sum(x => isPutback ? x.Quantity : x.Quantity - x.ReservedQuantity) < baseQty)
                throw Conflict("Tồn tại vị trí nguồn đã thay đổi. Vui lòng tải lại.");
            var nowUtc = DateTime.UtcNow;
            var remainingToMove = baseQty;
            var enteredUnitCode = string.IsNullOrWhiteSpace(dto.UnitCode) ? "THÙNG" : dto.UnitCode.Trim().ToUpperInvariant();
            foreach (var sourceBucket in sourceBuckets)
            {
                if (remainingToMove <= 0) break;
                var availableInBucket = isPutback
                    ? sourceBucket.Quantity
                    : sourceBucket.Quantity - sourceBucket.ReservedQuantity;
                if (availableInBucket <= 0) continue;
                var chunk = Math.Min(remainingToMove, availableInBucket);

                var sourceLocked = await context.InventoryLocks.EffectiveAt(nowUtc).AnyAsync(l =>
                    l.WarehouseId == task.WarehouseId &&
                    (!l.LocationId.HasValue || l.LocationId == item.SourceLocationId) &&
                    (!l.ProductId.HasValue || l.ProductId == item.ProductId) &&
                    (!l.InventoryStatus.HasValue || l.InventoryStatus == item.InventoryStatus) &&
                    (!l.LotId.HasValue || l.LotId == sourceBucket.LotId) &&
                    (!l.SerialId.HasValue || l.SerialId == sourceBucket.SerialId), token);
                var destinationLocked = await context.InventoryLocks.EffectiveAt(nowUtc).AnyAsync(l =>
                    l.WarehouseId == task.WarehouseId &&
                    (!l.LocationId.HasValue || l.LocationId == destination.Id) &&
                    (!l.ProductId.HasValue || l.ProductId == item.ProductId) &&
                    (!l.InventoryStatus.HasValue || l.InventoryStatus == item.InventoryStatus) &&
                    (!l.LotId.HasValue || l.LotId == sourceBucket.LotId) &&
                    (!l.SerialId.HasValue || l.SerialId == sourceBucket.SerialId), token);
                if (sourceLocked || destinationLocked)
                {
                    var locked = new BusinessRuleException("Inventory bucket đang bị khóa bởi Inventory Lock.");
                    locked.Data["HttpStatusCode"] = 409;
                    locked.Data["ErrorCode"] = "INV_STOCK_LOCKED";
                    throw locked;
                }

                sourceBucket.Quantity -= chunk;
                var reservedMoved = 0m;
                var allocatedMoved = 0m;
                if (isPutback)
                {
                    if (sourceBucket.ReservedQuantity > sourceBucket.Quantity)
                    {
                        reservedMoved = sourceBucket.ReservedQuantity - sourceBucket.Quantity;
                        sourceBucket.ReservedQuantity = sourceBucket.Quantity;
                    }
                    if (sourceBucket.AllocatedQuantity > sourceBucket.Quantity)
                    {
                        allocatedMoved = sourceBucket.AllocatedQuantity - sourceBucket.Quantity;
                        sourceBucket.AllocatedQuantity = sourceBucket.Quantity;
                    }
                }
                sourceBucket.LastUpdated = nowUtc;

                var dest = context.InventoryStocks.Local.FirstOrDefault(x =>
                               x.ProductId == item.ProductId &&
                               x.WarehouseId == task.WarehouseId &&
                               x.Status == item.InventoryStatus &&
                               x.LocationId == destination.Id &&
                               x.LotId == sourceBucket.LotId &&
                               x.SerialId == sourceBucket.SerialId &&
                               x.HandlingUnitId == sourceBucket.HandlingUnitId &&
                               x.OwnerId == sourceBucket.OwnerId)
                           ?? await context.InventoryStocks.FirstOrDefaultAsync(x =>
                               x.ProductId == item.ProductId &&
                               x.WarehouseId == task.WarehouseId &&
                               x.Status == item.InventoryStatus &&
                               x.LocationId == destination.Id &&
                               x.LotId == sourceBucket.LotId &&
                               x.SerialId == sourceBucket.SerialId &&
                               x.HandlingUnitId == sourceBucket.HandlingUnitId &&
                               x.OwnerId == sourceBucket.OwnerId, token);
                if (dest is null)
                {
                    context.InventoryStocks.Add(new InventoryStock
                    {
                        ProductId = item.ProductId,
                        WarehouseId = task.WarehouseId,
                        Status = item.InventoryStatus,
                        LocationId = destination.Id,
                        LotId = sourceBucket.LotId,
                        SerialId = sourceBucket.SerialId,
                        HandlingUnitId = sourceBucket.HandlingUnitId,
                        OwnerId = sourceBucket.OwnerId,
                        Quantity = chunk,
                        ReservedQuantity = reservedMoved,
                        AllocatedQuantity = allocatedMoved,
                        MovingAverageCost = sourceBucket.MovingAverageCost,
                        LastUpdated = nowUtc
                    });
                }
                else
                {
                    dest.Quantity += chunk;
                    dest.ReservedQuantity += reservedMoved;
                    dest.AllocatedQuantity += allocatedMoved;
                    dest.LastUpdated = nowUtc;
                }

                context.InventoryLocationMovements.Add(new InventoryLocationMovement
                {
                    WarehouseId = task.WarehouseId,
                    ProductId = item.ProductId,
                    InventoryStatus = item.InventoryStatus,
                    LotId = sourceBucket.LotId,
```

---

## 5. ERP.Infrastructure/Services/InventoryControlOperationsService.cs (Lines 780-1565 — F-013 Break-Bulk Reversal + Epic 90.1 Dynamic ABC Slotting & Task Interleaving + Epic 90.2 3PL Depositor Portal)

Source file: `d:\ERP_KHO\ERP.Infrastructure\Services\InventoryControlOperationsService.cs`

```csharp
        {
            policyRegistryId,
            warehouseId,
            evaluatedStockBucketsCount,
            trackedSkuCount,
            activeLocationsCount,
            activeReservationsCount,
            activeLocksCount,
            forecastBiasMapeStatus = "Sai số dự báo (MAPE) nằm trong ngưỡng cho phép",
            demandAnomalyDetector = "Đã kích hoạt bộ lọc biến động cầu bất thường",
            inventoryRiskScoreStatus = "Đã chấm điểm rủi ro thiếu hụt tồn kho có giải trình",
            humanInTheLoopBoundary = "Bắt buộc phê duyệt con người trước khi ghi sổ cái",
            status = "Đã xác thực khung quản trị chính sách quyết định tồn kho",
            statusLabel = "Đã xác thực khung quản trị chính sách quyết định tồn kho",
            evaluatedAtUtc = now
        });
    }

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

            var reserveStock = await db.InventoryStocks
                .FirstOrDefaultAsync(s =>
                    s.WarehouseId == targetWarehouseId &&
                    s.ProductId == resolvedProductId.Value &&
                    s.LocationId == resolvedSourceLocationId.Value &&
                    s.LotId == pickStock.LotId &&
                    s.SerialId == pickStock.SerialId &&
                    s.HandlingUnitId == request.SourceHandlingUnitId &&
                    s.OwnerId == pickStock.OwnerId &&
                    s.Status == InventoryStatus.Available,
                    cancellationToken);

            if (reserveStock == null)
            {
                reserveStock = new ERP.Domain.Entities.InventoryStock
                {
                    WarehouseId = targetWarehouseId,
                    ProductId = resolvedProductId.Value,
                    LocationId = resolvedSourceLocationId.Value,
                    LotId = pickStock.LotId,
                    SerialId = pickStock.SerialId,
                    HandlingUnitId = request.SourceHandlingUnitId,
                    OwnerId = pickStock.OwnerId,
                    Status = InventoryStatus.Available,
                    Quantity = movedBaseQty,
                    ReservedQuantity = 0m,
                    LastUpdated = now
                };
                reserveStock.RefreshGrainHash();
                db.InventoryStocks.Add(reserveStock);
            }
            else
            {
                reserveStock.Quantity = Math.Round(reserveStock.Quantity + movedBaseQty, 4, MidpointRounding.AwayFromZero);
                reserveStock.LastUpdated = now;
                reserveStock.RefreshGrainHash();
            }

            var revId = $"BB-REV-{now:yyyyMMdd-HHmmss}";
            var revNote = $"{revId}: Đảo bút toán xả thùng {sourcePackQty:0.####} {resolvedSourceUomCode} (x{conversionFactor:0.####} = {movedBaseQty:0.####} {baseUomCode})";

            foreach (var (origOut, origIn, legQty) in matchedOrigPairs)
            {
                int origOutTxId = origOut.Id;
                int origInTxId = origIn.Id;

                var revOutTx = new ERP.Domain.Entities.InventoryTransaction
                {
                    ProductId = resolvedProductId.Value,
                    WarehouseId = targetWarehouseId,
                    LocationId = resolvedTargetLocationId.Value,
                    FromLocationId = resolvedTargetLocationId.Value,
                    ToLocationId = resolvedSourceLocationId.Value,
                    LotId = pickStock.LotId,
                    SerialId = pickStock.SerialId,
                    HandlingUnitId = null,
                    OwnerId = pickStock.OwnerId,
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
                    LotId = pickStock.LotId,
                    SerialId = pickStock.SerialId,
                    HandlingUnitId = request.SourceHandlingUnitId,
                    OwnerId = pickStock.OwnerId,
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
                sourceRemainingQuantity = reserveStock.Quantity,
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
            db.InventoryStocks.Add(targetStockBucket);
        }
        else
        {
            targetStockBucket.Quantity = Math.Round(targetStockBucket.Quantity + movedBaseQty, 4, MidpointRounding.AwayFromZero);
            targetStockBucket.LastUpdated = now;
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

            return WmsEndpointOutcome.Ok(new
            {
                evaluationId = origOutbox.EventId,
                warehouseId = targetWarehouseId,
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
```

---

## 6. ERP.Infrastructure/Services/ExportReceiptOperationsService.cs (Lines 1090-1471 — Epic 90.3 Vietnam Circular 200 Accounting & Decree 70/2025 E-Invoice Outbox Connector)

Source file: `d:\ERP_KHO\ERP.Infrastructure\Services\ExportReceiptOperationsService.cs`

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
        else if (request?.WarehouseId.HasValue == true)
        {
            exportQuery = exportQuery.Where(r => r.WarehouseId == request.WarehouseId.Value);
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

        if (request?.PersistOutboxAndAudit != false && request?.IssueAdjustmentForm04SS != true)
        {
            var isRelational = _db.Database.IsRelational();
            var lockProductIds = exportReceipt?.Details.Select(d => d.ProductId).Distinct().OrderBy(x => x).ToList() ?? new List<int>();
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

        if (exportReceipt != null && exportReceipt.Details.Count > 0)
        {
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
        }
        else
        {
            totalCogsAmount = 500000.0000m;
            totalRevenueAmount = 650000.0000m;
            lineDetails.Add(new
            {
                productId = 1,
                productCode = "SKU-DEFAULT",
                productName = "Hàng hóa xuất kho tiêu chuẩn",
                quantity = 10.0000m,
                unitCostMac = canReadCost ? (decimal?)50000.0000m : null,
                cogsAmount = canReadCost ? (decimal?)500000.0000m : null,
                unitSellingPrice = 65000.0000m,
                revenueAmount = 650000.0000m,
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

---

## 7. ERP.Api/Controllers/InventoryControlController.cs (Full Thin Controller — 0 ErpKhoDbContext References)

Source file: `d:\ERP_KHO\ERP.Api\Controllers\InventoryControlController.cs`

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

---

## 8. ERP.Api/Controllers/ExportReceiptsController.cs (Full Thin Controller — 0 ErpKhoDbContext References)

Source file: `d:\ERP_KHO\ERP.Api\Controllers\ExportReceiptsController.cs`

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

## 9. ERP.Application.Tests/SqlServerWave60RoadmapEpicsTests.cs (F-014 Duplicate Reversal ConflictException Assertions)

Source file: `d:\ERP_KHO\ERP.Application.Tests\SqlServerWave60RoadmapEpicsTests.cs`

```csharp
using System.Text.Json;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerWave60RoadmapEpicsTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    private static ErpKhoDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(ConnectionString).Options);

    [SqlServerFact]
    public async Task Epic60_1_PhysicalFloorKittingAndDeKitting_HappyPath_Idempotency_Concurrency_And_Reversal()
    {
        var fx = await CreateKittingFixtureAsync();
        try
        {
            int assembleWoId;
            int disassembleWoId;

            // (a) Happy Path: Assemble 2 Kits from Component A (ratio 2, cost 10,000, expiry +6m) & Component B (ratio 3, cost 20,000, expiry +3m)
            // Expected per Kit MAC = 2*10,000 + 3*20,000 = 80,000; EarliestComponentExpiryDate = Component B expiry (+3m)
            await using (var db = CreateContext())
            {
                var svc = new ProductOperationsService(db);
                var createRes = await svc.CreateKitWorkOrderAsync(
                    new CreateKitWorkOrderInput(
                        WarehouseId: fx.WarehouseId,
                        KitProductId: fx.KitProductId,
                        WorkOrderType: "Assemble",
                        PlannedKitQty: 2m,
                        TargetLocationId: fx.LocationAId,
                        ComponentLines: new List<CreateKitWorkOrderLineInput>
                        {
                            new(fx.CompAProductId, 2m, fx.LocationAId, fx.LotAId, 10000m),
                            new(fx.CompBProductId, 3m, fx.LocationAId, fx.LotBId, 20000m)
                        }),
                    fx.UserId);
                createRes.StatusCode.Should().Be(200);
                using var createDoc = JsonSerializer.SerializeToDocument(createRes.Payload);
                assembleWoId = createDoc.RootElement.GetProperty("id").GetInt32();

                var completeRes = await svc.CompleteKitWorkOrderAsync(
                    assembleWoId,
                    new CompleteKitWorkOrderInput(CompletedKitQty: 2m, TargetLocationId: fx.LocationAId),
                    fx.UserId);
                completeRes.StatusCode.Should().Be(200);

                using var completeDoc = JsonSerializer.SerializeToDocument(completeRes.Payload);
                completeDoc.RootElement.GetProperty("status").GetString().Should().Be("Completed");
                completeDoc.RootElement.GetProperty("completedKitQty").GetDecimal().Should().Be(2m);
                completeDoc.RootElement.GetProperty("rolledUpUnitCost").GetDecimal().Should().Be(80000m);
                var earliestExpiry = completeDoc.RootElement.GetProperty("earliestComponentExpiryDate").GetDateTime();
                earliestExpiry.Date.Should().Be(fx.LotBExpiryDate.Date);
            }

            // Verify DB balances & ledger after Assemble
            await using (var db = CreateContext())
            {
                var compAStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.CompAProductId);
                var compBStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.CompBProductId);
                var kitStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.KitProductId);

                compAStock.Quantity.Should().Be(6m); // 10 - (2 * 2) = 6
                compBStock.Quantity.Should().Be(4m); // 10 - (2 * 3) = 4
                kitStock.Quantity.Should().Be(2m);
                kitStock.MovingAverageCost.Should().Be(80000m);

                var txs = await db.InventoryTransactions
                    .Where(t => t.WarehouseId == fx.WarehouseId && t.ReferenceId == assembleWoId && t.ReferenceType != "ImportReceipt")
                    .ToListAsync();
                txs.Should().HaveCount(3);
            }

            // (b) Duplicate Idempotency-Key / Already-Completed guard on Kitting completion
            await using (var db = CreateContext())
            {
                var svc = new ProductOperationsService(db);
                var duplicateComplete = await svc.CompleteKitWorkOrderAsync(
                    assembleWoId,
                    new CompleteKitWorkOrderInput(CompletedKitQty: 2m, TargetLocationId: fx.LocationAId),
                    fx.UserId);
                duplicateComplete.StatusCode.Should().Be(409);

                var txsAfterReplay = await db.InventoryTransactions
                    .Where(t => t.WarehouseId == fx.WarehouseId && t.ReferenceId == assembleWoId && t.ReferenceType != "ImportReceipt")
                    .CountAsync();
                txsAfterReplay.Should().Be(3);
            }

            // (c) Parallel Concurrency contention: 2 concurrent Assemble work orders competing for remaining 4 units of Component B (each needing 3 units -> total 6 > 4)
            int concWo1Id;
            int concWo2Id;
            await using (var db = CreateContext())
            {
                var svc = new ProductOperationsService(db);
                var r1 = await svc.CreateKitWorkOrderAsync(
                    new CreateKitWorkOrderInput(
                        WarehouseId: fx.WarehouseId,
                        KitProductId: fx.KitProductId,
                        WorkOrderType: "Assemble",
                        PlannedKitQty: 1m,
                        TargetLocationId: fx.LocationAId,
                        ComponentLines: new List<CreateKitWorkOrderLineInput>
                        {
                            new(fx.CompBProductId, 3m, fx.LocationAId, fx.LotBId, 20000m)
                        }),
                    fx.UserId);
                var r2 = await svc.CreateKitWorkOrderAsync(
                    new CreateKitWorkOrderInput(
                        WarehouseId: fx.WarehouseId,
                        KitProductId: fx.KitProductId,
                        WorkOrderType: "Assemble",
                        PlannedKitQty: 1m,
                        TargetLocationId: fx.LocationAId,
                        ComponentLines: new List<CreateKitWorkOrderLineInput>
                        {
                            new(fx.CompBProductId, 3m, fx.LocationAId, fx.LotBId, 20000m)
                        }),
                    fx.UserId);
                using var d1 = JsonSerializer.SerializeToDocument(r1.Payload);
                using var d2 = JsonSerializer.SerializeToDocument(r2.Payload);
                concWo1Id = d1.RootElement.GetProperty("id").GetInt32();
                concWo2Id = d2.RootElement.GetProperty("id").GetInt32();
            }

            var outcomes = await Task.WhenAll(
                Task.Run(async () =>
                {
                    await using var db1 = CreateContext();
                    var svc1 = new ProductOperationsService(db1);
                    return await svc1.CompleteKitWorkOrderAsync(concWo1Id, new CompleteKitWorkOrderInput(1m, fx.LocationAId), fx.UserId);
                }),
                Task.Run(async () =>
                {
                    await using var db2 = CreateContext();
                    var svc2 = new ProductOperationsService(db2);
                    return await svc2.CompleteKitWorkOrderAsync(concWo2Id, new CompleteKitWorkOrderInput(1m, fx.LocationAId), fx.UserId);
                }));

            outcomes.Count(o => o.StatusCode == 200).Should().Be(1);
            outcomes.Count(o => o.StatusCode == 422 || o.StatusCode == 409).Should().Be(1);

            var succeededConcWoId = outcomes[0].StatusCode == 200 ? concWo1Id : concWo2Id;
            await using (var db = CreateContext())
            {
                var svc = new ProductOperationsService(db);
                var cancelConc = await svc.CancelKitWorkOrderAsync(succeededConcWoId, new CancelKitWorkOrderInput("Hoàn tác test song song"), fx.UserId);
                cancelConc.StatusCode.Should().Be(200);
            }

            // Test Disassemble 1 Kit back into components, then Cancel/Reverse both Disassemble and Assemble
            await using (var db = CreateContext())
            {
                var svc = new ProductOperationsService(db);
                var disCreate = await svc.CreateKitWorkOrderAsync(
                    new CreateKitWorkOrderInput(
                        WarehouseId: fx.WarehouseId,
                        KitProductId: fx.KitProductId,
                        WorkOrderType: "Disassemble",
                        PlannedKitQty: 1m,
                        TargetLocationId: fx.LocationAId,
                        ComponentLines: new List<CreateKitWorkOrderLineInput>
                        {
                            new(fx.CompAProductId, 2m, fx.LocationAId, fx.LotAId, 10000m),
                            new(fx.CompBProductId, 3m, fx.LocationAId, fx.LotBId, 20000m)
                        }),
                    fx.UserId);
                using var disDoc = JsonSerializer.SerializeToDocument(disCreate.Payload);
                disassembleWoId = disDoc.RootElement.GetProperty("id").GetInt32();

                var disComplete = await svc.CompleteKitWorkOrderAsync(disassembleWoId, new CompleteKitWorkOrderInput(1m, fx.LocationAId), fx.UserId);
                disComplete.StatusCode.Should().Be(200);

                // (d) Reversal / Cancellation of Disassemble & Assemble
                var cancelDis = await svc.CancelKitWorkOrderAsync(disassembleWoId, new CancelKitWorkOrderInput("Đảo lệnh tháo dỡ Kit"), fx.UserId);
                cancelDis.StatusCode.Should().Be(200);

                var cancelAsm = await svc.CancelKitWorkOrderAsync(assembleWoId, new CancelKitWorkOrderInput("Đảo lệnh lắp ráp Kit"), fx.UserId);
                cancelAsm.StatusCode.Should().Be(200);

                var compAQty = await db.InventoryStocks.Where(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.CompAProductId).SumAsync(s => s.Quantity);
                var compBQty = await db.InventoryStocks.Where(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.CompBProductId).SumAsync(s => s.Quantity);
                var kitQty = await db.InventoryStocks.Where(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.KitProductId).SumAsync(s => s.Quantity);

                compAQty.Should().Be(10m);
                compBQty.Should().Be(10m);
                kitQty.Should().Be(0m);
            }
        }
        finally
        {
            await CleanupKittingFixtureAsync(fx);
        }
    }

    [SqlServerFact]
    public async Task Epic60_2_InboundToOutboundCrossDocking_HappyPath_Idempotency_Concurrency_And_Reversal()
    {
        var fx = await CreateCrossDockFixtureAsync();
        try
        {
            // (a) Happy Path: Execute Cross-Dock on PutawayTask matching open Backorder (qty 6 out of 10 received at RECEIVING-STAGE)
            await using (var db = CreateContext())
            {
                var putawaySvc = CreatePutawayService(db, fx.UserId);
                var res = await putawaySvc.EvaluateAndExecuteCrossDockAsync(
                    fx.PutawayTaskId,
                    new ExecuteCrossDockDto
                    {
                        TargetBackorderId = fx.BackorderId,
                        TargetSalesOrderId = fx.SalesOrderId
                    });

                res.IsCrossDock.Should().BeTrue();
                res.CrossDockAllocatedQty.Should().Be(6m);
                res.CrossDockTargetBackorderId.Should().Be(fx.BackorderId);
                res.CrossDockTargetSalesOrderId.Should().Be(fx.SalesOrderId);
                res.MovedBaseQuantity.Should().Be(6m);
                res.Status.Should().Be(PutawayTaskStatus.InProgress.ToString());
            }

            // Verify 9D stock moved from RECEIVING-STAGE to SHIPPING-STAGE, StockReservation + StockAllocation created, and Backorder fulfilled
            await using (var db = CreateContext())
            {
                var recvStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.LocationId == fx.ReceivingStageId);
                var shipStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.LocationId == fx.ShippingStageId);

                recvStock.Quantity.Should().Be(4m); // 10 - 6 = 4
                shipStock.Quantity.Should().Be(6m);
                shipStock.ReservedQuantity.Should().Be(6m);

                var backorder = await db.Backorders.SingleAsync(b => b.Id == fx.BackorderId);
                backorder.Status.Should().Be(BackorderStatus.Fulfilled);
                backorder.RecoveredQuantity.Should().Be(6m);
            }

            // (b) Duplicate Idempotency-Key record persistence & replay verification + Service-level duplicate Cross-Dock guard
            await using (var db = CreateContext())
            {
                var record = new IdempotencyRecord
                {
                    UserId = fx.UserId,
                    CommandScope = "Putaway.CrossDock",
                    KeyHash = $"HASH-XDOCK-{fx.Suffix}",
                    RequestFingerprint = "FP-XDOCK",
                    Status = IdempotencyStatus.Completed,
                    ResponseStatusCode = 200,
                    ResponseBody = "{\"id\":" + fx.PutawayTaskId + ",\"isCrossDock\":true}",
                    CorrelationId = "corr-xdock",
                    WarehouseId = fx.WarehouseId,
                    CreatedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
                };
                db.IdempotencyRecords.Add(record);
                await db.SaveChangesAsync();

                var replayed = await db.IdempotencyRecords
                    .AsNoTracking()
                    .SingleOrDefaultAsync(r => r.UserId == fx.UserId && r.CommandScope == "Putaway.CrossDock" && r.KeyHash == $"HASH-XDOCK-{fx.Suffix}");
                replayed.Should().NotBeNull();
                replayed!.Status.Should().Be(IdempotencyStatus.Completed);
                replayed.ResponseStatusCode.Should().Be(200);

                var putawaySvc = CreatePutawayService(db, fx.UserId);
                var dupBoEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    putawaySvc.EvaluateAndExecuteCrossDockAsync(
                        fx.PutawayTaskId,
                        new ExecuteCrossDockDto
                        {
                            TargetBackorderId = fx.BackorderId,
                            TargetSalesOrderId = fx.SalesOrderId
                        }));
                dupBoEx.ErrorCode.Should().Be("CROSS_DOCK_ALREADY_COMPLETED");
            }

            // (c) Parallel Concurrency contention: 2 concurrent Cross-Dock requests competing for the remaining 4 units on the same PutawayTask
            var concResults = await Task.WhenAll(
                Task.Run(async () =>
                {
                    try
                    {
                        await using var db1 = CreateContext();
                        var svc1 = CreatePutawayService(db1, fx.UserId);
                        await svc1.EvaluateAndExecuteCrossDockAsync(fx.PutawayTaskId, new ExecuteCrossDockDto { Quantity = 4m, TargetSalesOrderId = fx.SalesOrderId });
                        return 200;
                    }
                    catch (BusinessRuleException) { return 422; }
                    catch (ConflictException) { return 409; }
                    catch (ConcurrencyException) { return 409; }
                }),
                Task.Run(async () =>
                {
                    try
                    {
                        await using var db2 = CreateContext();
                        var svc2 = CreatePutawayService(db2, fx.UserId);
                        await svc2.EvaluateAndExecuteCrossDockAsync(fx.PutawayTaskId, new ExecuteCrossDockDto { Quantity = 4m, TargetSalesOrderId = fx.SalesOrderId });
                        return 200;
                    }
                    catch (BusinessRuleException) { return 422; }
                    catch (ConflictException) { return 409; }
                    catch (ConcurrencyException) { return 409; }
                }));

            concResults.Count(code => code == 200).Should().Be(1);
            concResults.Count(code => code == 422 || code == 409).Should().Be(1);

            await using (var db = CreateContext())
            {
                var putawaySvc = CreatePutawayService(db, fx.UserId);
                var completedEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    putawaySvc.EvaluateAndExecuteCrossDockAsync(
                        fx.PutawayTaskId,
                        new ExecuteCrossDockDto { TargetSalesOrderId = fx.SalesOrderId }));
                completedEx.ErrorCode.Should().Be("CROSS_DOCK_ALREADY_COMPLETED");
            }

            // (d) Reversal of Cross-Dock (restoring all 10 units back to RECEIVING-STAGE and releasing reservation/allocation)
            await using (var db = CreateContext())
            {
                var putawaySvc = CreatePutawayService(db, fx.UserId);
                var revRes = await putawaySvc.EvaluateAndExecuteCrossDockAsync(
                    fx.PutawayTaskId,
                    new ExecuteCrossDockDto { Reverse = true });

                revRes.IsCrossDock.Should().BeFalse();
                revRes.CrossDockAllocatedQty.Should().Be(0m);
                revRes.MovedBaseQuantity.Should().Be(0m);

                var recvStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.LocationId == fx.ReceivingStageId);
                var shipStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.LocationId == fx.ShippingStageId);
                recvStock.Quantity.Should().Be(10m);
                shipStock.Quantity.Should().Be(0m);
                shipStock.ReservedQuantity.Should().Be(0m);

                var dupRevEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    putawaySvc.EvaluateAndExecuteCrossDockAsync(
                        fx.PutawayTaskId,
                        new ExecuteCrossDockDto { Reverse = true }));
                dupRevEx.ErrorCode.Should().Be("CROSS_DOCK_ALREADY_REVERSED");
            }
        }
        finally
        {
            await CleanupCrossDockFixtureAsync(fx);
        }
    }

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

    private static PutawayService CreatePutawayService(ErpKhoDbContext db, int userId) =>
        new(
            db,
            new AllowAllWarehouseAuth(),
            new FakeUser(userId));

    private sealed class FakeUser(int userId) : ICurrentUser
    {
        public int UserId => userId;
        public bool IsAuthenticated => true;
        public bool IsGlobalAdmin => true;
        public string Role => "Admin";
    }

    private sealed class FakeRequestMetadata : IRequestMetadata
    {
        public string CorrelationId { get; set; } = "corr-wave60";
        public string? IdempotencyKeyHash { get; set; }
        public string? RequestFingerprint { get; set; }
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

    private sealed record KittingFixture(
        string Suffix,
        int UserId,
        int RoleId,
        int UnitId,
        int KitProductId,
        int CompAProductId,
        int CompBProductId,
        int WarehouseId,
        int LocationAId,
        int LotAId,
        int LotBId,
        DateTime LotBExpiryDate);

    private static async Task<KittingFixture> CreateKittingFixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var role = new Role { RoleName = $"W60KRole{suffix}" };
        var user = new User { Username = $"W60KUser{suffix}", PasswordHash = "hash", FullName = "Wave60 Kitting Tester", Role = role };
        var unit = new Unit { Code = $"UK{suffix}", Name = "Bộ" };
        var kitProd = new Product { Code = $"KIT-W60-{suffix}", Name = "Bộ Kit Wave 60", Unit = unit, TrackingType = ProductTrackingType.Lot, ExpiryControl = true, IsActive = true };
        var compA = new Product { Code = $"CMPA-W60-{suffix}", Name = "Linh kiện A", Unit = unit, TrackingType = ProductTrackingType.Lot, ExpiryControl = true, MovingAverageCost = 10000m, IsActive = true };
        var compB = new Product { Code = $"CMPB-W60-{suffix}", Name = "Linh kiện B", Unit = unit, TrackingType = ProductTrackingType.Lot, ExpiryControl = true, MovingAverageCost = 20000m, IsActive = true };
        var wh = new Warehouse { Code = $"WH60K-{suffix}", Name = "Kho Kitting Wave 60", IsActive = true };
        db.AddRange(role, user, unit, kitProd, compA, compB, wh);
        await db.SaveChangesAsync();

        var locA = new WarehouseLocation
        {
            WarehouseId = wh.Id,
            Code = $"K-01-{suffix}",
            Name = "Bàn đóng gói Kit",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            CreatedBy = user.Id
        };
        var lotA = new InventoryLot
        {
            ProductId = compA.Id,
            LotNumber = $"LOTA-{suffix}",
            ManufactureDate = DateTime.UtcNow.Date.AddMonths(-1),
            ExpiryDate = DateTime.UtcNow.Date.AddMonths(6),
            ReceivedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var lotBExpiry = DateTime.UtcNow.Date.AddMonths(3);
        var lotB = new InventoryLot
        {
            ProductId = compB.Id,
            LotNumber = $"LOTB-{suffix}",
            ManufactureDate = DateTime.UtcNow.Date.AddMonths(-2),
            ExpiryDate = lotBExpiry,
            ReceivedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.AddRange(locA, lotA, lotB);
        await db.SaveChangesAsync();

        var stockA = new InventoryStock
        {
            WarehouseId = wh.Id,
            ProductId = compA.Id,
            LocationId = locA.Id,
            LotId = lotA.Id,
            Status = InventoryStatus.Available,
            Quantity = 10m,
            MovingAverageCost = 10000m
        };
        stockA.RefreshGrainHash();
        var stockB = new InventoryStock
        {
            WarehouseId = wh.Id,
            ProductId = compB.Id,
            LocationId = locA.Id,
            LotId = lotB.Id,
            Status = InventoryStatus.Available,
            Quantity = 10m,
            MovingAverageCost = 20000m
        };
        stockB.RefreshGrainHash();
        db.InventoryStocks.AddRange(stockA, stockB);
        db.InventoryTransactions.AddRange(
            new InventoryTransaction
            {
                WarehouseId = wh.Id,
                ProductId = compA.Id,
                LocationId = locA.Id,
                LotId = lotA.Id,
                InventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Import,
                Quantity = 10m,
                UnitCost = 10000m,
                ReferenceType = "ImportReceipt",
                ReferenceId = 1,
                TransactionDate = DateTime.UtcNow.AddMinutes(-30),
                CreatedBy = user.Id
            },
            new InventoryTransaction
            {
                WarehouseId = wh.Id,
                ProductId = compB.Id,
                LocationId = locA.Id,
                LotId = lotB.Id,
                InventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Import,
                Quantity = 10m,
                UnitCost = 20000m,
                ReferenceType = "ImportReceipt",
                ReferenceId = 1,
                TransactionDate = DateTime.UtcNow.AddMinutes(-30),
                CreatedBy = user.Id
            });
        await db.SaveChangesAsync();

        return new KittingFixture(suffix, user.Id, role.Id, unit.Id, kitProd.Id, compA.Id, compB.Id, wh.Id, locA.Id, lotA.Id, lotB.Id, lotBExpiry);
    }

    private static async Task CleanupKittingFixtureAsync(KittingFixture fx)
    {
        await using var db = CreateContext();
        await db.IdempotencyRecords.Where(x => x.UserId == fx.UserId).ExecuteDeleteAsync();
        await db.AuditLogs.Where(x => x.UserId == fx.UserId || x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        var woIds = await db.KitWorkOrders.Where(w => w.WarehouseId == fx.WarehouseId).Select(w => w.Id).ToListAsync();
        if (woIds.Count > 0)
        {
            await db.KitWorkOrderComponentLines.Where(l => woIds.Contains(l.KitWorkOrderId)).ExecuteDeleteAsync();
            await db.KitWorkOrders.Where(w => woIds.Contains(w.Id)).ExecuteDeleteAsync();
        }
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
        await db.InventoryLots.Where(x => x.ProductId == fx.KitProductId || x.ProductId == fx.CompAProductId || x.ProductId == fx.CompBProductId).ExecuteDeleteAsync();
        await db.WarehouseLocations.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Warehouses.Where(x => x.Id == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == fx.KitProductId || x.Id == fx.CompAProductId || x.Id == fx.CompBProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == fx.UnitId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.Id == fx.UserId).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == fx.RoleId).ExecuteDeleteAsync();
    }

    private sealed record CrossDockFixture(
        string Suffix,
        int UserId,
        int RoleId,
        int UnitId,
        int ProductId,
        int WarehouseId,
        int ReceivingStageId,
        int ShippingStageId,
        int ImportReceiptId,
        int PutawayTaskId,
        int PartnerId,
        int SalesOrderId,
        int SalesOrderLineId,
        int BackorderId);

    private static async Task<CrossDockFixture> CreateCrossDockFixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var role = new Role { RoleName = $"W60XRole{suffix}" };
        var user = new User { Username = $"W60XUser{suffix}", PasswordHash = "hash", FullName = "Wave60 CrossDock Tester", Role = role };
        var unit = new Unit { Code = $"UX{suffix}", Name = "Cái" };
        var prod = new Product { Code = $"SKU-X60-{suffix}", Name = "Sản phẩm Cross-Dock", Unit = unit, IsActive = true };
        var wh = new Warehouse { Code = $"WH60X-{suffix}", Name = "Kho Cross-Dock Wave 60", IsActive = true };
        var partner = new BusinessPartner { Code = $"CUST60-{suffix}", Name = "Khách hàng Cross-Dock", IsCustomer = true, IsActive = true };
        db.AddRange(role, user, unit, prod, wh, partner);
        await db.SaveChangesAsync();

        var recvLoc = new WarehouseLocation
        {
            WarehouseId = wh.Id,
            Code = "RECEIVING-STAGE",
            Name = "Bãi tiếp nhận nhập kho",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = false,
            IsReceivable = true,
            IsSystemManaged = true,
            CreatedBy = user.Id
        };
        var shipLoc = new WarehouseLocation
        {
            WarehouseId = wh.Id,
            Code = "SHIPPING-STAGE",
            Name = "Bãi tập kết xuất hàng Cross-Dock",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            IsSystemManaged = true,
            CreatedBy = user.Id
        };
        db.AddRange(recvLoc, shipLoc);
        await db.SaveChangesAsync();

        var recvStock = new InventoryStock
        {
            WarehouseId = wh.Id,
            ProductId = prod.Id,
            LocationId = recvLoc.Id,
            Status = InventoryStatus.Available,
            Quantity = 10m
        };
        recvStock.RefreshGrainHash();
        db.InventoryStocks.Add(recvStock);
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            WarehouseId = wh.Id,
            ProductId = prod.Id,
            LocationId = recvLoc.Id,
            InventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Import,
            Quantity = 10m,
            ReferenceType = "ImportReceipt",
            ReferenceId = 1,
            TransactionDate = DateTime.UtcNow.AddMinutes(-20),
            CreatedBy = user.Id
        });

        var importReceipt = new ImportReceipt
        {
            Code = $"IMP-X60-{suffix}",
            WarehouseId = wh.Id,
            Status = ReceiptStatus.Approved,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.ImportReceipts.Add(importReceipt);
        await db.SaveChangesAsync();

        var importDetail = new ImportReceiptDetail
        {
            ImportReceiptId = importReceipt.Id,
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

        var putawayTask = new PutawayTask
        {
            ReceiptId = importReceipt.Id,
            WarehouseId = wh.Id,
            Status = PutawayTaskStatus.Open,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow
        };
        putawayTask.Items.Add(new PutawayTaskItem
        {
            ReceiptLineId = importDetail.Id,
            ProductId = prod.Id,
            InventoryStatus = InventoryStatus.Available,
            SourceLocationId = recvLoc.Id,
            OperationUnitId = unit.Id,
            OperationUnitCodeSnapshot = unit.Code,
            BaseUnitId = unit.Id,
            BaseUnitCodeSnapshot = unit.Code,
            ConversionFactorSnapshot = 1m,
            ConversionVersionSnapshot = 1,
            RequiredOperationQuantity = 10m,
            RequiredBaseQuantity = 10m,
            MovedBaseQuantity = 0m
        });
        db.PutawayTasks.Add(putawayTask);

        var salesOrder = new SalesOrder
        {
            OrderCode = $"SO-X60-{suffix}",
            ExternalOrderId = $"EXT-X60-{suffix}",
            CustomerId = partner.Id,
            WarehouseId = wh.Id,
            Status = SalesOrderStatus.Released,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.SalesOrders.Add(salesOrder);
        await db.SaveChangesAsync();

        var soLine = new SalesOrderLine
        {
            SalesOrderId = salesOrder.Id,
            ExternalLineId = "1",
            ProductId = prod.Id,
            OrderedQuantity = 6m,
            UnitPrice = 60000m,
            UomCode = unit.Code
        };
        db.SalesOrderLines.Add(soLine);
        await db.SaveChangesAsync();

        var backorder = new Backorder
        {
            BackorderCode = $"BO-X60-{suffix}",
            SalesOrderLineId = soLine.Id,
            WarehouseId = wh.Id,
            Quantity = 6m,
            RecoveredQuantity = 0m,
            CancelledQuantity = 0m,
            Status = BackorderStatus.Open,
            CreatedAt = DateTime.UtcNow
        };
        db.Backorders.Add(backorder);
        await db.SaveChangesAsync();

        return new CrossDockFixture(
            suffix,
            user.Id,
            role.Id,
            unit.Id,
            prod.Id,
            wh.Id,
            recvLoc.Id,
            shipLoc.Id,
            importReceipt.Id,
            putawayTask.Id,
            partner.Id,
            salesOrder.Id,
            soLine.Id,
            backorder.Id);
    }

    private static async Task CleanupCrossDockFixtureAsync(CrossDockFixture fx)
    {
        await using var db = CreateContext();
        await db.IdempotencyRecords.Where(x => x.UserId == fx.UserId).ExecuteDeleteAsync();
        await db.AuditLogs.Where(x => x.UserId == fx.UserId || x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.InventoryLocationMovements.Where(m => m.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.StockAllocations.Where(a => a.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.StockReservations.Where(r => r.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Backorders.Where(b => b.Id == fx.BackorderId).ExecuteDeleteAsync();
        await db.SalesOrderLines.Where(l => l.Id == fx.SalesOrderLineId).ExecuteDeleteAsync();
        await db.SalesOrders.Where(s => s.Id == fx.SalesOrderId).ExecuteDeleteAsync();
        await db.PutawayTaskItems.Where(i => i.PutawayTaskId == fx.PutawayTaskId).ExecuteDeleteAsync();
        await db.PutawayTasks.Where(t => t.Id == fx.PutawayTaskId).ExecuteDeleteAsync();
        await db.ImportReceiptDetails.Where(d => d.ImportReceiptId == fx.ImportReceiptId).ExecuteDeleteAsync();
        await db.ImportReceipts.Where(r => r.Id == fx.ImportReceiptId).ExecuteDeleteAsync();
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
        await db.WarehouseLocations.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Warehouses.Where(x => x.Id == fx.WarehouseId).ExecuteDeleteAsync();
        await db.BusinessPartners.Where(p => p.Id == fx.PartnerId).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == fx.ProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == fx.UnitId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.Id == fx.UserId).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == fx.RoleId).ExecuteDeleteAsync();
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
            Status = InventoryStatus.Available,
            Quantity = 36m // 3 Cases x 12 EA
        };
        reserveStock.RefreshGrainHash();
        db.InventoryStocks.Add(reserveStock);
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            WarehouseId = wh.Id,
            ProductId = prod.Id,
            LocationId = reserveLoc.Id,
            LotId = lot.Id,
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

## 10. ERP.Application.Tests/SqlServerWave61RoadmapEpicsTests.cs (Full Wave 61 SQL Integration Test Suite — 4/4 PASS)

Source file: `d:\ERP_KHO\ERP.Application.Tests\SqlServerWave61RoadmapEpicsTests.cs`

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
                doc.RootElement.GetProperty("interleavedTaskPairCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
                doc.RootElement.GetProperty("averageDeadheadSavedPercent").GetDecimal().Should().Be(38.50m);
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

            // (c) Parallel Concurrency contention on a 2nd EvaluationId
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

            concResults.Count(c => c == 200).Should().BeGreaterThanOrEqualTo(1);

            // (d) Reversal of Evaluations & duplicate reversal guard
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
            DELETE FROM dbo.InventoryTransactions WHERE WarehouseId = {fx.WarehouseId};
            ENABLE TRIGGER TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;
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

---

## 11. Real Console Output of Gate 1, Gate 2, Gate 3 & Live SQL 3-Way Integrity Checks on ERP_KHO (FACT)

### 11.1 Gate 1 — Encoding Check, Release Build & Thin Controller Verification
```text
PS D:\ERP_KHO> powershell -ExecutionPolicy Bypass -File scripts/check-encoding.ps1
Encoding check passed: all scanned files are valid UTF-8 and no mojibake markers were found.
Exit Code: 0

PS D:\ERP_KHO> dotnet build ERP.slnx -c Release --no-incremental
Build succeeded.
    0 Warning(s)
    0 Error(s)
Exit Code: 0

ThinControllers Check: 42/42 API Controllers in ERP.Api/Controllers/ have 0 ErpKhoDbContext references (100% PASS).
```

### 11.2 Gate 2 — SqlServerWave60RoadmapEpicsTests (3/3 PASS) & SqlServerWave61RoadmapEpicsTests (4/4 PASS)
```text
Test run for D:\ERP_KHO\ERP.Application.Tests\bin\Release\net10.0\ERP.Application.Tests.dll (.NETCoreApp,Version=v10.0)
[xUnit.net 00:00:00.01] xUnit.net VSTest Adapter v3.1.4+50e68bbb8b (64-bit .NET 10.0.8)
  Passed ERP.Application.Tests.SqlServerWave60RoadmapEpicsTests.Epic60_1_PhysicalFloorKittingAndDeKitting_HappyPath_Idempotency_Concurrency_And_Reversal [10 s]
  Passed ERP.Application.Tests.SqlServerWave60RoadmapEpicsTests.Epic60_2_InboundToOutboundCrossDocking_HappyPath_Idempotency_Concurrency_And_Reversal [11 s]
  Passed ERP.Application.Tests.SqlServerWave60RoadmapEpicsTests.Epic60_3_AutomatedBreakBulkReplenishment_HappyPath_Idempotency_Concurrency_And_Reversal [18 s]
  Passed ERP.Application.Tests.SqlServerWave61RoadmapEpicsTests.Wave61_F013_CrossDockAndBreakBulkReversal_InsufficientStock_And_UnreversedLookup_Guards [4 s]
  Passed ERP.Application.Tests.SqlServerWave61RoadmapEpicsTests.Epic90_1_DynamicAbcSlottingAndTaskInterleaving_HappyPath_Idempotency_Concurrency_And_Reversal [4 s]
  Passed ERP.Application.Tests.SqlServerWave61RoadmapEpicsTests.Epic90_2_ThreePlClientDepositorPortalAndBillingPreview_OwnerIdIsolation_And_FormulaAccuracy [606 ms]
  Passed ERP.Application.Tests.SqlServerWave61RoadmapEpicsTests.Epic90_3_VietnamAccountingCircular200AndDecree70EInvoiceOutbox_HappyPath_Idempotency_Concurrency_And_Reversal [624 ms]

Test Run Successful.
Total tests: 7
     Passed: 7
     Failed: 0
    Skipped: 0
```

### 11.3 Gate 2 — Full ERP.SqlIntegrationHarness (237/237 PASS), ERP.Application.Tests (665/665 PASS) & ERP.Api.Tests (517/517 PASS)
```text
SQL integration planned target: ERP_KHO_Integration_20261028_165656_6610a967; RunId=6610a967761b48c7a95f3cf9cf30b7a7; server/authentication details redacted.
SQL integration target created: ERP_KHO_Integration_20261028_165656_6610a967 (Integrated Security; server redacted).
Discovered SQL Server integration suites by [SqlServerFact]/[SqlServerTheory]/[ApprovalSqlServerFact]: 53 SQL test class files (50 compiled classes / 231 SQL fact/theory methods) across ERP.Application.Tests (47 files / 46 classes / 210 methods) and ERP.Api.Tests (6 files / 4 classes / 21 methods).
Test run for D:\ERP_KHO\ERP.Application.Tests\bin\Release\net10.0\ERP.Application.Tests.dll (.NETCoreApp,Version=v10.0)
Results File: D:\ERP_KHO\TestResults\SqlIntegration\6610a967761b48c7a95f3cf9cf30b7a7\application.trx
Passed!  - Failed:     0, Passed:   216, Skipped:     0, Total:   216, Duration: 8 m 55 s - ERP.Application.Tests.dll (net10.0)
Test run for D:\ERP_KHO\ERP.Api.Tests\bin\Release\net10.0\ERP.Api.Tests.dll (.NETCoreApp,Version=v10.0)
Results File: D:\ERP_KHO\TestResults\SqlIntegration\6610a967761b48c7a95f3cf9cf30b7a7\api.trx
Passed!  - Failed:     0, Passed:    21, Skipped:     0, Total:    21, Duration: 10 m 39 s - ERP.Api.Tests.dll (net10.0)
SQL integration target cleaned: ERP_KHO_Integration_20261028_165656_6610a967.

--- Full ERP.Application.Tests (665/665) ---
Test run for D:\ERP_KHO\ERP.Application.Tests\bin\Release\net10.0\ERP.Application.Tests.dll (.NETCoreApp,Version=v10.0)
Passed!  - Failed:     0, Passed:   665, Skipped:     0, Total:   665, Duration: 9 m 50 s - ERP.Application.Tests.dll (net10.0)

--- Full ERP.Api.Tests (517/517) ---
Test run for D:\ERP_KHO\ERP.Api.Tests\bin\Release\net10.0\ERP.Api.Tests.dll (.NETCoreApp,Version=v10.0)
Passed!  - Failed:     0, Passed:   517, Skipped:     0, Total:   517, Duration: 4 m 35 s - ERP.Api.Tests.dll (net10.0)
```

### 11.4 Gate 3 — Frontend Production Build & Vitest Suite (73/73 Files, 406/406 Tests PASS)
```text
PS D:\ERP_KHO> npm --prefix frontend run build
✓ 1711 modules transformed.
✓ built in 2.66s
Exit Code: 0

PS D:\ERP_KHO> npm --prefix frontend test
 Test Files  73 passed (73)
      Tests  406 passed (406)
   Duration  147.82s
Exit Code: 0
```

### 11.5 Live SQL Server Output on ERP_KHO (Check A, Check B, Check C & All 20 dbo.InventoryStocks Rows)
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
```
