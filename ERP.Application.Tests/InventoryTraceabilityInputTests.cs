using ERP.Application.Interfaces;
using ERP.Application.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ERP.Application.Tests;

public sealed class InventoryTraceabilityInputTests
{
    [Theory]
    [InlineData(0, 1, null, null, "ID kho")]
    [InlineData(null, 0, null, null, "ID sản phẩm")]
    [InlineData(null, null, "InventoryReversal", 0, "ID tham chiếu")]
    [InlineData(-1, 1, null, null, "ID kho")]
    [InlineData(null, -1, null, null, "ID sản phẩm")]
    [InlineData(null, null, "InventoryReversal", -1, "ID tham chiếu")]
    public async Task InvalidIdentifiers_AreRejectedBeforeAnyWarehouseScopeQuery(
        int? warehouseId, int? productId, string? referenceType, int? referenceId, string expected)
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ErpKhoDbContext(options);
        var auth = new Mock<IWarehouseAuthorizationService>(MockBehavior.Strict);
        var query = new InventoryTraceabilityQueryService(db, auth.Object);

        var act = () => query.TraceAsync(
            warehouseId: warehouseId,
            productId: productId,
            referenceType: referenceType,
            referenceId: referenceId);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage($"*{expected}*");
        auth.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CompletelyEmptySearchIsRejectedBeforeAnyWarehouseAccess()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ErpKhoDbContext(options);
        var auth = new Mock<IWarehouseAuthorizationService>(MockBehavior.Strict);
        var act = () => new InventoryTraceabilityQueryService(db, auth.Object).TraceAsync();

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*Kho*");
        auth.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(-500)]
    [InlineData(10)]
    [InlineData(50_500)]
    public async Task InvalidStockPageOffsets_AreRejectedBeforeWarehouseQueries(int bucketOffset)
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ErpKhoDbContext(options);
        var auth = new Mock<IWarehouseAuthorizationService>(MockBehavior.Strict);
        var query = new InventoryTraceabilityQueryService(db, auth.Object);
        var act = () => query.TraceAsync(
            warehouseId: 9, bucketOffset: bucketOffset);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*Trang nhóm tồn*");
        auth.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(-1, 200)]
    [InlineData(1, 200)]
    [InlineData(201, 50)]
    [InlineData(50_200, 200)]
    public async Task InvalidLedgerEventOffsets_AreRejectedBeforeWarehouseQueries(int eventOffset, int limit)
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ErpKhoDbContext(options);
        var auth = new Mock<IWarehouseAuthorizationService>(MockBehavior.Strict);
        var query = new InventoryTraceabilityQueryService(db, auth.Object);

        var act = () => query.TraceAsync(
            warehouseId: 9, limit: limit, eventOffset: eventOffset);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*Trang sự kiện sổ cái*");
        auth.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReferenceTypeAndReferenceId_MustAppearTogether()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ErpKhoDbContext(options);
        var auth = new Mock<IWarehouseAuthorizationService>(MockBehavior.Strict);
        var query = new InventoryTraceabilityQueryService(db, auth.Object);

        var act = () => query.TraceAsync(referenceType: "InventoryReversal");
        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*nhập cùng nhau*");
        auth.VerifyNoOtherCalls();
    }
}
