using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Timing.Client;
using VeloTime.WebUI.Mud.Client.Services;
using Facilities = VeloTime.Module.Facilities.Interface.Data;
using Timing = VeloTime.Module.Timing.Interface.Data;
using static VeloTime.WebUI.Mud.Services.AdministrationClientCall;

namespace VeloTime.WebUI.Mud.Services;

public sealed class InfrastructureAdministrationService(IFacilitiesClient facilities, ITimingClient timing) : IInfrastructureAdministrationService
{
    public Task<IReadOnlyList<Facilities.FacilityDTO>> GetFacilitiesAsync(CancellationToken cancellationToken = default) => Execute<IReadOnlyList<Facilities.FacilityDTO>>(async () =>
        (await facilities.FacilityAllAsync(cancellationToken)).Select(ToDto).OrderBy(facility => facility.Name).ToList());
    public Task<IReadOnlyList<Facilities.CourseLayoutDTO>> GetLayoutsAsync(Guid facilityId, CancellationToken cancellationToken = default) => Execute<IReadOnlyList<Facilities.CourseLayoutDTO>>(async () =>
        (await facilities.LayoutsAllAsync(facilityId, cancellationToken)).Select(ToDto).ToList());
    public Task<Facilities.FacilityDTO> SaveFacilityAsync(Guid? id, Facilities.SaveFacilityRequest request, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        var body = new SaveFacilityRequest { Name = request.Name };
        return ToDto(id.HasValue ? await facilities.UpdateFacilityAsync(id.Value, body, cancellationToken) : await facilities.CreateFacilityAsync(body, cancellationToken));
    });
    public Task DeleteFacilityAsync(Guid id, CancellationToken cancellationToken = default) => Execute(async () => { await facilities.DeleteFacilityAsync(id, cancellationToken); return true; });
    public Task<Facilities.CourseLayoutDTO> SaveLayoutAsync(Guid facilityId, Guid? id, Facilities.SaveCourseLayoutRequest request, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        var body = new SaveCourseLayoutRequest
        {
            Points = request.Points.Select(point => new SaveCoursePointRequest { Id = point.Id, Name = point.Name }).ToList(),
            Segments = request.Segments.Select(segment => new SaveCourseSegmentRequest { Id = segment.Id, StartId = segment.StartId, EndId = segment.EndId, Length = segment.Length }).ToList()
        };
        return ToDto(id.HasValue ? await facilities.UpdateCourseLayoutAsync(facilityId, id.Value, body, cancellationToken) : await facilities.CreateCourseLayoutAsync(facilityId, body, cancellationToken));
    });
    public Task DeleteLayoutAsync(Guid facilityId, Guid id, CancellationToken cancellationToken = default) => Execute(async () => { await facilities.DeleteCourseLayoutAsync(facilityId, id, cancellationToken); return true; });
    public Task SetTimingLinkAsync(Guid pointId, Guid? timingPointId, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await facilities.SetCoursePointTimingLinkAsync(pointId, new SetCoursePointTimingLinkRequest { TimingPointId = timingPointId }, cancellationToken);
        return true;
    });
    public Task<IReadOnlyList<Timing.InstallationDTO>> GetInstallationsAsync(CancellationToken cancellationToken = default) => Execute<IReadOnlyList<Timing.InstallationDTO>>(async () =>
        (await timing.InstallationsAllAsync(null, cancellationToken)).Select(ToDto).OrderBy(installation => installation.AgentId).ToList());
    public Task<Timing.InstallationDTO> SaveInstallationAsync(Guid? id, Timing.SaveInstallationRequest request, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        var body = new VeloTime.Module.Timing.Client.SaveInstallationRequest { FacilityId = request.FacilityId, AgentId = request.AgentId, TimingSystem = request.TimingSystem, Description = request.Description };
        return ToDto(id.HasValue ? await timing.UpdateManagedInstallationAsync(id.Value, body, cancellationToken) : await timing.CreateManagedInstallationAsync(body, cancellationToken));
    });
    public Task DeleteInstallationAsync(Guid id, CancellationToken cancellationToken = default) => Execute(async () => { await timing.DeleteManagedInstallationAsync(id, cancellationToken); return true; });
    public Task<Timing.TimingPointDTO> SaveTimingPointAsync(Guid installationId, Guid? id, Timing.SaveTimingPointRequest request, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        var body = new VeloTime.Module.Timing.Client.SaveTimingPointRequest { SystemId = request.SystemId, Description = request.Description };
        return ToDto(id.HasValue ? await timing.UpdateTimingPointAsync(installationId, id.Value, body, cancellationToken) : await timing.CreateTimingPointAsync(installationId, body, cancellationToken));
    });
    public Task DeleteTimingPointAsync(Guid installationId, Guid id, CancellationToken cancellationToken = default) => Execute(async () => { await timing.DeleteTimingPointAsync(installationId, id, cancellationToken); return true; });

    private static Facilities.FacilityDTO ToDto(VeloTime.Module.Facilities.Client.FacilityDTO facility) => new() { Id = facility.Id, Name = facility.Name, Layouts = facility.Layouts.ToList() };
    private static Facilities.CoursePointDTO ToDto(VeloTime.Module.Facilities.Client.CoursePointDTO point) => new() { Id = point.Id, Name = point.Name, TimingPointId = point.TimingPointId };
    private static Facilities.CourseLayoutDTO ToDto(VeloTime.Module.Facilities.Client.CourseLayoutDTO layout) => new()
    {
        Id = layout.Id, FacilityId = layout.FacilityId,
        Segments = layout.Segments.OrderBy(segment => segment.Order).Select(segment => new Facilities.SegmentDTO
        { Id = segment.Id, Order = segment.Order, Length = segment.Length, Start = ToDto(segment.Start), End = ToDto(segment.End) }).ToList()
    };
    private static Timing.InstallationDTO ToDto(VeloTime.Module.Timing.Client.InstallationDTO installation) => new()
    {
        Id = installation.Id, AgentId = installation.AgentId, FacilityId = installation.FacilityId, Description = installation.Description,
        LastSeen = installation.LastSeen?.UtcDateTime, TimingSystem = new Timing.TimingSystemDTO { Name = installation.TimingSystem.Name },
        TimingPoints = installation.TimingPoints.Select(ToDto).ToList()
    };
    private static Timing.TimingPointDTO ToDto(VeloTime.Module.Timing.Client.TimingPointDTO point) => new() { Id = point.Id, Description = point.Description, SystemId = point.SystemId };
}