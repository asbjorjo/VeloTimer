using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

internal class Worker(IServiceProvider serviceProvider, IHostApplicationLifetime hostApplicationLifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();

        var timingContext = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
        var facilityContext = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
        var statisticsContext = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
        
        var installations = await timingContext.Set<Installation>().Where(i => i.AgentId == TimingData.Installation.AgentId).ToListAsync();
        Installation installation;

        if (installations.Any())
        {
            installation = installations.First();
        }
        else
        {
            installation = TimingData.Installation;
            await timingContext.AddAsync(installation);
            await timingContext.SaveChangesAsync();
        }


        var sola = await timingContext.Set<Installation>()
            .Include(i => i.TimingPoints)
            .SingleAsync(i => i.AgentId == TimingData.Installation.AgentId);

        if (!sola.TimingPoints.Any())
        {
            sola.TimingPoints.AddRange(TimingData.timingPoints);
            await timingContext.SaveChangesAsync();
        }

        foreach (var timingpoint in sola.TimingPoints)
        {
            if (string.IsNullOrEmpty(timingpoint.Description)) timingpoint.Description = TimingData.timingPoints.Single(t => t.SystemId == timingpoint.SystemId).Description;
        }
        await timingContext.SaveChangesAsync();

        var facility = await facilityContext.Set<Facility>()
            .Include(f => f.Layouts).ThenInclude(l => l.Revisions).ThenInclude(r => r.Segments).ThenInclude(s => s.Start)
            .SingleOrDefaultAsync(f => f.Name == "Sola Arena");
        if (facility is null)
        {
            facility = new() { Id = Guid.NewGuid(), Name = "Sola Arena" };
            await facilityContext.AddAsync(facility);
            await facilityContext.SaveChangesAsync();
        }
        if (sola.Facility != facility.Id)
        {
            sola.Facility = facility.Id;
            await timingContext.SaveChangesAsync();
        }
        if (facility.Layouts.Count == 0)
        {
            // One layout in use for all time with one unbounded revision, and each course point associated with its
            // timing point for all time (docs/replay-safe-statistics-plan.md §4.2).
            List<CoursePoint> coursePoints = sola.TimingPoints.Select(t => new CoursePoint { Id = Guid.NewGuid(), Name = t.Description }).ToList();
            foreach (var (point, timingPoint) in coursePoints.Zip(sola.TimingPoints))
                point.TimingAssociations.Add(new CoursePointTimingAssociation
                {
                    Id = Guid.NewGuid(), CoursePointId = point.Id, CoursePoint = point, TimingPointId = timingPoint.Id
                });
            var layout = new CourseLayout { Id = Guid.NewGuid(), FacilityId = facility.Id, Facility = facility };
            var revision = new CourseLayoutRevision { Id = Guid.NewGuid(), CourseLayoutId = layout.Id, CourseLayout = layout };
            CoursePoint Point(string name) => coursePoints.Single(c => c.Name == name);
            (string From, string To, double Length)[] segments =
            [
                ("Finish", "200m", 50), ("200m", "Green", 60), ("Green", "100m", 40), ("100m", "Red", 85), ("Red", "Finish", 15)
            ];
            for (var order = 0; order < segments.Length; order++)
                revision.Segments.Add(new CourseSegment
                {
                    Id = Guid.NewGuid(), Revision = revision, CourseLayoutRevisionId = revision.Id, Order = order,
                    Start = Point(segments[order].From), End = Point(segments[order].To), Length = segments[order].Length
                });
            layout.Revisions.Add(revision);
            layout.Assignments.Add(new FacilityLayoutAssignment
            {
                Id = Guid.NewGuid(), FacilityId = facility.Id, CourseLayoutId = layout.Id, CourseLayout = layout
            });
            facility.Layouts.Add(layout);
            await facilityContext.AddRangeAsync(coursePoints);
            await facilityContext.AddAsync(layout);
            await facilityContext.SaveChangesAsync();
        }

        IEnumerable<CoursePoint> coursepoints = facility.Layouts.First().Revisions.SelectMany(r => r.Segments).Select(s => s.Start)
            .DistinctBy(p => p.Id);

        var lap = await statisticsContext.Set<StatisticsItem>().SingleOrDefaultAsync(s => s.Name == "Lap");
        if (lap is null)
        {
            lap = new() { Name = "Lap", Description = "Lap",  Distance = 0, DistanceMode = StatisticsDistanceMode.CourseLayout};
            SimpleStatisticsItemConfig lapsola = new() { StatisticsItem = lap, CoursePointStart = coursepoints.Single(c => c.Name == "Finish").Id , CoursePointEnd = coursepoints.Single(c => c.Name == "Finish").Id };
            statisticsContext.Add(lapsola);
            await statisticsContext.SaveChangesAsync();
        }

        var s200m = await statisticsContext.Set<StatisticsItem>().SingleOrDefaultAsync(s => s.Name == "200m");
        if (s200m is null)
        {
            s200m = new() { Name = "200m", Description = "200m flying", Distance = 200 };
            SimpleStatisticsItemConfig s200msola = new() { StatisticsItem = s200m, CoursePointStart = coursepoints.Single(c => c.Name == "200m").Id, CoursePointEnd = coursepoints.Single(c => c.Name == "Finish").Id };
            statisticsContext.Add(s200msola);
            await statisticsContext.SaveChangesAsync();
        }

        var s250m = await statisticsContext.Set<StatisticsItem>().SingleOrDefaultAsync(s => s.Name == "250m");
        if (s250m is null)
        {
            s250m = new() { Name = "250m", Description = "Pursuit lap", Distance = 250 };
            SimpleStatisticsItemConfig s200red = new() { StatisticsItem = s250m, CoursePointStart = coursepoints.Single(c => c.Name == "Red").Id, CoursePointEnd = coursepoints.Single(c => c.Name == "Red").Id };
            SimpleStatisticsItemConfig s200green = new() { StatisticsItem = s250m, CoursePointStart = coursepoints.Single(c => c.Name == "Green").Id, CoursePointEnd = coursepoints.Single(c => c.Name == "Green").Id };
            statisticsContext.Add(s200red);
            statisticsContext.Add(s200green);
            await statisticsContext.SaveChangesAsync();
        }

        var pursuits = await statisticsContext.Set<SimpleStatisticsItemConfig>().Where(s => s.StatisticsItem == s250m).ToListAsync();
        
        var s500m = await statisticsContext.Set<StatisticsItem>().SingleOrDefaultAsync(s => s.Name == "500m");
        if (s500m is null)
        {
            s500m = new() { Name = "500m", Description = "500m", Distance = 500 };
            IEnumerable<MultiStatisticsItemConfig> s500msola = new List<MultiStatisticsItemConfig>
            {
                new() { StatisticsItem = s500m, ParentConfig = pursuits.ElementAt(0), Repetitions = 2 },
                new() { StatisticsItem = s500m, ParentConfig = pursuits.ElementAt(1), Repetitions = 2 },
            };
            await statisticsContext.AddRangeAsync(s500msola);
        }
        var s1000m = await statisticsContext.Set<StatisticsItem>().SingleOrDefaultAsync(s => s.Name == "1000m");
        if (s1000m is null)
        {
            s1000m = new() { Name = "1000m", Description = "1000m", Distance = 1000 };
            IEnumerable<MultiStatisticsItemConfig> s1000msola = new List<MultiStatisticsItemConfig>
            {
                new() { StatisticsItem = s1000m, ParentConfig = pursuits.ElementAt(0), Repetitions = 4 },
                new() { StatisticsItem = s1000m, ParentConfig = pursuits.ElementAt(1), Repetitions = 4 },
            };
            await statisticsContext.AddRangeAsync(s1000msola);
        }
        var s3000m = await statisticsContext.Set<StatisticsItem>().SingleOrDefaultAsync(s => s.Name == "3000m");
        if (s3000m is null)
        {
            s3000m = new() { Name = "3000m", Description = "3000m", Distance = 3000 };
            IEnumerable<MultiStatisticsItemConfig> s3000msola = new List<MultiStatisticsItemConfig>
            {
                new() { StatisticsItem = s3000m, ParentConfig = pursuits.ElementAt(0), Repetitions = 12 },
                new() { StatisticsItem = s3000m, ParentConfig = pursuits.ElementAt(1), Repetitions = 12 },
            };
            await statisticsContext.AddRangeAsync(s3000msola);
        }
        var s4000m = await statisticsContext.Set<StatisticsItem>().SingleOrDefaultAsync(s => s.Name == "4000m");
        if (s4000m is null)
        {
            s4000m = new() { Name = "4000m", Description = "4000m", Distance = 4000 };
            IEnumerable<MultiStatisticsItemConfig> s4000msola = new List<MultiStatisticsItemConfig>
            {
                new() { StatisticsItem = s4000m, ParentConfig = pursuits.ElementAt(0), Repetitions = 16 },
                new() { StatisticsItem = s4000m, ParentConfig = pursuits.ElementAt(1), Repetitions = 16 },
            };
            await statisticsContext.AddRangeAsync(s4000msola);
        }
        await statisticsContext.SaveChangesAsync();

        hostApplicationLifetime.StopApplication();
    }
}
