namespace VeloTime.Agent.Model;

/// <summary>
/// A status sample for one loop, sent to Timing as <c>LoopStatusEvent</c>. Only <see cref="Time"/>, <see cref="LoopId"/>
/// and <see cref="Noise"/> are required; fill the other values when the decoder reports them. Use SI units where a
/// value has one; noise is in the decoder's own units.
/// </summary>
public class LoopStatus : IModel
{
    /// <summary>Decoder time of the sample, UTC.</summary>
    public required DateTime Time { get; init; }
    public required string LoopId { get; init; }
    /// <summary>Loop noise in the decoder's own units, as reported.</summary>
    public double Noise { get; init; }
    /// <summary>Loop triggers since the previous sample.</summary>
    public int? LoopTriggers { get; init; }
    /// <summary>Decoder temperature in kelvin.</summary>
    public double? DecoderTemperature { get; init; }
    /// <summary>Decoder input voltage in volt.</summary>
    public double? InputVoltage { get; init; }
    /// <summary>Whether the decoder clock is locked to GPS or another time source.</summary>
    public bool? TimeSynchronised { get; init; }
    public int? SatellitesInUse { get; init; }
    /// <summary>The decoder's own ID or serial, for tracing only.</summary>
    public string? DecoderId { get; init; }
    /// <summary>True when noise and passing strength from this decoder share a scale (enables SNR in Timing).</summary>
    public bool? NoiseSharesStrengthScale { get; init; }
}
