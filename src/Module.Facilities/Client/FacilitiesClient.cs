using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics;
using VeloTime.Module.Facilities.Interface.Client;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Service;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Client;

internal class FacilitiesClient(FacilitiesService facilities, FacilityDbContext storage, IMemoryCache cache) : IFacitiliesClient
{
    private MemoryCacheEntryOptions cacheEntryOptions = new()
    {
        SlidingExpiration = TimeSpan.FromMinutes(5)
    };

    public async Task CreateCourseLayout(CourseLayoutDTO Layout)
    {
        CourseLayout layout = new()
        {
            Id = Layout.FacilityId,
            Segments = Layout.Segments.Select(s => new CourseSegment
            {
                Start = new() { TimingPoint = s.Start.TimingPointId },
                End = new() { TimingPoint = s.End.TimingPointId },
                Length = s.Length
            }).ToList()
        };

        await facilities.CreateCourseLayout(layout);
    }

    public async Task<double> DistanceBetweenCoursePoints(Guid Start, Guid End)
    {
        using var activity = Instrumentation.Source.StartActivity("DistanceBetweenCoursePoints");
        activity?.SetTag("StartCoursePointId", Start);
        activity?.SetTag("EndCoursePointId", End);

        string cacheKey = "Facility_DistanceBetween_" + Start.ToString() + "_" + End.ToString();
        double distance = 0;

        if (cache.TryGetValue(cacheKey, out distance))
        {
            return distance;
        }

        CoursePoint StartPoint = await storage.Set<CoursePoint>()
            .SingleAsync(cp => cp.Id == Start);
        CoursePoint EndPoint = Start == End ? StartPoint : await storage.Set<CoursePoint>()
            .SingleAsync(cp => cp.Id == End);

        distance = await facilities.DistanceBetweenCoursePoints(StartPoint, EndPoint);

        cache.Set(cacheKey, distance, cacheEntryOptions);

        return distance;
    }

    public async Task<CoursePointDistance> DistanceBetweenTimingPoints(Guid Start, Guid End)
    {
        using var activity = Instrumentation.Source.StartActivity("DistanceBetweenTimingPoints");
        activity?.SetTag("StartTimingPointId", Start);
        activity?.SetTag("EndTimingPointId", End);

        string cacheKey = $"Facility_DistanceBetween_{Start}_{End}";
        CoursePointDistance distance;

        if (!cache.TryGetValue(cacheKey, out distance!)) {
            var StartPoint = await facilities.FindCoursePointByTimingPointId(Start);
            var EndPoint = await facilities.FindCoursePointByTimingPointId(End);

            distance = new(
                StartPoint.Id,
                EndPoint.Id,
                await facilities.DistanceBetweenCoursePoints(StartPoint, EndPoint)
            );
            cache.Set(cacheKey, distance, cacheEntryOptions);
        }

        return distance;
    }

    public async Task<ICollection<FacilityDTO>> GetAllFacilitiesAsync(CancellationToken token = default)
    {
        var facilities = await storage.Set<Facility>().ToListAsync(token);

        return facilities.Select(f => new FacilityDTO
        {
            Id = f.Id,
            Name = f.Name,
            Layouts = f.Layouts.Select(l => l.Id).ToList()
        }).ToList();
    }

    public Task<CourseLayoutDTO> GetCourseLayoutAsync(Guid layoutId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<CoursePointDTO> GetCoursePointById(Guid coursePointId)
    {
        using var activity = Instrumentation.Source.StartActivity("GetCoursePointById");
        activity?.SetTag("CoursePointId", coursePointId);

        return storage.Set<CoursePoint>()
            .Where(cp => cp.Id == coursePointId)
            .Select(cp => new CoursePointDTO
            {
                Id = cp.Id,
                TimingPointId = cp.TimingPoint,
                Name = cp.Name ?? string.Empty
            })
            .SingleAsync();
    }

    public async Task<CoursePointDTO> GetCoursePointByTimingPointId(Guid timingPointId)
    {
        using var activity = Instrumentation.Source.StartActivity("GetCoursePointByTimingPointId");
        activity?.SetTag("TimingPointId", timingPointId);

        var cp = await storage.Set<CoursePoint>()
            .SingleAsync(cp => cp.TimingPoint == timingPointId);

        return new()
        {
            Id = cp.Id,
            TimingPointId = cp.TimingPoint
        };
    }

    public async Task<FacilityDTO> GetFacilityAsync(Guid facilityId)
    {
        var facility = await storage.FindAsync<Facility>(facilityId);

        if (facility == null)
        {
            return null!;
        }

        return new FacilityDTO
        {
            Id = facility.Id,
            Name = facility.Name,
            Layouts = facility.Layouts.Select(l => l.Id).ToList()
        };
    }

    public async Task<ICollection<CourseLayoutDTO>> GetFacilityCourseLayoutsAsync(Guid facilityId, CancellationToken token)
    {
        var layouts = await storage.Set<CourseLayout>()
            .Where(cl => cl.FacilityId == facilityId)
            .Include(cl => cl.Segments)
            .ThenInclude(s => s.Start)
            .Include(cl => cl.Segments)
            .ThenInclude(s => s.End)
            .ToListAsync(cancellationToken: token);

        return layouts.Select(l => new CourseLayoutDTO { 
            FacilityId = l.FacilityId, 
            Id = l.Id, 
            Segments = l.Segments.Select(s => new SegmentDTO
            {
                Id = s.Id,
                Start = new CoursePointDTO { Id = s.Id, TimingPointId = s.Start.Id },
                End = new CoursePointDTO { Id = s.Id, TimingPointId = s.End.Id },
                Length = s.Length
            }).ToList()
        }).ToList();
    }
}
