using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Bootstrap;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Service;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Bootstrap;

public static class BootstrapExtension
{
    public static void VeloTimeBootstrap(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var timingContext = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
        var facilityContext = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
        var statisticsContext = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();

        var sola = timingContext.Set<Installation>()
            .Include(i => i.TimingPoints)
            .SingleOrDefault(i => i.AgentId == TimingData.Installation.AgentId);
        if (sola is null)
        {
            sola = new() { AgentId = TimingData.Installation.AgentId, TimingSystem = TimingSystem.MyLaps_X2 };
            foreach (var timingpoint in TimingData.timingPoints)
            {
                TimingPoint tp = new() { Description = timingpoint.Description, SystemId = timingpoint.SystemId, Installation = sola };
                sola.TimingPoints.Add(tp);
            }
            timingContext.Add(sola);
            timingContext.SaveChanges();
        }

        var facility = facilityContext.Set<Facility>()
            .Include(f => f.Layouts)
            .ThenInclude(l => l.Segments)
            .ThenInclude(s => s.Start)
            .SingleOrDefault(f => f.Name == "Sola Arena");
        if (facility is null)
        {
            List<CoursePoint> coursePoints = sola.TimingPoints.Select(t => new CoursePoint() { Name = t.Description, TimingPoint = t.Id }).ToList();
            List<CourseSegment> courseSegments = new List<CourseSegment>
            {
                new() { Start = coursePoints.Single(c => c.Name == "Finish"), End = coursePoints.Single(c => c.Name == "200m"), Order = 0, Length = 50},
                new() { Start = coursePoints.Single(c => c.Name == "200m"), End = coursePoints.Single(c => c.Name == "Green"), Order = 1, Length = 60},
                new() { Start = coursePoints.Single(c => c.Name == "Green"), End = coursePoints.Single(c => c.Name == "100m"), Order = 2, Length = 40},
                new() { Start = coursePoints.Single(c => c.Name == "100m"), End = coursePoints.Single(c => c.Name == "Red"), Order = 3, Length = 85},
                new() { Start = coursePoints.Single(c => c.Name == "Red"), End = coursePoints.Single(c => c.Name == "Finish"), Order = 4, Length = 15},
            };
            List<CourseLayout> layouts = new() { new() { Segments = courseSegments } };

            facility = new() { Name = "Sola Arena", Layouts = layouts };

            facilityContext.Add(facility);
            facilityContext.SaveChanges();
            sola.Facility = facility.Id;
            timingContext.SaveChanges();
        }

        IEnumerable<CoursePoint> coursepoints = facility.Layouts.First().Segments.Select(s => s.Start);

        var lap = statisticsContext.Set<StatisticsItem>().SingleOrDefault(s => s.Name == "Lap");
        if (lap is null)
        {
            lap = new() { Name = "Lap", Description = "Lap", Distance = 250 };
            SimpleStatisticsItem lapsola = new() { StatisticsItem = lap, CoursePointStart = coursepoints.Single(c => c.Name == "Finish").Id, CoursePointEnd = coursepoints.Single(c => c.Name == "Finish").Id };
            statisticsContext.Add(lapsola);
            statisticsContext.SaveChanges();
        }

        var s200m = statisticsContext.Set<StatisticsItem>().SingleOrDefault(s => s.Name == "200m");
        if (s200m is null)
        {
            s200m = new() { Name = "200m", Description = "200m flying", Distance = 200 };
            SimpleStatisticsItem s200msola = new() { StatisticsItem = s200m, CoursePointStart = coursepoints.Single(c => c.Name == "200m").Id, CoursePointEnd = coursepoints.Single(c => c.Name == "Finish").Id };
            statisticsContext.Add(s200msola);
            statisticsContext.SaveChanges();
        }

        var s250m = statisticsContext.Set<StatisticsItem>().SingleOrDefault(s => s.Name == "250m");
        if (s250m is null)
        {
            s250m = new() { Name = "250m", Description = "Pursuit lap", Distance = 250 };
            SimpleStatisticsItem s200red = new() { StatisticsItem = s250m, CoursePointStart = coursepoints.Single(c => c.Name == "Red").Id, CoursePointEnd = coursepoints.Single(c => c.Name == "Red").Id };
            SimpleStatisticsItem s200green = new() { StatisticsItem = s250m, CoursePointStart = coursepoints.Single(c => c.Name == "Green").Id, CoursePointEnd = coursepoints.Single(c => c.Name == "Green").Id };
            statisticsContext.Add(s200red);
            statisticsContext.Add(s200green);
            statisticsContext.SaveChanges();
        }
    }
}
