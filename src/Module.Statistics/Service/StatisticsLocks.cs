using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service;

/// <summary>Transaction-scoped PostgreSQL advisory locks shared by every writer of derived Statistics rows.</summary>
public static class StatisticsLocks
{
    /// <summary>Serializes recomputes and manual decisions for one transponder across replicas.</summary>
    public static Task LockTransponderAsync(StatisticsDbContext storage, Guid transponderId, CancellationToken cancellationToken) =>
        AdvisoryLockAsync(storage, $"statistics-transponder:{transponderId:D}", cancellationToken);

    public static async Task AdvisoryLockAsync(StatisticsDbContext storage, string key, CancellationToken cancellationToken)
    {
        if (storage.Database.IsNpgsql())
            await storage.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }
}
