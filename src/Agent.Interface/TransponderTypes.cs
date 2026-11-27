namespace VeloTime.Agent.Interface;

/// <summary>
/// The values agents send as <c>TransponderType</c>. A transponder type names the chip hardware, not the decoder that
/// read it: MYLAPS X2 and P3 decoders both read ProChip transponders and report the same numeric ID for a chip.
/// Timing rejects any other value.
/// </summary>
public static class TransponderTypes
{
    /// <summary>MYLAPS ProChip. <c>TransponderId</c> is the chip's numeric ID, e.g. <c>106187718</c> (printed as HT-24422).</summary>
    public const string MylapsProChip = "ProChip";
}
