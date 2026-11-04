using ERP.Domain.Common;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;

namespace ERP.Domain.Entities;

public class ImportReceipt : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public string Code { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    
    [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
    public ReceiptStatus Status { get; set; } = ReceiptStatus.Draft;
    
    public string? Note { get; set; }
    public int CreatedBy { get; set; }
    public int? ApprovedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public int? SupplierId { get; set; }
    public int? PurchaseOrderId { get; set; }
    public int? AsnId { get; set; }
    public string? SupplierCodeSnapshot { get; set; }
    public string? SupplierNameSnapshot { get; set; }
    public byte[] RowVersion { get; set; } = [0, 0, 0, 0, 0, 0, 0, 1];

    public void TransitionTo(ReceiptStatus targetStatus, int? actorUserId = null, DateTime? timestampUtc = null)
    {
        var isValid = (Status, targetStatus) switch
        {
            (ReceiptStatus.Draft, ReceiptStatus.Received) => true,
            (ReceiptStatus.Draft, ReceiptStatus.QcPending) => true,
            (ReceiptStatus.Draft, ReceiptStatus.DiscrepancyPending) => true,
            (ReceiptStatus.Draft, ReceiptStatus.DiscrepancySubmitted) => true,
            (ReceiptStatus.Draft, ReceiptStatus.ReadyToPost) => true,
            (ReceiptStatus.Draft, ReceiptStatus.Approved) => true,
            (ReceiptStatus.Draft, ReceiptStatus.Cancelled) => true,
            (ReceiptStatus.Received, ReceiptStatus.QcPending) => true,
            (ReceiptStatus.Received, ReceiptStatus.QcCompleted) => true,
            (ReceiptStatus.Received, ReceiptStatus.DiscrepancyPending) => true,
            (ReceiptStatus.Received, ReceiptStatus.DiscrepancySubmitted) => true,
            (ReceiptStatus.Received, ReceiptStatus.ReadyToPost) => true,
            (ReceiptStatus.Received, ReceiptStatus.Approved) => true,
            (ReceiptStatus.Received, ReceiptStatus.Posted) => true,
            (ReceiptStatus.Received, ReceiptStatus.Cancelled) => true,
            (ReceiptStatus.QcPending, ReceiptStatus.QcCompleted) => true,
            (ReceiptStatus.QcPending, ReceiptStatus.ReadyToPost) => true,
            (ReceiptStatus.QcPending, ReceiptStatus.DiscrepancyPending) => true,
            (ReceiptStatus.QcPending, ReceiptStatus.Cancelled) => true,
            (ReceiptStatus.QcCompleted, ReceiptStatus.ReadyToPost) => true,
            (ReceiptStatus.QcCompleted, ReceiptStatus.Approved) => true,
            (ReceiptStatus.QcCompleted, ReceiptStatus.Posted) => true,
            (ReceiptStatus.QcCompleted, ReceiptStatus.Cancelled) => true,
            (ReceiptStatus.DiscrepancyPending, ReceiptStatus.DiscrepancySubmitted) => true,
            (ReceiptStatus.DiscrepancyPending, ReceiptStatus.DiscrepancyPendingApproval) => true,
            (ReceiptStatus.DiscrepancyPending, ReceiptStatus.DiscrepancyResolved) => true,
            (ReceiptStatus.DiscrepancyPending, ReceiptStatus.DiscrepancyRejected) => true,
            (ReceiptStatus.DiscrepancyPending, ReceiptStatus.ReadyToPost) => true,
            (ReceiptStatus.DiscrepancyPending, ReceiptStatus.Received) => true,
            (ReceiptStatus.DiscrepancyPending, ReceiptStatus.Cancelled) => true,
            (ReceiptStatus.DiscrepancySubmitted, ReceiptStatus.DiscrepancyPendingApproval) => true,
            (ReceiptStatus.DiscrepancySubmitted, ReceiptStatus.DiscrepancyResolved) => true,
            (ReceiptStatus.DiscrepancySubmitted, ReceiptStatus.DiscrepancyRejected) => true,
            (ReceiptStatus.DiscrepancySubmitted, ReceiptStatus.Cancelled) => true,
            (ReceiptStatus.DiscrepancyPendingApproval, ReceiptStatus.DiscrepancyResolved) => true,
            (ReceiptStatus.DiscrepancyPendingApproval, ReceiptStatus.DiscrepancyRejected) => true,
            (ReceiptStatus.DiscrepancyPendingApproval, ReceiptStatus.Cancelled) => true,
            (ReceiptStatus.DiscrepancyResolved, ReceiptStatus.QcPending) => true,
            (ReceiptStatus.DiscrepancyResolved, ReceiptStatus.QcCompleted) => true,
            (ReceiptStatus.DiscrepancyResolved, ReceiptStatus.ReadyToPost) => true,
            (ReceiptStatus.DiscrepancyResolved, ReceiptStatus.Approved) => true,
            (ReceiptStatus.DiscrepancyResolved, ReceiptStatus.Posted) => true,
            (ReceiptStatus.DiscrepancyResolved, ReceiptStatus.Cancelled) => true,
            (ReceiptStatus.ReadyToPost, ReceiptStatus.Approved) => true,
            (ReceiptStatus.ReadyToPost, ReceiptStatus.Posted) => true,
            (ReceiptStatus.ReadyToPost, ReceiptStatus.Cancelled) => true,
            (ReceiptStatus.Approved, ReceiptStatus.Posted) => true,
            _ => false
        };

        if (!isValid)
        {
            throw new ConflictException(
                $"Chuyển trạng thái phiếu nhập kho từ '{Status}' sang '{targetStatus}' không hợp lệ.",
                "DOC_INVALID_STATE_TRANSITION");
        }

        Status = targetStatus;
        if (targetStatus is ReceiptStatus.Approved or ReceiptStatus.Posted)
        {
            if (actorUserId.HasValue)
            {
                ApprovedBy = actorUserId.Value;
            }
            ApprovedAt = timestampUtc ?? DateTime.UtcNow;
        }
    }

    // Navigation
    public Warehouse Warehouse { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public User? ApprovedByUser { get; set; }
    public BusinessPartner? Supplier { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public Asn? Asn { get; set; }
    public ICollection<ImportReceiptDetail> Details { get; set; } = new List<ImportReceiptDetail>();
    public ICollection<ReceivingDiscrepancy> Discrepancies { get; set; } = new List<ReceivingDiscrepancy>();
}
