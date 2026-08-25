using System.ComponentModel.DataAnnotations;

namespace ERP.Application.DTOs
{
    public class UpdateWarehouseDto
    {
        [Required(ErrorMessage = "Tên kho là bắt buộc")]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        public string? Address { get; set; }

        public bool IsActive { get; set; }
    }
}
