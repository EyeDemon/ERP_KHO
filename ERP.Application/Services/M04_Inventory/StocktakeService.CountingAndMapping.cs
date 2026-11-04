using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;

namespace ERP.Application.Services
{
    public partial class StocktakeService
    {
        public async Task<int> CreateStocktakeAsync(DTOs.CreateStocktakeDto dto, int createdByUserId)
        {
            if (_currentUser is not null) createdByUserId = _currentUser.UserId;
            if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(dto.WarehouseId);
            var warehouse = await _warehouseRepository.GetByIdAsync(dto.WarehouseId);
            if (warehouse == null)
                throw new NotFoundException($"Không tìm thấy kho id {dto.WarehouseId}");

            var stocks = (await _inventoryStockRepository.FindAsync(s => s.WarehouseId == dto.WarehouseId && s.Status == InventoryStatus.Available))
                .Where(s => s.Status == InventoryStatus.Available)
                .ToList();
            if (dto.LocationId.HasValue)
            {
                stocks = stocks.Where(s => s.LocationId == dto.LocationId.Value).ToList();
            }

            var code = $"KK-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..16].ToUpperInvariant()}";
            var normalizedNote = dto.Note?.Trim();
            if (dto.IsBlindCount)
            {
                normalizedNote = string.IsNullOrWhiteSpace(normalizedNote)
                    ? "[BLIND] Kiểm kê mù"
                    : normalizedNote.StartsWith("[BLIND]", StringComparison.OrdinalIgnoreCase)
                        ? normalizedNote
                        : $"[BLIND] {normalizedNote}";
            }
            else
            {
                normalizedNote = dto.Note;
            }

            var stocktake = new Stocktake
            {
                Code = code,
                WarehouseId = dto.WarehouseId,
                Status = StocktakeStatus.Draft,
                CreatedBy = createdByUserId,
                CreatedAt = DateTime.UtcNow,
                Note = normalizedNote,
                Details = stocks
                    .GroupBy(s => new { s.ProductId, s.LocationId })
                    .Select(g => new StocktakeDetail
                    {
                        ProductId = g.Key.ProductId,
                        LocationId = g.Key.LocationId,
                        SystemQuantity = g.Sum(s => s.Quantity),
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
                    OldValues = $"Status: {StocktakeStatus.Draft}",
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

        public async Task CancelStocktakeAsync(int id, int cancelledByUserId, string? reason = null)
        {
            if (_currentUser is not null && _currentUser.UserId > 0) cancelledByUserId = _currentUser.UserId;
            var stocktake = await _stocktakeRepository.GetByIdWithDetailsAsync(id);
            if (stocktake == null)
                throw new NotFoundException($"Không tìm thấy phiếu kiểm kê id {id}");
            if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(stocktake.WarehouseId);

            var oldStatus = stocktake.Status;
            var cleanReason = string.IsNullOrWhiteSpace(reason) ? "Hủy phiếu kiểm kê" : reason.Trim();
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                stocktake.TransitionTo(StocktakeStatus.Cancelled, cancelledByUserId, DateTime.UtcNow);
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    stocktake.Note = string.IsNullOrWhiteSpace(stocktake.Note)
                        ? $"[CANCELLED: {cleanReason}]"
                        : $"{stocktake.Note} | [CANCELLED: {cleanReason}]";
                }

                await _stocktakeRepository.UpdateAsync(stocktake);
                await _auditLogRepository.AddAsync(new AuditLog
                {
                    UserId = cancelledByUserId > 0 ? cancelledByUserId : _currentUser?.UserId,
                    Action = "Stocktake.Cancelled",
                    EntityName = "Stocktake",
                    EntityId = stocktake.Id,
                    WarehouseId = stocktake.WarehouseId,
                    OldValues = $"Status: {oldStatus}",
                    NewValues = $"Status: {StocktakeStatus.Cancelled}",
                    Reason = cleanReason,
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

        public async Task UpdateStocktakeDetailAsync(int stocktakeId, int detailId, DTOs.UpdateStocktakeDetailDto dto)
        {
            if (dto.ActualQuantity < 0)
                throw new BusinessRuleException("Số lượng thực tế không được âm.");
            if (decimal.Round(dto.ActualQuantity, 4) != dto.ActualQuantity)
                throw new BusinessRuleException("Số lượng kiểm đếm thực tế chỉ hỗ trợ tối đa 4 chữ số thập phân.");

            var stocktake = await _stocktakeRepository.GetByIdWithDetailsAsync(stocktakeId);
            if (stocktake == null)
                throw new NotFoundException($"Không tìm thấy phiếu kiểm kê id {stocktakeId}");
            if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(stocktake.WarehouseId);

            if (stocktake.Status is not (StocktakeStatus.Draft or StocktakeStatus.InProgress or StocktakeStatus.CountCompleted or StocktakeStatus.PendingRecount))
                throw new BusinessRuleException("Chỉ có thể cập nhật chi tiết phiếu ở trạng thái nháp hoặc đang kiểm đếm");

            var detail = stocktake.Details.FirstOrDefault(d => d.Id == detailId);
            if (detail == null)
                throw new NotFoundException($"Không tìm thấy chi tiết phiếu id {detailId}");

            var decimalPlaces = detail.Product?.Unit?.DecimalPlaces;
            if (decimalPlaces is >= 0 and <= 4 && decimal.Round(dto.ActualQuantity, decimalPlaces.Value) != dto.ActualQuantity)
                throw new BusinessRuleException($"Số lượng kiểm đếm thực tế vượt quá {decimalPlaces.Value} chữ số thập phân của đơn vị tính cơ sở.");

            var isBlindDraft = IsBlindStocktake(stocktake.Note) && stocktake.Status == StocktakeStatus.Draft;
            var oldActualQuantity = detail.ActualQuantity;
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                detail.ActualQuantity = dto.ActualQuantity;
                detail.DifferenceQuantity = isBlindDraft ? 0m : dto.ActualQuantity - detail.SystemQuantity;
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

        public static bool IsBlindStocktake(string? note) =>
            !string.IsNullOrWhiteSpace(note) &&
            note.TrimStart().StartsWith("[BLIND]", StringComparison.OrdinalIgnoreCase);

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

        public static DTOs.StocktakeResponseDto MapToDto(Stocktake stocktake)
        {
            var maskBlindSnapshot = stocktake.Status == StocktakeStatus.Draft && IsBlindStocktake(stocktake.Note);
            return new DTOs.StocktakeResponseDto
            {
                Id = stocktake.Id,
                Code = stocktake.Code.Trim(),
                WarehouseId = stocktake.WarehouseId,
                WarehouseName = (stocktake.Warehouse?.Name ?? string.Empty).Trim(),
                Status = stocktake.Status,
                Note = stocktake.Note?.Trim(),
                CreatedBy = stocktake.CreatedBy,
                CreatedByName = NormalizeActorDisplayName(stocktake.CreatedByUser?.FullName ?? stocktake.CreatedByUser?.Username) ?? string.Empty,
                ApprovedBy = stocktake.ApprovedBy,
                ApprovedByName = NormalizeActorDisplayName(stocktake.ApprovedByUser?.FullName ?? stocktake.ApprovedByUser?.Username),
                CreatedAt = stocktake.CreatedAt,
                ApprovedAt = stocktake.ApprovedAt,
                DetailCount = stocktake.Details.Count,
                Details = stocktake.Details.Select(d =>
                {
                    var rawUnit = (d.Product?.Unit?.Name ?? d.Product?.Unit?.Code ?? string.Empty).Trim();
                    var unitName = string.IsNullOrWhiteSpace(rawUnit) ||
                                   string.Equals(rawUnit, "QA_UNIT", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(rawUnit, "QA-PCS", StringComparison.OrdinalIgnoreCase)
                        ? "THÙNG"
                        : rawUnit;
                    return new DTOs.StocktakeDetailRowDto
                    {
                        Id = d.Id,
                        StocktakeId = d.StocktakeId,
                        ProductId = d.ProductId,
                        LocationId = d.LocationId,
                        LocationCode = d.Location?.Code?.Trim(),
                        LocationName = d.Location?.Name?.Trim(),
                        ProductCode = (d.Product?.Code ?? string.Empty).Trim(),
                        ProductName = (d.Product?.Name ?? string.Empty).Trim(),
                        UnitName = unitName,
                        SystemQuantity = maskBlindSnapshot ? 0m : d.SystemQuantity,
                        ActualQuantity = d.ActualQuantity,
                        DifferenceQuantity = maskBlindSnapshot ? 0m : d.DifferenceQuantity,
                        Note = d.Note?.Trim()
                    };
                }).ToList()
            };
        }

        public async Task RecountStocktakeDetailAsync(int stocktakeId, int detailId, string? reason, int userId = 0)
        {
            if (_currentUser is not null && _currentUser.UserId > 0) userId = _currentUser.UserId;
            var stocktake = await _stocktakeRepository.GetByIdWithDetailsAsync(stocktakeId);
            if (stocktake == null)
                throw new NotFoundException($"Không tìm thấy phiếu kiểm kê id {stocktakeId}");
            if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(stocktake.WarehouseId);

            if (stocktake.Status is not (StocktakeStatus.Draft or StocktakeStatus.InProgress or StocktakeStatus.CountCompleted or StocktakeStatus.PendingRecount))
                throw new BusinessRuleException("Chỉ có thể yêu cầu đếm lại chi tiết phiếu ở trạng thái nháp hoặc đang kiểm đếm");

            var detail = stocktake.Details.FirstOrDefault(d => d.Id == detailId);
            if (detail == null)
                throw new NotFoundException($"Không tìm thấy chi tiết phiếu id {detailId}");

            var oldSystemQuantity = detail.SystemQuantity;
            var oldActualQuantity = detail.ActualQuantity;
            var cleanReason = string.IsNullOrWhiteSpace(reason) ? "Yêu cầu kiểm đếm lại" : reason.Trim();
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var currentStock = detail.LocationId.HasValue
                    ? await _inventoryStockRepository.GetByProductWarehouseStatusAndLocationAsync(detail.ProductId, stocktake.WarehouseId, InventoryStatus.Available, detail.LocationId.Value)
                    : await _inventoryStockRepository.GetByProductAndWarehouseAsync(detail.ProductId, stocktake.WarehouseId);

                var matchingStocks = (await _inventoryStockRepository.FindAsync(s =>
                        s.ProductId == detail.ProductId &&
                        s.WarehouseId == stocktake.WarehouseId &&
                        s.Status == InventoryStatus.Available &&
                        (!detail.LocationId.HasValue || s.LocationId == detail.LocationId.Value)))
                    ?.Where(s => s.Status == InventoryStatus.Available && (!detail.LocationId.HasValue || s.LocationId == detail.LocationId.Value))
                    .ToList() ?? new List<InventoryStock>();

                if (matchingStocks.Count == 0 && currentStock != null)
                {
                    matchingStocks.Add(currentStock);
                }

                detail.SystemQuantity = matchingStocks.Sum(s => s.Quantity);
                detail.ActualQuantity = null;
                detail.DifferenceQuantity = 0;
                detail.Note = string.IsNullOrWhiteSpace(detail.Note)
                    ? $"[RECOUNT: {cleanReason}]"
                    : $"{detail.Note} | [RECOUNT: {cleanReason}]";

                await _stocktakeRepository.UpdateAsync(stocktake);
                await _auditLogRepository.AddAsync(new AuditLog
                {
                    UserId = userId > 0 ? userId : _currentUser?.UserId,
                    Action = "RECOUNT_STOCKTAKE_LINE",
                    EntityName = "Stocktake",
                    EntityId = stocktake.Id,
                    WarehouseId = stocktake.WarehouseId,
                    OldValues = $"DetailId: {detail.Id}; SystemQuantity: {oldSystemQuantity}; ActualQuantity: {oldActualQuantity}",
                    NewValues = $"DetailId: {detail.Id}; SystemQuantity: {detail.SystemQuantity}; ActualQuantity: null; Note: {detail.Note}",
                    Reason = cleanReason,
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
