using System.ComponentModel.DataAnnotations;

namespace ERP.Application.DTOs
{
    public class CreateUnitDto
    {
        [Required(ErrorMessage = "Mã đơn vị tính là bắt buộc")]
        [StringLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "Tên đơn vị tính là bắt buộc")]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

    }
}
