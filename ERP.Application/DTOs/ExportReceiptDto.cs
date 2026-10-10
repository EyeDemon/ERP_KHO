using System.Text.Json.Serialization;
using ERP.Domain.Enums;

namespace ERP.Application.DTOs
{
    public class ExportReceiptDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public int WarehouseId { get; set; }
        public string? WarehouseName { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Note { get; set; }
        public int CreatedBy { get; set; }
        public string? CreatedByName { get; set; }
        public int? ApprovedBy { get; set; }
        public string? ApprovedByName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? DispatchMode { get; set; }
        public int? DispatchedBy { get; set; }
        public string? DispatchedByName { get; set; }
        public DateTime? DispatchedAt { get; set; }
        public int? CustomerId { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerName { get; set; }
        public bool AllowPerReceiptDispatchMode { get; set; }
        public bool AllowWarehouseStaffDirectDispatch { get; set; }
        public bool WriteEnabled { get; set; }
        public List<ExportReceiptDetailDto> Details { get; set; } = new();
        public string ReservationStatus { get; set; } = "NotCreated";
    }

    public class ExportReceiptDetailDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string? ProductCode { get; set; }
        public string? ProductName { get; set; }
        public string? UnitName { get; set; }
        public decimal Quantity { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? UnitPrice { get; set; }
        public string? Note { get; set; }
        public int? BaseUomIdSnapshot { get; set; }
        public string? BaseUomCodeSnapshot { get; set; }
        public string? BaseUomNameSnapshot { get; set; }
        public int? BaseUomDecimalPlacesSnapshot { get; set; }
        public decimal? AvailableQuantity { get; set; }
    }
    public class CreateExportReceiptDto
    {
        public string Code { get; set; } = string.Empty;
        public int WarehouseId { get; set; }
        public string? Note { get; set; }
        public int? CustomerId { get; set; }
        public List<CreateExportReceiptDetailDto> Details { get; set; } = new();
    }

    public class CreateExportReceiptDetailDto
    {
        public int ProductId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public string? Note { get; set; }
    }
}
