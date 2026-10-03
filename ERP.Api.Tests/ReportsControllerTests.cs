using ERP.Api.Controllers;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using ClosedXML.Excel;

namespace ERP.Api.Tests
{
    public class ReportsControllerTests
    {
        private readonly Mock<IReportService> _mockService;
        private readonly ReportsController _controller;

        public ReportsControllerTests()
        {
            _mockService = new Mock<IReportService>();
            _controller = new ReportsController(_mockService.Object);
        }

        [Fact]
        public async Task ExportInventoryInOutReport_WritesTransferBreakdownAndFourDecimalFormat()
        {
            _mockService.Setup(x => x.GetInventoryInOutReportAsync(null, null, 1, 2, default))
                .ReturnsAsync(new[]
                {
                    new InventoryInOutReportDto
                    {
                        ProductCode = "P1", ProductName = "Product", UnitName = "Cái", WarehouseName = "Kho A",
                        OpeningQuantity = 100, ImportQuantity = 50, TransferInQuantity = 20,
                        AdjustmentIncreaseQuantity = 5, InQuantity = 75, ExportQuantity = 30,
                        TransferOutQuantity = 25, AdjustmentDecreaseQuantity = 10, OutQuantity = 65,
                        ClosingQuantity = 110
                    }
                });

            var result = await _controller.ExportInventoryInOutReport(null, null, 1, 2);

            var file = result.Should().BeOfType<FileContentResult>().Subject;
            using var stream = new MemoryStream(file.FileContents);
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheet("Báo cáo Xuất nhập tồn");
            sheet.Cell("G1").GetString().Should().Be("Nhận điều chuyển");
            sheet.Cell("K1").GetString().Should().Be("Xuất điều chuyển");
            sheet.Cell("G2").GetValue<decimal>().Should().Be(20);
            sheet.Cell("K2").GetValue<decimal>().Should().Be(25);
            sheet.Cell("N2").GetValue<decimal>().Should().Be(110);
            sheet.Cell("N2").Style.NumberFormat.Format.Should().Be("#,##0.0000");
        }

        [Fact]
        public async Task GetInventoryInOutReport_ValidRequest_ReturnsOkWithData()
        {
            var mockData = new List<InventoryInOutReportDto>
            {
                new InventoryInOutReportDto { ProductId = 1, ClosingQuantity = 10 }
            };
            
            _mockService.Setup(x => x.GetInventoryInOutReportAsync(null, null, null, null))
                        .ReturnsAsync(mockData);

            var result = await _controller.GetInventoryInOutReport(null, null, null, null);

            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            okResult.Value.Should().BeEquivalentTo(mockData);
        }

        [Fact]
        public async Task GetInventoryInOutReport_ServiceThrowsBusinessRuleException_ReturnsBadRequest()
        {
            _mockService.Setup(x => x.GetInventoryInOutReportAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, null))
                        .ThrowsAsync(new BusinessRuleException("Từ ngày không được lớn hơn đến ngày."));

            var result = await _controller.GetInventoryInOutReport(new DateTime(2023, 1, 10), new DateTime(2023, 1, 1), null, null);

            var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            badRequest.Value.Should().BeEquivalentTo(new { message = "Từ ngày không được lớn hơn đến ngày." });
        }

        [Fact]
        public async Task ExportInventoryReport_ValidRequest_ReturnsFileContentResult()
        {
            var lastUpdatedDate = new DateTime(2023, 9, 25, 14, 30, 0);
            var mockData = new List<InventoryReportDto>
            {
                new InventoryReportDto
                {
                    ProductId = 1,
                    ProductCode = "P1",
                    ProductName = "Prod1",
                    UnitName = "Cai",
                    WarehouseId = 10,
                    WarehouseName = "Kho 10",
                    Quantity = 5.5m,
                    ReportDate = new DateTime(2023, 10, 1),
                    LastUpdated = lastUpdatedDate
                }
            };

            var testAsOfDate = new DateTime(2023, 10, 1);
            int testWarehouseId = 2;
            int testProductId = 3;

            _mockService.Setup(x => x.GetInventoryReportAsync(testAsOfDate, testWarehouseId, testProductId))
                        .ReturnsAsync(mockData);

            var result = await _controller.ExportInventoryReport(testAsOfDate, testWarehouseId, testProductId);

            _mockService.Verify(x => x.GetInventoryReportAsync(testAsOfDate, testWarehouseId, testProductId), Times.Once);

            var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
            fileResult.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            fileResult.FileDownloadName.Should().EndWith(".xlsx");
            fileResult.FileContents.Should().NotBeEmpty();

            // Reopen ClosedXML workbook from memory stream to assert cell content
            using var stream = new System.IO.MemoryStream(fileResult.FileContents);
            using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
            var worksheet = workbook.Worksheet("Báo cáo Tồn kho");
            worksheet.Should().NotBeNull();

            // Assert headers
            worksheet.Cell(1, 1).GetString().Should().Be("Mã SP");
            worksheet.Cell(1, 2).GetString().Should().Be("Tên SP");
            worksheet.Cell(1, 3).GetString().Should().Be("ĐVT");
            worksheet.Cell(1, 4).GetString().Should().Be("Kho");
            worksheet.Cell(1, 5).GetString().Should().Be("Số lượng tồn");
            worksheet.Cell(1, 6).GetString().Should().Be("Cập nhật lần cuối");
            worksheet.Cell(1, 7).GetString().Should().Be("Ngày báo cáo");

            // Assert data row
            worksheet.Cell(2, 1).GetString().Should().Be("P1");
            worksheet.Cell(2, 2).GetString().Should().Be("Prod1");
            worksheet.Cell(2, 3).GetString().Should().Be("Cai");
            worksheet.Cell(2, 4).GetString().Should().Be("Kho 10");
            worksheet.Cell(2, 5).GetDouble().Should().Be(5.5);
            worksheet.Cell(2, 6).GetString().Should().Be("2023-09-25 14:30:00");
            worksheet.Cell(2, 7).GetString().Should().Be("2023-10-01 00:00:00");
        }

        [Fact]
        public async Task ExportInventoryInOutReport_ValidRequest_ReturnsFileContentResult()
        {
            var mockData = new List<InventoryInOutReportDto>
            {
                new InventoryInOutReportDto
                {
                    ProductId = 1,
                    ProductCode = "P1",
                    ProductName = "Prod1",
                    UnitName = "Cai",
                    WarehouseId = 4,
                    WarehouseName = "Kho Test",
                    OpeningQuantity = 100m,
                    ImportQuantity = 30m,
                    TransferInQuantity = 15m,
                    AdjustmentIncreaseQuantity = 5m,
                    InQuantity = 50m,
                    ExportQuantity = 10m,
                    TransferOutQuantity = 7m,
                    AdjustmentDecreaseQuantity = 3m,
                    OutQuantity = 20m,
                    ClosingQuantity = 130m
                }
            };

            var testFromDate = new DateTime(2023, 1, 1);
            var testToDate = new DateTime(2023, 1, 31);
            int testWarehouseId = 4;
            int testProductId = 5;

            _mockService.Setup(x => x.GetInventoryInOutReportAsync(testFromDate, testToDate, testWarehouseId, testProductId))
                        .ReturnsAsync(mockData);

            var result = await _controller.ExportInventoryInOutReport(testFromDate, testToDate, testWarehouseId, testProductId);

            _mockService.Verify(x => x.GetInventoryInOutReportAsync(testFromDate, testToDate, testWarehouseId, testProductId), Times.Once);

            var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
            fileResult.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            fileResult.FileDownloadName.Should().EndWith(".xlsx");
            fileResult.FileContents.Should().NotBeEmpty();

            // Reopen ClosedXML workbook from memory stream to assert cell content
            using var stream = new System.IO.MemoryStream(fileResult.FileContents);
            using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
            var worksheet = workbook.Worksheet("Báo cáo Xuất nhập tồn");
            worksheet.Should().NotBeNull();

            // Assert headers
            worksheet.Cell(1, 1).GetString().Should().Be("Mã SP");
            worksheet.Cell(1, 2).GetString().Should().Be("Tên SP");
            worksheet.Cell(1, 3).GetString().Should().Be("ĐVT");
            worksheet.Cell(1, 4).GetString().Should().Be("Kho");
            worksheet.Cell(1, 5).GetString().Should().Be("Tồn đầu kỳ");
            worksheet.Cell(1, 6).GetString().Should().Be("Nhập kho");
            worksheet.Cell(1, 7).GetString().Should().Be("Nhận điều chuyển");
            worksheet.Cell(1, 8).GetString().Should().Be("Điều chỉnh tăng");
            worksheet.Cell(1, 9).GetString().Should().Be("Tổng tăng");
            worksheet.Cell(1, 10).GetString().Should().Be("Xuất kho");
            worksheet.Cell(1, 11).GetString().Should().Be("Xuất điều chuyển");
            worksheet.Cell(1, 12).GetString().Should().Be("Điều chỉnh giảm");
            worksheet.Cell(1, 13).GetString().Should().Be("Tổng giảm");
            worksheet.Cell(1, 14).GetString().Should().Be("Tồn cuối kỳ");

            // Assert data row
            worksheet.Cell(2, 1).GetString().Should().Be("P1");
            worksheet.Cell(2, 2).GetString().Should().Be("Prod1");
            worksheet.Cell(2, 3).GetString().Should().Be("Cai");
            worksheet.Cell(2, 4).GetString().Should().Be("Kho Test");
            worksheet.Cell(2, 5).GetDouble().Should().Be(100.0);
            worksheet.Cell(2, 6).GetDouble().Should().Be(30.0);
            worksheet.Cell(2, 7).GetDouble().Should().Be(15.0);
            worksheet.Cell(2, 8).GetDouble().Should().Be(5.0);
            worksheet.Cell(2, 9).GetDouble().Should().Be(50.0);
            worksheet.Cell(2, 10).GetDouble().Should().Be(10.0);
            worksheet.Cell(2, 11).GetDouble().Should().Be(7.0);
            worksheet.Cell(2, 12).GetDouble().Should().Be(3.0);
            worksheet.Cell(2, 13).GetDouble().Should().Be(20.0);
            worksheet.Cell(2, 14).GetDouble().Should().Be(130.0);
        }

        [Fact]
        public async Task ExportInventoryInOutReport_ServiceThrowsBusinessRuleException_ReturnsBadRequest()
        {
            _mockService.Setup(x => x.GetInventoryInOutReportAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, null))
                        .ThrowsAsync(new BusinessRuleException("Từ ngày lỗi."));

            var result = await _controller.ExportInventoryInOutReport(new DateTime(2023, 1, 10), new DateTime(2023, 1, 1), null, null);

            var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            badRequest.Value.Should().BeEquivalentTo(new { message = "Từ ngày lỗi." });
        }
    }
}
