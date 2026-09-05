using ERP.SqlIntegrationHarness;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class OwnedDatabaseLifecycleTests
{
    [SqlServerFact]
    [Trait("Category", "HarnessSafety")]
    public async Task ExceptionDoesNotChangeTarget_AndSuccessfulCleanupIsIdempotent()
    {
        var source = Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;
        await using var database = await OwnedTemporaryMigrationDatabase.CreateAsync(source);
        var originalName = database.DatabaseName;
        try { throw new InvalidOperationException("Synthetic test-body failure"); }
        catch (InvalidOperationException)
        {
            database.DatabaseName.Should().Be(originalName).And.NotBe("ERP_KHO");
            await using var context = database.CreateContext();
            await context.Database.OpenConnectionAsync();
            context.Database.GetDbConnection().Database.Should().Be(originalName);
        }

        await database.DisposeAsync();
        await database.DisposeAsync();
        await using var master = new SqlConnection(OwnedDatabasePolicy.MasterConnection(source).ConnectionString);
        await master.OpenAsync();
        await using var command = master.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name=@name";
        command.Parameters.AddWithValue("@name", originalName);
        Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(0);
    }
}
