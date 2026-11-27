namespace VeloTime.Module.Timing.Interface.Data;

/// <summary>An agent observation Timing could not resolve, as received. Times are UTC.</summary>
public sealed record RejectedPassingDTO(Guid Id, string AgentId, Guid SourcePassingId, DateTimeOffset Time, string TransponderType,
    string TransponderId, string LoopId, bool LowBattery, bool LowStrength, bool LowHits, string Reason,
    DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt, int Occurrences);

public sealed record RejectedPassingPage(IReadOnlyList<RejectedPassingDTO> Items, int TotalCount);

/// <summary>Selects rejected passings to reprocess. Null fields match everything.</summary>
public sealed record ReprocessRejectedPassingsRequest(string? AgentId = null, string? Reason = null);

/// <summary>Outcome counts of a reprocess run. Rows that are no longer rejected are deleted.</summary>
public sealed record ReprocessRejectedPassingsResult(int Recorded, int Duplicate, int Conflict, int StillRejected);
