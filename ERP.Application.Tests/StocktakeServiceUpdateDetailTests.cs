using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;
using FluentAssertions;
using Moq;
using Xunit;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ERP.Application.Tests
{
    public class StocktakeServiceUpdateDetailTests
    {
        private readonly Mock<IStocktakeRepository> _mockStocktakeRepo;
        private readonly Mock<IInventoryStockRepository> _mockStockRepo;
        private readonly Mock<IInventoryTransactionRepository> _mockTransactionRepo;
        private readonly Mock<IUnitOfWork> _mockUnitOfWork;
        private readonly Mock<IWarehouseRepository> _mockWarehouseRepo;
        private readonly Mock<IAuditLogRepository> _mockAuditLogRepo;
        private readonly StocktakeService _service;

        public StocktakeServiceUpdateDetailTests()
        {
            _mockStocktakeRepo = new Mock<IStocktakeRepository>();
            _mockStockRepo = new Mock<IInventoryStockRepository>();
            _mockTransactionRepo = new Mock<IInventoryTransactionRepository>();
            _mockUnitOfWork = new Mock<IUnitOfWork>();
            _mockWarehouseRepo = new Mock<IWarehouseRepository>();
            _mockAuditLogRepo = new Mock<IAuditLogRepository>();

            _service = new StocktakeService(
                _mockStocktakeRepo.Object,
                _mockStockRepo.Object,
                _mockTransactionRepo.Object,
                _mockWarehouseRepo.Object,
                _mockUnitOfWork.Object,
                _mockAuditLogRepo.Object);
        }

        [Fact]
        public async Task UpdateStocktakeDetailAsync_NegativeQuantity_ThrowsBusinessRuleException()
        {
            var dto = new UpdateStocktakeDetailDto { ActualQuantity = -1 };
            await Assert.ThrowsAsync<BusinessRuleException>(() => _service.UpdateStocktakeDetailAsync(1, 1, dto));
        }

        [Fact]
        public async Task UpdateStocktakeDetailAsync_ValidRequest_UpdatesDetailAndSaves()
        {
            var detail = new StocktakeDetail { Id = 1, SystemQuantity = 10, ActualQuantity = null };
            var stocktake = new Stocktake 
            { 
                Id = 1, 
                Status = ReceiptStatus.Draft, 
                Details = new List<StocktakeDetail> { detail } 
            };

            _mockStocktakeRepo.Setup(r => r.GetByIdWithDetailsAsync(1)).ReturnsAsync(stocktake);

            var dto = new UpdateStocktakeDetailDto { ActualQuantity = 15, Note = "Test" };
            await _service.UpdateStocktakeDetailAsync(1, 1, dto);

            detail.ActualQuantity.Should().Be(15);
            detail.DifferenceQuantity.Should().Be(5);
            detail.Note.Should().Be("Test");

            _mockStocktakeRepo.Verify(r => r.UpdateAsync(stocktake), Times.Once);
            _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        }
    }
}



