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
            FacilityLookupSnapshots.Layouts(await facilities.LayoutsAllAsync(facilityId, cancellationToken: cancellationToken), facilityId));
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
    public Task<Facilities.CourseLayoutDTO> CreateLayoutAsync(Guid facilityId, Facilities.SaveCourseLayoutRequest request, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await cache.RequirePolicyAsync(AdministrationAuthorization.FacilitiesPolicy, cancellationToken);
        var body = new SaveCourseLayoutRequest { Points = ToClientPoints(request.Points), Segments = ToClientSegments(request.Segments) };
        var response = await cache.WriteAndInvalidateAsync(
            () => facilities.CreateCourseLayoutAsync(facilityId, body, cancellationToken),
            FacilitiesCacheTags.Relationships, "creating a course layout");
        return FacilityLookupSnapshots.ToContract(FacilityLookupSnapshots.From(response, null, facilityId));
    });
    public Task<Facilities.CourseLayoutHistoryDTO> GetLayoutHistoryAsync(Guid facilityId, Guid id, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await cache.RequirePolicyAsync(AdministrationAuthorization.ReferencePolicy, cancellationToken);
        var history = await facilities.GetCourseLayoutHistoryAsync(facilityId, id, cancellationToken);
        if (history is null || history.LayoutId != id || history.Assignments is null || history.Revisions is null ||
            history.Assignments.Any(interval => interval is null || !IsValidInterval(interval.ValidFromUtc, interval.ValidToUtc)) ||
            history.Revisions.Any(revision => revision is null || revision.RevisionId == Guid.Empty || revision.Segments is null ||
                !IsValidInterval(revision.ValidFromUtc, revision.ValidToUtc) || revision.Segments.Any(segment => segment is null)))
            throw new InvalidDataException("The course layout history is invalid.");
        return new Facilities.CourseLayoutHistoryDTO
        {
            LayoutId = history.LayoutId,
            Assignments = history.Assignments.Select(ToContract).ToArray(),
            Revisions = history.Revisions.Select(revision => new Facilities.CourseLayoutRevisionHistoryDTO
            {
                RevisionId = revision.RevisionId,
                ValidFromUtc = revision.ValidFromUtc,
                ValidToUtc = revision.ValidToUtc,
                Segments = revision.Segments.OrderBy(segment => segment.Order).Select(segment => new Facilities.CourseSegmentHistoryDTO
                {
                    Order = segment.Order, StartPointId = segment.StartPointId, EndPointId = segment.EndPointId, Length = segment.Length
                }).ToArray()
            }).ToArray()
        };
    });
    public Task<Facilities.SetCourseLayoutRevisionResponse> SetLayoutRevisionAsync(Guid facilityId, Guid id, Facilities.SetCourseLayoutRevisionRequest request, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await cache.RequirePolicyAsync(AdministrationAuthorization.FacilitiesPolicy, cancellationToken);
        var body = new VeloTime.Module.Facilities.Client.SetCourseLayoutRevisionRequest
        {
            Points = ToClientPoints(request.Points),
            Segments = ToClientSegments(request.Segments),
            ValidFromUtc = request.ValidFromUtc,
            ValidToUtc = request.ValidToUtc,
            ConfirmationToken = request.ConfirmationToken
        };
        try
        {
            var response = await cache.WriteAndInvalidateAsync(
                () => facilities.SetCourseLayoutRevisionAsync(facilityId, id, body, cancellationToken),
                FacilitiesCacheTags.Relationships, "revising a course layout");
            if (response is null || response.ChangeId.HasValue != response.RevisionId.HasValue ||
                response.ChangeId == Guid.Empty || response.RevisionId == Guid.Empty)
                throw new InvalidDataException("The course layout revision response is invalid.");
            return new Facilities.SetCourseLayoutRevisionResponse(response.ChangeId, response.RevisionId);
        }
        catch (VeloTime.Module.Facilities.Client.ApiException exception) when (exception.StatusCode == 409)
        {
            try
            {
                var preview = exception is VeloTime.Module.Facilities.Client.ApiException<
                    VeloTime.Module.Facilities.Client.CourseLayoutRevisionConflictPreview> { Result: { } result }
                    ? ToContract(result)
                    : JsonSerializer.Deserialize<Facilities.CourseLayoutRevisionConflictPreview>(
                        exception.Response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (preview is not null && preview.Code is "revision-overlap" or "confirmation-stale" &&
                    preview.CourseLayoutId == id && !string.IsNullOrWhiteSpace(preview.ConfirmationToken) &&
                    preview.Overlaps is not null && preview.Overlaps.All(overlap =>
                        overlap is not null && overlap.RevisionId != Guid.Empty &&
                        IsValidInterval(overlap.OverlapValidFromUtc, overlap.OverlapValidToUtc)))
                    throw new CourseLayoutRevisionConflictException(preview, exception);
            }
            catch (JsonException)
            {
                // A malformed structured conflict must remain an explicit upstream failure.
            }
            throw new HttpRequestException("The course layout revision conflict preview could not be read.", exception,
                System.Net.HttpStatusCode.BadGateway);
        }
    });
    public Task<Facilities.SetFacilityLayoutAssignmentResponse> SetLayoutAssignmentAsync(Guid facilityId, Guid id, Facilities.SetFacilityLayoutAssignmentRequest request, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await cache.RequirePolicyAsync(AdministrationAuthorization.FacilitiesPolicy, cancellationToken);
        var body = new VeloTime.Module.Facilities.Client.SetFacilityLayoutAssignmentRequest
        {
            InUse = request.InUse, ValidFromUtc = request.ValidFromUtc, ValidToUtc = request.ValidToUtc
        };
        var response = await cache.WriteAndInvalidateAsync(
            () => facilities.SetFacilityLayoutAssignmentAsync(facilityId, id, body, cancellationToken),
            FacilitiesCacheTags.Relationships, "updating a course layout assignment");
        if (response is null || response.Assignments is null || response.ChangeId == Guid.Empty ||
            response.Assignments.Any(interval => interval is null || !IsValidInterval(interval.ValidFromUtc, interval.ValidToUtc)))
            throw new InvalidDataException("The course layout assignment response is invalid.");
        return new Facilities.SetFacilityLayoutAssignmentResponse(response.ChangeId, response.Assignments.Select(ToContract).ToArray());
    });
    private static List<SaveCoursePointRequest> ToClientPoints(IEnumerable<Facilities.SaveCoursePointRequest> points) =>
        points.Select(point => new SaveCoursePointRequest { Id = point.Id, Name = point.Name }).ToList();
    private static List<SaveCourseSegmentRequest> ToClientSegments(IEnumerable<Facilities.SaveCourseSegmentRequest> segments) =>
        segments.Select(segment => new SaveCourseSegmentRequest { Id = segment.Id, StartId = segment.StartId, EndId = segment.EndId, Length = segment.Length }).ToList();
    private static bool IsValidInterval(DateTimeOffset? from, DateTimeOffset? to) =>
        !from.HasValue || !to.HasValue || from.Value < to.Value;
    private static Facilities.EffectiveIntervalDTO ToContract(VeloTime.Module.Facilities.Client.EffectiveIntervalDTO interval) =>
        new() { ValidFromUtc = interval.ValidFromUtc, ValidToUtc = interval.ValidToUtc };
    private static Facilities.CourseLayoutRevisionConflictPreview? ToContract(
        VeloTime.Module.Facilities.Client.CourseLayoutRevisionConflictPreview preview) =>
        preview.Overlaps is null || preview.Overlaps.Any(overlap => overlap is null)
            ? null
            : new(preview.Code, preview.CourseLayoutId, preview.ValidFromUtc, preview.ValidToUtc,
                preview.Overlaps.Select(overlap => new Facilities.CourseLayoutRevisionOverlap(overlap.RevisionId,
                    overlap.ValidFromUtc, overlap.ValidToUtc, overlap.OverlapValidFromUtc, overlap.OverlapValidToUtc,
                    overlap.TotalLength, overlap.SegmentCount)).ToArray(),
                preview.ConfirmationToken);
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
        // Intentionally discard the returned CoursePointDTO: this mutation only needs completion,
        // and callers refresh the authoritative current mapping separately after cache invalidation.
        await ExecuteNoContent(() => facilities.SetCoursePointTimingLinkAsync(pointId,
            new SetCoursePointTimingLinkRequest { TimingPointId = timingPointId }, cancellationToken));
        await cache.InvalidateTagAsync(FacilitiesCacheTags.Relationships, "updating a course-point timing link");
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
        return (IReadOnlyList<Facilities.CoursePointTimingAssociationSnapshot>)associations.Select(association =>
            new Facilities.CoursePointTimingAssociationSnapshot(association.TimingPointId, association.CoursePointId,
                association.ValidFromUtc, association.ValidToUtc)).ToArray();
    });
    public Task SetCoursePointTimingAssociationAsync(Guid id, Facilities.SetCoursePointTimingAssociationRequest request, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await cache.RequirePolicyAsync(AdministrationAuthorization.TimingLinkPolicy, cancellationToken);
        try
        {
            var body = new VeloTime.Module.Facilities.Client.SetCoursePointTimingAssociationRequest
            {
                TimingPointId = request.TimingPointId,
                ValidFromUtc = request.ValidFromUtc,
                ValidToUtc = request.ValidToUtc,
                ConfirmationToken = request.ConfirmationToken
            };
            var response = await cache.WriteAndInvalidateAsync(
                () => facilities.SetCoursePointTimingAssociationAsync(id, body, cancellationToken),
                FacilitiesCacheTags.Relationships, "updating a course-point timing association");
            if (response is null || response.Changes is null ||
                (response.Changes.Count == 0 ? response.ChangeId.HasValue : !response.ChangeId.HasValue) ||
                response.ChangeId == Guid.Empty || response.Changes.Any(change =>
                    change is null || (change.Before is null && change.After is null)))
                throw new InvalidDataException("The saved timing-association response is invalid.");
        }
        catch (VeloTime.Module.Facilities.Client.ApiException exception) when (exception.StatusCode == 409)
        {
            try
            {
                // NSwag stores a declared 409 body in Result and may leave Response empty.
                // Retain raw-body handling for undeclared/failed-deserialization responses.
                var preview = exception is VeloTime.Module.Facilities.Client.ApiException<
                    VeloTime.Module.Facilities.Client.TimingAssociationConflictPreview> { Result: { } result }
                    ? ToContract(result)
                    : JsonSerializer.Deserialize<Facilities.TimingAssociationConflictPreview>(
                        exception.Response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (preview is not null && preview.Code is "destination-occupied" or "confirmation-stale" &&
                    !string.IsNullOrWhiteSpace(preview.ConfirmationToken) &&
                    preview.Requested is { } requested && requested.CoursePointId == id &&
                    requested.TimingPointId == request.TimingPointId &&
                    NormalizeAssociationBound(requested.ValidFromUtc) == NormalizeAssociationBound(request.ValidFromUtc) &&
                    NormalizeAssociationBound(requested.ValidToUtc) == NormalizeAssociationBound(request.ValidToUtc) &&
                    preview.Conflicts is not null && preview.Conflicts.All(conflict =>
                        conflict is not null && conflict.Association is { } association &&
                        association.CoursePointId != Guid.Empty && association.TimingPointId != Guid.Empty &&
                        (!conflict.OverlapValidFromUtc.HasValue || !conflict.OverlapValidToUtc.HasValue ||
                         conflict.OverlapValidFromUtc.Value < conflict.OverlapValidToUtc.Value)))
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
    private static DateTimeOffset? NormalizeAssociationBound(DateTimeOffset? value)
    {
        if (!value.HasValue) return null;
        var utc = value.Value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }

    private static Facilities.TimingAssociationConflictPreview? ToContract(
        VeloTime.Module.Facilities.Client.TimingAssociationConflictPreview preview)
    {
        if (preview.Requested is null || preview.Conflicts is null ||
            preview.Conflicts.Any(conflict => conflict is null || conflict.Association is null))
            return null;
        return new(preview.Code,
            new(preview.Requested.CoursePointId, preview.Requested.TimingPointId,
                preview.Requested.ValidFromUtc, preview.Requested.ValidToUtc),
            preview.Conflicts.Select(conflict => new Facilities.TimingAssociationDestinationConflict(
                new(conflict.Association.TimingPointId, conflict.Association.CoursePointId,
                    conflict.Association.ValidFromUtc, conflict.Association.ValidToUtc),
                conflict.OverlapValidFromUtc, conflict.OverlapValidToUtc)).ToArray(),
            preview.ConfirmationToken);
    }
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