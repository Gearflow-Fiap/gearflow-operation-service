using GearFlow.Operation.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GearFlow.Operation.Api;

public sealed class OperationReadiness(IDbContextFactory<OperationDbContext> factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            if (!await db.Database.CanConnectAsync(ct)) return HealthCheckResult.Unhealthy("database_unavailable");
            if ((await db.Database.GetPendingMigrationsAsync(ct)).Any()) return HealthCheckResult.Unhealthy("migrations_pending");
            return HealthCheckResult.Healthy();
        }
        catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
        { return HealthCheckResult.Unhealthy("database_unavailable"); }
    }
    public static void Map(WebApplication app)
    {
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = _ => true,
            AllowCachingResponses = false,
            ResultStatusCodes = new Dictionary<HealthStatus, int> { [HealthStatus.Healthy] = 200, [HealthStatus.Degraded] = 503, [HealthStatus.Unhealthy] = 503 },
            ResponseWriter = (http, report) => http.Response.WriteAsJsonAsync(new { status = report.Status == HealthStatus.Healthy ? "ready" : "not_ready" })
        }).AllowAnonymous();
    }
}
