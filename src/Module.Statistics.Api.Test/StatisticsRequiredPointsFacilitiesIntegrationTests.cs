extern alias FacilitiesApiTest;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;
using VeloTime.Module.Statistics.Api.Endpoints;
using VeloTime.Module.Statistics.Interface.Data;
using FacilitiesApiFactory = FacilitiesApiTest::VeloTime.Module.Facilities.Api.Test.FacilitiesApiFactory;

namespace VeloTime.Module.Statistics.Api.Test;

/// <summary>Exercise the production HTTP resolver against the real Facilities API routes.</summary>
public sealed class StatisticsRequiredPointsFacilitiesIntegrationTests : IAsyncLifetime
{
    private const string Configurations = "/api/statistics/admin/configurations";
    private const string FacilitiesRoot = "/api/facilities";
    private readonly HashSet<Guid> unavailableStarts = [];
    private readonly TestLayouts layouts = new();
    private readonly FacilitiesApiFactory facilities;
    private readonly StatisticsApiFactory statistics;
    private HttpClient statisticsClient = null!;
    private Guid itemId;

    public StatisticsRequiredPointsFacilitiesIntegrationTests()
    {
        facilities = new FacilitiesApiFactory
        {
            ConfigureTestServices = services => services.AddSingleton<IStartupFilter>(new DistanceFailureStartupFilter(unavailableStarts))
        };
        statistics = new StatisticsApiFactory
        {
            ConfigureAdditionalServices = services =>
            {
                foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(ICoursePointReferences) || descriptor.ServiceType == typeof(ICourseLayoutReferences)).ToArray())
                    services.Remove(descriptor);
                services.AddHttpClient(typeof(ICoursePointReferences).FullName!, client =>
                    client.BaseAddress = new Uri("http://facilities.integration"))
                    .ConfigurePrimaryHttpMessageHandler(() => facilities.Server.CreateHandler());
                services.AddTransient<ICoursePointReferences>(provider => (ICoursePointReferences)CreateFacilitiesResolver(provider, typeof(ICoursePointReferences)));
                services.AddSingleton<ICourseLayoutReferences>(layouts);
            }
        };
    }

    public async Task InitializeAsync()
    {
        await facilities.InitializeDatabaseAsync();
        await statistics.InitializeDatabaseAsync();
        statisticsClient = statistics.CreateClient();
        statisticsClient.DefaultRequestHeaders.Add("X-Test-Role", "statistics-admin");
        using var created = await statisticsClient.PostAsJsonAsync("/api/statistics/admin/items",
            new SaveStatisticsItemRequest("Required points integration", "", 250));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        itemId = (await created.Content.ReadFromJsonAsync<StatisticsItemDTO>())!.Id;
    }

    public async Task DisposeAsync()
    {
        statisticsClient.Dispose();
        await statistics.DisposeAsync();
        await facilities.DisposeAsync();
    }

    [Fact]
    public async Task DuplicateLayoutsReturnFacilities422AndEmptyRequirementsSkipResolverValidation()
    {
        var graph = await SeedAsync(["A", "B", "C"], [[0, 1, 2], [0, 1, 2]]);
        using var ambiguous = await GetDistanceAsync(graph.Points[0].Id, graph.Points[0].Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, ambiguous.StatusCode);
        await AssertProblemAsync(ambiguous, 422, "one compatible course layout");

        using var rejected = await PostConfigurationAsync(graph,
            new StatisticsRequiredCoursePoints(graph.LayoutIds[0], [graph.Points[1].Id]));
        Assert.True(rejected.StatusCode == HttpStatusCode.BadRequest, $"Statistics route returned {(int)rejected.StatusCode}: {await rejected.Content.ReadAsStringAsync()}");
        await AssertProblemAsync(rejected, 400, "unambiguous layout");

        // The same duplicate layouts must not block a configuration with no required points.
        using var empty = await PostConfigurationAsync(graph, new StatisticsRequiredCoursePoints(null, []));
        Assert.Equal(HttpStatusCode.Created, empty.StatusCode);
        var responseDto = await empty.Content.ReadFromJsonAsync<StatisticsConfigurationDTO>();
        Assert.Equal(itemId, responseDto!.StatisticsItemId);
        Assert.Null(responseDto.RequiredCoursePoints);
    }

    [Fact]
    public async Task UniqueWrappedLoopAcceptsRequiredAndOptionalObservationsAndThresholdOnlyUpdate()
    {
        // The configured itinerary wraps C -> D -> A -> B. D is an optional observation and A is required.
        var graph = await SeedAsync(["A", "B", "C", "D"], [[0, 1, 2, 3, 0]]);
        using var distance = await GetDistanceAsync(graph.Points[2].Id, graph.Points[2].Id);
        Assert.True(distance.StatusCode == HttpStatusCode.OK, $"Facilities route returned {(int)distance.StatusCode} {distance.Content.Headers.ContentType}: {await distance.Content.ReadAsStringAsync()}");
        Assert.Equal(100d, await distance.Content.ReadFromJsonAsync<double>());

        var request = new SaveStatisticsConfigurationRequest(itemId, StatisticsConfigurationType.Simple,
            graph.Points[2].Id, graph.Points[1].Id, null, null)
        {
            RequiredCoursePoints = new StatisticsRequiredCoursePoints(graph.LayoutIds[0], [graph.Points[0].Id])
        };
        using var created = await statisticsClient.PostAsJsonAsync(Configurations, request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdDto = await created.Content.ReadFromJsonAsync<StatisticsConfigurationDTO>();
        Assert.Equal([graph.Points[0].Id], createdDto!.RequiredCoursePoints!.CoursePointIds);
        Assert.Equal(graph.LayoutIds[0], createdDto.RequiredCoursePoints.CourseLayoutId);

        // Omitted requirements mean preserve; a threshold-only edit must remain valid and retain the path snapshot.
        var thresholdOnly = request with { RequiredCoursePoints = null, MinDuration = TimeSpan.FromSeconds(2) };
        using var updated = await statisticsClient.PutAsJsonAsync($"{Configurations}/{createdDto.Id}", thresholdOnly);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedDto = await updated.Content.ReadFromJsonAsync<StatisticsConfigurationDTO>();
        Assert.Equal(TimeSpan.FromSeconds(2), updatedDto!.MinDuration);
        Assert.Equal(graph.LayoutIds[0], updatedDto.RequiredCoursePoints!.CourseLayoutId);
        Assert.Equal([graph.Points[0].Id], updatedDto.RequiredCoursePoints.CoursePointIds);
    }

    [Fact]
    public async Task Facilities404And422BecomeNonresolvableWhile5xxIsPropagated()
    {
        var graph = await SeedAsync(["A", "B", "C"], [[0, 1, 2], [0, 1, 2]]);
        using var facilitiesClient = facilities.CreateClient();
        var missing = Guid.NewGuid();
        using var missingResponse = await facilitiesClient.GetAsync($"{FacilitiesRoot}/coursepoint/{missing}/distance/{missing}");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Equal("", await missingResponse.Content.ReadAsStringAsync());

        using var ambiguousResponse = await GetDistanceAsync(graph.Points[0].Id, graph.Points[0].Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, ambiguousResponse.StatusCode);
        await AssertProblemAsync(ambiguousResponse, 422, "one compatible course layout");

        await using var scope = statistics.Services.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ICoursePointReferences>();
        Assert.False(await resolver.CanResolveDistanceAsync(missing, missing, CancellationToken.None));
        Assert.False(await resolver.CanResolveDistanceAsync(graph.Points[0].Id, graph.Points[0].Id, CancellationToken.None));

        unavailableStarts.Add(graph.Points[0].Id);
        using var unavailable = await GetDistanceAsync(graph.Points[0].Id, graph.Points[0].Id);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        using var unavailableBody = JsonDocument.Parse(await unavailable.Content.ReadAsStringAsync());
        Assert.Equal(503, unavailableBody.RootElement.GetProperty("status").GetInt32());
        Assert.Contains("distance resolver unavailable", unavailableBody.RootElement.GetProperty("detail").GetString());
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => resolver.CanResolveDistanceAsync(
            graph.Points[0].Id, graph.Points[0].Id, CancellationToken.None));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
    }

    // CoursePointReferences is internal; instantiate the production adapter with a test-server HttpClient.
    private static object CreateFacilitiesResolver(IServiceProvider provider, Type resolverContract)
    {
        var implementation = resolverContract.Assembly.GetType("VeloTime.Module.Statistics.Api.Endpoints.CoursePointReferences", throwOnError: true)!;
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(resolverContract.FullName!);
        return ActivatorUtilities.CreateInstance(provider, implementation, client);
    }

    private async Task<HttpResponseMessage> PostConfigurationAsync(Graph graph, StatisticsRequiredCoursePoints required) =>
        await statisticsClient.PostAsJsonAsync(Configurations, new SaveStatisticsConfigurationRequest(itemId,
            StatisticsConfigurationType.Simple, graph.Points[0].Id, graph.Points[^1].Id, null, null)
        { RequiredCoursePoints = required });

    private async Task<HttpResponseMessage> GetDistanceAsync(Guid start, Guid end)
    {
        using var client = facilities.CreateClient();
        return await client.GetAsync($"{FacilitiesRoot}/coursepoint/{start}/distance/{end}");
    }

    private async Task<Graph> SeedAsync(string[] names, int[][] paths)
    {
        var facility = new Facility { Id = Guid.NewGuid(), Name = "Required-point test facility" };
        var points = names.Select(name => new CoursePoint { Id = Guid.NewGuid(), Name = name }).ToArray();
        var layouts = paths.Select(path =>
        {
            var layout = new CourseLayout { Id = Guid.NewGuid(), FacilityId = facility.Id, Facility = facility };
            for (var index = 0; index < path.Length - 1; index++)
                layout.EnsureUnboundedRevision().Segments.Add(new CourseSegment
                {
                    Id = Guid.NewGuid(),
                    Order = index + 1, Start = points[path[index]], End = points[path[index + 1]], Length = 25
                });
            facility.Layouts.Add(layout);
            return layout;
        }).ToArray();

        await using var scope = facilities.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
        storage.Add(facility);
        await storage.SaveChangesAsync();
        foreach (var layout in layouts)
            this.layouts.Set(new CourseLayoutDTO
            {
                Id = layout.Id, FacilityId = facility.Id,
                Segments = layout.Revisions.Single().Segments.OrderBy(segment => segment.Order).Select(segment => new SegmentDTO
                {
                    Id = segment.Id, Order = segment.Order, Length = segment.Length,
                    Start = new CoursePointDTO { Id = segment.Start.Id, Name = segment.Start.Name ?? string.Empty },
                    End = new CoursePointDTO { Id = segment.End.Id, Name = segment.End.Name ?? string.Empty }
                }).ToArray()
            });
        return new Graph(points, layouts.Select(layout => layout.Id).ToArray());
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, int status, string detail)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(status, body.RootElement.GetProperty("status").GetInt32());
        Assert.Contains(detail, body.RootElement.GetProperty("detail").GetString());
    }

    private sealed record Graph(CoursePoint[] Points, Guid[] LayoutIds);

    private sealed class TestLayouts : ICourseLayoutReferences
    {
        private readonly Dictionary<Guid, CourseLayoutDTO> values = [];
        public void Set(CourseLayoutDTO layout) => values[layout.Id] = layout;
        public Task<CourseLayoutDTO?> GetLayoutAsync(Guid layoutId, CancellationToken cancellationToken) =>
            Task.FromResult(values.GetValueOrDefault(layoutId));
    }


    private sealed class DistanceFailureStartupFilter(HashSet<Guid> unavailableStarts) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                var segments = context.Request.Path.Value?.Split("/", StringSplitOptions.RemoveEmptyEntries) ?? [];
                if (segments.Length == 6 && segments[0] == "api" && segments[1] == "facilities"
                    && segments[2] == "coursepoint" && segments[4] == "distance"
                    && Guid.TryParse(segments[3], out var start) && unavailableStarts.Contains(start))
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    context.Response.ContentType = "application/problem+json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        type = "about:blank", title = "Service Unavailable", status = 503,
                        detail = "distance resolver unavailable"
                    });
                    return;
                }
                await continuation();
            });
            next(app);
        };
    }
}
