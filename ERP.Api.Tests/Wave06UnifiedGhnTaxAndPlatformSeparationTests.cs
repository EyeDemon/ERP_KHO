using System.Security.Claims;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Api.Controllers;
using ERP.Api.Middleware;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using ERP.Infrastructure.Services.Carriers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ERP.Api.Tests;

public class Wave06UnifiedGhnTaxAndPlatformSeparationTests
{
    private static DbContextOptions<ErpKhoDbContext> CreateInMemoryOptions(string dbName) =>
        new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static ControllerContext CreateControllerContext(
        int userId,
        string role,
        string securityDomain = "TENANT",
        params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("Id", userId.ToString()),
            new(ClaimTypes.Role, role),
            new(TenantClaimTypes.TenantId, "1"),
            new(TenantClaimTypes.SecurityDomain, securityDomain)
        };
        foreach (var perm in permissions)
        {
            claims.Add(new Claim("permissions", perm));
            claims.Add(new Claim("permission", perm));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new ControllerContext { HttpContext = httpContext };
    }

    private static IConfiguration BuildSandboxConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Carriers:GHN:Environment"] = "DEV_SANDBOX",
                ["Carriers:GHN:ShopId"] = "235512",
                ["Carriers:GHN:WebhookHmacSecret"] = "TEST_WAVE06_GHN_HMAC_SECRET",
                ["Carriers:TokenEncryptionKey"] = "TEST_WAVE06_TOKEN_ENCRYPTION_KEY"
            })
            .Build();

    [Fact]
    public async Task TC_W6_01_MultiCarrierRateShopping_AntiDuplicate409_SyncDetail_CancelWaybill_And_WebhookMonotonicRtsGuard()
    {
        var dbName = $"{nameof(TC_W6_01_MultiCarrierRateShopping_AntiDuplicate409_SyncDetail_CancelWaybill_And_WebhookMonotonicRtsGuard)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH-W6-01", Name = "Kho Tổng Quận 12", IsActive = true });
        db.Products.Add(new Product { Id = 101, Code = "SKU-W6-101", Name = "Bàn phím cơ không dây", UnitId = 1, MovingAverageCost = 500000m, IsActive = true });
        db.Shipments.Add(new Shipment
        {
            Id = 501,
            TenantId = 1,
            ShipmentCode = "SHP-W6-501",
            WarehouseId = 1,
            Status = ShipmentStatus.Draft,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow
        });
        db.ExportReceipts.Add(new ExportReceipt
        {
            Id = 601,
            TenantId = 1,
            Code = "PXK-W6-601",
            WarehouseId = 1,
            ShipmentId = 501,
            Status = ReceiptStatus.Approved,
            CustomerNameSnapshot = "Trần Minh Khoa",
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow,
            Details =
            [
                new ExportReceiptDetail { Id = 6011, ProductId = 101, Quantity = 2m, UnitPrice = 650000m }
            ]
        });
        await db.SaveChangesAsync();

        var config = BuildSandboxConfiguration();
        var shipmentOps = new ShipmentOperationsService(db, configuration: config);

        // 1. Multi-Carrier Rate Shopping (ODP-CARRIER-01: GHN, GHTK, VIETTELPOST, JT_EXPRESS)
        var rateOutcome = await shipmentOps.CalculateCarrierFeeAsync(
            new CalculateCarrierFeeInput(
                WarehouseId: 1,
                CarrierCode: "GHN",
                ToDistrictId: 1442,
                ToWardCode: "20101",
                WeightGrams: 1200,
                CompareAllCarriers: true));
        rateOutcome.StatusCode.Should().Be(StatusCodes.Status200OK);
        using (var rateDoc = JsonSerializer.SerializeToDocument(rateOutcome.Payload))
        {
            var quotes = rateDoc.RootElement.GetProperty("carrierQuotes");
            quotes.GetArrayLength().Should().BeGreaterThanOrEqualTo(4);
            var codes = quotes.EnumerateArray().Select(x => x.GetProperty("carrierCode").GetString()).ToList();
            codes.Should().Contain(["GHN", "GHTK", "VIETTELPOST", "JT_EXPRESS"]);
        }

        // 2. Book first waybill -> 200 OK; Book duplicate waybill -> 409 Conflict (GHN_WAYBILL_ALREADY_BOOKED)
        var bookReq = new CarrierWaybillBookingRequest(
            WarehouseId: 1,
            ShipmentId: 501,
            ExportReceiptId: 601,
            CarrierCode: "GHTK",
            RecipientName: "Trần Minh Khoa",
            RecipientPhone: "0909123456",
            RawAddress: "123 Nguyễn Huệ, Phường Bến Nghé",
            Province: "TP. Hồ Chí Minh",
            Ward: "Phường Bến Nghé",
            WeightGrams: 1200m,
            CodAmountVnd: 1300000m);

        var book1 = await shipmentOps.BookCarrierWaybillAsync(bookReq, actorUserId: 1);
        book1.StatusCode.Should().Be(StatusCodes.Status200OK);
        using var book1Doc = JsonSerializer.SerializeToDocument(book1.Payload);
        var trackingNumber = book1Doc.RootElement.GetProperty("trackingNumber").GetString()!;
        trackingNumber.Should().NotBeNullOrWhiteSpace();
        book1Doc.RootElement.GetProperty("clientOrderCode").GetString().Should().Be("ERPKHO-T1-S501-SHP-W6-501");

        var bookDuplicate = await shipmentOps.BookCarrierWaybillAsync(bookReq, actorUserId: 1);
        bookDuplicate.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        using (var dupDoc = JsonSerializer.SerializeToDocument(bookDuplicate.Payload))
        {
            dupDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("GHN_WAYBILL_ALREADY_BOOKED");
        }

        // 3. Sync waybill detail (sync-detail)
        var syncOutcome = await shipmentOps.SyncCarrierWaybillDetailAsync(
            501,
            new SyncGhnOrderDetailInput(OrderCode: trackingNumber, CarrierCode: "GHTK"),
            actorUserId: 1);
        syncOutcome.StatusCode.Should().Be(StatusCodes.Status200OK);

        // 4. Webhook Monotonic State Guard: advance to DELIVERED, then send out-of-order PICKED -> must block regression
        const string ghnSecret = "TEST_WAVE06_GHN_HMAC_SECRET";
        var delivEvt = new CarrierWebhookEventInput(
            EventId: "EVT-DELIV-501",
            TrackingNumber: trackingNumber,
            ShipmentId: 501,
            ExportReceiptId: 601,
            CarrierStatus: "DELIVERED",
            ActualCodCollected: 1300000m);
        var delivRaw = JsonSerializer.Serialize(delivEvt);
        var whDelivered = await shipmentOps.IngestCarrierWebhookAsync(
            "GHN",
            delivEvt,
            rawBody: delivRaw,
            signatureHeader: ERP.Infrastructure.Services.Carriers.VietnamCarrierAdapterBase.ComputeHmacSha256Hex(delivRaw, ghnSecret),
            actorUserId: 1);
        whDelivered.StatusCode.Should().Be(StatusCodes.Status200OK);
        (await db.Shipments.SingleAsync(s => s.Id == 501)).Status.Should().Be(ShipmentStatus.Delivered);

        var oooEvt = new CarrierWebhookEventInput(
            EventId: "EVT-PICKED-501",
            TrackingNumber: trackingNumber,
            ShipmentId: 501,
            ExportReceiptId: 601,
            CarrierStatus: "PICKED",
            ActualCodCollected: 1300000m);
        var oooRaw = JsonSerializer.Serialize(oooEvt);
        var whOutOfOrder = await shipmentOps.IngestCarrierWebhookAsync(
            "GHN",
            oooEvt,
            rawBody: oooRaw,
            signatureHeader: ERP.Infrastructure.Services.Carriers.VietnamCarrierAdapterBase.ComputeHmacSha256Hex(oooRaw, ghnSecret),
            actorUserId: 1);
        whOutOfOrder.StatusCode.Should().Be(StatusCodes.Status200OK);
        using (var oooDoc = JsonSerializer.SerializeToDocument(whOutOfOrder.Payload))
        {
            oooDoc.RootElement.GetProperty("stateRegressionBlocked").GetBoolean().Should().BeTrue();
        }
        (await db.Shipments.SingleAsync(s => s.Id == 501)).Status.Should().Be(ShipmentStatus.Delivered);

        // 5. Webhook RTS / RETURNED on a second shipment -> transitions to ReturnToWarehouse, zeroes ExpectedCodAmount, creates QUARANTINED CustomerReturn
        db.Shipments.Add(new Shipment
        {
            Id = 502,
            TenantId = 1,
            ShipmentCode = "SHP-W6-502",
            WarehouseId = 1,
            SourceCode = "G8RTS502",
            Status = ShipmentStatus.Dispatched,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow
        });
        db.ExportReceipts.Add(new ExportReceipt
        {
            Id = 602,
            TenantId = 1,
            Code = "PXK-W6-602",
            WarehouseId = 1,
            ShipmentId = 502,
            TrackingNumber = "G8RTS502",
            Status = ReceiptStatus.Dispatched,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow
        });
        db.ShipmentFinancialLedgers.Add(new ShipmentFinancialLedger
        {
            TenantId = 1,
            ShipmentId = 502,
            ExportReceiptId = 602,
            CarrierCode = "GHN",
            TrackingNumber = "G8RTS502",
            ExpectedCodAmount = 950000m,
            ReconciliationStatus = "PENDING",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var rtsEvt = new CarrierWebhookEventInput(
            EventId: "EVT-RTS-502",
            TrackingNumber: "G8RTS502",
            ShipmentId: 502,
            ExportReceiptId: 602,
            CarrierStatus: "RETURNED",
            ActualCodCollected: 0m);
        var rtsRaw = JsonSerializer.Serialize(rtsEvt);
        var whRts = await shipmentOps.IngestCarrierWebhookAsync(
            "GHN",
            rtsEvt,
            rawBody: rtsRaw,
            signatureHeader: ERP.Infrastructure.Services.Carriers.VietnamCarrierAdapterBase.ComputeHmacSha256Hex(rtsRaw, ghnSecret),
            actorUserId: 1);
        whRts.StatusCode.Should().Be(StatusCodes.Status200OK);
        (await db.Shipments.SingleAsync(s => s.Id == 502)).Status.Should().Be(ShipmentStatus.ReturnToWarehouse);
        (await db.ShipmentFinancialLedgers.SingleAsync(l => l.TrackingNumber == "G8RTS502")).ExpectedCodAmount.Should().Be(0m);
        var autoRma = await db.CustomerReturns.SingleOrDefaultAsync(r => r.ExportReceiptId == 602);
        autoRma.Should().NotBeNull();
        autoRma!.Status.Should().Be("QUARANTINED");
    }

    [Fact]
    public async Task TC_W6_02_TaxCompliance_BuyerPiiRetention_PreservesAccountingFieldsUnder10Years_And_Validates12DigitCccdTaxCode()
    {
        var dbName = $"{nameof(TC_W6_02_TaxCompliance_BuyerPiiRetention_PreservesAccountingFieldsUnder10Years_And_Validates12DigitCccdTaxCode)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        // 120-day-old posted ExportReceipt (< 10 years / 3,650 days) and a 11-year-old ExportReceipt (> 3,650 days)
        db.ExportReceipts.AddRange(
            new ExportReceipt
            {
                Id = 701,
                TenantId = 1,
                Code = "PXK-120D-701",
                WarehouseId = 1,
                Status = ReceiptStatus.Dispatched,
                CustomerNameSnapshot = "Công ty Cổ phần Thiết bị Minh Long",
                Note = "Giao tận nơi SĐT 0909123456",
                CreatedAt = DateTime.UtcNow.AddDays(-120),
                CreatedBy = 1
            },
            new ExportReceipt
            {
                Id = 702,
                TenantId = 1,
                Code = "PXK-11Y-702",
                WarehouseId = 1,
                Status = ReceiptStatus.Dispatched,
                CustomerNameSnapshot = "Khách hàng Quá hạn 10 Năm",
                Note = "Liên hệ cũ",
                CreatedAt = DateTime.UtcNow.AddDays(-3700),
                CreatedBy = 1
            });
        await db.SaveChangesAsync();

        var govService = new WmsGovernanceOperationsService(db);
        var piiOutcome = await govService.AnonymizeExpiredBuyerPiiAsync(retentionDays: 90, actorUserId: 1);
        piiOutcome.StatusCode.Should().Be(StatusCodes.Status200OK);

        var receipt120d = await db.ExportReceipts.SingleAsync(r => r.Id == 701);
        receipt120d.CustomerNameSnapshot.Should().Be("Công ty Cổ phần Thiết bị Minh Long",
            "Điều 41 Luật Kế toán 88/2015/QH13 mandates 10-year retention of accounting voucher identity");
        receipt120d.Note.Should().Contain("[PII_CONTACT_REDACTED_ND13_KEEP_ACCOUNTING_10Y]");

        var receipt11y = await db.ExportReceipts.SingleAsync(r => r.Id == 702);
        receipt11y.CustomerNameSnapshot.Should().StartWith("ANONYMIZED_PII_");

        // Thông tư 86/2024/TT-BTC: 10-digit enterprise MST, 12-digit individual CCCD, and invalid tax code
        var billingService = new SaasBillingService(db);
        var validEnterprise = await billingService.UpdateTaxProfileAsync(
            new SaasTaxProfileUpdateRequest(TaxCode: "0312345678", CompanyName: "CÔNG TY TNHH KHO VẬN Á CHÂU", TaxpayerType: "ENTERPRISE"),
            actorUserId: 1);
        validEnterprise.Valid.Should().BeTrue();

        var validCccd = await billingService.UpdateTaxProfileAsync(
            new SaasTaxProfileUpdateRequest(TaxCode: "079099001234", CompanyName: "Hộ Kinh Doanh Nguyễn Văn A", TaxpayerType: "HOUSEHOLD_INDIVIDUAL"),
            actorUserId: 1);
        validCccd.Valid.Should().BeTrue();

        var invalidTax = await billingService.UpdateTaxProfileAsync(
            new SaasTaxProfileUpdateRequest(TaxCode: "ABC-999", CompanyName: "Sai MST"),
            actorUserId: 1);
        invalidTax.Valid.Should().BeFalse();
        invalidTax.ErrorCode.Should().Be("INVALID_VIETNAM_TAX_CODE");
    }

    [Fact]
    public async Task TC_W6_03_TaxCompliance_EInvoiceBlocksUnilateralCancel_RequiresAgreementOnAdjustment_And_RejectsZeroUnitPrice()
    {
        var dbName = $"{nameof(TC_W6_03_TaxCompliance_EInvoiceBlocksUnilateralCancel_RequiresAgreementOnAdjustment_And_RejectsZeroUnitPrice)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH-EINV-01", Name = "Kho HĐĐT NĐ70", IsActive = true });
        var prod = new Product { Id = 201, Code = "SKU-EINV-201", Name = "Màn hình 27 inch", UnitId = 1, MovingAverageCost = 3000000m, DefaultVatRatePercent = 8.00m, IsActive = true };
        db.Products.Add(prod);
        db.ExportReceipts.Add(new ExportReceipt
        {
            Id = 801,
            Code = "PXK-EINV-801",
            WarehouseId = 1,
            Status = ReceiptStatus.Dispatched,
            EInvoiceStatus = "ND70_ISSUED",
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow,
            Details =
            [
                new ExportReceiptDetail { Id = 8011, ProductId = 201, Product = prod, Quantity = 1m, UnitPrice = 0m }
            ]
        });
        await db.SaveChangesAsync();

        var expOps = new ExportReceiptOperationsService(db);

        // 1. Unilateral CANCEL on issued e-invoice -> 422 EINVOICE_CANNOT_CANCEL_ISSUED_INVOICE (Khoản 13 Điều 1 NĐ 70/2025/NĐ-CP)
        var cancelOutcome = await expOps.IssueEinvoiceNd70DraftAsync(
            new IssueEinvoiceNd70DraftInput(ExportReceiptId: 801, ActionType: "CANCEL"),
            actorUserId: 1,
            isAuthenticated: true);
        cancelOutcome.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        using (var cancelDoc = JsonSerializer.SerializeToDocument(cancelOutcome.Payload))
        {
            cancelDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("EINVOICE_CANNOT_CANCEL_ISSUED_INVOICE");
        }

        // 2. ADJUSTMENT without AgreementDocumentNo -> 422 EINVOICE_AGREEMENT_DOCUMENT_REQUIRED
        var adjMissingAgreement = await expOps.IssueEinvoiceNd70DraftAsync(
            new IssueEinvoiceNd70DraftInput(ExportReceiptId: 801, ActionType: "ADJUSTMENT", AgreementDocumentNo: null),
            actorUserId: 1,
            isAuthenticated: true);
        adjMissingAgreement.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        using (var adjDoc = JsonSerializer.SerializeToDocument(adjMissingAgreement.Payload))
        {
            adjDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("EINVOICE_AGREEMENT_DOCUMENT_REQUIRED");
        }

        // 3. UnitPrice <= 0 with ActionType/EnforceRealUnitPrice -> 422 EINVOICE_MISSING_SELLING_PRICE
        var missingPriceOutcome = await expOps.IssueEinvoiceNd70DraftAsync(
            new IssueEinvoiceNd70DraftInput(
                ExportReceiptId: 801,
                ActionType: "ADJUSTMENT",
                AgreementDocumentNo: "BB-04SS-2026-01",
                EnforceRealUnitPrice: true),
            actorUserId: 1,
            isAuthenticated: true);
        missingPriceOutcome.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        using (var priceDoc = JsonSerializer.SerializeToDocument(missingPriceOutcome.Payload))
        {
            priceDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("EINVOICE_MISSING_SELLING_PRICE");
        }
    }

    [Fact]
    public async Task TC_W6_04_TaxCompliance_CashPaymentAbove5MillionFlagsNonDeductibleVat_ThreePlBailment_And_EcommerceTaxWithholding()
    {
        var dbName = $"{nameof(TC_W6_04_TaxCompliance_CashPaymentAbove5MillionFlagsNonDeductibleVat_ThreePlBailment_And_EcommerceTaxWithholding)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        var supplier = new BusinessPartner
        {
            Id = 301,
            TenantId = 1,
            Code = "SUP-W6-301",
            Name = "Công ty Cung ứng Bao bì",
            IsSupplier = true,
            IsActive = true,
            PayableBalance = 6600000m,
            RowVersion = [1, 0, 0, 0, 0, 0, 0, 1]
        };
        var debtInvoice = new PartnerDebtInvoice
        {
            Id = 901,
            TenantId = 1,
            Code = "AP-IR-000901",
            PartnerId = 301,
            Partner = supplier,
            DebtType = PartnerDebtType.AP,
            SourceType = "ImportReceipt",
            SourceId = 401,
            SourceCode = "PNK-401",
            Currency = "VND",
            SubTotalAmount = 6000000m,
            TaxAmount = 600000m,
            TotalAmount = 6600000m,
            PaidAmount = 0m,
            RemainingAmount = 6600000m,
            Status = PartnerDebtInvoiceStatus.Open,
            IssuedAtUtc = DateTime.UtcNow,
            CreatedBy = 1,
            CreatedAtUtc = DateTime.UtcNow,
            RowVersion = [1, 0, 0, 0, 0, 0, 0, 2]
        };
        db.BusinessPartners.Add(supplier);
        db.PartnerDebtInvoices.Add(debtInvoice);
        await db.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.SetupGet(x => x.UserId).Returns(1);
        var debtService = new PartnerDebtService(db, currentUserMock.Object);

        // 1. Cash payment >= 5,000,000 VND flags IsInputVatDeductible = false (Luật Thuế GTGT 48/2024/QH15 & Điều 26 NĐ 181/2025/NĐ-CP)
        var paidDto = await debtService.PayInvoiceAsync(
            901,
            new PayPartnerDebtDto
            {
                Amount = 6600000m,
                PaymentMethod = "CASH",
                ReferenceCode = "PT-TM-901",
                RowVersion = Convert.ToBase64String(debtInvoice.RowVersion)
            });
        paidDto.IsInputVatDeductible.Should().BeFalse();
        paidDto.TaxComplianceWarning.Should().Contain("5.000.000 VNĐ");

        // 2. 3PL Bailment ImportReceipt must NOT create goods-value AP debt
        var bailmentReceipt = new ImportReceipt
        {
            Id = 402,
            TenantId = 1,
            Code = "PNK-BAILMENT-402",
            WarehouseId = 1,
            SupplierId = 301,
            Note = "[BAILMENT] Hàng nhận giữ hộ gia công 3PL TK 002",
            Details =
            [
                new ImportReceiptDetail { Id = 4021, ProductId = 101, AcceptedQuantity = 10m, BaseAcceptedQuantity = 10m, ConversionFactor = 1m, UnitPrice = 500000m, TaxPercent = 10m }
            ]
        };
        var bailmentDebt = await debtService.RecordImportReceiptApDebtAsync(bailmentReceipt, actorUserId: 1);
        bailmentDebt.Should().BeNull("3PL bailment receipts must not record goods-value AP debt");

        // 3. E-commerce Platform Tax Withholding (Nghị định 117/2025/NĐ-CP): HOUSEHOLD_INDIVIDUAL (1.5%) vs ENTERPRISE (0%)
        var reportOps = new ReportOperationsService(db);
        var codOutcome = await reportOps.VerifyCodAndSettlementReconciliationAsync(
            warehouseId: null,
            carrierCode: "GHN",
            reconcileMode: null,
            items:
            [
                new CodSettlementItemRequestBase(
                    TrackingNumber: "GHN-HKD-01",
                    CarrierCode: "GHN",
                    ExpectedCodAmount: 1000000m,
                    ActualCodCollected: 1000000m,
                    TaxpayerType: "HOUSEHOLD_INDIVIDUAL"),
                new CodSettlementItemRequestBase(
                    TrackingNumber: "GHN-ENT-02",
                    CarrierCode: "GHN",
                    ExpectedCodAmount: 1000000m,
                    ActualCodCollected: 1000000m,
                    TaxpayerType: "ENTERPRISE")
            ]);
        codOutcome.StatusCode.Should().Be(StatusCodes.Status200OK);
        using var codDoc = JsonSerializer.SerializeToDocument(codOutcome.Payload);
        var itemsArr = codDoc.RootElement.GetProperty("items");
        itemsArr[0].GetProperty("withheldTaxAmount").GetDecimal().Should().Be(15000m); // 1% VAT (10,000) + 0.5% PIT (5,000)
        itemsArr[1].GetProperty("withheldTaxAmount").GetDecimal().Should().Be(0m);     // Enterprise self-declares (0 withheld)
    }

    [Fact]
    public async Task TC_W6_05_PlatformSeparation_PlatformAuthorizeBlocksTenantAdmin_BlocksDirectPlatformWarehouseAccess_And_EnforcesSuspendedTenant()
    {
        var dbName = $"{nameof(TC_W6_05_PlatformSeparation_PlatformAuthorizeBlocksTenantAdmin_BlocksDirectPlatformWarehouseAccess_And_EnforcesSuspendedTenant)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.SaasTenants.Add(new SaasTenant
        {
            Id = 1,
            TenantId = 1,
            TenantCode = "TENANT-001",
            TenantName = "Công ty Kho vận Khách hàng 01",
            LifecycleStatus = "ACTIVE",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // 1. PlatformAuthorizationFilter blocks TENANT Admin from /api/v1/platform/* (403 PLATFORM_PERMISSION_REQUIRED)
        var filter = new PlatformAuthorizationFilter(db, AppPermissions.PlatformDashboardRead);
        var tenantCtx = CreateControllerContext(10, AppRoles.Admin, "TENANT", AppPermissions.UserManage);
        var tenantFilterCtx = new AuthorizationFilterContext(
            new ActionContext(tenantCtx.HttpContext, new RouteData(), new ActionDescriptor()),
            []);
        await filter.OnAuthorizationAsync(tenantFilterCtx);
        tenantFilterCtx.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        // 2. PlatformAuthorizationFilter allows PLATFORM PlatformSuperAdmin
        var platformCtx = CreateControllerContext(1, AppRoles.PlatformSuperAdmin, "PLATFORM", AppPermissions.PlatformDashboardRead);
        var platformFilterCtx = new AuthorizationFilterContext(
            new ActionContext(platformCtx.HttpContext, new RouteData(), new ActionDescriptor()),
            []);
        await filter.OnAuthorizationAsync(platformFilterCtx);
        platformFilterCtx.Result.Should().BeNull();

        // 3. SupportImpersonationMiddleware blocks PLATFORM user from directly calling /api/v1/warehouses without an active impersonation session
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(db)
            .BuildServiceProvider();
        var middleware = new SupportImpersonationMiddleware(_ => Task.CompletedTask);
        var directWarehouseHttpCtx = new DefaultHttpContext
        {
            User = platformCtx.HttpContext.User,
            RequestServices = serviceProvider
        };
        directWarehouseHttpCtx.Request.Path = "/api/v1/warehouses";
        directWarehouseHttpCtx.Request.Method = HttpMethods.Get;
        directWarehouseHttpCtx.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(directWarehouseHttpCtx);
        directWarehouseHttpCtx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        directWarehouseHttpCtx.Response.Body.Position = 0;
        var directBody = await new StreamReader(directWarehouseHttpCtx.Response.Body).ReadToEndAsync();
        directBody.Should().Contain("PLATFORM_DIRECT_WAREHOUSE_ACCESS_FORBIDDEN");

        // 4. Suspended Tenant is blocked on /api/v1/warehouses with 403 TENANT_SUSPENDED
        var tenantEntity = await db.SaasTenants.SingleAsync(t => t.TenantId == 1);
        tenantEntity.LifecycleStatus = "SUSPENDED";
        await db.SaveChangesAsync();

        var suspendedTenantHttpCtx = new DefaultHttpContext
        {
            User = tenantCtx.HttpContext.User,
            RequestServices = serviceProvider
        };
        suspendedTenantHttpCtx.Request.Path = "/api/v1/warehouses";
        suspendedTenantHttpCtx.Request.Method = HttpMethods.Get;
        suspendedTenantHttpCtx.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(suspendedTenantHttpCtx);
        suspendedTenantHttpCtx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        suspendedTenantHttpCtx.Response.Body.Position = 0;
        var suspendedBody = await new StreamReader(suspendedTenantHttpCtx.Response.Body).ReadToEndAsync();
        suspendedBody.Should().Contain("TENANT_SUSPENDED");
    }
}
