using System.Data;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.QaSeed;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
if (string.IsNullOrWhiteSpace(environment))
    environment = "Development";
QaSeedPolicy.EnsureDevelopment(environment);

var revokeQaSessions = args.Contains("--revoke-qa-sessions", StringComparer.Ordinal);
var applySeed = args.Contains("--apply", StringComparer.Ordinal) || (!revokeQaSessions && args.Length == 0);
if (applySeed == revokeQaSessions)
    throw new InvalidOperationException("Specify exactly one operation: --apply or --revoke-qa-sessions.");

var configuredConnection = Environment.GetEnvironmentVariable("ERP_KHO_QA_SEED_CONNECTION");
var candidateConnections = new List<string>();
if (!string.IsNullOrWhiteSpace(configuredConnection))
{
    QaSeedPolicy.ValidateLocalConnection(configuredConnection);
    candidateConnections.Add(configuredConnection);
}

if (applySeed)
{
    string[] defaultLocalConnections =
    [
        @"Server=localhost;Database=ERP_KHO;Trusted_Connection=True;TrustServerCertificate=True",
        @"Server=.\SQLEXPRESS;Database=ERP_KHO;Trusted_Connection=True;TrustServerCertificate=True",
        @"Server=(localdb)\MSSQLLocalDB;Database=ERP_KHO;Trusted_Connection=True;TrustServerCertificate=True"
    ];
    foreach (var candidate in defaultLocalConnections)
    {
        QaSeedPolicy.ValidateLocalConnection(candidate);
        var builder = new SqlConnectionStringBuilder(candidate);
        if (!candidateConnections.Any(existing =>
            string.Equals(new SqlConnectionStringBuilder(existing).DataSource, builder.DataSource, StringComparison.OrdinalIgnoreCase)))
        {
            if (await CanConnectToLocalErpKhoAsync(candidate))
                candidateConnections.Add(candidate);
        }
    }
}

if (candidateConnections.Count == 0)
    throw new InvalidOperationException("QA seed connection is unavailable.");

if (revokeQaSessions)
{
    var revokeOptions = new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(candidateConnections[0]).Options;
    await using var revokeContext = new ErpKhoDbContext(revokeOptions);
    await RevokeQaSessionsAsync(revokeContext);
    return;
}

foreach (var connStr in candidateConnections)
{
    var ds = new SqlConnectionStringBuilder(connStr).DataSource;
    var options = new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(connStr).Options;
    await using var context = new ErpKhoDbContext(options);
    var pendingMigrations = (await context.Database.GetPendingMigrationsAsync()).ToList();
    if (pendingMigrations.Count > 0)
    {
        Console.WriteLine($"[{ds}] Applying {pendingMigrations.Count} pending migration(s)...");
        await context.Database.MigrateAsync();
    }

    await ApplyQaSeedToDatabaseAsync(context, ds);
}

static async Task<bool> CanConnectToLocalErpKhoAsync(string connectionString)
{
    try
    {
        var probeBuilder = new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = 3 };
        await using var sqlConn = new SqlConnection(probeBuilder.ConnectionString);
        await sqlConn.OpenAsync();
        await using var cmd = sqlConn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Warehouses'";
        var tableExists = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        return tableExists;
    }
    catch
    {
        return false;
    }
}

static async Task ApplyQaSeedToDatabaseAsync(ErpKhoDbContext context, string dataSource)
{
    await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

    var roleNames = new[] { "Admin", "Manager", "WarehouseStaff", "Viewer" };
    var roles = new Dictionary<string, Role>(StringComparer.Ordinal);
    foreach (var roleName in roleNames)
    {
        var matches = await context.Roles.IgnoreQueryFilters().Where(role => role.RoleName == roleName).ToListAsync();
        var role = matches.FirstOrDefault() ?? new Role { RoleName = roleName, Description = $"Local QA role: {roleName}" };
        if (role.Id == 0) context.Roles.Add(role);
        roles.Add(roleName, role);
    }
    await context.SaveChangesAsync();

    var primaryWarehouse = await context.Warehouses.IgnoreQueryFilters()
        .OrderBy(w => w.Id)
        .FirstOrDefaultAsync(w => w.Code == "WH01" || w.Code == "QA-HCM" || w.IsActive);
    if (primaryWarehouse is null)
    {
        primaryWarehouse = new Warehouse
        {
            Code = "WH01",
            Name = "Kho Chính TP.HCM",
            Address = "Quận 7, TP.HCM",
            IsActive = true
        };
        context.Warehouses.Add(primaryWarehouse);
        await context.SaveChangesAsync();
    }

    var wh01 = await context.Warehouses.IgnoreQueryFilters().FirstOrDefaultAsync(warehouse => warehouse.Code == "WH01") ?? primaryWarehouse;
    var wh02 = await context.Warehouses.IgnoreQueryFilters().FirstOrDefaultAsync(warehouse => warehouse.Code == "WH02" || warehouse.Code == "QA-HN");
    if (wh02 is null)
    {
        wh02 = new Warehouse
        {
            Code = "WH02",
            Name = "Kho QA ngoài phạm vi",
            Address = "Local QA only",
            IsActive = true
        };
        context.Warehouses.Add(wh02);
        await context.SaveChangesAsync();
    }

    var hasher = new PasswordHasherService();
    var qaAdmin = await context.Users.IgnoreQueryFilters()
        .OrderBy(u => u.Id)
        .FirstOrDefaultAsync(u => u.Username == "qa_admin" && u.IsActive);
    if (qaAdmin is null)
    {
        qaAdmin = new User
        {
            Username = "qa_admin",
            FullName = "QA Admin",
            Role = roles["Admin"],
            PasswordHash = hasher.HashPassword("QaAdmin@123456!"),
            IsActive = true
        };
        context.Users.Add(qaAdmin);
        await context.SaveChangesAsync();
    }

    var definitions = new[]
    {
        new AccountDefinition("qa_admin_local", "QA Local Admin", roles["Admin"], false),
        new AccountDefinition("qa_manager_wh01", "QA Manager WH01", roles["Manager"], true),
        new AccountDefinition("qa_staff_wh01", "QA Warehouse Staff WH01", roles["WarehouseStaff"], true),
        new AccountDefinition("qa_viewer_wh01", "QA Viewer WH01", roles["Viewer"], true)
    };
    var users = new Dictionary<string, User>(StringComparer.Ordinal);

    foreach (var definition in definitions)
    {
        var matches = await context.Users.IgnoreQueryFilters().Where(user => user.Username == definition.Username).ToListAsync();
        if (matches.Count > 1) throw new InvalidOperationException($"Duplicate QA user detected: {definition.Username}.");
        var user = matches.SingleOrDefault();
        if (user is null)
        {
            var password = Console.IsInputRedirected ? $"QaSeed!{definition.Username}#2026" : "QaSeed!LocalOnly#2026";
            user = new User
            {
                Username = definition.Username,
                FullName = definition.FullName,
                Role = definition.Role,
                PasswordHash = hasher.HashPassword(password),
                IsActive = true
            };
            context.Users.Add(user);
        }
        users.Add(definition.Username, user);
    }
    await context.SaveChangesAsync();

    var admin = users["qa_admin_local"];
    if (!await context.UserWarehouses.IgnoreQueryFilters().AnyAsync(access => access.UserId == qaAdmin.Id && access.WarehouseId == primaryWarehouse.Id))
    {
        context.UserWarehouses.Add(new UserWarehouse { UserId = qaAdmin.Id, WarehouseId = primaryWarehouse.Id, CreatedBy = qaAdmin.Id });
    }

    foreach (var definition in definitions.Where(item => item.AssignWh01))
    {
        var user = users[definition.Username];
        if (!await context.UserWarehouses.IgnoreQueryFilters().AnyAsync(access => access.UserId == user.Id && access.WarehouseId == wh01.Id))
            context.UserWarehouses.Add(new UserWarehouse { UserId = user.Id, WarehouseId = wh01.Id, CreatedBy = admin.Id });
    }
    await context.SaveChangesAsync();

    // 1. Clean up BusinessPartner names with concatenated "ViệtQA" -> "Việt QA"
    var allPartners = await context.BusinessPartners.IgnoreQueryFilters().ToListAsync();
    var cleanedPartnersCount = 0;
    foreach (var partner in allPartners)
    {
        if (partner.Name.Contains("ViệtQA", StringComparison.Ordinal) || partner.Name.Contains("VietQA", StringComparison.Ordinal))
        {
            partner.Name = partner.Name
                .Replace("ViệtQA", "Việt QA", StringComparison.Ordinal)
                .Replace("VietQA", "Viet QA", StringComparison.Ordinal);
            partner.UpdatedAt = DateTime.UtcNow;
            cleanedPartnersCount++;
        }
    }

    var receiptsWithDirtySnapshot = await context.ImportReceipts.IgnoreQueryFilters()
        .Where(r => r.SupplierNameSnapshot != null &&
                    (r.SupplierNameSnapshot.Contains("ViệtQA") || r.SupplierNameSnapshot.Contains("VietQA")))
        .ToListAsync();
    foreach (var r in receiptsWithDirtySnapshot)
    {
        r.SupplierNameSnapshot = r.SupplierNameSnapshot!
            .Replace("ViệtQA", "Việt QA", StringComparison.Ordinal)
            .Replace("VietQA", "Viet QA", StringComparison.Ordinal);
    }

    var supplier = allPartners.FirstOrDefault(p => p.IsSupplier && p.IsActive);
    if (supplier is null)
    {
        supplier = new BusinessPartner
        {
            Code = "NCC-QA-01",
            Name = "Công ty TNHH Thép & Thiết Bị Công Nghiệp Việt QA",
            IsSupplier = true,
            IsCustomer = false,
            IsActive = true,
            Phone = "0909123456",
            Email = "supplier@qa.local",
            Address = "KCN Tân Bình, TP.HCM"
        };
        context.BusinessPartners.Add(supplier);
    }

    var customer = allPartners.FirstOrDefault(p => p.IsCustomer && p.IsActive);
    if (customer is null)
    {
        customer = new BusinessPartner
        {
            Code = "KH-QA-01",
            Name = "Tập đoàn Phân Phối & Logistics Đông Á QA",
            IsSupplier = false,
            IsCustomer = true,
            IsActive = true,
            Phone = "0909888999",
            Email = "customer@qa.local",
            Address = "Quận 1, TP.HCM"
        };
        context.BusinessPartners.Add(customer);
    }
    await context.SaveChangesAsync();

    // 2. Ensure RECEIVING and storage bin BIN-A01-01 exist in the default warehouse
    var receivingLocation = await context.WarehouseLocations.IgnoreQueryFilters()
        .FirstOrDefaultAsync(l => l.WarehouseId == primaryWarehouse.Id && l.Code == "RECEIVING");
    if (receivingLocation is null)
    {
        receivingLocation = new WarehouseLocation
        {
            WarehouseId = primaryWarehouse.Id,
            Code = "RECEIVING",
            Name = "Vị trí nhận hàng",
            LocationType = WarehouseLocationType.Receiving,
            IsActive = true,
            IsBlocked = false,
            IsPickable = false,
            IsReceivable = true,
            IsSystemManaged = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = qaAdmin.Id
        };
        context.WarehouseLocations.Add(receivingLocation);
        await context.SaveChangesAsync();
    }

    var storageBin = await context.WarehouseLocations.IgnoreQueryFilters()
        .FirstOrDefaultAsync(l => l.WarehouseId == primaryWarehouse.Id && l.Code == "BIN-A01-01");
    if (storageBin is null)
    {
        storageBin = new WarehouseLocation
        {
            WarehouseId = primaryWarehouse.Id,
            Code = "BIN-A01-01",
            Name = "Kệ lưu trữ A01-01",
            StructurePath = "ZONE-A/AISLE-01/RACK-01/SHELF-01/BIN-01",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsBlocked = false,
            IsPickable = true,
            IsReceivable = false,
            IsSystemManaged = false,
            MapX = 10m,
            MapY = 10m,
            MapWidth = 20m,
            MapHeight = 15m,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = qaAdmin.Id
        };
        context.WarehouseLocations.Add(storageBin);
        await context.SaveChangesAsync();
    }

    // 3. Resolve Unit and Product for QA operational tasks
    var unit = await context.Units.IgnoreQueryFilters().OrderBy(u => u.Id).FirstOrDefaultAsync(u => u.IsActive);
    if (unit is null)
    {
        unit = new Unit { Code = "QA-PCS", Name = "QA Cái", DecimalPlaces = 0, IsActive = true };
        context.Units.Add(unit);
        await context.SaveChangesAsync();
    }

    var product = await context.Products.IgnoreQueryFilters().OrderBy(p => p.Id).FirstOrDefaultAsync(p => p.IsActive);
    if (product is null)
    {
        product = new Product
        {
            Code = "QA-LAPTOP",
            Name = "QA Laptop văn phòng",
            UnitId = unit.Id,
            Description = "Laptop dùng cho kiểm thử ERP",
            IsActive = true
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();
    }

    // 4. Idempotent PutawayTask ("PUT-QA-001") in Open status
    var putawayReceipt = await context.ImportReceipts.IgnoreQueryFilters()
        .Include(r => r.Details)
        .FirstOrDefaultAsync(r => r.Code == "PUT-QA-001");
    if (putawayReceipt is null)
    {
        putawayReceipt = new ImportReceipt
        {
            Code = "PUT-QA-001",
            WarehouseId = primaryWarehouse.Id,
            SupplierId = supplier.Id,
            SupplierCodeSnapshot = supplier.Code,
            SupplierNameSnapshot = supplier.Name,
            Status = ReceiptStatus.Posted,
            Note = "Phiếu nhập mẫu phục vụ nhiệm vụ cất hàng PUT-QA-001",
            CreatedBy = qaAdmin.Id,
            CreatedAt = DateTime.UtcNow,
            ApprovedBy = qaAdmin.Id,
            ApprovedAt = DateTime.UtcNow,
            Details =
            [
                new ImportReceiptDetail
                {
                    ProductId = product.Id,
                    Quantity = 10m,
                    UnitPrice = 1000m,
                    Note = "Dòng hàng mẫu cần cất từ RECEIVING sang BIN-A01-01",
                    OperationUnitId = unit.Id,
                    OperationUnitCodeSnapshot = unit.Code,
                    OperationUnitDecimalPlaces = unit.DecimalPlaces,
                    BaseUnitId = unit.Id,
                    BaseUnitCodeSnapshot = unit.Code,
                    BaseUnitDecimalPlaces = unit.DecimalPlaces,
                    ConversionFactor = 1m,
                    ConversionVersion = 1,
                    ExpectedQuantity = 10m,
                    ReceivedQuantity = 10m,
                    ObservedQuantity = 10m,
                    FinalReceivedQuantity = 10m,
                    AcceptedQuantity = 10m,
                    PostedQuantity = 10m,
                    BaseExpectedQuantity = 10m,
                    BaseReceivedQuantity = 10m,
                    BaseObservedQuantity = 10m,
                    BaseFinalReceivedQuantity = 10m,
                    BaseAcceptedQuantity = 10m
                }
            ]
        };
        context.ImportReceipts.Add(putawayReceipt);
        await context.SaveChangesAsync();
    }

    var putawayReceiptLine = putawayReceipt.Details.First();
    var receivingStock = await context.InventoryStocks.IgnoreQueryFilters()
        .FirstOrDefaultAsync(s =>
            s.WarehouseId == primaryWarehouse.Id &&
            s.ProductId == product.Id &&
            s.LocationId == receivingLocation.Id &&
            s.Status == InventoryStatus.Available &&
            s.LotId == null &&
            s.SerialId == null);
    if (receivingStock is null)
    {
        receivingStock = new InventoryStock
        {
            WarehouseId = primaryWarehouse.Id,
            ProductId = product.Id,
            LocationId = receivingLocation.Id,
            Status = InventoryStatus.Available,
            Quantity = 10m,
            ReservedQuantity = 0m,
            LastUpdated = DateTime.UtcNow
        };
        context.InventoryStocks.Add(receivingStock);
    }
    else if (receivingStock.Quantity - receivingStock.ReservedQuantity < 10m)
    {
        receivingStock.Quantity = receivingStock.ReservedQuantity + 10m;
        receivingStock.LastUpdated = DateTime.UtcNow;
    }

    if (!await context.InventoryTransactions.IgnoreQueryFilters().AnyAsync(t =>
        t.ReferenceType == "ImportReceipt" &&
        t.ReferenceId == putawayReceipt.Id &&
        t.ProductId == product.Id &&
        t.LocationId == receivingLocation.Id))
    {
        context.InventoryTransactions.Add(new InventoryTransaction
        {
            ProductId = product.Id,
            WarehouseId = primaryWarehouse.Id,
            LocationId = receivingLocation.Id,
            InventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Import,
            Quantity = 10m,
            ReferenceId = putawayReceipt.Id,
            ReferenceType = "ImportReceipt",
            Note = "Tồn kho tại RECEIVING cho nhiệm vụ cất hàng PUT-QA-001",
            TransactionDate = DateTime.UtcNow,
            CreatedBy = qaAdmin.Id
        });
    }
    await context.SaveChangesAsync();

    var putawayTask = await context.PutawayTasks.IgnoreQueryFilters()
        .Include(t => t.Items)
        .FirstOrDefaultAsync(t => t.ReceiptId == putawayReceipt.Id);
    if (putawayTask is null)
    {
        putawayTask = new PutawayTask
        {
            ReceiptId = putawayReceipt.Id,
            WarehouseId = primaryWarehouse.Id,
            Status = PutawayTaskStatus.Open,
            AssignedUserId = qaAdmin.Id,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = qaAdmin.Id,
            Items =
            [
                new PutawayTaskItem
                {
                    ReceiptLineId = putawayReceiptLine.Id,
                    ProductId = product.Id,
                    InventoryStatus = InventoryStatus.Available,
                    SourceLocationId = receivingLocation.Id,
                    OperationUnitId = unit.Id,
                    OperationUnitCodeSnapshot = unit.Code,
                    BaseUnitId = unit.Id,
                    BaseUnitCodeSnapshot = unit.Code,
                    ConversionFactorSnapshot = 1m,
                    ConversionVersionSnapshot = 1,
                    BaseUnitDecimalPlaces = unit.DecimalPlaces,
                    RequiredOperationQuantity = 10m,
                    RequiredBaseQuantity = 10m,
                    MovedBaseQuantity = 0m
                }
            ]
        };
        context.PutawayTasks.Add(putawayTask);
    }
    else
    {
        if (putawayTask.Status is not (PutawayTaskStatus.Open or PutawayTaskStatus.Assigned))
        {
            putawayTask.Status = PutawayTaskStatus.Open;
            putawayTask.StartedAt = null;
            putawayTask.CompletedAt = null;
            putawayTask.CancelledAt = null;
            putawayTask.ExceptionReason = null;
        }
        putawayTask.AssignedUserId ??= qaAdmin.Id;
        var item = putawayTask.Items.FirstOrDefault();
        if (item is null)
        {
            putawayTask.Items.Add(new PutawayTaskItem
            {
                ReceiptLineId = putawayReceiptLine.Id,
                ProductId = product.Id,
                InventoryStatus = InventoryStatus.Available,
                SourceLocationId = receivingLocation.Id,
                OperationUnitId = unit.Id,
                OperationUnitCodeSnapshot = unit.Code,
                BaseUnitId = unit.Id,
                BaseUnitCodeSnapshot = unit.Code,
                ConversionFactorSnapshot = 1m,
                ConversionVersionSnapshot = 1,
                BaseUnitDecimalPlaces = unit.DecimalPlaces,
                RequiredOperationQuantity = 10m,
                RequiredBaseQuantity = 10m,
                MovedBaseQuantity = 0m
            });
        }
        else if (item.MovedBaseQuantity >= item.RequiredBaseQuantity)
        {
            item.MovedBaseQuantity = 0m;
        }
    }
    await context.SaveChangesAsync();

    // 5. Idempotent PickingTask ("PICK-QA-001") in Open status
    var storageStock = await context.InventoryStocks.IgnoreQueryFilters()
        .FirstOrDefaultAsync(s =>
            s.WarehouseId == primaryWarehouse.Id &&
            s.ProductId == product.Id &&
            s.LocationId == storageBin.Id &&
            s.Status == InventoryStatus.Available &&
            s.LotId == null &&
            s.SerialId == null);
    if (storageStock is null)
    {
        storageStock = new InventoryStock
        {
            WarehouseId = primaryWarehouse.Id,
            ProductId = product.Id,
            LocationId = storageBin.Id,
            Status = InventoryStatus.Available,
            Quantity = 25m,
            ReservedQuantity = 5m,
            LastUpdated = DateTime.UtcNow
        };
        context.InventoryStocks.Add(storageStock);
    }
    else
    {
        if (storageStock.ReservedQuantity < 5m)
            storageStock.ReservedQuantity = 5m;
        if (storageStock.Quantity < storageStock.ReservedQuantity + 15m)
            storageStock.Quantity = storageStock.ReservedQuantity + 15m;
        storageStock.LastUpdated = DateTime.UtcNow;
    }
    await context.SaveChangesAsync();

    var salesOrder = await context.SalesOrders.IgnoreQueryFilters()
        .Include(o => o.Lines)
        .FirstOrDefaultAsync(o => o.OrderCode == "SO-QA-001");
    if (salesOrder is null)
    {
        salesOrder = new SalesOrder
        {
            OrderCode = "SO-QA-001",
            ExternalOrderId = "EXT-SO-QA-001",
            CustomerId = customer.Id,
            WarehouseId = primaryWarehouse.Id,
            RequestedShipDate = DateTime.UtcNow.Date.AddDays(2),
            Priority = 2,
            ShippingMethod = "STANDARD",
            Status = SalesOrderStatus.Released,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = qaAdmin.Id,
            ReleasedAt = DateTime.UtcNow,
            Lines =
            [
                new SalesOrderLine
                {
                    ExternalLineId = "EXT-SO-QA-001-L1",
                    ProductId = product.Id,
                    OrderedQuantity = 5m,
                    CancelledQuantity = 0m,
                    UomCode = unit.Code,
                    UnitPrice = 1200m
                }
            ]
        };
        context.SalesOrders.Add(salesOrder);
        await context.SaveChangesAsync();
    }

    var pickReservation = await context.StockReservations.IgnoreQueryFilters()
        .FirstOrDefaultAsync(r => r.ReservationCode == "RSV-QA-PICK-001" ||
                                  (r.SourceType == "SalesOrder" && r.SourceId == salesOrder.Id && r.ProductId == product.Id));
    if (pickReservation is null)
    {
        pickReservation = new StockReservation
        {
            ReservationCode = "RSV-QA-PICK-001",
            ProductId = product.Id,
            WarehouseId = primaryWarehouse.Id,
            Quantity = 5m,
            AllocatedQuantity = 5m,
            ConsumedQuantity = 0m,
            ReleasedQuantity = 0m,
            Status = StockReservationStatus.Allocated,
            SourceType = "SalesOrder",
            SourceId = salesOrder.Id,
            SourceCode = salesOrder.OrderCode,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = qaAdmin.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            AllocationVersion = 1
        };
        context.StockReservations.Add(pickReservation);
        await context.SaveChangesAsync();
    }

    var pickAllocation = await context.StockAllocations.IgnoreQueryFilters()
        .FirstOrDefaultAsync(a => a.AllocationCode == "ALC-QA-PICK-001" || a.ReservationId == pickReservation.Id);
    if (pickAllocation is null)
    {
        pickAllocation = new StockAllocation
        {
            AllocationCode = "ALC-QA-PICK-001",
            ReservationId = pickReservation.Id,
            WarehouseId = primaryWarehouse.Id,
            ProductId = product.Id,
            LocationId = storageBin.Id,
            InventoryStatus = InventoryStatus.Available,
            Quantity = 5m,
            Status = StockAllocationStatus.Active,
            Strategy = AllocationStrategy.LocationOrder,
            SelectionReason = "QA seed allocation at BIN-A01-01",
            AllocatedAt = DateTime.UtcNow,
            AllocatedBy = qaAdmin.Id,
            Version = 1
        };
        context.StockAllocations.Add(pickAllocation);
        await context.SaveChangesAsync();
    }

    var pickingTask = await context.PickingTasks.IgnoreQueryFilters()
        .Include(t => t.Lines)
        .FirstOrDefaultAsync(t => t.TaskCode == "PICK-QA-001" ||
                                  (t.SourceType == "SalesOrder" && t.SourceId == salesOrder.Id));
    if (pickingTask is null)
    {
        pickingTask = new PickingTask
        {
            TaskCode = "PICK-QA-001",
            SourceType = "SalesOrder",
            SourceId = salesOrder.Id,
            SourceCode = salesOrder.OrderCode,
            WarehouseId = primaryWarehouse.Id,
            PickingType = "SingleOrder",
            Status = PickingTaskStatus.Open,
            Priority = 2,
            AssignedUserId = qaAdmin.Id,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = qaAdmin.Id,
            Lines =
            [
                new PickingTaskLine
                {
                    Sequence = 1,
                    AllocationId = pickAllocation.Id,
                    ProductId = product.Id,
                    SourceLocationId = storageBin.Id,
                    RequestedQuantity = 5m,
                    PickedQuantity = 0m,
                    Status = PickingTaskLineStatus.Open
                }
            ]
        };
        context.PickingTasks.Add(pickingTask);
    }
    else if (pickingTask.Status is not (PickingTaskStatus.Open or PickingTaskStatus.Assigned))
    {
        pickingTask.Status = PickingTaskStatus.Open;
        pickingTask.StartedAt = null;
        pickingTask.CompletedAt = null;
        foreach (var line in pickingTask.Lines)
        {
            line.PickedQuantity = 0m;
            line.Status = PickingTaskLineStatus.Open;
        }
    }

    // 6. Idempotent PartnerDebtInvoices ("AP-IR-QA0001" and "AR-ER-QA0001") & PartnerDebtLedgers
    var nowUtc = DateTime.UtcNow;
    var apInvoice = await context.PartnerDebtInvoices.IgnoreQueryFilters()
        .FirstOrDefaultAsync(x => x.Code == "AP-IR-QA0001");
    if (apInvoice is null)
    {
        apInvoice = new PartnerDebtInvoice
        {
            Code = "AP-IR-QA0001",
            PartnerId = supplier.Id,
            DebtType = PartnerDebtType.AP,
            SourceType = "ImportReceipt",
            SourceId = putawayReceipt.Id,
            SourceCode = "IR-QA-001",
            Currency = "VND",
            SubTotalAmount = 25_000_000m,
            TaxAmount = 2_500_000m,
            TotalAmount = 27_500_000m,
            PaidAmount = 0m,
            RemainingAmount = 27_500_000m,
            Status = PartnerDebtInvoiceStatus.Open,
            IssuedAtUtc = nowUtc.AddDays(-5),
            DueDateUtc = nowUtc.Date.AddDays(25),
            Note = "Công nợ phải trả từ phiếu nhập kho IR-QA-001",
            CreatedBy = qaAdmin.Id,
            CreatedAtUtc = nowUtc.AddDays(-5)
        };
        context.PartnerDebtInvoices.Add(apInvoice);
        await context.SaveChangesAsync();
    }
    else
    {
        apInvoice.SourceType = "ImportReceipt";
        apInvoice.SourceCode = "IR-QA-001";
        apInvoice.SubTotalAmount = 25_000_000m;
        apInvoice.TaxAmount = 2_500_000m;
        apInvoice.TotalAmount = 27_500_000m;
        if (apInvoice.Status == PartnerDebtInvoiceStatus.Paid)
        {
            apInvoice.PaidAmount = 0m;
            apInvoice.RemainingAmount = 27_500_000m;
            apInvoice.Status = PartnerDebtInvoiceStatus.Open;
        }
    }

    if (!await context.PartnerDebtLedgers.IgnoreQueryFilters().AnyAsync(l => l.InvoiceId == apInvoice.Id))
    {
        context.PartnerDebtLedgers.Add(new PartnerDebtLedger
        {
            PartnerId = supplier.Id,
            InvoiceId = apInvoice.Id,
            DebtType = PartnerDebtType.AP,
            TransactionType = PartnerDebtTransactionType.InvoiceCreated,
            AmountDelta = 27_500_000m,
            BalanceAfter = 27_500_000m,
            ReferenceType = "ImportReceipt",
            ReferenceId = putawayReceipt.Id,
            ReferenceCode = "IR-QA-001",
            Note = "Ghi nhận công nợ phải trả từ phiếu nhập kho IR-QA-001",
            CreatedBy = qaAdmin.Id,
            CreatedAtUtc = nowUtc.AddDays(-5)
        });
    }

    var arInvoice = await context.PartnerDebtInvoices.IgnoreQueryFilters()
        .FirstOrDefaultAsync(x => x.Code == "AR-ER-QA0001");
    if (arInvoice is null)
    {
        arInvoice = new PartnerDebtInvoice
        {
            Code = "AR-ER-QA0001",
            PartnerId = customer.Id,
            DebtType = PartnerDebtType.AR,
            SourceType = "ExportReceipt",
            SourceId = salesOrder.Id,
            SourceCode = "ER-QA-001",
            Currency = "VND",
            SubTotalAmount = 18_000_000m,
            TaxAmount = 1_800_000m,
            TotalAmount = 19_800_000m,
            PaidAmount = 5_000_000m,
            RemainingAmount = 14_800_000m,
            Status = PartnerDebtInvoiceStatus.PartiallyPaid,
            IssuedAtUtc = nowUtc.AddDays(-4),
            DueDateUtc = nowUtc.Date.AddDays(26),
            Note = "Công nợ phải thu từ phiếu xuất kho ER-QA-001",
            CreatedBy = qaAdmin.Id,
            CreatedAtUtc = nowUtc.AddDays(-4)
        };
        context.PartnerDebtInvoices.Add(arInvoice);
        await context.SaveChangesAsync();
    }
    else
    {
        arInvoice.SourceType = "ExportReceipt";
        arInvoice.SourceCode = "ER-QA-001";
        arInvoice.SubTotalAmount = 18_000_000m;
        arInvoice.TaxAmount = 1_800_000m;
        arInvoice.TotalAmount = 19_800_000m;
        if (arInvoice.Status == PartnerDebtInvoiceStatus.Paid)
        {
            arInvoice.PaidAmount = 5_000_000m;
            arInvoice.RemainingAmount = 14_800_000m;
            arInvoice.Status = PartnerDebtInvoiceStatus.PartiallyPaid;
        }
    }

    if (!await context.PartnerDebtLedgers.IgnoreQueryFilters().AnyAsync(l => l.InvoiceId == arInvoice.Id))
    {
        context.PartnerDebtLedgers.AddRange(
            new PartnerDebtLedger
            {
                PartnerId = customer.Id,
                InvoiceId = arInvoice.Id,
                DebtType = PartnerDebtType.AR,
                TransactionType = PartnerDebtTransactionType.InvoiceCreated,
                AmountDelta = 19_800_000m,
                BalanceAfter = 19_800_000m,
                ReferenceType = "ExportReceipt",
                ReferenceId = salesOrder.Id,
                ReferenceCode = "ER-QA-001",
                Note = "Ghi nhận công nợ phải thu từ phiếu xuất kho ER-QA-001",
                CreatedBy = qaAdmin.Id,
                CreatedAtUtc = nowUtc.AddDays(-4)
            },
            new PartnerDebtLedger
            {
                PartnerId = customer.Id,
                InvoiceId = arInvoice.Id,
                DebtType = PartnerDebtType.AR,
                TransactionType = PartnerDebtTransactionType.Payment,
                AmountDelta = -5_000_000m,
                BalanceAfter = 14_800_000m,
                ReferenceType = "DebtPayment",
                ReferenceId = arInvoice.Id,
                ReferenceCode = "UNC-VCB-QA001",
                PaymentMethod = "BANK_TRANSFER",
                Note = "Thu tiền đợt 1 qua chuyển khoản ngân hàng",
                CreatedBy = qaAdmin.Id,
                CreatedAtUtc = nowUtc.AddDays(-2)
            });
    }

    if (supplier.PayableBalance < apInvoice.RemainingAmount)
        supplier.PayableBalance = apInvoice.RemainingAmount;
    if (customer.ReceivableBalance < arInvoice.RemainingAmount)
        customer.ReceivableBalance = arInvoice.RemainingAmount;

    context.AuditLogs.Add(new AuditLog
    {
        UserId = qaAdmin.Id,
        Action = "QA.IamAndOperationalSeedApplied",
        EntityName = "LocalQaOperationalSeed",
        NewValues = $"Warehouse={primaryWarehouse.Code};PutawayTask=PUT-QA-001({putawayTask.Status});PickingTask={pickingTask.TaskCode}({pickingTask.Status});AP={apInvoice.Code};AR={arInvoice.Code};CleanedPartners={cleanedPartnersCount}",
        Timestamp = DateTime.UtcNow,
        IpAddress = "local-cli"
    });
    await context.SaveChangesAsync();
    await transaction.CommitAsync();

    Console.WriteLine($"[{dataSource}] Local ERP_KHO QA seed completed: PutawayTask=PUT-QA-001 ({putawayTask.Status}), PickingTask={pickingTask.TaskCode} ({pickingTask.Status}), AP={apInvoice.Code}, AR={arInvoice.Code}, CleanedPartners={cleanedPartnersCount}.");
}

static async Task RevokeQaSessionsAsync(ErpKhoDbContext context)
{
    string[] usernames = ["qa_admin_local", "qa_manager_wh01", "qa_staff_wh01", "qa_viewer_wh01"];
    var users = await context.Users.Include(x => x.Role)
        .Where(x => usernames.Contains(x.Username))
        .ToDictionaryAsync(x => x.Username, StringComparer.Ordinal);
    if (users.Count != usernames.Length || usernames.Any(username => !users.ContainsKey(username)))
        throw new InvalidOperationException("The exact four local QA accounts must exist before session revocation.");

    var admin = users["qa_admin_local"];
    if (!string.Equals(admin.Role.RoleName, "Admin", StringComparison.Ordinal))
        throw new InvalidOperationException("qa_admin_local is not a global Admin account.");

    var before = await CaptureInventoryInvariantAsync(context);
    var activeBefore = await context.UserSessions.AsNoTracking()
        .Where(x => users.Values.Select(user => user.Id).Contains(x.UserId) && x.RevokedAt == null)
        .GroupBy(x => x.UserId).Select(group => new { group.Key, Count = group.Count() })
        .ToDictionaryAsync(x => x.Key, x => x.Count);

    var currentUser = new LocalQaAdminCurrentUser(admin.Id);
    var tokenService = new TokenService(new ConfigurationBuilder().Build());
    var service = new UserSessionService(context, tokenService, currentUser, new SessionSecurityOptions());
    await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
    foreach (var username in usernames)
        await service.RevokeUserSessionsAsAdminAsync(users[username].Id,
            (await new AccountAdminService(context, currentUser).GetAsync(users[username].Id)).RowVersion);
    await transaction.CommitAsync();

    var activeAfter = await context.UserSessions.AsNoTracking()
        .Where(x => users.Values.Select(user => user.Id).Contains(x.UserId) && x.RevokedAt == null)
        .CountAsync();
    var after = await CaptureInventoryInvariantAsync(context);
    if (activeAfter != 0) throw new InvalidOperationException("One or more QA sessions remain active after revocation.");
    if (before != after) throw new InvalidOperationException("Inventory or ledger invariant changed during QA session revocation.");

    foreach (var username in usernames)
        Console.WriteLine($"{username}: active sessions {activeBefore.GetValueOrDefault(users[username].Id)} -> 0");
    Console.WriteLine("Local ERP_KHO QA sessions revoked through UserSessionService; inventory and ledger invariants are unchanged.");
}

static async Task<InventoryInvariant> CaptureInventoryInvariantAsync(ErpKhoDbContext context)
{
    var stock = await context.InventoryStocks.AsNoTracking().GroupBy(_ => 1)
        .Select(group => new { OnHand = group.Sum(x => x.Quantity), Reserved = group.Sum(x => x.ReservedQuantity) })
        .SingleOrDefaultAsync();
    return new InventoryInvariant(
        stock?.OnHand ?? 0m,
        stock?.Reserved ?? 0m,
        await context.InventoryTransactions.AsNoTracking().CountAsync(),
        await context.StockReservations.AsNoTracking().CountAsync(),
        await context.ImportReceipts.AsNoTracking().CountAsync(),
        await context.ExportReceipts.AsNoTracking().CountAsync());
}

internal sealed record AccountDefinition(string Username, string FullName, Role Role, bool AssignWh01);

internal sealed record LocalQaAdminCurrentUser(int UserId) : ICurrentUser
{
    public bool IsAuthenticated => true;
    public bool IsGlobalAdmin => true;
    public string Role => "Admin";
}

internal sealed record InventoryInvariant(decimal OnHand, decimal Reserved, int Transactions, int Reservations, int Imports, int Exports);
