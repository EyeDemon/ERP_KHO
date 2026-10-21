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

    await EnsureWave44CanonicalSchemaAsync(context);
    await ApplyQaSeedToDatabaseAsync(context, ds);
}

static async Task EnsureWave44CanonicalSchemaAsync(ErpKhoDbContext context)
{
    await context.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('dbo.InventoryStocks', 'AllocatedQuantity') IS NULL
    ALTER TABLE dbo.InventoryStocks ADD AllocatedQuantity decimal(19,4) NOT NULL CONSTRAINT DF_InventoryStocks_AllocatedQuantity DEFAULT (0);
IF COL_LENGTH('dbo.InventoryStocks', 'LockedQuantity') IS NULL
    ALTER TABLE dbo.InventoryStocks ADD LockedQuantity decimal(19,4) NOT NULL CONSTRAINT DF_InventoryStocks_LockedQuantity DEFAULT (0);
IF COL_LENGTH('dbo.InventoryStocks', 'HandlingUnitId') IS NULL
    ALTER TABLE dbo.InventoryStocks ADD HandlingUnitId int NULL;
IF COL_LENGTH('dbo.InventoryStocks', 'OwnerId') IS NULL
    ALTER TABLE dbo.InventoryStocks ADD OwnerId int NULL;
IF COL_LENGTH('dbo.InventoryStocks', 'MovingAverageCost') IS NULL
    ALTER TABLE dbo.InventoryStocks ADD MovingAverageCost decimal(19,4) NOT NULL CONSTRAINT DF_InventoryStocks_MovingAverageCost DEFAULT (0);
IF COL_LENGTH('dbo.InventoryStocks', 'GrainHash') IS NULL
    ALTER TABLE dbo.InventoryStocks ADD GrainHash varbinary(32) NULL;

IF COL_LENGTH('dbo.InventoryStocks', 'CanonicalHandlingUnitId') IS NULL
    EXEC(N'ALTER TABLE dbo.InventoryStocks ADD CanonicalHandlingUnitId AS COALESCE([HandlingUnitId], 0) PERSISTED;');
IF COL_LENGTH('dbo.InventoryStocks', 'CanonicalOwnerId') IS NULL
    EXEC(N'ALTER TABLE dbo.InventoryStocks ADD CanonicalOwnerId AS COALESCE([OwnerId], 0) PERSISTED;');

IF NOT EXISTS (
    SELECT 1 FROM sys.index_columns ic
    JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
    JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE i.object_id = OBJECT_ID('dbo.InventoryStocks')
      AND i.name = 'IX_InventoryStocks_CanonicalBucket'
      AND c.name = 'CanonicalHandlingUnitId'
)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.InventoryStocks') AND name = 'IX_InventoryStocks_CanonicalBucket')
        EXEC(N'DROP INDEX IX_InventoryStocks_CanonicalBucket ON dbo.InventoryStocks;');
    EXEC(N'CREATE UNIQUE INDEX IX_InventoryStocks_CanonicalBucket ON dbo.InventoryStocks (TenantId, ProductId, WarehouseId, Status, CanonicalLocationId, CanonicalLotId, CanonicalSerialId, CanonicalHandlingUnitId, CanonicalOwnerId);');
END;

IF COL_LENGTH('dbo.InventoryTransactions', 'HandlingUnitId') IS NULL
    ALTER TABLE dbo.InventoryTransactions ADD HandlingUnitId int NULL;
IF COL_LENGTH('dbo.InventoryTransactions', 'OwnerId') IS NULL
    ALTER TABLE dbo.InventoryTransactions ADD OwnerId int NULL;
IF COL_LENGTH('dbo.InventoryTransactions', 'UnitCost') IS NULL
    ALTER TABLE dbo.InventoryTransactions ADD UnitCost decimal(19,4) NOT NULL CONSTRAINT DF_InventoryTransactions_UnitCost DEFAULT (0);
IF COL_LENGTH('dbo.InventoryTransactions', 'SourceDocumentLineId') IS NULL
    ALTER TABLE dbo.InventoryTransactions ADD SourceDocumentLineId int NULL;

IF COL_LENGTH('dbo.Products', 'MovingAverageCost') IS NULL
    ALTER TABLE dbo.Products ADD MovingAverageCost decimal(19,4) NOT NULL CONSTRAINT DF_Products_MovingAverageCost DEFAULT (0);

IF COL_LENGTH('dbo.StockAllocations', 'HandlingUnitId') IS NULL
    ALTER TABLE dbo.StockAllocations ADD HandlingUnitId int NULL;
IF COL_LENGTH('dbo.StockAllocations', 'OwnerId') IS NULL
    ALTER TABLE dbo.StockAllocations ADD OwnerId int NULL;

IF COL_LENGTH('dbo.StockReservations', 'BaseUomIdSnapshot') IS NULL
    ALTER TABLE dbo.StockReservations ADD BaseUomIdSnapshot int NOT NULL CONSTRAINT DF_StockReservations_BaseUomIdSnapshot DEFAULT (1);
IF COL_LENGTH('dbo.StockReservations', 'BaseUomCodeSnapshot') IS NULL
    ALTER TABLE dbo.StockReservations ADD BaseUomCodeSnapshot nvarchar(32) NOT NULL CONSTRAINT DF_StockReservations_BaseUomCodeSnapshot DEFAULT (N'THÙNG');
IF COL_LENGTH('dbo.StockReservations', 'BaseUomDecimalPlacesSnapshot') IS NULL
    ALTER TABLE dbo.StockReservations ADD BaseUomDecimalPlacesSnapshot int NOT NULL CONSTRAINT DF_StockReservations_BaseUomDecimalPlacesSnapshot DEFAULT (0);

IF COL_LENGTH('dbo.ExportReceipts', 'MarketplaceOrderId') IS NULL
    ALTER TABLE dbo.ExportReceipts ADD MarketplaceOrderId nvarchar(64) NULL;
IF COL_LENGTH('dbo.ExportReceipts', 'CarrierCode') IS NULL
    ALTER TABLE dbo.ExportReceipts ADD CarrierCode nvarchar(32) NULL;
IF COL_LENGTH('dbo.ExportReceipts', 'TrackingNumber') IS NULL
    ALTER TABLE dbo.ExportReceipts ADD TrackingNumber nvarchar(64) NULL;

IF COL_LENGTH('dbo.PutawayTasks', 'TaskType') IS NULL
    ALTER TABLE dbo.PutawayTasks ADD TaskType nvarchar(32) NOT NULL CONSTRAINT DF_PutawayTasks_TaskType DEFAULT (N'PutawayTask');

IF OBJECT_ID(N'dbo.IntegrationOutboxMessages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IntegrationOutboxMessages (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_IntegrationOutboxMessages PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_IntegrationOutboxMessages_TenantId DEFAULT(1),
        EventId nvarchar(64) NOT NULL,
        EventType nvarchar(100) NOT NULL,
        AggregateType nvarchar(64) NOT NULL,
        AggregateId int NULL,
        PayloadJson nvarchar(max) NOT NULL,
        Status nvarchar(32) NOT NULL,
        RetryCount int NOT NULL CONSTRAINT DF_IntegrationOutboxMessages_RetryCount DEFAULT(0),
        LastError nvarchar(1000) NULL,
        CreatedAt datetime2 NOT NULL,
        NextRetryAt datetime2 NULL,
        PublishedAt datetime2 NULL
    );
    CREATE UNIQUE INDEX UX_IntegrationOutboxMessages_TenantId_EventId ON dbo.IntegrationOutboxMessages(TenantId, EventId);
    CREATE INDEX IX_IntegrationOutboxMessages_TenantId_Status_NextRetryAt ON dbo.IntegrationOutboxMessages(TenantId, Status, NextRetryAt);
END;

IF OBJECT_ID(N'dbo.IntegrationInboxMessages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IntegrationInboxMessages (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_IntegrationInboxMessages PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_IntegrationInboxMessages_TenantId DEFAULT(1),
        SourceSystem nvarchar(64) NOT NULL,
        ExternalMessageId nvarchar(128) NOT NULL,
        EventType nvarchar(100) NOT NULL,
        PayloadJson nvarchar(max) NOT NULL,
        Status nvarchar(32) NOT NULL,
        ReceivedAt datetime2 NOT NULL,
        ProcessedAt datetime2 NULL,
        ErrorMessage nvarchar(1000) NULL
    );
    CREATE UNIQUE INDEX UX_IntegrationInboxMessages_TenantId_SourceSystem_ExternalMessageId ON dbo.IntegrationInboxMessages(TenantId, SourceSystem, ExternalMessageId);
END;

IF OBJECT_ID(N'dbo.ProductBundleComponents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProductBundleComponents (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductBundleComponents PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_ProductBundleComponents_TenantId DEFAULT(1),
        ParentProductId int NOT NULL,
        ComponentProductId int NOT NULL,
        ComponentQuantity decimal(19,4) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_ProductBundleComponents_IsActive DEFAULT(1),
        CreatedAt datetime2 NOT NULL
    );
    CREATE UNIQUE INDEX UX_ProductBundleComponents_TenantId_Parent_Component ON dbo.ProductBundleComponents(TenantId, ParentProductId, ComponentProductId);
END;
");
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

    var roleNames = new[] { "Admin", "Manager", "Accountant", "WarehouseStaff", "Viewer" };
    var roles = new Dictionary<string, Role>(StringComparer.Ordinal);
    foreach (var roleName in roleNames)
    {
        var matches = await context.Roles.IgnoreQueryFilters().Where(role => role.RoleName == roleName).ToListAsync();
        var role = matches.FirstOrDefault() ?? new Role { RoleName = roleName, Description = $"Local QA role: {roleName}" };
        if (role.Id == 0) context.Roles.Add(role);
        roles.Add(roleName, role);
    }
    await context.SaveChangesAsync();

    var costReadPerm = await context.Permissions.IgnoreQueryFilters()
        .FirstOrDefaultAsync(p => p.Code == "inventory.cost.read");
    if (costReadPerm is null)
    {
        costReadPerm = new Permission("inventory.cost.read", "Xem đơn giá, giá vốn bình quân và giá trị tồn kho");
        context.Permissions.Add(costReadPerm);
        await context.SaveChangesAsync();
    }
    else
    {
        costReadPerm.Description = "Xem đơn giá, giá vốn bình quân và giá trị tồn kho";
        await context.SaveChangesAsync();
    }

    foreach (var costRoleName in new[] { "Admin", "Manager", "Accountant" })
    {
        var targetRole = roles[costRoleName];
        if (!await context.RolePermissions.IgnoreQueryFilters()
                .AnyAsync(rp => rp.RoleId == targetRole.Id && rp.PermissionId == costReadPerm.Id))
        {
            context.RolePermissions.Add(new RolePermission
            {
                RoleId = targetRole.Id,
                PermissionId = costReadPerm.Id,
                GrantedAt = DateTime.UtcNow
            });
        }
    }
    var restrictedRoleIds = new[] { roles["WarehouseStaff"].Id, roles["Viewer"].Id };
    var forbiddenCostRolePerms = await context.RolePermissions.IgnoreQueryFilters()
        .Where(rp => restrictedRoleIds.Contains(rp.RoleId) && rp.PermissionId == costReadPerm.Id)
        .ToListAsync();
    if (forbiddenCostRolePerms.Count > 0)
    {
        context.RolePermissions.RemoveRange(forbiddenCostRolePerms);
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

    if (!await context.Warehouses.IgnoreQueryFilters().AnyAsync(w => w.Id != primaryWarehouse.Id && w.Code == "WH01"))
    {
        primaryWarehouse.Code = "WH01";
    }
    primaryWarehouse.Name = "Kho Chính TP.HCM";
    primaryWarehouse.Address = "Quận 7, TP.HCM";
    primaryWarehouse.IsActive = true;
    await context.SaveChangesAsync();

    var wh01 = await context.Warehouses.IgnoreQueryFilters().FirstOrDefaultAsync(warehouse => warehouse.Code == "WH01") ?? primaryWarehouse;
    wh01.Name = "Kho Chính TP.HCM";
    wh01.Address = "Quận 7, TP.HCM";
    wh01.IsActive = true;

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
    else
    {
        if (!await context.Warehouses.IgnoreQueryFilters().AnyAsync(w => w.Id != wh02.Id && w.Code == "WH02"))
        {
            wh02.Code = "WH02";
        }
        wh02.Name = "Kho Chi Nhánh Hà Nội";
        wh02.Address = "Quận Cầu Giấy, Hà Nội";
        wh02.IsActive = true;
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
            FullName = "Quản trị viên Hệ thống",
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
        qaAdmin.FullName = "Quản trị viên Hệ thống";
        qaAdmin.Role = roles["Admin"];
        qaAdmin.PasswordHash = hasher.HashPassword("QaAdmin@123456!");
        qaAdmin.IsActive = true;
        qaAdmin.FailedLoginCount = 0;
        qaAdmin.LockoutEnd = null;
        await context.SaveChangesAsync();
    }

    var definitions = new[]
    {
        new AccountDefinition("qa_admin_local", "Quản trị viên Nội bộ", roles["Admin"], false),
        new AccountDefinition("qa_manager_wh01", "Quản lý Kho WH01", roles["Manager"], true),
        new AccountDefinition("qa_staff_wh01", "Nhân viên Kho WH01", roles["WarehouseStaff"], true),
        new AccountDefinition("qa_viewer_wh01", "Giám sát Kho WH01", roles["Viewer"], true)
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
        else
        {
            user.FullName = definition.FullName;
        }
        users.Add(definition.Username, user);
    }
    await context.SaveChangesAsync();

    var allDbUsers = await context.Users.IgnoreQueryFilters().Where(u => u.TenantId == 1).ToListAsync();
    foreach (var dbUser in allDbUsers)
    {
        dbUser.FullName = (dbUser.Username?.Trim().ToLowerInvariant(), dbUser.FullName?.Trim()) switch
        {
            ("qa_admin_local", _) => "Quản trị viên Nội bộ",
            ("qa_manager_wh01" or "qa_manager" or "qa_manager_browser", _) => "Quản lý Kho WH01",
            ("qa_staff_wh01" or "qa_warehousestaff", _) => "Nhân viên Kho WH01",
            ("qa_viewer_wh01" or "qa_viewer" or "qa_viewer_browser", _) => "Giám sát Kho WH01",
            ("qa_admin" or "qa_admin_browser", _) => "Quản trị viên Hệ thống",
            (_, "QA Manager WH01" or "QA Manager") => "Quản lý Kho WH01",
            (_, "QA Staff WH01" or "QA WarehouseStaff") => "Nhân viên Kho WH01",
            (_, "QA Viewer WH01" or "QA Supervisor WH01" or "QA Viewer") => "Giám sát Kho WH01",
            (_, "QA System Admin" or "QA Admin" or "QA Admin Tenant 202") => "Quản trị viên Hệ thống",
            _ => string.IsNullOrWhiteSpace(dbUser.FullName) ? "Quản lý Kho WH01" : dbUser.FullName
        };
    }
    await context.SaveChangesAsync();
    await context.Database.ExecuteSqlRawAsync(@"
        UPDATE dbo.Users SET FullName = N'Quản lý Kho WH01' WHERE FullName IN (N'QA Manager WH01', N'QA Manager') OR Username IN ('qa_manager_wh01', 'qa_manager', 'qa_manager_browser');
        UPDATE dbo.Users SET FullName = N'Nhân viên Kho WH01' WHERE FullName IN (N'QA Staff WH01', N'QA WarehouseStaff') OR Username IN ('qa_staff_wh01', 'qa_warehousestaff');
        UPDATE dbo.Users SET FullName = N'Giám sát Kho WH01' WHERE FullName IN (N'QA Viewer WH01', N'QA Supervisor WH01', N'QA Viewer') OR Username IN ('qa_viewer_wh01', 'qa_viewer', 'qa_viewer_browser');
        UPDATE dbo.Users SET FullName = N'Quản trị viên Hệ thống' WHERE FullName IN (N'QA System Admin', N'QA Admin', N'QA Admin Tenant 202') OR Username IN ('qa_admin', 'qa_admin_browser');
        UPDATE dbo.Users SET FullName = N'Quản trị viên Nội bộ' WHERE Username = 'qa_admin_local';
    ");

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
            Name = "Kệ lưu trữ tiêu chuẩn A01-01",
            StructurePath = "ZONE-A/AISLE-01/RACK-01/SHELF-01/BIN-01",
            StorageClass = "AMBIENT",
            MaxWeightKg = 1500m,
            MaxVolumeM3 = 15m,
            MaxPalletEquivalent = 10m,
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
    else
    {
        storageBin.Name = "Kệ lưu trữ tiêu chuẩn A01-01";
        storageBin.StructurePath ??= "ZONE-A/AISLE-01/RACK-01/SHELF-01/BIN-01";
        storageBin.StorageClass = "AMBIENT";
        storageBin.MaxWeightKg = 1500m;
        storageBin.MaxVolumeM3 = 15m;
        storageBin.MaxPalletEquivalent = 10m;
        storageBin.IsActive = true;
        storageBin.IsBlocked = false;
        storageBin.IsPickable = true;
    }

    var storageBin2 = await context.WarehouseLocations.IgnoreQueryFilters()
        .FirstOrDefaultAsync(l => l.WarehouseId == primaryWarehouse.Id && l.Code == "BIN-A01-02");
    if (storageBin2 is null)
    {
        storageBin2 = new WarehouseLocation
        {
            WarehouseId = primaryWarehouse.Id,
            Code = "BIN-A01-02",
            Name = "Kệ lưu trữ cảm biến A01-02",
            StructurePath = "ZONE-A/AISLE-01/RACK-01/SHELF-02/BIN-02",
            StorageClass = "AMBIENT",
            MaxWeightKg = 1200m,
            MaxVolumeM3 = 12m,
            MaxPalletEquivalent = 8m,
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsBlocked = false,
            IsPickable = true,
            IsReceivable = false,
            IsSystemManaged = false,
            MapX = 35m,
            MapY = 10m,
            MapWidth = 20m,
            MapHeight = 15m,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = qaAdmin.Id
        };
        context.WarehouseLocations.Add(storageBin2);
    }
    else
    {
        storageBin2.Name = "Kệ lưu trữ cảm biến A01-02";
        storageBin2.StructurePath ??= "ZONE-A/AISLE-01/RACK-01/SHELF-02/BIN-02";
        storageBin2.StorageClass = "AMBIENT";
        storageBin2.MaxWeightKg = 1200m;
        storageBin2.MaxVolumeM3 = 12m;
        storageBin2.MaxPalletEquivalent = 8m;
        storageBin2.IsActive = true;
        storageBin2.IsBlocked = false;
        storageBin2.IsPickable = true;
    }

    var extraStructuredBins = new (string Code, string Name, string StructurePath, WarehouseLocationType LocType, bool Pickable, bool Receivable, decimal MaxKg, decimal MaxVol, decimal MaxPallet, decimal MapX, decimal MapY, decimal MapW, decimal MapH)[]
    {
        ("BIN-A01-03", "Kệ lưu trữ hàng nhanh A01-03", "ZONE-A/AISLE-01/RACK-02/SHELF-01/BIN-03", WarehouseLocationType.Storage, true, false, 1500m, 15m, 10m, 68m, 44m, 26m, 24m),
        ("RCV-01", "Cửa tiếp nhận tiêu chuẩn RCV-01", "ZONE-IN/AISLE-01/RACK-01/SHELF-01/BIN-RCV01", WarehouseLocationType.Receiving, false, true, 2500m, 25m, 16m, 6m, 10m, 26m, 22m),
        ("STG-OUT-01", "Khu tập kết xuất hàng STG-OUT-01", "ZONE-OUT/AISLE-01/RACK-01/SHELF-01/BIN-STG01", WarehouseLocationType.Staging, false, false, 2500m, 25m, 16m, 37m, 10m, 26m, 22m),
        ("DMG-01", "Kệ biệt trữ hàng lỗi DMG-01", "ZONE-QC/AISLE-01/RACK-01/SHELF-01/BIN-DMG01", WarehouseLocationType.Damaged, false, false, 1000m, 10m, 6m, 68m, 10m, 26m, 22m)
    };
    foreach (var spec in extraStructuredBins)
    {
        var existingLoc = await context.WarehouseLocations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.WarehouseId == primaryWarehouse.Id && l.Code == spec.Code);
        if (existingLoc is null)
        {
            context.WarehouseLocations.Add(new WarehouseLocation
            {
                WarehouseId = primaryWarehouse.Id,
                Code = spec.Code,
                Name = spec.Name,
                StructurePath = spec.StructurePath,
                StorageClass = "AMBIENT",
                MaxWeightKg = spec.MaxKg,
                MaxVolumeM3 = spec.MaxVol,
                MaxPalletEquivalent = spec.MaxPallet,
                LocationType = spec.LocType,
                IsActive = true,
                IsBlocked = false,
                IsPickable = spec.Pickable,
                IsReceivable = spec.Receivable,
                IsSystemManaged = false,
                MapX = spec.MapX,
                MapY = spec.MapY,
                MapWidth = spec.MapW,
                MapHeight = spec.MapH,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = qaAdmin.Id
            });
        }
        else
        {
            existingLoc.Name = spec.Name;
            existingLoc.StructurePath ??= spec.StructurePath;
            existingLoc.StorageClass = "AMBIENT";
            existingLoc.MaxWeightKg = spec.MaxKg;
            existingLoc.MaxVolumeM3 = spec.MaxVol;
            existingLoc.MaxPalletEquivalent = spec.MaxPallet;
            existingLoc.LocationType = spec.LocType;
            existingLoc.IsActive = true;
            existingLoc.IsBlocked = false;
            existingLoc.IsPickable = spec.Pickable;
            existingLoc.IsReceivable = spec.Receivable;
            existingLoc.IsSystemManaged = false;
            existingLoc.MapX = spec.MapX;
            existingLoc.MapY = spec.MapY;
            existingLoc.MapWidth = spec.MapW;
            existingLoc.MapHeight = spec.MapH;
        }
    }
    await context.SaveChangesAsync();

    var primaryNonSystemLocations = await context.WarehouseLocations.IgnoreQueryFilters()
        .Where(l => l.WarehouseId == primaryWarehouse.Id && !l.IsSystemManaged)
        .ToListAsync();
    var locIdx = 3;
    foreach (var loc in primaryNonSystemLocations)
    {
        loc.StorageClass ??= "AMBIENT";
        loc.MaxWeightKg ??= 1000m;
        loc.MaxVolumeM3 ??= 10m;
        loc.MaxPalletEquivalent ??= 6m;
        if (string.IsNullOrWhiteSpace(loc.StructurePath))
        {
            loc.StructurePath = $"ZONE-B/AISLE-01/RACK-01/SHELF-01/BIN-{locIdx:D2}";
            locIdx++;
        }
    }
    await context.SaveChangesAsync();

    foreach (var loc in primaryNonSystemLocations.OrderBy(x => x.Id))
    {
        if (loc.Code == "BIN-A01-01")
        {
            loc.MapX = 6m;
            loc.MapY = 44m;
            loc.MapWidth = 26m;
            loc.MapHeight = 24m;
        }
        else if (loc.Code == "BIN-A01-02")
        {
            loc.MapX = 37m;
            loc.MapY = 44m;
            loc.MapWidth = 26m;
            loc.MapHeight = 24m;
        }
        else if (loc.Code is not ("BIN-A01-03" or "RCV-01" or "STG-OUT-01" or "DMG-01"))
        {
            loc.MapX ??= 68m;
            loc.MapY ??= 72m;
            loc.MapWidth ??= 26m;
            loc.MapHeight ??= 20m;
            if (loc.Code == "BIN-DMG-01")
            {
                loc.Name = "Kệ biệt trữ hàng lỗi DMG-01";
            }
        }
    }

    var systemVirtualLocationSpecs = new (string Code, string Name, WarehouseLocationType LocType, bool Receivable, bool Pickable)[]
    {
        ("RECEIVING", "Vị trí nhận hàng", WarehouseLocationType.Receiving, true, false),
        ("STAGING", "Khu vực tập kết xuất hàng", WarehouseLocationType.Staging, false, false),
        ("QC_HOLD", "Khu vực tạm giữ chờ kiểm định chất lượng", WarehouseLocationType.Rejected, false, false),
        ("QUARANTINE", "Khu vực cách ly hàng nghi ngờ", WarehouseLocationType.Rejected, false, false),
        ("DAMAGED", "Khu vực biệt trữ hàng hỏng", WarehouseLocationType.Damaged, false, false),
        ("RETURNS_STAGE", "Khu vực tiếp nhận hàng hoàn trả (RMA)", WarehouseLocationType.Receiving, true, false),
        ("PACKING_STAGE", "Khu vực đóng gói đơn hàng", WarehouseLocationType.Staging, false, false),
        ("SHIPPING_STAGE", "Khu vực tập kết chờ bốc xếp giao hàng", WarehouseLocationType.Staging, false, false),
        ("SCRAP", "Khu vực chờ thanh hủy phế phẩm", WarehouseLocationType.Damaged, false, false),
        ("LEGACY", "Vị trí chuyển đổi dữ liệu lịch sử", WarehouseLocationType.Legacy, false, false)
    };
    foreach (var targetWh in new[] { primaryWarehouse, wh02 })
    {
        foreach (var sysLoc in systemVirtualLocationSpecs)
        {
            var existingSysLoc = await context.WarehouseLocations.IgnoreQueryFilters()
                .FirstOrDefaultAsync(l => l.WarehouseId == targetWh.Id && l.Code == sysLoc.Code);
            if (existingSysLoc is null)
            {
                context.WarehouseLocations.Add(new WarehouseLocation
                {
                    WarehouseId = targetWh.Id,
                    Code = sysLoc.Code,
                    Name = sysLoc.Name,
                    LocationType = sysLoc.LocType,
                    IsActive = true,
                    IsBlocked = false,
                    IsPickable = sysLoc.Pickable,
                    IsReceivable = sysLoc.Receivable,
                    IsSystemManaged = true,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = qaAdmin.Id
                });
            }
            else
            {
                existingSysLoc.Name = sysLoc.Name;
                existingSysLoc.LocationType = sysLoc.LocType;
                existingSysLoc.IsActive = true;
                existingSysLoc.IsBlocked = false;
                existingSysLoc.IsPickable = sysLoc.Pickable;
                existingSysLoc.IsReceivable = sysLoc.Receivable;
                existingSysLoc.IsSystemManaged = true;
            }
        }
    }
    await context.SaveChangesAsync();

    foreach (var targetWh in new[] { primaryWarehouse, wh02 })
    {
        var cal = await context.WarehouseCalendars.IgnoreQueryFilters()
            .Include(c => c.Days)
            .Include(c => c.Shifts)
            .FirstOrDefaultAsync(c => c.WarehouseId == targetWh.Id);
        if (cal is null)
        {
            cal = new WarehouseCalendar
            {
                WarehouseId = targetWh.Id,
                TimeZoneId = "Asia/Ho_Chi_Minh",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                UpdatedBy = qaAdmin.Id
            };
            for (var d = 0; d < 7; d++)
            {
                cal.Days.Add(new WarehouseCalendarDay
                {
                    DayOfWeek = d,
                    IsOpen = true,
                    OpensAtLocal = new TimeSpan(6, 0, 0),
                    ClosesAtLocal = new TimeSpan(23, 59, 0),
                    InboundCutoffLocal = new TimeSpan(21, 0, 0),
                    OutboundCutoffLocal = new TimeSpan(22, 0, 0)
                });
            }
            context.WarehouseCalendars.Add(cal);
            await context.SaveChangesAsync();
        }
        else
        {
            cal.TimeZoneId = "Asia/Ho_Chi_Minh";
            for (var d = 0; d < 7; d++)
            {
                var dayRow = cal.Days.FirstOrDefault(x => x.DayOfWeek == d);
                if (dayRow is null)
                {
                    cal.Days.Add(new WarehouseCalendarDay
                    {
                        DayOfWeek = d,
                        IsOpen = true,
                        OpensAtLocal = new TimeSpan(6, 0, 0),
                        ClosesAtLocal = new TimeSpan(23, 59, 0),
                        InboundCutoffLocal = new TimeSpan(21, 0, 0),
                        OutboundCutoffLocal = new TimeSpan(22, 0, 0)
                    });
                }
                else
                {
                    dayRow.IsOpen = true;
                    dayRow.OpensAtLocal = new TimeSpan(6, 0, 0);
                    dayRow.ClosesAtLocal = new TimeSpan(23, 59, 0);
                    dayRow.InboundCutoffLocal = new TimeSpan(21, 0, 0);
                    dayRow.OutboundCutoffLocal = new TimeSpan(22, 0, 0);
                }
            }
            await context.SaveChangesAsync();
        }

        var morningShift = cal.Shifts.FirstOrDefault(s => s.Code == "CA-SANG");
        if (morningShift is null)
        {
            context.WarehouseShifts.Add(new WarehouseShift
            {
                WarehouseId = targetWh.Id,
                Code = "CA-SANG",
                Name = "Ca Sáng — Tiếp nhận & Cất hàng",
                StartTimeLocal = new TimeSpan(6, 0, 0),
                EndTimeLocal = new TimeSpan(14, 30, 0),
                BreakMinutes = 45,
                PlannedHeadcount = 18,
                InboundPalletsPerHour = 35m,
                OutboundOrdersPerHour = 40m,
                DockSlots = 4,
                LaborHours = 135m,
                StagingCapacity = 120m,
                PackingStations = 6,
                EquipmentAvailable = 8,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                UpdatedBy = qaAdmin.Id
            });
        }
        else
        {
            morningShift.Name = "Ca Sáng — Tiếp nhận & Cất hàng";
            morningShift.StartTimeLocal = new TimeSpan(6, 0, 0);
            morningShift.EndTimeLocal = new TimeSpan(14, 30, 0);
            morningShift.BreakMinutes = 45;
            morningShift.PlannedHeadcount = 18;
            morningShift.InboundPalletsPerHour = 35m;
            morningShift.OutboundOrdersPerHour = 40m;
            morningShift.DockSlots = 4;
            morningShift.LaborHours = 135m;
            morningShift.StagingCapacity = 120m;
            morningShift.PackingStations = 6;
            morningShift.EquipmentAvailable = 8;
            morningShift.IsActive = true;
        }

        var afternoonShift = cal.Shifts.FirstOrDefault(s => s.Code == "CA-CHIEU");
        if (afternoonShift is null)
        {
            context.WarehouseShifts.Add(new WarehouseShift
            {
                WarehouseId = targetWh.Id,
                Code = "CA-CHIEU",
                Name = "Ca Chiều — Lấy hàng, Đóng gói & Xuất giao",
                StartTimeLocal = new TimeSpan(14, 0, 0),
                EndTimeLocal = new TimeSpan(22, 30, 0),
                BreakMinutes = 45,
                PlannedHeadcount = 22,
                InboundPalletsPerHour = 25m,
                OutboundOrdersPerHour = 55m,
                DockSlots = 4,
                LaborHours = 165m,
                StagingCapacity = 150m,
                PackingStations = 8,
                EquipmentAvailable = 10,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                UpdatedBy = qaAdmin.Id
            });
        }
        else
        {
            afternoonShift.Name = "Ca Chiều — Lấy hàng, Đóng gói & Xuất giao";
            afternoonShift.StartTimeLocal = new TimeSpan(14, 0, 0);
            afternoonShift.EndTimeLocal = new TimeSpan(22, 30, 0);
            afternoonShift.BreakMinutes = 45;
            afternoonShift.PlannedHeadcount = 22;
            afternoonShift.InboundPalletsPerHour = 25m;
            afternoonShift.OutboundOrdersPerHour = 55m;
            afternoonShift.DockSlots = 4;
            afternoonShift.LaborHours = 165m;
            afternoonShift.StagingCapacity = 150m;
            afternoonShift.PackingStations = 8;
            afternoonShift.EquipmentAvailable = 10;
            afternoonShift.IsActive = true;
        }
        await context.SaveChangesAsync();
    }

    // 3. Resolve Unit, Category, Barcode and Product for QA operational tasks
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

    var catQa01 = await context.ProductCategories.IgnoreQueryFilters()
        .FirstOrDefaultAsync(c => c.Code == "CAT-QA-01");
    if (catQa01 is null)
    {
        catQa01 = new ProductCategory
        {
            Code = "CAT-QA-01",
            Name = "Thiết bị & Linh kiện Công nghiệp QA",
            IsActive = true
        };
        context.ProductCategories.Add(catQa01);
        await context.SaveChangesAsync();
    }
    else
    {
        catQa01.Name = "Thiết bị & Linh kiện Công nghiệp QA";
        catQa01.IsActive = true;
    }

    var catQa02 = await context.ProductCategories.IgnoreQueryFilters()
        .FirstOrDefaultAsync(c => c.Code == "CAT-QA-02");
    if (catQa02 is null)
    {
        catQa02 = new ProductCategory
        {
            Code = "CAT-QA-02",
            Name = "Cảm biến & Tự động hóa QA",
            IsActive = true
        };
        context.ProductCategories.Add(catQa02);
        await context.SaveChangesAsync();
    }
    else
    {
        catQa02.Name = "Cảm biến & Tự động hóa QA";
        catQa02.IsActive = true;
    }

    var product = await context.Products.IgnoreQueryFilters().OrderBy(p => p.Id).FirstOrDefaultAsync(p => p.IsActive);
    if (product is null)
    {
        product = new Product
        {
            Code = "QA-E2E-001",
            Name = "Thùng linh kiện điện tử QA-01",
            UnitId = unit.Id,
            CategoryId = catQa01.Id,
            Description = "Linh kiện điện tử dùng cho kiểm thử ERP",
            StorageClass = "AMBIENT",
            UnitWeightKg = 2.5m,
            UnitVolumeM3 = 0.025m,
            UnitPalletEquivalent = 0.05m,
            TrackingType = ProductTrackingType.None,
            ExpiryControl = false,
            IsActive = true
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();
    }
    else
    {
        product.UnitId = unit.Id;
        product.CategoryId ??= catQa01.Id;
        product.StorageClass = "AMBIENT";
        product.UnitWeightKg ??= 2.5m;
        product.UnitVolumeM3 ??= 0.025m;
        product.UnitPalletEquivalent ??= 0.05m;
        await context.SaveChangesAsync();
    }

    var lotProduct = await context.Products.IgnoreQueryFilters().OrderBy(p => p.Id)
        .FirstOrDefaultAsync(p => p.IsActive && (p.Code == "QA-LOT-002" || p.Id != product.Id));
    if (lotProduct is null)
    {
        lotProduct = new Product
        {
            Code = "QA-LOT-002",
            Name = "Hộp cảm biến nhiệt độ QA-02",
            UnitId = unit.Id,
            CategoryId = catQa02.Id,
            Description = "Cảm biến nhiệt độ quản lý theo lô dùng cho kiểm thử ERP",
            StorageClass = "AMBIENT",
            UnitWeightKg = 1.2m,
            UnitVolumeM3 = 0.012m,
            UnitPalletEquivalent = 0.025m,
            TrackingType = ProductTrackingType.Lot,
            ExpiryControl = true,
            ShelfLifeDays = 365,
            IsActive = true
        };
        context.Products.Add(lotProduct);
        await context.SaveChangesAsync();
    }
    else
    {
        lotProduct.UnitId = unit.Id;
        lotProduct.CategoryId ??= catQa02.Id;
        lotProduct.StorageClass = "AMBIENT";
        lotProduct.UnitWeightKg ??= 1.2m;
        lotProduct.UnitVolumeM3 ??= 0.012m;
        lotProduct.UnitPalletEquivalent ??= 0.025m;
        lotProduct.TrackingType = ProductTrackingType.Lot;
        lotProduct.ExpiryControl = true;
        lotProduct.ShelfLifeDays ??= 365;
        await context.SaveChangesAsync();
    }

    var allExistingProducts = await context.Products.IgnoreQueryFilters()
        .Include(p => p.Barcodes)
        .OrderBy(p => p.Id)
        .ToListAsync();
    foreach (var p in allExistingProducts)
    {
        p.CategoryId ??= catQa01.Id;
        p.StorageClass ??= "AMBIENT";
        p.UnitWeightKg ??= 2.0m;
        p.UnitVolumeM3 ??= 0.02m;
        p.UnitPalletEquivalent ??= 0.04m;
        if (p.ExpiryControl && p.ShelfLifeDays is null)
        {
            p.ShelfLifeDays = 365;
        }
        if (!p.Barcodes.Any())
        {
            var bcVal = $"8938505{p.Id:D6}";
            if (!await context.ProductBarcodes.IgnoreQueryFilters().AnyAsync(b => b.Value == bcVal))
            {
                context.ProductBarcodes.Add(new ProductBarcode
                {
                    ProductId = p.Id,
                    Value = bcVal
                });
            }
        }
    }
    await context.SaveChangesAsync();

    var allExistingProductsWithDetails = await context.Products.IgnoreQueryFilters()
        .Include(p => p.Barcodes)
        .Include(p => p.Uoms)
        .Include(p => p.QcPolicies)
        .OrderBy(p => p.Id)
        .ToListAsync();
    var palletUnit = allUnits.FirstOrDefault(u => u.Code == "PALLET");
    if (palletUnit is null)
    {
        palletUnit = new Unit { Code = "PALLET", Name = "Pallet", DecimalPlaces = 0, IsActive = true };
        context.Units.Add(palletUnit);
        await context.SaveChangesAsync();
    }
    foreach (var p in allExistingProductsWithDetails)
    {
        if (palletUnit.Id != p.UnitId)
        {
            var existingPalletUom = p.Uoms.FirstOrDefault(u => u.UnitId == palletUnit.Id);
            if (existingPalletUom is null)
            {
                context.ProductUoms.Add(new ProductUom
                {
                    ProductId = p.Id,
                    UnitId = palletUnit.Id,
                    ConversionFactor = 20m,
                    Version = 1,
                    EffectiveFromUtc = DateTime.UtcNow.AddDays(-60),
                    IsActive = true
                });
            }
            else
            {
                existingPalletUom.ConversionFactor = 20m;
                existingPalletUom.IsActive = true;
            }
        }
        if (!p.QcPolicies.Any())
        {
            context.QcPolicies.Add(new QcPolicy
            {
                ProductId = p.Id,
                SupplierId = supplier.Id,
                Version = 1,
                RequiresQc = p.Id == lotProduct.Id,
                IsActive = true,
                EffectiveFromUtc = DateTime.UtcNow.AddDays(-60),
                Rule = p.Id == lotProduct.Id
                    ? "Kiểm định bắt buộc nhiệt độ bảo quản, tem lô và ngoại quan (AQL 2.5)"
                    : "Kiểm tra xác suất ngoại quan và niêm phong kiện hàng khi tiếp nhận"
            });
        }
        if (!await context.ReceivingTolerancePolicies.IgnoreQueryFilters().AnyAsync(t => t.ProductId == p.Id))
        {
            context.ReceivingTolerancePolicies.Add(new ReceivingTolerancePolicy
            {
                ProductId = p.Id,
                SupplierId = supplier.Id,
                WarehouseId = primaryWarehouse.Id,
                Version = 1,
                EffectiveFromUtc = DateTime.UtcNow.AddDays(-60),
                AbsoluteQuantityTolerance = 5m,
                PercentageTolerance = 2m,
                OverageAllowed = true,
                ShortageAllowed = true,
                ValueTolerance = 5_000_000m,
                RequiresApprovalOutsideTolerance = true,
                ApproverTarget = "Quản lý Kho",
                IsActive = true
            });
        }
    }
    await context.SaveChangesAsync();
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
            CreatedAt = DateTime.UtcNow.AddHours(-3),
            ApprovedBy = qaAdmin.Id,
            ApprovedAt = DateTime.UtcNow.AddMinutes(-30),
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
        putawayReceipt.CreatedAt = DateTime.UtcNow.AddHours(-3);
        putawayReceipt.ApprovedBy ??= qaAdmin.Id;
        putawayReceipt.ApprovedAt = DateTime.UtcNow.AddMinutes(-30);
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
            Note = "Tồn kho tại khu tiếp nhận RECEIVING cho nhiệm vụ cất hàng PUT-QA-001",
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
            SelectionReason = "Phân bổ tự động theo thứ tự vị trí tại kệ BIN-A01-01",
            AllocatedAt = DateTime.UtcNow,
            AllocatedBy = qaAdmin.Id,
            Version = 1
        };
        context.StockAllocations.Add(pickAllocation);
        await context.SaveChangesAsync();
    }
    else
    {
        pickAllocation.AllocationCode = "ALC-QA-PICK-001";
        pickAllocation.SelectionReason = "Phân bổ tự động theo thứ tự vị trí tại kệ BIN-A01-01";
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
            SelectionReason = "Phân bổ đóng gói theo đơn SO-QA-002 tại kệ BIN-A01-01",
            AllocatedAt = DateTime.UtcNow.AddMinutes(-31),
            AllocatedBy = qaAdmin.Id,
            Version = 1
        };
        context.StockAllocations.Add(packAllocation);
        await context.SaveChangesAsync();
    }
    else
    {
        packAllocation.AllocationCode = "ALC-QA-PACK-001";
        packAllocation.SelectionReason = "Phân bổ đóng gói theo đơn SO-QA-002 tại kệ BIN-A01-01";
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
                    Barcode = "BC-HU-QA-001",
                    Sscc = "008938505000000018",
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
                Barcode = "BC-HU-QA-001",
                Sscc = "008938505000000018",
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
            hu.Barcode = "BC-HU-QA-001";
            hu.Sscc = "008938505000000018";
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
                if (line.UnitPrice <= 10000m)
                {
                    line.UnitPrice = 250000m;
                }
                if (line.TaxPercent <= 0m)
                {
                    line.TaxPercent = 10m;
                }
            }
        }
        await context.SaveChangesAsync();
    }

    var purchaseOrder2 = await context.PurchaseOrders.IgnoreQueryFilters()
        .Include(p => p.Lines)
        .FirstOrDefaultAsync(p => p.Code == "PO-QA-002" || p.ExternalPoId == "EXT-PO-QA-002");
    if (purchaseOrder2 is null)
    {
        purchaseOrder2 = new PurchaseOrder
        {
            Code = "PO-QA-002",
            ExternalPoId = "EXT-PO-QA-002",
            SourceSystem = "MANUAL",
            SupplierId = supplier.Id,
            WarehouseId = primaryWarehouse.Id,
            OrderDate = DateTime.UtcNow.Date.AddDays(-6),
            ExpectedDate = DateTime.UtcNow.Date.AddDays(-1),
            Currency = "VND",
            Status = PurchaseOrderStatus.PartiallyReceived,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-6),
            CreatedBy = qaAdmin.Id,
            Lines =
            [
                new PurchaseOrderLine
                {
                    ExternalLineId = "EXT-PO-QA-002-L1",
                    LineNo = 1,
                    ProductId = lotProduct.Id,
                    OrderedQuantity = 80m,
                    UnitPrice = 420000m,
                    TaxPercent = 10m,
                    OperationUnitId = unit.Id,
                    OperationUnitCodeSnapshot = unit.Code,
                    OperationUnitDecimalPlaces = unit.DecimalPlaces,
                    BaseUnitId = unit.Id,
                    BaseUnitCodeSnapshot = unit.Code,
                    BaseUnitDecimalPlaces = unit.DecimalPlaces,
                    ConversionFactorSnapshot = 1m,
                    ConversionVersionSnapshot = 1,
                    BaseOrderedQuantity = 80m,
                    AllowedOverReceiptPct = 5m,
                    AllowedUnderReceiptPct = 5m
                }
            ]
        };
        context.PurchaseOrders.Add(purchaseOrder2);
        await context.SaveChangesAsync();
    }
    else
    {
        purchaseOrder2.Code = "PO-QA-002";
        purchaseOrder2.SupplierId = supplier.Id;
        purchaseOrder2.WarehouseId = primaryWarehouse.Id;
        purchaseOrder2.ExpectedDate = DateTime.UtcNow.Date.AddDays(-1);
        purchaseOrder2.Status = PurchaseOrderStatus.PartiallyReceived;
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
            ApprovedAt = DateTime.UtcNow.AddDays(-2).AddMinutes(150),
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
        irQa001.CreatedAt = DateTime.UtcNow.AddDays(-2);
        irQa001.ApprovedBy ??= qaAdmin.Id;
        irQa001.ApprovedAt = DateTime.UtcNow.AddDays(-2).AddMinutes(150);
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

    var validUserIds = allDbUsers.Select(u => u.Id).ToHashSet();
    var defaultManagerId = users.TryGetValue("qa_manager_wh01", out var defaultMgr) ? defaultMgr.Id : qaAdmin.Id;
    foreach (var r in await context.ImportReceipts.IgnoreQueryFilters().Where(x => x.TenantId == 1).Include(x => x.Details).ToListAsync())
    {
        if (r.CreatedBy <= 0 || !validUserIds.Contains(r.CreatedBy))
        {
            r.CreatedBy = defaultManagerId;
        }
        if (r.Status is ReceiptStatus.Posted or ReceiptStatus.Approved)
        {
            r.ApprovedBy ??= defaultManagerId;
            if (!r.ApprovedAt.HasValue || (r.ApprovedAt.Value - r.CreatedAt).TotalMinutes < 30)
            {
                r.ApprovedAt = r.CreatedAt.AddMinutes(150);
            }
        }
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
            ApprovedAt = DateTime.UtcNow.AddDays(-1).AddMinutes(60),
            DispatchedBy = qaAdmin.Id,
            DispatchedAt = DateTime.UtcNow.AddDays(-1).AddMinutes(90),
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
            if (er.CreatedAt < DateTime.UtcNow.AddDays(-15))
            {
                er.CreatedAt = DateTime.UtcNow.AddDays(-1);
            }
            if (er.Status is ReceiptStatus.Approved or ReceiptStatus.Posted or ReceiptStatus.Dispatched)
            {
                er.ApprovedBy ??= qaAdmin.Id;
                er.ApprovedAt = er.CreatedAt.AddMinutes(60);
                if (er.Status == ReceiptStatus.Dispatched)
                {
                    er.DispatchedBy ??= qaAdmin.Id;
                    er.DispatchedAt = er.CreatedAt.AddMinutes(90);
                }
            }
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

    var stockTransfer2 = await context.StockTransfers.IgnoreQueryFilters()
        .Include(t => t.Details)
        .FirstOrDefaultAsync(t => t.Code == "STF-QA-002");
    if (stockTransfer2 is null)
    {
        stockTransfer2 = new StockTransfer
        {
            Code = "STF-QA-002",
            SourceWarehouseId = primaryWarehouse.Id,
            DestinationWarehouseId = wh02.Id,
            Status = StockTransferStatus.Approved,
            Note = "Điều chuyển bổ sung cảm biến nhiệt độ QA-02 ra Kho Chi Nhánh Hà Nội",
            CreatedBy = qaAdmin.Id,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            ApprovedBy = qaAdmin.Id,
            ApprovedAt = DateTime.UtcNow.AddMinutes(-45),
            Details =
            [
                new StockTransferDetail
                {
                    ProductId = lotProduct.Id,
                    RequestedQuantity = 10m,
                    DispatchedQuantity = 0m,
                    ReceivedQuantity = 0m,
                    MissingQuantity = 0m,
                    DamagedQuantity = 0m,
                    Note = "Chờ xuất kho điều chuyển liên chi nhánh"
                }
            ]
        };
        context.StockTransfers.Add(stockTransfer2);
        await context.SaveChangesAsync();
    }
    else
    {
        stockTransfer2.Code = "STF-QA-002";
        stockTransfer2.SourceWarehouseId = primaryWarehouse.Id;
        stockTransfer2.DestinationWarehouseId = wh02.Id;
        stockTransfer2.Note = "Điều chuyển bổ sung cảm biến nhiệt độ QA-02 ra Kho Chi Nhánh Hà Nội";
        await context.SaveChangesAsync();
    }

    var qaManagerUser = users.TryGetValue("qa_manager_wh01", out var mgrUser) ? mgrUser : qaAdmin;
    var approvedStocktake = await context.Stocktakes.IgnoreQueryFilters()
        .Include(s => s.Details)
        .FirstOrDefaultAsync(s => s.Code == "STK-QA-000");
    if (approvedStocktake is null)
    {
        approvedStocktake = new Stocktake
        {
            Code = "STK-QA-000",
            WarehouseId = primaryWarehouse.Id,
            Status = ReceiptStatus.Approved,
            Note = "Kiểm kê định kỳ đợt 1 đã phê duyệt tại kệ BIN-A01-01",
            CreatedBy = qaManagerUser.Id,
            CreatedAt = DateTime.UtcNow.AddDays(-2).AddHours(-2),
            ApprovedBy = qaAdmin.Id,
            ApprovedAt = DateTime.UtcNow.AddDays(-2),
            Details =
            [
                new StocktakeDetail
                {
                    ProductId = product.Id,
                    LocationId = storageBin.Id,
                    SystemQuantity = 25m,
                    ActualQuantity = 25m,
                    DifferenceQuantity = 0m,
                    Note = "Khớp 100% tồn kho thực tế đợt 1 tại kệ BIN-A01-01"
                },
                new StocktakeDetail
                {
                    ProductId = lotProduct.Id,
                    LocationId = storageBin.Id,
                    SystemQuantity = 60m,
                    ActualQuantity = 60m,
                    DifferenceQuantity = 0m,
                    Note = "Khớp 100% tồn kho lô cảm biến đợt 1 tại kệ BIN-A01-01"
                }
            ]
        };
        context.Stocktakes.Add(approvedStocktake);
        await context.SaveChangesAsync();
    }
    else
    {
        approvedStocktake.WarehouseId = primaryWarehouse.Id;
        approvedStocktake.Status = ReceiptStatus.Approved;
        approvedStocktake.CreatedBy = qaManagerUser.Id;
        approvedStocktake.CreatedAt = DateTime.UtcNow.AddDays(-2).AddHours(-2);
        approvedStocktake.ApprovedBy = qaAdmin.Id;
        approvedStocktake.ApprovedAt = DateTime.UtcNow.AddDays(-2);
        approvedStocktake.Note = "Kiểm kê định kỳ đợt 1 đã phê duyệt tại kệ BIN-A01-01";
        if (!approvedStocktake.Details.Any())
        {
            approvedStocktake.Details.Add(new StocktakeDetail
            {
                ProductId = product.Id,
                LocationId = storageBin.Id,
                SystemQuantity = 25m,
                ActualQuantity = 25m,
                DifferenceQuantity = 0m,
                Note = "Khớp 100% tồn kho thực tế đợt 1 tại kệ BIN-A01-01"
            });
            approvedStocktake.Details.Add(new StocktakeDetail
            {
                ProductId = lotProduct.Id,
                LocationId = storageBin.Id,
                SystemQuantity = 60m,
                ActualQuantity = 60m,
                DifferenceQuantity = 0m,
                Note = "Khớp 100% tồn kho lô cảm biến đợt 1 tại kệ BIN-A01-01"
            });
        }
        else
        {
            foreach (var d in approvedStocktake.Details)
            {
                d.DifferenceQuantity = 0m;
                d.ActualQuantity = d.SystemQuantity;
            }
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
            Note = "Kiểm kê định kỳ kệ lưu trữ BIN-A01-01",
            CreatedBy = qaManagerUser.Id,
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
                },
                new StocktakeDetail
                {
                    ProductId = lotProduct.Id,
                    LocationId = storageBin.Id,
                    SystemQuantity = 60m,
                    ActualQuantity = 60m,
                    DifferenceQuantity = 0m,
                    Note = "Khớp tồn kho lô cảm biến tại kệ BIN-A01-01"
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
        stocktake.Status = ReceiptStatus.Draft;
        stocktake.CreatedBy = qaManagerUser.Id;
        stocktake.Note = "Kiểm kê định kỳ kệ lưu trữ BIN-A01-01";
        var line1 = stocktake.Details.FirstOrDefault(d => d.ProductId == product.Id);
        if (line1 is null)
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
        else
        {
            line1.LocationId = storageBin.Id;
            line1.SystemQuantity = 25m;
            line1.ActualQuantity = 25m;
            line1.DifferenceQuantity = 0m;
            line1.Note = "Khớp tồn kho thực tế tại kệ BIN-A01-01";
        }

        var line2 = stocktake.Details.FirstOrDefault(d => d.ProductId == lotProduct.Id);
        if (line2 is null)
        {
            stocktake.Details.Add(new StocktakeDetail
            {
                ProductId = lotProduct.Id,
                LocationId = storageBin.Id,
                SystemQuantity = 60m,
                ActualQuantity = 60m,
                DifferenceQuantity = 0m,
                Note = "Khớp tồn kho lô cảm biến tại kệ BIN-A01-01"
            });
        }
        else
        {
            line2.LocationId = storageBin.Id;
            line2.SystemQuantity = 60m;
            line2.ActualQuantity = 60m;
            line2.DifferenceQuantity = 0m;
            line2.Note = "Khớp tồn kho lô cảm biến tại kệ BIN-A01-01";
        }
        await context.SaveChangesAsync();
    }

    var existingStocktakeAudits = await context.AuditLogs.IgnoreQueryFilters()
        .AsNoTracking()
        .Where(a => a.EntityName == "Stocktake" && a.EntityId == stocktake.Id)
        .ToListAsync();
    if (!existingStocktakeAudits.Any())
    {
        context.AuditLogs.Add(new AuditLog
        {
            UserId = qaManagerUser.Id,
            Action = "Stocktake.Created",
            EntityName = "Stocktake",
            EntityId = stocktake.Id,
            WarehouseId = primaryWarehouse.Id,
            OldValues = "Status: Draft",
            NewValues = "Status: Pending",
            Result = "Success",
            Reason = "Gửi yêu cầu phê duyệt phiếu kiểm kê định kỳ STK-QA-001",
            Timestamp = DateTime.UtcNow.AddHours(-2),
            IpAddress = "127.0.0.1"
        });
        await context.SaveChangesAsync();
    }
    else
    {
        await context.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE AuditLogs
            SET UserId = {qaManagerUser.Id},
                Action = {"Stocktake.Created"},
                OldValues = {"Status: Draft"},
                NewValues = {"Status: Pending"},
                Reason = {"Gửi yêu cầu phê duyệt phiếu kiểm kê định kỳ STK-QA-001"}
            WHERE EntityName = {"Stocktake"} AND EntityId = {stocktake.Id}");
    }

    // 5d. Idempotent Dock ("DOCK-01"), YardSlot ("YARD-A01"), and DockAppointment ("APT-QA-001") in primaryWarehouse
    var dock01 = await context.Docks.IgnoreQueryFilters()
        .FirstOrDefaultAsync(d => d.WarehouseId == primaryWarehouse.Id && d.Code == "DOCK-01");
    if (dock01 is null)
    {
        dock01 = new Dock
        {
            WarehouseId = primaryWarehouse.Id,
            Code = "DOCK-01",
            Name = "Cửa nhập xuất hàng số 01",
            SupportsInbound = true,
            SupportsOutbound = true,
            AllowedVehicleType = "XE TẢI / CONTAINER",
            IsTemperatureControlled = false,
            HazardAllowed = false,
            IsActive = true
        };
        context.Docks.Add(dock01);
        await context.SaveChangesAsync();
    }
    else
    {
        dock01.Name = "Cửa nhập xuất hàng số 01";
        dock01.SupportsInbound = true;
        dock01.SupportsOutbound = true;
        dock01.AllowedVehicleType = "XE TẢI / CONTAINER";
        dock01.IsActive = true;
        await context.SaveChangesAsync();
    }

    var yardA01 = await context.YardSlots.IgnoreQueryFilters()
        .FirstOrDefaultAsync(y => y.WarehouseId == primaryWarehouse.Id && y.Code == "YARD-A01");
    if (yardA01 is null)
    {
        yardA01 = new YardSlot
        {
            WarehouseId = primaryWarehouse.Id,
            Code = "YARD-A01",
            Name = "Bãi đỗ xe tải A01",
            IsActive = true
        };
        context.YardSlots.Add(yardA01);
        await context.SaveChangesAsync();
    }
    else
    {
        yardA01.Name = "Bãi đỗ xe tải A01";
        yardA01.IsActive = true;
        await context.SaveChangesAsync();
    }

    var aptQa001 = await context.DockAppointments.IgnoreQueryFilters()
        .Include(a => a.Events)
        .FirstOrDefaultAsync(a => a.WarehouseId == primaryWarehouse.Id && a.Code == "APT-QA-001");
    var aptNow = DateTime.UtcNow;
    if (aptQa001 is null)
    {
        aptQa001 = new DockAppointment
        {
            WarehouseId = primaryWarehouse.Id,
            Code = "APT-QA-001",
            Direction = DockAppointmentDirection.Inbound,
            Status = DockAppointmentStatus.DockAssigned,
            PlannedStartUtc = aptNow.AddMinutes(-45),
            PlannedEndUtc = aptNow.AddMinutes(75),
            CarrierCode = "VTP",
            CarrierName = "Viettel Post Logistics",
            VehiclePlate = "51C-889.99",
            TrailerPlate = "51R-102.88",
            VehicleType = "XE TẢI",
            RequiresTemperatureControl = false,
            Hazardous = false,
            DriverName = "Nguyễn Văn Tài",
            DriverPhone = "0909111222",
            SealNumber = "SEAL-QA-001",
            YardSlotId = yardA01.Id,
            DockId = dock01.Id,
            ArrivedAtUtc = aptNow.AddMinutes(-35),
            CheckedInAtUtc = aptNow.AddMinutes(-30),
            DockAssignedAtUtc = aptNow.AddMinutes(-20),
            Note = "Lịch hẹn xe giao hàng PO-QA-001 & ASN-QA-001 tại cửa DOCK-01",
            CreatedBy = qaAdmin.Id,
            CreatedAtUtc = aptNow.AddHours(-2),
            UpdatedBy = qaAdmin.Id,
            UpdatedAtUtc = aptNow.AddMinutes(-20),
            Events =
            [
                new DockAppointmentEvent
                {
                    EventType = "Created",
                    EventAtUtc = aptNow.AddHours(-2),
                    ActorUserId = qaAdmin.Id,
                    Note = "Khởi tạo lịch hẹn xe giao hàng APT-QA-001"
                },
                new DockAppointmentEvent
                {
                    EventType = "INBOUND_DOC_LINK",
                    EventAtUtc = aptNow.AddHours(-2).AddMinutes(1),
                    ActorUserId = qaAdmin.Id,
                    Note = $"PO:{purchaseOrder.Id}:PO-QA-001|ASN:{asn.Id}:ASN-QA-001|GR:{irQa001.Id}:IR-QA-001"
                },
                new DockAppointmentEvent
                {
                    EventType = "DockAssigned",
                    EventAtUtc = aptNow.AddMinutes(-20),
                    ActorUserId = qaAdmin.Id,
                    DockId = dock01.Id,
                    YardSlotId = yardA01.Id,
                    Note = "Đã điều phối xe 51C-889.99 vào cửa DOCK-01"
                }
            ]
        };
        context.DockAppointments.Add(aptQa001);
        await context.SaveChangesAsync();
    }
    else
    {
        aptQa001.Direction = DockAppointmentDirection.Inbound;
        aptQa001.Status = DockAppointmentStatus.DockAssigned;
        aptQa001.PlannedStartUtc = aptNow.AddMinutes(-45);
        aptQa001.PlannedEndUtc = aptNow.AddMinutes(75);
        aptQa001.CarrierCode = "VTP";
        aptQa001.CarrierName = "Viettel Post Logistics";
        aptQa001.VehiclePlate = "51C-889.99";
        aptQa001.TrailerPlate = "51R-102.88";
        aptQa001.VehicleType = "XE TẢI";
        aptQa001.DriverName = "Nguyễn Văn Tài";
        aptQa001.DriverPhone = "0909111222";
        aptQa001.SealNumber = "SEAL-QA-001";
        aptQa001.YardSlotId = yardA01.Id;
        aptQa001.DockId = dock01.Id;
        aptQa001.ArrivedAtUtc = aptNow.AddMinutes(-35);
        aptQa001.CheckedInAtUtc = aptNow.AddMinutes(-30);
        aptQa001.DockAssignedAtUtc = aptNow.AddMinutes(-20);
        aptQa001.CheckedOutAtUtc = null;
        aptQa001.Note = "Lịch hẹn xe giao hàng PO-QA-001 & ASN-QA-001 tại cửa DOCK-01";
        if (!aptQa001.Events.Any(e => e.EventType == "INBOUND_DOC_LINK"))
        {
            aptQa001.Events.Add(new DockAppointmentEvent
            {
                EventType = "INBOUND_DOC_LINK",
                EventAtUtc = aptNow.AddHours(-1),
                ActorUserId = qaAdmin.Id,
                Note = $"PO:{purchaseOrder.Id}:PO-QA-001|ASN:{asn.Id}:ASN-QA-001|GR:{irQa001.Id}:IR-QA-001"
            });
        }
        await context.SaveChangesAsync();
    }

    await context.Database.ExecuteSqlRawAsync(
        "DELETE FROM DockAppointmentEvents WHERE DockAppointmentId IN (SELECT Id FROM DockAppointments WHERE WarehouseId = {0} AND Code <> 'APT-QA-001'); " +
        "DELETE FROM DockAppointments WHERE WarehouseId = {0} AND Code <> 'APT-QA-001';",
        primaryWarehouse.Id);

    // 5e. Idempotent lotProduct stock, LOT-QA-001 & LOT-QA-002, InventoryLocks, InventoryLocationMovements & InternalMove reversal candidates for Wave-26
    var lotQa001 = await context.InventoryLots.IgnoreQueryFilters()
        .FirstOrDefaultAsync(l => l.ProductId == lotProduct.Id && l.LotNumber == "LOT-QA-001");
    if (lotQa001 is null)
    {
        lotQa001 = new InventoryLot
        {
            ProductId = lotProduct.Id,
            LotNumber = "LOT-QA-001",
            ManufactureDate = DateTime.UtcNow.Date.AddDays(-60),
            ExpiryDate = DateTime.UtcNow.Date.AddDays(305),
            CreatedAt = DateTime.UtcNow.AddDays(-60)
        };
        context.InventoryLots.Add(lotQa001);
        await context.SaveChangesAsync();
    }

    var lotQa002 = await context.InventoryLots.IgnoreQueryFilters()
        .FirstOrDefaultAsync(l => l.ProductId == lotProduct.Id && l.LotNumber == "LOT-QA-002");
    if (lotQa002 is null)
    {
        lotQa002 = new InventoryLot
        {
            ProductId = lotProduct.Id,
            LotNumber = "LOT-QA-002",
            ManufactureDate = DateTime.UtcNow.Date.AddDays(-30),
            ExpiryDate = DateTime.UtcNow.Date.AddDays(335),
            CreatedAt = DateTime.UtcNow.AddDays(-30)
        };
        context.InventoryLots.Add(lotQa002);
        await context.SaveChangesAsync();
    }

    var lotStorageStocks = await context.InventoryStocks.IgnoreQueryFilters()
        .Where(s =>
            s.WarehouseId == primaryWarehouse.Id &&
            s.ProductId == lotProduct.Id &&
            s.LocationId == storageBin.Id &&
            s.Status == InventoryStatus.Available &&
            s.SerialId == null)
        .OrderByDescending(s => s.LotId == lotQa002.Id)
        .ToListAsync();
    var lotStorageStock = lotStorageStocks.FirstOrDefault();
    foreach (var extraLotStock in lotStorageStocks.Skip(1))
    {
        context.InventoryStocks.Remove(extraLotStock);
    }
    if (lotStorageStock is null)
    {
        lotStorageStock = new InventoryStock
        {
            WarehouseId = primaryWarehouse.Id,
            ProductId = lotProduct.Id,
            LocationId = storageBin.Id,
            Status = InventoryStatus.Available,
            LotId = lotQa002.Id,
            Quantity = 60m,
            ReservedQuantity = 0m,
            LastUpdated = DateTime.UtcNow
        };
        context.InventoryStocks.Add(lotStorageStock);
    }
    else
    {
        lotStorageStock.LotId = lotQa002.Id;
        lotStorageStock.Quantity = 60m;
        lotStorageStock.ReservedQuantity = 0m;
        lotStorageStock.LastUpdated = DateTime.UtcNow;
    }
    await context.SaveChangesAsync();

    // Ensure IR-QA-001 has a posted line & Import ledger transaction for LOT-QA-002 (65 units)
    if (!irQa001.Details.Any(d => d.ProductId == lotProduct.Id))
    {
        irQa001.Details.Add(new ImportReceiptDetail
        {
            ProductId = lotProduct.Id,
            Quantity = 65m,
            UnitPrice = 420000m,
            Note = "Nhập kho lô cảm biến nhiệt độ LOT-QA-002 theo đơn mua PO-QA-001",
            OperationUnitId = unit.Id,
            OperationUnitCodeSnapshot = unit.Code,
            OperationUnitDecimalPlaces = unit.DecimalPlaces,
            BaseUnitId = unit.Id,
            BaseUnitCodeSnapshot = unit.Code,
            BaseUnitDecimalPlaces = unit.DecimalPlaces,
            ConversionFactor = 1m,
            ConversionVersion = 1,
            ExpectedQuantity = 65m,
            ReceivedQuantity = 65m,
            ObservedQuantity = 65m,
            FinalReceivedQuantity = 65m,
            AcceptedQuantity = 65m,
            PostedQuantity = 65m,
            BaseExpectedQuantity = 65m,
            BaseReceivedQuantity = 65m,
            BaseObservedQuantity = 65m,
            BaseFinalReceivedQuantity = 65m,
            BaseAcceptedQuantity = 65m
        });
        await context.SaveChangesAsync();
    }

    if (!await context.InventoryTransactions.IgnoreQueryFilters().AsNoTracking().AnyAsync(t =>
            t.WarehouseId == primaryWarehouse.Id &&
            t.ProductId == lotProduct.Id &&
            t.TransactionType == TransactionType.Import &&
            t.ReferenceType == "ImportReceipt" &&
            t.ReferenceId == irQa001.Id))
    {
        context.InventoryTransactions.Add(new InventoryTransaction
        {
            WarehouseId = primaryWarehouse.Id,
            ProductId = lotProduct.Id,
            LocationId = receivingLocation.Id,
            LotId = lotQa002.Id,
            InventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Import,
            Quantity = 65m,
            ReferenceType = "ImportReceipt",
            ReferenceId = irQa001.Id,
            TransactionDate = DateTime.UtcNow.AddDays(-2),
            CreatedBy = qaAdmin.Id,
            Note = "Ghi sổ phiếu nhập kho IR-QA-001 cho lô cảm biến LOT-QA-002 tại khu tiếp nhận RECEIVING"
        });
        await context.SaveChangesAsync();
    }
    else
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE InventoryTransactions SET LocationId = {receivingLocation.Id}, LotId = {lotQa002.Id}, InventoryStatus = {(int)InventoryStatus.Available}, Quantity = 65, Note = N'Ghi sổ phiếu nhập kho IR-QA-001 cho lô cảm biến LOT-QA-002 tại khu tiếp nhận RECEIVING' WHERE WarehouseId = {primaryWarehouse.Id} AND ProductId = {lotProduct.Id} AND TransactionType = {(int)TransactionType.Import} AND ReferenceType = 'ImportReceipt' AND ReferenceId = {irQa001.Id}");
    }

    // Ensure ER-QA-001 -> RSV-QA-LOT-002 -> ALC-QA-LOT-002 -> PICK-QA-003 -> PACK-QA-002 -> HU-QA-002 -> SHP-QA-002 outbound genealogy for LOT-QA-002 (5 units)
    var erQa001Lot = await context.ExportReceipts.IgnoreQueryFilters()
        .Include(e => e.Details)
        .OrderBy(e => e.Id)
        .FirstOrDefaultAsync(e => e.Code == "ER-QA-001" || e.WarehouseId == primaryWarehouse.Id);
    if (erQa001Lot is not null)
    {
        erQa001Lot.Status = ReceiptStatus.Dispatched;
        erQa001Lot.ApprovedBy ??= qaAdmin.Id;
        erQa001Lot.ApprovedAt = erQa001Lot.CreatedAt.AddMinutes(60);
        erQa001Lot.DispatchedAt = erQa001Lot.CreatedAt.AddMinutes(90);
        erQa001Lot.DispatchedBy ??= qaAdmin.Id;
        if (!erQa001Lot.Details.Any(d => d.ProductId == lotProduct.Id))
        {
            erQa001Lot.Details.Add(new ExportReceiptDetail
            {
                ProductId = lotProduct.Id,
                Quantity = 5m,
                UnitPrice = 1800000m,
                TaxPercent = 10m,
                Note = "Xuất giao lô cảm biến nhiệt độ LOT-QA-002",
                BaseUomIdSnapshot = unit.Id,
                BaseUomCodeSnapshot = unit.Code,
                BaseUomNameSnapshot = unit.Name,
                BaseUomDecimalPlacesSnapshot = unit.DecimalPlaces
            });
            await context.SaveChangesAsync();
        }

        var rsvLot002 = await context.StockReservations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.ReservationCode == "RSV-QA-LOT-002" ||
                                      (r.SourceType == "ExportReceipt" && r.SourceId == erQa001Lot.Id && r.ProductId == lotProduct.Id));
        if (rsvLot002 is null)
        {
            rsvLot002 = new StockReservation
            {
                ReservationCode = "RSV-QA-LOT-002",
                ProductId = lotProduct.Id,
                WarehouseId = primaryWarehouse.Id,
                Quantity = 5m,
                AllocatedQuantity = 0m,
                ConsumedQuantity = 5m,
                ReleasedQuantity = 0m,
                Status = StockReservationStatus.Consumed,
                SourceType = "ExportReceipt",
                SourceId = erQa001Lot.Id,
                SourceCode = erQa001Lot.Code,
                CreatedAt = DateTime.UtcNow.AddHours(-18),
                CreatedBy = qaAdmin.Id,
                ExpiresAt = DateTime.UtcNow.AddDays(30),
                AllocationVersion = 1
            };
            context.StockReservations.Add(rsvLot002);
            await context.SaveChangesAsync();
        }
        else
        {
            rsvLot002.ReservationCode = "RSV-QA-LOT-002";
            rsvLot002.ProductId = lotProduct.Id;
            rsvLot002.WarehouseId = primaryWarehouse.Id;
            rsvLot002.SourceType = "ExportReceipt";
            rsvLot002.SourceId = erQa001Lot.Id;
            rsvLot002.SourceCode = erQa001Lot.Code;
            rsvLot002.Quantity = 5m;
            rsvLot002.AllocatedQuantity = 0m;
            rsvLot002.ConsumedQuantity = 5m;
            rsvLot002.ReleasedQuantity = 0m;
            rsvLot002.Status = StockReservationStatus.Consumed;
            await context.SaveChangesAsync();
        }

        var alcLot002 = await context.StockAllocations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.AllocationCode == "ALC-QA-LOT-002" || a.ReservationId == rsvLot002.Id);
        if (alcLot002 is null)
        {
            alcLot002 = new StockAllocation
            {
                AllocationCode = "ALC-QA-LOT-002",
                ReservationId = rsvLot002.Id,
                WarehouseId = primaryWarehouse.Id,
                ProductId = lotProduct.Id,
                LocationId = storageBin.Id,
                LotId = lotQa002.Id,
                InventoryStatus = InventoryStatus.Available,
                Quantity = 5m,
                Status = StockAllocationStatus.Consumed,
                Strategy = AllocationStrategy.LocationOrder,
                SelectionReason = "Phân bổ theo FEFO lô LOT-QA-002 tại kệ BIN-A01-01",
                AllocatedAt = DateTime.UtcNow.AddHours(-17),
                AllocatedBy = qaAdmin.Id,
                Version = 1
            };
            context.StockAllocations.Add(alcLot002);
            await context.SaveChangesAsync();
        }
        else
        {
            alcLot002.AllocationCode = "ALC-QA-LOT-002";
            alcLot002.WarehouseId = primaryWarehouse.Id;
            alcLot002.ProductId = lotProduct.Id;
            alcLot002.LocationId = storageBin.Id;
            alcLot002.LotId = lotQa002.Id;
            alcLot002.InventoryStatus = InventoryStatus.Available;
            alcLot002.Quantity = 5m;
            alcLot002.Status = StockAllocationStatus.Consumed;
            alcLot002.SelectionReason = "Phân bổ theo FEFO lô LOT-QA-002 tại kệ BIN-A01-01";
            await context.SaveChangesAsync();
        }

        var pickQa003 = await context.PickingTasks.IgnoreQueryFilters()
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.TaskCode == "PICK-QA-003");
        if (pickQa003 is null)
        {
            pickQa003 = new PickingTask
            {
                TaskCode = "PICK-QA-003",
                SourceType = "ExportReceipt",
                SourceId = erQa001Lot.Id,
                SourceCode = erQa001Lot.Code,
                WarehouseId = primaryWarehouse.Id,
                PickingType = "SingleOrder",
                Status = PickingTaskStatus.Completed,
                Priority = 1,
                AssignedUserId = qaAdmin.Id,
                CreatedAt = DateTime.UtcNow.AddHours(-16),
                StartedAt = DateTime.UtcNow.AddHours(-15),
                CompletedAt = DateTime.UtcNow.AddHours(-14),
                CreatedBy = qaAdmin.Id,
                Lines =
                [
                    new PickingTaskLine
                    {
                        Sequence = 1,
                        AllocationId = alcLot002.Id,
                        ProductId = lotProduct.Id,
                        SourceLocationId = storageBin.Id,
                        RequestedQuantity = 5m,
                        PickedQuantity = 5m,
                        Status = PickingTaskLineStatus.Picked
                    }
                ]
            };
            context.PickingTasks.Add(pickQa003);
            await context.SaveChangesAsync();
        }
        else
        {
            pickQa003.SourceType = "ExportReceipt";
            pickQa003.SourceId = erQa001Lot.Id;
            pickQa003.SourceCode = erQa001Lot.Code;
            pickQa003.WarehouseId = primaryWarehouse.Id;
            pickQa003.Status = PickingTaskStatus.Completed;
            pickQa003.CompletedAt ??= DateTime.UtcNow.AddHours(-14);
            foreach (var l in pickQa003.Lines)
            {
                l.AllocationId = alcLot002.Id;
                l.ProductId = lotProduct.Id;
                l.SourceLocationId = storageBin.Id;
                l.RequestedQuantity = 5m;
                l.PickedQuantity = 5m;
                l.Status = PickingTaskLineStatus.Picked;
            }
            await context.SaveChangesAsync();
        }

        var pickQa003Line = pickQa003.Lines.First();
        var packQa002 = await context.PackingSessions.IgnoreQueryFilters()
            .Include(s => s.HandlingUnits)
            .ThenInclude(h => h.Contents)
            .FirstOrDefaultAsync(s => s.SessionCode == "PACK-QA-002" || s.PickingTaskId == pickQa003.Id);
        if (packQa002 is null)
        {
            packQa002 = new PackingSession
            {
                SessionCode = "PACK-QA-002",
                PickingTaskId = pickQa003.Id,
                WarehouseId = primaryWarehouse.Id,
                Status = PackingSessionStatus.Packed,
                CreatedAt = DateTime.UtcNow.AddHours(-14),
                StartedAt = DateTime.UtcNow.AddHours(-14),
                PackedAt = DateTime.UtcNow.AddHours(-13),
                CreatedBy = qaAdmin.Id,
                HandlingUnits =
                [
                    new HandlingUnit
                    {
                        HuCode = "HU-QA-002",
                        Barcode = "BC-HU-QA-002",
                        Sscc = "008938505000000025",
                        WarehouseId = primaryWarehouse.Id,
                        Type = HandlingUnitType.Carton,
                        Status = HandlingUnitStatus.Shipped,
                        GrossWeightKg = 6.2m,
                        NetWeightKg = 6.0m,
                        VolumeM3 = 0.06m,
                        CreatedAt = DateTime.UtcNow.AddHours(-13),
                        CreatedBy = qaAdmin.Id,
                        Contents =
                        [
                            new HandlingUnitContent
                            {
                                PickingTaskLineId = pickQa003Line.Id,
                                ProductId = lotProduct.Id,
                                Quantity = 5m,
                                PackedAt = DateTime.UtcNow.AddHours(-13),
                                PackedBy = qaAdmin.Id
                            }
                        ]
                    }
                ]
            };
            context.PackingSessions.Add(packQa002);
            await context.SaveChangesAsync();
        }
        else
        {
            packQa002.SessionCode = "PACK-QA-002";
            packQa002.PickingTaskId = pickQa003.Id;
            packQa002.WarehouseId = primaryWarehouse.Id;
            packQa002.Status = PackingSessionStatus.Packed;
            packQa002.PackedAt ??= DateTime.UtcNow.AddHours(-13);
            var hu2 = packQa002.HandlingUnits.FirstOrDefault(h => h.HuCode == "HU-QA-002")
                      ?? packQa002.HandlingUnits.FirstOrDefault();
            if (hu2 is null)
            {
                hu2 = new HandlingUnit
                {
                    HuCode = "HU-QA-002",
                    Barcode = "BC-HU-QA-002",
                    Sscc = "008938505000000025",
                    WarehouseId = primaryWarehouse.Id,
                    PackingSessionId = packQa002.Id,
                    Type = HandlingUnitType.Carton,
                    Status = HandlingUnitStatus.Shipped,
                    GrossWeightKg = 6.2m,
                    NetWeightKg = 6.0m,
                    VolumeM3 = 0.06m,
                    CreatedAt = DateTime.UtcNow.AddHours(-13),
                    CreatedBy = qaAdmin.Id,
                    Contents =
                    [
                        new HandlingUnitContent
                        {
                            PickingTaskLineId = pickQa003Line.Id,
                            ProductId = lotProduct.Id,
                            Quantity = 5m,
                            PackedAt = DateTime.UtcNow.AddHours(-13),
                            PackedBy = qaAdmin.Id
                        }
                    ]
                };
                packQa002.HandlingUnits.Add(hu2);
            }
            else
            {
                hu2.HuCode = "HU-QA-002";
                hu2.Barcode = "BC-HU-QA-002";
                hu2.Sscc = "008938505000000025";
                hu2.WarehouseId = primaryWarehouse.Id;
                hu2.ParentHandlingUnitId = null;
                hu2.Status = HandlingUnitStatus.Shipped;
                hu2.SealedAt ??= DateTime.UtcNow.AddHours(-13);
                if (!hu2.Contents.Any(c => c.PickingTaskLineId == pickQa003Line.Id))
                {
                    hu2.Contents.Add(new HandlingUnitContent
                    {
                        PickingTaskLineId = pickQa003Line.Id,
                        ProductId = lotProduct.Id,
                        Quantity = 5m,
                        PackedAt = DateTime.UtcNow.AddHours(-13),
                        PackedBy = qaAdmin.Id
                    });
                }
            }
            await context.SaveChangesAsync();
        }

        var huQa002 = packQa002.HandlingUnits.First();
        var shpQa002 = await context.Shipments.IgnoreQueryFilters()
            .Include(s => s.HandlingUnits)
            .FirstOrDefaultAsync(s => s.ShipmentCode == "SHP-QA-002" || s.PackingSessionId == packQa002.Id);
        if (shpQa002 is null)
        {
            shpQa002 = new Shipment
            {
                ShipmentCode = "SHP-QA-002",
                PackingSessionId = packQa002.Id,
                WarehouseId = primaryWarehouse.Id,
                SourceType = "ExportReceipt",
                SourceId = erQa001Lot.Id,
                SourceCode = erQa001Lot.Code,
                Status = ShipmentStatus.Delivered,
                StagingLocationId = stagingLocation.Id,
                VehiclePlate = "51D-668.88",
                TrailerPlate = "51R-208.99",
                SealNumber = "SEAL-QA-002",
                CreatedAt = DateTime.UtcNow.AddHours(-12),
                CreatedBy = qaAdmin.Id,
                DispatchedAt = DateTime.UtcNow.AddHours(-12),
                DispatchedBy = qaAdmin.Id,
                CompletedAt = DateTime.UtcNow.AddHours(-6),
                HandlingUnits =
                [
                    new ShipmentHandlingUnit
                    {
                        HandlingUnitId = huQa002.Id,
                        Sequence = 1,
                        AssignedAt = DateTime.UtcNow.AddHours(-12),
                        StagedAt = DateTime.UtcNow.AddHours(-12),
                        LoadedAt = DateTime.UtcNow.AddHours(-12),
                        LoadedBy = qaAdmin.Id
                    }
                ]
            };
            context.Shipments.Add(shpQa002);
            await context.SaveChangesAsync();
        }
        else
        {
            shpQa002.ShipmentCode = "SHP-QA-002";
            shpQa002.PackingSessionId = packQa002.Id;
            shpQa002.WarehouseId = primaryWarehouse.Id;
            shpQa002.SourceType = "ExportReceipt";
            shpQa002.SourceId = erQa001Lot.Id;
            shpQa002.SourceCode = erQa001Lot.Code;
            shpQa002.Status = ShipmentStatus.Delivered;
            shpQa002.DispatchedAt ??= DateTime.UtcNow.AddHours(-12);
            shpQa002.DispatchedBy ??= qaAdmin.Id;
            if (!shpQa002.HandlingUnits.Any(x => x.HandlingUnitId == huQa002.Id))
            {
                shpQa002.HandlingUnits.Add(new ShipmentHandlingUnit
                {
                    HandlingUnitId = huQa002.Id,
                    Sequence = 1,
                    AssignedAt = DateTime.UtcNow.AddHours(-12),
                    StagedAt = DateTime.UtcNow.AddHours(-12),
                    LoadedAt = DateTime.UtcNow.AddHours(-12),
                    LoadedBy = qaAdmin.Id
                });
            }
            await context.SaveChangesAsync();
        }

        if (!await context.InventoryTransactions.IgnoreQueryFilters().AsNoTracking().AnyAsync(t =>
                t.WarehouseId == primaryWarehouse.Id &&
                t.ProductId == lotProduct.Id &&
                t.TransactionType == TransactionType.Ship &&
                t.ReferenceType == "Shipment" &&
                t.ReferenceId == shpQa002.Id))
        {
            context.InventoryTransactions.Add(new InventoryTransaction
            {
                WarehouseId = primaryWarehouse.Id,
                ProductId = lotProduct.Id,
                LocationId = storageBin.Id,
                LotId = lotQa002.Id,
                InventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Ship,
                Quantity = 5m,
                ReferenceType = "Shipment",
                ReferenceId = shpQa002.Id,
                TransactionDate = DateTime.UtcNow.AddHours(-12),
                CreatedBy = qaAdmin.Id,
                Note = "Xuất giao 5 hộp cảm biến nhiệt độ QA-02 (lô LOT-QA-002) trên chuyến giao SHP-QA-002 theo phiếu xuất ER-QA-001"
            });
            await context.SaveChangesAsync();
        }
        else
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE InventoryTransactions SET LocationId = {storageBin.Id}, LotId = {lotQa002.Id}, InventoryStatus = {(int)InventoryStatus.Available}, Quantity = 5, Note = N'Xuất giao 5 hộp cảm biến nhiệt độ QA-02 (lô LOT-QA-002) trên chuyến giao SHP-QA-002 theo phiếu xuất ER-QA-001' WHERE WarehouseId = {primaryWarehouse.Id} AND ProductId = {lotProduct.Id} AND TransactionType = {(int)TransactionType.Ship} AND ReferenceType = 'Shipment' AND ReferenceId = {shpQa002.Id}");
        }
    }

    var activeQcLock = await context.InventoryLocks.IgnoreQueryFilters()
        .FirstOrDefaultAsync(l =>
            l.WarehouseId == primaryWarehouse.Id &&
            l.ProductId == lotProduct.Id &&
            l.LockType == InventoryLockType.ManualOperationalLock);
    if (activeQcLock is null)
    {
        activeQcLock = new InventoryLock
        {
            WarehouseId = primaryWarehouse.Id,
            LocationId = storageBin.Id,
            ProductId = lotProduct.Id,
            LotId = lotQa002.Id,
            InventoryStatus = InventoryStatus.QcHold,
            LockType = InventoryLockType.ManualOperationalLock,
            Status = InventoryLockStatus.Active,
            Reason = "Phong tỏa lô LOT-QA-002 tại kệ BIN-A01-01 chờ kiểm định chất lượng định kỳ",
            CreatedBy = qaAdmin.Id,
            CreatedAt = DateTime.UtcNow.AddHours(-6),
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        context.InventoryLocks.Add(activeQcLock);
    }
    else
    {
        activeQcLock.LocationId = storageBin.Id;
        activeQcLock.LotId = lotQa002.Id;
        activeQcLock.InventoryStatus = InventoryStatus.QcHold;
        activeQcLock.Status = InventoryLockStatus.Active;
        activeQcLock.Reason = "Phong tỏa lô LOT-QA-002 tại kệ BIN-A01-01 chờ kiểm định chất lượng định kỳ";
        activeQcLock.ExpiresAt = DateTime.UtcNow.AddDays(7);
        activeQcLock.ReleasedAt = null;
        activeQcLock.ReleasedBy = null;
        activeQcLock.ReleaseReason = null;
    }

    var releasedCountLock = await context.InventoryLocks.IgnoreQueryFilters()
        .FirstOrDefaultAsync(l =>
            l.WarehouseId == primaryWarehouse.Id &&
            l.LocationId == storageBin.Id &&
            l.ProductId == product.Id &&
            l.LockType == InventoryLockType.CountFreeze);
    if (releasedCountLock is null)
    {
        releasedCountLock = new InventoryLock
        {
            WarehouseId = primaryWarehouse.Id,
            LocationId = storageBin.Id,
            ProductId = product.Id,
            InventoryStatus = InventoryStatus.Available,
            LockType = InventoryLockType.CountFreeze,
            Status = InventoryLockStatus.Released,
            Reason = "Khóa tạm thời kệ BIN-A01-01 phục vụ đối chiếu kiểm kê định kỳ",
            CreatedBy = qaAdmin.Id,
            CreatedAt = DateTime.UtcNow.AddHours(-24),
            ReleasedBy = qaAdmin.Id,
            ReleasedAt = DateTime.UtcNow.AddHours(-20),
            ReleaseReason = "Đã hoàn tất đối chiếu số liệu kiểm kê tại kệ BIN-A01-01"
        };
        context.InventoryLocks.Add(releasedCountLock);
    }
    else
    {
        releasedCountLock.Status = InventoryLockStatus.Released;
        releasedCountLock.Reason = "Khóa tạm thời kệ BIN-A01-01 phục vụ đối chiếu kiểm kê định kỳ";
        releasedCountLock.ReleasedBy = qaAdmin.Id;
        releasedCountLock.ReleasedAt ??= DateTime.UtcNow.AddHours(-20);
        releasedCountLock.ReleaseReason = "Đã hoàn tất đối chiếu số liệu kiểm kê tại kệ BIN-A01-01";
    }

    if (!await context.InventoryLocationMovements.IgnoreQueryFilters().AsNoTracking().AnyAsync(m =>
            m.WarehouseId == primaryWarehouse.Id &&
            m.ProductId == product.Id &&
            m.FromLocationId == receivingLocation.Id &&
            m.ToLocationId == storageBin.Id))
    {
        var locationMove = new InventoryLocationMovement
        {
            WarehouseId = primaryWarehouse.Id,
            ProductId = product.Id,
            InventoryStatus = InventoryStatus.Available,
            FromLocationId = receivingLocation.Id,
            ToLocationId = storageBin.Id,
            BaseQuantity = 10m,
            EnteredQuantity = 10m,
            EnteredUnitCode = "THÙNG",
            ReferenceType = "InventoryMove",
            CreatedBy = qaAdmin.Id,
            CreatedAt = DateTime.UtcNow.AddHours(-3)
        };
        context.InventoryLocationMovements.Add(locationMove);
        await context.SaveChangesAsync();
        locationMove.ReferenceId = locationMove.Id;
        await context.SaveChangesAsync();
    }

    if (!await context.InventoryTransactions.IgnoreQueryFilters().AsNoTracking().AnyAsync(t =>
            t.WarehouseId == primaryWarehouse.Id &&
            t.TransactionType == TransactionType.Move &&
            t.ReferenceType == "InternalMove" &&
            t.ReferenceId == 9001))
    {
        context.InventoryTransactions.Add(new InventoryTransaction
        {
            WarehouseId = primaryWarehouse.Id,
            ProductId = product.Id,
            LocationId = storageBin.Id,
            FromLocationId = receivingLocation.Id,
            ToLocationId = storageBin.Id,
            InventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Move,
            Quantity = 10m,
            ReferenceType = "InternalMove",
            ReferenceId = 9001,
            TransactionDate = DateTime.UtcNow.AddHours(-3),
            CreatedBy = qaAdmin.Id,
            Note = "Điều chuyển nội bộ 10 thùng linh kiện QA-01 từ khu tiếp nhận RECEIVING vào kệ lưu trữ BIN-A01-01"
        });
    }

    if (!await context.InventoryTransactions.IgnoreQueryFilters().AsNoTracking().AnyAsync(t =>
            t.WarehouseId == primaryWarehouse.Id &&
            t.TransactionType == TransactionType.Move &&
            t.ReferenceType == "InternalMove" &&
            t.ReferenceId == 9002))
    {
        context.InventoryTransactions.Add(new InventoryTransaction
        {
            WarehouseId = primaryWarehouse.Id,
            ProductId = lotProduct.Id,
            LocationId = storageBin.Id,
            FromLocationId = receivingLocation.Id,
            ToLocationId = storageBin.Id,
            LotId = lotQa002.Id,
            InventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Move,
            Quantity = 5m,
            ReferenceType = "InternalMove",
            ReferenceId = 9002,
            TransactionDate = DateTime.UtcNow.AddHours(-19),
            CreatedBy = qaAdmin.Id,
            Note = "Điều chuyển nội bộ 5 hộp cảm biến nhiệt độ QA-02 (lô LOT-QA-002) từ RECEIVING vào kệ BIN-A01-01"
        });
    }
    else
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE InventoryTransactions SET LotId = {lotQa002.Id}, LocationId = {storageBin.Id}, FromLocationId = {receivingLocation.Id}, ToLocationId = {storageBin.Id} WHERE WarehouseId = {primaryWarehouse.Id} AND TransactionType = {(int)TransactionType.Move} AND ReferenceType = 'InternalMove' AND ReferenceId = 9002");
    }

    var allAllocations = await context.StockAllocations.IgnoreQueryFilters()
        .OrderBy(a => a.Id)
        .ToListAsync();
    var alcCounter = 3;
    foreach (var alloc in allAllocations)
    {
        if (!string.Equals(alloc.AllocationCode, "ALC-QA-PICK-001", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(alloc.AllocationCode, "ALC-QA-PACK-001", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(alloc.AllocationCode, "ALC-QA-LOT-002", StringComparison.OrdinalIgnoreCase))
        {
            alloc.AllocationCode = $"ALC-QA-{alcCounter:D3}";
            alcCounter++;
        }

        if (string.IsNullOrWhiteSpace(alloc.SelectionReason) ||
            alloc.SelectionReason.Contains("Seed", StringComparison.OrdinalIgnoreCase) ||
            alloc.SelectionReason.Contains("Auto", StringComparison.OrdinalIgnoreCase))
        {
            alloc.SelectionReason = "Phân bổ tự động theo kệ lưu trữ ưu tiên FIFO (BIN-A01-01)";
        }

        if (!string.IsNullOrWhiteSpace(alloc.ReleaseReason) &&
            (alloc.ReleaseReason.Contains("Seed", StringComparison.OrdinalIgnoreCase) ||
             alloc.ReleaseReason.Contains("Release", StringComparison.OrdinalIgnoreCase) ||
             alloc.ReleaseReason.Contains("Auto", StringComparison.OrdinalIgnoreCase)))
        {
            alloc.ReleaseReason = "Giải phóng phân bổ tự động để tái điều phối kệ hàng";
        }
    }

    var allReservations = await context.StockReservations.IgnoreQueryFilters()
        .OrderBy(r => r.Id)
        .ToListAsync();
    var rsvCounter = 3;
    foreach (var rsv in allReservations)
    {
        if (!string.Equals(rsv.ReservationCode, "RSV-QA-PICK-001", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(rsv.ReservationCode, "RSV-QA-PACK-001", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(rsv.ReservationCode, "RSV-QA-LOT-002", StringComparison.OrdinalIgnoreCase))
        {
            rsv.ReservationCode = $"RSV-QA-{rsvCounter:D3}";
            if (rsv.SourceCode != null && rsv.SourceCode.Contains("QA-EXP", StringComparison.OrdinalIgnoreCase))
            {
                rsv.SourceCode = $"ER-QA-{rsvCounter - 2:D3}";
            }
            rsvCounter++;
        }
    }
    await context.SaveChangesAsync();

    await context.Database.ExecuteSqlRawAsync(
        "UPDATE InventoryLocationMovements SET EnteredUnitCode = N'THÙNG' WHERE EnteredUnitCode IN ('QA_UNIT', 'QA-PCS') OR EnteredUnitCode IS NULL; " +
        "UPDATE InventoryTransactions SET CorrectiveTransactionId = NULL, Note = N'Điều chuyển nội bộ 10 thùng linh kiện QA-01 từ khu tiếp nhận RECEIVING vào kệ lưu trữ BIN-A01-01' WHERE ReferenceType = 'InternalMove' AND ReferenceId = 9001; " +
        "UPDATE InventoryTransactions SET CorrectiveTransactionId = NULL, Note = N'Điều chuyển nội bộ 5 hộp cảm biến nhiệt độ QA-02 (lô LOT-QA-002) từ khu tiếp nhận RECEIVING vào kệ BIN-A01-01' WHERE ReferenceType = 'InternalMove' AND ReferenceId = 9002; " +
        "UPDATE InventoryTransactions SET Note = N'Tồn kho tại khu tiếp nhận RECEIVING cho nhiệm vụ cất hàng PUT-QA-001' WHERE Note = N'Tồn kho tại RECEIVING cho nhiệm vụ cất hàng PUT-QA-001'; " +
        "UPDATE InventoryTransactions SET Note = REPLACE(Note, N'chờ QC', N'chờ kiểm định chất lượng') WHERE Note LIKE N'%chờ QC%'; " +
        "UPDATE InventoryTransactions SET Note = REPLACE(Note, N'(100% Match)', N'(Khớp 100%)') WHERE Note LIKE N'%(100% Match)%'; " +
        "UPDATE ImportReceipts SET ApprovedAt = DATEADD(MINUTE, 150, CreatedAt) WHERE Status IN (1, 2) AND (ApprovedAt IS NULL OR DATEDIFF(MINUTE, CreatedAt, ApprovedAt) < 30); " +
        "UPDATE ExportReceipts SET ApprovedAt = DATEADD(MINUTE, 60, CreatedAt), DispatchedAt = DATEADD(MINUTE, 90, CreatedAt) WHERE Status IN (1, 2, 4) AND (DispatchedAt IS NULL OR DATEDIFF(MINUTE, CreatedAt, DispatchedAt) < 30);");

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
    supplier.CreditLimit = 100_000_000m;
    supplier.PaymentTermDays = 30;
    customer.CreditLimit = 80_000_000m;
    customer.PaymentTermDays = 30;
    foreach (var bp in allPartners.Where(x => x.IsActive && x.CreditLimit <= 0m))
    {
        bp.CreditLimit = bp.IsCustomer ? 80_000_000m : 100_000_000m;
        if (bp.PaymentTermDays <= 0) bp.PaymentTermDays = 30;
    }
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
                var dominantBucket = pairStocks
                    .Where(s => s.Status == status)
                    .OrderByDescending(s => s.Quantity)
                    .FirstOrDefault();
                context.InventoryTransactions.Add(new InventoryTransaction
                {
                    TenantId = tenantId,
                    WarehouseId = warehouseId,
                    ProductId = productId,
                    LocationId = dominantBucket?.LocationId ?? storageBin.Id,
                    LotId = dominantBucket?.LotId,
                    SerialId = dominantBucket?.SerialId,
                    InventoryStatus = status,
                    TransactionType = delta > 0m ? TransactionType.Import : TransactionType.AdjustmentDecrease,
                    Quantity = Math.Abs(delta),
                    ReferenceType = "QASeedReconcile",
                    ReferenceId = productId,
                    TransactionDate = DateTime.UtcNow.AddMinutes(-1),
                    CreatedBy = qaAdmin.Id,
                    Note = "Đồng bộ đối chiếu sổ cái tồn kho chuẩn QA (Khớp 100%)"
                });
                reconciledPairCount++;
            }
        }
    }
    await context.SaveChangesAsync();

    // 7b. Populate Wave-44 Canonical columns & tables (MAC, UnitCost, UoM Snapshot, E-commerce AWB, ProductBundleComponents, IntegrationOutboxMessages)
    await context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE dbo.Products SET MovingAverageCost = 250000 WHERE Id = {product.Id};
UPDATE dbo.Products SET MovingAverageCost = 420000 WHERE Id = {lotProduct.Id};
UPDATE dbo.Products SET MovingAverageCost = 250000 WHERE MovingAverageCost <= 0;

UPDATE dbo.InventoryStocks SET MovingAverageCost = 250000 WHERE ProductId = {product.Id};
UPDATE dbo.InventoryStocks SET MovingAverageCost = 420000 WHERE ProductId = {lotProduct.Id};
UPDATE dbo.InventoryStocks SET MovingAverageCost = 250000 WHERE MovingAverageCost <= 0;

UPDATE dbo.InventoryTransactions SET UnitCost = 250000 WHERE ProductId = {product.Id} AND UnitCost <= 0;
UPDATE dbo.InventoryTransactions SET UnitCost = 420000 WHERE ProductId = {lotProduct.Id} AND UnitCost <= 0;

UPDATE dbo.StockReservations
SET BaseUomIdSnapshot = {unit.Id},
    BaseUomCodeSnapshot = N'THÙNG',
    BaseUomDecimalPlacesSnapshot = 0
WHERE BaseUomIdSnapshot <= 0 OR BaseUomCodeSnapshot IS NULL OR BaseUomCodeSnapshot = '';

UPDATE dbo.ExportReceipts
SET MarketplaceOrderId = N'SHOPEE-VN-20261021-01',
    CarrierCode = N'VTP',
    TrackingNumber = N'VTP8938505001'
WHERE Code = 'ER-QA-001';

UPDATE dbo.ExportReceipts
SET MarketplaceOrderId = N'LAZADA-VN-20261021-02',
    CarrierCode = N'GHN',
    TrackingNumber = N'GHN8938505002'
WHERE Code = 'ER-QA-002';

DELETE FROM dbo.IntegrationOutboxMessages WHERE TenantId = 1 AND EventId IN (N'OUTBOX-GL-001', N'OUTBOX-TMS-002', N'OUTBOX-APAR-003', N'OUTBOX-WMS-004');
INSERT INTO dbo.IntegrationOutboxMessages (TenantId, EventId, EventType, AggregateType, AggregateId, PayloadJson, Status, RetryCount, CreatedAt, PublishedAt)
VALUES
(1, N'OUTBOX-GL-001', N'Kết chuyển Bút toán Sổ cái Chung', N'ImportReceipt', {irQa001.Id}, N'{{""documentCode"":""IR-QA-001"",""targetSystem"":""Phân hệ Kế toán Tài chính"",""valuationAmount"":27300000}}', N'DISPATCHED', 0, DATEADD(MINUTE, -18, SYSUTCDATETIME()), DATEADD(MINUTE, -15, SYSUTCDATETIME())),
(1, N'OUTBOX-TMS-002', N'Điều phối Vận đơn & Xuất bến', N'Shipment', {shipment.Id}, N'{{""documentCode"":""SHP-QA-001"",""targetSystem"":""Hệ thống Quản lý Vận tải"",""carrierCode"":""VTP""}}', N'DISPATCHED', 0, DATEADD(MINUTE, -14, SYSUTCDATETIME()), DATEADD(MINUTE, -12, SYSUTCDATETIME())),
(1, N'OUTBOX-APAR-003', N'Hạch toán Hóa đơn Công nợ', N'PartnerDebtInvoice', {apInvoice.Id}, N'{{""documentCode"":""AP-IR-QA0001"",""targetSystem"":""Sổ phụ Công nợ Đối tác"",""totalAmount"":27500000}}', N'DISPATCHED', 0, DATEADD(MINUTE, -10, SYSUTCDATETIME()), DATEADD(MINUTE, -8, SYSUTCDATETIME())),
(1, N'OUTBOX-WMS-004', N'Chốt Số dư Sổ cái Kho Cuối kỳ', N'Stocktake', {stocktake.Id}, N'{{""documentCode"":""STK-QA-001"",""targetSystem"":""Kho Dữ liệu Đối soát Tập trung"",""totalOnHand"":220}}', N'DISPATCHED', 0, DATEADD(MINUTE, -6, SYSUTCDATETIME()), DATEADD(MINUTE, -4, SYSUTCDATETIME()));
");

    var dualPartner = allPartners.FirstOrDefault(p => p.Code == "DT-QA-01" || (p.IsSupplier && p.IsCustomer && p.IsActive));
    if (dualPartner is null)
    {
        dualPartner = new BusinessPartner
        {
            Code = "DT-QA-01",
            Name = "Công ty Cổ phần Logistics & Thương Mại Mekong QA",
            IsSupplier = true,
            IsCustomer = true,
            IsActive = true,
            Phone = "0909666888",
            Email = "mekong-logistics@qa.local",
            Address = "Khu Công Nghệ Cao Quận 9, TP.HCM",
            CreditLimit = 150_000_000m,
            PaymentTermDays = 45,
            PayableBalance = 0m,
            ReceivableBalance = 0m
        };
        context.BusinessPartners.Add(dualPartner);
    }
    else
    {
        dualPartner.IsSupplier = true;
        dualPartner.IsCustomer = true;
        dualPartner.IsActive = true;
        dualPartner.Phone ??= "0909666888";
        dualPartner.Email ??= "mekong-logistics@qa.local";
        dualPartner.Address ??= "Khu Công Nghệ Cao Quận 9, TP.HCM";
        if (dualPartner.CreditLimit <= 0m) dualPartner.CreditLimit = 150_000_000m;
        if (dualPartner.PaymentTermDays <= 0) dualPartner.PaymentTermDays = 45;
    }

    foreach (var bp in allPartners.Where(x => x.IsActive))
    {
        if (bp.CreditLimit <= 0m) bp.CreditLimit = bp.IsCustomer ? 80_000_000m : 100_000_000m;
        if (bp.PaymentTermDays <= 0) bp.PaymentTermDays = 30;
        bp.Phone ??= "0909000111";
        bp.Email ??= $"{bp.Code.ToLowerInvariant()}@qa.local";
        bp.Address ??= "TP.HCM";
    }

    await context.Database.ExecuteSqlRawAsync(
        "DELETE FROM AuditLogs WHERE IpAddress = '10.0.10.25' OR (EntityName = 'UserSession' AND (Action LIKE '%Refresh%' OR Action LIKE N'%Làm mới%')) OR EntityName = 'LocalQaOperationalSeed' OR Action LIKE 'QA.%';");

    context.AuditLogs.Add(new AuditLog
    {
        UserId = qaAdmin.Id,
        WarehouseId = primaryWarehouse.Id,
        EntityName = "WarehouseCalendarException",
        EntityId = primaryWarehouse.Id,
        Action = "EMERGENCY_OPEN_OVERRIDE",
        OldValues = $"{DateTime.UtcNow.Date.AddDays(3):yyyy-MM-dd} 22:00..23:59",
        NewValues = $"{DateTime.UtcNow.Date.AddDays(3):yyyy-MM-dd} 22:00..23:59",
        Reason = "Mở kho khẩn cấp ngoài giờ phục vụ tiếp nhận lô hàng ưu tiên PO-QA-001",
        Result = "Success",
        Timestamp = DateTime.UtcNow.AddDays(-2),
        IpAddress = "10.0.10.25"
    });
    context.AuditLogs.Add(new AuditLog
    {
        UserId = qaAdmin.Id,
        WarehouseId = primaryWarehouse.Id,
        EntityName = "WarehouseCalendarException",
        EntityId = primaryWarehouse.Id,
        Action = "MAINTENANCE_WINDOW",
        OldValues = $"{DateTime.UtcNow.Date.AddDays(7):yyyy-MM-dd} 02:00..04:00",
        NewValues = $"{DateTime.UtcNow.Date.AddDays(7):yyyy-MM-dd} 02:00..04:00",
        Reason = "Bảo trì định kỳ hệ thống băng chuyền & cân điện tử khu tiếp nhận DOCK-01",
        Result = "Success",
        Timestamp = DateTime.UtcNow.AddDays(-3),
        IpAddress = "10.0.10.25"
    });

    var operationalAudits = new (string Action, string EntityName, int EntityId, string Reason, int MinutesAgo)[]
    {
        ("Ghi sổ phiếu nhập kho", "ImportReceipt", irQa001.Id, "Ghi sổ phiếu nhập IR-QA-001 (65 thùng cảm biến LOT-QA-002) tại khu tiếp nhận TIEP-NHAN", 10),
        ("Hoàn tất cất hàng vào kệ", "PutawayTask", putawayTask.Id, "Hoàn tất tác vụ cất hàng PUT-QA-001 từ khu tiếp nhận vào kệ lưu trữ BIN-A01-01", 9),
        ("Hoàn tất lấy hàng theo đơn", "PickingTask", pickingTask.Id, "Hoàn tất lấy hàng PICK-QA-003 theo chiến lược FEFO lô LOT-QA-002 tại kệ BIN-A01-01", 8),
        ("Đóng gói đơn vị xử lý", "PackingSession", packingSession.Id, "Đóng gói hoàn tất kiện hàng HU-QA-002 (BC-HU-QA-002) thuộc phiên PACK-QA-002", 7),
        ("Xuất giao chuyến hàng", "Shipment", shipment.Id, "Xuất giao chuyến hàng SHP-QA-002 (xe 51D-668.88, niêm phong SEAL-QA-002) theo phiếu ER-QA-001", 6),
        ("Di chuyển vị trí nội bộ", "InventoryLocationMovement", 9001, "Điều chuyển nội bộ MOV-QA-9001 (10 thùng linh kiện) từ TIEP-NHAN sang kệ lưu trữ BIN-A01-01", 5),
        ("Phong tỏa lô hàng chờ kiểm định", "InventoryLock", activeQcLock.Id, "Phong tỏa lô LOT-QA-002 tại kệ BIN-A01-01 chờ kiểm định chất lượng định kỳ", 4),
        ("Phê duyệt điều chỉnh kiểm kê", "Stocktake", 1, "Phê duyệt biên bản kiểm kê định kỳ STK-QA-001 tại Kho Chính TP.HCM", 3),
        ("Phát hành bảng kê phí dịch vụ 3PL", "ThreePlBillingStatement", dualPartner.Id, "Phát hành bảng kê phí dịch vụ lưu kho & hạch toán hóa đơn phải thu 3PL cho đối tác DT-QA-01", 2),
        ("Đồng bộ bản tin tích hợp hệ thống", "IntegrationOutboxBatch", irQa001.Id, "Chuyển phát thành công lô bản tin tích hợp hệ thống (IR-QA-001, ER-QA-001, SHP-QA-001, AP-IR-QA0001)", 1)
    };

    foreach (var op in operationalAudits)
    {
        context.AuditLogs.Add(new AuditLog
        {
            UserId = qaAdmin.Id,
            WarehouseId = primaryWarehouse.Id,
            Action = op.Action,
            EntityName = op.EntityName,
            EntityId = op.EntityId,
            Reason = op.Reason,
            NewValues = op.Reason,
            Result = "Success",
            Timestamp = DateTime.UtcNow.AddMinutes(-op.MinutesAgo),
            IpAddress = "10.0.10.25"
        });
    }
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
