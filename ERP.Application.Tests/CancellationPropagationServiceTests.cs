using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Domain.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace ERP.Application.Tests
{
    public class CancellationPropagationServiceTests
    {
        [Fact]
        public async Task ProductService_PropagatesCancellationToken_ToRepository()
        {
            // Arrange
            var mockRepo = new Mock<IProductRepository>();
            var service = new ProductService(mockRepo.Object);
            using var cts = new CancellationTokenSource();
            var token = cts.Token;

            var product = new Product { Id = 10, Code = "P10", Name = "Product 10" };
            mockRepo.Setup(r => r.GetProductsWithDetailsAsync(token))
                .ReturnsAsync(new List<Product> { product });
            mockRepo.Setup(r => r.GetPagedAsync(1, 20, "kw", token))
                .ReturnsAsync(((IReadOnlyList<Product>)new List<Product> { product }, 1));
            mockRepo.Setup(r => r.GetByIdAsync(10, token))
                .ReturnsAsync(product);
            mockRepo.Setup(r => r.ExistsByCodeAsync("P10", null, token))
                .ReturnsAsync(false);
            mockRepo.Setup(r => r.AddAsync(It.IsAny<Product>(), token))
                .Callback<Product, CancellationToken>((p, ct) => p.Id = 10)
                .ReturnsAsync((Product p, CancellationToken ct) => p);
            mockRepo.Setup(r => r.UpdateAsync(It.IsAny<Product>(), token))
                .Returns(Task.CompletedTask);
            mockRepo.Setup(r => r.HasTransactionsAsync(10, token))
                .ReturnsAsync(false);
            mockRepo.Setup(r => r.DeleteAsync(It.IsAny<Product>(), token))
                .Returns(Task.CompletedTask);

            // Act & Assert
            var all = await service.GetAllProductsAsync(token);
            all.Should().HaveCount(1);
            mockRepo.Verify(r => r.GetProductsWithDetailsAsync(token), Times.Once);

            var paged = await service.GetPagedProductsAsync(1, 20, "kw", token);
            paged.Items.Should().HaveCount(1);
            mockRepo.Verify(r => r.GetPagedAsync(1, 20, "kw", token), Times.Once);

            var single = await service.GetProductByIdAsync(10, token);
            single.Should().NotBeNull();
            mockRepo.Verify(r => r.GetByIdAsync(10, token), Times.Once);

            var created = await service.CreateProductAsync(new CreateProductDto { Code = "P10", Name = "Product 10", UnitId = 1 }, "admin", token);
            created.Should().NotBeNull();
            mockRepo.Verify(r => r.ExistsByCodeAsync("P10", null, token), Times.Once);
            mockRepo.Verify(r => r.AddAsync(It.IsAny<Product>(), token), Times.Once);

            await service.UpdateProductAsync(10, new UpdateProductDto { Name = "Product 10 Updated", UnitId = 1 }, "admin", token);
            mockRepo.Verify(r => r.UpdateAsync(It.IsAny<Product>(), token), Times.Once);

            await service.DeleteProductAsync(10, token);
            mockRepo.Verify(r => r.HasTransactionsAsync(10, token), Times.Once);
            mockRepo.Verify(r => r.DeleteAsync(product, token), Times.Once);
        }

        [Fact]
        public async Task ReportService_PropagatesCancellationToken_ToRepository()
        {
            // Arrange
            var mockRepo = new Mock<IReportRepository>();
            var service = new ReportService(mockRepo.Object);
            using var cts = new CancellationTokenSource();
            var token = cts.Token;

            mockRepo.Setup(r => r.GetCurrentStocksAsync(1, 2, token))
                .ReturnsAsync(new List<InventoryStock>());
            mockRepo.Setup(r => r.GetTransactionsUpToDateAsync(It.IsAny<DateTime>(), 1, 2, token))
                .ReturnsAsync(new List<InventoryTransaction>());
            mockRepo.Setup(r => r.GetInventoryInOutReportAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), 1, 2, token))
                .ReturnsAsync(new List<InventoryInOutReportModel>());

            // Act & Assert
            await service.GetInventoryReportAsync(null, 1, 2, token);
            mockRepo.Verify(r => r.GetCurrentStocksAsync(1, 2, token), Times.Once);

            var asOfDate = new DateTime(2026, 1, 1);
            await service.GetInventoryReportAsync(asOfDate, 1, 2, token);
            mockRepo.Verify(r => r.GetTransactionsUpToDateAsync(asOfDate, 1, 2, token), Times.Once);

            await service.GetInventoryInOutReportAsync(null, null, 1, 2, token);
            mockRepo.Verify(r => r.GetInventoryInOutReportAsync(null, null, 1, 2, token), Times.Once);
        }

        [Fact]
        public async Task AuthService_PropagatesCancellationToken_ToUserRepository()
        {
            // Arrange
            var mockUserRepo = new Mock<IUserRepository>();
            var mockHasher = new Mock<IPasswordHasherService>();
            var mockTokenService = new Mock<ITokenService>();
            var service = new AuthService(mockUserRepo.Object, mockHasher.Object, mockTokenService.Object);
            using var cts = new CancellationTokenSource();
            var token = cts.Token;

            var user = new User { Id = 1, Username = "admin", PasswordHash = "HASH", IsActive = true };
            mockUserRepo.Setup(r => r.GetByUsernameWithRoleAsync("admin", token))
                .ReturnsAsync(user);
            mockHasher.Setup(h => h.VerifyPassword("HASH", "pass"))
                .Returns(true);
            mockTokenService.Setup(t => t.GenerateToken(user))
                .Returns("jwt.token");

            // Act
            var res = await service.LoginAsync(new LoginRequestDto { Username = "admin", Password = "pass" }, token);

            // Assert
            res.Should().NotBeNull();
            mockUserRepo.Verify(r => r.GetByUsernameWithRoleAsync("admin", token), Times.Once);
        }
    }
}
