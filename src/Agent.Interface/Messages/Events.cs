namespace VeloTime.Agent.Interface.Messages.Events;

public record AgentStartedEvent(DateTime Time, string AgentId);

/// <summary>
/// A passing observed by an agent. <paramref name="SourcePassingId"/> is the agent's stable local passing ID and must
/// be a UUID; together with the AgentId message header it identifies the observation, so redelivery and replay are idempotent.
/// <paramref name="TransponderType"/> is one of <see cref="TransponderTypes"/>: the chip type, not the decoder.
/// </summary>
public record PassingObservedV1(Guid SourcePassingId, DateTime Time, string TransponderType, string TransponderId, string LoopId)
{
    public bool LowBattery { get; init; }
    public bool LowStrength { get; init; }
    public bool LowHits { get; init; }

    /// <summary>
    /// Signal strength of the read in the decoder's own units, as reported (no SI unit). Optional: null when the
    /// decoder or agent does not report it. Timing graphs it but applies no threshold; "low" is the decoder's
    /// <see cref="LowStrength"/> flag.
    /// </summary>
    public double? Strength { get; init; }

    /// <summary>
    /// Number of hits (reads) the decoder counted for the passing. Optional: null when not reported. Timing graphs
    /// it but applies no threshold; "low" is the decoder's <see cref="LowHits"/> flag.
    /// </summary>
    public int? Hits { get; init; }
}

/// <summary>
/// A periodic status sample for one timing loop. <paramref name="Time"/> is the decoder time in UTC; together with
/// the loop it identifies the sample, so redelivery and replay are idempotent. <paramref name="Noise"/> is the loop
/// noise in the decoder's own units, as reported (no SI unit). Every other field is optional: each agent sends what
/// its decoder offers, and Timing handles any of them being absent.
/// </summary>
public record LoopStatusEvent(DateTime Time, string LoopId, double Noise)
{
    /// <summary>Loop triggers counted since the previous sample, if the decoder reports them.</summary>
    public int? LoopTriggers { get; init; }

    /// <summary>Decoder temperature in kelvin (SI).</summary>
    public double? DecoderTemperature { get; init; }

    /// <summary>Decoder input voltage in volt.</summary>
    public double? InputVoltage { get; init; }

    /// <summary>Whether the decoder clock is locked to GPS or another time source.</summary>
    public bool? TimeSynchronised { get; init; }

    /// <summary>GPS satellites in use, if the decoder has GPS.</summary>
    public int? SatellitesInUse { get; init; }

    /// <summary>The decoder's own ID or serial number, for tracing only.</summary>
    public string? DecoderId { get; init; }

    /// <summary>
    /// True when <see cref="Noise"/> and <see cref="PassingObservedV1.Strength"/> from this decoder share a scale, so
    /// Timing may show a signal-to-noise estimate. Null or false hides it.
    /// </summary>
    public bool? NoiseSharesStrengthScale { get; init; }
}

public record SystemConfigEvent(string SystemId, string Sport, string Description, bool IsActive);
public record LoopConfigEvent(string LoopId, string Name, int Order, string SystemId);
public record SegmentConfigEvent(string SegmentId, string Name, string StartLoopId, string EndLoopId, double Length, string SystemId);

public record InstallationLayoutEvent(List<SystemConfigEvent> Systems, List<LoopConfigEvent> TimingLoops, List<SegmentConfigEvent> Segments);