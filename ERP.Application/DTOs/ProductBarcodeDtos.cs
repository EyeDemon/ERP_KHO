using System.ComponentModel.DataAnnotations;

namespace ERP.Application.DTOs;

public sealed class ProductBarcodeDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Value { get; set; } = string.Empty;
}

public sealed class CreateProductBarcodeDto
{
    [Required, StringLength(64, MinimumLength = 1)]
    [RegularExpression(@"^[A-Za-z0-9._-]+$", ErrorMessage = "Barcode chỉ được chứa chữ cái ASCII, chữ số, dấu chấm, gạch dưới hoặc gạch ngang")]
    public string Value { get; set; } = string.Empty;
}
