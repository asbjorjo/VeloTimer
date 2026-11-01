using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Routing;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;
using VeloTime.Module.Facilities.Interface.Data;

namespace VeloTime.Module.Facilities.Api.Test;

public sealed class FacilitiesEndpointTests(FacilitiesApiFactory factory) : IClassFixture<FacilitiesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task LayoutCommands_RejectDisconnectedAndCrossFacilityGraphsAndRetainSharedPoints()
    {
        using var client = factory.CreateClient();
        var facility = (await (await client.PostAsJsonAsync("/api/facilities/facility", new SaveFacilityRequest("First track"))).Content.ReadFromJsonAsync<FacilityDTO>())!;
        var secondFacility = (await (await client.PostAsJsonAsync("/api/facilities/facility", new SaveFacilityRequest("Second track"))).Content.ReadFromJsonAsync<FacilityDTO>())!;
        var start = new SaveCoursePointRequest(Guid.NewGuid(), "Start");
        var middle = new SaveCoursePointRequest(Guid.NewGuid(), "Middle");
        var finish = new SaveCoursePointRequest(Guid.NewGuid(), "Finish");
        var firstSegment = new SaveCourseSegmentRequest(Guid.NewGuid(), start.Id, middle.Id, 100);
        var secondSegment = new SaveCourseSegmentRequest(Guid.NewGuid(), middle.Id, finish.Id, 150);
        var request = new SaveCourseLayoutRequest([start, middle, finish], [firstSegment, secondSegment]);
        var created = await client.PostAsJsonAsync($"/api/facilities/facility/{facility.Id}/layouts", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var firstLayout = (await created.Content.ReadFromJsonAsync<CourseLayoutDTO>())!;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/facilities/facility/{facility.Id}/layouts/{firstLayout.Id}", request with { Segments = [secondSegment, firstSegment] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/facilities/facility/{secondFacility.Id}/layouts", request with
        { Segments = [firstSegment with { Id = Guid.NewGuid() }, secondSegment with { Id = Guid.NewGuid() }] })).StatusCode);
        var secondRequest = new SaveCourseLayoutRequest([start, finish], [new(Guid.NewGuid(), start.Id, finish.Id, 250)]);
        var secondResponse = await client.PostAsJsonAsync($"/api/facilities/facility/{facility.Id}/layouts", secondRequest);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var secondLayout = (await secondResponse.Content.ReadFromJsonAsync<CourseLayoutDTO>())!;
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/facilities/facility/{facility.Id}/layouts/{firstLayout.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/facilities/coursepoint/{start.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/facilities/coursepoint/{middle.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/facilities/facility/{facility.Id}/layouts/{secondLayout.Id}")).StatusCode);
    }

    [Fact]
    public async Task TimingLinkChanges_InvalidateRelationshipCaches()
    {
        var graph = await SeedFacilityAsync();
        factory.References.FacilityId = graph.Facility.Id;
        using var client = factory.CreateClient();
        var oldId = graph.Start.TimingPoint!.Value;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/facilities/timingpoint/{oldId}")).StatusCode);
        var newId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/facilities/coursepoint/{graph.Start.Id}/timing-link", new SetCoursePointTimingLinkRequest(newId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/facilities/timingpoint/{oldId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/facilities/timingpoint/{newId}")).StatusCode);
        Assert.Equal(newId, (await client.GetFromJsonAsync<CoursePointDTO>($"/api/facilities/coursepoint/{graph.Start.Id}"))!.TimingPointId);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/facilities/facility/{graph.Facility.Id}", new SaveFacilityRequest("Renamed facility"))).StatusCode);
        Assert.Equal("Renamed facility", (await client.GetFromJsonAsync<FacilityDTO>($"/api/facilities/coursepoint/{graph.Start.Id}/facility"))!.Name);
        Assert.True(factory.OutputCache.EvictionCalls >= 2);
    }

    [Fact]
    public async Task RelationshipOutputPolicy_CachesOnlySuccessfulResponsesForThirtySecondsOnAllFiveRoutes()
    {
        var graph = await SeedFacilityAsync();
        factory.OutputCache.Reset();
        using var client = factory.CreateClient();
        Assert.Same(factory.OutputCache, factory.Services.GetRequiredService<IOutputCacheStore>());
        var relationshipPolicyCount = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true)
            .Select(endpoint => endpoint.Metadata.FirstOrDefault(item => item.GetType().Name == "NamedPolicy"))
            .Count(policy => policy is not null);
        Assert.Equal(5, relationshipPolicyCount);
        var routes = new[]
        {
            $"/api/facilities/coursepoint/{graph.Start.Id}",
            $"/api/facilities/coursepoint/{graph.Start.Id}/facility",
            $"/api/facilities/coursepoint/{graph.Start.Id}/distance/{graph.End.Id}",
            $"/api/facilities/timingpoint/{graph.Start.TimingPoint}",
            $"/api/facilities/timingpoint/{graph.Start.TimingPoint}/distance/{graph.End.TimingPoint}"
        };

        var responseDetails = new List<string>();
        foreach (var route in routes)
        {
            var response = await GetAnonymouslyAsync(client, route);
            responseDetails.Add($"{route}: status={(int)response.StatusCode}; cache-control={response.Headers.CacheControl}; set-cookie={response.Headers.Contains("Set-Cookie")}; age={response.Headers.Age}");
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"GET {route} returned {(int)response.StatusCode}; cache-control={response.Headers.CacheControl}, set-cookie={response.Headers.Contains("Set-Cookie")}.");
        }

        var firstStoredResponses = factory.OutputCache.Stored;
        Assert.True(firstStoredResponses.Count == 5,
            $"Expected all five successful relationship GETs to be cached; store calls={factory.OutputCache.SetCalls}, hits/reads={factory.OutputCache.GetCalls}, tagged responses={firstStoredResponses.Count}; policy routes={relationshipPolicyCount}; responses: {string.Join(" | ", responseDetails)}.");
        Assert.All(firstStoredResponses, stored =>
        {
            Assert.Contains("Facilities.Relationships", stored.Tags);
            Assert.InRange(stored.ValidFor, TimeSpan.FromSeconds(29), TimeSpan.FromSeconds(30));
        });
        Assert.Equal(5, factory.OutputCache.SetCalls);

        foreach (var route in routes)
        {
            Assert.Equal(HttpStatusCode.OK, (await GetAnonymouslyAsync(client, route)).StatusCode);
        }
        Assert.Equal(5, factory.OutputCache.Stored.Count);
        Assert.Equal(10, factory.OutputCache.GetCalls);
        Assert.Equal(5, factory.OutputCache.SetCalls);

        factory.OutputCache.AdvanceBy(TimeSpan.FromSeconds(31));
        foreach (var route in routes)
        {
            Assert.Equal(HttpStatusCode.OK, (await GetAnonymouslyAsync(client, route)).StatusCode);
        }
        Assert.Equal(10, factory.OutputCache.Stored.Count);
        Assert.Equal(15, factory.OutputCache.GetCalls);
        Assert.Equal(10, factory.OutputCache.SetCalls);

        var countBeforeMiss = factory.OutputCache.Stored.Count;
        Assert.Equal(HttpStatusCode.NotFound,
            (await GetAnonymouslyAsync(client, $"/api/facilities/coursepoint/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(countBeforeMiss, factory.OutputCache.Stored.Count);

        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/facilities/facility", new SaveFacilityRequest("Not output cached"))).StatusCode);
        Assert.Equal(countBeforeMiss, factory.OutputCache.Stored.Count);
    }

    [Fact]
    public async Task LayoutCommands_PreserveIdsAndValidateTopologyAndLinks()
    {
        using var client = factory.CreateClient();
        var facilityResponse = await client.PostAsJsonAsync("/api/facilities/facility", new SaveFacilityRequest("Track"));
        var facility = (await facilityResponse.Content.ReadFromJsonAsync<FacilityDTO>())!;
        var start = new SaveCoursePointRequest(Guid.NewGuid(), "Start");
        var finish = new SaveCoursePointRequest(Guid.NewGuid(), "Finish");
        var segment = new SaveCourseSegmentRequest(Guid.NewGuid(), start.Id, finish.Id, 250);
        var request = new SaveCourseLayoutRequest([start, finish], [segment]);
        var created = await client.PostAsJsonAsync($"/api/facilities/facility/{facility.Id}/layouts", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var layout = (await created.Content.ReadFromJsonAsync<CourseLayoutDTO>())!;
        var route = $"/api/facilities/facility/{facility.Id}/layouts/{layout.Id}";
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(route, request with { Points = [start with { Name = "Renamed" }, finish] })).StatusCode);
        var updated = (await client.GetFromJsonAsync<CourseLayoutDTO>($"/api/facilities/layouts/{layout.Id}"))!;
        Assert.Equal(start.Id, Assert.Single(updated.Segments).Start.Id);
        Assert.Equal(segment.Id, updated.Segments.Single().Id);
        Assert.Equal("Renamed", updated.Segments.Single().Start.Name);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(route, request with { Segments = [segment with { Length = -1 }] })).StatusCode);

        using var timingAdmin = factory.CreateClient();
        timingAdmin.DefaultRequestHeaders.Add("X-Test-Role", "timing-admin");
        var timingPointId = Guid.NewGuid();
        factory.References.FacilityId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await timingAdmin.PutAsJsonAsync($"/api/facilities/coursepoint/{start.Id}/timing-link", new SetCoursePointTimingLinkRequest(timingPointId))).StatusCode);
        factory.References.FacilityId = facility.Id;
        Assert.Equal(HttpStatusCode.OK, (await timingAdmin.PutAsJsonAsync($"/api/facilities/coursepoint/{start.Id}/timing-link", new SetCoursePointTimingLinkRequest(timingPointId))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await timingAdmin.PutAsJsonAsync($"/api/facilities/coursepoint/{finish.Id}/timing-link", new SetCoursePointTimingLinkRequest(timingPointId))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await timingAdmin.PutAsJsonAsync(route, request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await timingAdmin.PutAsJsonAsync($"/api/facilities/coursepoint/{start.Id}/timing-link", new SetCoursePointTimingLinkRequest(null))).StatusCode);
        factory.References.Dependencies = 1;
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(route, request with { Segments = [segment with { Length = 300 }] })).StatusCode);
        factory.References.Dependencies = 0;
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/facilities/facility/{facility.Id}")).StatusCode);
    }

    [Fact]
    public async Task FacilityCommands_ManageEmptyFacilityAndBlockDependencies()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/facilities/facility", new SaveFacilityRequest(" "))).StatusCode);
        var created = await client.PostAsJsonAsync("/api/facilities/facility", new SaveFacilityRequest("New track"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var facility = (await created.Content.ReadFromJsonAsync<FacilityDTO>())!;
        Assert.Empty((await client.GetFromJsonAsync<List<CourseLayoutDTO>>($"/api/facilities/facility/{facility.Id}/layouts"))!);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/facilities/facility/{facility.Id}", new SaveFacilityRequest("Renamed track"))).StatusCode);
        factory.References.Dependencies = 1;
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/facilities/facility/{facility.Id}")).StatusCode);
        factory.References.Dependencies = 0;
        factory.References.Unavailable = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.DeleteAsync($"/api/facilities/facility/{facility.Id}")).StatusCode);
        factory.References.Unavailable = false;
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/facilities/facility/{facility.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("facilities-admin", HttpStatusCode.Created)]
    [InlineData("velotime-admin", HttpStatusCode.Created)]
    [InlineData("timing-admin", HttpStatusCode.Forbidden)]
    [InlineData("statistics-admin", HttpStatusCode.Forbidden)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    public async Task FacilityCommands_EnforceModuleRole(string role, HttpStatusCode status)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        Assert.Equal(status, (await client.PostAsJsonAsync("/api/facilities/facility", new SaveFacilityRequest("Track"))).StatusCode);
    }

    [Fact]
    public async Task FacilitiesEndpoints_ReturnExpectedDataAndMissingResourceStatuses()
    {
        using var client = factory.CreateClient();
        var graph = await SeedFacilityAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/facilities/facility")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/facilities/facility/{graph.Facility.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/facilities/facility/{graph.Facility.Id}/layouts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/facilities/layouts/{graph.Layout.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/facilities/coursepoint/{graph.Start.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/facilities/coursepoint/{graph.Start.Id}/facility")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/facilities/timingpoint/{graph.Start.TimingPoint}")).StatusCode);

        var courseDistanceResponse = await client.GetAsync($"/api/facilities/coursepoint/{graph.Start.Id}/distance/{graph.End.Id}");
        Assert.Equal(HttpStatusCode.OK, courseDistanceResponse.StatusCode);
        Assert.Equal(120, await courseDistanceResponse.Content.ReadFromJsonAsync<double>());

        var timingDistanceResponse = await client.GetAsync($"/api/facilities/timingpoint/{graph.Start.TimingPoint}/distance/{graph.End.TimingPoint}");
        Assert.Equal(HttpStatusCode.OK, timingDistanceResponse.StatusCode);

        var missingId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/facilities/facility/{missingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/facilities/facility/{missingId}/layouts")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/facilities/layouts/{missingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/facilities/coursepoint/{missingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/facilities/coursepoint/{missingId}/facility")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/facilities/coursepoint/{missingId}/distance/{missingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/facilities/timingpoint/{missingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/facilities/timingpoint/{missingId}/distance/{missingId}")).StatusCode);
    }

    private async Task<FacilityGraph> SeedFacilityAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
        var facility = new Facility { Id = Guid.NewGuid(), Name = "Test course" };
        var start = new CoursePoint { Id = Guid.NewGuid(), Name = "Start", TimingPoint = Guid.NewGuid() };
        var middle = new CoursePoint { Id = Guid.NewGuid(), Name = "Middle", TimingPoint = Guid.NewGuid() };
        var end = new CoursePoint { Id = Guid.NewGuid(), Name = "Finish", TimingPoint = Guid.NewGuid() };
        var layout = new CourseLayout { Id = Guid.NewGuid(), FacilityId = facility.Id, Facility = facility };
        layout.Segments.Add(new CourseSegment
        {
            Id = Guid.NewGuid(),
            CourseLayout = layout,
            CourseLayoutId = layout.Id,
            Order = 1,
            Start = start,
            End = middle,
            Length = 50
        });
        layout.Segments.Add(new CourseSegment
        {
            Id = Guid.NewGuid(),
            CourseLayout = layout,
            CourseLayoutId = layout.Id,
            Order = 2,
            Start = middle,
            End = end,
            Length = 70
        });
        facility.Layouts.Add(layout);
        storage.Add(facility);
        await storage.SaveChangesAsync();
        return new FacilityGraph(facility, layout, start, end);
    }

    private static async Task<HttpResponseMessage> GetAnonymouslyAsync(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Test-Anonymous", "true");
        return await client.SendAsync(request);
    }

    private sealed record FacilityGraph(Facility Facility, CourseLayout Layout, CoursePoint Start, CoursePoint End);
}
