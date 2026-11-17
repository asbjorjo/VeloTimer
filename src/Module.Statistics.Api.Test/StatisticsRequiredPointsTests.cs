using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Statistics.Api.Endpoints;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed class StatisticsRequiredPointsTests : IAsyncLifetime
{
    private readonly StatisticsApiFactory factory;
    private readonly LayoutReferences layouts = new();
    private readonly Guid[] points = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
    private HttpClient client = null!;
    private Guid itemId;
    private const string Root = "/api/statistics/admin/configurations";

    public StatisticsRequiredPointsTests()
    {
        factory = new StatisticsApiFactory { ConfigureAdditionalServices = services =>
        {
            services.AddSingleton<ICourseLayoutReferences>(layouts);
            services.AddSingleton<IStatisticsThresholdEnqueue, NoopEnqueue>();
        }};
    }

    public async Task InitializeAsync()
    {
        client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "statistics-admin");
        await factory.InitializeDatabaseAsync();
        SetLayout(points);
        var response = await client.PostAsJsonAsync("/api/statistics/admin/items", new SaveStatisticsItemRequest("Lap", "", 250));
        response.EnsureSuccessStatusCode();
        itemId = (await response.Content.ReadFromJsonAsync<StatisticsItemDTO>())!.Id;
    }

    public async Task DisposeAsync() { client.Dispose(); await factory.DisposeAsync(); }

    private void SetLayout(Guid[] path, bool closed = false)
    {
        var vertices = closed ? path.Append(path[0]).ToArray() : path;
        layouts.Layout = new CourseLayoutDTO
        {
            Id = layouts.Id, FacilityId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            // Deliberately return unsorted segments: segment Order, not transport order, controls validation.
            Segments = vertices.Zip(vertices.Skip(1)).Select((pair, index) => new SegmentDTO
            {
                Id = Guid.NewGuid(), Order = index + 1,
                Start = new CoursePointDTO { Id = pair.First, Name = "start" },
                End = new CoursePointDTO { Id = pair.Second, Name = "end" }
            }).Reverse().ToArray()
        };
    }

    private SaveStatisticsConfigurationRequest Request(params Guid[] required) =>
        new(itemId, StatisticsConfigurationType.Simple, points[0], points[^1], null, null)
        { RequiredCoursePoints = new(layouts.Id, required) };

    private async Task<StatisticsConfigurationDTO> Create(SaveStatisticsConfigurationRequest request)
    {
        var response = await client.PostAsJsonAsync(Root, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StatisticsConfigurationDTO>())!;
    }

    [Fact]
    public async Task RoundTrip_PreserveNullOmitted_AndExplicitClear_InvalidateLoadedDefinitions()
    {
        var request = Request(points[1], points[3]);
        var config = await Create(request);
        Assert.Equal(new[] { points[1], points[3] }, config.RequiredCoursePoints!.CoursePointIds);
        var listed = await client.GetFromJsonAsync<List<StatisticsConfigurationDTO>>(Root);
        Assert.Equal(config.RequiredCoursePoints.CoursePointIds, Assert.Single(listed!).RequiredCoursePoints!.CoursePointIds);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<StatisticsService>();
        Assert.Equal(new[] { points[1], points[3] }, Assert.Single(await service.GetStatisticsItemConfigByEndCoursePoint(points[^1])).RequiredCoursePointIds);
        var preserve = request with { RequiredCoursePoints = null };
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"{Root}/{config.Id}", preserve)).StatusCode);
        var raw = new { statisticsItemId = itemId, type = 0, coursePointStart = points[0], coursePointEnd = points[^1] };
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"{Root}/{config.Id}", raw)).StatusCode);
        var dependencies = await client.GetFromJsonAsync<VeloTime.Module.Common.DependencySummary>($"/api/statistics/references/coursepoints/{points[1]}/dependencies");
        Assert.Equal(1, dependencies!.Count);
        var clear = preserve with { RequiredCoursePoints = new(null, []) };
        var cleared = await client.PutAsJsonAsync($"{Root}/{config.Id}", clear);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null((await cleared.Content.ReadFromJsonAsync<StatisticsConfigurationDTO>())!.RequiredCoursePoints);
        await using var fresh = factory.Services.CreateAsyncScope();
        Assert.Empty(Assert.Single(await fresh.ServiceProvider.GetRequiredService<StatisticsService>()
            .GetStatisticsItemConfigByEndCoursePoint(points[^1])).RequiredCoursePointIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Results_BlockStructuralRequirements_ButAllowValueEqualAndThresholdUpdates(bool dependentMulti)
    {
        var request = Request(points[1], points[3]);
        var config = await Create(request);
        var resultConfigId = config.Id;
        if (dependentMulti)
            resultConfigId = (await Create(new(itemId, StatisticsConfigurationType.Multi, null, null, config.Id, 2))).Id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
            storage.Add(new StatisticsEntry
            {
                Id = Guid.NewGuid(), StatisticsItemId = itemId, StatisticsItemConfigId = resultConfigId,
                TransponderId = Guid.NewGuid(), FacilityId = layouts.Layout!.FacilityId,
                TimeStart = DateTime.UtcNow.AddMinutes(-1), TimeEnd = DateTime.UtcNow,
                Duration = TimeSpan.FromMinutes(1), Speed = 250d / 60
            });
            await storage.SaveChangesAsync();
        }
        foreach (var allowed in new[] { request, request with { RequiredCoursePoints = null, MinDuration = TimeSpan.FromSeconds(1) } })
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"{Root}/{config.Id}", allowed)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"{Root}/{config.Id}", request with { RequiredCoursePoints = new(null, []) })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"{Root}/{config.Id}", Request(points[2]))).StatusCode);
        layouts.Id = Guid.NewGuid(); SetLayout(points);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"{Root}/{config.Id}", Request(points[1], points[3]))).StatusCode);
    }

    [Fact]
    public async Task InvalidShapesAndMultiRequirements_AreRejectedExplicitly()
    {
        var request = Request(points[1]);
        StatisticsRequiredCoursePoints[] invalid = [new(null, [points[1]]), new(Guid.Empty, [points[1]]),
            new(layouts.Id, null), new(layouts.Id, []), new(layouts.Id, [Guid.Empty]),
            new(layouts.Id, [points[0]]), new(layouts.Id, [points[^1]]), new(layouts.Id, [points[1], points[1]]),
            new(layouts.Id, [points[3], points[1]]), new(layouts.Id, [Guid.NewGuid()])];
        foreach (var requirements in invalid)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root, request with { RequiredCoursePoints = requirements })).StatusCode);
        var parent = await Create(request);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root,
            new SaveStatisticsConfigurationRequest(itemId, StatisticsConfigurationType.Multi, null, null, parent.Id, 2)
            { RequiredCoursePoints = new(null, []) })).StatusCode);
    }

    [Fact]
    public async Task ClosedPath_AllowsOneWrapAndEqualEndpointsOneLap_RejectsReversedOrder()
    {
        SetLayout(points, closed: true);
        var request = Request(points[4], points[0]) with { CoursePointStart = points[3], CoursePointEnd = points[1] };
        await Create(request);
        await Create(Request(points[1], points[3], points[4]) with { CoursePointEnd = points[0] });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root,
            request with { RequiredCoursePoints = new(layouts.Id, [points[0], points[4]]) })).StatusCode);
    }

    [Fact]
    public async Task PreservedSnapshot_IsRevalidatedAndNeverReorderedOnSave()
    {
        var request = Request(points[1], points[3]);
        var config = await Create(request);
        SetLayout([points[0], points[3], points[2], points[1], points[4]]);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{Root}/{config.Id}", request with { RequiredCoursePoints = null })).StatusCode);
        var stored = Assert.Single((await client.GetFromJsonAsync<List<StatisticsConfigurationDTO>>(Root))!);
        Assert.Equal(new[] { points[1], points[3] }, stored.RequiredCoursePoints!.CoursePointIds);
        SetLayout(points);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{Root}/{config.Id}",
            request with { RequiredCoursePoints = null, CoursePointStart = points[2] })).StatusCode);
    }

    [Fact]
    public async Task InvalidOrMissingLayouts_AreRejected()
    {
        var request = Request(points[1]);
        layouts.Layout = null;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root, request)).StatusCode);
        SetLayout(points); layouts.Layout = new CourseLayoutDTO { Id = layouts.Id, FacilityId = Guid.NewGuid(), Segments = layouts.Layout!.Segments };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root, request)).StatusCode);
        SetLayout(points); layouts.Layout!.Segments.First().Order = layouts.Layout.Segments.Last().Order;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root, request)).StatusCode);
        SetLayout(points); layouts.Layout!.Segments.First().Start = new CoursePointDTO { Id = Guid.NewGuid(), Name = "disconnected" };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root, request)).StatusCode);
        SetLayout([points[0], points[1], points[2], points[1], points[4]]);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root, request)).StatusCode);
    }

    private sealed class LayoutReferences : ICourseLayoutReferences
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public CourseLayoutDTO? Layout { get; set; }
        public Task<CourseLayoutDTO?> GetLayoutAsync(Guid layoutId, CancellationToken cancellationToken) => Task.FromResult(Layout);
    }
    private sealed class NoopEnqueue : IStatisticsThresholdEnqueue
    {
        public Task EnqueueAsync(StatisticsValidityThresholdsChangedV1 message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
