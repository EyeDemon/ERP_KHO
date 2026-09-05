using ERP.Infrastructure.Persistence;
using ERP.SqlIntegrationHarness;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string ConnectionVariable = "ERP_KHO_SQLSERVER_TEST_CONNECTION";
    public const string RunIdVariable = "ERP_KHO_SQLSERVER_TEST_RUN_ID";

}

public sealed class SqlServerIntegrationFixture : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable);
        var runId = Environment.GetEnvironmentVariable(SqlServerFactAttribute.RunIdVariable);
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(runId))
            throw new InvalidOperationException("Run SQL integration tests through the owned harness; missing connection/run ID is a failure, not a skip.");

        var builder = new SqlConnectionStringBuilder(connectionString);
        OwnedDatabasePolicy.EnsureAllowedName(builder.InitialCatalog);
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var verify = connection.CreateCommand();
        verify.CommandText = $"""
            SELECT COUNT(*) FROM dbo.{OwnedDatabasePolicy.MarkerTable}
            WHERE MarkerType=@marker AND RunId=@runId AND DatabaseName=@database AND DB_NAME()=@database
            """;
        verify.Parameters.AddWithValue("@marker", OwnedDatabasePolicy.MarkerType);
        verify.Parameters.AddWithValue("@runId", runId);
        verify.Parameters.AddWithValue("@database", builder.InitialCatalog);
        Convert.ToInt32(await verify.ExecuteScalarAsync()).Should().Be(1, "the target must be owned by this exact test run");

        await using var context = new ErpKhoDbContext(
            new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(builder.ConnectionString).Options);
        await context.Database.GetDbConnection().OpenAsync();
        context.Database.GetDbConnection().Database.Should().Be(builder.InitialCatalog);
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class OwnedDatabasePolicyTests
{
    [Theory]
    [InlineData("ERP_KHO")]
    [InlineData("master")]
    [InlineData("model")]
    [InlineData("msdb")]
    [InlineData("tempdb")]
    [InlineData("Unowned_Test")]
    [InlineData("ERP_KHO_Integration_invalid")]
    public void UnsafeDatabaseNames_AreRejected(string name) =>
        FluentActions.Invoking(() => OwnedDatabasePolicy.EnsureAllowedName(name)).Should().Throw<InvalidOperationException>();

    [Fact]
    public void GeneratedDatabaseName_IsAcceptedAndBuilderChangesOnlyCatalog()
    {
        var runId = OwnedDatabasePolicy.NewRunId();
        var name = OwnedDatabasePolicy.NewDatabaseName(DateTime.UtcNow, runId);
        var source = "Server=localhost\\SQLEXPRESS;Database=ERP_KHO;Integrated Security=True;Encrypt=True";
        var sourceBuilder = new SqlConnectionStringBuilder(source);
        var builder = OwnedDatabasePolicy.DatabaseConnection(source, name);

        OwnedDatabasePolicy.EnsureAllowedName(name);
        builder.InitialCatalog.Should().Be(name);
        builder.DataSource.Should().Be("localhost\\SQLEXPRESS");
        builder.IntegratedSecurity.Should().BeTrue();
        builder.Encrypt.Should().Be(sourceBuilder.Encrypt);
        builder.ConnectionString.ToLowerInvariant().Should().NotContain("password");
    }

    [Fact]
    public void OwnershipGuard_AcceptsOnlyExactMarkerAndRun()
    {
        var runId = OwnedDatabasePolicy.NewRunId();
        var name = OwnedDatabasePolicy.NewDatabaseName(DateTime.UtcNow, runId);

        FluentActions.Invoking(() => OwnedDatabasePolicy.EnsureOwnership(
            name, runId, name, OwnedDatabasePolicy.MarkerType, runId, name, 1)).Should().NotThrow();
        FluentActions.Invoking(() => OwnedDatabasePolicy.EnsureOwnership(
            name, runId, name, OwnedDatabasePolicy.MarkerType, OwnedDatabasePolicy.NewRunId(), name, 1)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => OwnedDatabasePolicy.EnsureOwnership(
            name, runId, name, null, null, null, 0)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void OwnershipGuard_RejectsDatabaseNameMismatch()
    {
        var runId = OwnedDatabasePolicy.NewRunId();
        var name = OwnedDatabasePolicy.NewDatabaseName(DateTime.UtcNow, runId);
        var otherRun = OwnedDatabasePolicy.NewRunId();
        var other = OwnedDatabasePolicy.NewDatabaseName(DateTime.UtcNow.AddSeconds(1), otherRun);

        FluentActions.Invoking(() => OwnedDatabasePolicy.EnsureOwnership(
            name, runId, other, OwnedDatabasePolicy.MarkerType, runId, name, 1)).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MissingOrDuplicateOwnership_CannotAuthorizeCleanup(int count)
    {
        var run = OwnedDatabasePolicy.NewRunId();
        var name = OwnedDatabasePolicy.NewDatabaseName(DateTime.UtcNow, run);
        FluentActions.Invoking(() => OwnedDatabasePolicy.EnsureOwnership(
            name, run, name, OwnedDatabasePolicy.MarkerType, run, name, count)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RemoteOrAttachedDatabaseSources_AreRejectedWithoutLeakingSource()
    {
        var run = OwnedDatabasePolicy.NewRunId();
        var name = OwnedDatabasePolicy.NewDatabaseName(DateTime.UtcNow, run);
        var remote = new SqlConnectionStringBuilder { DataSource = "unapproved.invalid", IntegratedSecurity = true };
        var error = FluentActions.Invoking(() => OwnedDatabasePolicy.DatabaseConnection(remote.ConnectionString, name))
            .Should().Throw<InvalidOperationException>().Which;
        error.Message.Should().NotContain(remote.DataSource).And.NotContain("Data Source=");
        var attached = new SqlConnectionStringBuilder { DataSource = "localhost", IntegratedSecurity = true, AttachDBFilename = "unapproved.mdf" };
        FluentActions.Invoking(() => OwnedDatabasePolicy.DatabaseConnection(attached.ConnectionString, name))
            .Should().Throw<InvalidOperationException>();
    }
}
