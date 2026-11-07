using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ERP.Application.Tests;

public sealed class ReceivingDiscrepancyServiceTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly DbContextOptions<ErpKhoDbContext> options;

    public ReceivingDiscrepancyServiceTests()
    {
        connection.Open();
        options = new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlite(connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    [Fact]
    public void Submit_contract_accepts_canonical_responsible_party_name()
    {
        var dto = JsonSerializer.Deserialize<SubmitReceivingDiscrepancyDto>("""{"action":"ACCEPT_OBSERVED","reasonCode":"UNDER_RECEIPT","responsibleParty":"SUPPLIER"}""");

        dto!.ResponsibleParty.Should().Be(ResponsibleParty.Supplier);
    }

    [Fact]
    public async Task Exact_observation_routes_to_received_without_inventory_effect()
    {
        var seeded = await SeedAsync(10);
        await using var db = CreateContext();
        var service = Service(db, seeded.WarehouseId, seeded.MakerId);

        await service.ObserveAsync(seeded.ReceiptId, new ObserveReceivingDto
        {
            Lines = [new() { LineId = seeded.LineId, ObservedQuantity = 10 }]
        }, default);

        (await db.ImportReceipts.SingleAsync(x => x.Id == seeded.ReceiptId)).Status.Should().Be(ReceiptStatus.Received);
        (await db.ImportReceiptDetails.SingleAsync(x => x.Id == seeded.LineId)).FinalReceivedQuantity.Should().Be(10);
        (await db.InventoryStocks.CountAsync()).Should().Be(0);
        (await db.InventoryTransactions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Alternate_uom_observation_normalizes_operation_quantity_from_snapshotted_base_quantity()
    {
        var seeded = await SeedAsync(2);
        await using var db = CreateContext();
        var each = await db.Units.SingleAsync(x => x.Id == seeded.UnitId);
        var box = new Unit { Code = "BOX", Name = "Box", DecimalPlaces = 2 };
        db.Units.Add(box);
        await db.SaveChangesAsync();
        var line = await db.ImportReceiptDetails.SingleAsync(x => x.Id == seeded.LineId);
        line.OperationUnitId = box.Id;
        line.OperationUnitCodeSnapshot = box.Code;
        line.ConversionFactor = 12;
        line.ConversionVersion = 4;
        line.BaseExpectedQuantity = 24;
        db.ProductUoms.Add(new ProductUom { ProductId = line.ProductId, UnitId = each.Id, ConversionFactor = 1, Version = 7, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1), IsActive = true });
        await db.SaveChangesAsync();

        var result = (await Service(db, seeded.WarehouseId, seeded.MakerId).ObserveAsync(seeded.ReceiptId, new ObserveReceivingDto
        {
            Lines = [new() { LineId = seeded.LineId, ObservedQuantity = 24, ObservedUnitId = each.Id }]
        }, default)).Single();

        result.ObservedQuantity.Should().Be(24);
        result.ObservedUnitCode.Should().Be("EA");
        result.NormalizedObservedQuantity.Should().Be(2);
        result.DifferenceQuantity.Should().Be(0);
        var persisted = await db.ImportReceiptDetails.SingleAsync(x => x.Id == seeded.LineId);
        persisted.FinalReceivedQuantity.Should().Be(2);
        persisted.BaseFinalReceivedQuantity.Should().Be(24);
    }

    [Fact]
    public async Task Shortage_resolution_versions_reason_and_excludes_door_rejected_quantity()
    {
        var seeded = await SeedAsync(10);
        await using var db = CreateContext();
        db.ReceivingTolerancePolicies.RemoveRange(db.ReceivingTolerancePolicies);
        db.ReceivingTolerancePolicies.Add(new ReceivingTolerancePolicy
        {
            Id = 2, WarehouseId = seeded.WarehouseId, Version = 1, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
            AbsoluteQuantityTolerance = 10, OverageAllowed = true, ShortageAllowed = true,
            RequiresApprovalOutsideTolerance = true, IsActive = true
        });
        await db.SaveChangesAsync();
        var service = Service(db, seeded.WarehouseId, seeded.MakerId);
        var observed = await service.ObserveAsync(seeded.ReceiptId, new ObserveReceivingDto
        {
            Lines = [new() { LineId = seeded.LineId, ObservedQuantity = 8 }]
        }, default);

        var resolved = await service.SubmitAsync(seeded.ReceiptId, observed.Single().Id, new SubmitReceivingDiscrepancyDto
        {
            Action = "ACCEPT_OBSERVED", ReasonCode = "UNDER_RECEIPT", ResponsibleParty = ResponsibleParty.Supplier,
            Note = "Supplier shipped short", RowVersion = observed.Single().RowVersion
        }, default);

        resolved.Status.Should().Be(nameof(ReceivingDiscrepancyStatus.Resolved));
        resolved.Resolutions.Single().ReasonCode.Should().Be("UNDER_RECEIPT");
        (await db.ImportReceiptDetails.SingleAsync(x => x.Id == seeded.LineId)).FinalReceivedQuantity.Should().Be(8);
        (await db.InventoryStocks.CountAsync()).Should().Be(0);
        (await db.InventoryTransactions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Viewer_response_filters_private_resolution_fields()
    {
        var seeded = await SeedAsync(10);
        await using var db = CreateContext();
        var discrepancy = new ReceivingDiscrepancy
        {
            ImportReceiptId = seeded.ReceiptId, ImportReceiptDetailId = seeded.LineId, CreatedBy = seeded.MakerId,
            CreatedAtUtc = DateTime.UtcNow, Status = ReceivingDiscrepancyStatus.Resolved,
            Observations = [new() { Version = 1, ObservedQuantity = 8, BaseObservedQuantity = 8, ObservedUnitId = seeded.UnitId, ObservedUnitCodeSnapshot = "EA", ConversionFactorSnapshot = 1, ConversionVersionSnapshot = 1, CreatedBy = seeded.MakerId, CreatedAtUtc = DateTime.UtcNow }],
            Resolutions = [new() { Version = 1, Action = ReceivingResolutionAction.AcceptObserved, FinalReceivedQuantity = 8, BaseFinalReceivedQuantity = 8, ReasonCodeId = 1, ReasonCodeSnapshot = "UNDER_RECEIPT", ReasonNameSnapshot = "Nhận thiếu", ReasonCategorySnapshot = "Quantity", ReasonVersionSnapshot = 1, ReasonEffectiveAtUtcSnapshot = DateTime.UtcNow, ResponsibleParty = ResponsibleParty.Supplier, SupplierClaimRequired = true, Note = "private", EvidenceReference = "private-ref", SubmittedBy = seeded.MakerId, SubmittedAtUtc = DateTime.UtcNow }]
        };
        db.ReceivingDiscrepancies.Add(discrepancy);
        await db.SaveChangesAsync();

        var result = await Service(db, seeded.WarehouseId, seeded.MakerId, "Viewer").GetAsync(seeded.ReceiptId, default);

        result.Single().Resolutions.Single().Should().Match<ReceivingResolutionDto>(x =>
            x.Note == null && x.EvidenceReference == null && x.ResponsibleParty == "" && !x.SupplierClaimRequired);
    }

    private async Task<(int ReceiptId, int LineId, int WarehouseId, int MakerId, int UnitId)> SeedAsync(decimal expected)
    {
        await using var db = CreateContext();
        var role = new Role { RoleName = "Manager" };
        var maker = new User { Username = Guid.NewGuid().ToString("N"), PasswordHash = "x", FullName = "Maker", Role = role };
        var unit = new Unit { Code = "EA", Name = "Each", DecimalPlaces = 4 };
        var warehouse = new Warehouse { Code = Guid.NewGuid().ToString("N")[..8], Name = "QA" };
        var product = new Product { Code = Guid.NewGuid().ToString("N")[..8], Name = "Product", Unit = unit };
        db.AddRange(role, maker, warehouse, product);
        await db.SaveChangesAsync();
        var receipt = new ImportReceipt { Code = Guid.NewGuid().ToString("N"), WarehouseId = warehouse.Id, Status = ReceiptStatus.Draft, CreatedBy = maker.Id };
        var line = new ImportReceiptDetail
        {
            ImportReceipt = receipt, ProductId = product.Id, Quantity = expected, ExpectedQuantity = expected, BaseExpectedQuantity = expected,
            OperationUnitId = unit.Id, OperationUnitCodeSnapshot = unit.Code, BaseUnitId = unit.Id, BaseUnitCodeSnapshot = unit.Code,
            ConversionFactor = 1, ConversionVersion = 1, UnitPrice = 1
        };
        db.Add(line);
        await db.SaveChangesAsync();
        return (receipt.Id, line.Id, warehouse.Id, maker.Id, unit.Id);
    }

    private static ReceivingDiscrepancyService Service(ErpKhoDbContext db, int warehouseId, int userId, string role = "Manager") =>
        new(db, new WarehouseAccess(warehouseId), new Current(userId, role));

    private TestDbContext CreateContext() => new(options);

    public void Dispose() => connection.Dispose();
    private sealed record Current(int UserId, string Role) : ICurrentUser { public bool IsAuthenticated => true; public bool IsGlobalAdmin => false; }
    private sealed class TestDbContext(DbContextOptions<ErpKhoDbContext> options) : ErpKhoDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var entry in ChangeTracker.Entries<ReceivingDiscrepancy>().Where(x => x.State is EntityState.Added or EntityState.Modified))
                entry.Entity.RowVersion = Guid.NewGuid().ToByteArray();
            return base.SaveChangesAsync(cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(x => x.GetProperties()))
                if (property.GetColumnType()?.Contains("max", StringComparison.OrdinalIgnoreCase) == true) property.SetColumnType(null);
            modelBuilder.Entity<ReceivingDiscrepancy>().Property(x => x.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
        }
    }
    private sealed class WarehouseAccess(int id) : IWarehouseAuthorizationService
    {
        public Task<IReadOnlyList<int>> GetAccessibleWarehouseIdsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<int>>([id]);
        public Task<bool> CanAccessWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default) => Task.FromResult(warehouseId == id);
        public Task EnsureWarehouseAccessAsync(int warehouseId, CancellationToken cancellationToken = default) => warehouseId == id ? Task.CompletedTask : throw new UnauthorizedAccessException();
    }
}
