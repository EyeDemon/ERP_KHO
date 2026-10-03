using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace ERP.Infrastructure.Services;

public sealed class ReceivingDiscrepancyService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser) : IReceivingDiscrepancyService
{
    public async Task<IReadOnlyList<ReceivingDiscrepancyDto>> GetAsync(int receiptId, CancellationToken token)
    {
        var receipt = await LoadReceipt(receiptId, token);
        return receipt.Discrepancies.OrderBy(x => x.ImportReceiptDetailId).Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ReceivingReasonCodeDto>> GetActiveReasonsAsync(CancellationToken token)
    {
        var now = DateTime.UtcNow;
        return await context.ReceivingReasonCodes.AsNoTracking()
            .Where(x => x.IsActive && x.EffectiveFromUtc <= now && (x.EffectiveToUtc == null || x.EffectiveToUtc > now))
            .OrderBy(x => x.Code)
            .Select(x => new ReceivingReasonCodeDto { Code = x.Code, Name = x.Name, Category = x.Category, Version = x.Version, RequiresNote = x.RequiresNote, RequiresAttachment = x.RequiresAttachment, RequiresApproval = x.RequiresApproval })
            .ToListAsync(token);
    }

    public async Task<IReadOnlyList<ReceivingDiscrepancyDto>> ObserveAsync(int receiptId, ObserveReceivingDto dto, CancellationToken token)
    {
        var receipt = await LoadReceipt(receiptId, token);
        if (receipt.Status is not (ReceiptStatus.Draft or ReceiptStatus.DiscrepancyPending or ReceiptStatus.DiscrepancyRejected))
            throw Conflict("Phiếu nhập không ở trạng thái có thể ghi nhận số lượng quan sát.");
        if (dto.Lines.Count != receipt.Details.Count || dto.Lines.Select(x => x.LineId).Distinct().Count() != dto.Lines.Count)
            throw new BusinessRuleException("Phải gửi đúng một observation cho mỗi dòng phiếu nhập.");

        var now = DateTime.UtcNow;
        foreach (var input in dto.Lines)
        {
            var line = receipt.Details.SingleOrDefault(x => x.Id == input.LineId)
                ?? throw new BusinessRuleException($"Dòng {input.LineId} không thuộc phiếu nhập.");
            var (unitId, unitCode, factor, conversionVersion, decimalPlaces) = await ResolveObservedUom(line, input.ObservedUnitId, now, token);
            EnsureQuantity(input.ObservedQuantity, decimalPlaces, "Số lượng quan sát");
            var baseObserved = input.ObservedQuantity * factor;
            EnsureQuantity(baseObserved, line.BaseUnitDecimalPlaces, "Số lượng quan sát Base UOM");
            if (input.Items.Count > 0 && input.Items.Sum(x => x.Quantity) != input.ObservedQuantity)
                throw new BusinessRuleException("Tổng observation items phải bằng số lượng quan sát.");

            var discrepancy = receipt.Discrepancies.SingleOrDefault(x => x.ImportReceiptDetailId == line.Id);
            if (discrepancy is null)
            {
                discrepancy = new ReceivingDiscrepancy { ImportReceipt = receipt, ImportReceiptDetail = line, CreatedBy = currentUser.UserId, CreatedAtUtc = now };
                context.ReceivingDiscrepancies.Add(discrepancy);
            }
            var previous = discrepancy.Observations.OrderByDescending(x => x.Version).FirstOrDefault();
            var observation = new ReceivingObservationVersion
            {
                ReceivingDiscrepancy = discrepancy, Version = (previous?.Version ?? 0) + 1, PreviousObservationVersion = previous,
                ObservedQuantity = input.ObservedQuantity, BaseObservedQuantity = baseObserved, ObservedUnitId = unitId,
                ObservedUnitCodeSnapshot = unitCode, ConversionFactorSnapshot = factor, ConversionVersionSnapshot = conversionVersion,
                CreatedBy = currentUser.UserId, CreatedAtUtc = now,
                Items = input.Items.Select(x => new ReceivingObservationItem { Quantity = x.Quantity, ScanReference = x.ScanReference?.Trim() }).ToList()
            };
            discrepancy.Observations.Add(observation);
            line.ObservedQuantity = input.ObservedQuantity;
            line.BaseObservedQuantity = baseObserved;
            if (baseObserved == line.BaseExpectedQuantity)
            {
                ApplyFinal(line, NormalizeToOperationUom(line, baseObserved), baseObserved, 0, 0, null);
                discrepancy.Status = ReceivingDiscrepancyStatus.Resolved;
            }
            else discrepancy.Status = ReceivingDiscrepancyStatus.Pending;
        }

        receipt.Status = receipt.Discrepancies.All(x => x.Status == ReceivingDiscrepancyStatus.Resolved)
            ? RouteAfterResolution(receipt) : ReceiptStatus.DiscrepancyPending;
        context.AuditLogs.Add(Audit(receipt, "ImportReceipt.ReceivingObserved", $"Status: {receipt.Status}"));
        await Save(token);
        return receipt.Discrepancies.OrderBy(x => x.ImportReceiptDetailId).Select(Map).ToList();
    }

    public async Task<ReceivingDiscrepancyDto> SubmitAsync(int receiptId, int discrepancyId, SubmitReceivingDiscrepancyDto dto, CancellationToken token)
    {
        var receipt = await LoadReceipt(receiptId, token);
        var discrepancy = Find(receipt, discrepancyId);
        ApplyRowVersion(discrepancy, dto.RowVersion);
        if (receipt.Status is not (ReceiptStatus.DiscrepancyPending or ReceiptStatus.DiscrepancyRejected) || discrepancy.Status is not (ReceivingDiscrepancyStatus.Pending or ReceivingDiscrepancyStatus.Rejected))
            throw Conflict("Discrepancy không ở trạng thái có thể submit.");
        var observation = discrepancy.Observations.OrderByDescending(x => x.Version).FirstOrDefault() ?? throw Conflict("Discrepancy chưa có observation.");
        var reason = await ActiveReason(dto.ReasonCode, token);
        var action = ParseAction(dto.Action);
        var line = discrepancy.ImportReceiptDetail;
        var normalizedObserved = NormalizeToOperationUom(line, observation.BaseObservedQuantity);
        if (observation.ObservedUnitId != line.OperationUnitId && !dto.ConfirmNormalizedObservation)
            throw new BusinessRuleException("UOM quan sát khác UOM dự kiến; cần xác nhận normalized observation trước khi resolve.");
        EnsureQuantity(dto.DoorRejectedQuantity, line.OperationUnitDecimalPlaces, "Số lượng từ chối tại cửa");
        if (dto.DoorRejectedQuantity > normalizedObserved) throw new BusinessRuleException("Số lượng từ chối tại cửa không được vượt số lượng quan sát đã chuẩn hóa.");
        if ((reason.RequiresNote || dto.ResponsibleParty == ResponsibleParty.Unknown || action is ReceivingResolutionAction.RejectAtDoor or ReceivingResolutionAction.RejectWrongProduct) && string.IsNullOrWhiteSpace(dto.Note))
            throw new BusinessRuleException("Lý do này yêu cầu ghi chú.");
        if (reason.RequiresAttachment && string.IsNullOrWhiteSpace(dto.EvidenceReference))
            throw new BusinessRuleException("Lý do này yêu cầu evidence reference.");

        var doorRejected = dto.DoorRejectedQuantity;
        var final = action switch
        {
            ReceivingResolutionAction.AcceptObserved => normalizedObserved - doorRejected,
            ReceivingResolutionAction.AcceptExpectedRejectExcess when normalizedObserved >= line.ExpectedQuantity => line.ExpectedQuantity,
            ReceivingResolutionAction.RejectAtDoor => normalizedObserved - doorRejected,
            ReceivingResolutionAction.RejectWrongProduct when doorRejected == normalizedObserved => 0,
            ReceivingResolutionAction.RouteToQc => normalizedObserved - doorRejected,
            _ => throw new BusinessRuleException("Resolution action và quantity không hợp lệ.")
        };
        if (action == ReceivingResolutionAction.AcceptExpectedRejectExcess) doorRejected = normalizedObserved - line.ExpectedQuantity;
        var baseDoor = doorRejected * line.ConversionFactor;
        var baseFinal = final * line.ConversionFactor;
        EnsureQuantity(baseDoor, line.BaseUnitDecimalPlaces, "Door Rejected Base UOM");
        EnsureQuantity(baseFinal, line.BaseUnitDecimalPlaces, "Final Received Base UOM");

        var policy = await SelectPolicy(receipt, line, token);
        var allowed = Math.Max(policy?.AbsoluteQuantityTolerance ?? 0, line.BaseExpectedQuantity * (policy?.PercentageTolerance ?? 0));
        var difference = observation.BaseObservedQuantity - line.BaseExpectedQuantity;
        var outside = Math.Abs(difference) > allowed;
        var directionDisallowed = difference > 0 ? policy?.OverageAllowed == false : difference < 0 && policy?.ShortageAllowed == false;
        var valueOutside = policy?.ValueTolerance is decimal valueTolerance && Math.Abs(difference) * line.UnitPrice > valueTolerance;
        var requiresApproval = reason.RequiresApproval || directionDisallowed || valueOutside || (outside && (policy?.RequiresApprovalOutsideTolerance ?? true));
        var previous = discrepancy.Resolutions.OrderByDescending(x => x.Version).FirstOrDefault();
        var now = DateTime.UtcNow;
        var resolution = new ReceivingResolutionVersion
        {
            ReceivingDiscrepancy = discrepancy, Version = (previous?.Version ?? 0) + 1, PreviousResolutionVersion = previous,
            Action = action, DoorRejectedQuantity = doorRejected, BaseDoorRejectedQuantity = baseDoor, FinalReceivedQuantity = final, BaseFinalReceivedQuantity = baseFinal,
            ReasonCodeId = reason.Id, ReasonCodeSnapshot = reason.Code, ReasonNameSnapshot = reason.Name, ReasonCategorySnapshot = reason.Category,
            ReasonVersionSnapshot = reason.Version, ReasonEffectiveAtUtcSnapshot = reason.EffectiveFromUtc, ReasonRequiresNoteSnapshot = reason.RequiresNote,
            ReasonRequiresAttachmentSnapshot = reason.RequiresAttachment, ReasonRequiresApprovalSnapshot = reason.RequiresApproval,
            TolerancePolicyId = policy?.Id, TolerancePolicyVersionSnapshot = policy?.Version, TolerancePolicySourceSnapshot = PolicySource(policy), TolerancePolicyEffectiveAtUtcSnapshot = policy?.EffectiveFromUtc,
            AbsoluteToleranceSnapshot = policy?.AbsoluteQuantityTolerance ?? 0, PercentageToleranceSnapshot = policy?.PercentageTolerance ?? 0,
            AllowedBaseToleranceSnapshot = allowed, ValueToleranceSnapshot = policy?.ValueTolerance, RequiresApproval = requiresApproval,
            ResponsibleParty = dto.ResponsibleParty, SupplierClaimRequired = dto.SupplierClaimRequired, Note = dto.Note?.Trim(), EvidenceReference = dto.EvidenceReference?.Trim(),
            SubmittedBy = currentUser.UserId, SubmittedAtUtc = now
        };
        discrepancy.Resolutions.Add(resolution);
        if (requiresApproval)
        {
            discrepancy.Status = ReceivingDiscrepancyStatus.PendingApproval;
            receipt.Status = ReceiptStatus.DiscrepancyPendingApproval;
        }
        else
        {
            Resolve(receipt, discrepancy, resolution, currentUser.UserId, now);
        }
        context.AuditLogs.Add(Audit(receipt, "ImportReceipt.DiscrepancySubmitted", $"DiscrepancyId: {discrepancy.Id}; Version: {resolution.Version}; Approval: {requiresApproval}"));
        await Save(token);
        return Map(discrepancy);
    }

    public async Task<ReceivingDiscrepancyDto> ApproveAsync(int receiptId, int discrepancyId, byte[] rowVersion, CancellationToken token)
    {
        var receipt = await LoadReceipt(receiptId, token); var discrepancy = Find(receipt, discrepancyId); ApplyRowVersion(discrepancy, rowVersion);
        if (discrepancy.Status != ReceivingDiscrepancyStatus.PendingApproval) throw Conflict("Discrepancy không chờ phê duyệt.");
        var resolution = discrepancy.Resolutions.OrderByDescending(x => x.Version).First();
        if (resolution.SubmittedBy == currentUser.UserId) throw new ForbiddenException("Người submit không được tự phê duyệt resolution.");
        var now = DateTime.UtcNow; resolution.ApprovedBy = currentUser.UserId; resolution.ApprovedAtUtc = now;
        Resolve(receipt, discrepancy, resolution, currentUser.UserId, now);
        context.AuditLogs.Add(Audit(receipt, "ImportReceipt.DiscrepancyApproved", $"DiscrepancyId: {discrepancy.Id}; Version: {resolution.Version}"));
        await Save(token); return Map(discrepancy);
    }

    public async Task<ReceivingDiscrepancyDto> RejectAsync(int receiptId, int discrepancyId, byte[] rowVersion, CancellationToken token)
    {
        var receipt = await LoadReceipt(receiptId, token); var discrepancy = Find(receipt, discrepancyId); ApplyRowVersion(discrepancy, rowVersion);
        if (discrepancy.Status != ReceivingDiscrepancyStatus.PendingApproval) throw Conflict("Discrepancy không chờ phê duyệt.");
        var resolution = discrepancy.Resolutions.OrderByDescending(x => x.Version).First();
        if (resolution.SubmittedBy == currentUser.UserId) throw new ForbiddenException("Người submit không được tự từ chối resolution của chính mình.");
        resolution.RejectedBy = currentUser.UserId; resolution.RejectedAtUtc = DateTime.UtcNow;
        discrepancy.Status = ReceivingDiscrepancyStatus.Pending; receipt.Status = ReceiptStatus.DiscrepancyPending;
        context.AuditLogs.Add(Audit(receipt, "ImportReceipt.DiscrepancyRejected", $"DiscrepancyId: {discrepancy.Id}; Version: {resolution.Version}"));
        await Save(token); return Map(discrepancy);
    }

    public async Task<ReceivingDiscrepancyDto> RecountAsync(int receiptId, int discrepancyId, RecountReceivingDto dto, CancellationToken token)
    {
        var receipt = await LoadReceipt(receiptId, token); var discrepancy = Find(receipt, discrepancyId); ApplyRowVersion(discrepancy, dto.RowVersion);
        if (discrepancy.Status is not (ReceivingDiscrepancyStatus.Pending or ReceivingDiscrepancyStatus.Rejected)) throw Conflict("Chỉ có thể recount discrepancy đang chờ xử lý.");
        var line = discrepancy.ImportReceiptDetail; var now = DateTime.UtcNow;
        var (unitId, unitCode, factor, conversionVersion, decimalPlaces) = await ResolveObservedUom(line, dto.ObservedUnitId, now, token);
        EnsureQuantity(dto.ObservedQuantity, decimalPlaces, "Số lượng recount"); var baseObserved = dto.ObservedQuantity * factor; EnsureQuantity(baseObserved, line.BaseUnitDecimalPlaces, "Số lượng recount Base UOM");
        if (dto.Items.Count > 0 && dto.Items.Sum(x => x.Quantity) != dto.ObservedQuantity) throw new BusinessRuleException("Tổng observation items phải bằng số lượng recount.");
        var previous = discrepancy.Observations.OrderByDescending(x => x.Version).First();
        var next = new ReceivingObservationVersion { ReceivingDiscrepancy = discrepancy, Version = previous.Version + 1, PreviousObservationVersion = previous, ObservedQuantity = dto.ObservedQuantity, BaseObservedQuantity = baseObserved, ObservedUnitId = unitId, ObservedUnitCodeSnapshot = unitCode, ConversionFactorSnapshot = factor, ConversionVersionSnapshot = conversionVersion, CreatedBy = currentUser.UserId, CreatedAtUtc = now, Items = dto.Items.Select(x => new ReceivingObservationItem { Quantity = x.Quantity, ScanReference = x.ScanReference?.Trim() }).ToList() };
        var voided = discrepancy.Observations.SelectMany(x => x.Items).Where(x => dto.VoidedObservationItemIds.Contains(x.Id)).ToList();
        if (voided.Count != dto.VoidedObservationItemIds.Distinct().Count()) throw new BusinessRuleException("Observation item cần void không thuộc discrepancy.");
        foreach (var item in voided) { item.IsVoided = true; item.VoidedBy = currentUser.UserId; item.VoidedAtUtc = now; item.SupersededByObservationVersion = next; }
        discrepancy.Observations.Add(next); discrepancy.Status = ReceivingDiscrepancyStatus.Pending; receipt.Status = ReceiptStatus.DiscrepancyPending; line.ObservedQuantity = dto.ObservedQuantity; line.BaseObservedQuantity = baseObserved;
        context.AuditLogs.Add(Audit(receipt, "ImportReceipt.DiscrepancyRecounted", $"DiscrepancyId: {discrepancy.Id}; ObservationVersion: {next.Version}"));
        await Save(token); return Map(discrepancy);
    }

    private async Task<ImportReceipt> LoadReceipt(int id, CancellationToken token)
    {
        var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(token);
        return await context.ImportReceipts.Where(x => x.Id == id && allowed.Contains(x.WarehouseId))
            .Include(x => x.Details).ThenInclude(x => x.Product).ThenInclude(x => x.Uoms).ThenInclude(x => x.Unit)
            .Include(x => x.Discrepancies).ThenInclude(x => x.ImportReceiptDetail)
            .Include(x => x.Discrepancies).ThenInclude(x => x.Observations).ThenInclude(x => x.Items)
            .Include(x => x.Discrepancies).ThenInclude(x => x.Resolutions)
            .SingleOrDefaultAsync(token) ?? throw new NotFoundException("Không tìm thấy phiếu nhập.");
    }

    private static ReceivingDiscrepancy Find(ImportReceipt receipt, int id) => receipt.Discrepancies.SingleOrDefault(x => x.Id == id) ?? throw new NotFoundException("Không tìm thấy discrepancy.");
    private async Task<ReceivingReasonCode> ActiveReason(string code, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        return await context.ReceivingReasonCodes.Where(x => x.Code == code && x.IsActive && x.EffectiveFromUtc <= now && (x.EffectiveToUtc == null || x.EffectiveToUtc > now)).OrderByDescending(x => x.Version).FirstOrDefaultAsync(token)
            ?? throw new BusinessRuleException("Reason Code không active/effective.");
    }
    private async Task<ReceivingTolerancePolicy?> SelectPolicy(ImportReceipt receipt, ImportReceiptDetail line, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        return await context.ReceivingTolerancePolicies.Where(x => x.IsActive && x.EffectiveFromUtc <= now && (x.EffectiveToUtc == null || x.EffectiveToUtc > now)
            && (x.ProductId == null || x.ProductId == line.ProductId) && (x.SupplierId == null || x.SupplierId == receipt.SupplierId) && (x.WarehouseId == null || x.WarehouseId == receipt.WarehouseId))
            .OrderByDescending(x => x.ProductId.HasValue && x.SupplierId.HasValue && x.WarehouseId.HasValue)
            .ThenByDescending(x => x.ProductId.HasValue && x.SupplierId.HasValue)
            .ThenByDescending(x => x.ProductId.HasValue)
            .ThenByDescending(x => x.WarehouseId.HasValue)
            .ThenByDescending(x => x.Version).FirstOrDefaultAsync(token);
    }
    private async Task<(int Id,string Code,decimal Factor,int Version,int DecimalPlaces)> ResolveObservedUom(ImportReceiptDetail line, int? observedUnitId, DateTime now, CancellationToken token)
    {
        var id = observedUnitId.GetValueOrDefault(line.OperationUnitId);
        if (id == line.OperationUnitId) return (id, line.OperationUnitCodeSnapshot, line.ConversionFactor, line.ConversionVersion, line.OperationUnitDecimalPlaces);
        var conversion = await context.ProductUoms.Include(x => x.Unit).Where(x => x.ProductId == line.ProductId && x.UnitId == id && x.IsActive && x.EffectiveFromUtc <= now).OrderByDescending(x => x.Version).FirstOrDefaultAsync(token);
        if (conversion is null || conversion.ConversionFactor <= 0) throw new BusinessRuleException("UOM quan sát không có conversion active/effective; hãy sửa master data hoặc recount bằng UOM hỗ trợ.");
        return (conversion.UnitId, conversion.Unit.Code, conversion.ConversionFactor, conversion.Version, conversion.Unit.DecimalPlaces);
    }
    private static ReceivingResolutionAction ParseAction(string value) => value.Trim().ToUpperInvariant() switch { "ACCEPT_OBSERVED" => ReceivingResolutionAction.AcceptObserved, "ACCEPT_EXPECTED_REJECT_EXCESS" => ReceivingResolutionAction.AcceptExpectedRejectExcess, "REJECT_AT_DOOR" => ReceivingResolutionAction.RejectAtDoor, "REJECT_WRONG_PRODUCT" => ReceivingResolutionAction.RejectWrongProduct, "ROUTE_TO_QC" => ReceivingResolutionAction.RouteToQc, _ => throw new BusinessRuleException("Resolution action không hợp lệ.") };
    private static string PolicySource(ReceivingTolerancePolicy? p) => p is null ? "SystemDefault" : p.ProductId.HasValue && p.SupplierId.HasValue && p.WarehouseId.HasValue ? "ProductSupplierWarehouse" : p.ProductId.HasValue && p.SupplierId.HasValue ? "ProductSupplier" : p.ProductId.HasValue ? "Product" : p.WarehouseId.HasValue ? "WarehouseDefault" : "SystemDefault";
    private static decimal NormalizeToOperationUom(ImportReceiptDetail line, decimal baseQuantity) => baseQuantity / line.ConversionFactor;
    private static void ApplyFinal(ImportReceiptDetail line, decimal final, decimal baseFinal, decimal door, decimal baseDoor, ReceivingResolutionVersion? resolution)
    {
        line.DoorRejectedQuantity = door; line.BaseDoorRejectedQuantity = baseDoor; line.FinalReceivedQuantity = final; line.BaseFinalReceivedQuantity = baseFinal; line.FinalResolutionVersion = resolution;
        line.ReceivedQuantity = final; line.BaseReceivedQuantity = baseFinal;
        if (!line.RequiresQc) { line.AcceptedQuantity = final; line.BaseAcceptedQuantity = baseFinal; }
        else { line.AcceptedQuantity = line.DamagedQuantity = line.RejectedQuantity = 0; line.BaseAcceptedQuantity = line.BaseDamagedQuantity = line.BaseRejectedQuantity = 0; }
    }
    private static void Resolve(ImportReceipt receipt, ReceivingDiscrepancy d, ReceivingResolutionVersion r, int userId, DateTime now)
    {
        r.ResolvedBy = userId; r.ResolvedAtUtc = now; d.Status = ReceivingDiscrepancyStatus.Resolved;
        ApplyFinal(d.ImportReceiptDetail, r.FinalReceivedQuantity, r.BaseFinalReceivedQuantity, r.DoorRejectedQuantity, r.BaseDoorRejectedQuantity, r);
        if (r.Action == ReceivingResolutionAction.RouteToQc) { d.ImportReceiptDetail.RequiresQc = true; d.ImportReceiptDetail.QcState = ReceiptLineQcState.QcPending; d.ImportReceiptDetail.QcPolicySourceSnapshot = "DiscrepancyRouteToQc"; }
        receipt.Status = receipt.Discrepancies.All(x => x.Status == ReceivingDiscrepancyStatus.Resolved) ? RouteAfterResolution(receipt) : ReceiptStatus.DiscrepancyPending;
    }
    private static ReceiptStatus RouteAfterResolution(ImportReceipt receipt) => receipt.Details.Any(x => x.RequiresQc) ? ReceiptStatus.QcPending : ReceiptStatus.Received;
    private void ApplyRowVersion(ReceivingDiscrepancy entity, byte[] rowVersion)
    {
        if (rowVersion.Length == 0) throw new BusinessRuleException("RowVersion là bắt buộc.");
        context.Entry(entity).Property(x => x.RowVersion).OriginalValue = rowVersion;
    }
    private static void EnsureQuantity(decimal value, int places, string name) { if (value < 0 || decimal.Round(value, places) != value) throw new BusinessRuleException($"{name} không hợp lệ hoặc vượt precision {places}."); }
    private AuditLog Audit(ImportReceipt receipt, string action, string values) => new() { UserId = currentUser.UserId, Action = action, EntityName = "ImportReceipt", EntityId = receipt.Id, WarehouseId = receipt.WarehouseId, NewValues = values, Result = "Success", Severity = "Information", Timestamp = DateTime.UtcNow };
    private async Task Save(CancellationToken token)
    {
        try { await context.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException ex) { throw new ConcurrencyException("Discrepancy đã thay đổi; vui lòng tải lại.", ex); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        { throw new ConcurrencyException("Discrepancy đã được ghi nhận đồng thời; vui lòng tải lại.", ex); }
    }
    private static ConcurrencyException Conflict(string message) => new(message);

    private ReceivingDiscrepancyDto Map(ReceivingDiscrepancy x)
    {
        var line = x.ImportReceiptDetail; var latest = x.Observations.OrderByDescending(o => o.Version).FirstOrDefault(); var resolution = x.Resolutions.OrderByDescending(r => r.Version).FirstOrDefault();
        var viewer = string.Equals(currentUser.Role, "Viewer", StringComparison.OrdinalIgnoreCase);
        return new ReceivingDiscrepancyDto { Id = x.Id, ReceiptLineId = line.Id, Status = x.Status.ToString(), ExpectedQuantity = line.ExpectedQuantity, BaseExpectedQuantity = line.BaseExpectedQuantity, ObservedQuantity = latest?.ObservedQuantity ?? 0, BaseObservedQuantity = latest?.BaseObservedQuantity ?? 0, NormalizedObservedQuantity = latest is null ? 0 : NormalizeToOperationUom(line, latest.BaseObservedQuantity), DoorRejectedQuantity = resolution?.DoorRejectedQuantity ?? line.DoorRejectedQuantity, FinalReceivedQuantity = resolution?.FinalReceivedQuantity ?? line.FinalReceivedQuantity, OperationUnitId = line.OperationUnitId, ObservedUnitId = latest?.ObservedUnitId ?? line.OperationUnitId, ObservedUnitCode = latest?.ObservedUnitCodeSnapshot ?? line.OperationUnitCodeSnapshot, OperationUnitCode = line.OperationUnitCodeSnapshot, BaseUnitCode = line.BaseUnitCodeSnapshot, ConversionFactor = latest?.ConversionFactorSnapshot ?? line.ConversionFactor, ConversionVersion = latest?.ConversionVersionSnapshot ?? line.ConversionVersion, RowVersion = x.RowVersion,
            Observations = x.Observations.OrderBy(o => o.Version).Select(o => new ReceivingObservationDto { Id = o.Id, Version = o.Version, ObservedQuantity = o.ObservedQuantity, BaseObservedQuantity = o.BaseObservedQuantity, CreatedAtUtc = o.CreatedAtUtc }).ToList(),
            Resolutions = x.Resolutions.OrderBy(r => r.Version).Select(r => new ReceivingResolutionDto { Id = r.Id, Version = r.Version, Action = r.Action.ToString(), DoorRejectedQuantity = r.DoorRejectedQuantity, FinalReceivedQuantity = r.FinalReceivedQuantity, ReasonCode = r.ReasonCodeSnapshot, ReasonName = r.ReasonNameSnapshot, ReasonVersion = r.ReasonVersionSnapshot, RequiresApproval = r.RequiresApproval, ResponsibleParty = viewer ? string.Empty : r.ResponsibleParty.ToString(), SupplierClaimRequired = !viewer && r.SupplierClaimRequired, Note = viewer ? null : r.Note, EvidenceReference = viewer ? null : r.EvidenceReference, SubmittedAtUtc = r.SubmittedAtUtc, ApprovedAtUtc = r.ApprovedAtUtc, RejectedAtUtc = r.RejectedAtUtc }).ToList() };
    }
}
