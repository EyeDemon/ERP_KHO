using System;
using ERP.Domain.Enums;

namespace ERP.Application.DTOs
{
    public class StocktakeSummaryDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public int WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public ReceiptStatus Status { get; set; }
        public string? Note { get; set; }
        public int CreatedBy { get; set; }
        public string CreatedByName { get; set; } = string.Empty;
        public int? ApprovedBy { get; set; }
        public string? ApprovedByName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public int DetailCount { get; set; }
    }
}
