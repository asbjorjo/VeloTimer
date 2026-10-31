using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using VeloTime.Module.Profiles.Storage;

namespace VeloTime.Module.Profiles.Api.Health;

internal sealed class ProfilesDatabaseHealthCheck(
    IServiceScopeFactory scopeFactory,
    ILogger<ProfilesDatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var profiles = scope.ServiceProvider.GetRequiredService<ProfilesDbContext>();

            // Querying the mapped table ensures the service is not marked ready before its migration runs.
            await profiles.LanguagePreferences.AsNoTracking()
                .Select(_ => 1)
                .Take(1)
                .ToListAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is DbException or TimeoutException)
        {
            logger.LogWarning(exception, "Profiles database readiness check failed.");
            return HealthCheckResult.Unhealthy("Profiles storage is unavailable.");
        }
    }
}
