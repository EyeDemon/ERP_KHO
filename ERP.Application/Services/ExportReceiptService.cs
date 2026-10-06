using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;
using ERP.Application.Options;

namespace ERP.Application.Services
{
    public class ExportReceiptService : IExportReceiptService
    {
        private readonly IExportReceiptRepository _exportReceiptRepository;
        private readonly IInventoryStockRepository _inventoryStockRepository;
        private readonly IInventoryTransactionRepository _inventoryTransactionRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogRepository _auditLogRepository;
        private readonly IWarehouseAuthorizationService? _warehouseAuthorization;
        private readonly ICurrentUser? _currentUser;
        private readonly IStockReservationService? _stockReservationService;
        private readonly IPickingDispatchReadiness? _pickingDispatchReadiness;
        private readonly IPackingDispatchReadiness? _packingDispatchReadiness;
        private readonly IShipmentDispatchReadiness? _shipmentDispatchReadiness;
        private readonly ExportReceiptOptions _options;

        internal ExportReceiptService(IExportReceiptRepository exportReceiptRepository, IInventoryStockRepository inventoryStockRepository, IInventoryTransactionRepository inventoryTransactionRepository, IUnitOfWork unitOfWork, IAuditLogRepository auditLogRepository, ExportReceiptOptions? options = null)
        {
            _exportReceiptRepository = exportReceiptRepository;
            _inventoryStockRepository = inventoryStockRepository;
            _inventoryTransactionRepository = inventoryTransactionRepository;
            _unitOfWork = unitOfWork;
            _auditLogRepository = auditLogRepository;
            _stockReservationService = null;
            _pickingDispatchReadiness = null;
            _packingDispatchReadiness = null;
            _shipmentDispatchReadiness = null;
            _options = options ?? new ExportReceiptOptions();
        }

        public ExportReceiptService(
            IExportReceiptRepository exportReceiptRepository,
            IInventoryStockRepository inventoryStockRepository,
            IInventoryTransactionRepository inventoryTransactionRepository,
            IUnitOfWork unitOfWork,
            IAuditLogRepository auditLogRepository,
            IWarehouseAuthorizationService warehouseAuthorization,
            ICurrentUser currentUser,
            IStockReservationService stockReservationService,
            ExportReceiptOptions? options = null,
            IPickingDispatchReadiness? pickingDispatchReadiness = null,
            IPackingDispatchReadiness? packingDispatchReadiness = null,
            IShipmentDispatchReadiness? shipmentDispatchReadiness = null)
        {
            _exportReceiptRepository = exportReceiptRepository;
            _inventoryStockRepository = inventoryStockRepository;
            _inventoryTransactionRepository = inventoryTransactionRepository;
            _unitOfWork = unitOfWork;
            _auditLogRepository = auditLogRepository;
            _warehouseAuthorization = warehouseAuthorization;
            _currentUser = currentUser;
            _stockReservationService = stockReservationService;
            _pickingDispatchReadiness = pickingDispatchReadiness;
            _packingDispatchReadiness = packingDispatchReadiness;
            _shipmentDispatchReadiness = shipmentDispatchReadiness;
            _options = options ?? new ExportReceiptOptions();
        }

        public async Task<ExportReceiptDto> CreateAsync(CreateExportReceiptDto dto, int userId)
        {
            if (_currentUser is not null) userId = _currentUser.UserId;
            if (string.IsNullOrWhiteSpace(dto.Code))
                throw new BusinessRuleException("Mã phiếu xuất không được để trống");

            if (dto.WarehouseId <= 0)
                throw new BusinessRuleException("Kho xuất không hợp lệ");

            if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(dto.WarehouseId);

            if (dto.Details == null || !dto.Details.Any())
                throw new BusinessRuleException("Phiếu xuất phải có ít nhất 1 sản phẩm");

            if (await _exportReceiptRepository.ExistsByCodeAsync(dto.Code))
            {
                throw new BusinessRuleException($"Mã phiếu xuất '{dto.Code}' đã tồn tại");
            }

            var receipt = new ERP.Domain.Entities.ExportReceipt
            {
                Code = dto.Code,
                WarehouseId = dto.WarehouseId,
                Status = ReceiptStatus.Draft,
                Note = dto.Note,
                CustomerId = dto.CustomerId,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow,
                Details = new List<ERP.Domain.Entities.ExportReceiptDetail>()
            };

            foreach (var detailDto in dto.Details)
            {
                if (detailDto.ProductId <= 0)
                    throw new BusinessRuleException("Sản phẩm không hợp lệ");

                if (detailDto.Quantity <= 0)
                    throw new BusinessRuleException($"Số lượng xuất của sản phẩm ID {detailDto.ProductId} phải lớn hơn 0");

                if (detailDto.UnitPrice < 0)
                    throw new BusinessRuleException($"Đơn giá của sản phẩm ID {detailDto.ProductId} không được âm");

                var stock = await _inventoryStockRepository.GetByProductAndWarehouseAsync(detailDto.ProductId, dto.WarehouseId);
                if (stock == null)
                    throw new BusinessRuleException($"Không tìm thấy thông tin tồn kho khả dụng cho sản phẩm ID {detailDto.ProductId} tại kho ID {dto.WarehouseId}");
                if (stock.Product == null || !stock.Product.IsActive || stock.Product.Unit == null || !stock.Product.Unit.IsActive)
                    throw new BusinessRuleException($"Sản phẩm ID {detailDto.ProductId} hoặc Base UOM không hoạt động.");
                if (decimal.Round(detailDto.Quantity, stock.Product.Unit.DecimalPlaces) != detailDto.Quantity)
                    throw new BusinessRuleException($"Số lượng sản phẩm ID {detailDto.ProductId} vượt quá độ chính xác Base UOM cho phép ({stock.Product.Unit.DecimalPlaces} chữ số thập phân).");

                var currentStock = await _inventoryStockRepository.GetAvailableQuantityAsync(detailDto.ProductId, dto.WarehouseId);

                if (detailDto.Quantity > currentStock)
                {
                    throw new BusinessRuleException($"Sản phẩm có ID {detailDto.ProductId} không đủ tồn kho. Tồn khả dụng: {currentStock}, yêu cầu xuất: {detailDto.Quantity}");
                }

                receipt.Details.Add(new ERP.Domain.Entities.ExportReceiptDetail
                {
                    ProductId = detailDto.ProductId,
                    Quantity = detailDto.Quantity,
                    UnitPrice = detailDto.UnitPrice,
                    Note = detailDto.Note,
                    BaseUomIdSnapshot = stock.Product.Unit.Id,
                    BaseUomCodeSnapshot = stock.Product.Unit.Code,
                    BaseUomNameSnapshot = stock.Product.Unit.Name,
                    BaseUomDecimalPlacesSnapshot = stock.Product.Unit.DecimalPlaces
                });
            }

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                await _exportReceiptRepository.AddAsync(receipt);
                
                await _auditLogRepository.AddAsync(new ERP.Domain.Entities.AuditLog
                {
                    UserId = userId,
                    Action = "ExportReceipt.Created",
                    EntityName = "ExportReceipt",
                    EntityId = receipt.Id,
                    WarehouseId = receipt.WarehouseId,
                    Result = "Success",
                    Severity = "Information",
                    Timestamp = DateTime.UtcNow
                });

                await _unitOfWork.CommitTransactionAsync();

                var createdReceipt = await _exportReceiptRepository.GetByIdWithDetailsAsync(receipt.Id);
                return MapToDto(createdReceipt!);
            }
            catch
            {
                try { await _unitOfWork.RollbackTransactionAsync(); } catch { }
                throw;
            }
        }

        public Task ApproveAsync(int id, int approvedByUserId) =>
            ApproveAndReserveAsync(id, approvedByUserId);

        public Task ApproveAndReserveAsync(int id, int approvedByUserId) =>
            ApproveCoreAsync(id, approvedByUserId, ExportDispatchMode.RequireSeparateDispatch);

        public Task ApproveAndDispatchAsync(int id, int approvedByUserId) =>
            ApproveCoreAsync(id, approvedByUserId, ExportDispatchMode.DispatchOnApproval);

        private async Task ApproveCoreAsync(int id, int approvedByUserId, ExportDispatchMode mode)
        {
            EnsureWriteEnabled();
            if (_currentUser is not null) approvedByUserId = _currentUser.UserId;
            var maxDeadlockAttempts = _unitOfWork.HasExternalTransaction ? 1 : 2;
            for (var attempt = 1; attempt <= maxDeadlockAttempts; attempt++)
            {
                await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var receipt = await _exportReceiptRepository.GetByIdWithDetailsAsync(id);
                    if (receipt == null) throw new NotFoundException($"Không tìm thấy phiếu xuất id {id}");
                    if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(receipt.WarehouseId);
                    Security.ApprovalSafetyGuard.EnsureDifferentChecker(receipt.CreatedBy, approvedByUserId);
                    if (receipt.Status != ReceiptStatus.Draft)
                        throw new ConcurrencyException("Phiếu xuất đã được xử lý hoặc đang được xử lý bởi yêu cầu khác.");
                    if (receipt.Details == null || !receipt.Details.Any())
                        throw new BusinessRuleException("Phiếu xuất phải có ít nhất 1 sản phẩm để duyệt");
                    if (_stockReservationService is null)
                        throw new InvalidOperationException("Stock reservation service is required for export approval.");

                    receipt.CustomerCodeSnapshot = receipt.Customer?.Code;
                    receipt.CustomerNameSnapshot = receipt.Customer?.Name;
                    receipt.DispatchMode = mode;
                    receipt.ApprovedBy = approvedByUserId;
                    receipt.ApprovedAt = DateTime.UtcNow;

                    foreach (var detail in receipt.Details.OrderBy(detail => detail.ProductId))
                    {
                        var reservation = await _stockReservationService.ReserveForExportAsync(
                            receipt.Id, receipt.Code, receipt.WarehouseId, detail.ProductId, detail.Quantity, approvedByUserId);
                        if (mode == ExportDispatchMode.DispatchOnApproval)
                        {
                            var consumptions = await _stockReservationService.ConsumeAsync(reservation, approvedByUserId);
                            await AddExportTransactionsAsync(receipt, detail, consumptions, approvedByUserId);
                        }
                    }

                    if (mode == ExportDispatchMode.DispatchOnApproval)
                    {
                        receipt.Status = ReceiptStatus.Dispatched;
                        receipt.DispatchedBy = approvedByUserId;
                        receipt.DispatchedAt = DateTime.UtcNow;
                    }
                    else receipt.Status = ReceiptStatus.Approved;

                    await _exportReceiptRepository.UpdateAsync(receipt);
                    await _auditLogRepository.AddAsync(new ERP.Domain.Entities.AuditLog
                    {
                        UserId = approvedByUserId,
                        Action = mode == ExportDispatchMode.DispatchOnApproval ? "ExportReceipt.ApprovedAndDispatched" : "ExportReceipt.ApprovedAndReserved",
                        EntityName = "ExportReceipt",
                        EntityId = receipt.Id,
                        WarehouseId = receipt.WarehouseId,
                        Result = "Success",
                        Severity = "Information",
                        Timestamp = DateTime.UtcNow,
                        OldValues = $"Status: {ReceiptStatus.Draft}",
                        NewValues = $"WarehouseId: {receipt.WarehouseId}; DispatchMode: {mode}; Status: {receipt.Status}"
                    });
                    await _unitOfWork.CommitTransactionAsync();
                    return;
                }
                catch (DeadlockException) when (attempt < maxDeadlockAttempts)
                {
                    await _unitOfWork.RollbackTransactionAsync();
                }
                catch
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    throw;
                }
            }
        }

        public async Task DispatchAsync(int id, int userId)
        {
            EnsureWriteEnabled();
            if (_currentUser is not null) userId = _currentUser.UserId;
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var receipt = await _exportReceiptRepository.GetByIdWithDetailsAsync(id);
                if (receipt is null) throw new NotFoundException($"Không tìm thấy phiếu xuất id {id}");
                if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(receipt.WarehouseId);
                if (receipt.Status != ReceiptStatus.Approved || receipt.DispatchMode != ExportDispatchMode.RequireSeparateDispatch)
                    throw new ConcurrencyException("Phiếu xuất không ở trạng thái có thể xác nhận xuất.");
                if (_options.RequireDifferentDispatcher && receipt.ApprovedBy == userId)
                    throw new BusinessRuleException("Người duyệt phải khác người xác nhận xuất.");
                if (_stockReservationService is null) throw new InvalidOperationException("Stock reservation service is required for export dispatch.");
                if (_pickingDispatchReadiness is not null)
                    await _pickingDispatchReadiness.EnsureSourceReadyAsync("ExportReceipt", receipt.Id);
                if (_packingDispatchReadiness is not null)
                    await _packingDispatchReadiness.EnsureSourceReadyAsync("ExportReceipt", receipt.Id);
                if (_shipmentDispatchReadiness is not null)
                    await _shipmentDispatchReadiness.EnsureSourceReadyAsync("ExportReceipt", receipt.Id);

                foreach (var detail in receipt.Details.OrderBy(x => x.ProductId))
                {
                    var reservation = await _stockReservationService.GetExportReservationAsync(receipt.Id, receipt.WarehouseId, detail.ProductId);
                    var consumptions = await _stockReservationService.ConsumeAsync(reservation, userId);
                    await AddExportTransactionsAsync(receipt, detail, consumptions, userId);
                }

                receipt.Status = ReceiptStatus.Dispatched;
                receipt.DispatchedBy = userId;
                receipt.DispatchedAt = DateTime.UtcNow;
                await _exportReceiptRepository.UpdateAsync(receipt);
                await _auditLogRepository.AddAsync(new ERP.Domain.Entities.AuditLog
                {
                    UserId = userId,
                    Action = "ExportReceipt.Dispatched",
                    EntityName = "ExportReceipt",
                    EntityId = receipt.Id,
                    WarehouseId = receipt.WarehouseId,
                    Result = "Success",
                    Severity = "Information",
                    Timestamp = DateTime.UtcNow,
                    OldValues = $"Status: {ReceiptStatus.Approved}",
                    NewValues = $"WarehouseId: {receipt.WarehouseId}; DispatchMode: {receipt.DispatchMode}; Status: {ReceiptStatus.Dispatched}"
                });
                await _unitOfWork.CommitTransactionAsync();
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        private async Task AddExportTransactionsAsync(
            ERP.Domain.Entities.ExportReceipt receipt,
            ERP.Domain.Entities.ExportReceiptDetail detail,
            IReadOnlyList<InventoryStockConsumption> consumptions,
            int userId)
        {
            if (consumptions.Count == 0 || consumptions.Any(x => x.LocationId <= 0 || x.Quantity <= 0) ||
                consumptions.Sum(x => x.Quantity) != detail.Quantity)
                throw new ConcurrencyException("Không thể đối soát vị trí tồn kho đã xuất.");

            foreach (var consumption in consumptions)
            {
                await _inventoryTransactionRepository.AddAsync(new ERP.Domain.Entities.InventoryTransaction
                {
                    ProductId = detail.ProductId,
                    WarehouseId = receipt.WarehouseId,
                    LocationId = consumption.LocationId,
                    InventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Export,
                    Quantity = consumption.Quantity,
                    ReferenceId = receipt.Id,
                    ReferenceType = "ExportReceipt",
                    TransactionDate = DateTime.UtcNow,
                    CreatedBy = userId,
                    Note = detail.Note
                });
            }
        }

        public async Task<IEnumerable<ExportReceiptDto>> GetAllAsync()
        {
            var receipts = await _exportReceiptRepository.GetListAsync();
            return receipts.Select(MapToDto);
        }

        public async Task<ExportReceiptDto> GetByIdAsync(int id)
        {
            var receipt = await _exportReceiptRepository.GetByIdWithDetailsAsync(id);
            if (receipt == null) throw new NotFoundException($"Không tìm thấy phiếu xuất id {id}");

            return MapToDto(receipt);
        }

        public async Task CancelAsync(int id, int userId)
        {
            if (_currentUser is not null) userId = _currentUser.UserId;
            var receipt = _stockReservationService is null
                ? await _exportReceiptRepository.GetByIdAsync(id)
                : await _exportReceiptRepository.GetByIdWithDetailsAsync(id);
            if (receipt == null) throw new NotFoundException($"Không tìm thấy phiếu xuất id {id}");
            if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(receipt.WarehouseId);

            if (receipt.Status is ReceiptStatus.Cancelled or ReceiptStatus.Dispatched)
            {
                throw new BusinessRuleException("Không thể hủy phiếu xuất đã xuất kho hoặc đã bị hủy.");
            }

            if (receipt.Status == ReceiptStatus.Approved) EnsureWriteEnabled();

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var wasApproved = receipt.Status == ReceiptStatus.Approved;
                if (receipt.Status == ReceiptStatus.Draft)
                {
                    receipt.CustomerCodeSnapshot = receipt.Customer?.Code;
                    receipt.CustomerNameSnapshot = receipt.Customer?.Name;
                }
                receipt.Status = ReceiptStatus.Cancelled;
                if (_stockReservationService is not null) await _stockReservationService.ReleaseSourceAsync("ExportReceipt", receipt.Id, userId, "Export receipt cancelled");
                await _exportReceiptRepository.UpdateAsync(receipt);
                
                await _auditLogRepository.AddAsync(new ERP.Domain.Entities.AuditLog
                {
                    UserId = userId,
                    Action = wasApproved ? "ExportReceipt.CancelledAndReleased" : "ExportReceipt.Cancelled",
                    EntityName = "ExportReceipt",
                    EntityId = receipt.Id,
                    WarehouseId = receipt.WarehouseId,
                    Result = "Success",
                    Reason = "Export receipt cancelled",
                    Severity = "Information",
                    Timestamp = DateTime.UtcNow,
                    OldValues = $"Status: {(wasApproved ? ReceiptStatus.Approved : ReceiptStatus.Draft)}",
                    NewValues = $"WarehouseId: {receipt.WarehouseId}; DispatchMode: {receipt.DispatchMode}; Status: {ReceiptStatus.Cancelled}"
                });

                await _unitOfWork.CommitTransactionAsync();
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        private ExportReceiptDto MapToDto(ERP.Domain.Entities.ExportReceipt receipt)
        {
            var viewer = string.Equals(_currentUser?.Role, "Viewer", StringComparison.Ordinal);
            return new ExportReceiptDto
            {
                Id = receipt.Id,
                Code = receipt.Code,
                WarehouseId = receipt.WarehouseId,
                WarehouseName = receipt.Warehouse?.Name,
                Status = receipt.Status.ToString(),
                Note = viewer ? null : receipt.Note,
                CreatedBy = receipt.CreatedBy,
                CreatedByName = receipt.CreatedByUser?.FullName ?? receipt.CreatedByUser?.Username,
                ApprovedBy = receipt.ApprovedBy,
                ApprovedByName = receipt.ApprovedByUser?.FullName ?? receipt.ApprovedByUser?.Username,
                CreatedAt = receipt.CreatedAt,
                ApprovedAt = receipt.ApprovedAt,
                DispatchMode = receipt.DispatchMode?.ToString(),
                DispatchedBy = receipt.DispatchedBy,
                DispatchedByName = receipt.DispatchedByUser?.FullName ?? receipt.DispatchedByUser?.Username,
                DispatchedAt = receipt.DispatchedAt,
                CustomerId = receipt.CustomerId,
                CustomerCode = receipt.Status == ReceiptStatus.Draft ? receipt.Customer?.Code : receipt.CustomerCodeSnapshot,
                CustomerName = receipt.Status == ReceiptStatus.Draft ? receipt.Customer?.Name : receipt.CustomerNameSnapshot,
                AllowPerReceiptDispatchMode = _options.AllowPerReceiptDispatchMode,
                AllowWarehouseStaffDirectDispatch = _options.AllowWarehouseStaffDirectDispatch,
                WriteEnabled = _options.WriteEnabled,
                ReservationStatus = receipt.Status == ReceiptStatus.Approved ? "Active" : receipt.Status == ReceiptStatus.Dispatched ? "Consumed" : receipt.Status == ReceiptStatus.Cancelled ? "Released" : "PendingApproval",
                Details = receipt.Details.Select(d => new ExportReceiptDetailDto
                {
                    Id = d.Id,
                    ProductId = d.ProductId,
                    ProductCode = d.Product?.Code,
                    ProductName = d.Product?.Name,
                    UnitName = d.BaseUomNameSnapshot ?? d.Product?.Unit?.Name,
                    Quantity = d.Quantity,
                    UnitPrice = viewer ? null : d.UnitPrice,
                    Note = viewer ? null : d.Note,
                    BaseUomIdSnapshot = d.BaseUomIdSnapshot,
                    BaseUomCodeSnapshot = d.BaseUomCodeSnapshot,
                    BaseUomNameSnapshot = d.BaseUomNameSnapshot,
                    BaseUomDecimalPlacesSnapshot = d.BaseUomDecimalPlacesSnapshot
                }).ToList()
            };
        }

        private void EnsureWriteEnabled()
        {
            if (!_options.WriteEnabled)
                throw new ServiceUnavailableException("Workflow xuất kho đang tạm dừng để bảo trì. Vui lòng thử lại sau.");
        }

    }
}
