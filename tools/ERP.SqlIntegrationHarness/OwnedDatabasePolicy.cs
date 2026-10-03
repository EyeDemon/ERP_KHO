using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace ERP.SqlIntegrationHarness;

public static partial class OwnedDatabasePolicy
{
    public const string Prefix = "ERP_KHO_Integration_";
    public const string MarkerType = "ERP_KHO_SQL_INTEGRATION_TEST";
    public const string MarkerTable = "__SqlIntegrationOwnership";
    private static readonly HashSet<string> Denylist = new(StringComparer.OrdinalIgnoreCase)
        { "ERP_KHO", "master", "model", "msdb", "tempdb" };

    public static string NewRunId() => Guid.NewGuid().ToString("N");

    public static string NewDatabaseName(DateTime utcNow, string runId)
    {
        if (!RunIdPattern().IsMatch(runId)) throw new InvalidOperationException("Invalid SQL integration Run ID.");
        return $"{Prefix}{utcNow:yyyyMMdd_HHmmss}_{runId[..8]}";
    }

    public static void EnsureAllowedName(string databaseName)
    {
        if (Denylist.Contains(databaseName) || !DatabaseNamePattern().IsMatch(databaseName))
            throw new InvalidOperationException("Refusing SQL integration operation for a non-owned database name.");
    }

    public static void EnsureOwnership(
        string expectedDatabase,
        string expectedRunId,
        string? actualDatabase,
        string? markerType,
        string? markerRunId,
        string? markerDatabase,
        int markerCount)
    {
        EnsureAllowedName(expectedDatabase);
        if (!RunIdPattern().IsMatch(expectedRunId) || markerCount != 1 ||
            !string.Equals(actualDatabase, expectedDatabase, StringComparison.Ordinal) ||
            !string.Equals(markerType, MarkerType, StringComparison.Ordinal) ||
            !string.Equals(markerRunId, expectedRunId, StringComparison.Ordinal) ||
            !string.Equals(markerDatabase, expectedDatabase, StringComparison.Ordinal))
            throw new InvalidOperationException("SQL integration ownership marker mismatch; destructive operation refused.");
    }

    public static SqlConnectionStringBuilder MasterConnection(string source)
    {
        var builder = new SqlConnectionStringBuilder(source);
        ValidateSource(builder);
        builder.InitialCatalog = "master";
        return builder;
    }

    public static SqlConnectionStringBuilder DatabaseConnection(string source, string databaseName)
    {
        EnsureAllowedName(databaseName);
        var builder = new SqlConnectionStringBuilder(source) { InitialCatalog = databaseName };
        ValidateSource(builder);
        return builder;
    }

    private static void ValidateSource(SqlConnectionStringBuilder builder)
    {
        if (!builder.IntegratedSecurity || !string.IsNullOrEmpty(builder.UserID) || !string.IsNullOrEmpty(builder.Password))
            throw new InvalidOperationException("SQL integration harness requires credential-free Integrated Security.");
        if (!string.IsNullOrEmpty(builder.AttachDBFilename) || builder.UserInstance)
            throw new InvalidOperationException("Database attachment and user-instance overrides are not allowed.");
        var server = builder.DataSource;
        if (server.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) server = server[4..];
        var host = server.Split('\\', ',')[0];
        if (!new[] { ".", "localhost", "127.0.0.1", "(local)", "(localdb)", Environment.MachineName }
            .Contains(host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("This harness is restricted to the approved local SQL Server instance.");
    }

    public static string QuoteIdentifier(string value)
    {
        EnsureAllowedName(value);
        return "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    [GeneratedRegex("^[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex RunIdPattern();

    [GeneratedRegex("^ERP_KHO_Integration_[0-9]{8}_[0-9]{6}_[a-f0-9]{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex DatabaseNamePattern();
}
