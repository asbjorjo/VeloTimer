namespace VeloTime.Module.Timing;

/// <summary>Timing module settings, bound from the <c>Timing</c> configuration section.</summary>
public class TimingOptions
{
    public const string SectionName = "Timing";

    /// <summary>
    /// The smallest step by which <c>Installation.LastSeen</c> advances. Passings and loop status arrive every few
    /// seconds; writing every one would rewrite the installation row and churn its cache entry. Default 30 s.
    /// </summary>
    public TimeSpan LastSeenResolution { get; set; } = TimeSpan.FromSeconds(30);
}
