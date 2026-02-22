
using VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.Module.Timing.Interface.Client;

public interface ITimingClient
{
    Task<InstallationDTO> GetInstallationById(Guid id, CancellationToken cancellationToken = default);
    Task<ICollection<InstallationDTO>> GetAllInstallations(CancellationToken cancellationToken = default);
    Task<ICollection<InstallationDTO>> GetInstallationsForFacility(Guid id);
}
