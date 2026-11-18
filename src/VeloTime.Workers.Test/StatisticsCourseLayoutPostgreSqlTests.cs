using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests
{
    [PostgreSqlStatisticsFact]
    public async Task CourseLayoutLap_TwoWrapsAreRejectedWithoutPersistingAnEntry()
    {
        var rider = Guid.NewGuid();
        var finish = Guid.NewGuid();
        var at300 = Guid.NewGuid();
        var at200 = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var layout = Guid.NewGuid();
        var config = new SimpleStatisticsItemConfig
        {
            Id = Guid.NewGuid(), CoursePointStart = finish, CoursePointEnd = finish,
            StatisticsItem = new StatisticsItem { Id = Guid.NewGuid(), Name = "Lap", Distance = 0,
                DistanceMode = StatisticsDistanceMode.CourseLayout }
        };
        await using var db = Db();
        db.AddRange(config,
            Edge(finish, at300, 0, 10, 300),
            Edge(at300, at200, 10, 20, 300),
            Edge(at200, finish, 20, 30, 200));
        await db.SaveChangesAsync();
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new StatisticsService(db, services.GetRequiredService<HybridCache>()).ProcessCompletedAsync(
                rider, Start.AddSeconds(20), Start.AddSeconds(30), at200, finish));
        Assert.Contains("exactly one closed-loop distance", error.Message);
        await transaction.RollbackAsync();
        await using var verify = Db();
        Assert.Empty(await verify.Set<StatisticsEntry>().AsNoTracking().ToListAsync());
        Assert.Equal(new[] { 200d, 300d, 300d },
            (await verify.Set<Sample>().AsNoTracking().Select(s => s.Distance).ToListAsync()).Order().ToArray());
        Assert.All(await verify.Set<Sample>().AsNoTracking().ToListAsync(), sample =>
        {
            Assert.Equal(400, sample.ClosedLoopDistanceMetres);
            Assert.Equal(DateTimeKind.Utc, sample.TimeStart.Kind);
            Assert.Equal(DateTimeKind.Utc, sample.TimeEnd.Kind);
        });

        Sample Edge(Guid from, Guid to, int start, int end, double distance) => new()
        {
            Id = Guid.NewGuid(), TransponderId = rider, FacilityId = facility,
            CoursePointStartId = from, CoursePointEndId = to, CourseLayoutId = layout,
            ClosedLoopDistanceMetres = 400, Distance = distance,
            TimeStart = Start.AddSeconds(start), TimeEnd = Start.AddSeconds(end),
            Duration = TimeSpan.FromSeconds(end - start), Speed = distance / (end - start)
        };
    }
}
