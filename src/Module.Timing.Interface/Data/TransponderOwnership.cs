namespace VeloTime.Module.Timing.Interface.Data;

public record TransponderOwnership(Guid OwnerId, DateTime OwnedFrom, DateTime? OwnedTo);
