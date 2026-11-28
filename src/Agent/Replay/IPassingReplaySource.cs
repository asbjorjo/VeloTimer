using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Agent.Model;

namespace VeloTime.Agent.Replay;

/// <summary>
/// A half-open range [<paramref name="FromUtc"/>, <paramref name="ToUtc"/>) of passing time to read again, optionally
/// limited to <paramref name="LoopIds"/> (only passed when the source advertised <c>SupportsLoopFilter</c>).
/// </summary>
public sealed record PassingReplayRange(DateTime FromUtc, DateTime ToUtc, IReadOnlyList<string>? LoopIds);

/// <summary>
/// Implemented by hardware agents that can read passing history from the decoder or timing server behind them, so an
/// admin can run a <c>Source</c> replay (see <c>docs/passing-replay-plan.md</c>). Optional: register at most one
/// implementation as a singleton. The shared library wires it into capability reports and replays in Phase 3 of that
/// plan; until then the contract is fixed so hardware agents can start implementing it.
/// </summary>
/// <remarks>
/// A passing read here must get the same <see cref="Passing.Id"/> it got when it was read live: derive it with
/// <see cref="Identity.PassingId"/> from fields the source returns identically on live and replay reads. Otherwise every
/// replayed passing becomes a <c>DuplicateObservation</c> conflict in Timing.
/// </remarks>
public interface IPassingReplaySource
{
    /// <summary>
    /// The current limits of the source, with <see cref="PassingReplayCapability.Mode"/> set to
    /// <c>PassingReplayModes.Source</c>, or <c>null</c> while the source cannot replay (for example the decoder is
    /// disconnected). Called for every capability report, so keep it cheap.
    /// </summary>
    PassingReplayCapability? Describe();

    /// <summary>Reads every passing in <paramref name="range"/> from the source, in any order.</summary>
    IAsyncEnumerable<Passing> ReadAsync(PassingReplayRange range, CancellationToken cancellationToken);

    /// <summary>Raised when <see cref="Describe"/> would return something different, so a new report is sent.</summary>
    event EventHandler? CapabilityChanged;
}
