using System.Linq.Expressions;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ERP.Application.Tests;

public sealed class Wave4SectorARemediationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<ErpKhoDbContext> _options;

    public Wave4SectorARemediationTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task W4_SEC_A_01a_RecountStocktakeDetailAsync_RefreshesSystemQuantitySnapshot_AllowingSubsequentApproval()
    {
        var stocktakeRepo = new Mock<IStocktakeRepository>();
        var stockRepo = new Mock<IInventoryStockRepository>();
        var txRepo = new Mock<IInventoryTransactionRepository>();
        var warehouseRepo = new Mock<IWarehouseRepository>();
        var uow = new Mock<IUnitOfWork>();
        var auditRepo = new Mock<IAuditLogRepository>();

        var stocktake = new Stocktake
        {
            Id = 10,
            Code = "KK-01",
            WarehouseId = 1,
            Status = StocktakeStatus.Draft,
            CreatedBy = 100,
            Details =
            [
                new StocktakeDetail
                {
                    Id = 101,
                    StocktakeId = 10,
                    ProductId = 5,
                    LocationId = 11,
                    SystemQuantity = 100m,
                    ActualQuantity = 100m,
                    DifferenceQuantity = 0m
                }
            ]
        };

        // Intervening export reduced actual stock from 100 to 90
        var liveStock = new InventoryStock
        {
            Id = 1,
            ProductId = 5,
            WarehouseId = 1,
            LocationId = 11,
            Status = InventoryStatus.Available,
            Quantity = 90m,
            ReservedQuantity = 0m
        };

        stocktakeRepo.Setup(r => r.GetByIdWithDetailsAsync(10)).ReturnsAsync(stocktake);
        stockRepo.Setup(r => r.GetByProductWarehouseStatusAndLocationAsync(5, 1, InventoryStatus.Available, 11))
            .ReturnsAsync(liveStock);
        stockRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<InventoryStock, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([liveStock]);

        var service = new StocktakeService(
            stocktakeRepo.Object,
            stockRepo.Object,
            txRepo.Object,
            warehouseRepo.Object,
            uow.Object,
            auditRepo.Object);

        // Before recount: approval fails with 409 because currentQuantity (90) != SystemQuantity (100)
        var initialApprove = () => service.ApproveStocktakeAsync(10, approvedByUserId: 200);
        await initialApprove.Should().ThrowAsync<BusinessRuleException>()
            .Where(ex => Equals(ex.Data["HttpStatusCode"], 409));

        // Reset status to Draft after simulated failure in in-memory object
        stocktake.Status = StocktakeStatus.Draft;

        // Recount refreshes SystemQuantity to 90 and resets ActualQuantity
        await service.RecountStocktakeDetailAsync(10, 101, "Giao dịch xuất chen ngang", userId: 200);
        stocktake.Details.Single().SystemQuantity.Should().Be(90m);
        stocktake.Details.Single().ActualQuantity.Should().BeNull();

        // Operator enters new physical count (88) and manager approves
        await service.UpdateStocktakeDetailAsync(10, 101, new UpdateStocktakeDetailDto { ActualQuantity = 88m, Note = "Đã đếm lại" });
        await service.ApproveStocktakeAsync(10, approvedByUserId: 200);

        stocktake.Status.Should().Be(StocktakeStatus.Approved);
        liveStock.Quantity.Should().Be(88m);
    }

    [Fact]
    public async Task W4_SEC_A_01b_UpdateStocktakeDetailAsync_RejectsFractionalQuantityExceedingBaseUomDecimalPlaces()
    {
        var stocktakeRepo = new Mock<IStocktakeRepository>();
        var stockRepo = new Mock<IInventoryStockRepository>();
        var txRepo = new Mock<IInventoryTransactionRepository>();
        var warehouseRepo = new Mock<IWarehouseRepository>();
        var uow = new Mock<IUnitOfWork>();
        var auditRepo = new Mock<IAuditLogRepository>();

        var stocktake = new Stocktake
        {
            Id = 20,
            WarehouseId = 1,
            Status = StocktakeStatus.Draft,
            Details =
            [
                new StocktakeDetail
                {
                    Id = 201,
                    StocktakeId = 20,
                    ProductId = 7,
                    SystemQuantity = 10m,
                    Product = new Product
                    {
                        Id = 7,
                        Code = "SKU-EA",
                        Unit = new Unit { Id = 1, Code = "EA", Name = "Cái", DecimalPlaces = 0 }
                    }
                }
            ]
        };
        stocktakeRepo.Setup(r => r.GetByIdWithDetailsAsync(20)).ReturnsAsync(stocktake);

        var service = new StocktakeService(
            stocktakeRepo.Object,
            stockRepo.Object,
            txRepo.Object,
            warehouseRepo.Object,
            uow.Object,
            auditRepo.Object);

        var act = () => service.UpdateStocktakeDetailAsync(20, 201, new UpdateStocktakeDetailDto { ActualQuantity = 9.5m });
        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*0 chữ số thập phân*");
    }

    [Fact]
    public async Task W4_SEC_A_02_ImportReceiptPostAsync_ProcessesLinesInDeterministicProductOrder()
    {
        var receiptRepo = new Mock<IImportReceiptRepository>();
        var productRepo = new Mock<IProductRepository>();
        var stockRepo = new Mock<IInventoryStockRepository>();
        var txRepo = new Mock<IInventoryTransactionRepository>();
        var warehouseRepo = new Mock<IWarehouseRepository>();
        var uow = new Mock<IUnitOfWork>();
        var auditRepo = new Mock<IAuditLogRepository>();

        var receipt = new ImportReceipt
        {
            Id = 30,
            WarehouseId = 1,
            CreatedBy = 10,
            Status = ReceiptStatus.ReadyToPost,
            Details =
            [
                new ImportReceiptDetail { Id = 1, ProductId = 20, BaseReceivedQuantity = 5, BaseAcceptedQuantity = 5, AcceptedQuantity = 5 },
                new ImportReceiptDetail { Id = 2, ProductId = 10, BaseReceivedQuantity = 3, BaseAcceptedQuantity = 3, AcceptedQuantity = 3 }
            ]
        };
        receiptRepo.Setup(r => r.GetByIdWithDetailsAsync(30)).ReturnsAsync(receipt);

        var lockedProductOrder = new List<int>();
        stockRepo.Setup(r => r.GetByProductAndWarehouseAsync(It.IsAny<int>(), 1))
            .Callback<int, int>((productId, _) => lockedProductOrder.Add(productId))
            .ReturnsAsync((int productId, int _) => new InventoryStock { ProductId = productId, WarehouseId = 1, Quantity = 0 });

        var service = new ImportReceiptService(
            receiptRepo.Object,
            stockRepo.Object,
            txRepo.Object,
            warehouseRepo.Object,
            productRepo.Object,
            uow.Object,
            auditRepo.Object);

        await service.PostAsync(30, postedByUserId: 20);

        lockedProductOrder.Should().Equal(10, 20);
    }

    [Fact]
    public async Task W4_SEC_A_03_ReceivingDiscrepancyService_HandlesRepeatingDecimalsAndZeroConversionFactor()
    {
        var seeded = await SeedDiscrepancyScenarioAsync();
        await using var db = CreateContext();
        var service = new ReceivingDiscrepancyService(db, new WarehouseAccess(seeded.WarehouseId), new Current(seeded.MakerId, "Manager"));

        // Observe 10 EA when 1 BOX (12 EA) was expected -> repeating decimal 10/12 in OperationUom
        var observed = (await service.ObserveAsync(seeded.ReceiptId, new ObserveReceivingDto
        {
            Lines = [new() { LineId = seeded.LineId, ObservedQuantity = 10, ObservedUnitId = seeded.EachUnitId }]
        }, default)).Single();

        var resolved = await service.SubmitAsync(seeded.ReceiptId, observed.Id, new SubmitReceivingDiscrepancyDto
        {
            Action = "ACCEPT_OBSERVED",
            ReasonCode = "UNDER_RECEIPT",
            ResponsibleParty = ResponsibleParty.Supplier,
            ConfirmNormalizedObservation = true,
            DoorRejectedQuantity = 0m,
            Note = "Thiếu 2 cái trong thùng 12 cái",
            RowVersion = observed.RowVersion
        }, default);

        resolved.Status.Should().Be(nameof(ReceivingDiscrepancyStatus.Resolved));
        var line = await db.ImportReceiptDetails.SingleAsync(x => x.Id == seeded.LineId);
        line.BaseFinalReceivedQuantity.Should().Be(10m);
        line.FinalReceivedQuantity.Should().Be(0.8333m);

        // Zero ConversionFactor guard: should not throw DivideByZeroException
        line.ConversionFactor = 0m;
        await db.SaveChangesAsync();
        var fetched = await service.GetAsync(seeded.ReceiptId, default);
        fetched.Single().NormalizedObservedQuantity.Should().Be(10m);
    }

    [Fact]
    public async Task W4_SEC_A_04_PutawayUpdateLocationAsync_BlocksDeactivationOrBlockingWhenReservedStockExists()
    {
        var seeded = await SeedDiscrepancyScenarioAsync();
        await using var db = CreateContext();
        var bin = new WarehouseLocation
        {
            WarehouseId = seeded.WarehouseId,
            Code = "BIN-RES-01",
            Name = "Kệ có giữ hàng",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            IsReceivable = false
        };
        db.WarehouseLocations.Add(bin);
        await db.SaveChangesAsync();

        db.InventoryStocks.Add(new InventoryStock
        {
            WarehouseId = seeded.WarehouseId,
            LocationId = bin.Id,
            ProductId = seeded.ProductId,
            Status = InventoryStatus.Available,
            Quantity = 8m,
            ReservedQuantity = 4m
        });
        await db.SaveChangesAsync();

        var putawayService = new PutawayService(db, new WarehouseAccess(seeded.WarehouseId), new Current(seeded.MakerId, "Manager"));
        var currentBin = (await putawayService.ListLocationsAsync(seeded.WarehouseId)).Single(x => x.Id == bin.Id);

        var blockAction = () => putawayService.UpdateLocationAsync(bin.Id, new UpdateWarehouseLocationDto
        {
            Name = currentBin.Name,
            IsActive = true,
            IsBlocked = true,
            IsPickable = true,
            RowVersion = currentBin.RowVersion!
        });
        await blockAction.Should().ThrowAsync<BusinessRuleException>()
            .Where(ex => ex.Data["HttpStatusCode"] as int? == 409);

        var deactivateAction = () => putawayService.UpdateLocationAsync(bin.Id, new UpdateWarehouseLocationDto
        {
            Name = currentBin.Name,
            IsActive = false,
            IsBlocked = false,
            IsPickable = true,
            RowVersion = currentBin.RowVersion!
        });
        await deactivateAction.Should().ThrowAsync<BusinessRuleException>()
            .Where(ex => ex.Data["HttpStatusCode"] as int? == 409);
    }

    [Fact]
    public async Task W4_SEC_A_05_ProductService_UpdateProductAsync_BlocksBaseUomChangeAndDeactivationWhenTransactionsExist()
    {
        var mockRepo = new Mock<IProductRepository>();
        var product = new Product { Id = 50, Code = "P50", Name = "Sản phẩm 50", UnitId = 1, IsActive = true };
        mockRepo.Setup(r => r.GetByIdAsync(50, It.IsAny<CancellationToken>())).ReturnsAsync(product);
        mockRepo.Setup(r => r.HasTransactionsAsync(50, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var service = new ProductService(mockRepo.Object, new Mock<IProductCatalogService>().Object);

        var changeUom = () => service.UpdateProductAsync(50, new UpdateProductDto
        {
            Name = "Sản phẩm 50",
            UnitId = 2,
            IsActive = true
        }, "admin");
        await changeUom.Should().ThrowAsync<BusinessRuleException>().WithMessage("*đơn vị tính cơ sở*");

        var deactivate = () => service.UpdateProductAsync(50, new UpdateProductDto
        {
            Name = "Sản phẩm 50",
            UnitId = 1,
            IsActive = false
        }, "admin");
        await deactivate.Should().ThrowAsync<BusinessRuleException>().WithMessage("*ngừng hoạt động sản phẩm*");
    }

    [Fact]
    public async Task W4_SEC_A_06_WarehouseService_UpdateWarehouseAsync_BlocksDeactivationWhenActiveStockExists()
    {
        var warehouseRepo = new Mock<IWarehouseRepository>();
        var stockRepo = new Mock<IInventoryStockRepository>();
        var warehouse = new Warehouse { Id = 7, Code = "WH07", Name = "Kho 07", IsActive = true };

        warehouseRepo.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(warehouse);
        stockRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<InventoryStock, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new InventoryStock { Id = 1, WarehouseId = 7, ProductId = 1, Quantity = 5m, ReservedQuantity = 0m }]);

        var service = new WarehouseService(warehouseRepo.Object, stockRepo.Object);

        var act = () => service.UpdateWarehouseAsync(7, new UpdateWarehouseDto
        {
            Name = "Kho 07",
            Address = "HCM",
            IsActive = false
        }, "admin");

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*ngừng hoạt động kho*");
        warehouseRepo.Verify(r => r.UpdateAsync(It.IsAny<Warehouse>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private async Task<(int ReceiptId, int LineId, int WarehouseId, int MakerId, int EachUnitId, int BoxUnitId, int ProductId)> SeedDiscrepancyScenarioAsync()
    {
        await using var db = CreateContext();
        var role = new Role { RoleName = "Manager" };
        var maker = new User { Username = Guid.NewGuid().ToString("N"), PasswordHash = "x", FullName = "Maker", Role = role };
        var each = new Unit { Code = "EA", Name = "Cái", DecimalPlaces = 0 };
        var box = new Unit { Code = "BOX", Name = "Thùng 12", DecimalPlaces = 4 };
        var warehouse = new Warehouse { Code = Guid.NewGuid().ToString("N")[..8], Name = "WH-A" };
        var product = new Product { Code = Guid.NewGuid().ToString("N")[..8], Name = "SKU-BOX12", Unit = each };
        db.AddRange(role, maker, each, box, warehouse, product);
        await db.SaveChangesAsync();

        db.ProductUoms.Add(new ProductUom
        {
            ProductId = product.Id,
            UnitId = each.Id,
            ConversionFactor = 1m,
            Version = 1,
            EffectiveFromUtc = DateTime.UtcNow.AddDays(-5),
            IsActive = true
        });
        db.ProductUoms.Add(new ProductUom
        {
            ProductId = product.Id,
            UnitId = box.Id,
            ConversionFactor = 12m,
            Version = 1,
            EffectiveFromUtc = DateTime.UtcNow.AddDays(-5),
            IsActive = true
        });

        db.ReceivingTolerancePolicies.RemoveRange(db.ReceivingTolerancePolicies);
        db.ReceivingTolerancePolicies.Add(new ReceivingTolerancePolicy
        {
            WarehouseId = warehouse.Id,
            Version = 1,
            EffectiveFromUtc = DateTime.UtcNow.AddDays(-5),
            AbsoluteQuantityTolerance = 5m,
            ValueTolerance = 50m,
            OverageAllowed = true,
            ShortageAllowed = true,
            RequiresApprovalOutsideTolerance = true,
            IsActive = true
        });

        var receipt = new ImportReceipt
        {
            Code = Guid.NewGuid().ToString("N"),
            WarehouseId = warehouse.Id,
            Status = ReceiptStatus.Draft,
            CreatedBy = maker.Id
        };
        var line = new ImportReceiptDetail
        {
            ImportReceipt = receipt,
            ProductId = product.Id,
            Quantity = 1m,
            ExpectedQuantity = 1m,
            BaseExpectedQuantity = 12m,
            OperationUnitId = box.Id,
            OperationUnitCodeSnapshot = box.Code,
            OperationUnitDecimalPlaces = 4,
            BaseUnitId = each.Id,
            BaseUnitCodeSnapshot = each.Code,
            BaseUnitDecimalPlaces = 0,
            ConversionFactor = 12m,
            ConversionVersion = 1,
            UnitPrice = 120m
        };
        db.Add(line);
        await db.SaveChangesAsync();

        return (receipt.Id, line.Id, warehouse.Id, maker.Id, each.Id, box.Id, product.Id);
    }

    private TestDbContext CreateContext() => new(_options);

    public void Dispose() => _connection.Dispose();

    private sealed record Current(int UserId, string Role) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public bool IsGlobalAdmin => false;
    }

    private sealed class TestDbContext(DbContextOptions<ErpKhoDbContext> options) : ErpKhoDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var entry in ChangeTracker.Entries<ReceivingDiscrepancy>().Where(x => x.State is EntityState.Added or EntityState.Modified))
                entry.Entity.RowVersion = Guid.NewGuid().ToByteArray();
            foreach (var entry in ChangeTracker.Entries<WarehouseLocation>().Where(x => x.State is EntityState.Added or EntityState.Modified))
                entry.Entity.RowVersion = Guid.NewGuid().ToByteArray();
            return base.SaveChangesAsync(cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(x => x.GetProperties()))
                if (property.GetColumnType()?.Contains("max", StringComparison.OrdinalIgnoreCase) == true)
                    property.SetColumnType(null);
            modelBuilder.Entity<ReceivingDiscrepancy>().Property(x => x.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
            modelBuilder.Entity<WarehouseLocation>().Property(x => x.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
        }
    }

    private sealed class WarehouseAccess(int id) : IWarehouseAuthorizationService
    {
        public Task<IReadOnlyList<int>> GetAccessibleWarehouseIdsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<int>>([id]);
        public Task<bool> CanAccessWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default) => Task.FromResult(warehouseId == id);
        public Task EnsureWarehouseAccessAsync(int warehouseId, CancellationToken cancellationToken = default) => warehouseId == id ? Task.CompletedTask : throw new UnauthorizedAccessException();
    }
}
