using VeloTime.Module.Timing.Model;

namespace VeloTime.Module.Timing.Service;

/// <summary>A passing as reported by an agent, before Timing resolves its identities.</summary>
public sealed record PassingObservation(
    string AgentId,
    Guid SourcePassingId,
    string TransponderId,
    string TransponderType,
    string LoopId,
    DateTime Time,
    bool LowBattery = false,
    bool LowStrength = false,
    bool LowHits = false)
{
    /// <summary>The replay this observation came from (the <c>ReplayRequestId</c> header); null for live passings.</summary>
    public Guid? ReplayRequestId { get; init; }
}

public enum PassingRegistrationOutcome
{
    /// <summary>A new passing fact was stored and <c>PassingRecordedV1</c> enqueued in the same transaction.</summary>
    Recorded,
    /// <summary>The same observation was already recorded; nothing changed and nothing was published.</summary>
    Duplicate,
    /// <summary>The observation contradicts a recorded passing; it was stored as a <see cref="PassingConflict"/> for review.</summary>
    Conflict,
    /// <summary>The transponder could not be resolved; the raw observation was stored as a <see cref="RejectedPassing"/>.</summary>
    Rejected,
}

/// <param name="Passing">The recorded passing: the new one, or the existing one for duplicates and conflicts. Null when rejected.</param>
public sealed record PassingRegistration(PassingRegistrationOutcome Outcome, Passing? Passing, PassingConflict? Conflict = null,
    RejectedPassing? Rejection = null);

public interface IAgentService
{
    Task<PassingRegistration> RegisterPassingAsync(PassingObservation observation, CancellationToken cancellationToken = default);
}
