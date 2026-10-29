using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ERP.Api.Infrastructure
{
    internal static class WmsControllerExcelHelper
    {
        public static List<OpeningBalanceItemInput> ParseOpeningBalanceExcel(IFormFile? uploadedFile)
        {
            var items = new List<OpeningBalanceItemInput>();
            if (uploadedFile == null || uploadedFile.Length <= 0)
            {
                return items;
            }

            using var stream = uploadedFile.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheets.First();
            foreach (var row in ws.RowsUsed().Skip(1))
            {
                var pCode = row.Cell(1).GetString()?.Trim();
                var locCode = row.Cell(2).GetString()?.Trim();
                var qtyVal = row.Cell(3).TryGetValue<decimal>(out var q)
                    ? q
                    : (decimal.TryParse(row.Cell(3).GetString()?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var qParsed) ? qParsed : 0m);
                var costVal = row.Cell(4).TryGetValue<decimal>(out var c)
                    ? c
                    : (decimal.TryParse(row.Cell(4).GetString()?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var cParsed) ? cParsed : 0m);
                var lotNumber = row.Cell(5).GetString()?.Trim();
                DateTime? expiryDate = null;
                if (row.Cell(6).TryGetValue<DateTime>(out var dt))
                {
                    expiryDate = DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);
                }
                else if (DateTime.TryParse(row.Cell(6).GetString()?.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dtParsed))
                {
                    expiryDate = DateTime.SpecifyKind(dtParsed.Date, DateTimeKind.Utc);
                }

                if (!string.IsNullOrWhiteSpace(pCode) && qtyVal > 0m)
                {
                    items.Add(new OpeningBalanceItemInput(
                        ProductCode: pCode,
                        LocationCode: string.IsNullOrWhiteSpace(locCode) ? null : locCode,
                        Quantity: qtyVal,
                        UnitCost: costVal,
                        LotNumber: string.IsNullOrWhiteSpace(lotNumber) ? null : lotNumber,
                        ExpiryDate: expiryDate));
                }
            }

            return items;
        }

        public static WmsEndpointOutcome BuildMisaExcelExportOutcome(MisaExcelExportData data, string? format)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("MISA_AMIS_ACT_200");
            var headers = new[]
            {
                "SoChungTu", "NgayHachToan", "MaHang", "TenHang",
                "TKNoGiaVon", "TKCoKho", "SoLuong", "DonGiaVonMAC", "ThanhTienGiaVon",
                "TKNoCongNo", "TKCoDoanhThu", "DonGiaBan", "DoanhThu",
                "EInvoiceStatus", "EInvoiceProvider"
            };
            for (var c = 0; c < headers.Length; c++)
            {
                sheet.Cell(1, c + 1).Value = headers[c];
            }
            sheet.Row(1).Style.Font.Bold = true;

            var rowIdx = 2;
            var rows = new List<object>(data.Rows.Count);
            foreach (var row in data.Rows)
            {
                sheet.Cell(rowIdx, 1).Value = row.SoChungTu;
                sheet.Cell(rowIdx, 2).Value = row.NgayHachToan;
                sheet.Cell(rowIdx, 3).Value = row.MaHang;
                sheet.Cell(rowIdx, 4).Value = row.TenHang;
                sheet.Cell(rowIdx, 5).Value = row.TkNoGiaVon;
                sheet.Cell(rowIdx, 6).Value = row.TkCoKho;
                sheet.Cell(rowIdx, 7).Value = row.SoLuong;
                if (data.CanReadCost && row.DonGiaVonMac.HasValue && row.ThanhTienGiaVon.HasValue)
                {
                    sheet.Cell(rowIdx, 8).Value = row.DonGiaVonMac.Value;
                    sheet.Cell(rowIdx, 9).Value = row.ThanhTienGiaVon.Value;
                }
                else
                {
                    sheet.Cell(rowIdx, 8).Value = string.Empty;
                    sheet.Cell(rowIdx, 9).Value = string.Empty;
                }
                sheet.Cell(rowIdx, 10).Value = row.TkNoCongNo;
                sheet.Cell(rowIdx, 11).Value = row.TkCoDoanhThu;
                sheet.Cell(rowIdx, 12).Value = row.DonGiaBan;
                sheet.Cell(rowIdx, 13).Value = row.DoanhThu;
                sheet.Cell(rowIdx, 14).Value = row.EInvoiceStatus;
                sheet.Cell(rowIdx, 15).Value = row.EInvoiceProvider;
                rowIdx++;

                rows.Add(new
                {
                    soChungTu = row.SoChungTu,
                    ngayHachToan = row.NgayHachToan,
                    maHang = row.MaHang,
                    tenHang = row.TenHang,
                    tkNoGiaVon = row.TkNoGiaVon,
                    tkCoKho = row.TkCoKho,
                    soLuong = row.SoLuong,
                    unitCost = row.UnitCost,
                    donGiaVonMac = row.DonGiaVonMac,
                    costAmount = row.CostAmount,
                    thanhTienGiaVon = row.ThanhTienGiaVon,
                    tkNoCongNo = row.TkNoCongNo,
                    tkCoDoanhThu = row.TkCoDoanhThu,
                    donGiaBan = row.DonGiaBan,
                    doanhThu = row.DoanhThu,
                    eInvoiceStatus = row.EInvoiceStatus,
                    eInvoiceProvider = row.EInvoiceProvider
                });
            }

            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            var xlsxBytes = ms.ToArray();

            if (string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(format, "binary", StringComparison.OrdinalIgnoreCase))
            {
                return WmsEndpointOutcome.File(
                    xlsxBytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "Bang_Ke_Xuat_Kho_MISA_AMIS.xlsx");
            }

            return WmsEndpointOutcome.Ok(new
            {
                templateStandard = "MISA_AMIS_ACT_200",
                circularStandard = "Thông tư 200/2014/TT-BTC & Thông tư 133/2016/TT-BTC",
                defaultDebitCogsAccount = "632",
                defaultCreditInventoryAccount = "1561",
                defaultDebitReceivableAccount = "131",
                defaultCreditRevenueAccount = "5111",
                rowCount = rows.Count,
                costHiddenByRbac = !data.CanReadCost,
                totalCostAmount = data.CanReadCost ? (decimal?)data.TotalCostAmountValue : null,
                totalRevenueAmount = data.TotalRevenueAmountValue,
                downloadUrl = "/api/v1/export-receipts/misa-excel-export?format=xlsx",
                excelBase64 = Convert.ToBase64String(xlsxBytes),
                rows,
                statusLabel = "Đã kết xuất bảng kê chứng từ xuất kho & giá vốn chuẩn MISA AMIS (TK 632 / 1561 / 131 / 5111)"
            });
        }

        public static List<MarketplaceOrderExcelRowInput> ParseMarketplaceOrdersExcel(IFormFile? uploadedFile)
        {
            var rows = new List<MarketplaceOrderExcelRowInput>();
            if (uploadedFile == null || uploadedFile.Length <= 0)
            {
                return rows;
            }

            using var stream = uploadedFile.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheets.First();
            foreach (var r in ws.RowsUsed().Skip(1))
            {
                var extId = r.Cell(2).GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(extId))
                {
                    var qVal = r.Cell(7).TryGetValue<decimal>(out var q) && q > 0m
                        ? q
                        : (decimal.TryParse(r.Cell(7).GetString()?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var qp) && qp > 0m ? qp : 1m);
                    var pVal = r.Cell(8).TryGetValue<decimal>(out var p) && p >= 0m
                        ? p
                        : (decimal.TryParse(r.Cell(8).GetString()?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var pp) && pp >= 0m ? pp : 150000m);
                    rows.Add(new MarketplaceOrderExcelRowInput(
                        ChannelCode: r.Cell(1).GetString()?.Trim(),
                        ExternalOrderId: extId,
                        BuyerName: r.Cell(3).GetString()?.Trim(),
                        BuyerPhone: r.Cell(4).GetString()?.Trim(),
                        ShippingAddress: r.Cell(5).GetString()?.Trim(),
                        SkuCode: r.Cell(6).GetString()?.Trim(),
                        Quantity: qVal,
                        UnitPrice: pVal));
                }
            }

            return rows;
        }
    }
}

namespace ERP.Api.Controllers
{
    internal sealed class FlexibleBodyOrFormModelBinder : IModelBinder
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        public async Task BindModelAsync(ModelBindingContext bindingContext)
        {
            var request = bindingContext.HttpContext.Request;
            var modelType = bindingContext.ModelType;

            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync(bindingContext.HttpContext.RequestAborted);
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in form)
                {
                    var val = kv.Value.ToString();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        if (int.TryParse(val, out var intVal)) dict[kv.Key] = intVal;
                        else if (decimal.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var decVal)) dict[kv.Key] = decVal;
                        else if (bool.TryParse(val, out var boolVal)) dict[kv.Key] = boolVal;
                        else dict[kv.Key] = val;
                    }
                }
                var json = JsonSerializer.Serialize(dict, JsonOptions);
                bindingContext.Result = ModelBindingResult.Success(JsonSerializer.Deserialize(json, modelType, JsonOptions));
                return;
            }

            try
            {
                if (request.Body != null)
                {
                    if (request.Body.CanSeek) request.Body.Position = 0;
                    using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                    var bodyText = await reader.ReadToEndAsync(bindingContext.HttpContext.RequestAborted);
                    if (!string.IsNullOrWhiteSpace(bodyText))
                    {
                        bindingContext.Result = ModelBindingResult.Success(JsonSerializer.Deserialize(bodyText, modelType, JsonOptions));
                        return;
                    }
                }
            }
            catch (JsonException)
            {
                bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "Payload JSON không hợp lệ.");
            }

            bindingContext.Result = ModelBindingResult.Success(null);
        }
    }

    internal sealed class FlexibleFormFileModelBinder : IModelBinder
    {
        public async Task BindModelAsync(ModelBindingContext bindingContext)
        {
            var request = bindingContext.HttpContext.Request;
            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync(bindingContext.HttpContext.RequestAborted);
                bindingContext.Result = ModelBindingResult.Success(form.Files.FirstOrDefault());
                return;
            }
            bindingContext.Result = ModelBindingResult.Success(null);
        }
    }
}
