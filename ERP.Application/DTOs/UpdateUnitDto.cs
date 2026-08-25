using System.ComponentModel.DataAnnotations;

namespace ERP.Application.DTOs
{
    public class UpdateUnitDto
    {
        [Required(ErrorMessage = "Tên đơn vị tính là bắt buộc")]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }
}
