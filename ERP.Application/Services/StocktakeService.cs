using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Application.Services
{
    public class StocktakeService : IStocktakeService
    {
        private readonly IStocktakeRepository _stocktakeRepository;
        private readonly IInventoryStockRepository _inventoryStockRepository;
        private readonly IInventoryTransactionRepository _inventoryTransactionRepository;
        private readonly IWarehouseRepository _warehouseRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogRepository _auditLogRepository;
        private readonly IWarehouseAuthorizationService? _warehouseAuthorization;
        private readonly ICurrentUser? _currentUser;

        internal StocktakeService(IStocktakeRepository stocktakeRepository, IInventoryStockRepository inventoryStockRepository, IInventoryTransactionRepository inventoryTransactionRepository, IWarehouseRepository warehouseRepository, IUnitOfWork unitOfWork, IAuditLogRepository auditLogRepository)
        {
            _stocktakeRepository = stocktakeRepository;
            _inventoryStockRepository = inventoryStockRepository;
            _inventoryTransactionRepository = inventoryTransactionRepository;
            _warehouseRepository = warehouseRepository;
            _unitOfWork = unitOfWork;
            _auditLogRepository = auditLogRepository;
        }

        public StocktakeService(
            IStocktakeRepository stocktakeRepository,
            IInventoryStockRepository inventoryStockRepository,
            IInventoryTransactionRepository inventoryTransactionRepository,
            IWarehouseRepository warehouseRepository,
            IUnitOfWork unitOfWork,
            IAuditLogRepository auditLogRepository,
            IWarehouseAuthorizationService warehouseAuthorization,
            ICurrentUser currentUser)
        {
            _stocktakeRepository = stocktakeRepository;
            _inventoryStockRepository = inventoryStockRepository;
            _inventoryTransactionRepository = inventoryTransactionRepository;
            _warehouseRepository = warehouseRepository;
            _unitOfWork = unitOfWork;
            _auditLogRepository = auditLogRepository;
            _warehouseAuthorization = warehouseAuthorization;
            _currentUser = currentUser;
        }

                public async Task<int> CreateStocktakeAsync(DTOs.CreateStocktakeDto dto, int createdByUserId)
        {
            if (_currentUser is not null) createdByUserId = _currentUser.UserId;
            if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(dto.WarehouseId);
            var warehouse = await _warehouseRepository.GetByIdAsync(dto.WarehouseId);
            if (warehouse == null)
                throw new NotFoundException($"Không tìm thấy kho id {dto.WarehouseId}");

            var stocks = await _inventoryStockRepository.FindAsync(s => s.WarehouseId == dto.WarehouseId);

            var code = $"KK-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..16].ToUpperInvariant()}";

            var stocktake = new Stocktake
            {
                Code = code,
                WarehouseId = dto.WarehouseId,
                Status = ReceiptStatus.Draft,
                CreatedBy = createdByUserId,
                CreatedAt = DateTime.UtcNow,
                Note = dto.Note,
                Details = stocks.Select(s => new StocktakeDetail
                {
                    ProductId = s.ProductId,
                    SystemQuantity = s.Quantity,
                    DifferenceQuantity = 0
                }).ToList()
            };

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                await _stocktakeRepository.AddAsync(stocktake);
                await _unitOfWork.SaveChangesAsync();

                await _auditLogRepository.AddAsync(new AuditLog
                {
                    UserId = createdByUserId,
                    Action = "Stocktake.Created",
                    EntityName = "Stocktake",
                    EntityId = stocktake.Id,
                    WarehouseId = stocktake.WarehouseId,
                    OldValues = $"Status: {ReceiptStatus.Draft}",
                    Result = "Success",
                    Severity = "Information",
                    Timestamp = DateTime.UtcNow,
                    NewValues = $"WarehouseId: {dto.WarehouseId}, Note: {dto.Note}"
                });

                await _unitOfWork.CommitTransactionAsync();
                return stocktake.Id;
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        public async Task ApproveStocktakeAsync(int id, int approvedByUserId)
        {
            if (_currentUser is not null) approvedByUserId = _currentUser.UserId;
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var stocktake = await _stocktakeRepository.GetByIdWithDetailsAsync(id);
                if (stocktake == null) throw new NotFoundException($"Không tìm thấy phiếu kiểm kê id {id}");
                if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(stocktake.WarehouseId);

                Security.ApprovalSafetyGuard.EnsureDifferentChecker(stocktake.CreatedBy, approvedByUserId);

                if (stocktake.Status != ReceiptStatus.Draft)
                    throw new BusinessRuleException("Chỉ có thể duyệt phiếu ở trạng thái nháp");

                stocktake.Status = ReceiptStatus.Approved;
                stocktake.ApprovedBy = approvedByUserId;
                stocktake.ApprovedAt = DateTime.UtcNow;

                foreach (var detail in stocktake.Details)
                {
                    if (detail.ActualQuantity == null)
                        throw new BusinessRuleException($"Chưa nhập số lượng thực tế cho sản phẩm id {detail.ProductId}");

                    var reservedStock = await _inventoryStockRepository.GetByProductAndWarehouseAsync(detail.ProductId, stocktake.WarehouseId);
                    if (detail.ActualQuantity.Value < (reservedStock?.ReservedQuantity ?? 0))
                        throw new ERP.Domain.Exceptions.ConcurrencyException($"Không thể duyệt kiểm kê: sản phẩm ID {detail.ProductId} có tồn thực tế {detail.ActualQuantity.Value} thấp hơn số lượng đang giữ {reservedStock?.ReservedQuantity ?? 0}. Hãy xử lý reservation trước.");

                    var diff = detail.ActualQuantity.Value - detail.SystemQuantity;
                    if (diff != 0)
                    {
                        // Update Stock
                        var stock = await _inventoryStockRepository.GetByProductAndWarehouseAsync(detail.ProductId, stocktake.WarehouseId);
                        if (stock == null)
                        {
                            stock = new InventoryStock
                            {
                                ProductId = detail.ProductId,
                                WarehouseId = stocktake.WarehouseId,
                                Quantity = detail.ActualQuantity.Value,
                                LastUpdated = DateTime.UtcNow
                            };
                            await _inventoryStockRepository.AddAsync(stock);
                        }
                        else
                        {
                            stock.Quantity = detail.ActualQuantity.Value;
                            stock.LastUpdated = DateTime.UtcNow;
                            await _inventoryStockRepository.UpdateAsync(stock);
                        }

                        // Create Transaction
                        var tx = new InventoryTransaction
                        {
                            ProductId = detail.ProductId,
                            WarehouseId = stocktake.WarehouseId,
                            TransactionType = diff > 0 ? TransactionType.AdjustmentIncrease : TransactionType.AdjustmentDecrease,
                            Quantity = Math.Abs(diff),
                            ReferenceId = stocktake.Id,
                            ReferenceType = "Stocktake",
                            TransactionDate = DateTime.UtcNow,
                            CreatedBy = approvedByUserId,
                            Note = detail.Note
                        };
                        await _inventoryTransactionRepository.AddAsync(tx);
                    }
                    
                    detail.DifferenceQuantity = diff;
                }

                await _stocktakeRepository.UpdateAsync(stocktake);

                decimal totalAdjustments = stocktake.Details.Sum(d => Math.Abs(d.DifferenceQuantity));
                await _auditLogRepository.AddAsync(new AuditLog
                {
                    UserId = approvedByUserId,
                    Action = "Stocktake.Approved",
                    EntityName = "Stocktake",
                    EntityId = stocktake.Id,
                    WarehouseId = stocktake.WarehouseId,
                    OldValues = $"Status: {ReceiptStatus.Draft}",
                    Result = "Success",
                    Severity = "Information",
                    Timestamp = DateTime.UtcNow,
                    NewValues = $"Status: Approved, TotalAdjustments: {totalAdjustments}"
                });

                await _unitOfWork.CommitTransactionAsync();
            }
            catch (ERP.Domain.Exceptions.ConcurrencyException)
            {
                await _unitOfWork.RollbackTransactionAsync();
                var conflict = new BusinessRuleException("Dữ liệu đã bị thay đổi bởi người khác, vui lòng thử lại.");
                conflict.Data["HttpStatusCode"] = 409;
                throw conflict;
            }
            catch (Exception)
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }
        public async Task UpdateStocktakeDetailAsync(int stocktakeId, int detailId, DTOs.UpdateStocktakeDetailDto dto)
        {
            if (dto.ActualQuantity < 0)
                throw new BusinessRuleException("Số lượng thực tế không được âm.");

            var stocktake = await _stocktakeRepository.GetByIdWithDetailsAsync(stocktakeId);
            if (stocktake == null)
                throw new NotFoundException($"Không tìm thấy phiếu kiểm kê id {stocktakeId}");
            if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(stocktake.WarehouseId);

            if (stocktake.Status != ReceiptStatus.Draft)
                throw new BusinessRuleException("Chỉ có thể cập nhật chi tiết phiếu ở trạng thái nháp");

            var detail = stocktake.Details.FirstOrDefault(d => d.Id == detailId);
            if (detail == null)
                throw new NotFoundException($"Không tìm thấy chi tiết phiếu id {detailId}");

            var oldActualQuantity = detail.ActualQuantity;
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                detail.ActualQuantity = dto.ActualQuantity;
                detail.DifferenceQuantity = dto.ActualQuantity - detail.SystemQuantity;
                if (dto.Note != null) detail.Note = dto.Note;

                await _stocktakeRepository.UpdateAsync(stocktake);
                await _auditLogRepository.AddAsync(new AuditLog
                {
                    UserId = _currentUser?.UserId,
                    Action = "Stocktake.DetailUpdated",
                    EntityName = "Stocktake",
                    EntityId = stocktake.Id,
                    WarehouseId = stocktake.WarehouseId,
                    OldValues = $"DetailId: {detail.Id}; ActualQuantity: {oldActualQuantity}",
                    NewValues = $"DetailId: {detail.Id}; ActualQuantity: {detail.ActualQuantity}",
                    Result = "Success",
                    Severity = "Information",
                    Timestamp = DateTime.UtcNow
                });
                await _unitOfWork.CommitTransactionAsync();
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }
    }
}
