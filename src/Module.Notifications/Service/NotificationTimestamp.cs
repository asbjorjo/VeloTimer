namespace VeloTime.Module.Notifications.Service;

/// <summary>Match PostgreSQL/Npgsql timestamptz precision before returning authoritative state.</summary>
public static class NotificationTimestamp
{
    private static readonly long EpochTicks = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).Ticks;

    public static DateTimeOffset Persisted(DateTimeOffset value)
    {
        var ticks = value.UtcTicks - EpochTicks;
        return new DateTimeOffset(EpochTicks + ticks / 10 * 10, TimeSpan.Zero);
    }
}
