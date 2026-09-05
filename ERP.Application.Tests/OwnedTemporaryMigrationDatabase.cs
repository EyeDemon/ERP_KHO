using ERP.Infrastructure.Persistence;
using ERP.SqlIntegrationHarness;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

internal sealed class OwnedTemporaryMigrationDatabase : IAsyncDisposable
{
    private readonly string _masterConnection;
    private readonly string _databaseConnection;
    private readonly string _databaseName;
    private readonly string _runId;
    private bool _disposed;
    internal string DatabaseName => _databaseName;
    internal string RunId => _runId;

    private OwnedTemporaryMigrationDatabase(
        string masterConnection,
        string databaseConnection,
        string databaseName,
        string runId)
    {
        _masterConnection = masterConnection;
        _databaseConnection = databaseConnection;
        _databaseName = databaseName;
        _runId = runId;
    }

    public static async Task<OwnedTemporaryMigrationDatabase> CreateAsync(string baseConnection)
    {
        var runId = OwnedDatabasePolicy.NewRunId();
        var databaseName = OwnedDatabasePolicy.NewDatabaseName(DateTime.UtcNow, runId);
        var master = OwnedDatabasePolicy.MasterConnection(baseConnection);
        var database = OwnedDatabasePolicy.DatabaseConnection(baseConnection, databaseName);
        Console.WriteLine($"Owned migration target: {databaseName}; RunId={runId}.");

        await using (var connection = new SqlConnection(master.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE {OwnedDatabasePolicy.QuoteIdentifier(databaseName)}";
            await command.ExecuteNonQueryAsync();
        }

        var owned = new OwnedTemporaryMigrationDatabase(master.ConnectionString, database.ConnectionString, databaseName, runId);
        try
        {
            await owned.CreateMarkerAsync();
            return owned;
        }
        catch
        {
            await owned.DisposeAsync();
            throw;
        }
    }

    public ErpKhoDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(_databaseConnection).Options);

    private async Task CreateMarkerAsync()
    {
        await using var connection = new SqlConnection(_databaseConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_NAME() <> @database THROW 51020, 'SQL integration DB_NAME mismatch.', 1;
            CREATE TABLE dbo.{OwnedDatabasePolicy.MarkerTable}
            (
                MarkerType nvarchar(64) NOT NULL,
                RunId char(32) NOT NULL,
                CreatedAtUtc datetime2 NOT NULL,
                HarnessVersion nvarchar(32) NOT NULL,
                HeadReference char(40) NULL,
                DatabaseName sysname NOT NULL,
                CONSTRAINT PK_{OwnedDatabasePolicy.MarkerTable} PRIMARY KEY (MarkerType, RunId)
            );
            INSERT dbo.{OwnedDatabasePolicy.MarkerTable}
                (MarkerType, RunId, CreatedAtUtc, HarnessVersion, HeadReference, DatabaseName)
            VALUES (@marker, @runId, SYSUTCDATETIME(), N'1.0', NULL, @database);
            """;
        command.Parameters.AddWithValue("@marker", OwnedDatabasePolicy.MarkerType);
        command.Parameters.AddWithValue("@runId", _runId);
        command.Parameters.AddWithValue("@database", _databaseName);
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        OwnedDatabasePolicy.EnsureAllowedName(_databaseName);
        await using (var database = new SqlConnection(_databaseConnection))
        {
            await database.OpenAsync();
            await using var verify = database.CreateCommand();
            verify.CommandText = $"""
                SELECT COUNT(*) FROM dbo.{OwnedDatabasePolicy.MarkerTable}
                WHERE MarkerType=@marker AND RunId=@runId AND DatabaseName=@database AND DB_NAME()=@database
                """;
            verify.Parameters.AddWithValue("@marker", OwnedDatabasePolicy.MarkerType);
            verify.Parameters.AddWithValue("@runId", _runId);
            verify.Parameters.AddWithValue("@database", _databaseName);
            var markerCount = Convert.ToInt32(await verify.ExecuteScalarAsync());
            OwnedDatabasePolicy.EnsureOwnership(_databaseName, _runId, database.Database, OwnedDatabasePolicy.MarkerType, _runId, _databaseName, markerCount);
        }

        await using var master = new SqlConnection(_masterConnection);
        await master.OpenAsync();
        await using var command = master.CreateCommand();
        var quoted = OwnedDatabasePolicy.QuoteIdentifier(_databaseName);
        command.CommandText = $"ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {quoted};";
        await command.ExecuteNonQueryAsync();
        await using var absent = master.CreateCommand();
        absent.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name=@database";
        absent.Parameters.AddWithValue("@database", _databaseName);
        if (Convert.ToInt32(await absent.ExecuteScalarAsync()) != 0)
            throw new InvalidOperationException("Temporary migration database cleanup verification failed.");
        _disposed = true;
        Console.WriteLine($"Owned migration target cleaned: {_databaseName}; RunId={_runId}.");
    }
}
