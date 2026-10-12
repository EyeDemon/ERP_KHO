using ERP.Infrastructure.Persistence;
using ERP.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ERP.Api.Tests;

public sealed class SqlFailureDiagnosticsTests
{
    [Fact]
    public void Server_logger_records_types_without_message_state_or_formatter()
    {
        using var db = new ErpKhoDbContext(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer().Options);
        var lines = new List<string>();
        using var diagnostic = new SqlFailureDiagnostics(db, output: lines.Add);
        using var logger = new ServerFailureLogger(diagnostic);
        logger.CreateLogger("ERP.Api.Middleware.GlobalExceptionHandlingMiddleware").Log(LogLevel.Error, 0,
            "private-state", new InvalidOperationException("private-message", new TimeoutException("private-inner")),
            (_, _) => throw new InvalidOperationException("formatter must not run"));
        var output = string.Join('\n', lines);
        Assert.Contains(nameof(InvalidOperationException), output);
        Assert.Contains(nameof(TimeoutException), output);
        Assert.Contains("server-middleware", output);
        Assert.DoesNotContain("private-", output);
    }

    [Fact]
    public async Task Broken_output_preserves_original_exception_and_cleanup_keeps_both_failures()
    {
        using var db = new ErpKhoDbContext(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer().Options);
        using var diagnostic = new SqlFailureDiagnostics(db, output: _ => throw new IOException("sink unavailable"));
        var original = new TimeoutException("original");
        Assert.Same(original, await Assert.ThrowsAsync<TimeoutException>(() => diagnostic.ObserveAsync("request", () => Task.FromException(original))));
        var cleanup = new IOException("cleanup");
        var combined = await Assert.ThrowsAsync<AggregateException>(() => SqlFailureDiagnostics.CleanupAfterFailureAsync(original, () => Task.FromException(cleanup)));
        Assert.Equal(new Exception[] { original, cleanup }, combined.InnerExceptions);
        var safeErrors = System.Text.Json.JsonSerializer.Serialize(SqlFailureDiagnostics.Errors(combined));
        Assert.Contains(typeof(TimeoutException).FullName!, safeErrors);
        Assert.Contains(typeof(IOException).FullName!, safeErrors);
        Assert.DoesNotContain("original", safeErrors);
        Assert.DoesNotContain("cleanup", safeErrors);
        await SqlFailureDiagnostics.CleanupAfterFailureAsync(original, () => Task.CompletedTask);
    }

    [Theory]
    [InlineData("sp_getapplock '__EFMigrationsLock'", "migration-lock-acquire")]
    [InlineData("sp_releaseapplock '__EFMigrationsLock'", "migration-lock-release")]
    [InlineData("SELECT * FROM __EFMigrationsHistory", "migration-history")]
    [InlineData("SELECT private_payload", "application-command")]
    public void Command_classification_does_not_return_sql(string sql, string expected) => Assert.Equal(expected, SqlFailureDiagnostics.CommandStage(sql));
}
