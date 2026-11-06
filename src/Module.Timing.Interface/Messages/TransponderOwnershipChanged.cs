using VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.Module.Timing.Interface.Messages;

public sealed record TransponderOwnershipChanged(Guid EventId, Guid TransponderId, IReadOnlyList<TransponderOwnershipChange> Changes);

public sealed record TransponderOwnershipChange(TransponderOwnershipDTO? Before, TransponderOwnershipDTO? After);
