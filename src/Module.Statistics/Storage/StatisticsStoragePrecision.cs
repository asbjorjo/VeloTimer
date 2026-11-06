using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace VeloTime.Module.Statistics.Storage;

/// <summary>
/// Canonical persisted precision of statistics measurements on PostgreSQL. <c>timestamp with time zone</c>
/// and <c>interval</c> store whole microseconds; Npgsql truncates .NET 100 ns ticks toward zero relative
/// to the PostgreSQL epoch (2000-01-01 UTC) for timestamps and toward zero for intervals. Entry creation
/// normalizes endpoints to that representation before duration, speed and validity are derived, so a new
/// entry is classified from exactly the values later reads and historical revalidation observe. Stores
/// that keep 100 ns ticks (SQLite tests) are left unchanged; see <see cref="UsesMicrosecondPrecision"/>.
/// </summary>
public static class StatisticsStoragePrecision
{
    private const long TicksPerMicrosecond = TimeSpan.TicksPerMicrosecond;
    private static readonly long PostgresEpochTicks = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    /// <summary>True when the configured store persists timestamps and intervals at microsecond precision.</summary>
    public static bool UsesMicrosecondPrecision(DatabaseFacade database) => database.IsNpgsql();

    /// <summary>Truncates to the persisted microsecond, preserving <see cref="DateTime.Kind"/>.</summary>
    public static DateTime Timestamp(DateTime value)
    {
        var sinceEpoch = value.Ticks - PostgresEpochTicks;
        var truncated = sinceEpoch / TicksPerMicrosecond * TicksPerMicrosecond;
        return new DateTime(PostgresEpochTicks + truncated, value.Kind);
    }

    /// <summary>Truncates toward zero to the persisted microsecond.</summary>
    public static TimeSpan Interval(TimeSpan value) =>
        TimeSpan.FromTicks(value.Ticks / TicksPerMicrosecond * TicksPerMicrosecond);
}
