using System.ComponentModel.DataAnnotations;

namespace ERP.Application.DTOs
{
    public class CreateProductDto
    {
        [Required(ErrorMessage = "Mã sản phẩm là bắt buộc")]
        [StringLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "Tên sản phẩm là bắt buộc")]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public int UnitId { get; set; }
    }
}
