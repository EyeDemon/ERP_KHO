using System.ComponentModel.DataAnnotations;

namespace ERP.Application.DTOs
{
    public class UpdateProductDto
    {
        [Required(ErrorMessage = "Tên sản phẩm là bắt buộc")]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }
        public bool UpdateStorageProfile { get; set; }
        public string? StorageClass { get; set; }
        public decimal? UnitWeightKg { get; set; }
        public decimal? UnitVolumeM3 { get; set; }
        public decimal? UnitPalletEquivalent { get; set; }

        public int UnitId { get; set; }

        public bool IsActive { get; set; }
    }
}
