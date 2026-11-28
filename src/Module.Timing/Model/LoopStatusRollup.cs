namespace VeloTime.Module.Timing.Model;

/// <summary>
/// Mergeable aggregates of <see cref="LoopStatusSample"/> rows per timing point and
/// <see cref="BucketSize"/> bucket. Updated in the same transaction as each newly inserted sample, so replays and
/// out-of-order delivery give the same values. Averages are <c>Sum / Samples</c> for each value; a value's
/// <c>Samples</c> counts only the samples that reported it. Coarser buckets are aggregated from these rows.
/// </summary>
public class LoopStatusRollup
{
    public static readonly TimeSpan BucketSize = TimeSpan.FromMinutes(5);

    public Guid TimingPointId { get; init; }
    /// <summary>Start of the bucket, UTC, aligned to <see cref="BucketSize"/> from the Unix epoch.</summary>
    public DateTime BucketStart { get; init; }
    public Guid InstallationId { get; init; }

    public int Samples { get; set; }
    public double NoiseSum { get; set; }
    public double NoiseMin { get; set; }
    public double NoiseMax { get; set; }

    public int TemperatureSamples { get; set; }
    public double TemperatureSum { get; set; }
    public double? TemperatureMin { get; set; }
    public double? TemperatureMax { get; set; }

    public int VoltageSamples { get; set; }
    public double VoltageSum { get; set; }
    public double? VoltageMin { get; set; }
    public double? VoltageMax { get; set; }

    public int TriggerSamples { get; set; }
    public long TriggersSum { get; set; }

    /// <summary>Samples that reported a time-sync state.</summary>
    public int SyncSamples { get; set; }
    /// <summary>Samples that reported no time sync.</summary>
    public int UnsynchronisedSamples { get; set; }

    /// <summary>Samples whose agent declared noise and strength share a scale.</summary>
    public int SharedScaleSamples { get; set; }

    public static DateTime BucketOf(DateTime time)
    {
        var ticks = time.Ticks - DateTime.UnixEpoch.Ticks;
        return new DateTime(time.Ticks - ticks % BucketSize.Ticks, DateTimeKind.Utc);
    }
}
