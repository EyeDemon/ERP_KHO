using ERP.Domain.Common;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;

namespace ERP.Domain.Entities;

public class ExportReceipt : ITenantEntity
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
    public ExportDispatchMode? DispatchMode { get; set; }
    public int? DispatchedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public int? CustomerId { get; set; }
    public int? SalesOrderId { get; set; }
    public int? ShipmentId { get; set; }
    public string? CustomerCodeSnapshot { get; set; }
    public string? CustomerNameSnapshot { get; set; }
    public string? MarketplaceOrderId { get; set; }
    public string? CarrierCode { get; set; }
    public string? TrackingNumber { get; set; }
    public string? AwbLabelUrl { get; set; }
    public int AwbPrintCount { get; set; }
    public DateTime? AwbFirstPrintedAtUtc { get; set; }
    public DateTime? MarketplaceShipByUtc { get; set; }
    public string? EInvoiceStatus { get; set; } = "PENDING";
    public string? EInvoiceProvider { get; set; }
    public string? EInvoiceNumber { get; set; }
    public string? BuyerTaxCode { get; set; }
    public byte[] RowVersion { get; set; } = [0, 0, 0, 0, 0, 0, 0, 1];

    public void TransitionTo(ReceiptStatus targetStatus, int? actorUserId = null, DateTime? timestampUtc = null)
    {
        var isValid = (Status, targetStatus) switch
        {
            (ReceiptStatus.Draft, ReceiptStatus.Approved) => true,
            (ReceiptStatus.Draft, ReceiptStatus.Cancelled) => true,
            (ReceiptStatus.Approved, ReceiptStatus.Dispatched) => true,
            (ReceiptStatus.Approved, ReceiptStatus.Posted) => true,
            (ReceiptStatus.Approved, ReceiptStatus.CancelPendingPutback) => true,
            (ReceiptStatus.Approved, ReceiptStatus.Cancelled) => true,
            (ReceiptStatus.CancelPendingPutback, ReceiptStatus.Cancelled) => true,
            _ => false
        };

        if (!isValid)
        {
            throw new ConflictException(
                $"Chuyển trạng thái phiếu xuất kho từ '{Status}' sang '{targetStatus}' không hợp lệ.",
                "DOC_INVALID_STATE_TRANSITION");
        }

        Status = targetStatus;
        var now = timestampUtc ?? DateTime.UtcNow;
        if (targetStatus == ReceiptStatus.Approved)
        {
            if (actorUserId.HasValue)
            {
                ApprovedBy = actorUserId.Value;
            }
            ApprovedAt = now;
        }
        else if (targetStatus is ReceiptStatus.Dispatched or ReceiptStatus.Posted)
        {
            if (actorUserId.HasValue)
            {
                DispatchedBy = actorUserId.Value;
            }
            DispatchedAt = now;
        }
    }

    // Navigation
    public Warehouse Warehouse { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public User? ApprovedByUser { get; set; }
    public User? DispatchedByUser { get; set; }
    public BusinessPartner? Customer { get; set; }
    public SalesOrder? SalesOrder { get; set; }
    public Shipment? Shipment { get; set; }
    public ICollection<ExportReceiptDetail> Details { get; set; } = new List<ExportReceiptDetail>();
}
