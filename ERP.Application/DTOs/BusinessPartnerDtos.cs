namespace ERP.Application.DTOs;

public sealed class BusinessPartnerDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsSupplier { get; set; }
    public bool IsCustomer { get; set; }
    public bool IsActive { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SaveBusinessPartnerDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsSupplier { get; set; }
    public bool IsCustomer { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class SetReceiptPartnerDto { public int? PartnerId { get; set; } public string? RowVersion { get; set; } }
