using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class VnAdministrativeUnit : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public string ProvinceCode { get; set; } = string.Empty;
    public string ProvinceName { get; set; } = string.Empty;
    public string DistrictCode { get; set; } = string.Empty;
    public string DistrictName { get; set; } = string.Empty;
    public string WardCode { get; set; } = string.Empty;
    public string WardName { get; set; } = string.Empty;
    public string Keywords { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<CarrierLocationMapping> CarrierMappings { get; set; } = new List<CarrierLocationMapping>();
}

public class CarrierLocationMapping : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int AdministrativeUnitId { get; set; }
    public string CarrierCode { get; set; } = string.Empty; // GHN | GHTK | VIETTELPOST | JT_EXPRESS | SPX
    public string CarrierProvinceId { get; set; } = string.Empty;
    public string CarrierDistrictId { get; set; } = string.Empty;
    public string CarrierWardCode { get; set; } = string.Empty;
    public string SortingHubCode { get; set; } = string.Empty;
    public string RouteCode { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public VnAdministrativeUnit? AdministrativeUnit { get; set; }
}
