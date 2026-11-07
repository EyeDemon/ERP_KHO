using System.Security.Claims;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Api.Controllers;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using ERP.Infrastructure.Services.Carriers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace ERP.Api.Tests;

public class Wave05GhnLiveIntegrationTests
{
    private static DbContextOptions<ErpKhoDbContext> CreateInMemoryOptions(string dbName) =>
        new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static ControllerContext CreateControllerContextWithPermissions(int userId, string role, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("Id", userId.ToString()),
            new(ClaimTypes.Role, role)
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

    private static IConfiguration BuildUserSecretsAwareConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Carriers:GHN:Environment"] = "DEV_SANDBOX",
                ["Carriers:GHN:ShopId"] = "235512",
                ["Carriers:TokenEncryptionKey"] = "test-wave05-ghn-token-encryption-key"
            })
            .AddUserSecrets<Program>(optional: true)
            .Build();

    [Fact]
    public async Task TC_W5_01_VerifyAndSyncGhnConfig_UnauthorizedRole_Returns403Forbidden()
    {
        var dbName = $"{nameof(TC_W5_01_VerifyAndSyncGhnConfig_UnauthorizedRole_Returns403Forbidden)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH-GHN-01", Name = "Kho GHN Q12", IsActive = true });
        await db.SaveChangesAsync();

        var shipmentServiceMock = new Mock<IShipmentService>();
        var warehouseAccessMock = new Mock<IWarehouseAuthorizationService>();

        var shipmentOps = new ShipmentOperationsService(db, configuration: BuildUserSecretsAwareConfiguration());
        var controller = new ShipmentsController(shipmentServiceMock.Object, shipmentOps, warehouseAccessMock.Object)
        {
            // Viewer role only has ShipmentRead, lacks ShipmentUpdate / ShipmentDispatch
            ControllerContext = CreateControllerContextWithPermissions(99, "Viewer", AppPermissions.ShipmentRead)
        };

        var result = await controller.VerifyAndSyncGhnConfig(
            new VerifyAndSyncGhnInput(WarehouseId: 1, ApiToken: null, Environment: "DEV_SANDBOX", ShopId: "235512"),
            CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task TC_W5_02_CarrierConfigs_MasksApiToken_And_NeverExposesPlaintextToken()
    {
        var dbName = $"{nameof(TC_W5_02_CarrierConfigs_MasksApiToken_And_NeverExposesPlaintextToken)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH-GHN-02", Name = "Kho GHN Bảo Mật Token", IsActive = true });
        await db.SaveChangesAsync();

        var config = BuildUserSecretsAwareConfiguration();
        var rawToken = GhnLiveGatewayHelper.ResolveConfiguredGhnToken(null, null, config);
        rawToken.Should().NotBeNullOrWhiteSpace("UserSecrets must provide the GHN Dev Sandbox API token");

        var shipmentServiceMock = new Mock<IShipmentService>();
        var warehouseAccessMock = new Mock<IWarehouseAuthorizationService>();

        var shipmentOps = new ShipmentOperationsService(db, configuration: config);
        var controller = new ShipmentsController(shipmentServiceMock.Object, shipmentOps, warehouseAccessMock.Object)
        {
            ControllerContext = CreateControllerContextWithPermissions(1, "Admin", AppPermissions.ShipmentRead, AppPermissions.ShipmentUpdate, AppPermissions.ShipmentDispatch)
        };

        var syncActionResult = await controller.VerifyAndSyncGhnConfig(
            new VerifyAndSyncGhnInput(WarehouseId: 1, ApiToken: null, Environment: "DEV_SANDBOX", ShopId: "235512"),
            CancellationToken.None);

        var syncOk = syncActionResult.Should().BeOfType<OkObjectResult>().Subject;
        var syncJson = JsonSerializer.Serialize(syncOk.Value);
        syncJson.Should().NotContain(rawToken!);

        using var syncDoc = JsonDocument.Parse(syncJson);
        syncDoc.RootElement.GetProperty("maskedApiToken").GetString().Should().Be("0c06****12be");
        syncDoc.RootElement.GetProperty("shopId").GetString().Should().Be("235512");

        var storedConfig = await db.CarrierIntegrationConfigs.SingleAsync(x => x.WarehouseId == 1 && x.CarrierCode == "GHN");
        storedConfig.EncryptedApiToken.Should().StartWith("ENC:v1:");
        storedConfig.EncryptedApiToken.Should().NotContain(rawToken!);

        var getActionResult = await controller.GetCarrierConfigs(1, CancellationToken.None);
        var getOk = getActionResult.Should().BeOfType<OkObjectResult>().Subject;
        var getJson = JsonSerializer.Serialize(getOk.Value);
        getJson.Should().NotContain(rawToken!);
        getJson.Should().Contain("0c06****12be");
    }

    [Fact]
    public async Task TC_W5_03_VerifyAndSyncGhnConfig_IdempotencyKeyReplay_ReturnsSameOutcome()
    {
        var dbName = $"{nameof(TC_W5_03_VerifyAndSyncGhnConfig_IdempotencyKeyReplay_ReturnsSameOutcome)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH-GHN-03", Name = "Kho GHN Idempotent", IsActive = true });
        await db.SaveChangesAsync();

        var shipmentServiceMock = new Mock<IShipmentService>();
        var warehouseAccessMock = new Mock<IWarehouseAuthorizationService>();

        var shipmentOps = new ShipmentOperationsService(db, configuration: BuildUserSecretsAwareConfiguration());
        var controller = new ShipmentsController(shipmentServiceMock.Object, shipmentOps, warehouseAccessMock.Object)
        {
            ControllerContext = CreateControllerContextWithPermissions(1, "Admin", AppPermissions.ShipmentUpdate, AppPermissions.ShipmentDispatch)
        };

        var input = new VerifyAndSyncGhnInput(WarehouseId: 1, ApiToken: null, Environment: "DEV_SANDBOX", ShopId: "235512");
        var firstResult = await controller.VerifyAndSyncGhnConfig(input, CancellationToken.None);
        var secondResult = await controller.VerifyAndSyncGhnConfig(input, CancellationToken.None);

        var firstOk = firstResult.Should().BeOfType<OkObjectResult>().Subject;
        var secondOk = secondResult.Should().BeOfType<OkObjectResult>().Subject;

        using var doc1 = JsonDocument.Parse(JsonSerializer.Serialize(firstOk.Value));
        using var doc2 = JsonDocument.Parse(JsonSerializer.Serialize(secondOk.Value));

        doc1.RootElement.GetProperty("shopId").GetString().Should().Be("235512");
        doc2.RootElement.GetProperty("shopId").GetString().Should().Be("235512");
        doc1.RootElement.GetProperty("gatewayMode").GetString().Should().Be("LIVE_GHN_DEV_SANDBOX");
        doc2.RootElement.GetProperty("gatewayMode").GetString().Should().Be("LIVE_GHN_DEV_SANDBOX");

        var ghnConfigs = await db.CarrierIntegrationConfigs
            .Where(x => x.WarehouseId == 1 && x.CarrierCode == "GHN")
            .ToListAsync();
        ghnConfigs.Should().ContainSingle("Repeated VerifyAndSyncGhnConfig calls must upsert the single GHN config row for the warehouse");
    }

    [Fact]
    public async Task TC_W5_04_LiveGhnDevSandbox_VerifySync_CalculateFee_And_CreateShippingOrder_EndToEnd()
    {
        var dbName = $"{nameof(TC_W5_04_LiveGhnDevSandbox_VerifySync_CalculateFee_And_CreateShippingOrder_EndToEnd)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));

        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH-GHN-LIVE", Name = "Kho Trung Tâm Quận 12", IsActive = true });
        db.Shipments.Add(new Shipment
        {
            Id = 501,
            ShipmentCode = "SHP-GHN-LIVE-501",
            WarehouseId = 1,
            Status = ShipmentStatus.Ready,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = 1
        });
        await db.SaveChangesAsync();

        var shipmentServiceMock = new Mock<IShipmentService>();
        var warehouseAccessMock = new Mock<IWarehouseAuthorizationService>();

        var shipmentOps = new ShipmentOperationsService(db, configuration: BuildUserSecretsAwareConfiguration());
        var controller = new ShipmentsController(shipmentServiceMock.Object, shipmentOps, warehouseAccessMock.Object)
        {
            ControllerContext = CreateControllerContextWithPermissions(
                1,
                "Admin",
                AppPermissions.ShipmentRead,
                AppPermissions.ShipmentUpdate,
                AppPermissions.ShipmentDispatch)
        };

        // Step 1: Verify & Sync GHN Dev Sandbox (ShopId 235512 + Master Data)
        var verifyActionResult = await controller.VerifyAndSyncGhnConfig(
            new VerifyAndSyncGhnInput(WarehouseId: 1, ApiToken: null, Environment: "DEV_SANDBOX", ShopId: "235512"),
            CancellationToken.None);
        var verifyOk = verifyActionResult.Should().BeOfType<OkObjectResult>().Subject;
        using var verifyDoc = JsonDocument.Parse(JsonSerializer.Serialize(verifyOk.Value));
        verifyDoc.RootElement.GetProperty("hasApiToken").GetBoolean().Should().BeTrue();
        verifyDoc.RootElement.GetProperty("shopId").GetString().Should().Be("235512");
        verifyDoc.RootElement.GetProperty("fromDistrictId").GetInt32().Should().Be(1454);
        verifyDoc.RootElement.GetProperty("syncedProvincesCount").GetInt32().Should().BeGreaterThan(0);
        verifyDoc.RootElement.GetProperty("gatewayMode").GetString().Should().Be("LIVE_GHN_DEV_SANDBOX");

        // Step 2: Calculate live shipping fee from Q12 (1454) to Q1 (1442)
        var feeActionResult = await controller.CalculateCarrierFee(
            new CalculateCarrierFeeInput(
                WarehouseId: 1,
                ShipmentId: 501,
                CarrierCode: "GHN",
                FromDistrictId: 1454,
                FromWardCode: "21211",
                ToDistrictId: 1442,
                ToWardCode: "20101",
                WeightGrams: 1500,
                LengthCm: 25,
                WidthCm: 20,
                HeightCm: 15,
                InsuranceValueVnd: 350000m,
                ServiceTypeId: 2),
            CancellationToken.None);
        var feeOk = feeActionResult.Should().BeOfType<OkObjectResult>().Subject;
        using var feeDoc = JsonDocument.Parse(JsonSerializer.Serialize(feeOk.Value));
        feeDoc.RootElement.GetProperty("totalFee").GetDecimal().Should().BeGreaterThan(0m);
        feeDoc.RootElement.GetProperty("gatewayMode").GetString().Should().Be("LIVE_GHN_DEV_SANDBOX");

        // Step 3: Book live carrier waybill (/v2/shipping-order/create on GHN Dev Sandbox)
        var bookActionResult = await controller.BookCarrierWaybill(
            new CarrierWaybillBookingRequest(
                WarehouseId: 1,
                ShipmentId: 501,
                CarrierCode: "GHN",
                RecipientName: "Nguyễn Văn Nhận Hàng",
                RecipientPhone: "0987654321",
                RawAddress: "72 Lê Thánh Tôn, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh",
                Province: "TP. Hồ Chí Minh",
                District: "Quận 1",
                Ward: "Phường Bến Nghé",
                WeightGrams: 1500m,
                CodAmountVnd: 350000m,
                OrderValueVnd: 350000m),
            CancellationToken.None);
        var bookOk = bookActionResult.Should().BeOfType<OkObjectResult>().Subject;
        using var bookDoc = JsonDocument.Parse(JsonSerializer.Serialize(bookOk.Value));

        var awbTrackingCode = bookDoc.RootElement.GetProperty("awbTrackingCode").GetString();
        awbTrackingCode.Should().NotBeNullOrWhiteSpace();
        bookDoc.RootElement.GetProperty("estimatedShippingFeeVnd").GetDecimal().Should().BeGreaterThan(0m);
        bookDoc.RootElement.GetProperty("gatewayMode").GetString().Should().Be("LIVE_GHN_DEV_SANDBOX");
        bookDoc.RootElement.GetProperty("shopId").GetString().Should().Be("235512");

        var updatedShipment = await db.Shipments.SingleAsync(x => x.Id == 501);
        updatedShipment.SourceCode.Should().Be(awbTrackingCode);

        var financialLedger = await db.ShipmentFinancialLedgers.SingleAsync(x => x.ShipmentId == 501);
        financialLedger.TrackingNumber.Should().Be(awbTrackingCode);
        financialLedger.EstimatedShippingFee.Should().BeGreaterThan(0m);
    }
}
