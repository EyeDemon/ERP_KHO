using ERP.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ERP.Api.Health;

public sealed class DatabaseReadinessHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<ErpKhoDbContext>();
            var reachable = await database.Database.CanConnectAsync(cancellationToken);
            return reachable
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Required database is unavailable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Required database readiness check timed out.");
        }
        catch
        {
            return HealthCheckResult.Unhealthy("Required database is unavailable.");
        }
    }
}
