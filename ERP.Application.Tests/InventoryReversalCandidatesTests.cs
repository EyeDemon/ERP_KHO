using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ERP.Application.Tests;

public sealed class InventoryReversalCandidatesTests
{
    [Fact]
    public async Task Candidates_PaginateAllRowsAndReadReversalMarkerOutsideFirstHundred()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var context = new ErpKhoDbContext(options);
        context.Units.Add(new Unit { Id = 1, Code = "PCS", Name = "Cái" });
        context.Products.Add(new Product { Id = 1, UnitId = 1, Code = "SKU", Name = "Sản phẩm" });
        context.Warehouses.AddRange(
            new Warehouse { Id = 1, Code = "W1", Name = "Kho được phép" },
            new Warehouse { Id = 2, Code = "W2", Name = "Kho không được phép" });
        context.Users.Add(new User { Id = 1, Username = "operator", FullName = "Vận hành" });

        var start = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 121; i++)
            context.InventoryTransactions.Add(new InventoryTransaction
            {
                Id = i, ProductId = 1, WarehouseId = 1, CreatedBy = 1,
                TransactionType = i % 2 == 0 ? TransactionType.StatusChange : TransactionType.Move,
                Quantity = 1, TransactionDate = start.AddMinutes(i)
            });
        context.InventoryTransactions.Add(new InventoryTransaction
        {
            Id = 200, ProductId = 1, WarehouseId = 1, CreatedBy = 1,
            TransactionType = TransactionType.Reversal,
            ReversalOfTransactionId = 1, CorrectiveTransactionId = 201,
            ReferenceId = 1, ReferenceType = "InventoryReversal", Quantity = 1
        });
        context.InventoryTransactions.Add(new InventoryTransaction
        {
            Id = 201, ProductId = 1, WarehouseId = 1, CreatedBy = 1,
            TransactionType = TransactionType.Move,
            Quantity = 1, TransactionDate = start.AddMinutes(122)
        });
        context.InventoryTransactions.Add(new InventoryTransaction
        {
            Id = 300, ProductId = 1, WarehouseId = 2, CreatedBy = 1,
            TransactionType = TransactionType.Move, Quantity = 1
        });
        await context.SaveChangesAsync();

        var auth = new Mock<IWarehouseAuthorizationService>();
        auth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<int> { 1 });
        var service = new InventoryReversalService(context, auth.Object, Mock.Of<ICurrentUser>(),
            Mock.Of<IInventoryMovementService>(), Mock.Of<IInventoryStatusService>());
        var first = await service.GetCandidatesAsync(null, 1, 20);
        first.TotalRecords.Should().Be(121);
        first.Items.Should().HaveCount(20);
        first.Items.Should().OnlyContain(x => x.WarehouseId == 1);
        first.Items[0].Id.Should().Be(121);
        first.Items.Select(x => x.Id).Should().NotContain(201);
        (await service.GetCandidatesAsync(null, 1, 100))
            .Items.Select(x => x.Id).Should().NotContain(201);
        var last = await service.GetCandidatesAsync(null, 7, 20);
        last.Items.Should().ContainSingle();
        last.Items[0].Id.Should().Be(1);
        last.Items[0].IsReversed.Should().BeTrue();
        (await service.GetCandidatesAsync(null, 1, 1000)).PageSize.Should().Be(20);
    }

    [Fact]
    public async Task Candidates_ExplicitWarehouseRequiresAccess()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var context = new ErpKhoDbContext(options);
        var auth = new Mock<IWarehouseAuthorizationService>();
        auth.Setup(x => x.EnsureWarehouseAccessAsync(2, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ERP.Application.Exceptions.NotFoundException("Không tìm thấy tài nguyên."));
        var service = new InventoryReversalService(context, auth.Object, Mock.Of<ICurrentUser>(),
            Mock.Of<IInventoryMovementService>(), Mock.Of<IInventoryStatusService>());

        await Assert.ThrowsAsync<ERP.Application.Exceptions.NotFoundException>(
            () => service.GetCandidatesAsync(2, 1, 20));
    }
}
