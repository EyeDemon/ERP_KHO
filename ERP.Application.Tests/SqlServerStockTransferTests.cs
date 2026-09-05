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

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerStockTransferTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

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
        db.UserWarehouses.AddRange(
            new UserWarehouse { UserId = creator.Id, WarehouseId = source.Id, CreatedBy = creator.Id },
            new UserWarehouse { UserId = creator.Id, WarehouseId = destination.Id, CreatedBy = creator.Id },
            new UserWarehouse { UserId = approver.Id, WarehouseId = source.Id, CreatedBy = creator.Id },
            new UserWarehouse { UserId = approver.Id, WarehouseId = destination.Id, CreatedBy = creator.Id });
        db.InventoryStocks.AddRange(new InventoryStock { ProductId = product.Id, WarehouseId = source.Id, Quantity = sourceQuantity }, new InventoryStock { ProductId = product.Id, WarehouseId = destination.Id, Quantity = 0 });
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
        await db.UserWarehouses.Where(x => userIds.Contains(x.UserId)).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == fixture.ProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == fixture.UnitId).ExecuteDeleteAsync();
        await db.Warehouses.Where(x => x.Id == fixture.SourceId || x.Id == fixture.DestinationId).ExecuteDeleteAsync();
        await db.Users.Where(x => userIds.Contains(x.Id)).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == fixture.RoleId).ExecuteDeleteAsync();
    }

    private static Task<decimal> StockAsync(ErpKhoDbContext db, int product, int warehouse) => db.InventoryStocks.Where(x => x.ProductId == product && x.WarehouseId == warehouse).Select(x => x.Quantity).SingleAsync();
    private static ErpKhoDbContext CreateContext() => new(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(ConnectionString).Options);
    private sealed record TestCurrentUser(int UserId, bool IsGlobalAdmin, string Role) : ICurrentUser { public bool IsAuthenticated => true; }
    private sealed record Fixture(int CreatorId, int UserId, int RoleId, int UnitId, int ProductId, int SourceId, int DestinationId);
}
