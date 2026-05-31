using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Timing.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.WebUI.Mud.Services;

public class TimingService(ITimingClient timing, IFacilitiesClient facilities) : ITimingService
{
    public async Task<InstallationView> GetInstallationAsync(Guid Id, CancellationToken cancellationToken = default)
    {
        var installation = await timing.InstallationsGETAsync(Id, cancellationToken);
        var facilityName = await GetFacilityNameAsync(installation.FacilityId, cancellationToken);

        return new InstallationView
        {
            Id = installation.Id,
            FacilityId = installation.FacilityId,
            FacilityName = facilityName,
            AgentId = installation.AgentId,
            TimingSystem = installation.TimingSystem.Name,
            Description = installation.Description,
            TimingPoints = installation.TimingPoints.Select(tp => new TimingPointView
            {
                Id = tp.Id,
                Description = tp.Description,
                SystemId = tp.SystemId
            })
        };
    }

    public async Task<IEnumerable<InstallationView>> GetInstallationsAsync(CancellationToken cancellationToken)
    {
        var facilitiesById = (await facilities.FacilityAllAsync(cancellationToken: cancellationToken))
            .ToDictionary(facility => facility.Id, facility => facility.Name);
        var installations = await timing.InstallationsAllAsync(null, cancellationToken);

        return installations.Select(installation => new InstallationView
        {
            Id = installation.Id,
            FacilityId = installation.FacilityId,
            FacilityName = facilitiesById.GetValueOrDefault(installation.FacilityId, string.Empty),
            AgentId = installation.AgentId,
            TimingSystem = installation.TimingSystem.Name,
            Description = installation.Description,
            TimingPoints = installation.TimingPoints.Select(tp => new TimingPointView
            {
                Id = tp.Id,
                Description = tp.Description,
                SystemId = tp.SystemId
            })
        });
    }

    public async Task UpdateInstallationAsync(InstallationView installation, CancellationToken cancellationToken = default)
    {
        var request = new InstallationDTO
        {
            Id = installation.Id,
            FacilityId = installation.FacilityId,
            AgentId = installation.AgentId,
            Description = installation.Description,
            TimingSystem = new TimingSystemDTO
            {
                Name = installation.TimingSystem
            },
            TimingPoints = installation.TimingPoints.Select(tp => new TimingPointDTO
            {
                Id = tp.Id,
                Description = tp.Description,
                SystemId = tp.SystemId
            }).ToList()
        };

        await timing.InstallationsPUTAsync(installation.Id, request, cancellationToken);
    }

    private async Task<string> GetFacilityNameAsync(Guid facilityId, CancellationToken cancellationToken)
    {
        if (facilityId == Guid.Empty)
        {
            return string.Empty;
        }

        var facility = await facilities.Facility2Async(facilityId, cancellationToken);
        return facility.Name;
    }
}
