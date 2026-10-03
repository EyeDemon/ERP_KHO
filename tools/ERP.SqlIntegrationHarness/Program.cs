using System.Diagnostics;
using ERP.SqlIntegrationHarness;
using Microsoft.Data.SqlClient;

const string sourceVariable = "ERP_KHO_SQLSERVER_ADMIN_CONNECTION";
var source = Environment.GetEnvironmentVariable(sourceVariable);
if (string.IsNullOrWhiteSpace(source))
{
    Console.Error.WriteLine($"BLOCKED: {sourceVariable} is not configured.");
    return 2;
}

var runId = OwnedDatabasePolicy.NewRunId();
var databaseName = OwnedDatabasePolicy.NewDatabaseName(DateTime.UtcNow, runId);
var masterBuilder = OwnedDatabasePolicy.MasterConnection(source);
var databaseBuilder = OwnedDatabasePolicy.DatabaseConnection(source, databaseName);
var created = false;
Console.WriteLine($"SQL integration planned target: {databaseName}; RunId={runId}; server/authentication details redacted.");

try
{
    await using (var master = new SqlConnection(masterBuilder.ConnectionString))
    {
        await master.OpenAsync();
        await using var create = master.CreateCommand();
        create.CommandText = $"CREATE DATABASE {OwnedDatabasePolicy.QuoteIdentifier(databaseName)}";
        await create.ExecuteNonQueryAsync();
        created = true;
    }

    await using (var database = new SqlConnection(databaseBuilder.ConnectionString))
    {
        await database.OpenAsync();
        await using var command = database.CreateCommand();
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
            VALUES (@marker, @runId, SYSUTCDATETIME(), @version, @head, @database);
            """;
        command.Parameters.AddWithValue("@marker", OwnedDatabasePolicy.MarkerType);
        command.Parameters.AddWithValue("@runId", runId);
        command.Parameters.AddWithValue("@version", "1.0");
        command.Parameters.AddWithValue("@head", await HeadAsync());
        command.Parameters.AddWithValue("@database", databaseName);
        await command.ExecuteNonQueryAsync();
    }

    Console.WriteLine($"SQL integration target created: {databaseName} (Integrated Security; server redacted).");
    var result = await RunTestsAsync(databaseBuilder.ConnectionString, runId);
    return result;
}
finally
{
    if (created)
        await CleanupAsync(masterBuilder.ConnectionString, databaseBuilder.ConnectionString, databaseName, runId);
    source = null;
}

static async Task<int> RunTestsAsync(string connectionString, string runId)
{
    var startInfo = new ProcessStartInfo("dotnet")
    {
        UseShellExecute = false,
        WorkingDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."))
    };
    startInfo.ArgumentList.Add("test");
    startInfo.ArgumentList.Add("ERP.Application.Tests/ERP.Application.Tests.csproj");
    startInfo.ArgumentList.Add("--configuration");
    startInfo.ArgumentList.Add("Release");
    startInfo.ArgumentList.Add("--no-build");
    startInfo.ArgumentList.Add("--logger");
    startInfo.ArgumentList.Add("trx;LogFileName=application.trx");
    startInfo.ArgumentList.Add("--results-directory");
    startInfo.ArgumentList.Add(Path.Combine("TestResults", "SqlIntegration", runId));
    startInfo.ArgumentList.Add("--verbosity");
    startInfo.ArgumentList.Add("minimal");
    startInfo.Environment["ERP_KHO_SQLSERVER_TEST_CONNECTION"] = connectionString;
    startInfo.Environment["ERP_KHO_SQLSERVER_TEST_RUN_ID"] = runId;
    startInfo.Environment.Remove("ERP_KHO_SQLSERVER_ADMIN_CONNECTION");
    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start SQL integration tests.");
    await process.WaitForExitAsync();
    return process.ExitCode;
}

static async Task CleanupAsync(string masterConnection, string databaseConnection, string databaseName, string runId)
{
    OwnedDatabasePolicy.EnsureAllowedName(databaseName);
    await using (var database = new SqlConnection(databaseConnection))
    {
        await database.OpenAsync();
        await using var verify = database.CreateCommand();
        verify.CommandText = $"""
            SELECT COUNT(*) FROM dbo.{OwnedDatabasePolicy.MarkerTable}
            WHERE MarkerType=@marker AND RunId=@runId AND DatabaseName=@database AND DB_NAME()=@database
            """;
        verify.Parameters.AddWithValue("@marker", OwnedDatabasePolicy.MarkerType);
        verify.Parameters.AddWithValue("@runId", runId);
        verify.Parameters.AddWithValue("@database", databaseName);
        var markerCount = Convert.ToInt32(await verify.ExecuteScalarAsync());
        OwnedDatabasePolicy.EnsureOwnership(databaseName, runId, database.Database, OwnedDatabasePolicy.MarkerType, runId, databaseName, markerCount);
    }

    await using var master = new SqlConnection(masterConnection);
    await master.OpenAsync();
    await using var cleanup = master.CreateCommand();
    var quoted = OwnedDatabasePolicy.QuoteIdentifier(databaseName);
    cleanup.CommandText = $"ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {quoted};";
    await cleanup.ExecuteNonQueryAsync();
    await using var absent = master.CreateCommand();
    absent.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name=@database";
    absent.Parameters.AddWithValue("@database", databaseName);
    if (Convert.ToInt32(await absent.ExecuteScalarAsync()) != 0)
        throw new InvalidOperationException($"Cleanup verification failed for {databaseName}.");
    Console.WriteLine($"SQL integration target cleaned: {databaseName}.");
}

static async Task<object> HeadAsync()
{
    var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true };
    info.ArgumentList.Add("rev-parse");
    info.ArgumentList.Add("HEAD");
    using var process = Process.Start(info);
    if (process is null) return DBNull.Value;
    var value = (await process.StandardOutput.ReadToEndAsync()).Trim();
    await process.WaitForExitAsync();
    return process.ExitCode == 0 && value.Length == 40 ? value : DBNull.Value;
}
