using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Application.Options;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerApprovalQueueRejectTests
{
    [SqlServerFact]
    public async Task QueueAging_UsesControlledUtcClockAndPreservesWarehouseScope()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var now = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        var role = new Role { RoleName = $"QA-AGING-{suffix}" };
        db.Roles.Add(role); await db.SaveChangesAsync();
        var maker = new User { Username = $"qa-aging-maker-{suffix}", PasswordHash = "test-only", RoleId = role.Id };
        var checker = new User { Username = $"qa-aging-checker-{suffix}", PasswordHash = "test-only", RoleId = role.Id };
        db.Users.AddRange(maker, checker); await db.SaveChangesAsync();
        var warehouse = new Warehouse { Code = $"QA-AGING-{suffix}", Name = "QA Aging" };
        var outside = new Warehouse { Code = $"QA-OUT-{suffix}", Name = "QA Outside" };
        db.Warehouses.AddRange(warehouse, outside); await db.SaveChangesAsync();
        db.UserWarehouses.Add(new UserWarehouse { UserId = checker.Id, WarehouseId = warehouse.Id, CreatedBy = maker.Id });
        var normal = new ImportReceipt { Code = $"QA-NORMAL-{suffix}", WarehouseId = warehouse.Id, CreatedBy = maker.Id, CreatedAt = now.UtcDateTime.AddHours(-23).AddMinutes(-59) };
        var warning = new ImportReceipt { Code = $"QA-WARNING-{suffix}", WarehouseId = warehouse.Id, CreatedBy = maker.Id, CreatedAt = now.UtcDateTime.AddHours(-24) };
        var overdue = new ImportReceipt { Code = $"QA-OVERDUE-{suffix}", WarehouseId = warehouse.Id, CreatedBy = maker.Id, CreatedAt = now.UtcDateTime.AddHours(-48) };
        var closed = new ImportReceipt { Code = $"QA-CLOSED-{suffix}", WarehouseId = warehouse.Id, CreatedBy = maker.Id, CreatedAt = now.UtcDateTime.AddDays(-5), Status = ReceiptStatus.Cancelled };
        var hidden = new ImportReceipt { Code = $"QA-HIDDEN-AGING-{suffix}", WarehouseId = outside.Id, CreatedBy = maker.Id, CreatedAt = now.UtcDateTime.AddDays(-5) };
        db.AddRange(normal, warning, overdue, closed, hidden); await db.SaveChangesAsync();

        var current = new Current(checker.Id);
        var service = new ApprovalWorkflowService(db, current, new WarehouseAuthorizationService(db, current), new Metadata(), new FixedTimeProvider(now), new ApprovalAgingOptions());
        var queue = await service.GetQueueAsync(new ApprovalQueueQuery { Keyword = suffix, PageSize = 20 });

        queue.Items.Should().ContainSingle(x => x.DocumentId == normal.Id && x.SlaStatus == "Normal" && x.WaitingMinutes == 1439);
        queue.Items.Should().ContainSingle(x => x.DocumentId == warning.Id && x.SlaStatus == "Warning" && x.WaitingMinutes == 1440);
        queue.Items.Should().ContainSingle(x => x.DocumentId == overdue.Id && x.SlaStatus == "Overdue" && x.WaitingMinutes == 2880);
        queue.Items.Should().NotContain(x => x.DocumentId == closed.Id || x.DocumentId == hidden.Id);
        (await service.GetQueueAsync(new ApprovalQueueQuery { Keyword = suffix, SlaStatus = "Overdue" })).Items.Should().ContainSingle(x => x.DocumentId == overdue.Id);
        var closedDetail = await service.GetDetailAsync("ImportReceipt", closed.Id);
        closedDetail.Summary.SlaStatus.Should().BeNull();
        closedDetail.Summary.WaitingMinutes.Should().BeNull();

        await transaction.RollbackAsync();
    }

    [SqlServerFact]
    public async Task StockTransferApprove_RequiresBothWarehouseScopesAndCheckerRole()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var role = new Role { RoleName = $"QA-SCOPE-{suffix}" };
        db.Roles.Add(role); await db.SaveChangesAsync();
        var maker = new User { Username = $"qa-scope-maker-{suffix}", PasswordHash = "test-only", RoleId = role.Id };
        var sourceOnly = new User { Username = $"qa-scope-source-{suffix}", PasswordHash = "test-only", RoleId = role.Id };
        var destinationOnly = new User { Username = $"qa-scope-destination-{suffix}", PasswordHash = "test-only", RoleId = role.Id };
        var both = new User { Username = $"qa-scope-both-{suffix}", PasswordHash = "test-only", RoleId = role.Id };
        var none = new User { Username = $"qa-scope-none-{suffix}", PasswordHash = "test-only", RoleId = role.Id };
        var staff = new User { Username = $"qa-scope-staff-{suffix}", PasswordHash = "test-only", RoleId = role.Id };
        var viewer = new User { Username = $"qa-scope-viewer-{suffix}", PasswordHash = "test-only", RoleId = role.Id };
        db.Users.AddRange(maker, sourceOnly, destinationOnly, both, none, staff, viewer); await db.SaveChangesAsync();
        var source = new Warehouse { Code = $"QA-SCOPE-S-{suffix}", Name = "Scope Source" };
        var destination = new Warehouse { Code = $"QA-SCOPE-D-{suffix}", Name = "Scope Destination" };
        db.Warehouses.AddRange(source, destination); await db.SaveChangesAsync();
        db.UserWarehouses.AddRange(
            new UserWarehouse { UserId = maker.Id, WarehouseId = source.Id, CreatedBy = maker.Id },
            new UserWarehouse { UserId = maker.Id, WarehouseId = destination.Id, CreatedBy = maker.Id },
            new UserWarehouse { UserId = sourceOnly.Id, WarehouseId = source.Id, CreatedBy = maker.Id },
            new UserWarehouse { UserId = destinationOnly.Id, WarehouseId = destination.Id, CreatedBy = maker.Id },
            new UserWarehouse { UserId = both.Id, WarehouseId = source.Id, CreatedBy = maker.Id },
            new UserWarehouse { UserId = both.Id, WarehouseId = destination.Id, CreatedBy = maker.Id },
            new UserWarehouse { UserId = staff.Id, WarehouseId = source.Id, CreatedBy = maker.Id },
            new UserWarehouse { UserId = staff.Id, WarehouseId = destination.Id, CreatedBy = maker.Id },
            new UserWarehouse { UserId = viewer.Id, WarehouseId = source.Id, CreatedBy = maker.Id },
            new UserWarehouse { UserId = viewer.Id, WarehouseId = destination.Id, CreatedBy = maker.Id });
        var transfers = Enumerable.Range(0, 7).Select(i => new StockTransfer
        {
            Code = $"QA-SCOPE-{i}-{suffix}", SourceWarehouseId = source.Id,
            DestinationWarehouseId = destination.Id, CreatedBy = maker.Id
        }).ToArray();
        db.StockTransfers.AddRange(transfers); await db.SaveChangesAsync();

        StockTransferService Service(int userId, string currentRole = "Manager")
        {
            var user = new Current(userId, currentRole);
            return new StockTransferService(db, Mock.Of<IInventoryStockRepository>(), new WarehouseAuthorizationService(db, user), user);
        }

        await FluentActions.Awaiting(() => Service(sourceOnly.Id).ApproveAsync(transfers[0].Id)).Should().ThrowAsync<ERP.Application.Exceptions.NotFoundException>();
        await FluentActions.Awaiting(() => Service(destinationOnly.Id).ApproveAsync(transfers[1].Id)).Should().ThrowAsync<ERP.Application.Exceptions.NotFoundException>();
        await FluentActions.Awaiting(() => Service(none.Id).ApproveAsync(transfers[2].Id)).Should().ThrowAsync<ERP.Application.Exceptions.NotFoundException>();
        await FluentActions.Awaiting(() => Service(maker.Id).ApproveAsync(transfers[3].Id)).Should().ThrowAsync<ERP.Application.Exceptions.ForbiddenException>();
        await FluentActions.Awaiting(() => Service(staff.Id, "WarehouseStaff").ApproveAsync(transfers[4].Id)).Should().ThrowAsync<ERP.Application.Exceptions.ForbiddenException>();
        await FluentActions.Awaiting(() => Service(viewer.Id, "Viewer").ApproveAsync(transfers[5].Id)).Should().ThrowAsync<ERP.Application.Exceptions.ForbiddenException>();
        await Service(both.Id).ApproveAsync(transfers[6].Id);
        await FluentActions.Awaiting(() => Service(both.Id).ApproveAsync(transfers[6].Id)).Should().ThrowAsync<ERP.Domain.Exceptions.ConcurrencyException>();

        (await db.StockTransfers.Where(x => transfers.Take(6).Select(t => t.Id).Contains(x.Id)).Select(x => x.Status).ToListAsync())
            .Should().OnlyContain(x => x == StockTransferStatus.Draft);
        (await db.StockTransfers.Where(x => x.Id == transfers[6].Id).Select(x => x.Status).SingleAsync()).Should().Be(StockTransferStatus.Approved);
        (await db.AuditLogs.CountAsync(x => x.EntityName == "StockTransfer" && x.EntityId == transfers[6].Id && x.Action == "StockTransfer.Approved")).Should().Be(1);
        await transaction.RollbackAsync();
    }

    [SqlServerFact]
    public async Task ApproveRejectAndCancelRejectRaces_CommitExactlyOneTransition()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        int roleId = 0, makerId = 0, approverId = 0, rejecterId = 0, warehouseOneId = 0, warehouseTwoId = 0;
        int approveRaceId = 0, cancelRaceId = 0;
        await using (var seed = CreateContext())
        {
            var role = new Role { RoleName = $"QA-COMMAND-RACE-{suffix}" };
            seed.Roles.Add(role); await seed.SaveChangesAsync(); roleId = role.Id;
            var maker = new User { Username = $"qa-command-maker-{suffix}", PasswordHash = "test-only", RoleId = role.Id };
            var approver = new User { Username = $"qa-command-approve-{suffix}", PasswordHash = "test-only", RoleId = role.Id };
            var rejecter = new User { Username = $"qa-command-reject-{suffix}", PasswordHash = "test-only", RoleId = role.Id };
            seed.Users.AddRange(maker, approver, rejecter); await seed.SaveChangesAsync();
            makerId = maker.Id; approverId = approver.Id; rejecterId = rejecter.Id;
            var one = new Warehouse { Code = $"QA-CMD-S-{suffix}", Name = "Command Race Source" };
            var two = new Warehouse { Code = $"QA-CMD-D-{suffix}", Name = "Command Race Destination" };
            seed.Warehouses.AddRange(one, two); await seed.SaveChangesAsync(); warehouseOneId = one.Id; warehouseTwoId = two.Id;
            foreach (var userId in new[] { approver.Id, rejecter.Id })
                seed.UserWarehouses.AddRange(
                    new UserWarehouse { UserId = userId, WarehouseId = one.Id, CreatedBy = maker.Id },
                    new UserWarehouse { UserId = userId, WarehouseId = two.Id, CreatedBy = maker.Id });
            var approveRace = new StockTransfer { Code = $"QA-APR-REJ-{suffix}", SourceWarehouseId = one.Id, DestinationWarehouseId = two.Id, CreatedBy = maker.Id };
            var cancelRace = new StockTransfer { Code = $"QA-CAN-REJ-{suffix}", SourceWarehouseId = one.Id, DestinationWarehouseId = two.Id, CreatedBy = maker.Id };
            seed.StockTransfers.AddRange(approveRace, cancelRace); await seed.SaveChangesAsync();
            approveRaceId = approveRace.Id; cancelRaceId = cancelRace.Id;
        }

        try
        {
            async Task<Exception?> Approve(int id)
            {
                await using var db = CreateContext(); var user = new Current(approverId);
                try { await new StockTransferService(db, Mock.Of<IInventoryStockRepository>(), new WarehouseAuthorizationService(db, user), user).ApproveAsync(id); return null; }
                catch (Exception error) { return error; }
            }
            async Task<Exception?> Cancel(int id)
            {
                await using var db = CreateContext(); var user = new Current(approverId);
                try { await new StockTransferService(db, Mock.Of<IInventoryStockRepository>(), new WarehouseAuthorizationService(db, user), user).CancelAsync(id); return null; }
                catch (Exception error) { return error; }
            }
            async Task<Exception?> Reject(int id)
            {
                await using var db = CreateContext(); var user = new Current(rejecterId);
                try { await new ApprovalWorkflowService(db, user, new WarehouseAuthorizationService(db, user), new Metadata { CorrelationId = Guid.NewGuid().ToString("N"), IdempotencyKeyHash = "hash", RequestFingerprint = "fingerprint" }).RejectAsync("StockTransfer", id, "Không đạt yêu cầu"); return null; }
                catch (Exception error) { return error; }
            }

            var approveReject = await Task.WhenAll(Approve(approveRaceId), Reject(approveRaceId));
            approveReject.Count(x => x is null).Should().Be(1);
            approveReject.Count(x => x is ERP.Domain.Exceptions.ConcurrencyException).Should().Be(1);
            var cancelReject = await Task.WhenAll(Cancel(cancelRaceId), Reject(cancelRaceId));
            cancelReject.Count(x => x is null).Should().Be(1);
            cancelReject.Count(x => x is ERP.Domain.Exceptions.ConcurrencyException).Should().Be(1);

            await using var verify = CreateContext();
            var approveFinal = await verify.StockTransfers.Where(x => x.Id == approveRaceId).Select(x => x.Status).SingleAsync();
            new[] { StockTransferStatus.Approved, StockTransferStatus.Cancelled }.Should().Contain(approveFinal);
            (await verify.AuditLogs.CountAsync(x => x.EntityName == "StockTransfer" && x.EntityId == approveRaceId && (x.Action == "StockTransfer.Approved" || x.Action == "ApprovalRejected"))).Should().Be(1);
            (await verify.StockTransfers.Where(x => x.Id == cancelRaceId).Select(x => x.Status).SingleAsync()).Should().Be(StockTransferStatus.Cancelled);
            (await verify.AuditLogs.CountAsync(x => x.EntityName == "StockTransfer" && x.EntityId == cancelRaceId && (x.Action == "StockTransfer.Cancelled" || x.Action == "ApprovalRejected"))).Should().Be(1);
        }
        finally
        {
            await using var cleanup = CreateContext();
            var transfers = new[] { approveRaceId, cancelRaceId };
            await cleanup.AuditLogs.Where(x => x.EntityName == "StockTransfer" && x.EntityId.HasValue && transfers.Contains(x.EntityId.Value)).ExecuteDeleteAsync();
            await cleanup.StockTransfers.Where(x => transfers.Contains(x.Id)).ExecuteDeleteAsync();
            var users = new[] { makerId, approverId, rejecterId };
            await cleanup.UserWarehouses.Where(x => users.Contains(x.UserId)).ExecuteDeleteAsync();
            await cleanup.Warehouses.Where(x => x.Id == warehouseOneId || x.Id == warehouseTwoId).ExecuteDeleteAsync();
            await cleanup.Users.Where(x => users.Contains(x.Id)).ExecuteDeleteAsync();
            await cleanup.Roles.Where(x => x.Id == roleId).ExecuteDeleteAsync();
        }
    }

    [SqlServerFact]
    public async Task ConcurrentRejects_CommitOneStateAndOneBusinessAudit()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        int roleId = 0, makerId = 0, checkerOneId = 0, checkerTwoId = 0, warehouseId = 0, receiptId = 0;
        await using (var seed = CreateContext())
        {
            var role = new Role { RoleName = $"QA-RACE-{suffix}" }; seed.Roles.Add(role); await seed.SaveChangesAsync(); roleId = role.Id;
            var maker = new User { Username = $"qa-race-maker-{suffix}", FullName = "Maker", PasswordHash = "test-only", RoleId = role.Id };
            var one = new User { Username = $"qa-race-one-{suffix}", FullName = "One", PasswordHash = "test-only", RoleId = role.Id };
            var two = new User { Username = $"qa-race-two-{suffix}", FullName = "Two", PasswordHash = "test-only", RoleId = role.Id };
            seed.Users.AddRange(maker, one, two); await seed.SaveChangesAsync(); makerId = maker.Id; checkerOneId = one.Id; checkerTwoId = two.Id;
            var warehouse = new Warehouse { Code = $"QA-RACE-{suffix}", Name = "Race Warehouse" }; seed.Warehouses.Add(warehouse); await seed.SaveChangesAsync(); warehouseId = warehouse.Id;
            seed.UserWarehouses.AddRange(new UserWarehouse { UserId = one.Id, WarehouseId = warehouse.Id, CreatedBy = maker.Id }, new UserWarehouse { UserId = two.Id, WarehouseId = warehouse.Id, CreatedBy = maker.Id });
            var receipt = new ImportReceipt { Code = $"QA-RACE-IMP-{suffix}", WarehouseId = warehouse.Id, CreatedBy = maker.Id }; seed.ImportReceipts.Add(receipt); await seed.SaveChangesAsync(); receiptId = receipt.Id;
        }
        try
        {
            async Task<Exception?> Attempt(int userId)
            {
                await using var db = CreateContext(); var user = new Current(userId);
                try { await new ApprovalWorkflowService(db, user, new WarehouseAuthorizationService(db, user), new Metadata { CorrelationId = Guid.NewGuid().ToString("N"), IdempotencyKeyHash = "hash", RequestFingerprint = "fp" }).RejectAsync("ImportReceipt", receiptId, "Không đạt yêu cầu"); return null; }
                catch (Exception error) { return error; }
            }
            var outcomes = await Task.WhenAll(Attempt(checkerOneId), Attempt(checkerTwoId));
            outcomes.Count(x => x is null).Should().Be(1);
            outcomes.Count(x => x is ERP.Domain.Exceptions.ConcurrencyException).Should().Be(1);
            await using var verify = CreateContext();
            (await verify.ImportReceipts.Where(x => x.Id == receiptId).Select(x => x.Status).SingleAsync()).Should().Be(ReceiptStatus.Cancelled);
            (await verify.AuditLogs.CountAsync(x => x.EntityName == "ImportReceipt" && x.EntityId == receiptId && x.Action == "ApprovalRejected")).Should().Be(1);
        }
        finally
        {
            await using var cleanup = CreateContext();
            await cleanup.AuditLogs.Where(x => x.EntityName == "ImportReceipt" && x.EntityId == receiptId).ExecuteDeleteAsync();
            await cleanup.ImportReceipts.Where(x => x.Id == receiptId).ExecuteDeleteAsync();
            var users = new[] { makerId, checkerOneId, checkerTwoId };
            await cleanup.UserWarehouses.Where(x => users.Contains(x.UserId)).ExecuteDeleteAsync();
            await cleanup.Warehouses.Where(x => x.Id == warehouseId).ExecuteDeleteAsync();
            await cleanup.Users.Where(x => users.Contains(x.Id)).ExecuteDeleteAsync();
            await cleanup.Roles.Where(x => x.Id == roleId).ExecuteDeleteAsync();
        }
    }

    [SqlServerFact]
    public async Task QueueRejectAndHistory_ScopeFourTypesWithoutInventoryMutation()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var role = new Role { RoleName = $"QA-MANAGER-{suffix}" };
        db.Roles.Add(role); await db.SaveChangesAsync();
        var creator = new User { Username = $"qa-maker-{suffix}", FullName = "QA Maker", PasswordHash = "test-only", RoleId = role.Id };
        var checker = new User { Username = $"qa-checker-{suffix}", FullName = "QA Checker", PasswordHash = "test-only", RoleId = role.Id };
        db.Users.AddRange(creator, checker); await db.SaveChangesAsync();
        var source = new Warehouse { Code = $"QA-S-{suffix}", Name = "QA Source" };
        var destination = new Warehouse { Code = $"QA-D-{suffix}", Name = "QA Destination" };
        var outside = new Warehouse { Code = $"QA-O-{suffix}", Name = "QA Outside" };
        db.Warehouses.AddRange(source, destination, outside); await db.SaveChangesAsync();
        db.UserWarehouses.AddRange(
            new UserWarehouse { UserId = checker.Id, WarehouseId = source.Id, CreatedBy = creator.Id },
            new UserWarehouse { UserId = checker.Id, WarehouseId = destination.Id, CreatedBy = creator.Id });
        var import = new ImportReceipt { Code = $"QA-IMP-{suffix}", WarehouseId = source.Id, CreatedBy = creator.Id };
        var export = new ExportReceipt { Code = $"QA-EXP-{suffix}", WarehouseId = source.Id, CreatedBy = creator.Id };
        var stocktake = new Stocktake { Code = $"QA-STK-{suffix}", WarehouseId = source.Id, CreatedBy = creator.Id };
        var transfer = new StockTransfer { Code = $"QA-TRF-{suffix}", SourceWarehouseId = source.Id, DestinationWarehouseId = destination.Id, CreatedBy = creator.Id };
        var own = new ImportReceipt { Code = $"QA-SELF-{suffix}-X", WarehouseId = source.Id, CreatedBy = checker.Id };
        var hidden = new ImportReceipt { Code = $"QA-HIDDEN-{suffix}-X", WarehouseId = outside.Id, CreatedBy = creator.Id };
        db.AddRange(import, export, stocktake, transfer, own, hidden); await db.SaveChangesAsync();

        var before = await InventorySnapshotAsync(db);
        var current = new Current(checker.Id);
        var metadata = new Metadata { CorrelationId = "qa-correlation", IdempotencyKeyHash = "hashed", RequestFingerprint = "fingerprint" };
        var service = new ApprovalWorkflowService(db, current, new WarehouseAuthorizationService(db, current), metadata);
        var staff = new Current(checker.Id, "WarehouseStaff");
        await FluentActions.Awaiting(() => new ApprovalWorkflowService(db, staff, new WarehouseAuthorizationService(db, staff), metadata).GetQueueAsync(new ApprovalQueueQuery()))
            .Should().ThrowAsync<ERP.Application.Exceptions.ForbiddenException>();
        var queue = await service.GetQueueAsync(new ApprovalQueueQuery { PageSize = 20 });
        queue.Items.Where(x => x.DocumentCode.EndsWith(suffix)).Should().HaveCount(4).And.OnlyContain(x => x.CanApprove && x.CanReject);
        queue.Items.Should().ContainSingle(x => x.DocumentCode == own.Code && !x.CanApprove && !x.CanReject && x.DeniedReasonCode == "CREATOR_CANNOT_CHECK");
        queue.Items.Should().NotContain(x => x.DocumentCode == hidden.Code);
        await FluentActions.Awaiting(() => service.RejectAsync("ImportReceipt", own.Id, "Không đạt yêu cầu"))
            .Should().ThrowAsync<ERP.Application.Exceptions.ForbiddenException>();
        await FluentActions.Awaiting(() => service.RejectAsync("ImportReceipt", hidden.Id, "Không đạt yêu cầu"))
            .Should().ThrowAsync<ERP.Application.Exceptions.NotFoundException>();
        (await service.GetQueueAsync(new ApprovalQueueQuery { DocumentType = "ExportReceipt", CreatorId = creator.Id, WarehouseId = source.Id, Keyword = "QA-EXP", FromUtc = DateTime.UtcNow.AddMinutes(-5), ToUtc = DateTime.UtcNow.AddMinutes(5), PageSize = 1 })).Items
            .Should().ContainSingle(x => x.DocumentCode == export.Code);

        foreach (var item in queue.Items.Where(x => x.DocumentCode.EndsWith(suffix)))
        {
            var result = await service.RejectAsync(item.DocumentType, item.DocumentId, "  Không đạt yêu cầu QA  ");
            result.OldState.Should().Be("Draft"); result.NewState.Should().Be("Cancelled"); result.DisplayStatus.Should().Be("Rejected");
        }

        (await service.GetQueueAsync(new ApprovalQueueQuery { PageSize = 20 })).Items.Should().NotContain(x => x.DocumentCode.EndsWith(suffix));
        var history = await service.GetHistoryAsync(new ApprovalHistoryQuery { Action = "ApprovalRejected", PageSize = 20 });
        history.Items.Where(x => new[] { import.Id, export.Id, stocktake.Id, transfer.Id }.Contains(x.DocumentId))
            .Should().HaveCount(4).And.OnlyContain(x => x.DisplayAction == "Bị từ chối" && x.Reason == "Không đạt yêu cầu QA" && x.CorrelationId == "qa-correlation");
        (await InventorySnapshotAsync(db)).Should().Be(before);
        (await db.AuditLogs.CountAsync(x => x.Action == "ApprovalRejected" && new[] { import.Id, export.Id, stocktake.Id, transfer.Id }.Contains(x.EntityId!.Value))).Should().Be(4);
        await transaction.RollbackAsync();
    }

    private static async Task<(decimal OnHand, decimal Reserved, int Transactions, int Reservations)> InventorySnapshotAsync(ErpKhoDbContext db) =>
        (await db.InventoryStocks.SumAsync(x => x.Quantity), await db.InventoryStocks.SumAsync(x => x.ReservedQuantity), await db.InventoryTransactions.CountAsync(), await db.StockReservations.CountAsync());
    private static ErpKhoDbContext CreateContext() => new(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!).Options);
    private sealed record Current(int UserId, string CurrentRole = "Manager") : ICurrentUser { public bool IsAuthenticated => true; public bool IsGlobalAdmin => false; public string Role => CurrentRole; }
    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider { public override DateTimeOffset GetUtcNow() => value; }
    private sealed class Metadata : IRequestMetadata { public string CorrelationId { get; set; } = string.Empty; public string? IdempotencyKeyHash { get; set; } public string? RequestFingerprint { get; set; } }
}
