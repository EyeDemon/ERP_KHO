using System.Data;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.QaSeed;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

QaSeedPolicy.EnsureDevelopment(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));
var applySeed = args.Contains("--apply", StringComparer.Ordinal);
var revokeQaSessions = args.Contains("--revoke-qa-sessions", StringComparer.Ordinal);
if (applySeed == revokeQaSessions)
    throw new InvalidOperationException("Specify exactly one operation: --apply or --revoke-qa-sessions.");

var connectionString = Environment.GetEnvironmentVariable("ERP_KHO_QA_SEED_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("QA seed connection is unavailable.");

QaSeedPolicy.ValidateLocalConnection(connectionString);

var options = new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(connectionString).Options;
await using var context = new ErpKhoDbContext(options);

if (revokeQaSessions)
{
    await RevokeQaSessionsAsync(context);
    return;
}

await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

var roleNames = new[] { "Admin", "Manager", "WarehouseStaff", "Viewer" };
var roles = new Dictionary<string, Role>(StringComparer.Ordinal);
foreach (var roleName in roleNames)
{
    var matches = await context.Roles.Where(role => role.RoleName == roleName).ToListAsync();
    if (matches.Count > 1) throw new InvalidOperationException($"Duplicate role detected: {roleName}.");
    var role = matches.SingleOrDefault() ?? new Role { RoleName = roleName, Description = $"Local QA role: {roleName}" };
    if (role.Id == 0) context.Roles.Add(role);
    roles.Add(roleName, role);
}

var wh01 = await context.Warehouses.SingleOrDefaultAsync(warehouse => warehouse.Code == "WH01")
    ?? throw new InvalidOperationException("Required local warehouse WH01 does not exist.");
var wh02Matches = await context.Warehouses.Where(warehouse => warehouse.Code == "WH02").ToListAsync();
if (wh02Matches.Count > 1) throw new InvalidOperationException("Duplicate WH02 warehouses detected.");
var wh02 = wh02Matches.SingleOrDefault() ?? new Warehouse
{
    Code = "WH02", Name = "Kho QA ngoài phạm vi", Address = "Local QA only", IsActive = true
};
if (wh02.Id == 0) context.Warehouses.Add(wh02);
await context.SaveChangesAsync();

var definitions = new[]
{
    new AccountDefinition("qa_admin_local", "QA Local Admin", roles["Admin"], false),
    new AccountDefinition("qa_manager_wh01", "QA Manager WH01", roles["Manager"], true),
    new AccountDefinition("qa_staff_wh01", "QA Warehouse Staff WH01", roles["WarehouseStaff"], true),
    new AccountDefinition("qa_viewer_wh01", "QA Viewer WH01", roles["Viewer"], true)
};
var hasher = new PasswordHasherService();
var users = new Dictionary<string, User>(StringComparer.Ordinal);

foreach (var definition in definitions)
{
    var matches = await context.Users.Where(user => user.Username == definition.Username).ToListAsync();
    if (matches.Count > 1) throw new InvalidOperationException($"Duplicate QA user detected: {definition.Username}.");
    var user = matches.SingleOrDefault();
    if (user is null)
    {
        var password = ReadValidPassword(definition.Username);
        user = new User
        {
            Username = definition.Username,
            FullName = definition.FullName,
            Role = definition.Role,
            PasswordHash = hasher.HashPassword(password),
            IsActive = true
        };
        password = string.Empty;
        context.Users.Add(user);
    }
    else if (user.RoleId != definition.Role.Id || !user.IsActive)
    {
        throw new InvalidOperationException($"Existing QA user has unexpected role or active state: {definition.Username}.");
    }
    users.Add(definition.Username, user);
}
await context.SaveChangesAsync();

var admin = users["qa_admin_local"];
foreach (var definition in definitions.Where(item => item.AssignWh01))
{
    var user = users[definition.Username];
    if (!await context.UserWarehouses.AnyAsync(access => access.UserId == user.Id && access.WarehouseId == wh01.Id))
        context.UserWarehouses.Add(new UserWarehouse { UserId = user.Id, WarehouseId = wh01.Id, CreatedBy = admin.Id });
    if (await context.UserWarehouses.AnyAsync(access => access.UserId == user.Id && access.WarehouseId == wh02.Id))
        throw new InvalidOperationException($"QA user unexpectedly has WH02 access: {definition.Username}.");
}

context.AuditLogs.Add(new AuditLog
{
    UserId = admin.Id,
    Action = "QA.IamSeedApplied",
    EntityName = "LocalQaIdentitySet",
    NewValues = "Roles=Admin,Manager,WarehouseStaff,Viewer;Users=4;ScopedWarehouse=WH01;IsolationWarehouse=WH02",
    Timestamp = DateTime.UtcNow,
    IpAddress = "local-cli"
});
await context.SaveChangesAsync();
await transaction.CommitAsync();

Console.WriteLine("Local ERP_KHO QA IAM seed completed without inventory or ledger mutation.");

static string ReadPasswordTwice(string username)
{
    Console.Write($"Enter password for {username}: ");
    var first = ReadHidden();
    Console.WriteLine();
    Console.Write($"Confirm password for {username}: ");
    var second = ReadHidden();
    Console.WriteLine();
    if (!string.Equals(first, second, StringComparison.Ordinal))
        throw new InvalidOperationException($"Password confirmation failed for {username}.");
    return first;
}

static string ReadValidPassword(string username)
{
    while (true)
    {
        try
        {
            var password = ReadPasswordTwice(username);
            QaSeedPolicy.ValidatePassword(username, password);
            return password;
        }
        catch (ArgumentException exception)
        {
            Console.WriteLine(exception.Message);
            Console.WriteLine("Please try again.");
        }
        catch (InvalidOperationException exception)
        {
            Console.WriteLine(exception.Message);
            Console.WriteLine("Please try again.");
        }
    }
}

static string ReadHidden()
{
    var chars = new List<char>();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (chars.Count > 0)
            {
                chars.RemoveAt(chars.Count - 1);
                Console.Write("\b \b");
            }
            continue;
        }
        if (!char.IsControl(key.KeyChar))
        {
            chars.Add(key.KeyChar);
            Console.Write('*');
        }
    }
    return new string(chars.ToArray());
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
        await service.RevokeUserSessionsAsAdminAsync(users[username].Id);
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
