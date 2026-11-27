namespace VeloTime.Module.Timing.Model;

public class Passing {
    public Guid Id { get; init; }
    /// <summary>The installation (agent) that observed the passing; part of the observation identity.</summary>
    public Guid InstallationId { get; init; }
    /// <summary>The agent's own passing ID. Unique per installation.</summary>
    public required string SourcePassingId { get; init; }
    public Guid TransponderId { get; init; }
    public Guid TimingPointId { get; init; }
    public required DateTime Time { get; init; }
    public Installation Installation { get; init; } = null!;
    public Transponder Transponder { get; init; } = null!;
    public TimingPoint TimingPoint { get; init; } = null!;
    public bool LowBattery { get; init; } = false;
    public bool LowStrength { get; init; } = false;
    public bool LowHits { get; init; } = false;
}
