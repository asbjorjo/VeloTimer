using VeloTime.Module.Facilities.Interface.Client;
using VeloTime.Module.Timing.Interface.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.WebUI.Mud.Services
{
    public class FacilityService(IFacitiliesClient facitilies, ITimingClient timing) : IFacilityService
    {
        public async Task<CourseLayoutDetailView> GetCourseLayoutDetailAsync(Guid layoutId, CancellationToken cancellationToken = default)
        {
            var layout = await facitilies.GetCourseLayoutAsync(layoutId, cancellationToken);

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
            var f = await facitilies.GetAllFacilitiesAsync(token);

            var facilities = f.Select(f => new FacilityView
            {
                Id = f.Id,
                Name = f.Name,
                LayoutId = f.Layouts,
            });

            return facilities.OrderBy(f => f.Name);
        }

        public async Task<IEnumerable<CourseLayoutView>> GetFacilityLayoutsAsync(Guid id, CancellationToken token)
        {
            var l = await facitilies.GetFacilityCourseLayoutsAsync(id, token);

            return l.Select(l => new CourseLayoutView
            {
                Id = l.Id,
                Segments = l.Segments.Select(s => new SegmentView
                {
                    Id = s.Id,
                    Name = $"{s.Start.Name} - {s.End.Name}",
                    Distance = s.Length,
                    Order = s.Order,
                }).OrderBy(s => s.Order),
            });
        }

        public async Task<IEnumerable<InstallationView>> GetInstallationsAsync(Guid id, CancellationToken cancellationToken)
        {
            var i = await timing.GetInstallationsForFacility(id);

            return i.Select(i => new InstallationView
            {
                Id = i.Id,
                Description = i.Description,
                AgentId = i.AgentId,
                TimingSystem = i.TimingSystem.Name,
            });
        }
    }
}
