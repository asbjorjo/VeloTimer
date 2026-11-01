using System.Net;
using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Facilities.Interface;
using VeloTime.Module.Timing.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;
using Contracts = VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.WebUI.Mud.Services;

public sealed class TransponderStatusService(ITimingClient timing,
    ILogger<TransponderStatusService> logger, WebUiCache cache) : ITransponderStatusService
{
    public async Task<TransponderStatusView> GetAsync(Guid id, int ownershipPage = 0, int warningPage = 0,
        int pageSize = 10, CancellationToken cancellationToken = default)
    {
        TransponderStatus result;
        try
        {
            result = await timing.GetTransponderStatusAsync(id, ownershipPage, warningPage, pageSize, cancellationToken);
        }
        catch (Module.Timing.Client.ApiException exception)
        {
            throw new HttpRequestException("Transponder status could not be loaded. Check your access and retry.",
                exception, (HttpStatusCode)exception.StatusCode);
        }
        if (result is null || result.Id != id)
            throw new HttpRequestException("The transponder status response was invalid.", null, HttpStatusCode.BadGateway);
        var status = new Contracts.TransponderStatus(result.Id, result.Label, result.Code, result.SystemId, result.System,
            result.Administration, result.LastSeen is null ? null : ToSighting(result.LastSeen),
            result.WarningWindowStart, result.WarningWindowEnd, result.LowBattery, result.LowStrength, result.LowHits,
            result.Ownerships.Select(owner => new Contracts.TransponderOwnershipDTO(
                owner.Id, owner.TransponderId, owner.OwnerId, owner.OwnedFrom, owner.OwnedTo)).ToList(),
            result.OwnershipCount, result.Warnings.Select(ToSighting).ToList(), result.WarningCount);
        var names = new Dictionary<Guid, string>();
        string? facilityError = null;
        var facilityIds = status.Warnings.Select(warning => warning.FacilityId)
            .Concat(status.LastSeen is { } last ? [last.FacilityId] : []).Where(facilityId => facilityId != Guid.Empty).Distinct();
        foreach (var facilityId in facilityIds)
        {
            try
            {
                names.Add(facilityId, await cache.GetPublicAsync(
                    "facility-name", "string-v1", facilityId.ToString("N"),
                    async (IFacilitiesClient factoryClient, CancellationToken token) =>
                    {
                        var facility = await factoryClient.Facility2Async(facilityId, token);
                        if (facility is null || facility.Id != facilityId || string.IsNullOrWhiteSpace(facility.Name))
                            throw new HttpRequestException("An assigned facility returned no name.", null, HttpStatusCode.BadGateway);
                        return facility.Name;
                    },
                    [FacilitiesCacheTags.Relationships], cancellationToken));
            }
            catch (Module.Facilities.Client.ApiException exception)
            {
                logger.LogWarning(exception, "Could not resolve status facility {FacilityId}", facilityId);
                facilityError = "Some facility names could not be loaded. Retry to resolve assigned facilities.";
            }
            catch (HttpRequestException exception)
            {
                logger.LogWarning(exception, "Could not resolve status facility {FacilityId}", facilityId);
                facilityError = "Some facility names could not be loaded. Retry to resolve assigned facilities.";
            }
        }
        return new(status, names, facilityError);
    }

    private static Contracts.TransponderSighting ToSighting(TransponderSighting sighting) =>
        new(sighting.Id, sighting.Time, sighting.FacilityId, sighting.InstallationId, sighting.Installation,
            sighting.TimingPointId, sighting.TimingPoint, sighting.LowBattery, sighting.LowStrength, sighting.LowHits);
}
