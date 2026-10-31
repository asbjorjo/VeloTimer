using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Facilities.Interface;
using VeloTime.Module.Timing.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.WebUI.Mud.Services;

public class TimingService(ITimingClient timing, WebUiCache cache) : ITimingService
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
        var installations = await timing.InstallationsAllAsync(null, cancellationToken);
        var facilitiesById = new Dictionary<Guid, string>();
        foreach (var facilityId in installations.Select(installation => installation.FacilityId).Distinct())
            facilitiesById[facilityId] = await GetFacilityNameAsync(facilityId, cancellationToken);

        return installations.Select(installation => new InstallationView
        {
            Id = installation.Id,
            FacilityId = installation.FacilityId,
            FacilityName = facilitiesById.GetValueOrDefault(installation.FacilityId, string.Empty),
            AgentId = installation.AgentId,
            TimingSystem = installation.TimingSystem.Name,
            LastSeen = installation.LastSeen?.UtcDateTime,
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

        await AdministrationClientCall.ExecuteNoContent(() => timing.InstallationsPUTAsync(installation.Id, request, cancellationToken));
    }

    private async Task<string> GetFacilityNameAsync(Guid facilityId, CancellationToken cancellationToken)
    {
        if (facilityId == Guid.Empty)
        {
            return string.Empty;
        }

        return await cache.GetPublicAsync(
            "facility-name", "string-v1", facilityId.ToString("N"),
            async (IFacilitiesClient factoryClient, CancellationToken token) =>
            {
                var facility = await factoryClient.Facility2Async(facilityId, token);
                if (facility is null || facility.Id != facilityId || string.IsNullOrWhiteSpace(facility.Name))
                    throw new InvalidDataException("A facility name could not be resolved.");
                return facility.Name;
            },
            [FacilitiesCacheTags.Relationships], cancellationToken);
    }
}
