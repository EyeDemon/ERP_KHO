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
        var probeBuilder = new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = 15 };
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
    if (wh01.Name is "QA Kho TP.HCM" or "Local QA Warehouse")
    {
        wh01.Name = "Kho Chính TP.HCM";
    }
    var wh02 = await context.Warehouses.IgnoreQueryFilters().FirstOrDefaultAsync(warehouse => warehouse.Code == "WH02" || warehouse.Code == "QA-HN");
    if (wh02 is null)
    {
        wh02 = new Warehouse
        {
            Code = "WH02",
            Name = "Kho Chi Nhánh Hà Nội",
            Address = "Quận Cầu Giấy, Hà Nội",
            IsActive = true
        };
        context.Warehouses.Add(wh02);
        await context.SaveChangesAsync();
    }
    else if (wh02.Name is "Kho QA ngoài phạm vi" or "QA Kho Hà Nội")
    {
        wh02.Name = "Kho Chi Nhánh Hà Nội";
        wh02.Address = "Quận Cầu Giấy, Hà Nội";
        await context.SaveChangesAsync();
    }

    var hasher = new PasswordHasherService();
    var qaAdmin = await context.Users.IgnoreQueryFilters()
        .OrderBy(u => u.Id)
        .FirstOrDefaultAsync(u => u.Username == "qa_admin");
    if (qaAdmin is null)
    {
        qaAdmin = new User
        {
            TenantId = 1,
            Username = "qa_admin",
            FullName = "QA Admin",
            Role = roles["Admin"],
            PasswordHash = hasher.HashPassword("QaAdmin@123456!"),
            IsActive = true,
            FailedLoginCount = 0,
            LockoutEnd = null
        };
        context.Users.Add(qaAdmin);
        await context.SaveChangesAsync();
    }
    else
    {
        qaAdmin.TenantId = 1;
        qaAdmin.Role = roles["Admin"];
        qaAdmin.PasswordHash = hasher.HashPassword("QaAdmin@123456!");
        qaAdmin.IsActive = true;
        qaAdmin.FailedLoginCount = 0;
        qaAdmin.LockoutEnd = null;
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
    if (!await context.UserWarehouses.IgnoreQueryFilters().AnyAsync(access => access.UserId == qaAdmin.Id && access.WarehouseId == wh02.Id))
    {
        context.UserWarehouses.Add(new UserWarehouse { UserId = qaAdmin.Id, WarehouseId = wh02.Id, CreatedBy = qaAdmin.Id });
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

    // 2. Ensure RECEIVING, STAGING, and storage bin BIN-A01-01 exist in the default warehouse
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

    var stagingLocation = await context.WarehouseLocations.IgnoreQueryFilters()
        .FirstOrDefaultAsync(l => l.WarehouseId == primaryWarehouse.Id && l.Code == "STAGING");
    if (stagingLocation is null)
    {
        stagingLocation = new WarehouseLocation
        {
            WarehouseId = primaryWarehouse.Id,
            Code = "STAGING",
            Name = "Khu vực tập kết xuất hàng",
            LocationType = WarehouseLocationType.Staging,
            IsActive = true,
            IsBlocked = false,
            IsPickable = false,
            IsReceivable = false,
            IsSystemManaged = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = qaAdmin.Id
        };
        context.WarehouseLocations.Add(stagingLocation);
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
    var allUnits = await context.Units.IgnoreQueryFilters().OrderBy(u => u.Id).ToListAsync();
    foreach (var u in allUnits)
    {
        if (u.Code is "QA_UNIT" or "QA-PCS")
        {
            u.Code = "THÙNG";
            u.Name = "Thùng";
        }
        else if (u.Code == "QA-BOX")
        {
            u.Code = "HỘP";
            u.Name = "Hộp";
        }
    }
    await context.SaveChangesAsync();

    var unit = allUnits.FirstOrDefault(u => u.IsActive && u.Code == "THÙNG")
               ?? allUnits.FirstOrDefault(u => u.IsActive);
    if (unit is null)
    {
        unit = new Unit { Code = "THÙNG", Name = "Thùng", DecimalPlaces = 0, IsActive = true };
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

    // 4. Idempotent PutawayTask ("PUT-QA-001") backed by ImportReceipt ("IR-QA-002") in Open status
    var putawayReceipt = await context.ImportReceipts.IgnoreQueryFilters()
        .Include(r => r.Details)
        .FirstOrDefaultAsync(r => r.Code == "IR-QA-002" || r.Code == "PUT-QA-001");
    if (putawayReceipt is null)
    {
        putawayReceipt = new ImportReceipt
        {
            Code = "IR-QA-002",
            WarehouseId = primaryWarehouse.Id,
            SupplierId = supplier.Id,
            SupplierCodeSnapshot = supplier.Code,
            SupplierNameSnapshot = supplier.Name,
            Status = ReceiptStatus.Posted,
            Note = "Phiếu nhập kho lô hàng chờ cất kệ IR-QA-002",
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
                    UnitPrice = 250000m,
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
    else
    {
        putawayReceipt.Code = "IR-QA-002";
        putawayReceipt.SupplierId = supplier.Id;
        putawayReceipt.SupplierCodeSnapshot = supplier.Code;
        putawayReceipt.SupplierNameSnapshot = supplier.Name;
        putawayReceipt.Note = "Phiếu nhập kho lô hàng chờ cất kệ IR-QA-002";
    }

    var putawayReceiptLine = putawayReceipt.Details.First();
    putawayReceiptLine.OperationUnitCodeSnapshot = unit.Code;
    putawayReceiptLine.BaseUnitCodeSnapshot = unit.Code;
    if (putawayReceiptLine.UnitPrice <= 10000m)
    {
        putawayReceiptLine.UnitPrice = 250000m;
    }
    await context.SaveChangesAsync();
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
        else
        {
            item.OperationUnitCodeSnapshot = unit.Code;
            item.BaseUnitCodeSnapshot = unit.Code;
            if (item.MovedBaseQuantity >= item.RequiredBaseQuantity)
            {
                item.MovedBaseQuantity = 0m;
            }
        }

        var stalePutawayTasks = await context.PutawayTasks.IgnoreQueryFilters()
            .Include(t => t.Items)
            .Where(t => t.WarehouseId == primaryWarehouse.Id && t.Id != putawayTask.Id)
            .ToListAsync();
        foreach (var staleTask in stalePutawayTasks)
        {
            if (staleTask.Items.Count > 0)
            {
                context.PutawayTaskItems.RemoveRange(staleTask.Items);
            }
            context.PutawayTasks.Remove(staleTask);
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
            ReservedQuantity = 9m,
            LastUpdated = DateTime.UtcNow
        };
        context.InventoryStocks.Add(storageStock);
    }
    else
    {
        if (storageStock.ReservedQuantity < 9m)
            storageStock.ReservedQuantity = 9m;
        if (storageStock.Quantity < storageStock.ReservedQuantity + 16m)
            storageStock.Quantity = storageStock.ReservedQuantity + 16m;
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
                    UnitPrice = 1800000m,
                    TaxPercent = 10m
                }
            ]
        };
        context.SalesOrders.Add(salesOrder);
        await context.SaveChangesAsync();
    }
    else
    {
        foreach (var l in salesOrder.Lines)
        {
            l.UomCode = unit.Code;
            if (l.UnitPrice <= 10000m || l.UnitPrice == 1200000m)
            {
                l.UnitPrice = 1800000m;
            }
            if (l.TaxPercent <= 0m)
            {
                l.TaxPercent = 10m;
            }
        }
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
        .FirstOrDefaultAsync(t => t.TaskCode == "PICK-QA-001");
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
    else
    {
        pickingTask.CreatedAt = DateTime.UtcNow;
        if (pickingTask.Status is not (PickingTaskStatus.Open or PickingTaskStatus.Assigned))
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
    }
    await context.SaveChangesAsync();

    // 5b. Idempotent Completed PickingTask ("PICK-QA-002") & Active PackingSession ("PACK-QA-001") + HandlingUnit ("HU-QA-001")
    var packSalesOrder = await context.SalesOrders.IgnoreQueryFilters()
        .Include(o => o.Lines)
        .FirstOrDefaultAsync(o => o.OrderCode == "SO-QA-002");
    if (packSalesOrder is null)
    {
        packSalesOrder = new SalesOrder
        {
            OrderCode = "SO-QA-002",
            ExternalOrderId = "EXT-SO-QA-002",
            CustomerId = customer.Id,
            WarehouseId = primaryWarehouse.Id,
            RequestedShipDate = DateTime.UtcNow.Date.AddDays(1),
            Priority = 1,
            ShippingMethod = "EXPRESS",
            Status = SalesOrderStatus.Released,
            CreatedAt = DateTime.UtcNow.AddMinutes(-35),
            CreatedBy = qaAdmin.Id,
            ReleasedAt = DateTime.UtcNow.AddMinutes(-32),
            Lines =
            [
                new SalesOrderLine
                {
                    ExternalLineId = "EXT-SO-QA-002-L1",
                    ProductId = product.Id,
                    OrderedQuantity = 4m,
                    CancelledQuantity = 0m,
                    UomCode = unit.Code,
                    UnitPrice = 1800000m,
                    TaxPercent = 10m
                }
            ]
        };
        context.SalesOrders.Add(packSalesOrder);
        await context.SaveChangesAsync();
    }
    else
    {
        foreach (var l in packSalesOrder.Lines)
        {
            l.UomCode = unit.Code;
            if (l.UnitPrice <= 10000m || l.UnitPrice == 1200000m)
            {
                l.UnitPrice = 1800000m;
            }
            if (l.TaxPercent <= 0m)
            {
                l.TaxPercent = 10m;
            }
        }
        await context.SaveChangesAsync();
    }

    var packReservation = await context.StockReservations.IgnoreQueryFilters()
        .FirstOrDefaultAsync(r => r.ReservationCode == "RSV-QA-PACK-001" ||
                                  (r.SourceType == "SalesOrder" && r.SourceId == packSalesOrder.Id && r.ProductId == product.Id));
    if (packReservation is null)
    {
        packReservation = new StockReservation
        {
            ReservationCode = "RSV-QA-PACK-001",
            ProductId = product.Id,
            WarehouseId = primaryWarehouse.Id,
            Quantity = 4m,
            AllocatedQuantity = 4m,
            ConsumedQuantity = 0m,
            ReleasedQuantity = 0m,
            Status = StockReservationStatus.Allocated,
            SourceType = "SalesOrder",
            SourceId = packSalesOrder.Id,
            SourceCode = packSalesOrder.OrderCode,
            CreatedAt = DateTime.UtcNow.AddMinutes(-33),
            CreatedBy = qaAdmin.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            AllocationVersion = 1
        };
        context.StockReservations.Add(packReservation);
        await context.SaveChangesAsync();
    }

    var packAllocation = await context.StockAllocations.IgnoreQueryFilters()
        .FirstOrDefaultAsync(a => a.AllocationCode == "ALC-QA-PACK-001" || a.ReservationId == packReservation.Id);
    if (packAllocation is null)
    {
        packAllocation = new StockAllocation
        {
            AllocationCode = "ALC-QA-PACK-001",
            ReservationId = packReservation.Id,
            WarehouseId = primaryWarehouse.Id,
            ProductId = product.Id,
            LocationId = storageBin.Id,
            InventoryStatus = InventoryStatus.Available,
            Quantity = 4m,
            Status = StockAllocationStatus.Active,
            Strategy = AllocationStrategy.LocationOrder,
            SelectionReason = "QA seed packing allocation at BIN-A01-01",
            AllocatedAt = DateTime.UtcNow.AddMinutes(-31),
            AllocatedBy = qaAdmin.Id,
            Version = 1
        };
        context.StockAllocations.Add(packAllocation);
        await context.SaveChangesAsync();
    }

    var packPickingTask = await context.PickingTasks.IgnoreQueryFilters()
        .Include(t => t.Lines)
        .FirstOrDefaultAsync(t => t.TaskCode == "PICK-QA-002" ||
                                  (t.SourceType == "SalesOrder" && t.SourceId == packSalesOrder.Id));
    if (packPickingTask is null)
    {
        packPickingTask = new PickingTask
        {
            TaskCode = "PICK-QA-002",
            SourceType = "SalesOrder",
            SourceId = packSalesOrder.Id,
            SourceCode = packSalesOrder.OrderCode,
            WarehouseId = primaryWarehouse.Id,
            PickingType = "SingleOrder",
            Status = PickingTaskStatus.Completed,
            Priority = 1,
            AssignedUserId = qaAdmin.Id,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30),
            StartedAt = DateTime.UtcNow.AddMinutes(-25),
            CompletedAt = DateTime.UtcNow.AddMinutes(-20),
            CreatedBy = qaAdmin.Id,
            Lines =
            [
                new PickingTaskLine
                {
                    Sequence = 1,
                    AllocationId = packAllocation.Id,
                    ProductId = product.Id,
                    SourceLocationId = storageBin.Id,
                    RequestedQuantity = 4m,
                    PickedQuantity = 4m,
                    Status = PickingTaskLineStatus.Picked
                }
            ]
        };
        context.PickingTasks.Add(packPickingTask);
        await context.SaveChangesAsync();
    }
    else
    {
        packPickingTask.Status = PickingTaskStatus.Completed;
        packPickingTask.CompletedAt ??= DateTime.UtcNow.AddMinutes(-20);
        foreach (var line in packPickingTask.Lines)
        {
            line.RequestedQuantity = 4m;
            line.PickedQuantity = 4m;
            line.Status = PickingTaskLineStatus.Picked;
        }
        await context.SaveChangesAsync();
    }

    var packTaskLine = packPickingTask.Lines.First();
    var packingSession = await context.PackingSessions.IgnoreQueryFilters()
        .Include(s => s.HandlingUnits)
        .ThenInclude(h => h.Contents)
        .FirstOrDefaultAsync(s => s.SessionCode == "PACK-QA-001" || s.PickingTaskId == packPickingTask.Id);
    if (packingSession is null)
    {
        packingSession = new PackingSession
        {
            SessionCode = "PACK-QA-001",
            PickingTaskId = packPickingTask.Id,
            WarehouseId = primaryWarehouse.Id,
            Status = PackingSessionStatus.InProgress,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow.AddMinutes(-15),
            CreatedBy = qaAdmin.Id,
            HandlingUnits =
            [
                new HandlingUnit
                {
                    HuCode = "HU-QA-001",
                    Barcode = "HU-QA-001",
                    Sscc = "003893000000000018",
                    WarehouseId = primaryWarehouse.Id,
                    Type = HandlingUnitType.Carton,
                    Status = HandlingUnitStatus.InUse,
                    GrossWeightKg = 8.4m,
                    NetWeightKg = 8.0m,
                    VolumeM3 = 0.04m,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-14),
                    CreatedBy = qaAdmin.Id,
                    Contents =
                    [
                        new HandlingUnitContent
                        {
                            PickingTaskLineId = packTaskLine.Id,
                            ProductId = product.Id,
                            Quantity = 2m,
                            PackedAt = DateTime.UtcNow.AddMinutes(-10),
                            PackedBy = qaAdmin.Id
                        }
                    ]
                }
            ]
        };
        context.PackingSessions.Add(packingSession);
        await context.SaveChangesAsync();
    }
    else
    {
        packingSession.SessionCode = "PACK-QA-001";
        packingSession.Status = PackingSessionStatus.InProgress;
        packingSession.CreatedAt = DateTime.UtcNow;
        packingSession.StartedAt ??= DateTime.UtcNow.AddMinutes(-15);
        packingSession.PackedAt = null;
        packingSession.ClosedAt = null;
        var hu = packingSession.HandlingUnits.FirstOrDefault(h => h.HuCode == "HU-QA-001")
                 ?? packingSession.HandlingUnits.FirstOrDefault();
        if (hu is null)
        {
            hu = new HandlingUnit
            {
                HuCode = "HU-QA-001",
                Barcode = "HU-QA-001",
                Sscc = "003893000000000018",
                WarehouseId = primaryWarehouse.Id,
                PackingSessionId = packingSession.Id,
                Type = HandlingUnitType.Carton,
                Status = HandlingUnitStatus.InUse,
                GrossWeightKg = 8.4m,
                NetWeightKg = 8.0m,
                VolumeM3 = 0.04m,
                CreatedAt = DateTime.UtcNow.AddMinutes(-14),
                CreatedBy = qaAdmin.Id
            };
            packingSession.HandlingUnits.Add(hu);
            await context.SaveChangesAsync();
        }
        else
        {
            hu.HuCode = "HU-QA-001";
            hu.Barcode = "HU-QA-001";
            hu.Sscc = "003893000000000018";
            hu.Type = HandlingUnitType.Carton;
            hu.Status = HandlingUnitStatus.InUse;
            hu.GrossWeightKg = 8.4m;
            hu.NetWeightKg = 8.0m;
            hu.VolumeM3 = 0.04m;
            hu.ClosedAt = null;
            hu.SealedAt = null;
        }

        if (!hu.Contents.Any())
        {
            hu.Contents.Add(new HandlingUnitContent
            {
                PickingTaskLineId = packTaskLine.Id,
                ProductId = product.Id,
                Quantity = 2m,
                PackedAt = DateTime.UtcNow.AddMinutes(-10),
                PackedBy = qaAdmin.Id
            });
        }
        else
        {
            var firstContent = hu.Contents.First();
            firstContent.PickingTaskLineId = packTaskLine.Id;
            firstContent.ProductId = product.Id;
            firstContent.Quantity = 2m;
        }
        await context.SaveChangesAsync();
    }

    // 5c. Idempotent PO-QA-001, ASN-QA-001, IR-QA-001, SHP-QA-001, ER-QA-001, STF-QA-001, STK-QA-001
    var purchaseOrder = await context.PurchaseOrders.IgnoreQueryFilters()
        .Include(p => p.Lines)
        .FirstOrDefaultAsync(p => p.Code == "PO-QA-001" || p.Code == "PO-QA-2026-01" || p.ExternalPoId == "EXT-PO-QA-001");
    if (purchaseOrder is null)
    {
        purchaseOrder = new PurchaseOrder
        {
            Code = "PO-QA-001",
            ExternalPoId = "EXT-PO-QA-001",
            SourceSystem = "MANUAL",
            SupplierId = supplier.Id,
            WarehouseId = primaryWarehouse.Id,
            OrderDate = DateTime.UtcNow.Date.AddDays(-3),
            ExpectedDate = DateTime.UtcNow.Date.AddDays(3),
            Currency = "VND",
            Status = PurchaseOrderStatus.Open,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3),
            CreatedBy = qaAdmin.Id,
            Lines =
            [
                new PurchaseOrderLine
                {
                    ExternalLineId = "EXT-PO-QA-001-L1",
                    LineNo = 1,
                    ProductId = product.Id,
                    OrderedQuantity = 100m,
                    UnitPrice = 250000m,
                    TaxPercent = 10m,
                    OperationUnitId = unit.Id,
                    OperationUnitCodeSnapshot = unit.Code,
                    OperationUnitDecimalPlaces = unit.DecimalPlaces,
                    BaseUnitId = unit.Id,
                    BaseUnitCodeSnapshot = unit.Code,
                    BaseUnitDecimalPlaces = unit.DecimalPlaces,
                    ConversionFactorSnapshot = 1m,
                    ConversionVersionSnapshot = 1,
                    BaseOrderedQuantity = 100m,
                    AllowedOverReceiptPct = 5m,
                    AllowedUnderReceiptPct = 5m
                }
            ]
        };
        context.PurchaseOrders.Add(purchaseOrder);
        await context.SaveChangesAsync();
    }
    else
    {
        purchaseOrder.Code = "PO-QA-001";
        purchaseOrder.SupplierId = supplier.Id;
        purchaseOrder.WarehouseId = primaryWarehouse.Id;
        purchaseOrder.Currency = "VND";
        purchaseOrder.ExpectedDate ??= DateTime.UtcNow.Date.AddDays(3);
        if (!purchaseOrder.Lines.Any())
        {
            purchaseOrder.Lines.Add(new PurchaseOrderLine
            {
                ExternalLineId = "EXT-PO-QA-001-L1",
                LineNo = 1,
                ProductId = product.Id,
                OrderedQuantity = 100m,
                UnitPrice = 250000m,
                TaxPercent = 10m,
                OperationUnitId = unit.Id,
                OperationUnitCodeSnapshot = unit.Code,
                OperationUnitDecimalPlaces = unit.DecimalPlaces,
                BaseUnitId = unit.Id,
                BaseUnitCodeSnapshot = unit.Code,
                BaseUnitDecimalPlaces = unit.DecimalPlaces,
                ConversionFactorSnapshot = 1m,
                ConversionVersionSnapshot = 1,
                BaseOrderedQuantity = 100m,
                AllowedOverReceiptPct = 5m,
                AllowedUnderReceiptPct = 5m
            });
        }
        else
        {
            foreach (var line in purchaseOrder.Lines)
            {
                line.OperationUnitId = unit.Id;
                line.OperationUnitCodeSnapshot = unit.Code;
                line.BaseUnitId = unit.Id;
                line.BaseUnitCodeSnapshot = unit.Code;
            }
        }
        await context.SaveChangesAsync();
    }

    var poLine = purchaseOrder.Lines.First();
    var asn = await context.Asns.IgnoreQueryFilters()
        .Include(a => a.Lines)
        .FirstOrDefaultAsync(a => a.Code == "ASN-QA-001" || a.Code == "ASN-QA-2026-01");
    if (asn is null)
    {
        asn = new Asn
        {
            Code = "ASN-QA-001",
            PurchaseOrderId = purchaseOrder.Id,
            SupplierId = supplier.Id,
            WarehouseId = primaryWarehouse.Id,
            ExpectedArrivalAtUtc = DateTime.UtcNow.AddHours(6),
            CarrierName = "Viettel Post Logistics",
            VehiclePlate = "51C-889.99",
            Note = "Thông báo giao hàng trước cho đơn mua PO-QA-001",
            Status = AsnStatus.Confirmed,
            CreatedAtUtc = DateTime.UtcNow.AddHours(-12),
            CreatedBy = qaAdmin.Id,
            Lines =
            [
                new AsnLine
                {
                    PurchaseOrderLineId = poLine.Id,
                    LineNo = 1,
                    ProductId = product.Id,
                    ExpectedQuantity = 100m,
                    OperationUnitId = unit.Id,
                    OperationUnitCodeSnapshot = unit.Code,
                    OperationUnitDecimalPlaces = unit.DecimalPlaces,
                    BaseUnitId = unit.Id,
                    BaseUnitCodeSnapshot = unit.Code,
                    BaseUnitDecimalPlaces = unit.DecimalPlaces,
                    ConversionFactorSnapshot = 1m,
                    ConversionVersionSnapshot = 1,
                    BaseExpectedQuantity = 100m
                }
            ]
        };
        context.Asns.Add(asn);
        await context.SaveChangesAsync();
    }
    else
    {
        asn.Code = "ASN-QA-001";
        asn.PurchaseOrderId = purchaseOrder.Id;
        asn.SupplierId = supplier.Id;
        asn.WarehouseId = primaryWarehouse.Id;
        asn.ExpectedArrivalAtUtc ??= DateTime.UtcNow.AddHours(6);
        asn.CarrierName = string.IsNullOrWhiteSpace(asn.CarrierName) ? "Viettel Post Logistics" : asn.CarrierName;
        asn.VehiclePlate = string.IsNullOrWhiteSpace(asn.VehiclePlate) ? "51C-889.99" : asn.VehiclePlate;
        asn.Note = "Thông báo giao hàng trước cho đơn mua PO-QA-001";
        if (!asn.Lines.Any())
        {
            asn.Lines.Add(new AsnLine
            {
                PurchaseOrderLineId = poLine.Id,
                LineNo = 1,
                ProductId = product.Id,
                ExpectedQuantity = 100m,
                OperationUnitId = unit.Id,
                OperationUnitCodeSnapshot = unit.Code,
                OperationUnitDecimalPlaces = unit.DecimalPlaces,
                BaseUnitId = unit.Id,
                BaseUnitCodeSnapshot = unit.Code,
                BaseUnitDecimalPlaces = unit.DecimalPlaces,
                ConversionFactorSnapshot = 1m,
                ConversionVersionSnapshot = 1,
                BaseExpectedQuantity = 100m
            });
        }
        else
        {
            foreach (var line in asn.Lines)
            {
                line.PurchaseOrderLineId ??= poLine.Id;
                line.OperationUnitId = unit.Id;
                line.OperationUnitCodeSnapshot = unit.Code;
                line.BaseUnitId = unit.Id;
                line.BaseUnitCodeSnapshot = unit.Code;
            }
        }
        await context.SaveChangesAsync();
    }

    var allImportReceipts = await context.ImportReceipts.IgnoreQueryFilters()
        .Include(r => r.Details)
        .OrderBy(r => r.Id)
        .ToListAsync();
    var nonPutawayReceipts = allImportReceipts.Where(r => r.Id != putawayReceipt.Id).ToList();
    var irQa001 = nonPutawayReceipts.FirstOrDefault(r => r.Code == "IR-QA-001") ?? nonPutawayReceipts.FirstOrDefault();
    if (irQa001 is null)
    {
        irQa001 = new ImportReceipt
        {
            Code = "IR-QA-001",
            WarehouseId = primaryWarehouse.Id,
            SupplierId = supplier.Id,
            SupplierCodeSnapshot = supplier.Code,
            SupplierNameSnapshot = supplier.Name,
            PurchaseOrderId = purchaseOrder.Id,
            AsnId = asn.Id,
            Status = ReceiptStatus.Posted,
            Note = "Phiếu nhập kho lô hàng thiết bị định kỳ IR-QA-001",
            CreatedBy = qaAdmin.Id,
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            ApprovedBy = qaAdmin.Id,
            ApprovedAt = DateTime.UtcNow.AddDays(-2),
            Details =
            [
                new ImportReceiptDetail
                {
                    ProductId = product.Id,
                    Quantity = 100m,
                    UnitPrice = 250000m,
                    Note = "Nhập kho theo đơn mua PO-QA-001",
                    OperationUnitId = unit.Id,
                    OperationUnitCodeSnapshot = unit.Code,
                    OperationUnitDecimalPlaces = unit.DecimalPlaces,
                    BaseUnitId = unit.Id,
                    BaseUnitCodeSnapshot = unit.Code,
                    BaseUnitDecimalPlaces = unit.DecimalPlaces,
                    ConversionFactor = 1m,
                    ConversionVersion = 1,
                    ExpectedQuantity = 100m,
                    ReceivedQuantity = 100m,
                    ObservedQuantity = 100m,
                    FinalReceivedQuantity = 100m,
                    AcceptedQuantity = 100m,
                    PostedQuantity = 100m,
                    BaseExpectedQuantity = 100m,
                    BaseReceivedQuantity = 100m,
                    BaseObservedQuantity = 100m,
                    BaseFinalReceivedQuantity = 100m,
                    BaseAcceptedQuantity = 100m
                }
            ]
        };
        context.ImportReceipts.Add(irQa001);
        await context.SaveChangesAsync();
    }
    else
    {
        irQa001.Code = "IR-QA-001";
        irQa001.SupplierId = supplier.Id;
        irQa001.SupplierCodeSnapshot = supplier.Code;
        irQa001.SupplierNameSnapshot = supplier.Name;
        irQa001.PurchaseOrderId = purchaseOrder.Id;
        irQa001.AsnId = asn.Id;
        irQa001.Note = "Phiếu nhập kho lô hàng thiết bị định kỳ IR-QA-001";
    }

    var otherNonPutaway = nonPutawayReceipts.Where(r => r.Id != irQa001.Id).ToList();
    for (var idx = 0; idx < otherNonPutaway.Count; idx++)
    {
        var r = otherNonPutaway[idx];
        r.Code = $"IR-QA-{idx + 3:D3}";
        r.SupplierId ??= supplier.Id;
        r.SupplierCodeSnapshot = supplier.Code;
        r.SupplierNameSnapshot = supplier.Name;
        if (string.IsNullOrWhiteSpace(r.Note) || r.Note.Contains("E2E", StringComparison.OrdinalIgnoreCase))
        {
            r.Note = $"Phiếu nhập kho bổ sung {r.Code}";
        }
    }

    foreach (var r in await context.ImportReceipts.IgnoreQueryFilters().Include(x => x.Details).ToListAsync())
    {
        foreach (var d in r.Details)
        {
            d.OperationUnitId = unit.Id;
            d.OperationUnitCodeSnapshot = unit.Code;
            d.BaseUnitId = unit.Id;
            d.BaseUnitCodeSnapshot = unit.Code;
            if (d.UnitPrice <= 10000m)
            {
                d.UnitPrice = 250000m;
            }
        }
    }
    await context.SaveChangesAsync();

    var primaryHu = packingSession.HandlingUnits.First();
    var shipment = await context.Shipments.IgnoreQueryFilters()
        .Include(s => s.HandlingUnits)
        .Include(s => s.TrackingEvents)
        .FirstOrDefaultAsync(s => s.ShipmentCode == "SHP-QA-001" || s.PackingSessionId == packingSession.Id);
    if (shipment is null)
    {
        shipment = new Shipment
        {
            ShipmentCode = "SHP-QA-001",
            PackingSessionId = packingSession.Id,
            WarehouseId = primaryWarehouse.Id,
            SourceType = "SalesOrder",
            SourceId = packSalesOrder.Id,
            SourceCode = packSalesOrder.OrderCode,
            Status = ShipmentStatus.Ready,
            StagingLocationId = stagingLocation.Id,
            VehiclePlate = "51C-889.99",
            TrailerPlate = "51R-102.88",
            SealNumber = "SEAL-QA-001",
            CreatedAt = DateTime.UtcNow.AddMinutes(-8),
            CreatedBy = qaAdmin.Id,
            HandlingUnits =
            [
                new ShipmentHandlingUnit
                {
                    HandlingUnitId = primaryHu.Id,
                    Sequence = 1,
                    AssignedAt = DateTime.UtcNow.AddMinutes(-7)
                }
            ],
            TrackingEvents =
            [
                new ShipmentTrackingEvent
                {
                    EventType = "ShipmentCreated",
                    FromStatus = ShipmentStatus.Draft,
                    ToStatus = ShipmentStatus.Ready,
                    OccurredAt = DateTime.UtcNow.AddMinutes(-8),
                    RecordedAt = DateTime.UtcNow.AddMinutes(-8),
                    Source = "Internal",
                    Note = "Khởi tạo chuyến giao hàng SHP-QA-001 từ phiên đóng gói PACK-QA-001",
                    RecordedBy = qaAdmin.Id
                }
            ]
        };
        context.Shipments.Add(shipment);
        await context.SaveChangesAsync();
    }
    else
    {
        shipment.ShipmentCode = "SHP-QA-001";
        shipment.SourceType = "SalesOrder";
        shipment.SourceId = packSalesOrder.Id;
        shipment.SourceCode = packSalesOrder.OrderCode;
        shipment.StagingLocationId ??= stagingLocation.Id;
        shipment.VehiclePlate = "51C-889.99";
        shipment.TrailerPlate = "51R-102.88";
        shipment.SealNumber = "SEAL-QA-001";
        if (!shipment.HandlingUnits.Any(x => x.HandlingUnitId == primaryHu.Id))
        {
            shipment.HandlingUnits.Add(new ShipmentHandlingUnit
            {
                HandlingUnitId = primaryHu.Id,
                Sequence = 1,
                AssignedAt = DateTime.UtcNow.AddMinutes(-7)
            });
        }
        if (!shipment.TrackingEvents.Any())
        {
            shipment.TrackingEvents.Add(new ShipmentTrackingEvent
            {
                EventType = "ShipmentCreated",
                FromStatus = ShipmentStatus.Draft,
                ToStatus = ShipmentStatus.Ready,
                OccurredAt = DateTime.UtcNow.AddMinutes(-8),
                RecordedAt = DateTime.UtcNow.AddMinutes(-8),
                Source = "Internal",
                Note = "Khởi tạo chuyến giao hàng SHP-QA-001 từ phiên đóng gói PACK-QA-001",
                RecordedBy = qaAdmin.Id
            });
        }
        await context.SaveChangesAsync();
    }

    var allExportReceipts = await context.ExportReceipts.IgnoreQueryFilters()
        .Include(e => e.Details)
        .OrderBy(e => e.Id)
        .ToListAsync();
    if (!allExportReceipts.Any())
    {
        var erQa001 = new ExportReceipt
        {
            Code = "ER-QA-001",
            WarehouseId = primaryWarehouse.Id,
            CustomerId = customer.Id,
            CustomerCodeSnapshot = customer.Code,
            CustomerNameSnapshot = customer.Name,
            SalesOrderId = salesOrder.Id,
            ShipmentId = shipment.Id,
            Status = ReceiptStatus.Dispatched,
            DispatchMode = ExportDispatchMode.DispatchOnApproval,
            Note = "Phiếu xuất kho giao hàng theo đơn bán SO-QA-001",
            CreatedBy = qaAdmin.Id,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            ApprovedBy = qaAdmin.Id,
            ApprovedAt = DateTime.UtcNow.AddDays(-1),
            DispatchedBy = qaAdmin.Id,
            DispatchedAt = DateTime.UtcNow.AddDays(-1),
            Details =
            [
                new ExportReceiptDetail
                {
                    ProductId = product.Id,
                    Quantity = 5m,
                    UnitPrice = 1800000m,
                    TaxPercent = 10m,
                    Note = "Xuất giao lô hàng chuẩn QA",
                    BaseUomIdSnapshot = unit.Id,
                    BaseUomCodeSnapshot = unit.Code,
                    BaseUomNameSnapshot = unit.Name,
                    BaseUomDecimalPlacesSnapshot = unit.DecimalPlaces
                }
            ]
        };
        context.ExportReceipts.Add(erQa001);
        await context.SaveChangesAsync();
    }
    else
    {
        for (var idx = 0; idx < allExportReceipts.Count; idx++)
        {
            var er = allExportReceipts[idx];
            er.Code = $"ER-QA-{idx + 1:D3}";
            er.CustomerId = customer.Id;
            er.CustomerCodeSnapshot = customer.Code;
            er.CustomerNameSnapshot = customer.Name;
            if (idx == 0)
            {
                er.SalesOrderId = salesOrder.Id;
                er.ShipmentId = shipment.Id;
                er.Note = "Phiếu xuất kho giao hàng theo đơn bán SO-QA-001";
            }
            else if (idx == 1)
            {
                er.SalesOrderId = packSalesOrder.Id;
                er.Note = "Phiếu xuất kho xuất thẳng đơn bán SO-QA-002";
            }
            else
            {
                er.Note = $"Phiếu xuất kho đã xử lý {er.Code}";
            }

            foreach (var d in er.Details)
            {
                d.BaseUomIdSnapshot = unit.Id;
                d.BaseUomCodeSnapshot = unit.Code;
                d.BaseUomNameSnapshot = unit.Name;
                d.BaseUomDecimalPlacesSnapshot = unit.DecimalPlaces;
                if (d.UnitPrice <= 10000m || d.UnitPrice == 1200000m)
                {
                    d.UnitPrice = 1800000m;
                }
                if (d.TaxPercent <= 0m)
                {
                    d.TaxPercent = 10m;
                }
                if (string.IsNullOrWhiteSpace(d.Note) || d.Note.Contains("E2E", StringComparison.OrdinalIgnoreCase))
                {
                    d.Note = "Xuất giao lô hàng chuẩn QA";
                }
            }
        }
        await context.SaveChangesAsync();
    }

    var stockTransfer = await context.StockTransfers.IgnoreQueryFilters()
        .Include(t => t.Details)
        .FirstOrDefaultAsync(t => t.Code == "STF-QA-001" || t.Code == "STF-QA-2026-01");
    if (stockTransfer is null)
    {
        stockTransfer = new StockTransfer
        {
            Code = "STF-QA-001",
            SourceWarehouseId = primaryWarehouse.Id,
            DestinationWarehouseId = wh02.Id,
            Status = StockTransferStatus.InTransit,
            Note = "Điều chuyển hàng từ Kho Chính TP.HCM ra Kho Chi Nhánh Hà Nội",
            CreatedBy = qaAdmin.Id,
            CreatedAt = DateTime.UtcNow.AddHours(-4),
            ApprovedBy = qaAdmin.Id,
            ApprovedAt = DateTime.UtcNow.AddHours(-3),
            DispatchedBy = qaAdmin.Id,
            DispatchedAt = DateTime.UtcNow.AddHours(-2),
            Details =
            [
                new StockTransferDetail
                {
                    ProductId = product.Id,
                    RequestedQuantity = 15m,
                    DispatchedQuantity = 15m,
                    ReceivedQuantity = 0m,
                    MissingQuantity = 0m,
                    DamagedQuantity = 0m,
                    Note = "Điều chuyển bổ sung tồn kho chi nhánh"
                }
            ]
        };
        context.StockTransfers.Add(stockTransfer);
        await context.SaveChangesAsync();
    }
    else
    {
        stockTransfer.Code = "STF-QA-001";
        stockTransfer.SourceWarehouseId = primaryWarehouse.Id;
        stockTransfer.DestinationWarehouseId = wh02.Id;
        stockTransfer.Note = "Điều chuyển hàng từ Kho Chính TP.HCM ra Kho Chi Nhánh Hà Nội";
        if (!stockTransfer.Details.Any())
        {
            stockTransfer.Details.Add(new StockTransferDetail
            {
                ProductId = product.Id,
                RequestedQuantity = 15m,
                DispatchedQuantity = 15m,
                ReceivedQuantity = 0m,
                MissingQuantity = 0m,
                DamagedQuantity = 0m,
                Note = "Điều chuyển bổ sung tồn kho chi nhánh"
            });
        }
        await context.SaveChangesAsync();
    }

    var stocktake = await context.Stocktakes.IgnoreQueryFilters()
        .Include(s => s.Details)
        .FirstOrDefaultAsync(s => s.Code == "STK-QA-001" || s.Code == "STK-QA-2026-01");
    if (stocktake is null)
    {
        stocktake = new Stocktake
        {
            Code = "STK-QA-001",
            WarehouseId = primaryWarehouse.Id,
            Status = ReceiptStatus.Draft,
            Note = "[BLIND] Kiểm kê chu kỳ kệ lưu trữ BIN-A01-01",
            CreatedBy = qaAdmin.Id,
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            Details =
            [
                new StocktakeDetail
                {
                    ProductId = product.Id,
                    LocationId = storageBin.Id,
                    SystemQuantity = 25m,
                    ActualQuantity = 25m,
                    DifferenceQuantity = 0m,
                    Note = "Khớp tồn kho thực tế tại kệ BIN-A01-01"
                }
            ]
        };
        context.Stocktakes.Add(stocktake);
        await context.SaveChangesAsync();
    }
    else
    {
        stocktake.Code = "STK-QA-001";
        stocktake.WarehouseId = primaryWarehouse.Id;
        stocktake.Note = "[BLIND] Kiểm kê chu kỳ kệ lưu trữ BIN-A01-01";
        if (!stocktake.Details.Any())
        {
            stocktake.Details.Add(new StocktakeDetail
            {
                ProductId = product.Id,
                LocationId = storageBin.Id,
                SystemQuantity = 25m,
                ActualQuantity = 25m,
                DifferenceQuantity = 0m,
                Note = "Khớp tồn kho thực tế tại kệ BIN-A01-01"
            });
        }
        await context.SaveChangesAsync();
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
        apInvoice.PartnerId = supplier.Id;
        apInvoice.DebtType = PartnerDebtType.AP;
        apInvoice.SourceType = "ImportReceipt";
        apInvoice.SourceCode = "IR-QA-001";
        apInvoice.SubTotalAmount = 25_000_000m;
        apInvoice.TaxAmount = 2_500_000m;
        apInvoice.TotalAmount = 27_500_000m;
        apInvoice.PaidAmount = 0m;
        apInvoice.RemainingAmount = 27_500_000m;
        apInvoice.Status = PartnerDebtInvoiceStatus.Open;
    }

    var existingApLedgers = await context.PartnerDebtLedgers.IgnoreQueryFilters().AsNoTracking()
        .Where(l => l.InvoiceId == apInvoice.Id)
        .ToListAsync();
    if (existingApLedgers.Count != 1 ||
        existingApLedgers[0].DebtType != PartnerDebtType.AP ||
        existingApLedgers[0].TransactionType != PartnerDebtTransactionType.InvoiceCreated ||
        existingApLedgers[0].AmountDelta != 27_500_000m ||
        existingApLedgers[0].BalanceAfter != 27_500_000m)
    {
        if (existingApLedgers.Count > 0)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM PartnerDebtLedgers WHERE InvoiceId = {apInvoice.Id}");
        }

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
        arInvoice.PartnerId = customer.Id;
        arInvoice.DebtType = PartnerDebtType.AR;
        arInvoice.SourceType = "ExportReceipt";
        arInvoice.SourceCode = "ER-QA-001";
        arInvoice.SubTotalAmount = 18_000_000m;
        arInvoice.TaxAmount = 1_800_000m;
        arInvoice.TotalAmount = 19_800_000m;
        arInvoice.PaidAmount = 5_000_000m;
        arInvoice.RemainingAmount = 14_800_000m;
        arInvoice.Status = PartnerDebtInvoiceStatus.PartiallyPaid;
    }

    var existingArLedgers = await context.PartnerDebtLedgers.IgnoreQueryFilters().AsNoTracking()
        .Where(l => l.InvoiceId == arInvoice.Id)
        .OrderBy(l => l.Id)
        .ToListAsync();
    if (existingArLedgers.Count != 2 ||
        existingArLedgers.Any(l => l.DebtType != PartnerDebtType.AR))
    {
        if (existingArLedgers.Count > 0)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM PartnerDebtLedgers WHERE InvoiceId = {arInvoice.Id}");
        }

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

    supplier.PayableBalance = apInvoice.RemainingAmount;
    customer.ReceivableBalance = arInvoice.RemainingAmount;

    // 7. Idempotent Inventory Reconciliation pass — ensure 100% Match across all (WarehouseId, ProductId, Status) pairs
    await context.Database.ExecuteSqlRawAsync(
        "DELETE FROM InventoryTransactions WHERE ReferenceType = 'QASeedReconcile' OR Quantity <= 0 OR TransactionType = 6; " +
        "DELETE FROM InventoryStocks WHERE Status <> 0 AND Quantity <= 0 AND ReservedQuantity <= 0;");

    var allStocks = await context.InventoryStocks.IgnoreQueryFilters().ToListAsync();
    foreach (var stock in allStocks)
    {
        if (stock.Quantity < 0m)
            stock.Quantity = 0m;
        if (stock.ReservedQuantity < 0m)
            stock.ReservedQuantity = 0m;
        if (stock.ReservedQuantity > stock.Quantity)
            stock.ReservedQuantity = stock.Quantity;
    }
    await context.SaveChangesAsync();

    var allTransactions = await context.InventoryTransactions.IgnoreQueryFilters().AsNoTracking().ToListAsync();
    var allPairs = allStocks
        .Select(s => (s.TenantId, s.WarehouseId, s.ProductId))
        .Union(allTransactions.Select(t => (t.TenantId, t.WarehouseId, t.ProductId)))
        .Distinct()
        .ToList();

    var reconciledPairCount = 0;
    foreach (var (tenantId, warehouseId, productId) in allPairs)
    {
        var pairStocks = allStocks
            .Where(s => s.TenantId == tenantId && s.WarehouseId == warehouseId && s.ProductId == productId)
            .ToList();
        var pairTxs = allTransactions
            .Where(t => t.TenantId == tenantId && t.WarehouseId == warehouseId && t.ProductId == productId)
            .ToList();

        var expectedByStatus = Enum.GetValues<InventoryStatus>().ToDictionary(st => st, _ => 0m);
        foreach (var tx in pairTxs)
        {
            if (tx.TransactionType == TransactionType.Move)
                continue;
            if (tx.TransactionType == TransactionType.StatusChange &&
                tx.FromInventoryStatus.HasValue &&
                tx.ToInventoryStatus.HasValue)
            {
                expectedByStatus[tx.FromInventoryStatus.Value] -= tx.Quantity;
                expectedByStatus[tx.ToInventoryStatus.Value] += tx.Quantity;
            }
            else if (Enum.IsDefined(tx.InventoryStatus))
            {
                expectedByStatus[tx.InventoryStatus] += tx.TransactionType.ApplySign(tx.Quantity);
            }
        }

        foreach (var status in Enum.GetValues<InventoryStatus>())
        {
            var actualQty = pairStocks.Where(s => s.Status == status).Sum(s => s.Quantity);
            var expectedQty = expectedByStatus[status];
            var delta = actualQty - expectedQty;
            if (delta != 0m)
            {
                context.InventoryTransactions.Add(new InventoryTransaction
                {
                    TenantId = tenantId,
                    WarehouseId = warehouseId,
                    ProductId = productId,
                    InventoryStatus = status,
                    TransactionType = delta > 0m ? TransactionType.Import : TransactionType.AdjustmentDecrease,
                    Quantity = Math.Abs(delta),
                    ReferenceType = "QASeedReconcile",
                    ReferenceId = productId,
                    TransactionDate = DateTime.UtcNow.AddMinutes(-1),
                    CreatedBy = qaAdmin.Id,
                    Note = "Đồng bộ đối chiếu sổ cái tồn kho chuẩn QA (100% Match)"
                });
                reconciledPairCount++;
            }
        }
    }

    context.AuditLogs.Add(new AuditLog
    {
        UserId = qaAdmin.Id,
        Action = "QA.IamAndOperationalSeedApplied",
        EntityName = "LocalQaOperationalSeed",
        NewValues = $"Warehouse={primaryWarehouse.Code};PutawayTask=PUT-QA-001({putawayTask.Status});PickingTask={pickingTask.TaskCode}({pickingTask.Status});PackingSession={packingSession.SessionCode}({packingSession.Status});AP={apInvoice.Code};AR={arInvoice.Code};Reconciled={reconciledPairCount};CleanedPartners={cleanedPartnersCount}",
        Timestamp = DateTime.UtcNow,
        IpAddress = "local-cli"
    });
    await context.SaveChangesAsync();
    await transaction.CommitAsync();

    Console.WriteLine($"[{dataSource}] Local ERP_KHO QA seed completed: PutawayTask=PUT-QA-001 ({putawayTask.Status}), PickingTask={pickingTask.TaskCode} ({pickingTask.Status}), PackingSession={packingSession.SessionCode} ({packingSession.Status}), AP={apInvoice.Code}, AR={arInvoice.Code}, ReconciledDeltas={reconciledPairCount}, CleanedPartners={cleanedPartnersCount}.");
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
