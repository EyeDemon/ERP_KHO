using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ImportReceiptOperationsService
{
    public async Task<WmsEndpointOutcome> DisposeCustomerReturnAsync(
        int id,
        CustomerReturnDisposeInput? request,
        int? actorUserId = null,
        bool canReadCost = true,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = db.CurrentTenantId > 0 ? db.CurrentTenantId : 1;
        var gate = _dispositionLocks.GetOrAdd((tenantId, id), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var isRelational = db.Database.IsRelational();
            await using var tx = isRelational && db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
                : null;

            try
            {
                var customerReturn = await db.CustomerReturns
                    .Include(r => r.Lines)
                    .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
                if (customerReturn == null)
                {
                    if (tx != null) await tx.RollbackAsync(cancellationToken);
                    return WmsEndpointOutcome.NotFound(new
                    {
                        code = "RMA_NOT_FOUND",
                        errorCode = "RMA_NOT_FOUND",
                        message = $"Không tìm thấy phiếu hoàn trả #{id}."
                    });
                }

                var effectiveAuth = warehouseAuth ?? warehouseAccess;
                if (effectiveAuth != null)
                    await effectiveAuth.EnsureWarehouseAccessAsync(customerReturn.WarehouseId, cancellationToken);

                if (string.Equals(customerReturn.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                {
                    if (tx != null) await tx.RollbackAsync(cancellationToken);
                    return WmsEndpointOutcome.Conflict(new
                    {
                        code = "RMA_ALREADY_COMPLETED",
                        errorCode = "RMA_ALREADY_COMPLETED",
                        message = $"Phiếu hoàn trả '{customerReturn.ReturnCode}' đã được hoàn tất định đoạt trước đó."
                    });
                }

                if (!string.IsNullOrWhiteSpace(request?.RowVersion) && customerReturn.RowVersion is { Length: > 0 })
                {
                    byte[] expectedVersion;
                    try
                    {
                        expectedVersion = Convert.FromBase64String(request.RowVersion.Trim());
                    }
                    catch
                    {
                        if (tx != null) await tx.RollbackAsync(cancellationToken);
                        return WmsEndpointOutcome.BadRequest(new
                        {
                            code = "INVALID_ROW_VERSION",
                            errorCode = "INVALID_ROW_VERSION",
                            message = "RowVersion không hợp lệ."
                        });
                    }

                    if (!customerReturn.RowVersion.SequenceEqual(expectedVersion))
                    {
                        if (tx != null) await tx.RollbackAsync(cancellationToken);
                        return WmsEndpointOutcome.Conflict(new
                        {
                            code = "CONCURRENCY_CONFLICT",
                            errorCode = "CONCURRENCY_CONFLICT",
                            message = "Dữ liệu phiếu hoàn trả đã bị thay đổi bởi người dùng khác. Vui lòng tải lại."
                        });
                    }
                }

                var splitsInput = request?.Splits ?? request?.Lines;
                var lockProductIds = customerReturn.Lines.Select(l => l.ProductId)
                    .Concat(customerReturn.Lines.Where(l => l.PackagingProductId.HasValue).Select(l => l.PackagingProductId!.Value))
                    .Concat(splitsInput?.Where(s => s.ProductId.HasValue).Select(s => s.ProductId!.Value) ?? [])
                    .Concat(splitsInput?.Where(s => s.PackagingProductId.HasValue).Select(s => s.PackagingProductId!.Value) ?? [])
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                if (lockProductIds.Count > 0)
                {
                    await db.AcquireInventoryAllocationLocksAsync(customerReturn.WarehouseId, lockProductIds, cancellationToken);
                }

                async Task<int?> ResolveLocationIdAsync(int? locId, string? locCode, int? fallbackLocId)
                {
                    if (locId.HasValue) return locId.Value;
                    if (!string.IsNullOrWhiteSpace(locCode))
                    {
                        var trimmedCode = locCode.Trim();
                        var found = await db.WarehouseLocations.AsNoTracking()
                            .Where(l => l.WarehouseId == customerReturn.WarehouseId && l.Code == trimmedCode)
                            .Select(l => (int?)l.Id)
                            .FirstOrDefaultAsync(cancellationToken);
                        if (found.HasValue) return found.Value;
                    }
                    return fallbackLocId;
                }

                var topLevelRestockLocId = await ResolveLocationIdAsync(
                    request?.RestockLocationId ?? request?.TargetLocationId,
                    request?.RestockLocationCode,
                    null);

                if (splitsInput is { Count: > 0 })
                {
                    var existingLinesSnapshot = customerReturn.Lines.OrderBy(l => l.Id).ToList();
                    var groupedBySourceLine = new Dictionary<int, List<CustomerReturnDispositionSplitInput>>();

                    foreach (var split in splitsInput)
                    {
                        var matchedLine = split.LineId.HasValue
                            ? existingLinesSnapshot.FirstOrDefault(l => l.Id == split.LineId.Value)
                            : (split.ProductId.HasValue
                                ? existingLinesSnapshot.FirstOrDefault(l => l.ProductId == split.ProductId.Value)
                                : existingLinesSnapshot.FirstOrDefault());

                        if (matchedLine == null)
                        {
                            if (tx != null) await tx.RollbackAsync(cancellationToken);
                            return WmsEndpointOutcome.NotFound(new
                            {
                                code = "RMA_LINE_NOT_FOUND",
                                errorCode = "RMA_LINE_NOT_FOUND",
                                message = "Không tìm thấy dòng hàng hoàn trả tương ứng để tách định đoạt."
                            });
                        }

                        if (!groupedBySourceLine.TryGetValue(matchedLine.Id, out var bucket))
                        {
                            bucket = [];
                            groupedBySourceLine[matchedLine.Id] = bucket;
                        }
                        bucket.Add(split);
                    }

                    foreach (var (sourceLineId, splitGroup) in groupedBySourceLine)
                    {
                        var sourceLine = existingLinesSnapshot.Single(l => l.Id == sourceLineId);
                        var totalSplitQty = splitGroup.Sum(s => s.Quantity);
                        if (splitGroup.Any(s => s.Quantity <= 0m) || totalSplitQty != sourceLine.Quantity)
                        {
                            if (tx != null) await tx.RollbackAsync(cancellationToken);
                            return WmsEndpointOutcome.UnprocessableEntity(new
                            {
                                code = "RMA_SPLIT_QUANTITY_MISMATCH",
                                errorCode = "RMA_SPLIT_QUANTITY_MISMATCH",
                                message = $"Tổng số lượng tách định đoạt ({totalSplitQty}) phải khớp chính xác với số lượng dòng gốc ({sourceLine.Quantity})."
                            });
                        }

                        var originalQuarantineLocId = sourceLine.QuarantineLocationId ?? customerReturn.QuarantineLocationId;
                        var first = splitGroup[0];
                        var firstRestockLocId = await ResolveLocationIdAsync(
                            first.RestockLocationId ?? first.TargetLocationId,
                            first.RestockLocationCode,
                            topLevelRestockLocId ?? sourceLine.RestockLocationId ?? sourceLine.TargetLocationId);

                        sourceLine.Quantity = first.Quantity;
                        sourceLine.Disposition = NormalizeDispositionCode(first.Disposition ?? request?.Disposition);
                        if (!string.IsNullOrWhiteSpace(first.Condition))
                            sourceLine.Condition = NormalizeConditionCode(first.Condition);
                        sourceLine.RestockLocationId = firstRestockLocId;
                        sourceLine.TargetLocationId = firstRestockLocId ?? originalQuarantineLocId;
                        if (first.PackagingProductId.HasValue)
                            sourceLine.PackagingProductId = first.PackagingProductId.Value;
                        if (first.PackagingQuantity.HasValue)
                            sourceLine.PackagingQuantity = first.PackagingQuantity.Value;

                        for (var i = 1; i < splitGroup.Count; i++)
                        {
                            var extra = splitGroup[i];
                            var extraRestockLocId = await ResolveLocationIdAsync(
                                extra.RestockLocationId ?? extra.TargetLocationId,
                                extra.RestockLocationCode,
                                topLevelRestockLocId ?? sourceLine.RestockLocationId);

                            var clonedLine = new ERP.Domain.Entities.CustomerReturnLine
                            {
                                CustomerReturnId = customerReturn.Id,
                                ProductId = sourceLine.ProductId,
                                LotId = extra.LotId ?? sourceLine.LotId,
                                SerialId = extra.SerialId ?? sourceLine.SerialId,
                                ExpiryDate = sourceLine.ExpiryDate,
                                BaseUomId = sourceLine.BaseUomId,
                                BaseUomCode = sourceLine.BaseUomCode,
                                TransactionUomCode = sourceLine.TransactionUomCode,
                                ConversionRateSnapshot = sourceLine.ConversionRateSnapshot,
                                QuarantineLocationId = originalQuarantineLocId,
                                TargetLocationId = extraRestockLocId ?? originalQuarantineLocId,
                                RestockLocationId = extraRestockLocId,
                                Quantity = extra.Quantity,
                                UnitCost = sourceLine.UnitCost,
                                Condition = NormalizeConditionCode(extra.Condition ?? sourceLine.Condition),
                                Disposition = NormalizeDispositionCode(extra.Disposition ?? request?.Disposition),
                                PackagingProductId = extra.PackagingProductId ?? sourceLine.PackagingProductId,
                                PackagingQuantity = extra.PackagingQuantity ?? 0m
                            };
                            customerReturn.Lines.Add(clonedLine);
                            db.CustomerReturnLines.Add(clonedLine);
                        }
                    }
                }
                else
                {
                    foreach (var line in customerReturn.Lines)
                    {
                        var effectiveDisp = !string.IsNullOrWhiteSpace(request?.Disposition)
                            ? request.Disposition
                            : (line.Disposition == "PENDING" ? "RESTOCK_AVAILABLE" : line.Disposition);
                        line.Disposition = NormalizeDispositionCode(effectiveDisp);
                        if (topLevelRestockLocId.HasValue)
                        {
                            line.RestockLocationId = topLevelRestockLocId.Value;
                            line.TargetLocationId = topLevelRestockLocId.Value;
                        }
                        if (request?.PackagingProductId.HasValue == true)
                            line.PackagingProductId = request.PackagingProductId.Value;
                        if (request?.PackagingQuantity.HasValue == true)
                            line.PackagingQuantity = request.PackagingQuantity.Value;
                    }
                }

                var now = DateTime.UtcNow;
                var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : 1);


                var dispLineErr = await ExecuteCustomerReturnLineDispositionsAsync(
                    customerReturn,
                    request,
                    topLevelRestockLocId,
                    now,
                    effectiveActorId,
                    tx,
                    cancellationToken);
                if (dispLineErr != null) return dispLineErr;

                customerReturn.Status = "COMPLETED";
                customerReturn.ApprovedBy = effectiveActorId;
                customerReturn.ApprovedAtUtc = now;
                customerReturn.PostedAtUtc = now;
                customerReturn.CompletedAtUtc = now;
                if (!db.Database.IsSqlServer())
                    customerReturn.RowVersion = Guid.NewGuid().ToByteArray()[..8];

                await ApplyCustomerReturnDebtAdjustmentsAsync(customerReturn, now, effectiveActorId, cancellationToken);

                db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
                {
                    UserId = effectiveActorId,
                    WarehouseId = customerReturn.WarehouseId,
                    EntityName = "CustomerReturn",
                    EntityId = customerReturn.Id,
                    Action = "CustomerReturn.Disposed",
                    OldValues = "INSPECTED",
                    NewValues = $"Status: COMPLETED; Lines: {customerReturn.Lines.Count}",
                    Reason = request?.Note ?? request?.ReasonCode ?? "Hoàn tất định đoạt & ghi sổ hàng hoàn trả",
                    Result = "Success",
                    Timestamp = now
                });

                await db.SaveChangesAsync(cancellationToken);
                if (tx != null) await tx.CommitAsync(cancellationToken);

                return await GetCustomerReturnByIdAsync(customerReturn.Id, canReadCost, effectiveAuth, cancellationToken);
            }
            catch
            {
                if (tx != null) await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
