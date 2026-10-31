namespace VeloTime.Module.Timing.Interface.Data;

public sealed record TransponderDTO(Guid Id, string System, string SystemId, string Label, IReadOnlyList<TransponderOwnershipDTO> Ownerships,
    DateTimeOffset? LastSeen = null, bool CanViewStatus = false);

public sealed record TransponderOwnershipDTO(Guid Id, Guid TransponderId, Guid OwnerId, DateTimeOffset OwnedFrom, DateTimeOffset? OwnedTo);

public sealed record ClaimTransponderRequest(string Code);

public enum TransponderOwnershipStatus
{
    Unassigned,
    Active,
    Scheduled,
    Ended
}

public sealed record TransponderColumnFilters(
    string? Label = null,
    string? System = null,
    DateOnly? LastSeen = null,
    string? Owner = null,
    DateOnly? OwnedFrom = null,
    DateOnly? OwnedTo = null,
    TransponderOwnershipStatus? Status = null)
{
    /// <summary>Optional UTC instant range for LastSeen, start-inclusive and end-exclusive.</summary>
    /// <remarks>Both bounds must be UTC, start must precede end, and this cannot be combined with LastSeen.</remarks>
    public UtcInstantRange? LastSeenUtcRange { get; init; }

    /// <summary>Optional UTC instant range for OwnedFrom, start-inclusive and end-exclusive.</summary>
    /// <remarks>Both bounds must be UTC, start must precede end, and this cannot be combined with OwnedFrom.</remarks>
    public UtcInstantRange? OwnedFromUtcRange { get; init; }

    /// <summary>Optional UTC instant range for OwnedTo, start-inclusive and end-exclusive.</summary>
    /// <remarks>Both bounds must be UTC, start must precede end, and this cannot be combined with OwnedTo.</remarks>
    public UtcInstantRange? OwnedToUtcRange { get; init; }
}

/// <summary>A UTC instant interval with inclusive start and exclusive end.</summary>
/// <remarks>Both DateTimeOffset values must have a zero UTC offset and StartInclusive must precede EndExclusive.</remarks>
public sealed record UtcInstantRange(DateTimeOffset StartInclusive, DateTimeOffset EndExclusive);

public sealed record TransponderPageRequest(int Page, int PageSize, string Search, bool IncludeHistory,
    IReadOnlyList<Guid>? MatchingTransponderIds = null,
    IReadOnlyList<Guid>? MatchingOwnerIds = null,
    TransponderColumnFilters? Filters = null,
    string? SortBy = null,
    bool SortDescending = false);

public sealed record TransponderRow(TransponderDTO Transponder, TransponderOwnershipDTO? Ownership);

public sealed record TransponderPage(IReadOnlyList<TransponderRow> Items, int TotalCount);

public sealed record TransponderLookupItem(Guid Id, string Label, string SystemId, IReadOnlyList<Guid> OwnerIds);

public sealed record TransponderLookupPage(IReadOnlyList<TransponderLookupItem> Items, int TotalCount);

/// <summary>A calendar-day registration converted by the browser to UTC instants.</summary>
/// <param name="Code">The MyLaps X2 transponder code.</param>
/// <param name="OwnedFrom">The inclusive start instant.</param>
/// <param name="OwnedTo">The exclusive end instant, or null for no expiry.</param>
/// <param name="OwnerId">The selected administrator-owned registration subject; omitted for self-service.</param>
/// <param name="ConfirmationToken">The ownership snapshot explicitly confirmed before shortening.</param>
public sealed record RegisterTransponderRequest(string Code, DateTimeOffset OwnedFrom, DateTimeOffset? OwnedTo,
    Guid? OwnerId = null, string? ConfirmationToken = null);

/// <summary>The saved outcome or a proposal requiring confirmation before ownership changes.</summary>
/// <param name="RequiresConfirmation">Whether the caller must confirm and resubmit.</param>
/// <param name="ConfirmationToken">A token bound to the proposed dates and current ownership snapshot.</param>
/// <param name="ExistingOwnerships">The affected ownership periods, for display in the confirmation dialog.</param>
public sealed record RegisterTransponderResult(bool RequiresConfirmation, string? ConfirmationToken,
    IReadOnlyList<TransponderOwnershipDTO> ExistingOwnerships);

public sealed record SaveTransponderOwnershipRequest(Guid TransponderId, Guid OwnerId, DateTimeOffset OwnedFrom, DateTimeOffset? OwnedTo);