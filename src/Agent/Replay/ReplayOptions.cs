namespace VeloTime.Agent.Replay;

/// <summary>Passing replay settings, bound from the <c>Agent:Replay</c> configuration section.</summary>
public sealed class ReplayOptions
{
    public const string Section = "Agent:Replay";

    /// <summary>
    /// Advertise and run <c>AgentStore</c> replays. On by default; turn off for an agent that does not send passings
    /// through <c>MessagingService</c>, since its store is empty.
    /// </summary>
    public bool AgentStoreEnabled { get; set; } = true;

    /// <summary>Longest range one <c>AgentStore</c> request may cover.</summary>
    public TimeSpan AgentStoreMaxRange { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Passings enqueued per transaction.</summary>
    public int ChunkSize { get; set; } = 200;

    /// <summary>
    /// The next chunk waits until at most this many outbox messages are pending, so a replay never holds more than
    /// about one chunk ahead of live passings in the sequenced outbox. Defaults to <see cref="ChunkSize"/>.
    /// </summary>
    public int? MaxPendingOutbox { get; set; }

    /// <summary>Pause between chunks, and between backlog checks while waiting.</summary>
    public TimeSpan ChunkPause { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Minimum time between <c>Running</c> progress reports.</summary>
    public TimeSpan ProgressInterval { get; set; } = TimeSpan.FromSeconds(5);
}
