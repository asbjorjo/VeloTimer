namespace VeloTime.Module.Timing.Interface.Data;

public sealed record TransponderDTO(Guid Id, string System, string SystemId, string Label, IReadOnlyList<TransponderOwnershipDTO> Ownerships);

public sealed record TransponderOwnershipDTO(Guid Id, Guid TransponderId, Guid OwnerId, DateTimeOffset OwnedFrom, DateTimeOffset? OwnedTo);

public sealed record ClaimTransponderRequest(string Code);

public sealed record SaveTransponderOwnershipRequest(Guid TransponderId, Guid OwnerId, DateTimeOffset OwnedFrom, DateTimeOffset? OwnedTo);