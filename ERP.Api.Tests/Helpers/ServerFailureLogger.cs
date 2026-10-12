using ERP.TestSupport;
using Microsoft.Extensions.Logging;

namespace ERP.Api.Tests;

internal sealed class ServerFailureLogger(SqlFailureDiagnostics diagnostic) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ExceptionLogger(diagnostic, categoryName);
    public void Dispose() { }

    private sealed class ExceptionLogger(SqlFailureDiagnostics diagnostic, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception != null && category == "ERP.Api.Middleware.GlobalExceptionHandlingMiddleware")
                diagnostic.Failure("server-middleware", exception);
            // Do not invoke formatter: state/message can contain payloads, SQL or credentials.
        }
    }
}
