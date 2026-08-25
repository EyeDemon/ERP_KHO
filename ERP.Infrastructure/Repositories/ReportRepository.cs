using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Repositories
{
    public class ReportRepository : IReportRepository
    {
        private readonly ErpKhoDbContext _context;

        public ReportRepository(ErpKhoDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<InventoryStock>> GetCurrentStocksAsync(int? warehouseId, int? productId, CancellationToken cancellationToken = default)
        {
            var query = _context.InventoryStocks
                .AsNoTracking()
                .Include(s => s.Product).ThenInclude(p => p.Unit)
                .Include(s => s.Warehouse)
                .AsQueryable();

            if (warehouseId.HasValue)
                query = query.Where(s => s.WarehouseId == warehouseId.Value);
            if (productId.HasValue)
                query = query.Where(s => s.ProductId == productId.Value);

            return await query.ToListAsync(cancellationToken);
        }

        public async Task<IEnumerable<InventoryTransaction>> GetTransactionsUpToDateAsync(DateTime asOfDate, int? warehouseId, int? productId, CancellationToken cancellationToken = default)
        {
            var query = _context.InventoryTransactions
                .AsNoTracking()
                .Include(t => t.Product).ThenInclude(p => p.Unit)
                .Include(t => t.Warehouse)
                .Where(t => t.TransactionDate <= asOfDate);

            if (warehouseId.HasValue)
                query = query.Where(t => t.WarehouseId == warehouseId.Value);
            if (productId.HasValue)
                query = query.Where(t => t.ProductId == productId.Value);

            return await query.ToListAsync(cancellationToken);
        }

        public async Task<IEnumerable<ERP.Domain.Models.InventoryInOutReportModel>> GetInventoryInOutReportAsync(DateTime? fromDate, DateTime? toDate, int? warehouseId, int? productId, CancellationToken cancellationToken = default)
        {
            var connection = _context.Database.GetDbConnection();
            var parameters = new Dapper.DynamicParameters();
            parameters.Add("@FromDate", fromDate);
            parameters.Add("@ToDate", toDate);
            parameters.Add("@WarehouseId", warehouseId);
            parameters.Add("@ProductId", productId);

            var command = new Dapper.CommandDefinition(
                "sp_GetInventoryInOutReport",
                parameters,
                commandType: System.Data.CommandType.StoredProcedure,
                cancellationToken: cancellationToken);

            return await Dapper.SqlMapper.QueryAsync<ERP.Domain.Models.InventoryInOutReportModel>(
                connection,
                command);
        }
    }
}
