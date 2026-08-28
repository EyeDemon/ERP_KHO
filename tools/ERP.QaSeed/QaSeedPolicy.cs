namespace ERP.QaSeed;

using Microsoft.Data.SqlClient;

public static class QaSeedPolicy
{
    public static void EnsureDevelopment(string? environment)
    {
        if (!string.Equals(environment, "Development", StringComparison.Ordinal))
            throw new InvalidOperationException("QA seed is allowed only in Development.");
    }

    public static void ValidatePassword(string username, string password)
    {
        if (password.Length < 12 ||
            !password.Any(char.IsUpper) ||
            !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit) ||
            !password.Any(ch => !char.IsLetterOrDigit(ch)) ||
            password.Contains(username, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Password must contain at least 12 characters, upper/lower case, a digit, a special character, and must not contain the username.");
        }
    }

    public static SqlConnectionStringBuilder ValidateLocalConnection(string connectionString)
    {
        var connection = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(connection.InitialCatalog, "ERP_KHO", StringComparison.Ordinal) ||
            !connection.DataSource.StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase) ||
            !connection.IntegratedSecurity)
        {
            throw new InvalidOperationException("QA seed requires Windows-authenticated SQL Server LocalDB database ERP_KHO.");
        }

        return connection;
    }
}
