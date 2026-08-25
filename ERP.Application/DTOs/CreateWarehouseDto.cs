using System.ComponentModel.DataAnnotations;

namespace ERP.Application.DTOs
{
    public class CreateWarehouseDto
    {
        [Required(ErrorMessage = "Mã kho là bắt buộc")]
        [StringLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "Tên kho là bắt buộc")]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        public string? Address { get; set; }
    }
}
