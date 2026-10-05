using ERP.Application.DTOs;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

public sealed class ReceiptInboundPlanningIntegrationTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly DbContextOptions<ErpKhoDbContext> options;

    public ReceiptInboundPlanningIntegrationTests()
    {
        connection.Open();
        options = new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlite(connection).Options;
        using var db = Create();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task Asn_receipt_post_reconciles_planning_without_inventory_side_effects()
    {
        var ids = await SeedAsync();
        await using var db = Create();
        var integration = new ReceiptInboundPlanningIntegration(db);

        var receipt = new ImportReceipt
        {
            Code = "GR-PLAN-001",
            WarehouseId = ids.WarehouseId,
            SupplierId = ids.SupplierId,
            CreatedBy = ids.UserId,
            Status = ReceiptStatus.Draft,
            Details =
            [
                new ImportReceiptDetail
                {
                    ProductId = ids.ProductId,
                    Quantity = 1,
                    ExpectedQuantity = 1,
                    OperationUnitId = ids.UnitId,
                    OperationUnitCodeSnapshot = "EA",
                    BaseUnitId = ids.UnitId,
                    BaseUnitCodeSnapshot = "EA",
                    ConversionFactor = 1,
                    ConversionVersion = 1,
                    BaseExpectedQuantity = 1,
                    QcState = ReceiptLineQcState.NoQcRequired
                }
            ]
        };
        var dto = new CreateImportReceiptDto
        {
            Code = receipt.Code,
            WarehouseId = ids.WarehouseId,
            SupplierId = ids.SupplierId,
            AsnId = ids.AsnId,
            Details =
            [
                new CreateImportReceiptDetailDto
                {
                    ProductId = ids.ProductId,
                    AsnLineId = ids.AsnLineId,
                    PurchaseOrderLineId = ids.PurchaseOrderLineId,
                    OperationUnitId = ids.UnitId,
                    ExpectedQuantity = 1
                }
            ]
        };

        await integration.AttachSourceAsync(receipt, dto);

        receipt.AsnId.Should().Be(ids.AsnId);
        receipt.PurchaseOrderId.Should().Be(ids.PurchaseOrderId);
        receipt.Details.Single().BaseExpectedQuantity.Should().Be(10);
        receipt.Details.Single().AsnLineId.Should().Be(ids.AsnLineId);

        db.ImportReceipts.Add(receipt);
        await db.SaveChangesAsync();

        await integration.MarkReceivingAsync(receipt);
        await db.SaveChangesAsync();

        (await db.Asns.AsNoTracking().SingleAsync(x => x.Id == ids.AsnId)).Status.Should().Be(AsnStatus.Receiving);
        (await db.InventoryStocks.CountAsync()).Should().Be(0);
        (await db.InventoryTransactions.CountAsync()).Should().Be(0);

        receipt.Status = ReceiptStatus.ReadyToPost;
        var detail = receipt.Details.Single();
        detail.BaseAcceptedQuantity = 10;
        await integration.ValidatePostAsync(receipt);

        detail.BasePostedQuantity = 10;
        receipt.Status = ReceiptStatus.Posted;
        await integration.ApplyPostedStateAsync(receipt, ids.UserId);
        await db.SaveChangesAsync();

        (await db.Asns.AsNoTracking().SingleAsync(x => x.Id == ids.AsnId)).Status.Should().Be(AsnStatus.Completed);
        (await db.PurchaseOrders.AsNoTracking().SingleAsync(x => x.Id == ids.PurchaseOrderId)).Status.Should().Be(PurchaseOrderStatus.Received);
        (await db.InventoryStocks.CountAsync()).Should().Be(0);
        (await db.InventoryTransactions.CountAsync()).Should().Be(0);
    }

    private async Task<(int WarehouseId,int SupplierId,int ProductId,int UnitId,int UserId,int PurchaseOrderId,int PurchaseOrderLineId,int AsnId,int AsnLineId)> SeedAsync()
    {
        await using var db = Create();
        var role = new Role { RoleName = "Manager" };
        var user = new User { Username = "receipt-plan-test", PasswordHash = "x", FullName = "Receipt Planning QA", Role = role };
        var unit = new Unit { Code = "EA", Name = "Cái", DecimalPlaces = 0 };
        var warehouse = new Warehouse { Code = "W-RPL", Name = "Kho Receipt Planning", IsActive = true };
        var supplier = new BusinessPartner { Code = "SUP-RPL", Name = "Nhà cung cấp", IsSupplier = true, IsActive = true, RowVersion = Guid.NewGuid().ToByteArray() };
        var product = new Product { Code = "P-RPL", Name = "Sản phẩm", Unit = unit, IsActive = true };
        db.AddRange(role,user,unit,warehouse,supplier,product);
        await db.SaveChangesAsync();

        var po = new PurchaseOrder
        {
            ExternalPoId = "EXT-RPL-001", SourceSystem = "ERP", Code = "PO-RPL-001",
            SupplierId = supplier.Id, WarehouseId = warehouse.Id, OrderDate = DateTime.UtcNow.Date,
            Status = PurchaseOrderStatus.Open, CreatedBy = user.Id, RowVersion = Guid.NewGuid().ToByteArray()
        };
        var poLine = new PurchaseOrderLine
        {
            PurchaseOrder = po, ExternalLineId = "10", LineNo = 1, ProductId = product.Id,
            OrderedQuantity = 10, OperationUnitId = unit.Id, OperationUnitCodeSnapshot = unit.Code,
            BaseUnitId = unit.Id, BaseUnitCodeSnapshot = unit.Code, ConversionFactorSnapshot = 1,
            ConversionVersionSnapshot = 1, BaseOrderedQuantity = 10
        };
        po.Lines.Add(poLine);
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync();

        var asn = new Asn
        {
            Code = "ASN-RPL-001", PurchaseOrderId = po.Id, SupplierId = supplier.Id, WarehouseId = warehouse.Id,
            Status = AsnStatus.Arrived, CreatedBy = user.Id, RowVersion = Guid.NewGuid().ToByteArray()
        };
        var asnLine = new AsnLine
        {
            Asn = asn, PurchaseOrderLineId = poLine.Id, LineNo = 1, ProductId = product.Id,
            ExpectedQuantity = 10, OperationUnitId = unit.Id, OperationUnitCodeSnapshot = unit.Code,
            BaseUnitId = unit.Id, BaseUnitCodeSnapshot = unit.Code, ConversionFactorSnapshot = 1,
            ConversionVersionSnapshot = 1, BaseExpectedQuantity = 10
        };
        asn.Lines.Add(asnLine);
        db.Asns.Add(asn);
        await db.SaveChangesAsync();

        return (warehouse.Id,supplier.Id,product.Id,unit.Id,user.Id,po.Id,poLine.Id,asn.Id,asnLine.Id);
    }

    private InboundPlanningTestContext Create() => new(options);
    public void Dispose() => connection.Dispose();

}
