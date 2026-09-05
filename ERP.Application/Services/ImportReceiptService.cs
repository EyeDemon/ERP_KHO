using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;

namespace ERP.Application.Services
{
    public class ImportReceiptService : IImportReceiptService
    {
        private readonly IImportReceiptRepository _importReceiptRepository;
        private readonly IInventoryStockRepository _inventoryStockRepository;
        private readonly IInventoryTransactionRepository _inventoryTransactionRepository;
        private readonly IWarehouseRepository _warehouseRepository;
        private readonly IProductRepository _productRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogRepository _auditLogRepository;
        private readonly IWarehouseAuthorizationService? _warehouseAuthorization;
        private readonly ICurrentUser? _currentUser;

        internal ImportReceiptService(IImportReceiptRepository importReceiptRepository, IInventoryStockRepository inventoryStockRepository, IInventoryTransactionRepository inventoryTransactionRepository, IWarehouseRepository warehouseRepository, IProductRepository productRepository, IUnitOfWork unitOfWork, IAuditLogRepository auditLogRepository)
        {
            _importReceiptRepository = importReceiptRepository;
            _inventoryStockRepository = inventoryStockRepository;
            _inventoryTransactionRepository = inventoryTransactionRepository;
            _warehouseRepository = warehouseRepository;
            _productRepository = productRepository;
            _unitOfWork = unitOfWork;
            _auditLogRepository = auditLogRepository;
        }

        public ImportReceiptService(
            IImportReceiptRepository importReceiptRepository,
            IInventoryStockRepository inventoryStockRepository,
            IInventoryTransactionRepository inventoryTransactionRepository,
            IWarehouseRepository warehouseRepository,
            IProductRepository productRepository,
            IUnitOfWork unitOfWork,
            IAuditLogRepository auditLogRepository,
            IWarehouseAuthorizationService warehouseAuthorization,
            ICurrentUser currentUser)
        {
            _importReceiptRepository = importReceiptRepository;
            _inventoryStockRepository = inventoryStockRepository;
            _inventoryTransactionRepository = inventoryTransactionRepository;
            _warehouseRepository = warehouseRepository;
            _productRepository = productRepository;
            _unitOfWork = unitOfWork;
            _auditLogRepository = auditLogRepository;
            _warehouseAuthorization = warehouseAuthorization;
            _currentUser = currentUser;
        }

        public async Task<ImportReceiptDto> CreateAsync(CreateImportReceiptDto dto, int userId)
        {
            if (_currentUser is not null) userId = _currentUser.UserId;
            if (string.IsNullOrWhiteSpace(dto.Code))
                throw new BusinessRuleException("Mã phiếu nhập không được để trống");

            if (dto.WarehouseId <= 0)
                throw new BusinessRuleException("Kho nhập không hợp lệ");

            if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(dto.WarehouseId);

            var warehouse = await _warehouseRepository.GetByIdAsync(dto.WarehouseId);
            if (warehouse == null)
                throw new BusinessRuleException($"Không tìm thấy kho nhập với ID {dto.WarehouseId}");

            if (dto.Details == null || !dto.Details.Any())
                throw new BusinessRuleException("Phiếu nhập phải có ít nhất 1 sản phẩm");

            if (await _importReceiptRepository.ExistsByCodeAsync(dto.Code))
                throw new BusinessRuleException($"Mã phiếu nhập '{dto.Code}' đã tồn tại");

            var receipt = new ImportReceipt
            {
                Code = dto.Code,
                WarehouseId = dto.WarehouseId,
                Status = ReceiptStatus.Draft,
                Note = dto.Note,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow,
                Details = new List<ImportReceiptDetail>()
            };

            foreach (var detailDto in dto.Details)
            {
                if (detailDto.ProductId <= 0)
                    throw new BusinessRuleException("Sản phẩm không hợp lệ");

                var product = await _productRepository.GetByIdAsync(detailDto.ProductId);
                if (product == null)
                    throw new BusinessRuleException($"Không tìm thấy sản phẩm với ID {detailDto.ProductId}");

                if (detailDto.Quantity <= 0)
                    throw new BusinessRuleException($"Số lượng nhập của sản phẩm ID {detailDto.ProductId} phải lớn hơn 0");

                if (detailDto.UnitPrice < 0)
                    throw new BusinessRuleException($"Đơn giá của sản phẩm ID {detailDto.ProductId} không được âm");

                receipt.Details.Add(new ImportReceiptDetail
                {
                    ProductId = detailDto.ProductId,
                    Quantity = detailDto.Quantity,
                    UnitPrice = detailDto.UnitPrice,
                    Note = detailDto.Note
                });
            }

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                await _importReceiptRepository.AddAsync(receipt);
                
                await _auditLogRepository.AddAsync(new AuditLog
                {
                    UserId = userId,
                    Action = "ImportReceipt.Created",
                    EntityName = "ImportReceipt",
                    EntityId = receipt.Id,
                    WarehouseId = receipt.WarehouseId,
                    OldValues = "Status: None",
                    NewValues = $"Status: {receipt.Status}",
                    Result = "Success",
                    Severity = "Information",
                    Timestamp = DateTime.UtcNow
                });

                await _unitOfWork.CommitTransactionAsync();

                var createdReceipt = await _importReceiptRepository.GetByIdWithDetailsAsync(receipt.Id);
                return MapToDto(createdReceipt!);
            }
            catch
            {
                try { await _unitOfWork.RollbackTransactionAsync(); } catch { }
                throw;
            }
        }

        private ImportReceiptDto MapToDto(ImportReceipt receipt)
        {
            return new ImportReceiptDto
            {
                Id = receipt.Id,
                Code = receipt.Code,
                WarehouseId = receipt.WarehouseId,
                WarehouseName = receipt.Warehouse?.Name,
                Status = receipt.Status.ToString(),
                Note = receipt.Note,
                CreatedBy = receipt.CreatedBy,
                CreatedByName = receipt.CreatedByUser?.FullName ?? receipt.CreatedByUser?.Username,
                ApprovedBy = receipt.ApprovedBy,
                ApprovedByName = receipt.ApprovedByUser?.FullName ?? receipt.ApprovedByUser?.Username,
                CreatedAt = receipt.CreatedAt,
                ApprovedAt = receipt.ApprovedAt,
                Details = receipt.Details.Select(d => new ImportReceiptDetailDto
                {
                    Id = d.Id,
                    ProductId = d.ProductId,
                    ProductCode = d.Product?.Code,
                    ProductName = d.Product?.Name,
                    Quantity = d.Quantity,
                    UnitPrice = d.UnitPrice,
                    Note = d.Note
                }).ToList()
            };
        }

        public async Task ApproveImportReceiptAsync(int id, int approvedByUserId)
        {
            if (_currentUser is not null) approvedByUserId = _currentUser.UserId;
            try
            {
                await _unitOfWork.BeginTransactionAsync();
                var receipt = await _importReceiptRepository.GetByIdWithDetailsAsync(id);
                if (receipt == null) throw new NotFoundException($"Không tìm thấy phiếu nhập id {id}");
                if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(receipt.WarehouseId);

                Security.ApprovalSafetyGuard.EnsureDifferentChecker(receipt.CreatedBy, approvedByUserId);

                if (receipt.Status != ReceiptStatus.Draft)
                    throw new BusinessRuleException("Chỉ có thể duyệt phiếu ở trạng thái nháp");

                receipt.Status = ReceiptStatus.Approved;
                receipt.ApprovedBy = approvedByUserId;
                receipt.ApprovedAt = DateTime.UtcNow;

                foreach (var detail in receipt.Details)
                {
                    // Update Stock
                    var stock = await _inventoryStockRepository.GetByProductAndWarehouseAsync(detail.ProductId, receipt.WarehouseId);
                    if (stock == null)
                    {
                        stock = new InventoryStock
                        {
                            ProductId = detail.ProductId,
                            WarehouseId = receipt.WarehouseId,
                            Quantity = detail.Quantity,
                            LastUpdated = DateTime.UtcNow
                        };
                        await _inventoryStockRepository.AddAsync(stock);
                    }
                    else
                    {
                        stock.Quantity += detail.Quantity;
                        stock.LastUpdated = DateTime.UtcNow;
                        await _inventoryStockRepository.UpdateAsync(stock);
                    }

                    // Create Transaction
                    var tx = new InventoryTransaction
                    {
                        ProductId = detail.ProductId,
                        WarehouseId = receipt.WarehouseId,
                        TransactionType = TransactionType.Import,
                        Quantity = detail.Quantity,
                        ReferenceId = receipt.Id,
                        ReferenceType = "ImportReceipt",
                        TransactionDate = DateTime.UtcNow,
                        CreatedBy = approvedByUserId,
                        Note = detail.Note
                    };
                    await _inventoryTransactionRepository.AddAsync(tx);
                }

                await _importReceiptRepository.UpdateAsync(receipt);
                
            await _auditLogRepository.AddAsync(new AuditLog
            {
                UserId = approvedByUserId,
                Action = "ImportReceipt.Approved",
                EntityName = "ImportReceipt",
                EntityId = receipt.Id,
                WarehouseId = receipt.WarehouseId,
                OldValues = $"Status: {ReceiptStatus.Draft}",
                NewValues = $"Status: {ReceiptStatus.Approved}",
                Result = "Success",
                Severity = "Information",
                Timestamp = DateTime.UtcNow
            });
                
                await _unitOfWork.CommitTransactionAsync();
            }
            catch (ConcurrencyException)
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw new BusinessRuleException("Phiếu nhập đang được duyệt bởi một người khác. Vui lòng thử lại.");
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }
        public async Task<IEnumerable<ImportReceiptDto>> GetAllAsync(Domain.Enums.ReceiptStatus? status = null)
        {
            var receipts = await _importReceiptRepository.GetAllWithDetailsAsync(status);
            return receipts.Select(receipt => new ImportReceiptDto
            {
                Id = receipt.Id,
                Code = receipt.Code,
                WarehouseId = receipt.WarehouseId,
                WarehouseName = receipt.Warehouse?.Name,
                Status = receipt.Status.ToString(),
                Note = receipt.Note,
                CreatedBy = receipt.CreatedBy,
                CreatedByName = receipt.CreatedByUser?.FullName ?? receipt.CreatedByUser?.Username,
                ApprovedBy = receipt.ApprovedBy,
                ApprovedByName = receipt.ApprovedByUser?.FullName ?? receipt.ApprovedByUser?.Username,
                CreatedAt = receipt.CreatedAt,
                ApprovedAt = receipt.ApprovedAt,
                Details = receipt.Details.Select(d => new ImportReceiptDetailDto
                {
                    Id = d.Id,
                    ProductId = d.ProductId,
                    ProductCode = d.Product?.Code,
                    ProductName = d.Product?.Name,
                    Quantity = d.Quantity,
                    UnitPrice = d.UnitPrice,
                    Note = d.Note
                }).ToList()
            });
        }

        public async Task<ImportReceiptDto> GetByIdAsync(int id)
        {
            var receipt = await _importReceiptRepository.GetByIdWithDetailsAsync(id);
            if (receipt == null)
                throw new NotFoundException($"Không tìm thấy phiếu nhập id {id}");

            return new ImportReceiptDto
            {
                Id = receipt.Id,
                Code = receipt.Code,
                WarehouseId = receipt.WarehouseId,
                WarehouseName = receipt.Warehouse?.Name,
                Status = receipt.Status.ToString(),
                Note = receipt.Note,
                CreatedBy = receipt.CreatedBy,
                CreatedByName = receipt.CreatedByUser?.FullName ?? receipt.CreatedByUser?.Username,
                ApprovedBy = receipt.ApprovedBy,
                ApprovedByName = receipt.ApprovedByUser?.FullName ?? receipt.ApprovedByUser?.Username,
                CreatedAt = receipt.CreatedAt,
                ApprovedAt = receipt.ApprovedAt,
                Details = receipt.Details.Select(d => new ImportReceiptDetailDto
                {
                    Id = d.Id,
                    ProductId = d.ProductId,
                    ProductCode = d.Product?.Code,
                    ProductName = d.Product?.Name,
                    Quantity = d.Quantity,
                    UnitPrice = d.UnitPrice,
                    Note = d.Note
                }).ToList()
            };
        }

        public async Task CancelAsync(int id, int cancelledByUserId)
        {
            if (_currentUser is not null) cancelledByUserId = _currentUser.UserId;
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var receipt = await _importReceiptRepository.GetByIdWithDetailsAsync(id);
                if (receipt == null)
                    throw new NotFoundException($"Không tìm thấy phiếu nhập id {id}");
                if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(receipt.WarehouseId);

                if (receipt.Status != Domain.Enums.ReceiptStatus.Draft)
                    throw new BusinessRuleException("Chỉ có thể hủy phiếu nhập ở trạng thái nháp");

                receipt.Status = Domain.Enums.ReceiptStatus.Cancelled;
                await _importReceiptRepository.UpdateAsync(receipt);

                await _auditLogRepository.AddAsync(new AuditLog
                {
                    UserId = cancelledByUserId,
                    Action = "ImportReceipt.Cancelled",
                    EntityName = "ImportReceipt",
                    EntityId = receipt.Id,
                    WarehouseId = receipt.WarehouseId,
                    OldValues = $"Status: {Domain.Enums.ReceiptStatus.Draft}",
                    NewValues = $"Status: {Domain.Enums.ReceiptStatus.Cancelled}",
                    Result = "Success",
                    Reason = "Import receipt cancelled",
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


