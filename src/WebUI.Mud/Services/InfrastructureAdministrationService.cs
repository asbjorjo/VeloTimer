using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Facilities.Interface;
using VeloTime.Module.Common;
using VeloTime.Module.Timing.Client;
using VeloTime.WebUI.Mud.Client.Services;
using Facilities = VeloTime.Module.Facilities.Interface.Data;
using Timing = VeloTime.Module.Timing.Interface.Data;
using System.Text.Json;
using static VeloTime.WebUI.Mud.Services.AdministrationClientCall;

namespace VeloTime.WebUI.Mud.Services;

public sealed class InfrastructureAdministrationService(IFacilitiesClient facilities, ITimingClient timing, WebUiCache cache) : IInfrastructureAdministrationService
{
    public async Task<IReadOnlyList<Facilities.FacilityDTO>> GetFacilitiesAsync(CancellationToken cancellationToken = default)
    {
        var snapshots = await cache.GetAdminAsync(
            AdministrationAuthorization.ReferencePolicy, "admin-facility-list", "facility-snapshot-v1",
            async (IFacilitiesClient factoryClient, CancellationToken token) => await Execute(async () =>
                FacilityLookupSnapshots.Facilities(await factoryClient.FacilityAllAsync(token))),
            [FacilitiesCacheTags.Relationships], cancellationToken);
        return snapshots.Select(FacilityLookupSnapshots.ToContract).OrderBy(facility => facility.Name).ToArray();
    }

    public async Task<IReadOnlyList<Facilities.CourseLayoutDTO>> GetLayoutsAsync(Guid facilityId, CancellationToken cancellationToken = default)
    {
        // Layout projections include the current timing association. Do not retain them in the
        // WebUI cache: a scheduled boundary can change that mapping without another write.
        await cache.RequirePolicyAsync(AdministrationAuthorization.ReferencePolicy, cancellationToken);
        var snapshots = await Execute(async () =>
            FacilityLookupSnapshots.Layouts(await facilities.LayoutsAllAsync(facilityId, cancellationToken), facilityId));
        return snapshots.Select(FacilityLookupSnapshots.ToContract).ToArray();
    }
    public Task<Facilities.FacilityDTO> SaveFacilityAsync(Guid? id, Facilities.SaveFacilityRequest request, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await cache.RequirePolicyAsync(AdministrationAuthorization.FacilitiesPolicy, cancellationToken);
        var body = new SaveFacilityRequest { Name = request.Name };
        var response = await cache.WriteAndInvalidateAsync(
            () => id.HasValue ? facilities.UpdateFacilityAsync(id.Value, body, cancellationToken) : facilities.CreateFacilityAsync(body, cancellationToken),
            FacilitiesCacheTags.Relationships, "saving a facility");
        var snapshot = FacilityLookupSnapshots.From(response);
        if (id.HasValue && snapshot.Id != id.Value)
            throw new InvalidDataException("The saved facility has an unexpected ID.");
        return FacilityLookupSnapshots.ToContract(snapshot);
    });
    public Task DeleteFacilityAsync(Guid id, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await cache.RequirePolicyAsync(AdministrationAuthorization.FacilitiesPolicy, cancellationToken);
        await ExecuteNoContent(() => facilities.DeleteFacilityAsync(id, cancellationToken));
        await cache.InvalidateTagAsync(FacilitiesCacheTags.Relationships, "deleting a facility");
        return true;
    });
    public Task<Facilities.CourseLayoutDTO> SaveLayoutAsync(Guid facilityId, Guid? id, Facilities.SaveCourseLayoutRequest request, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await cache.RequirePolicyAsync(AdministrationAuthorization.FacilitiesPolicy, cancellationToken);
        var body = new SaveCourseLayoutRequest
        {
            Points = request.Points.Select(point => new SaveCoursePointRequest { Id = point.Id, Name = point.Name }).ToList(),
            Segments = request.Segments.Select(segment => new SaveCourseSegmentRequest { Id = segment.Id, StartId = segment.StartId, EndId = segment.EndId, Length = segment.Length }).ToList()
        };
        var response = await cache.WriteAndInvalidateAsync(
            () => id.HasValue ? facilities.UpdateCourseLayoutAsync(facilityId, id.Value, body, cancellationToken) : facilities.CreateCourseLayoutAsync(facilityId, body, cancellationToken),
            FacilitiesCacheTags.Relationships, "saving a course layout");
        return FacilityLookupSnapshots.ToContract(FacilityLookupSnapshots.From(response, id, facilityId));
    });
    public Task DeleteLayoutAsync(Guid facilityId, Guid id, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await cache.RequirePolicyAsync(AdministrationAuthorization.FacilitiesPolicy, cancellationToken);
        await ExecuteNoContent(() => facilities.DeleteCourseLayoutAsync(facilityId, id, cancellationToken));
        await cache.InvalidateTagAsync(FacilitiesCacheTags.Relationships, "deleting a course layout");
        return true;
    });
    public Task SetTimingLinkAsync(Guid pointId, Guid? timingPointId, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await cache.RequirePolicyAsync(AdministrationAuthorization.TimingLinkPolicy, cancellationToken);
        var response = await cache.WriteAndInvalidateAsync(
            () => facilities.SetCoursePointTimingLinkAsync(pointId, new SetCoursePointTimingLinkRequest { TimingPointId = timingPointId }, cancellationToken),
            FacilitiesCacheTags.Relationships, "updating a course-point timing link");
        if (response is null || response.Id != pointId || response.Id == Guid.Empty ||
            string.IsNullOrWhiteSpace(response.Name) || response.TimingPointId != timingPointId)
            throw new InvalidDataException("The saved course-point timing link is invalid.");
        return true;
    });
    public Task<IReadOnlyList<Facilities.CoursePointTimingAssociationSnapshot>> GetCoursePointTimingAssociationsAsync(Guid id, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await cache.RequirePolicyAsync(AdministrationAuthorization.TimingLinkPolicy, cancellationToken);
        var associations = await facilities.GetCoursePointTimingAssociationsAsync(id, cancellationToken);
        if (associations is null || associations.Any(association =>
                association is null || association.CoursePointId != id || association.TimingPointId == Guid.Empty ||
                (association.ValidFromUtc.HasValue && association.ValidToUtc.HasValue &&
                 association.ValidFromUtc.Value >= association.ValidToUtc.Value)))
            throw new InvalidDataException("The coursepoint timing-association schedule is invalid.");
        return (IReadOnlyList<Facilities.CoursePointTimingAssociationSnapshot>)associations.ToArray();
    });
    public Task SetCoursePointTimingAssociationAsync(Guid id, Facilities.SetCoursePointTimingAssociationRequest request, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await cache.RequirePolicyAsync(AdministrationAuthorization.TimingLinkPolicy, cancellationToken);
        try
        {
            await cache.WriteAndInvalidateAsync(
                () => facilities.SetCoursePointTimingAssociationAsync(id, request, cancellationToken),
                FacilitiesCacheTags.Relationships, "updating a course-point timing association");
        }
        catch (VeloTime.Module.Facilities.Client.ApiException exception) when (exception.StatusCode == 409)
        {
            try
            {
                var preview = JsonSerializer.Deserialize<Facilities.TimingAssociationConflictPreview>(
                    exception.Response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (preview is not null && !string.IsNullOrWhiteSpace(preview.ConfirmationToken))
                    throw new TimingAssociationConflictException(preview, exception);
            }
            catch (JsonException)
            {
                // A malformed structured conflict must remain an explicit upstream failure.
            }
            throw new HttpRequestException("The timing-association conflict preview could not be read.", exception,
                System.Net.HttpStatusCode.BadGateway);
        }
        return true;
    });
    public async Task<IReadOnlyList<Timing.InstallationDTO>> GetInstallationsAsync(CancellationToken cancellationToken = default)
    {
        await cache.RequirePolicyAsync(AdministrationAuthorization.ReferencePolicy, cancellationToken);
        return await Execute<IReadOnlyList<Timing.InstallationDTO>>(async () =>
            (await timing.InstallationsAllAsync(null, cancellationToken)).Select(ToDto).OrderBy(installation => installation.AgentId).ToList());
    }
    public Task<Timing.InstallationDTO> SaveInstallationAsync(Guid? id, Timing.SaveInstallationRequest request, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await cache.RequirePolicyAsync(AdministrationAuthorization.TimingPolicy, cancellationToken);
        var body = new VeloTime.Module.Timing.Client.SaveInstallationRequest { FacilityId = request.FacilityId, AgentId = request.AgentId, TimingSystem = request.TimingSystem, Description = request.Description };
        var response = id.HasValue ? await timing.UpdateManagedInstallationAsync(id.Value, body, cancellationToken) : await timing.CreateManagedInstallationAsync(body, cancellationToken);
        if (response is null || response.Id == Guid.Empty || (id.HasValue && response.Id != id.Value) ||
            response.FacilityId != request.FacilityId)
            throw new InvalidDataException("The saved installation has unexpected entity IDs.");
        return ToDto(response);
    });
    public Task DeleteInstallationAsync(Guid id, CancellationToken cancellationToken = default) => Execute(async () => { await cache.RequirePolicyAsync(AdministrationAuthorization.TimingPolicy, cancellationToken); await ExecuteNoContent(() => timing.DeleteManagedInstallationAsync(id, cancellationToken)); return true; });
    public Task<Timing.TimingPointDTO> SaveTimingPointAsync(Guid installationId, Guid? id, Timing.SaveTimingPointRequest request, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await cache.RequirePolicyAsync(AdministrationAuthorization.TimingPolicy, cancellationToken);
        var body = new VeloTime.Module.Timing.Client.SaveTimingPointRequest { SystemId = request.SystemId, Description = request.Description };
        var response = id.HasValue ? await timing.UpdateTimingPointAsync(installationId, id.Value, body, cancellationToken) : await timing.CreateTimingPointAsync(installationId, body, cancellationToken);
        if (response is null || response.Id == Guid.Empty || (id.HasValue && response.Id != id.Value))
            throw new InvalidDataException("The saved timing point has an unexpected entity ID.");
        return ToDto(response);
    });
    public Task DeleteTimingPointAsync(Guid installationId, Guid id, CancellationToken cancellationToken = default) => Execute(async () => { await cache.RequirePolicyAsync(AdministrationAuthorization.TimingPolicy, cancellationToken); await ExecuteNoContent(() => timing.DeleteTimingPointAsync(installationId, id, cancellationToken)); return true; });

    private static Facilities.FacilityDTO ToDto(VeloTime.Module.Facilities.Client.FacilityDTO facility) => new() { Id = facility.Id, Name = facility.Name, Layouts = facility.Layouts.ToList() };
    private static Facilities.CoursePointDTO ToDto(VeloTime.Module.Facilities.Client.CoursePointDTO point) => new() { Id = point.Id, Name = point.Name, TimingPointId = point.TimingPointId };
    private static Facilities.CourseLayoutDTO ToDto(VeloTime.Module.Facilities.Client.CourseLayoutDTO layout) =>
        FacilityLookupSnapshots.ToContract(FacilityLookupSnapshots.From(layout));
    private static Timing.InstallationDTO ToDto(VeloTime.Module.Timing.Client.InstallationDTO installation)
    {
        if (installation is null || installation.Id == Guid.Empty || installation.FacilityId == Guid.Empty ||
            installation.TimingSystem is null || string.IsNullOrWhiteSpace(installation.TimingSystem.Name) ||
            installation.TimingPoints is null)
            throw new InvalidDataException("The installation response is invalid.");
        return new()
        {
            Id = installation.Id, AgentId = installation.AgentId, FacilityId = installation.FacilityId, Description = installation.Description,
            LastSeen = installation.LastSeen?.UtcDateTime, TimingSystem = new Timing.TimingSystemDTO { Name = installation.TimingSystem.Name },
            TimingPoints = installation.TimingPoints.Select(ToDto).ToList()
        };
    }
    private static Timing.TimingPointDTO ToDto(VeloTime.Module.Timing.Client.TimingPointDTO point)
    {
        if (point is null || point.Id == Guid.Empty)
            throw new InvalidDataException("The timing-point response is invalid.");
        return new() { Id = point.Id, Description = point.Description, SystemId = point.SystemId };
    }
}