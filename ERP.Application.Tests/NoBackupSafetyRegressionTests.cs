using System.Text.RegularExpressions;
using ERP.SqlIntegrationHarness;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class NoBackupSafetyRegressionTests
{
    [SqlServerFact]
    [Trait("Category", "NoBackupSafety")]
    public async Task ApprovalSuite_HasNoOperationalDependenciesOrBackupHistory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ERP.slnx"))) root = root.Parent;
        root.Should().NotBeNull();
        var repo = root!.FullName;
        var sources = Directory.GetFiles(Path.Combine(repo, "tools", "ERP.SqlIntegrationHarness"), "*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(Path.Combine(repo, "ERP.Application.Tests"), "SqlServer*Tests.cs"))
            .Append(Path.Combine(repo, "ERP.Application.Tests", "OwnedTemporaryMigrationDatabase.cs"))
            .Append(Path.Combine(repo, "ERP.Api.Tests", "SqlServerApprovalIdempotencyTests.cs"));
        const string forbidden = @"\bBACKUP\s+(DATABASE|LOG)\b|\bRESTORE\s+(DATABASE|LOG|VERIFYONLY|HEADERONLY|FILELISTONLY)\b|\bSET\s+RECOVERY\b|(?:Register|Enable|Start)-ScheduledTask|schtasks\s+/(?:run|create|change)|sql-express-backup|System\.Net\.Mail|smtp\.gmail|api\.zalo|\.(?:bak|trn)\b";
        foreach (var path in sources)
            Regex.IsMatch(await File.ReadAllTextAsync(path), forbidden, RegexOptions.IgnoreCase)
                .Should().BeFalse("execution source {0} must not call operational backup, scheduler or messaging paths", Path.GetFileName(path));

        var project = await File.ReadAllTextAsync(Path.Combine(repo, "ERP.Application.Tests", "ERP.Application.Tests.csproj"));
        project.Should().Contain("<Compile Remove=\"Operational/**/*.cs\" />");
        (await File.ReadAllTextAsync(Path.Combine(repo, "ERP.slnx"))).Should().NotContain("ERP.Operational.Tests");

        var source = Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;
        var builder = new SqlConnectionStringBuilder(source);
        OwnedDatabasePolicy.EnsureAllowedName(builder.InitialCatalog);
        await using var connection = new SqlConnection(source);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM msdb.dbo.backupset WHERE database_name=DB_NAME()";
        Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(0);
    }
}
