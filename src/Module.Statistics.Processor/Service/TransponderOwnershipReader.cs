using VeloTime.Module.Timing.Client;
using TransponderOwnershipDTO = VeloTime.Module.Timing.Interface.Data.TransponderOwnershipDTO;

namespace VeloTime.Module.Statistics.Service;

public interface ITransponderOwnershipReader
{
    Task<IReadOnlyList<TransponderOwnershipDTO>> ReadAsync(Guid transponderId, CancellationToken cancellationToken);
}

public sealed class TransponderOwnershipReader(ITimingClient timing) : ITransponderOwnershipReader
{
    public async Task<IReadOnlyList<TransponderOwnershipDTO>> ReadAsync(Guid transponderId, CancellationToken cancellationToken) =>
        (await timing.GetTransponderOwnershipsAsync(transponderId, cancellationToken))
            .Select(owner => new TransponderOwnershipDTO(owner.Id, owner.TransponderId, owner.OwnerId, owner.OwnedFrom, owner.OwnedTo))
            .ToList();
}
