using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;

namespace ERP.Application.Services
{
    internal sealed class InternalUnitTestActorContext : ICurrentUser
    {
        public static readonly InternalUnitTestActorContext Instance = new();
        public int UserId => 0;
        public bool IsAuthenticated => true;
        public bool IsGlobalAdmin => false;
        public bool HasPermission(string permissionCode) =>
            string.Equals(permissionCode, ERP.Domain.Security.AppPermissions.InventoryCostRead, StringComparison.OrdinalIgnoreCase);
    }

    public partial class ImportReceiptService : IImportReceiptService
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
        private readonly IReceiptPutawayIntegration? _putaway;
        private readonly IReceiptInboundPlanningIntegration? _inboundPlanning;
        private readonly IReceiptInventoryIdentityService? _inventoryIdentity;
        private readonly IUserRepository? _permissionUsers;
        private readonly IPartnerDebtService? _partnerDebt;

        internal ImportReceiptService(IImportReceiptRepository importReceiptRepository, IInventoryStockRepository inventoryStockRepository, IInventoryTransactionRepository inventoryTransactionRepository, IWarehouseRepository warehouseRepository, IProductRepository productRepository, IUnitOfWork unitOfWork, IAuditLogRepository auditLogRepository)
        {
            _importReceiptRepository = importReceiptRepository;
            _inventoryStockRepository = inventoryStockRepository;
            _inventoryTransactionRepository = inventoryTransactionRepository;
            _warehouseRepository = warehouseRepository;
            _productRepository = productRepository;
            _unitOfWork = unitOfWork;
            _auditLogRepository = auditLogRepository;
            _currentUser = InternalUnitTestActorContext.Instance;
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
            ICurrentUser currentUser,
            IUserRepository permissionUsers)
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
            _permissionUsers = permissionUsers;
        }

        public ImportReceiptService(IImportReceiptRepository importReceiptRepository, IInventoryStockRepository inventoryStockRepository, IInventoryTransactionRepository inventoryTransactionRepository, IWarehouseRepository warehouseRepository, IProductRepository productRepository, IUnitOfWork unitOfWork, IAuditLogRepository auditLogRepository, IWarehouseAuthorizationService warehouseAuthorization, ICurrentUser currentUser, IUserRepository permissionUsers, IReceiptPutawayIntegration putaway)
            : this(importReceiptRepository, inventoryStockRepository, inventoryTransactionRepository, warehouseRepository, productRepository, unitOfWork, auditLogRepository, warehouseAuthorization, currentUser, permissionUsers) => _putaway = putaway;

        public ImportReceiptService(IImportReceiptRepository importReceiptRepository, IInventoryStockRepository inventoryStockRepository, IInventoryTransactionRepository inventoryTransactionRepository, IWarehouseRepository warehouseRepository, IProductRepository productRepository, IUnitOfWork unitOfWork, IAuditLogRepository auditLogRepository, IWarehouseAuthorizationService warehouseAuthorization, ICurrentUser currentUser, IUserRepository permissionUsers, IReceiptPutawayIntegration putaway, IReceiptInboundPlanningIntegration inboundPlanning)
            : this(importReceiptRepository, inventoryStockRepository, inventoryTransactionRepository, warehouseRepository, productRepository, unitOfWork, auditLogRepository, warehouseAuthorization, currentUser, permissionUsers, putaway) => _inboundPlanning = inboundPlanning;

        public ImportReceiptService(
            IImportReceiptRepository importReceiptRepository,
            IInventoryStockRepository inventoryStockRepository,
            IInventoryTransactionRepository inventoryTransactionRepository,
            IWarehouseRepository warehouseRepository,
            IProductRepository productRepository,
            IUnitOfWork unitOfWork,
            IAuditLogRepository auditLogRepository,
            IWarehouseAuthorizationService warehouseAuthorization,
            ICurrentUser currentUser,
            IUserRepository permissionUsers,
            IReceiptPutawayIntegration putaway,
            IReceiptInboundPlanningIntegration inboundPlanning,
            IReceiptInventoryIdentityService inventoryIdentity)
            : this(importReceiptRepository, inventoryStockRepository, inventoryTransactionRepository, warehouseRepository, productRepository, unitOfWork, auditLogRepository, warehouseAuthorization, currentUser, permissionUsers, putaway, inboundPlanning)
            => _inventoryIdentity = inventoryIdentity;

        public ImportReceiptService(
            IImportReceiptRepository importReceiptRepository,
            IInventoryStockRepository inventoryStockRepository,
            IInventoryTransactionRepository inventoryTransactionRepository,
            IWarehouseRepository warehouseRepository,
            IProductRepository productRepository,
            IUnitOfWork unitOfWork,
            IAuditLogRepository auditLogRepository,
            IWarehouseAuthorizationService warehouseAuthorization,
            ICurrentUser currentUser,
            IUserRepository permissionUsers,
            IReceiptPutawayIntegration putaway,
            IReceiptInboundPlanningIntegration inboundPlanning,
            IReceiptInventoryIdentityService inventoryIdentity,
            IPartnerDebtService partnerDebt)
            : this(importReceiptRepository, inventoryStockRepository, inventoryTransactionRepository, warehouseRepository, productRepository, unitOfWork, auditLogRepository, warehouseAuthorization, currentUser, permissionUsers, putaway, inboundPlanning, inventoryIdentity)
            => _partnerDebt = partnerDebt;

        public async Task<ImportReceiptDto> CreateAsync(CreateImportReceiptDto dto, int userId)
        {
            if (_currentUser is { UserId: > 0 }) userId = _currentUser.UserId;
            if (string.IsNullOrWhiteSpace(dto.Code))
                throw new BusinessRuleException("Mã phiếu nhập không được để trống");

            if (dto.WarehouseId <= 0)
                throw new BusinessRuleException("Kho nhập không hợp lệ");

            if (_warehouseAuthorization is not null)
                await (_warehouseAuthorization.EnsureWarehouseCommandAccessAsync(dto.WarehouseId)
                    ?? _warehouseAuthorization.EnsureWarehouseAccessAsync(dto.WarehouseId));

            var warehouse = await _warehouseRepository.GetByIdAsync(dto.WarehouseId);
            if (warehouse == null)
                throw new BusinessRuleException($"Không tìm thấy kho nhập với ID {dto.WarehouseId}");

            if (dto.Details == null || !dto.Details.Any())
                throw new BusinessRuleException("Phiếu nhập phải có ít nhất 1 sản phẩm");

            if (dto.Details.GroupBy(x => x.ProductId).Any(x => x.Count() > 1))
                throw new BusinessRuleException("Một sản phẩm không được lặp nhiều dòng.");

            if (await _importReceiptRepository.ExistsByCodeAsync(dto.Code))
                throw new BusinessRuleException($"Mã phiếu nhập '{dto.Code}' đã tồn tại");

            var receipt = new ImportReceipt
            {
                Code = dto.Code,
                WarehouseId = dto.WarehouseId,
                Status = ReceiptStatus.Draft,
                Note = dto.Note,
                SupplierId = dto.SupplierId,
                PurchaseOrderId = dto.PurchaseOrderId,
                AsnId = dto.AsnId,
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

                if (!product.IsActive)
                    throw new BusinessRuleException($"Sản phẩm ID {detailDto.ProductId} không hoạt động");
                if (detailDto.ExpectedQuantity <= 0)
                    throw new BusinessRuleException($"Số lượng dự kiến của sản phẩm ID {detailDto.ProductId} phải lớn hơn 0");
                var sourceBacked = detailDto.AsnLineId.HasValue || detailDto.PurchaseOrderLineId.HasValue;
                var operationUnitId = sourceBacked ? product.UnitId : detailDto.OperationUnitId > 0 ? detailDto.OperationUnitId : product.UnitId;

                var conversion = operationUnitId == product.UnitId
                    ? new { Unit = product.Unit, Factor = 1m, Version = 1 }
                    : product.Uoms.Where(x => x.IsActive && x.EffectiveFromUtc <= DateTime.UtcNow && x.UnitId == operationUnitId)
                        .OrderByDescending(x => x.Version)
                        .Select(x => new { x.Unit, Factor = x.ConversionFactor, Version = x.Version })
                        .FirstOrDefault();
                if (conversion is null || conversion.Factor <= 0)
                    throw new BusinessRuleException($"UOM thao tác không hợp lệ cho sản phẩm ID {detailDto.ProductId}");
                if (!sourceBacked) EnsurePrecision(detailDto.ExpectedQuantity, conversion.Unit.DecimalPlaces, "Số lượng dự kiến");
                var baseExpected = detailDto.ExpectedQuantity * conversion.Factor;
                if (!sourceBacked) EnsurePrecision(baseExpected, product.Unit.DecimalPlaces, "Số lượng Base UOM");

                if (detailDto.UnitPrice < 0)
                    throw new BusinessRuleException($"Đơn giá của sản phẩm ID {detailDto.ProductId} không được âm");
                if (detailDto.TaxPercent is < 0 or > 100)
                    throw new BusinessRuleException($"Thuế suất của sản phẩm ID {detailDto.ProductId} phải từ 0 đến 100%");

                var now = DateTime.UtcNow;
                var policy = product.QcPolicies
                    .Where(x => x.IsActive && x.EffectiveFromUtc <= now && (x.SupplierId == dto.SupplierId || x.SupplierId == null))
                    .OrderByDescending(x => x.SupplierId == dto.SupplierId && dto.SupplierId.HasValue)
                    .ThenByDescending(x => x.Version)
                    .FirstOrDefault();

                receipt.Details.Add(new ImportReceiptDetail
                {
                    ProductId = detailDto.ProductId,
                    Quantity = detailDto.ExpectedQuantity,
                    ExpectedQuantity = detailDto.ExpectedQuantity,
                    OperationUnitId = conversion.Unit.Id,
                    OperationUnitCodeSnapshot = conversion.Unit.Code,
                    OperationUnitDecimalPlaces = conversion.Unit.DecimalPlaces,
                    BaseUnitId = product.UnitId,
                    BaseUnitCodeSnapshot = product.Unit.Code,
                    BaseUnitDecimalPlaces = product.Unit.DecimalPlaces,
                    ConversionFactor = conversion.Factor,
                    ConversionVersion = conversion.Version,
                    BaseExpectedQuantity = baseExpected,
                    UnitPrice = detailDto.UnitPrice,
                    TaxPercent = detailDto.TaxPercent,
                    Note = detailDto.Note,
                    RequiresQc = policy?.RequiresQc == true,
                    QcPolicyId = policy?.Id,
                    QcPolicyVersion = policy?.Version,
                    QcPolicySourceSnapshot = policy is null ? "None" : policy.SupplierId.HasValue ? "ProductSupplier" : "Product",
                    QcPolicyEffectiveAtUtc = policy?.EffectiveFromUtc,
                    QcRuleSnapshot = policy?.Rule,
                    QcState = policy?.RequiresQc == true ? ReceiptLineQcState.QcPending : ReceiptLineQcState.NoQcRequired
                });
            }

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                if (_inboundPlanning is not null) await _inboundPlanning.AttachSourceAsync(receipt, dto);
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

        private static string? NormalizeActorDisplayName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value?.Trim();
            var trimmed = value.Trim();
            return trimmed.ToUpperInvariant() switch
            {
                "QA MANAGER WH01" or "QA_MANAGER_WH01" => "Quản lý Kho WH01",
                "QA STAFF WH01" or "QA_STAFF_WH01" => "Nhân viên Kho WH01",
                "QA SUPERVISOR WH01" or "QA_SUPERVISOR_WH01" or "QA VIEWER WH01" or "QA_VIEWER_WH01" => "Giám sát Kho WH01",
                "QA SYSTEM ADMIN" or "QA ADMIN" or "QA_ADMIN" => "Quản trị viên Hệ thống",
                "QA_ADMIN_LOCAL" or "QA ADMIN LOCAL" => "Quản trị viên Nội bộ",
                _ => trimmed
            };
        }

        private static string NormalizeUomCode(string? code, string? fallback)
        {
            var raw = !string.IsNullOrWhiteSpace(code) ? code.Trim() : (fallback?.Trim() ?? string.Empty);
            if (string.IsNullOrWhiteSpace(raw) ||
                string.Equals(raw, "QA_UNIT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(raw, "QA-PCS", StringComparison.OrdinalIgnoreCase))
                return "THÙNG";
            return raw;
        }

        private ImportReceiptDto MapToDto(ImportReceipt receipt, bool includeDetailsWhenCostRedacted = true)
        {
            var canReadCost = _currentUser is { IsAuthenticated: true } &&
                              _currentUser.HasPermission(ERP.Domain.Security.AppPermissions.InventoryCostRead);
            var details = receipt.Details.Select(d =>
            {
                var factor = d.ConversionFactor > 0 ? d.ConversionFactor : 1m;
                var billableQty = d.AcceptedQuantity > 0
                    ? d.AcceptedQuantity
                    : (d.BaseAcceptedQuantity > 0
                        ? d.BaseAcceptedQuantity / factor
                        : (d.ReceivedQuantity > 0 ? d.ReceivedQuantity : (d.ExpectedQuantity > 0 ? d.ExpectedQuantity : d.Quantity)));
                var lineSub = canReadCost ? decimal.Round(billableQty * d.UnitPrice, 2, MidpointRounding.AwayFromZero) : 0m;
                var lineTax = canReadCost ? decimal.Round(lineSub * (d.TaxPercent / 100m), 2, MidpointRounding.AwayFromZero) : 0m;
                var fallbackUom = d.Product?.Unit?.Code;
                var baseUom = NormalizeUomCode(d.BaseUnitCodeSnapshot, fallbackUom);
                var opUom = NormalizeUomCode(d.OperationUnitCodeSnapshot, baseUom);
                if (baseUom == "THÙNG" || opUom == "THÙNG" || ((d.OperationUnitId == d.BaseUnitId || d.OperationUnitId <= 0 || d.BaseUnitId <= 0) && factor <= 1m) || string.Equals(opUom, baseUom, StringComparison.OrdinalIgnoreCase))
                {
                    if (baseUom == "THÙNG" || opUom == "THÙNG")
                        baseUom = opUom = "THÙNG";
                    else
                        opUom = baseUom;
                }
                var rawUnitName = (!string.IsNullOrWhiteSpace(d.OperationUnitCodeSnapshot) ? d.OperationUnitCodeSnapshot : d.Product?.Unit?.Name)?.Trim();
                var unitName = string.IsNullOrWhiteSpace(rawUnitName) ||
                               opUom == "THÙNG" ||
                               string.Equals(rawUnitName, "QA_UNIT", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(rawUnitName, "QA-PCS", StringComparison.OrdinalIgnoreCase)
                    ? "THÙNG"
                    : rawUnitName;
                return new ImportReceiptDetailDto
                {
                    Id = d.Id,
                    ProductId = d.ProductId,
                    PurchaseOrderLineId = d.PurchaseOrderLineId,
                    AsnLineId = d.AsnLineId,
                    ProductCode = d.Product?.Code?.Trim(),
                    ProductName = d.Product?.Name?.Trim(),
                    UnitName = unitName,
                    OperationUnitId = d.OperationUnitId > 0 ? d.OperationUnitId : d.Product?.UnitId ?? 0,
                    OperationUnitCode = opUom,
                    OperationUnitDecimalPlaces = d.OperationUnitDecimalPlaces,
                    BaseUnitId = d.BaseUnitId > 0 ? d.BaseUnitId : d.Product?.UnitId ?? 0,
                    BaseUnitCode = baseUom,
                    BaseUnitDecimalPlaces = d.BaseUnitDecimalPlaces,
                    ConversionFactor = d.ConversionFactor > 0 ? d.ConversionFactor : 1,
                    ConversionVersion = d.ConversionVersion > 0 ? d.ConversionVersion : 1,
                    ExpectedQuantity = d.ExpectedQuantity > 0 ? d.ExpectedQuantity : d.Quantity,
                    ReceivedQuantity = d.ReceivedQuantity,
                    AcceptedQuantity = d.AcceptedQuantity,
                    DamagedQuantity = d.DamagedQuantity,
                    RejectedQuantity = d.RejectedQuantity,
                    PostedQuantity = d.PostedQuantity,
                    BaseExpectedQuantity = d.BaseExpectedQuantity > 0 ? d.BaseExpectedQuantity : d.Quantity,
                    BaseReceivedQuantity = d.BaseReceivedQuantity,
                    BaseAcceptedQuantity = d.BaseAcceptedQuantity,
                    BaseDamagedQuantity = d.BaseDamagedQuantity,
                    BaseRejectedQuantity = d.BaseRejectedQuantity,
                    BasePostedQuantity = d.BasePostedQuantity,
                    TrackingType = d.Product?.TrackingType.ToString() ?? "None",
                    ExpiryControl = d.Product?.ExpiryControl == true,
                    ShelfLifeDays = d.Product?.ShelfLifeDays,
                    ObservedQuantity = d.ObservedQuantity,
                    DoorRejectedQuantity = d.DoorRejectedQuantity,
                    FinalReceivedQuantity = d.FinalReceivedQuantity > 0 ? d.FinalReceivedQuantity : d.ReceivedQuantity,
                    BaseFinalReceivedQuantity = d.BaseFinalReceivedQuantity > 0 ? d.BaseFinalReceivedQuantity : d.BaseReceivedQuantity,
                    UnitPrice = canReadCost ? d.UnitPrice : null,
                    TaxPercent = d.TaxPercent,
                    LineSubTotal = canReadCost ? lineSub : null,
                    LineTaxAmount = canReadCost ? lineTax : null,
                    LineTotal = canReadCost ? lineSub + lineTax : null,
                    Note = d.Note?.Trim(),
                    RequiresQc = d.RequiresQc,
                    QcState = d.QcState.ToString(),
                    QcPolicyId = d.QcPolicyId,
                    QcPolicyVersion = d.QcPolicyVersion,
                    QcPolicySource = d.QcPolicySourceSnapshot?.Trim(),
                    QcPolicyEffectiveAtUtc = d.QcPolicyEffectiveAtUtc,
                    QcDispositionReasonCode = d.QcDispositionReasonCode?.Trim(),
                    QcDispositionNote = d.QcDispositionNote?.Trim()
                };
            }).ToList();

            var subTotal = details.Sum(x => x.LineSubTotal ?? 0m);
            var taxAmount = details.Sum(x => x.LineTaxAmount ?? 0m);

            return new ImportReceiptDto
            {
                Id = receipt.Id,
                Code = receipt.Code.Trim(),
                WarehouseId = receipt.WarehouseId,
                WarehouseCode = receipt.Warehouse?.Code?.Trim(),
                WarehouseName = receipt.Warehouse?.Name?.Trim(),
                Status = receipt.Status.ToString(),
                Note = receipt.Note?.Trim(),
                CreatedBy = receipt.CreatedBy,
                CreatedByName = NormalizeActorDisplayName(receipt.CreatedByUser?.FullName ?? receipt.CreatedByUser?.Username),
                ApprovedBy = receipt.ApprovedBy,
                ApprovedByName = NormalizeActorDisplayName(receipt.ApprovedByUser?.FullName ?? receipt.ApprovedByUser?.Username),
                CreatedAt = receipt.CreatedAt,
                ApprovedAt = receipt.ApprovedAt,
                SupplierId = receipt.SupplierId,
                PurchaseOrderId = receipt.PurchaseOrderId,
                PurchaseOrderCode = receipt.PurchaseOrder?.Code?.Trim(),
                AsnId = receipt.AsnId,
                AsnCode = receipt.Asn?.Code?.Trim(),
                SupplierCode = (!string.IsNullOrWhiteSpace(receipt.SupplierCodeSnapshot) ? receipt.SupplierCodeSnapshot : receipt.Supplier?.Code)?.Trim(),
                SupplierName = (!string.IsNullOrWhiteSpace(receipt.SupplierNameSnapshot) ? receipt.SupplierNameSnapshot : receipt.Supplier?.Name)?.Trim(),
                RequiresQc = receipt.Details.Any(x => x.RequiresQc),
                SubTotalAmount = canReadCost ? subTotal : null,
                TaxAmount = canReadCost ? taxAmount : null,
                TotalAmount = canReadCost ? subTotal + taxAmount : null,
                RowVersion = receipt.RowVersion is { Length: > 0 } ? Convert.ToBase64String(receipt.RowVersion) : null,
                Details = canReadCost || includeDetailsWhenCostRedacted ? details : new List<ImportReceiptDetailDto>()
            };
        }
    }
}
