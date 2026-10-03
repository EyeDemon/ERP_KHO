using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;
using FluentAssertions;
using Moq;
using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace ERP.Application.Tests
{
    public class StocktakeServiceCreateTests
    {
        private readonly Mock<IStocktakeRepository> _mockStocktakeRepo;
        private readonly Mock<IInventoryStockRepository> _mockStockRepo;
        private readonly Mock<IInventoryTransactionRepository> _mockTxRepo;
        private readonly Mock<IUnitOfWork> _mockUnitOfWork;
        private readonly Mock<IWarehouseRepository> _mockWarehouseRepo;
        private readonly Mock<IAuditLogRepository> _mockAuditLogRepo;
        private readonly StocktakeService _service;

        public StocktakeServiceCreateTests()
        {
            _mockStocktakeRepo = new Mock<IStocktakeRepository>();
            _mockStockRepo = new Mock<IInventoryStockRepository>();
            _mockTxRepo = new Mock<IInventoryTransactionRepository>();
            _mockUnitOfWork = new Mock<IUnitOfWork>();
            _mockWarehouseRepo = new Mock<IWarehouseRepository>();
            _mockAuditLogRepo = new Mock<IAuditLogRepository>();

            _service = new StocktakeService(
                _mockStocktakeRepo.Object,
                _mockStockRepo.Object,
                _mockTxRepo.Object,
                _mockWarehouseRepo.Object,
                _mockUnitOfWork.Object,
                _mockAuditLogRepo.Object);
        }

        [Fact]
        public async Task CreateStocktakeAsync_InvalidWarehouse_ThrowsNotFoundException_And_DoesNotMutateState()
        {
            var dto = new CreateStocktakeDto { WarehouseId = 99 };
            _mockWarehouseRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((Warehouse)null!);

            await Assert.ThrowsAsync<NotFoundException>(() => _service.CreateStocktakeAsync(dto, 1));
            
            _mockStocktakeRepo.Verify(r => r.AddAsync(It.IsAny<Stocktake>()), Times.Never);
            _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
            _mockAuditLogRepo.Verify(a => a.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task CreateStocktakeAsync_ValidWarehouse_CreatesSnapshot_And_AuditLog()
        {
            var warehouse = new Warehouse { Id = 1, Name = "Main" };
            _mockWarehouseRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(warehouse);

            var stocks = new List<InventoryStock>
            {
                new InventoryStock { ProductId = 10, Quantity = 50 },
                new InventoryStock { ProductId = 20, Quantity = 100 }
            };
            
            _mockStockRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<InventoryStock, bool>>>()))
                .ReturnsAsync(stocks);

            Stocktake? capturedStocktake = null;
            _mockStocktakeRepo.Setup(r => r.AddAsync(It.IsAny<Stocktake>(), It.IsAny<CancellationToken>()))
                .Callback<Stocktake, CancellationToken>((s, ct) => 
                {
                    capturedStocktake = s;
                    s.Id = 99; // Simulate DB generated ID
                });

            var dto = new CreateStocktakeDto { WarehouseId = 1, Note = "Test" };
            var result = await _service.CreateStocktakeAsync(dto, 1);

            result.Should().Be(99);

            capturedStocktake.Should().NotBeNull();
            capturedStocktake!.WarehouseId.Should().Be(1);
            capturedStocktake.Status.Should().Be(ReceiptStatus.Draft);
            capturedStocktake.Details.Should().HaveCount(2);
            
            var firstDetail = capturedStocktake.Details.ElementAt(0);
            firstDetail.ProductId.Should().Be(10);
            firstDetail.SystemQuantity.Should().Be(50);
            firstDetail.ActualQuantity.Should().BeNull("ActualQuantity must be null on creation");
            firstDetail.DifferenceQuantity.Should().Be(0);

            var secondDetail = capturedStocktake.Details.ElementAt(1);
            secondDetail.ProductId.Should().Be(20);
            secondDetail.SystemQuantity.Should().Be(100);
            secondDetail.ActualQuantity.Should().BeNull();
            secondDetail.DifferenceQuantity.Should().Be(0);

            _mockStockRepo.Verify(r => r.AddAsync(It.IsAny<InventoryStock>(), It.IsAny<CancellationToken>()), Times.Never, "Should not mutate stock on creation");
            _mockStockRepo.Verify(r => r.UpdateAsync(It.IsAny<InventoryStock>(), It.IsAny<CancellationToken>()), Times.Never, "Should not mutate stock on creation");
            _mockTxRepo.Verify(r => r.AddAsync(It.IsAny<InventoryTransaction>(), It.IsAny<CancellationToken>()), Times.Never, "Should not create transaction on creation");

            _mockUnitOfWork.Verify(u => u.BeginTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
            _mockUnitOfWork.Verify(u => u.CommitTransactionAsync(), Times.Once);

            _mockAuditLogRepo.Verify(r => r.AddAsync(It.Is<AuditLog>(a => 
                a.Action == "Stocktake.Created" && 
                a.EntityId == 99 && 
                a.UserId == 1)), Times.Once);
        }
        [Fact]
        public async Task CreateStocktakeAsync_AuditLogFails_RollsBackTransaction_And_Rethrows()
        {
            var warehouse = new Warehouse { Id = 1, Name = "Main" };
            _mockWarehouseRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(warehouse);

            var stocks = new List<InventoryStock>
            {
                new InventoryStock { ProductId = 10, Quantity = 50 }
            };
            
            _mockStockRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<InventoryStock, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(stocks);

            _mockStocktakeRepo.Setup(r => r.AddAsync(It.IsAny<Stocktake>(), It.IsAny<CancellationToken>()))
                .Callback<Stocktake, CancellationToken>((s, ct) => s.Id = 99);

            var expectedException = new Exception("Database connection failed during audit log write.");
            _mockAuditLogRepo.Setup(r => r.AddAsync(It.IsAny<AuditLog>()))
                .ThrowsAsync(expectedException);

            var dto = new CreateStocktakeDto { WarehouseId = 1, Note = "Test" };
            
            var act = async () => await _service.CreateStocktakeAsync(dto, 1);

            await act.Should().ThrowAsync<Exception>().WithMessage(expectedException.Message);

            _mockUnitOfWork.Verify(u => u.BeginTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once, "Saves the stocktake first to get ID");
            _mockUnitOfWork.Verify(u => u.CommitTransactionAsync(), Times.Never, "Commit must not be called if audit fails");
            _mockUnitOfWork.Verify(u => u.RollbackTransactionAsync(), Times.Once, "Transaction must be rolled back to discard the stocktake");
        }
    }
}
