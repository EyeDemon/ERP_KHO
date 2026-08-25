using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Application.Services
{
    public class ReportService : IReportService
    {
        private readonly IReportRepository _reportRepository;
        private readonly IWarehouseAuthorizationService? _warehouseAuthorization;

        internal ReportService(IReportRepository reportRepository)
        {
            _reportRepository = reportRepository;
        }

        public ReportService(IReportRepository reportRepository, IWarehouseAuthorizationService warehouseAuthorization)
        {
            _reportRepository = reportRepository;
            _warehouseAuthorization = warehouseAuthorization;
        }

        public async Task<IEnumerable<InventoryReportDto>> GetInventoryReportAsync(DateTime? asOfDate, int? warehouseId, int? productId, CancellationToken cancellationToken = default)
        {
            if (_warehouseAuthorization is null)
                return await GetInventoryReportLegacyAsync(asOfDate, warehouseId, productId, cancellationToken);
            var warehouseIds = await ResolveWarehouseIdsAsync(warehouseId, cancellationToken);
            if (asOfDate.HasValue)
            {
                var reportDate = asOfDate.Value;
                var transactions = new List<ERP.Domain.Entities.InventoryTransaction>();
                foreach (var allowedWarehouseId in warehouseIds)
                    transactions.AddRange(await _reportRepository.GetTransactionsUpToDateAsync(reportDate, allowedWarehouseId, productId, cancellationToken));

                var grouped = transactions.GroupBy(t => new { t.ProductId, t.WarehouseId })
                    .Select(g => new InventoryReportDto
                    {
                        ProductId = g.Key.ProductId,
                        ProductCode = g.First().Product.Code,
                        ProductName = g.First().Product.Name,
                        UnitName = g.First().Product.Unit?.Name ?? "",
                        WarehouseId = g.Key.WarehouseId,
                        WarehouseName = g.First().Warehouse.Name,
                        Quantity = g.Sum(t => t.TransactionType.ApplySign(t.Quantity)),
                        ReportDate = reportDate,
                        LastUpdated = g.Max(t => (DateTime?)t.TransactionDate)
                    })
                    .Where(x => x.Quantity != 0)
                    .OrderBy(x => x.ProductCode)
                    .ThenBy(x => x.WarehouseName)
                    .ToList();

                return grouped;
            }
            else
            {
                var reportDate = DateTime.UtcNow;
                var stocks = new List<ERP.Domain.Entities.InventoryStock>();
                foreach (var allowedWarehouseId in warehouseIds)
                    stocks.AddRange(await _reportRepository.GetCurrentStocksAsync(allowedWarehouseId, productId, cancellationToken));

                return stocks.Select(s => new InventoryReportDto
                {
                    ProductId = s.ProductId,
                    ProductCode = s.Product.Code,
                    ProductName = s.Product.Name,
                    UnitName = s.Product.Unit?.Name ?? "",
                    WarehouseId = s.WarehouseId,
                    WarehouseName = s.Warehouse.Name,
                    Quantity = s.Quantity,
                    ReportDate = reportDate,
                    LastUpdated = s.LastUpdated
                })
                .OrderBy(x => x.ProductCode)
                .ThenBy(x => x.WarehouseName)
                .ToList();
            }
        }

        public async Task<IEnumerable<InventoryInOutReportDto>> GetInventoryInOutReportAsync(DateTime? fromDate, DateTime? toDate, int? warehouseId, int? productId, CancellationToken cancellationToken = default)
        {
            if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
            {
                throw new ERP.Application.Exceptions.BusinessRuleException("Từ ngày không được lớn hơn đến ngày.");
            }

            if (_warehouseAuthorization is null)
            {
                var legacyModels = await _reportRepository.GetInventoryInOutReportAsync(fromDate, toDate, warehouseId, productId, cancellationToken);
                return MapInOut(legacyModels);
            }
            var warehouseIds = await ResolveWarehouseIdsAsync(warehouseId, cancellationToken);
            var reportModels = new List<ERP.Domain.Models.InventoryInOutReportModel>();
            foreach (var allowedWarehouseId in warehouseIds)
                reportModels.AddRange(await _reportRepository.GetInventoryInOutReportAsync(fromDate, toDate, allowedWarehouseId, productId, cancellationToken));
            return MapInOut(reportModels);
        }

        private static IEnumerable<InventoryInOutReportDto> MapInOut(IEnumerable<ERP.Domain.Models.InventoryInOutReportModel> reportModels) => reportModels.Select(m => new InventoryInOutReportDto
            {
                ProductId = m.ProductId,
                ProductCode = m.ProductCode,
                ProductName = m.ProductName,
                UnitName = m.UnitName,
                WarehouseId = m.WarehouseId,
                WarehouseName = m.WarehouseName,
                OpeningQuantity = m.OpeningQuantity,
                ImportQuantity = m.ImportQuantity,
                TransferInQuantity = m.TransferInQuantity,
                AdjustmentIncreaseQuantity = m.AdjustmentIncreaseQuantity,
                InQuantity = m.InQuantity,
                ExportQuantity = m.ExportQuantity,
                TransferOutQuantity = m.TransferOutQuantity,
                AdjustmentDecreaseQuantity = m.AdjustmentDecreaseQuantity,
                OutQuantity = m.OutQuantity,
                ClosingQuantity = m.ClosingQuantity
            });

        private async Task<IReadOnlyList<int>> ResolveWarehouseIdsAsync(int? warehouseId, CancellationToken cancellationToken)
        {
            if (warehouseId.HasValue)
            {
                await _warehouseAuthorization!.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
                return new[] { warehouseId.Value };
            }

            return await _warehouseAuthorization!.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        private async Task<IEnumerable<InventoryReportDto>> GetInventoryReportLegacyAsync(DateTime? asOfDate, int? warehouseId, int? productId, CancellationToken cancellationToken)
        {
            if (asOfDate.HasValue)
            {
                var transactions = await _reportRepository.GetTransactionsUpToDateAsync(asOfDate.Value, warehouseId, productId, cancellationToken);
                return transactions.GroupBy(t => new { t.ProductId, t.WarehouseId }).Select(g => new InventoryReportDto
                {
                    ProductId = g.Key.ProductId, ProductCode = g.First().Product.Code, ProductName = g.First().Product.Name,
                    UnitName = g.First().Product.Unit?.Name ?? "", WarehouseId = g.Key.WarehouseId,
                    WarehouseName = g.First().Warehouse.Name,
                    Quantity = g.Sum(t => t.TransactionType.ApplySign(t.Quantity)),
                    ReportDate = asOfDate.Value, LastUpdated = g.Max(t => (DateTime?)t.TransactionDate)
                }).Where(x => x.Quantity != 0).OrderBy(x => x.ProductCode).ThenBy(x => x.WarehouseName).ToList();
            }

            var stocks = await _reportRepository.GetCurrentStocksAsync(warehouseId, productId, cancellationToken);
            return stocks.Select(s => new InventoryReportDto
            {
                ProductId = s.ProductId, ProductCode = s.Product.Code, ProductName = s.Product.Name,
                UnitName = s.Product.Unit?.Name ?? "", WarehouseId = s.WarehouseId, WarehouseName = s.Warehouse.Name,
                Quantity = s.Quantity, ReportDate = DateTime.UtcNow, LastUpdated = s.LastUpdated
            }).OrderBy(x => x.ProductCode).ThenBy(x => x.WarehouseName).ToList();
        }
    }
}
