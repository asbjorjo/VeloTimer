using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Service;
using VeloTime.Module.Facilities.Storage;
using ZiggyCreatures.Caching.Fusion;

namespace VeloTime.Module.Facilities.Test;

public class DistanceSnapshotTests
{
    [FacilitiesPostgresFact]
    public async Task Resolver_BackdatedCommitBetweenQueries_ReturnsOldPairThenNewPair()
    {
        await ExerciseAsync("resolver");
    }

    [FacilitiesPostgresFact]
    public async Task ExplicitService_BackdatedCommitBetweenQueries_UsesCoherentGeometry()
    {
        await ExerciseAsync("explicit");
    }

    [FacilitiesPostgresFact]
    public async Task LegacyService_BackdatedCommitBetweenQueries_UsesCoherentGeometry()
    {
        await ExerciseAsync("legacy");
    }

    private static async Task ExerciseAsync(string path)
    {
        await using var database = new FacilitiesAssociationDatabase();
        await database.InitializeAsync();
        await using var writer = database.CreateContext();
        var points = Enumerable.Range(0, 4).Select(_ => new CoursePoint { Id = Guid.NewGuid() }).ToArray();
        var timingStart = Guid.NewGuid();
        var timingEnd = Guid.NewGuid();
        var boundary = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var startAt = boundary.AddDays(1);
        var endAt = boundary.AddDays(2);
        points[0].TimingAssociations.Add(new() { TimingPointId = timingStart });
        points[1].TimingAssociations.Add(new() { TimingPointId = timingEnd });
        var layout = new CourseLayout { Id = Guid.NewGuid(), Facility = new() { Id = Guid.NewGuid() } };
        var lengths = new double[] { 10, 70, 20 };
        for (var i = 0; i < lengths.Length; i++)
            layout.Segments.Add(new()
            {
                Id = Guid.NewGuid(), CourseLayout = layout, Order = i,
                Start = points[i], End = points[i + 1], Length = lengths[i]
            });
        writer.Add(layout);
        await writer.SaveChangesAsync();
        writer.ChangeTracker.Clear();

        var interceptor = new CommitAfterFirstResolution(async () =>
        {
            await using var transaction = await writer.Database.BeginTransactionAsync();
            await writer.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE facility.course_point_timing_association SET valid_to_utc = {boundary}
                WHERE timing_point_id IN ({timingStart}, {timingEnd})
                """);
            writer.AddRange(
                new CoursePointTimingAssociation { CoursePointId = points[2].Id, TimingPointId = timingStart, ValidFromUtc = boundary },
                new CoursePointTimingAssociation { CoursePointId = points[3].Id, TimingPointId = timingEnd, ValidFromUtc = boundary });
            await writer.SaveChangesAsync();
            await transaction.CommitAsync();
        });
        await using var reader = new FacilityDbContext(new DbContextOptionsBuilder<FacilityDbContext>()
            .UseNpgsql(database.ConnectionString).UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptor).Options);
        var associations = new TimingAssociationService(reader);
        var services = new ServiceCollection();
        services.AddFusionCache().AsHybridCache();
        using var provider = services.BuildServiceProvider();
        var facilities = new FacilitiesService(reader, provider.GetRequiredService<HybridCache>());

        if (path == "resolver")
        {
            var old = await associations.ResolveDistanceEndpointsAsync(timingStart, timingEnd, startAt, endAt, default);
            Assert.Equal(points[0].Id, old.Start?.Id);
            Assert.Equal(points[1].Id, old.End?.Id);
            var updated = await associations.ResolveDistanceEndpointsAsync(timingStart, timingEnd, startAt, endAt, default);
            Assert.Equal(points[2].Id, updated.Start?.Id);
            Assert.Equal(points[3].Id, updated.End?.Id);
        }
        else
        {
            async Task<double> Distance() => path == "explicit"
                ? await facilities.DistanceBetweenTimingPoints(timingStart, timingEnd, startAt, endAt, associations)
                : await facilities.DistanceBetweenTimingPoints(timingStart, timingEnd);
            Assert.Equal(10, await Distance()); // Impossible mixed pair would be 100.
            Assert.Equal(20, await Distance()); // New resolved IDs must not reuse old geometry cache.
        }
        Assert.True(interceptor.CommittedBetweenQueries);
        Assert.True(interceptor.ResolutionQueries >= 4);
        Assert.Null(reader.Database.CurrentTransaction);
    }

    private sealed class CommitAfterFirstResolution(Func<Task> commit) : DbCommandInterceptor
    {
        public int ResolutionQueries { get; private set; }
        public bool CommittedBetweenQueries { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM facility.course_point_timing_association", StringComparison.Ordinal))
            {
                ResolutionQueries++;
                // PostgreSQL has established the SELECT snapshot, but the second SELECT has not run.
                if (ResolutionQueries == 1)
                {
                    await commit();
                    CommittedBetweenQueries = true;
                }
            }
            return result;
        }
    }
}
