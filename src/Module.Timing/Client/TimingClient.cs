using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Timing.Interface.Client;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Mapping;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Client;

internal class TimingClient(TimingDbContext storage) : ITimingClient
{
    public Task<ICollection<InstallationDTO>> GetAllInstallations(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<InstallationDTO> GetInstallationById(Guid id, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task<ICollection<InstallationDTO>> GetInstallationsForFacility(Guid id)
    {
        using var activity = Instrumentation.Source.StartActivity(nameof(GetInstallationsForFacility));
        var installations = await storage.Set<Installation>()
            .Where(i => i.Facility == id)
            .Include(i => i.TimingPoints)
            .ToListAsync();

        return installations.Select(i => i.ToDto()).ToList();
    }
}
