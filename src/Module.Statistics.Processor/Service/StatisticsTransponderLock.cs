using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace VeloTime.Module.Statistics.Service;

/// <summary>Serializes local completion receipt creation with receipt processing.</summary>
internal static class StatisticsTransponderLock
{
    public static Task AcquireAsync(DatabaseFacade database, Guid transponderId, CancellationToken cancellationToken)
    {
        if (!database.IsNpgsql()) return Task.CompletedTask;
        var key = $"statistics-transponder:{transponderId:D}";
        return database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }
}
