using System;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERP.Application.Tests
{
    public class StocktakeQueryServiceTests
    {
        private ErpKhoDbContext GetContext()
        {
            var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            return new ErpKhoDbContext(options);
        }

        [Fact]
        public async Task GetStocktakesAsync_EmptyList_ReturnsEmpty()
        {
            var context = GetContext();
            var service = new StocktakeQueryService(context);
            var result = await service.GetStocktakesAsync();
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetStocktakesAsync_PopulatedList_ReturnsNewestFirst()
        {
            var context = GetContext();
            var w = new Warehouse { Id = 1, Name = "W1", Code = "W1" };
            var u = new User { Id = 1, FullName = "U1", Username = "U1" };
            context.Warehouses.Add(w);
            context.Users.Add(u);

            var s1 = new Stocktake { Id = 1, Code = "S1", WarehouseId = 1, CreatedBy = 1, CreatedAt = new DateTime(2026, 1, 1) };
            var s2 = new Stocktake { Id = 2, Code = "S2", WarehouseId = 1, CreatedBy = 1, CreatedAt = new DateTime(2026, 1, 2) };
            context.Stocktakes.AddRange(s1, s2);
            await context.SaveChangesAsync();

            var service = new StocktakeQueryService(context);
            var result = await service.GetStocktakesAsync();
            
            Assert.Equal(2, result.Count());
            Assert.Equal(2, result.First().Id);
            Assert.Equal(1, result.Last().Id);
        }

        [Fact]
        public async Task GetStocktakeByIdAsync_ValidId_ReturnsCompleteMappingWithDecimalsAndNullableApproval()
        {
            var context = GetContext();
            var w = new Warehouse { Id = 1, Name = "W1", Code = "W1" };
            var u1 = new User { Id = 1, FullName = "Creator", Username = "Creator" };
            var u2 = new User { Id = 2, FullName = "Approver", Username = "Approver" };
            var unit = new Unit { Id = 1, Name = "Cái", Code = "CAI" };
            var p = new Product { Id = 1, Code = "P1", Name = "Product 1", UnitId = 1 };

            context.Warehouses.Add(w);
            context.Users.AddRange(u1, u2);
            context.Units.Add(unit);
            context.Products.Add(p);

            var s = new Stocktake 
            { 
                Id = 1, 
                Code = "S1", 
                WarehouseId = 1, 
                CreatedBy = 1, 
                ApprovedBy = 2,
                Status = ReceiptStatus.Approved,
                Note = "Test note",
                CreatedAt = new DateTime(2026, 1, 1),
                ApprovedAt = new DateTime(2026, 1, 2)
            };

            s.Details.Add(new StocktakeDetail
            {
                Id = 10,
                StocktakeId = 1,
                ProductId = 1,
                SystemQuantity = 10.5m,
                ActualQuantity = 8.2m,
                DifferenceQuantity = -2.3m,
                Note = "Lost"
            });

            context.Stocktakes.Add(s);
            await context.SaveChangesAsync();

            var service = new StocktakeQueryService(context);
            var result = await service.GetStocktakeByIdAsync(1);
            
            Assert.NotNull(result);
            Assert.Equal("S1", result.Code);
            Assert.Equal("W1", result.WarehouseName);
            Assert.Equal("Creator", result.CreatedByName);
            Assert.Equal("Approver", result.ApprovedByName);
            Assert.Equal(ReceiptStatus.Approved, result.Status);
            
            var detail = result.Details.First();
            Assert.Equal("P1", detail.ProductCode);
            Assert.Equal("Product 1", detail.ProductName);
            Assert.Equal("Cái", detail.UnitName);
            Assert.Equal(10.5m, detail.SystemQuantity);
            Assert.Equal(8.2m, detail.ActualQuantity);
            Assert.Equal(-2.3m, detail.DifferenceQuantity);
            Assert.Equal("Lost", detail.Note);
        }

        [Fact]
        public async Task GetStocktakeByIdAsync_UnknownId_ThrowsNotFoundException()
        {
            var context = GetContext();
            var service = new StocktakeQueryService(context);
            await Assert.ThrowsAsync<NotFoundException>(() => service.GetStocktakeByIdAsync(999));
        }

        [Fact]
        public async Task GetStocktakeByIdAsync_NullableApprovalFields()
        {
            var context = GetContext();
            var w = new Warehouse { Id = 1, Name = "W1", Code = "W1" };
            var u1 = new User { Id = 1, FullName = "Creator", Username = "Creator" };
            context.Warehouses.Add(w);
            context.Users.Add(u1);

            var s = new Stocktake 
            { 
                Id = 1, 
                Code = "S1", 
                WarehouseId = 1, 
                CreatedBy = 1, 
                ApprovedBy = null,
                ApprovedAt = null
            };
            context.Stocktakes.Add(s);
            await context.SaveChangesAsync();

            var service = new StocktakeQueryService(context);
            var result = await service.GetStocktakeByIdAsync(1);
            
            Assert.Null(result.ApprovedBy);
            Assert.Null(result.ApprovedByName);
            Assert.Null(result.ApprovedAt);
        }
    }
}
