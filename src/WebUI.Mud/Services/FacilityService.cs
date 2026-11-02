using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Timing.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;
using VeloTime.Module.Facilities.Interface;

namespace VeloTime.WebUI.Mud.Services
{
    public class FacilityService(IFacilitiesClient facilities, ITimingClient timing, WebUiCache cache) : IFacilityService
    {
        public async Task<CourseLayoutDetailView> GetCourseLayoutDetailAsync(Guid layoutId, CancellationToken cancellationToken = default)
        {
            // Detail views expose current timing-point IDs. A scheduled boundary can change
            // those IDs without a mutation/cache eviction, so always read the current projection.
            var layout = FacilityLookupSnapshots.From(
                await facilities.LayoutsAsync(layoutId, cancellationToken: cancellationToken), layoutId);

            return new CourseLayoutDetailView
            {
                Id = layout.Id,
                Segments = layout.Segments.Select(s => new SegmentDetailView
                {
                    Id = s.Id,
                    Distance = s.Length,
                    Order = s.Order,
                    StartPoint = new CoursePointView
                    {
                        Id = s.Start.Id,
                        Name = s.Start.Name,
                        TimingPoint = s.Start.TimingPointId ?? Guid.Empty,
                    },
                    EndPoint = new CoursePointView
                    {
                        Id = s.End.Id,
                        Name = s.End.Name,
                        TimingPoint = s.End.TimingPointId ?? Guid.Empty,
                    },
                }).OrderBy(s => s.Order),
            };
        }

        public async Task<IEnumerable<FacilityView>> GetFacilitiesAsync(CancellationToken token)
        {
            var f = await cache.GetPublicAsync(
                "facility-list", "facility-view-v1", "all",
                async (IFacilitiesClient factoryClient, CancellationToken factoryToken) =>
                    FacilityLookupSnapshots.Facilities(await factoryClient.FacilityAllAsync(cancellationToken: factoryToken)),
                [FacilitiesCacheTags.Relationships], token);

            return f.Select(facility => new FacilityView
            {
                Id = facility.Id,
                Name = facility.Name,
                LayoutId = facility.LayoutIds.ToArray(),
            }).OrderBy(facility => facility.Name).ToArray();
        }

        public async Task<IEnumerable<CourseLayoutView>> GetFacilityLayoutsAsync(Guid id, CancellationToken token)
        {
            var layouts = await cache.GetPublicAsync(
                "facility-layout-list", "course-layout-geometry-view-v2", id.ToString("N"),
                async (IFacilitiesClient factoryClient, CancellationToken factoryToken) =>
                    FacilityLookupSnapshots.GeometryLayouts(
                        await factoryClient.LayoutsAllAsync(id, cancellationToken: factoryToken), id),
                [FacilitiesCacheTags.Relationships], token);

            return layouts.Select(layout => new CourseLayoutView
            {
                Id = layout.Id,
                Segments = layout.Segments.Select(segment => new SegmentView
                {
                    Id = segment.Id,
                    Name = $"{segment.Start.Name} - {segment.End.Name}",
                    Distance = segment.Length,
                    Order = segment.Order,
                }).OrderBy(s => s.Order),
            }).ToArray();
        }

        public async Task<IEnumerable<InstallationView>> GetInstallationsAsync(Guid id, CancellationToken cancellationToken)
        {
            var i = await timing.InstallationsAllAsync(id, cancellationToken);
            var facilityName = await GetFacilityNameAsync(id, cancellationToken);

            return i.Select(i => new InstallationView
            {
                Id = i.Id,
                FacilityId = i.FacilityId,
                FacilityName = facilityName,
                Description = i.Description,
                AgentId = i.AgentId,
                TimingSystem = i.TimingSystem.Name,
            }).ToArray();
        }

        private async Task<string> GetFacilityNameAsync(Guid facilityId, CancellationToken cancellationToken)
        {
            if (facilityId == Guid.Empty)
                return string.Empty;

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
}
