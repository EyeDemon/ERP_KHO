using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ERP.TestSupport;

// Test-only observation. Never format EF event data, commands, parameters or exception messages.
public sealed class SqlFailureDiagnostics : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private readonly string database;
    private readonly Guid contextId;
    private readonly DbConnection targetConnection;
    private readonly bool includeOtherContexts;
    private readonly Action<string> output;
    private readonly List<IDisposable> subscriptions = [];
    private readonly IDisposable allListeners;
    private bool disposed;
    public string Correlation { get; } = Guid.NewGuid().ToString("N");

    public SqlFailureDiagnostics(DbContext context, bool includeOtherContexts = false, Action<string>? output = null)
    {
        targetConnection = context.Database.GetDbConnection();
        database = targetConnection.Database;
        contextId = context.ContextId.InstanceId;
        this.includeOtherContexts = includeOtherContexts;
        this.output = output ?? Console.WriteLine;
        allListeners = DiagnosticListener.AllListeners.Subscribe(this);
        Write(new { stage = "versions", provider = context.Database.ProviderName,
            ef = Version(typeof(DbContext)), providerVersion = Version(typeof(SqlServerDbContextOptionsExtensions)),
            sqlClient = Version(typeof(SqlConnection)) });
    }

    private static string? Version(Type type) => type.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    public void Write(object values)
    {
        // Observation failure must never replace the request/provider exception being diagnosed.
        try { output(JsonSerializer.Serialize(new { diagnostic = Correlation, utc = DateTimeOffset.UtcNow, values })); }
        catch { /* Best-effort test diagnostics only. */ }
    }

    public void Failure(string stage, Exception exception) => Write(new { stage, errors = Errors(exception) });

    public static object[] Errors(Exception exception)
    {
        var errors = new List<object>();
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            errors.Add(new { type = current.GetType().FullName,
                sqlNumbers = current is SqlException sql ? sql.Errors.Cast<SqlError>().Select(x => x.Number).ToArray() : [] });
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions) errors.AddRange(Errors(inner));
                break;
            }
        }
        return errors.ToArray();
    }

    public async Task ObserveAsync(string stage, Func<Task> action)
    {
        Write(new { stage, phase = "start" });
        try { await action(); Write(new { stage, phase = "completed" }); }
        catch (Exception exception) { Failure(stage, exception); throw; }
    }

    public static async Task CleanupAfterFailureAsync(Exception original, Func<Task> cleanup)
    {
        try { await cleanup(); }
        catch (Exception cleanupFailure)
        {
            // This is fixture cleanup, not a replacement for EF's own lock disposal.
            throw new AggregateException("Owned QA startup and cleanup both failed.", original, cleanupFailure);
        }
    }

    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name != DbLoggerCategory.Name) return;
        lock (subscriptions)
            if (!disposed) subscriptions.Add(listener.Subscribe(this));
    }

    public void OnNext(KeyValuePair<string, object?> entry)
    {
        try
        {
            var context = (entry.Value as DbContextEventData)?.Context;
            var connection = entry.Value switch { CommandEventData c => c.Command.Connection, ConnectionEventData c => c.Connection, _ => null };
            if (context?.ContextId.InstanceId != contextId && !ReferenceEquals(connection, targetConnection)
                && !(includeOtherContexts && connection?.Database == database)) return;
            if (entry.Value is not CommandEventData && entry.Value is not ConnectionEventData) return;
            var command = entry.Value as CommandEventData;
            var stage = command == null ? "connection" : CommandStage(command.Command.CommandText);
            if (stage == "application-command" && command?.CommandSource == CommandSource.Migrations) stage = "migration-command";
            // Successful application commands are irrelevant to lock lifetime; failures remain observable.
            if (command != null && stage == "application-command" && entry.Value is not CommandErrorEventData) return;
            var sql = connection as SqlConnection;
            var open = sql?.State == ConnectionState.Open;
            var exception = entry.Value switch { CommandErrorEventData c => c.Exception, ConnectionErrorEventData c => c.Exception, _ => null };
            Write(new { stage, eventName = entry.Key, context = context?.ContextId.InstanceId,
                connection = entry.Value switch { CommandEventData c => c.ConnectionId, ConnectionEventData c => c.ConnectionId, _ => Guid.Empty },
                clientConnection = open ? sql!.ClientConnectionId : (Guid?)null, session = open ? sql!.ServerProcessId : (int?)null,
                serverVersion = open ? sql!.ServerVersion : null,
                command = command?.CommandId,
                commandSource = command?.CommandSource.ToString(),
                durationMs = entry.Value switch { CommandEndEventData c => c.Duration.TotalMilliseconds, ConnectionEndEventData c => c.Duration.TotalMilliseconds, _ => (double?)null },
                errors = exception == null ? [] : Errors(exception) });
        }
        catch { /* A disposed connection or diagnostic sink cannot mask a provider failure. */ }
    }

    public static string CommandStage(string text) => text.Contains("__EFMigrationsLock", StringComparison.Ordinal)
        ? text.Contains("sp_releaseapplock", StringComparison.OrdinalIgnoreCase) ? "migration-lock-release" : "migration-lock-acquire"
        : text.Contains("__EFMigrationsHistory", StringComparison.Ordinal) ? "migration-history" : "application-command";

    public void OnError(Exception error) => Failure("diagnostic-source", error);
    public void OnCompleted() { }
    public void Dispose()
    {
        allListeners.Dispose();
        lock (subscriptions)
        {
            disposed = true;
            foreach (var subscription in subscriptions) subscription.Dispose();
        }
    }
}
