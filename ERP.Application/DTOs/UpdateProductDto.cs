using System.ComponentModel.DataAnnotations;

namespace ERP.Application.DTOs
{
    public class UpdateProductDto
    {
        [Required(ErrorMessage = "Tên sản phẩm là bắt buộc")]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public int UnitId { get; set; }

        public bool IsActive { get; set; }
    }
}
