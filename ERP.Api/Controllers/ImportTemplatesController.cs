using ClosedXML.Excel;
using ERP.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/v1/import-templates")]
[Route("api/import-templates")]
[Route("api/v1/data-imports/templates")]
[Route("api/data-imports/templates")]
[Authorize]
public sealed class ImportTemplatesController : ControllerBase
{
    [HttpGet("{type}")]
    public IActionResult DownloadTemplate(string type)
    {
        var normalizedType = (type ?? "products").Trim().ToLowerInvariant();
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("DuLieuNhap");
        var guideSheet = workbook.Worksheets.Add("HuongDanCot");

        guideSheet.Cell(1, 1).Value = "Cột";
        guideSheet.Cell(1, 2).Value = "Bắt buộc";
        guideSheet.Cell(1, 3).Value = "Quy tắc & Định dạng dữ liệu";

        string fileName;
        switch (normalizedType)
        {
            case "opening-balance":
            case "opening-balances":
                fileName = "Mau_Nhap_Ton_Kho_Dau_Ky.xlsx";
                WriteHeaders(sheet, "ProductCode", "LocationCode", "Quantity", "UnitCost", "LotNumber", "ExpiryDate");
                WriteRow(sheet, 2, "SKU-DEMO-01", "A-01-01-01", 100m, 85000m, "LOT-2026-01", "2027-12-31");
                WriteRow(sheet, 3, "SKU-DEMO-02", "A-01-01-02", 50m, 120000m, "LOT-2026-02", "2028-06-30");
                WriteGuide(guideSheet,
                    ("ProductCode", "Có", "Mã SKU sản phẩm đã tồn tại trong danh mục"),
                    ("LocationCode", "Có", "Mã vị trí kệ lưu trữ khả dụng"),
                    ("Quantity", "Có", "Số lượng tồn đầu kỳ (> 0)"),
                    ("UnitCost", "Có", "Đơn giá vốn bình quân đầu kỳ VND (>= 0)"),
                    ("LotNumber", "Tùy chọn", "Số lô sản xuất (bắt buộc nếu hàng quản lý theo lô)"),
                    ("ExpiryDate", "Tùy chọn", "Hạn sử dụng định dạng YYYY-MM-DD"));
                break;

            case "marketplace-orders":
            case "orders":
                fileName = "Mau_Nhap_Don_Hang_San_TMDT.xlsx";
                WriteHeaders(sheet, "ChannelCode", "ExternalOrderId", "BuyerName", "BuyerPhone", "ShippingAddress", "SkuCode", "Quantity", "UnitPrice");
                WriteRow(sheet, 2, "SHOPEE", "SP-261022-001", "Nguyễn Văn An", "0909123456", "P. Bến Nghé, Quận 1, TP. Hồ Chí Minh", "SKU-DEMO-01", 2m, 150000m);
                WriteRow(sheet, 3, "TIKTOK", "TT-261022-002", "Trần Thị Bình", "0912345678", "P. Dịch Vọng Hậu, Quận Cầu Giấy, Hà Nội", "SKU-DEMO-02", 1m, 220000m);
                WriteGuide(guideSheet,
                    ("ChannelCode", "Có", "Mã sàn: SHOPEE, TIKTOK, LAZADA, ZALO, FACEBOOK"),
                    ("ExternalOrderId", "Có", "Mã đơn hàng trên sàn (chống trùng tự động)"),
                    ("BuyerName", "Có", "Họ tên người nhận hàng"),
                    ("BuyerPhone", "Có", "Số điện thoại người nhận"),
                    ("ShippingAddress", "Có", "Địa chỉ giao hàng 3 cấp (Phường/Xã, Quận/Huyện, Tỉnh/Thành)"),
                    ("SkuCode", "Có", "Mã SKU nội bộ hoặc SKU sàn đã ánh xạ"),
                    ("Quantity", "Có", "Số lượng đặt (> 0)"),
                    ("UnitPrice", "Có", "Đơn giá bán VND (>= 0)"));
                break;

            case "partners":
            case "business-partners":
                fileName = "Mau_Nhap_Doi_Tac_NCC_KhachHang.xlsx";
                WriteHeaders(sheet, "Code", "Name", "PartnerRole", "TaxCode", "Phone", "Email", "Address", "PaymentTermDays", "CreditLimit");
                WriteRow(sheet, 2, "SUP-VN-001", "Công ty TNHH Cung Ứng Á Châu", "Supplier", "0312345678", "02838123456", "contact@achau.vn", "Quận 1, TP. Hồ Chí Minh", 30, 500000000m);
                WriteRow(sheet, 3, "CUS-VN-001", "Chuỗi Siêu Thị Thực Phẩm Xanh", "Customer", "0108765432", "02439876543", "purchasing@thucphamxanh.vn", "Quận Cầu Giấy, Hà Nội", 15, 300000000m);
                WriteGuide(guideSheet,
                    ("Code", "Có", "Mã đối tác duy nhất"),
                    ("Name", "Có", "Tên pháp nhân hoặc cá nhân kinh doanh"),
                    ("PartnerRole", "Có", "Vai trò: Supplier, Customer, hoặc Both"),
                    ("TaxCode", "Tùy chọn", "Mã số thuế (10 hoặc 13 chữ số)"),
                    ("PaymentTermDays", "Tùy chọn", "Số ngày công nợ cho phép (0 - 365)"),
                    ("CreditLimit", "Tùy chọn", "Hạn mức tín dụng VND (>= 0)"));
                break;

            default:
                fileName = "Mau_Nhap_Danh_Muc_San_Pham.xlsx";
                WriteHeaders(sheet, "ProductCode", "ProductName", "CategoryCode", "BaseUomCode", "Barcode", "TrackingType", "MovingAverageCost", "SalePrice");
                WriteRow(sheet, 2, "SKU-NEW-001", "Nước Khoáng Thiên Nhiên Thùng 24 Chai", "CAT-BEV", "THÙNG", "8938501110012", "Lot", 85000m, 125000m);
                WriteRow(sheet, 3, "SKU-NEW-002", "Cà Phê Rang Xay Nguyên Chất 500g", "CAT-FNB", "GÓI", "8938501110029", "Lot", 95000m, 145000m);
                WriteGuide(guideSheet,
                    ("ProductCode", "Có", "Mã SKU duy nhất toàn hệ thống"),
                    ("ProductName", "Có", "Tên thương mại đầy đủ của sản phẩm"),
                    ("CategoryCode", "Có", "Mã nhóm ngành hàng"),
                    ("BaseUomCode", "Có", "Đơn vị tính cơ sở (THÙNG, CÁI, HỘP, GÓI...)"),
                    ("Barcode", "Tùy chọn", "Mã vạch EAN-13 / Code-128"),
                    ("TrackingType", "Có", "Kiểu quản lý: None, Lot, Serial"),
                    ("MovingAverageCost", "Tùy chọn", "Giá vốn bình quân khởi tạo VND"),
                    ("SalePrice", "Tùy chọn", "Giá bán niêm yết VND"));
                break;
        }

        sheet.Row(1).Style.Font.Bold = true;
        guideSheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();
        guideSheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private static void WriteHeaders(IXLWorksheet sheet, params string[] headers)
    {
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(1, i + 1).Value = headers[i];
    }

    private static void WriteRow(IXLWorksheet sheet, int row, params object[] values)
    {
        for (var i = 0; i < values.Length; i++)
            sheet.Cell(row, i + 1).Value = XLCellValue.FromObject(values[i]);
    }

    private static void WriteGuide(IXLWorksheet sheet, params (string Col, string Req, string Desc)[] rules)
    {
        for (var i = 0; i < rules.Length; i++)
        {
            sheet.Cell(i + 2, 1).Value = rules[i].Col;
            sheet.Cell(i + 2, 2).Value = rules[i].Req;
            sheet.Cell(i + 2, 3).Value = rules[i].Desc;
        }
    }
}
