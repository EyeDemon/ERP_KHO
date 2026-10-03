using System.ComponentModel.DataAnnotations;

namespace ERP.Application.DTOs;

public sealed class ProductCategoryDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class CreateProductCategoryDto
{
    [Required, StringLength(50)] public string Code { get; set; } = string.Empty;
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
}

public sealed class UpdateProductCategoryDto
{
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class SetProductCategoryDto
{
    public int? CategoryId { get; set; }
}
