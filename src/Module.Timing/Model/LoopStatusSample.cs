namespace VeloTime.Module.Timing.Model;

/// <summary>
/// A raw loop status sample as reported by an agent (<c>LoopStatusEvent</c>). Immutable; one row per timing point and
/// decoder instant. Kept indefinitely so <see cref="LoopStatusRollup"/> can always be rebuilt from it.
/// Noise is in the decoder's own units; temperature in kelvin and voltage in volt.
/// </summary>
public class LoopStatusSample
{
    public Guid Id { get; init; }
    public Guid InstallationId { get; init; }
    public Guid TimingPointId { get; init; }
    /// <summary>Decoder time of the sample, UTC, normalised like passing times.</summary>
    public DateTime Time { get; init; }
    /// <summary>When Timing stored the sample (Timing clock, UTC).</summary>
    public DateTime ReceivedAt { get; init; }
    public double Noise { get; init; }
    public int? LoopTriggers { get; init; }
    public double? DecoderTemperature { get; init; }
    public double? InputVoltage { get; init; }
    public bool? TimeSynchronised { get; init; }
    public int? SatellitesInUse { get; init; }
    public string? DecoderId { get; init; }
    /// <summary>The agent declares noise and passing strength share a scale, so a signal-to-noise estimate is meaningful.</summary>
    public bool? NoiseSharesStrengthScale { get; init; }
}
