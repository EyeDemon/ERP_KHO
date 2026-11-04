using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public static class VnAdministrativeSeedHelper
{
    public static async Task EnsureVnAdministrativeSeededAsync(ErpKhoDbContext dbContext, CancellationToken ct)
    {
        if (await dbContext.VnAdministrativeUnits.AnyAsync(ct))
            return;

        var seedUnits = new[]
        {
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "769", DistrictName = "Thành phố Thủ Đức", WardCode = "27100", WardName = "Phường An Khánh", Keywords = "an khanh, thu duc, hcm, tphcm, q2" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "769", DistrictName = "Thành phố Thủ Đức", WardCode = "27088", WardName = "Phường Thảo Điền", Keywords = "thao dien, thu duc, q2, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "769", DistrictName = "Thành phố Thủ Đức", WardCode = "26830", WardName = "Phường Hiệp Bình Chánh", Keywords = "hiep binh chanh, thu duc, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "760", DistrictName = "Quận 1", WardCode = "26734", WardName = "Phường Bến Nghé", Keywords = "ben nghe, quan 1, q.1, q1, hcm, tphcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "760", DistrictName = "Quận 1", WardCode = "26740", WardName = "Phường Bến Thành", Keywords = "ben thanh, quan 1, q1, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "760", DistrictName = "Quận 1", WardCode = "26743", WardName = "Phường Nguyễn Thái Bình", Keywords = "nguyen thai binh, quan 1, q1, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "766", DistrictName = "Quận Tân Bình", WardCode = "26974", WardName = "Phường 2", Keywords = "phuong 2, tan binh, san bay, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "766", DistrictName = "Quận Tân Bình", WardCode = "26983", WardName = "Phường 4", Keywords = "phuong 4, tan binh, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "765", DistrictName = "Quận Bình Thạnh", WardCode = "26944", WardName = "Phường 25", Keywords = "phuong 25, binh thanh, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "770", DistrictName = "Quận 3", WardCode = "27127", WardName = "Phường Võ Thị Sáu", Keywords = "vo thi sau, quan 3, q3, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "778", DistrictName = "Quận 7", WardCode = "27487", WardName = "Phường Tân Phong", Keywords = "tan phong, quan 7, q7, phu my hung, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "764", DistrictName = "Quận Gò Vấp", WardCode = "26881", WardName = "Phường 10", Keywords = "phuong 10, go vap, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "01", ProvinceName = "Thành phố Hà Nội", DistrictCode = "005", DistrictName = "Quận Cầu Giấy", WardCode = "00166", WardName = "Phường Dịch Vọng Hậu", Keywords = "dich vong hau, cau giay, ha noi, hn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "01", ProvinceName = "Thành phố Hà Nội", DistrictCode = "005", DistrictName = "Quận Cầu Giấy", WardCode = "00169", WardName = "Phường Mai Dịch", Keywords = "mai dich, cau giay, ha noi, hn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "01", ProvinceName = "Thành phố Hà Nội", DistrictCode = "002", DistrictName = "Quận Hoàn Kiếm", WardCode = "00058", WardName = "Phường Hàng Bài", Keywords = "hang bai, hoan kiem, ha noi, hn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "01", ProvinceName = "Thành phố Hà Nội", DistrictCode = "002", DistrictName = "Quận Hoàn Kiếm", WardCode = "00070", WardName = "Phường Tràng Tiền", Keywords = "trang tien, hoan kiem, ha noi, hn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "01", ProvinceName = "Thành phố Hà Nội", DistrictCode = "001", DistrictName = "Quận Ba Đình", WardCode = "00004", WardName = "Phường Điện Biên", Keywords = "dien bien, ba dinh, ha noi, hn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "01", ProvinceName = "Thành phố Hà Nội", DistrictCode = "006", DistrictName = "Quận Đống Đa", WardCode = "00208", WardName = "Phường Láng Hạ", Keywords = "lang ha, dong da, ha noi, hn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "01", ProvinceName = "Thành phố Hà Nội", DistrictCode = "009", DistrictName = "Quận Thanh Xuân", WardCode = "00343", WardName = "Phường Nhân Chính", Keywords = "nhan chinh, thanh xuan, ha noi, hn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "01", ProvinceName = "Thành phố Hà Nội", DistrictCode = "019", DistrictName = "Quận Nam Từ Liêm", WardCode = "00622", WardName = "Phường Mỹ Đình 1", Keywords = "my dinh 1, nam tu liem, ha noi, hn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "48", ProvinceName = "Thành phố Đà Nẵng", DistrictCode = "492", DistrictName = "Quận Hải Châu", WardCode = "20242", WardName = "Phường Thạch Thang", Keywords = "thach thang, hai chau, da nang, dn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "48", ProvinceName = "Thành phố Đà Nẵng", DistrictCode = "492", DistrictName = "Quận Hải Châu", WardCode = "20245", WardName = "Phường Hải Châu I", Keywords = "hai chau 1, hai chau, da nang, dn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "48", ProvinceName = "Thành phố Đà Nẵng", DistrictCode = "493", DistrictName = "Quận Sơn Trà", WardCode = "20275", WardName = "Phường Phước Mỹ", Keywords = "phuoc my, son tra, da nang, dn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "31", ProvinceName = "Thành phố Hải Phòng", DistrictCode = "304", DistrictName = "Quận Ngô Quyền", WardCode = "11329", WardName = "Phường Đông Khê", Keywords = "dong khe, ngo quyen, hai phong, hp" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "31", ProvinceName = "Thành phố Hải Phòng", DistrictCode = "305", DistrictName = "Quận Lê Chân", WardCode = "11380", WardName = "Phường Kênh Dương", Keywords = "kenh duong, le chan, hai phong, hp" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "92", ProvinceName = "Thành phố Cần Thơ", DistrictCode = "916", DistrictName = "Quận Ninh Kiều", WardCode = "31144", WardName = "Phường Tân An", Keywords = "tan an, ninh kieu, can tho, ct" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "92", ProvinceName = "Thành phố Cần Thơ", DistrictCode = "916", DistrictName = "Quận Ninh Kiều", WardCode = "31141", WardName = "Phường Xuân Khánh", Keywords = "xuan khanh, ninh kieu, can tho, ct" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "74", ProvinceName = "Tỉnh Bình Dương", DistrictCode = "724", DistrictName = "Thành phố Dĩ An", WardCode = "25942", WardName = "Phường Đông Hòa", Keywords = "dong hoa, di an, binh duong, bd" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "74", ProvinceName = "Tỉnh Bình Dương", DistrictCode = "725", DistrictName = "Thành phố Thuận An", WardCode = "25960", WardName = "Phường Lái Thiêu", Keywords = "lai thieu, thuan an, binh duong, bd" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "75", ProvinceName = "Tỉnh Đồng Nai", DistrictCode = "731", DistrictName = "Thành phố Biên Hòa", WardCode = "26020", WardName = "Phường Tân Hiệp", Keywords = "tan hiep, bien hoa, dong nai" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "56", ProvinceName = "Tỉnh Khánh Hòa", DistrictCode = "568", DistrictName = "Thành phố Nha Trang", WardCode = "22360", WardName = "Phường Lộc Thọ", Keywords = "loc tho, nha trang, khanh hoa" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "46", ProvinceName = "Tỉnh Thừa Thiên Huế", DistrictCode = "474", DistrictName = "Thành phố Huế", WardCode = "19777", WardName = "Phường Vĩnh Ninh", Keywords = "vinh ninh, hue, thua thien hue" }
        };

        dbContext.VnAdministrativeUnits.AddRange(seedUnits);
        await dbContext.SaveChangesAsync(ct);

        var carriers = new[] { "GHN", "GHTK", "VTPOST", "SPX" };
        foreach (var u in seedUnits)
        {
            foreach (var carrier in carriers)
            {
                dbContext.CarrierLocationMappings.Add(new ERP.Domain.Entities.CarrierLocationMapping
                {
                    AdministrativeUnitId = u.Id,
                    CarrierCode = carrier,
                    CarrierProvinceId = $"{carrier}-P-{u.ProvinceCode}",
                    CarrierDistrictId = $"{carrier}-D-{u.DistrictCode}",
                    CarrierWardCode = $"{carrier}-W-{u.WardCode}",
                    SortingHubCode = $"HUB-{carrier}-{u.ProvinceCode}-{u.DistrictCode}",
                    RouteCode = $"RT-{carrier}-{u.DistrictCode}",
                    IsActive = true
                });
            }
        }
        await dbContext.SaveChangesAsync(ct);
    }
}
