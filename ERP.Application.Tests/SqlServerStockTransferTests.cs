using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Queries;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerStockTransferTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task CreateMustRollbackDocumentAndDetailsWhenAuditPersistenceFails()
    {
        var fixture = await CreateFixtureAsync(10);
        var note = "Bắt buộc nguyên tử tạo phiếu " + Guid.NewGuid().ToString("N");
        try
        {
            await using (var db = CreateContext(new RejectTransferAuditInterceptor("StockTransfer.Created")))
            {
                var request = Request(fixture.SourceId, fixture.DestinationId, fixture.ProductId, 3);
                request.Note = note;
                var act = () => CreateService(db, fixture.CreatorId).CreateAsync(request);
                await act.Should().ThrowAsync<InvalidOperationException>()
                    .WithMessage("*Chặn lưu audit thử nghiệm*");
            }
            await using var verify = CreateContext();
            (await verify.StockTransfers.CountAsync(x => x.Note == note)).Should().Be(0);
            (await verify.StockTransferDetails.CountAsync(x =>
                x.StockTransfer.Note == note)).Should().Be(0);
            (await StockAsync(verify, fixture.ProductId, fixture.SourceId)).Should().Be(10);
            (await verify.AuditLogs.CountAsync(x => x.Action == "StockTransfer.Created" &&
                x.UserId == fixture.CreatorId && x.SourceWarehouseId == fixture.SourceId &&
                x.DestinationWarehouseId == fixture.DestinationId)).Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task ApproveCancelAndCompleteMustRollbackStatusWhenAuditSaveFails()
    {
        var fixture = await CreateFixtureAsync(12);
        try
        {
            int approveId, cancelId, completeId;
            await using (var setup = CreateContext())
            {
                var maker = CreateService(setup, fixture.CreatorId);
                approveId = (await maker.CreateAsync(Request(fixture.SourceId,
                    fixture.DestinationId, fixture.ProductId, 2))).Id;
                cancelId = (await maker.CreateAsync(Request(fixture.SourceId,
                    fixture.DestinationId, fixture.ProductId, 2))).Id;
                completeId = (await maker.CreateAsync(Request(fixture.SourceId,
                    fixture.DestinationId, fixture.ProductId, 3))).Id;
                var checker = CreateService(setup, fixture.UserId);
                await checker.ApproveAsync(completeId);
                await checker.DispatchAsync(completeId);
                await checker.ReceiveAsync(completeId, new ReceiveStockTransferDto
                {
                    Details = [new() { ProductId = fixture.ProductId, ReceivedQuantity = 3 }]
                });
            }

            async Task RejectTransitionAsync(int id, string action,
                Func<StockTransferService, Task> perform,
                StockTransferStatus expectedStatus)
            {
                await using (var failing = CreateContext(new RejectTransferAuditInterceptor(action)))
                {
                    var act = () => perform(CreateService(failing, fixture.UserId));
                    await act.Should().ThrowAsync<InvalidOperationException>()
                        .WithMessage("*Chặn lưu audit thử nghiệm*");
                }
                await using var verify = CreateContext();
                (await verify.StockTransfers.AsNoTracking()
                    .Where(x => x.Id == id).Select(x => x.Status).SingleAsync())
                    .Should().Be(expectedStatus);
                (await verify.AuditLogs.CountAsync(x =>
                    x.EntityName == "StockTransfer" && x.EntityId == id &&
                    x.Action == action)).Should().Be(0);
            }

            await RejectTransitionAsync(approveId, "StockTransfer.Approved",
                service => service.ApproveAsync(approveId), StockTransferStatus.Draft);
            await RejectTransitionAsync(cancelId, "StockTransfer.Cancelled",
                service => service.CancelAsync(cancelId), StockTransferStatus.Draft);
            await RejectTransitionAsync(completeId, "StockTransfer.Completed",
                service => service.CompleteAsync(completeId), StockTransferStatus.Received);

            await using (var recover = CreateContext())
            {
                var service = CreateService(recover, fixture.UserId);
                await service.ApproveAsync(approveId);
                await service.CancelAsync(cancelId);
                await service.CompleteAsync(completeId);
            }
            await using (var verify = CreateContext())
            {
                (await verify.StockTransfers.AsNoTracking()
                    .Where(x => x.Id == approveId).Select(x => x.Status).SingleAsync())
                    .Should().Be(StockTransferStatus.Approved);
                (await verify.StockTransfers.AsNoTracking()
                    .Where(x => x.Id == cancelId).Select(x => x.Status).SingleAsync())
                    .Should().Be(StockTransferStatus.Cancelled);
                (await verify.StockTransfers.AsNoTracking()
                    .Where(x => x.Id == completeId).Select(x => x.Status).SingleAsync())
                    .Should().Be(StockTransferStatus.Completed);
                foreach (var (id, action) in new[]
                {
                    (approveId, "StockTransfer.Approved"),
                    (cancelId, "StockTransfer.Cancelled"),
                    (completeId, "StockTransfer.Completed")
                })
                {
                    (await verify.AuditLogs.CountAsync(x => x.EntityName == "StockTransfer"
                        && x.EntityId == id && x.Action == action)).Should().Be(1);
                }
                (await StockAsync(verify, fixture.ProductId, fixture.SourceId)).Should().Be(9);
                (await StockAsync(verify, fixture.ProductId, fixture.DestinationId)).Should().Be(3);
                (await verify.InventoryTransactions.CountAsync(x =>
                    x.ReferenceType == "StockTransfer" && x.ReferenceId == completeId))
                    .Should().Be(2);
            }
        }
        finally { await CleanupAsync(fixture); }
    }

    private sealed class RejectTransferAuditInterceptor(string action) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<AuditLog>().Any(entry =>
                    entry.State == EntityState.Added && entry.Entity.Action == action) == true)
                throw new InvalidOperationException("Chặn lưu audit thử nghiệm.");
            return new ValueTask<InterceptionResult<int>>(result);
        }
    }

    [SqlServerFact]
    public async Task ValidationRejectsSameWarehouseNonPositiveAndDuplicateProducts()
    {
        var fixture = await CreateFixtureAsync(10);
        try
        {
            await using var db = CreateContext();
            var service = CreateService(db, fixture.UserId);
            var same = () => service.CreateAsync(Request(fixture.SourceId, fixture.SourceId, fixture.ProductId, 1));
            await same.Should().ThrowAsync<BusinessRuleException>();
            var zero = () => service.CreateAsync(Request(fixture.SourceId, fixture.DestinationId, fixture.ProductId, 0));
            await zero.Should().ThrowAsync<BusinessRuleException>();
            var duplicateRequest = Request(fixture.SourceId, fixture.DestinationId, fixture.ProductId, 1);
            duplicateRequest.Details.Add(new StockTransferDetailInputDto { ProductId = fixture.ProductId, Quantity = 1 });
            var duplicate = () => service.CreateAsync(duplicateRequest);
            await duplicate.Should().ThrowAsync<BusinessRuleException>();
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task LifecycleMovesStockOnceAndRecordsDiscrepancyAndTransactions()
    {
        var fixture = await CreateFixtureAsync(10);
        try
        {
            int id;
            await using (var db = CreateContext())
            {
                var service = CreateService(db, fixture.CreatorId);
                id = (await service.CreateAsync(Request(fixture.SourceId, fixture.DestinationId, fixture.ProductId, 8))).Id;
                (await service.GetAsync(new StockTransferQueryDto { PageIndex = 1, PageSize = 10 })).Items.Should().Contain(x => x.Id == id);
                await service.UpdateAsync(id, new UpdateStockTransferDto { SourceWarehouseId = fixture.SourceId, DestinationWarehouseId = fixture.DestinationId, Details = [new() { ProductId = fixture.ProductId, Quantity = 8 }] });
                var approverService = CreateService(db, fixture.UserId);
                await approverService.ApproveAsync(id);
                var updateAfterApproval = () => approverService.UpdateAsync(id, new UpdateStockTransferDto());
                await updateAfterApproval.Should().ThrowAsync<ConcurrencyException>();
                await approverService.DispatchAsync(id);
                var repeatedDispatch = () => approverService.DispatchAsync(id);
                await repeatedDispatch.Should().ThrowAsync<ConcurrencyException>();
            }

            await using (var verifyDispatch = CreateContext())
            {
                (await StockAsync(verifyDispatch, fixture.ProductId, fixture.SourceId)).Should().Be(2);
                (await StockAsync(verifyDispatch, fixture.ProductId, fixture.DestinationId)).Should().Be(0);
                (await verifyDispatch.InventoryTransactions.CountAsync(x => x.ReferenceType == "StockTransfer" && x.ReferenceId == id && x.TransactionType == TransactionType.TransferOut)).Should().Be(1);
            }

            await using (var db = CreateContext())
            {
                var service = CreateService(db, fixture.CreatorId);
                await service.ReceiveAsync(id, new ReceiveStockTransferDto { Details = [new() { ProductId = fixture.ProductId, ReceivedQuantity = 7, MissingQuantity = 1, DamagedQuantity = 0 }] });
                var repeatedReceive = () => service.ReceiveAsync(id, new ReceiveStockTransferDto());
                await repeatedReceive.Should().ThrowAsync<ConcurrencyException>();
                await service.CompleteAsync(id);
            }

            await using (var verify = CreateContext())
            {
                (await StockAsync(verify, fixture.ProductId, fixture.DestinationId)).Should().Be(7);
                var transfer = await verify.StockTransfers.Include(x => x.Details).SingleAsync(x => x.Id == id);
                transfer.Status.Should().Be(StockTransferStatus.Completed);
                transfer.Details.Single().MissingQuantity.Should().Be(1);
                (await verify.InventoryTransactions.CountAsync(x => x.ReferenceType == "StockTransfer" && x.ReferenceId == id)).Should().Be(2);

                var reconciliation = new InventoryReconciliationQueryService(verify);
                (await reconciliation.GetReconciliationsAsync(fixture.SourceId, fixture.ProductId, null)).Items.Single().Status.Should().Be("Match");
                (await reconciliation.GetReconciliationsAsync(fixture.DestinationId, fixture.ProductId, null)).Items.Single().Status.Should().Be("Match");
            }
            await using (var reportDb = CreateContext())
            {
                var reports = new ReportRepository(reportDb);
                var sourceReport = (await reports.GetInventoryInOutReportAsync(null, null, fixture.SourceId, fixture.ProductId)).Single();
                sourceReport.TransferOutQuantity.Should().Be(8);
                sourceReport.ClosingQuantity.Should().Be(2);
                var destinationReport = (await reports.GetInventoryInOutReportAsync(null, null, fixture.DestinationId, fixture.ProductId)).Single();
                destinationReport.TransferInQuantity.Should().Be(7);
                destinationReport.ClosingQuantity.Should().Be(7);
            }
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task ReceiveRequiresFullDispositionBeforeLeavingTransitAndPostingLedger()
    {
        var fixture = await CreateFixtureAsync(10);
        try
        {
            int id;
            await using (var db = CreateContext())
            {
                var create = CreateService(db, fixture.CreatorId);
                id = (await create.CreateAsync(Request(fixture.SourceId, fixture.DestinationId, fixture.ProductId, 8))).Id;
                var operations = CreateService(db, fixture.UserId);
                await operations.ApproveAsync(id);
                await operations.DispatchAsync(id);
            }

            async Task RejectAndVerifyAsync(decimal received, decimal missing, decimal damaged, string? note = null)
            {
                await using (var db = CreateContext())
                {
                    var act = () => CreateService(db, fixture.UserId).ReceiveAsync(id,
                        new ReceiveStockTransferDto
                        {
                            Details = [new()
                            {
                                ProductId = fixture.ProductId,
                                ReceivedQuantity = received,
                                MissingQuantity = missing,
                                DamagedQuantity = damaged,
                                Note = note
                            }]
                        });
                    await act.Should().ThrowAsync<BusinessRuleException>();
                }
                await using var verify = CreateContext();
                (await verify.StockTransfers.SingleAsync(x => x.Id == id)).Status
                    .Should().Be(StockTransferStatus.InTransit);
                (await StockAsync(verify, fixture.ProductId, fixture.DestinationId)).Should().Be(0);
                (await verify.InventoryTransactions.CountAsync(x => x.ReferenceType == "StockTransfer" &&
                    x.ReferenceId == id && x.TransactionType == TransactionType.TransferIn)).Should().Be(0);
                (await verify.AuditLogs.CountAsync(x => x.EntityName == "StockTransfer" &&
                    x.EntityId == id && x.Action == "StockTransfer.Received")).Should().Be(0);
            }

            // All five units left over must be explicitly recorded as missing
            // or damaged; they cannot disappear on Received/Completed.
            await RejectAndVerifyAsync(3, 0, 0);
            await RejectAndVerifyAsync(7, 0, 2);
            await RejectAndVerifyAsync(decimal.MaxValue, 0, 0);
            await RejectAndVerifyAsync(8, 0, 0, new string('X', 501));
            await RejectAndVerifyAsync(-1, 9, 0);
            await RejectAndVerifyAsync(7.00001m, 0.99999m, 0);

            foreach (var malformed in new[]
            {
                new ReceiveStockTransferDto { Details = null! },
                new ReceiveStockTransferDto { Details = [null!] }
            })
            {
                await using (var db = CreateContext())
                {
                    var act = () => CreateService(db, fixture.UserId).ReceiveAsync(id, malformed);
                    await act.Should().ThrowAsync<BusinessRuleException>();
                }
                await using var verify = CreateContext();
                (await verify.StockTransfers.SingleAsync(x => x.Id == id)).Status
                    .Should().Be(StockTransferStatus.InTransit);
                (await verify.InventoryTransactions.CountAsync(x => x.ReferenceType == "StockTransfer" &&
                    x.ReferenceId == id && x.TransactionType == TransactionType.TransferIn)).Should().Be(0);
            }

            await using (var db = CreateContext())
                await CreateService(db, fixture.UserId).ReceiveAsync(id,
                    new ReceiveStockTransferDto
                    {
                        Details = [new()
                        {
                            ProductId = fixture.ProductId,
                            ReceivedQuantity = 3,
                            MissingQuantity = 4,
                            DamagedQuantity = 1
                        }]
                    });

            await using (var verify = CreateContext())
            {
                var transfer = await verify.StockTransfers.Include(x => x.Details).SingleAsync(x => x.Id == id);
                transfer.Status.Should().Be(StockTransferStatus.Received);
                var line = transfer.Details.Single();
                line.ReceivedQuantity.Should().Be(3);
                line.MissingQuantity.Should().Be(4);
                line.DamagedQuantity.Should().Be(1);
                (await StockAsync(verify, fixture.ProductId, fixture.DestinationId)).Should().Be(3);
                var posted = await verify.InventoryTransactions.SingleAsync(x =>
                    x.ReferenceType == "StockTransfer" && x.ReferenceId == id &&
                    x.TransactionType == TransactionType.TransferIn);
                posted.Quantity.Should().Be(3);
                (await verify.AuditLogs.CountAsync(x => x.EntityName == "StockTransfer" &&
                    x.EntityId == id && x.Action == "StockTransfer.Received")).Should().Be(1);
            }
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task DraftEditingPreservesInventoryAndAuditAndRejectsChangesAfterApproval()
    {
        var fixture = await CreateFixtureAsync(10);
        try
        {
            int id;
            await using (var db = CreateContext())
                id = (await CreateService(db, fixture.CreatorId).CreateAsync(
                    Request(fixture.SourceId, fixture.DestinationId, fixture.ProductId, 8))).Id;

            await using (var edit = CreateContext())
                await CreateService(edit, fixture.CreatorId).UpdateAsync(id, new UpdateStockTransferDto
                {
                    SourceWarehouseId = fixture.SourceId,
                    DestinationWarehouseId = fixture.DestinationId,
                    Note = "Đã kiểm tra và sửa số lượng",
                    Details = [new()
                    {
                        ProductId = fixture.ProductId,
                        Quantity = 6,
                        Note = "Theo phiếu đã đối soát"
                    }]
                });

            await using (var verify = CreateContext())
            {
                var updated = await verify.StockTransfers.Include(x => x.Details).SingleAsync(x => x.Id == id);
                updated.Status.Should().Be(StockTransferStatus.Draft);
                updated.Note.Should().Be("Đã kiểm tra và sửa số lượng");
                updated.Details.Should().ContainSingle();
                updated.Details.Single().RequestedQuantity.Should().Be(6);
                updated.Details.Single().Note.Should().Be("Theo phiếu đã đối soát");
                (await StockAsync(verify, fixture.ProductId, fixture.SourceId)).Should().Be(10);
                (await verify.InventoryTransactions.CountAsync(x => x.ReferenceType == "StockTransfer" &&
                    x.ReferenceId == id)).Should().Be(0);
                (await verify.AuditLogs.CountAsync(x => x.EntityName == "StockTransfer" &&
                    x.EntityId == id && x.Action == "StockTransfer.Updated")).Should().Be(1);
            }

            await using (var approve = CreateContext())
                await CreateService(approve, fixture.UserId).ApproveAsync(id);

            await using (var rejected = CreateContext())
            {
                var act = () => CreateService(rejected, fixture.CreatorId).UpdateAsync(id,
                    new UpdateStockTransferDto
                    {
                        SourceWarehouseId = fixture.SourceId,
                        DestinationWarehouseId = fixture.DestinationId,
                        Details = [new() { ProductId = fixture.ProductId, Quantity = 3 }]
                    });
                await act.Should().ThrowAsync<ConcurrencyException>();
            }
            await using (var verify = CreateContext())
            {
                var unchanged = await verify.StockTransfers.Include(x => x.Details).SingleAsync(x => x.Id == id);
                unchanged.Status.Should().Be(StockTransferStatus.Approved);
                unchanged.Details.Single().RequestedQuantity.Should().Be(6);
                (await verify.AuditLogs.CountAsync(x => x.EntityName == "StockTransfer" &&
                    x.EntityId == id && x.Action == "StockTransfer.Updated")).Should().Be(1);
            }
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task DraftEditRejectsInvalidQuantityAndLongNotesWithoutDocumentMutation()
    {
        var fixture = await CreateFixtureAsync(10);
        try
        {
            int id;
            await using (var db = CreateContext())
                id = (await CreateService(db, fixture.CreatorId).CreateAsync(
                    Request(fixture.SourceId, fixture.DestinationId, fixture.ProductId, 8))).Id;
            await using (var db = CreateContext())
            {
                var service = CreateService(db, fixture.CreatorId);
                foreach (var request in new[]
                {
                    new UpdateStockTransferDto
                    {
                        SourceWarehouseId = fixture.SourceId, DestinationWarehouseId = fixture.DestinationId,
                        Details = [new() { ProductId = fixture.ProductId, Quantity = 8.00001m }]
                    },
                    new UpdateStockTransferDto
                    {
                        SourceWarehouseId = fixture.SourceId, DestinationWarehouseId = fixture.DestinationId,
                        Details = [new() { ProductId = fixture.ProductId, Quantity = 100_000_000_000_000m }]
                    },
                    new UpdateStockTransferDto
                    {
                        SourceWarehouseId = fixture.SourceId, DestinationWarehouseId = fixture.DestinationId,
                        Note = new string('x', 501),
                        Details = [new() { ProductId = fixture.ProductId, Quantity = 4 }]
                    },
                    new UpdateStockTransferDto
                    {
                        SourceWarehouseId = fixture.SourceId, DestinationWarehouseId = fixture.DestinationId,
                        Details = [new()
                        {
                            ProductId = fixture.ProductId, Quantity = 4,
                            Note = new string('x', 501)
                        }]
                    }
                })
                {
                    var act = () => service.UpdateAsync(id, request);
                    await act.Should().ThrowAsync<BusinessRuleException>();
                }
            }
            await using var verify = CreateContext();
            var untouched = await verify.StockTransfers.Include(x => x.Details).SingleAsync(x => x.Id == id);
            untouched.Status.Should().Be(StockTransferStatus.Draft);
            untouched.Details.Single().RequestedQuantity.Should().Be(8);
            (await verify.AuditLogs.CountAsync(x => x.EntityName == "StockTransfer" &&
                x.EntityId == id && x.Action == "StockTransfer.Updated")).Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task CompetingTransfersCannotDriveSourceStockNegative()
    {
        var fixture = await CreateFixtureAsync(10);
        try
        {
            int firstId;
            int secondId;
            await using (var db = CreateContext())
            {
                var service = CreateService(db, fixture.CreatorId);
                firstId = (await service.CreateAsync(Request(fixture.SourceId, fixture.DestinationId, fixture.ProductId, 8))).Id;
                secondId = (await service.CreateAsync(Request(fixture.SourceId, fixture.DestinationId, fixture.ProductId, 8))).Id;
                var approverService = CreateService(db, fixture.UserId);
                await approverService.ApproveAsync(firstId);
                await approverService.ApproveAsync(secondId);
            }

            async Task<bool> DispatchAsync(int id)
            {
                await using var db = CreateContext();
                try { await CreateService(db, fixture.UserId).DispatchAsync(id); return true; }
                catch (Exception ex) when (ex is ConcurrencyException or DbUpdateException) { return false; }
            }
            var results = await Task.WhenAll(DispatchAsync(firstId), DispatchAsync(secondId));
            results.Count(x => x).Should().Be(1);
            await using var verify = CreateContext();
            (await StockAsync(verify, fixture.ProductId, fixture.SourceId)).Should().Be(2);
            (await verify.InventoryStocks.Where(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.SourceId).MinAsync(x => x.Quantity)).Should().BeGreaterThanOrEqualTo(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task ApprovalMustRejectTheWarehousePairChangedAfterAuthorization()
    {
        var fixture = await CreateFixtureAsync(10);
        try
        {
            int id;
            await using (var db = CreateContext())
                id = (await CreateService(db, fixture.CreatorId).CreateAsync(
                    Request(fixture.SourceId, fixture.DestinationId, fixture.ProductId, 3))).Id;

            await using (var approvingDb = CreateContext())
            {
                var user = new TestCurrentUser(fixture.UserId, false, "Manager");
                var auth = new WarehouseAuthorizationService(approvingDb, user);
                var racingAuth = new ChangeWarehouseAfterScopeCheck(
                    auth, id, fixture.SourceId, fixture.DestinationId);
                var service = new StockTransferService(approvingDb,
                    new InventoryStockRepository(approvingDb), racingAuth, user);
                var act = () => service.ApproveAsync(id);
                await act.Should().ThrowAsync<ConcurrencyException>()
                    .WithMessage("*Trạng thái phiếu đã thay đổi*");
            }

            await using var verify = CreateContext();
            var transfer = await verify.StockTransfers.SingleAsync(x => x.Id == id);
            transfer.Status.Should().Be(StockTransferStatus.Draft);
            transfer.SourceWarehouseId.Should().Be(fixture.DestinationId);
            transfer.DestinationWarehouseId.Should().Be(fixture.SourceId);
            (await verify.AuditLogs.CountAsync(x =>
                x.EntityName == "StockTransfer" && x.EntityId == id &&
                x.Action == "StockTransfer.Approved")).Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task ConcurrentReceiveAllowsOneSuccessAndWarehouseScopeIsEnforced()
    {
        var fixture = await CreateFixtureAsync(10);
        try
        {
            int id;
            await using (var db = CreateContext())
            {
                var service = CreateService(db, fixture.CreatorId);
                id = (await service.CreateAsync(Request(fixture.SourceId, fixture.DestinationId, fixture.ProductId, 8))).Id;
                var approverService = CreateService(db, fixture.UserId);
                await approverService.ApproveAsync(id); await approverService.DispatchAsync(id);
            }
            async Task<bool> ReceiveAsync()
            {
                await using var db = CreateContext();
                try { await CreateService(db, fixture.UserId).ReceiveAsync(id, new ReceiveStockTransferDto { Details = [new() { ProductId = fixture.ProductId, ReceivedQuantity = 8 }] }); return true; }
                catch (Exception ex) when (ex is ConcurrencyException or DbUpdateException) { return false; }
            }
            var results = await Task.WhenAll(ReceiveAsync(), ReceiveAsync());
            results.Count(x => x).Should().Be(1);
            await using (var verify = CreateContext())
            {
                (await StockAsync(verify, fixture.ProductId, fixture.DestinationId)).Should().Be(8);
                (await verify.InventoryTransactions.CountAsync(x => x.ReferenceType == "StockTransfer" && x.ReferenceId == id && x.TransactionType == TransactionType.TransferIn)).Should().Be(1);
                await verify.UserWarehouses.Where(x => x.UserId == fixture.UserId && x.WarehouseId == fixture.DestinationId).ExecuteDeleteAsync();
            }
            await using (var deniedDb = CreateContext())
            {
                var denied = () => CreateService(deniedDb, fixture.UserId).CompleteAsync(id);
                await denied.Should().ThrowAsync<NotFoundException>();
            }
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task ConcurrentDifferentTransfersUpsertSameDestinationWithoutLostUpdate()
    {
        var fixture = await CreateFixtureAsync(10);
        try
        {
            int firstId;
            int secondId;
            await using (var db = CreateContext())
            {
                await db.InventoryStocks.Where(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.DestinationId).ExecuteDeleteAsync();
                var service = CreateService(db, fixture.CreatorId);
                firstId = (await service.CreateAsync(Request(fixture.SourceId, fixture.DestinationId, fixture.ProductId, 3))).Id;
                secondId = (await service.CreateAsync(Request(fixture.SourceId, fixture.DestinationId, fixture.ProductId, 3))).Id;
                var approverService = CreateService(db, fixture.UserId);
                await approverService.ApproveAsync(firstId); await approverService.ApproveAsync(secondId);
                await approverService.DispatchAsync(firstId); await approverService.DispatchAsync(secondId);
            }

            async Task ReceiveAsync(int id)
            {
                await using var db = CreateContext();
                await CreateService(db, fixture.UserId).ReceiveAsync(id, new ReceiveStockTransferDto { Details = [new() { ProductId = fixture.ProductId, ReceivedQuantity = 3 }] });
            }
            await Task.WhenAll(ReceiveAsync(firstId), ReceiveAsync(secondId));
            await using var verify = CreateContext();
            (await StockAsync(verify, fixture.ProductId, fixture.DestinationId)).Should().Be(6);
            (await verify.InventoryTransactions.CountAsync(x => x.ReferenceType == "StockTransfer" && (x.ReferenceId == firstId || x.ReferenceId == secondId) && x.TransactionType == TransactionType.TransferIn)).Should().Be(2);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task FourIndependentReceiptsIntoMissingCanonicalBucketAreSerializedWithoutDuplicateLedger()
    {
        var fixture = await CreateFixtureAsync(20);
        try
        {
            var ids = new List<int>();
            await using (var setup = CreateContext())
            {
                // Force the absent-row upsert path which previously produced a
                // SERIALIZABLE range-lock conversion deadlock on the same bucket.
                await setup.InventoryStocks.Where(x => x.ProductId == fixture.ProductId &&
                    x.WarehouseId == fixture.DestinationId).ExecuteDeleteAsync();
                // Another tracked identity shares product, warehouse, location
                // and status. The transfer must only upsert the NULL lot/serial
                // bucket, never increment this lot-controlled stock.
                var locationId = await setup.WarehouseLocations
                    .Where(x => x.WarehouseId == fixture.DestinationId && x.Code == "LEGACY")
                    .Select(x => x.Id).SingleAsync();
                var lot = new InventoryLot
                {
                    ProductId = fixture.ProductId,
                    LotNumber = "TRANSFER-LOT-" + Guid.NewGuid().ToString("N")[..8],
                };
                setup.InventoryLots.Add(lot);
                await setup.SaveChangesAsync();
                setup.InventoryStocks.Add(new InventoryStock
                {
                    ProductId = fixture.ProductId,
                    WarehouseId = fixture.DestinationId,
                    LocationId = locationId,
                    LotId = lot.Id,
                    Status = InventoryStatus.Available,
                    Quantity = 7
                });
                await setup.SaveChangesAsync();
                var maker = CreateService(setup, fixture.CreatorId);
                var checker = CreateService(setup, fixture.UserId);
                for (var i = 0; i < 4; i++)
                {
                    var id = (await maker.CreateAsync(
                        Request(fixture.SourceId, fixture.DestinationId, fixture.ProductId, 3))).Id;
                    await checker.ApproveAsync(id);
                    await checker.DispatchAsync(id);
                    ids.Add(id);
                }
            }

            async Task PostReceiptAsync(int transferId)
            {
                await using var receive = CreateContext();
                await CreateService(receive, fixture.UserId).ReceiveAsync(transferId,
                    new ReceiveStockTransferDto { Details = [new()
                    {
                        ProductId = fixture.ProductId,
                        ReceivedQuantity = 3
                    }] });
            }

            await Task.WhenAll(ids.Select(PostReceiptAsync));
            await using var verify = CreateContext();
            (await StockAsync(verify, fixture.ProductId, fixture.SourceId)).Should().Be(8);
            (await verify.InventoryStocks.Where(x =>
                x.ProductId == fixture.ProductId && x.WarehouseId == fixture.DestinationId &&
                x.LotId == null && x.SerialId == null &&
                x.Status == InventoryStatus.Available).Select(x => x.Quantity).SingleAsync()).Should().Be(12);
            (await verify.InventoryStocks.Where(x =>
                x.ProductId == fixture.ProductId && x.WarehouseId == fixture.DestinationId &&
                x.LotId != null).Select(x => x.Quantity).SingleAsync()).Should().Be(7);
            (await verify.InventoryTransactions.CountAsync(x =>
                x.ReferenceType == "StockTransfer" && x.ReferenceId.HasValue &&
                ids.Contains(x.ReferenceId.Value) && x.TransactionType == TransactionType.TransferIn))
                .Should().Be(4);
            (await verify.AuditLogs.CountAsync(x =>
                x.EntityName == "StockTransfer" && x.EntityId.HasValue && ids.Contains(x.EntityId.Value) &&
                x.Action == "StockTransfer.Received")).Should().Be(4);
            (await verify.StockTransfers.CountAsync(x =>
                ids.Contains(x.Id) && x.Status == StockTransferStatus.Received)).Should().Be(4);
            (await verify.InventoryStocks.CountAsync(x =>
                x.ProductId == fixture.ProductId &&
                x.WarehouseId == fixture.DestinationId &&
                x.Status == InventoryStatus.Available &&
                x.LotId == null && x.SerialId == null)).Should().Be(1);
        }
        finally { await CleanupAsync(fixture); }
    }

    private sealed class ChangeWarehouseAfterScopeCheck(
        IWarehouseAuthorizationService inner, int transferId, int sourceId, int destinationId)
        : IWarehouseAuthorizationService
    {
        private bool changed;
        public Task<IReadOnlyList<int>> GetAccessibleWarehouseIdsAsync(
            CancellationToken cancellationToken = default) =>
            inner.GetAccessibleWarehouseIdsAsync(cancellationToken);

        public Task<bool> CanAccessWarehouseAsync(int warehouseId,
            CancellationToken cancellationToken = default) =>
            inner.CanAccessWarehouseAsync(warehouseId, cancellationToken);

        public async Task EnsureWarehouseAccessAsync(int warehouseId,
            CancellationToken cancellationToken = default)
        {
            await inner.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);
            if (changed || warehouseId != destinationId) return;
            changed = true;
            // Simulate another editor committing a different warehouse pair
            // between the approval's permission reads and status CAS.
            await using var editingDb = CreateContext();
            var rows = await editingDb.StockTransfers
                .Where(x => x.Id == transferId && x.Status == StockTransferStatus.Draft)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.SourceWarehouseId, destinationId)
                    .SetProperty(x => x.DestinationWarehouseId, sourceId),
                    cancellationToken);
            rows.Should().Be(1);
        }
    }

    private static StockTransferService CreateService(ErpKhoDbContext db, int userId)
    {
        var user = new TestCurrentUser(userId, false, "Manager");
        var authorization = new WarehouseAuthorizationService(db, user);
        return new StockTransferService(db, new InventoryStockRepository(db), authorization, user);
    }

    private static CreateStockTransferDto Request(int source, int destination, int product, decimal quantity) => new()
        { SourceWarehouseId = source, DestinationWarehouseId = destination, Details = [new() { ProductId = product, Quantity = quantity }] };

    private static async Task<Fixture> CreateFixtureAsync(decimal sourceQuantity)
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var role = new Role { RoleName = $"TRole{suffix}" };
        var creator = new User { Username = $"TCreator{suffix}", PasswordHash = "not-used", FullName = "Transfer creator", Role = role };
        var approver = new User { Username = $"TApprover{suffix}", PasswordHash = "not-used", FullName = "Transfer approver", Role = role };
        var unit = new Unit { Code = $"U{suffix}", Name = "Transfer unit" };
        var product = new Product { Code = $"P{suffix}", Name = "Transfer product", Unit = unit };
        var source = new Warehouse { Code = $"S{suffix}", Name = "Source" };
        var destination = new Warehouse { Code = $"D{suffix}", Name = "Destination" };
        db.AddRange(creator, approver, product, source, destination);
        await db.SaveChangesAsync();
        var sourceLocation = new WarehouseLocation { WarehouseId = source.Id, Code = "LEGACY", Name = "Legacy stock", LocationType = WarehouseLocationType.Legacy, IsActive = true, IsPickable = true, IsSystemManaged = true, CreatedBy = creator.Id };
        var destinationLocation = new WarehouseLocation { WarehouseId = destination.Id, Code = "LEGACY", Name = "Legacy stock", LocationType = WarehouseLocationType.Legacy, IsActive = true, IsPickable = true, IsSystemManaged = true, CreatedBy = creator.Id };
        db.AddRange(sourceLocation, destinationLocation);
        await db.SaveChangesAsync();
        db.UserWarehouses.AddRange(
            new UserWarehouse { UserId = creator.Id, WarehouseId = source.Id, CreatedBy = creator.Id },
            new UserWarehouse { UserId = creator.Id, WarehouseId = destination.Id, CreatedBy = creator.Id },
            new UserWarehouse { UserId = approver.Id, WarehouseId = source.Id, CreatedBy = creator.Id },
            new UserWarehouse { UserId = approver.Id, WarehouseId = destination.Id, CreatedBy = creator.Id });
        db.InventoryStocks.AddRange(new InventoryStock { ProductId = product.Id, WarehouseId = source.Id, LocationId = sourceLocation.Id, Quantity = sourceQuantity }, new InventoryStock { ProductId = product.Id, WarehouseId = destination.Id, LocationId = destinationLocation.Id, Quantity = 0 });
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            ProductId = product.Id,
            WarehouseId = source.Id,
            TransactionType = TransactionType.Import,
            Quantity = sourceQuantity,
            ReferenceType = "TestSetup",
            TransactionDate = DateTime.UtcNow,
            CreatedBy = creator.Id
        });
        await db.SaveChangesAsync();
        return new Fixture(creator.Id, approver.Id, role.Id, unit.Id, product.Id, source.Id, destination.Id);
    }

    private static async Task CleanupAsync(Fixture fixture)
    {
        await using var db = CreateContext();
        var userIds = new[] { fixture.CreatorId, fixture.UserId };
        var transferIds = await db.StockTransferDetails
            .Where(x => x.ProductId == fixture.ProductId)
            .Select(x => x.StockTransferId)
            .Distinct()
            .ToListAsync();
        await db.InventoryTransactions.Where(x => x.ProductId == fixture.ProductId).ExecuteDeleteAsync();
        await db.AuditLogs.Where(x => x.UserId.HasValue && userIds.Contains(x.UserId.Value)).ExecuteDeleteAsync();
        await db.StockTransferDetails.Where(x => x.ProductId == fixture.ProductId).ExecuteDeleteAsync();
        await db.StockTransfers.Where(x => transferIds.Contains(x.Id)).ExecuteDeleteAsync();
        await db.InventoryStocks.Where(x => x.ProductId == fixture.ProductId).ExecuteDeleteAsync();
        await db.InventoryLots.Where(x => x.ProductId == fixture.ProductId).ExecuteDeleteAsync();
        await db.UserWarehouses.Where(x => userIds.Contains(x.UserId)).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == fixture.ProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == fixture.UnitId).ExecuteDeleteAsync();
        await db.WarehouseLocations.Where(x => x.WarehouseId == fixture.SourceId || x.WarehouseId == fixture.DestinationId).ExecuteDeleteAsync();
        await db.Warehouses.Where(x => x.Id == fixture.SourceId || x.Id == fixture.DestinationId).ExecuteDeleteAsync();
        await db.Users.Where(x => userIds.Contains(x.Id)).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == fixture.RoleId).ExecuteDeleteAsync();
    }

    private static Task<decimal> StockAsync(ErpKhoDbContext db, int product, int warehouse) => db.InventoryStocks.Where(x => x.ProductId == product && x.WarehouseId == warehouse).Select(x => x.Quantity).SingleAsync();
    private static ErpKhoDbContext CreateContext(SaveChangesInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseSqlServer(ConnectionString);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new ErpKhoDbContext(options.Options);
    }
    private sealed record TestCurrentUser(int UserId, bool IsGlobalAdmin, string Role) : ICurrentUser { public bool IsAuthenticated => true; }
    private sealed record Fixture(int CreatorId, int UserId, int RoleId, int UnitId, int ProductId, int SourceId, int DestinationId);
}
