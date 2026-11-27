namespace VeloTime.Module.Timing.Model;

/// <summary>The transponder chip hardware. Stored as the transponder discriminator, so values must never change.</summary>
public enum TransponderType
{
    Unknown = 0,
    MylapsProChip = 1
}
