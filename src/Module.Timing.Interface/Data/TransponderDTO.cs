namespace VeloTime.Module.Timing.Interface.Data;

public sealed record TransponderDTO(Guid Id, string System, string SystemId, string Label, IReadOnlyList<TransponderOwnershipDTO> Ownerships);

public sealed record TransponderOwnershipDTO(Guid Id, Guid TransponderId, Guid OwnerId, DateTimeOffset OwnedFrom, DateTimeOffset? OwnedTo);

public sealed record ClaimTransponderRequest(string Code);

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