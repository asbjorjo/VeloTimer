using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;
using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Model;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed class StatisticsRequiredPointsPostgreSqlTests(ITestOutputHelper output)
{
    [PublicStatisticsPostgreSqlFact]
    public async Task CoursePointDependenciesCountPersistedArrayMembershipAndReflectClearing()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        using var client = host.App.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "facilities-admin");
        var requiredPoint = Guid.NewGuid();
        var absentPoint = Guid.NewGuid();
        var item = new StatisticsItem { Name = "Required points", Distance = 100 };
        var requiredOnly = new SimpleStatisticsItemConfig
        {
            StatisticsItem = item,
            CoursePointStart = PublicStatisticsTestHost.Start,
            CoursePointEnd = PublicStatisticsTestHost.End,
            RequiredCourseLayoutId = Guid.NewGuid(),
            RequiredCoursePointIds = [Guid.NewGuid(), requiredPoint, Guid.NewGuid()]
        };
        var endpointOnly = new SimpleStatisticsItemConfig
        {
            StatisticsItem = item, CoursePointStart = requiredPoint,
            CoursePointEnd = PublicStatisticsTestHost.End
        };
        var unrelated = new SimpleStatisticsItemConfig
        {
            StatisticsItem = item, CoursePointStart = PublicStatisticsTestHost.Start,
            CoursePointEnd = PublicStatisticsTestHost.End,
            RequiredCourseLayoutId = Guid.NewGuid(), RequiredCoursePointIds = [Guid.NewGuid()]
        };
        var multi = new MultiStatisticsItemConfig
        {
            StatisticsItem = item, ParentConfig = requiredOnly, Repetitions = 2
        };
        var sample = new Sample
        {
            TransponderId = Guid.NewGuid(), FacilityId = PublicStatisticsTestHost.Facility,
            CoursePointStartId = requiredPoint, CoursePointEndId = requiredPoint,
            TimeStart = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc),
            TimeEnd = new DateTime(2026, 11, 16, 0, 0, 10, DateTimeKind.Utc),
            Duration = TimeSpan.FromSeconds(10), Distance = 100, Speed = 10
        };
        await host.SeedAsync(requiredOnly, endpointOnly, unrelated, multi, sample);
        host.Sql.Commands.Clear();

        // The point occurs only inside the first configuration array, not at either endpoint.
        // Multi inherits it but must not be counted again; a sample with two matching ends counts once.
        var summary = await Read(requiredPoint);
        Assert.Equal(3, summary.Count);
        Assert.Contains("2 statistics configuration(s) and 1 recorded sample(s)", summary.Description);
        foreach (var command in host.Sql.Commands) output.WriteLine(command.Text);
        Assert.Contains(host.Sql.Commands, command =>
            command.Text.Contains("= ANY (s.required_course_point_ids)")
            && command.Parameters.Contains(requiredPoint));
        Assert.Equal(0, (await Read(absentPoint)).Count);

        // Mutate in a fresh context: the endpoint must read persisted membership, not a cached snapshot.
        await host.MutateAsync(async db =>
        {
            var config = await db.Set<SimpleStatisticsItemConfig>().SingleAsync(c => c.Id == requiredOnly.Id);
            config.RequiredCourseLayoutId = null;
            config.RequiredCoursePointIds = [];
        });
        Assert.Equal(2, (await Read(requiredPoint)).Count);
        await host.MutateAsync(async db =>
        {
            var config = await db.Set<SimpleStatisticsItemConfig>().SingleAsync(c => c.Id == requiredOnly.Id);
            config.RequiredCourseLayoutId = Guid.NewGuid();
            config.RequiredCoursePointIds = [requiredPoint];
        });
        Assert.Equal(3, (await Read(requiredPoint)).Count);

        async Task<DependencySummary> Read(Guid id) =>
            (await client.GetFromJsonAsync<DependencySummary>(
                $"/api/statistics/references/coursepoints/{id}/dependencies"))!;
    }
}
