using VeloTime.Module.Statistics.Service.Recompute;
using VeloTime.Module.Timing.Client;
using TransponderOwnershipDTO = VeloTime.Module.Timing.Interface.Data.TransponderOwnershipDTO;

namespace VeloTime.Module.Statistics.Service;

public interface ITransponderOwnershipReader
{
    Task<IReadOnlyList<TransponderOwnershipDTO>> ReadAsync(Guid transponderId, CancellationToken cancellationToken);
}

/// <summary>
/// A transponder's ownership periods from Timing, through the shared configuration-history cache (plan §4.3,
/// §5.5). <see cref="Handlers.TransponderOwnershipChangedHandler"/> evicts the entry before it marks the
/// transponder dirty, so a recompute never applies owners from before the change.
/// </summary>
public sealed class TransponderOwnershipReader(ITimingClient timing, StatisticsConfigurationHistory history) : ITransponderOwnershipReader
{
    public async Task<IReadOnlyList<TransponderOwnershipDTO>> ReadAsync(Guid transponderId, CancellationToken cancellationToken) =>
        await history.GetOwnershipAsync(transponderId, async (id, cancel) =>
            (await timing.GetTransponderOwnershipsAsync(id, cancel))
                .Select(owner => new TransponderOwnershipDTO(owner.Id, owner.TransponderId, owner.OwnerId, owner.OwnedFrom, owner.OwnedTo))
                .ToList(), cancellationToken);
}
