using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Routing;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Interface.Messages;

namespace VeloTime.Module.Facilities.Api.Test;

public sealed class FacilitiesEndpointTests(FacilitiesApiFactory factory) : IClassFixture<FacilitiesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [TimingAssociationPostgresFact]
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
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/facilities/facility/{facility.Id}/layouts/{firstLayout.Id}/revision",
            Revision(request with { Segments = [secondSegment, firstSegment] }))).StatusCode);
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

    [TimingAssociationPostgresFact]
    public async Task TimingLinkChanges_InvalidateRelationshipCaches()
    {
        var graph = await SeedFacilityAsync();
        factory.References.FacilityId = graph.Facility.Id;
        using var client = factory.CreateClient();
        var oldId = graph.Start.TimingAssociations.Single().TimingPointId;
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

    [TimingAssociationPostgresFact]
    public async Task RelationshipOutputPolicy_CachesOnlyPointFacilityForThirtySecondsAndBypassesTimeDependentReads()
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
        // Distances depend on the instant (dated layout revisions), so only point → facility is output cached.
        Assert.Equal(1, relationshipPolicyCount);
        var routes = new[]
        {
            $"/api/facilities/coursepoint/{graph.Start.Id}",
            $"/api/facilities/coursepoint/{graph.Start.Id}/facility",
            $"/api/facilities/coursepoint/{graph.Start.Id}/distance/{graph.End.Id}",
            $"/api/facilities/timingpoint/{graph.Start.TimingAssociations.Single().TimingPointId}",
            $"/api/facilities/timingpoint/{graph.Start.TimingAssociations.Single().TimingPointId}/distance/{graph.End.TimingAssociations.Single().TimingPointId}"
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
        Assert.True(firstStoredResponses.Count == 1,
            $"Expected only the coursepoint facility to be cached; store calls={factory.OutputCache.SetCalls}, tagged responses={firstStoredResponses.Count}; responses: {string.Join(" | ", responseDetails)}.");
        Assert.All(firstStoredResponses, stored =>
        {
            Assert.Contains("Facilities.Relationships", stored.Tags);
            Assert.InRange(stored.ValidFor, TimeSpan.FromSeconds(29), TimeSpan.FromSeconds(30));
        });
        Assert.Equal(1, factory.OutputCache.SetCalls);

        foreach (var route in routes)
        {
            Assert.Equal(HttpStatusCode.OK, (await GetAnonymouslyAsync(client, route)).StatusCode);
        }
        Assert.Equal(1, factory.OutputCache.Stored.Count);
        Assert.Equal(2, factory.OutputCache.GetCalls);
        Assert.Equal(1, factory.OutputCache.SetCalls);

        factory.OutputCache.AdvanceBy(TimeSpan.FromSeconds(31));
        foreach (var route in routes)
        {
            Assert.Equal(HttpStatusCode.OK, (await GetAnonymouslyAsync(client, route)).StatusCode);
        }
        Assert.Equal(2, factory.OutputCache.Stored.Count);
        Assert.Equal(3, factory.OutputCache.GetCalls);
        Assert.Equal(2, factory.OutputCache.SetCalls);

        var countBeforeMiss = factory.OutputCache.Stored.Count;
        Assert.Equal(HttpStatusCode.NotFound,
            (await GetAnonymouslyAsync(client, $"/api/facilities/coursepoint/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(countBeforeMiss, factory.OutputCache.Stored.Count);

        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/facilities/facility", new SaveFacilityRequest("Not output cached"))).StatusCode);
        Assert.Equal(countBeforeMiss, factory.OutputCache.Stored.Count);
    }

    [TimingAssociationPostgresFact]
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
        var revisionRoute = $"{route}/revision";
        // Same geometry throughout: a rename is applied in place and keeps the revision and its segment IDs.
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(revisionRoute, Revision(request with { Points = [start with { Name = "Renamed" }, finish] }))).StatusCode);
        var updated = (await client.GetFromJsonAsync<CourseLayoutDTO>($"/api/facilities/layouts/{layout.Id}"))!;
        Assert.Equal(start.Id, Assert.Single(updated.Segments).Start.Id);
        Assert.Equal(layout.Segments.Single().Id, updated.Segments.Single().Id);
        Assert.Equal(layout.RevisionId, updated.RevisionId);
        Assert.Equal("Renamed", updated.Segments.Single().Start.Name);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(revisionRoute, Revision(request with { Segments = [segment with { Length = -1 }] }))).StatusCode);

        using var timingAdmin = factory.CreateClient();
        timingAdmin.DefaultRequestHeaders.Add("X-Test-Role", "timing-admin");
        var timingPointId = Guid.NewGuid();
        factory.References.FacilityId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await timingAdmin.PutAsJsonAsync($"/api/facilities/coursepoint/{start.Id}/timing-link", new SetCoursePointTimingLinkRequest(timingPointId))).StatusCode);
        factory.References.FacilityId = facility.Id;
        Assert.Equal(HttpStatusCode.OK, (await timingAdmin.PutAsJsonAsync($"/api/facilities/coursepoint/{start.Id}/timing-link", new SetCoursePointTimingLinkRequest(timingPointId))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await timingAdmin.PutAsJsonAsync($"/api/facilities/coursepoint/{finish.Id}/timing-link", new SetCoursePointTimingLinkRequest(timingPointId))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await timingAdmin.PutAsJsonAsync(revisionRoute, Revision(request))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await timingAdmin.PutAsJsonAsync($"/api/facilities/coursepoint/{start.Id}/timing-link", new SetCoursePointTimingLinkRequest(null))).StatusCode);
        factory.References.Dependencies = 1;
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(route)).StatusCode);
        // A geometry change over existing revisions needs confirmation; Statistics recomputes rather than blocking it.
        using var revised = await client.PutAsJsonAsync(revisionRoute, Revision(request with { Segments = [segment with { Length = 300 }] }));
        Assert.Equal(HttpStatusCode.Conflict, revised.StatusCode);
        Assert.Equal("revision-overlap", (await revised.Content.ReadFromJsonAsync<CourseLayoutRevisionConflictPreview>())!.Code);
        factory.References.Dependencies = 0;
        // Unlink closes the current mapping; it no longer destroys effective history.
        // Deletion must retain the layout/facility required to interpret that history.
        var history = (await client.GetFromJsonAsync<CoursePointTimingAssociationSnapshot[]>(
            $"/api/facilities/coursepoint/{start.Id}/timing-associations"))!;
        var prior = Assert.Single(history);
        Assert.Equal(timingPointId, prior.TimingPointId);
        Assert.NotNull(prior.ValidFromUtc);
        Assert.NotNull(prior.ValidToUtc);
        Assert.True(prior.ValidFromUtc < prior.ValidToUtc);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/facilities/facility/{facility.Id}")).StatusCode);
        Assert.Equal(history, await client.GetFromJsonAsync<CoursePointTimingAssociationSnapshot[]>(
            $"/api/facilities/coursepoint/{start.Id}/timing-associations"));
        Assert.Equal(layout.Segments.Single().Id, Assert.Single((await client.GetFromJsonAsync<CourseLayoutDTO>(
            $"/api/facilities/layouts/{layout.Id}"))!.Segments).Id);
    }

    [TimingAssociationPostgresFact]
    public async Task LayoutRevisionsAndAssignments_AreDatedAndPublishCourseConfigChanges()
    {
        using var client = factory.CreateClient();
        var facility = (await (await client.PostAsJsonAsync("/api/facilities/facility", new SaveFacilityRequest("Dated track"))).Content.ReadFromJsonAsync<FacilityDTO>())!;
        var start = new SaveCoursePointRequest(Guid.NewGuid(), "Start");
        var finish = new SaveCoursePointRequest(Guid.NewGuid(), "Finish");
        var request = new SaveCourseLayoutRequest([start, finish], [new(Guid.NewGuid(), start.Id, finish.Id, 250)]);
        var layout = (await (await client.PostAsJsonAsync($"/api/facilities/facility/{facility.Id}/layouts", request)).Content.ReadFromJsonAsync<CourseLayoutDTO>())!;
        var route = $"/api/facilities/facility/{facility.Id}/layouts/{layout.Id}";
        var changeAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var longer = Revision(request with { Segments = [new(Guid.NewGuid(), start.Id, finish.Id, 333.3)] }, changeAt);

        factory.Events.Enqueued.Clear();
        using var preview = await client.PutAsJsonAsync($"{route}/revision", longer);
        Assert.Equal(HttpStatusCode.Conflict, preview.StatusCode);
        var conflict = (await preview.Content.ReadFromJsonAsync<CourseLayoutRevisionConflictPreview>())!;
        var overlap = Assert.Single(conflict.Overlaps);
        Assert.Equal(changeAt, overlap.OverlapValidFromUtc);
        Assert.Null(overlap.OverlapValidToUtc);
        Assert.Empty(factory.Events.Enqueued);

        using var confirmed = await client.PutAsJsonAsync($"{route}/revision", longer with { ConfirmationToken = conflict.ConfirmationToken });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var revised = (await confirmed.Content.ReadFromJsonAsync<SetCourseLayoutRevisionResponse>())!;
        Assert.NotNull(revised.ChangeId);
        var revisionEvent = Assert.Single(factory.Events.Enqueued);
        Assert.Equal(CourseConfigChangeKind.LayoutRevision, revisionEvent.Kind);
        Assert.Equal(facility.Id, revisionEvent.FacilityId);
        Assert.Equal(layout.Id, revisionEvent.CourseLayoutId);
        Assert.Equal(changeAt, revisionEvent.AffectedFromUtc);
        Assert.Null(revisionEvent.AffectedToUtc);

        // The old geometry applies before the change, the new one from it.
        Assert.Equal(250, Assert.Single((await client.GetFromJsonAsync<CourseLayoutDTO>(
            $"/api/facilities/layouts/{layout.Id}?at={Uri.EscapeDataString(changeAt.AddTicks(-1).ToString("o"))}"))!.Segments).Length);
        Assert.Equal(333.3, Assert.Single((await client.GetFromJsonAsync<CourseLayoutDTO>(
            $"/api/facilities/layouts/{layout.Id}?at={Uri.EscapeDataString(changeAt.ToString("o"))}"))!.Segments).Length);

        factory.Events.Enqueued.Clear();
        var retiredAt = changeAt.AddDays(30);
        using var retired = await client.PutAsJsonAsync($"{route}/assignment",
            new SetFacilityLayoutAssignmentRequest { InUse = false, ValidFromUtc = retiredAt, ValidToUtc = null });
        Assert.Equal(HttpStatusCode.OK, retired.StatusCode);
        var assignment = (await retired.Content.ReadFromJsonAsync<SetFacilityLayoutAssignmentResponse>())!;
        var kept = Assert.Single(assignment.Assignments);
        Assert.Null(kept.ValidFromUtc);
        Assert.Equal(retiredAt, kept.ValidToUtc);
        var assignmentEvent = Assert.Single(factory.Events.Enqueued);
        Assert.Equal(CourseConfigChangeKind.LayoutAssignment, assignmentEvent.Kind);
        Assert.Equal(retiredAt, assignmentEvent.AffectedFromUtc);

        var history = (await client.GetFromJsonAsync<CourseLayoutHistoryDTO>($"{route}/history"))!;
        Assert.Equal(layout.Id, history.LayoutId);
        Assert.Equal(retiredAt, Assert.Single(history.Assignments).ValidToUtc);
        Assert.Collection(history.Revisions,
            before => { Assert.Null(before.ValidFromUtc); Assert.Equal(changeAt, before.ValidToUtc); Assert.Equal(250, Assert.Single(before.Segments).Length); },
            after => { Assert.Equal(changeAt, after.ValidFromUtc); Assert.Null(after.ValidToUtc); Assert.Equal(333.3, Assert.Single(after.Segments).Length); });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/facilities/facility/{facility.Id}/layouts/{Guid.NewGuid()}/history")).StatusCode);
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

    [TimingAssociationPostgresFact]
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
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/facilities/timingpoint/{graph.Start.TimingAssociations.Single().TimingPointId}")).StatusCode);

        var courseDistanceResponse = await client.GetAsync($"/api/facilities/coursepoint/{graph.Start.Id}/distance/{graph.End.Id}");
        Assert.Equal(HttpStatusCode.OK, courseDistanceResponse.StatusCode);
        Assert.Equal(120, await courseDistanceResponse.Content.ReadFromJsonAsync<double>());

        var timingDistanceResponse = await client.GetAsync($"/api/facilities/timingpoint/{graph.Start.TimingAssociations.Single().TimingPointId}/distance/{graph.End.TimingAssociations.Single().TimingPointId}");
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

    [TimingAssociationPostgresFact]
    public async Task TimingDistance_ClosedLoopReturnsLayoutAndFullLoopEvidenceTogether()
    {
        var graph = await SeedClosedFacilityAsync();
        using var client = factory.CreateClient();

        var distance = await client.GetFromJsonAsync<CoursePointDistance>(
            $"/api/facilities/timingpoint/{graph.StartTiming}/distance/{graph.MiddleTiming}");

        Assert.NotNull(distance);
        Assert.Equal(graph.Start.Id, distance.CoursePointStartId);
        Assert.Equal(graph.Middle.Id, distance.CoursePointEndId);
        Assert.Equal(125, distance.Distance);
        Assert.Equal(graph.Facility.Id, distance.FacilityId);
        Assert.Equal(graph.Layout.Id, distance.CourseLayoutId);
        Assert.Equal(600, distance.ClosedLoopDistanceMetres);
    }

    [TimingAssociationPostgresFact]
    public async Task TimingDistance_OpenEqualEndpointRetainsWholeLayoutDistanceWithoutLoopEvidence()
    {
        var graph = await SeedFacilityAsync();
        using var client = factory.CreateClient();
        var startTiming = graph.Start.TimingAssociations.Single().TimingPointId;

        var distance = await client.GetFromJsonAsync<CoursePointDistance>(
            $"/api/facilities/timingpoint/{startTiming}/distance/{startTiming}");

        Assert.NotNull(distance);
        Assert.Equal(graph.Start.Id, distance.CoursePointStartId);
        Assert.Equal(graph.Start.Id, distance.CoursePointEndId);
        Assert.Equal(120, distance.Distance);
        Assert.Null(distance.CourseLayoutId);
        Assert.Null(distance.ClosedLoopDistanceMetres);
    }

    private async Task<ClosedFacilityGraph> SeedClosedFacilityAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
        var facility = new Facility { Id = Guid.NewGuid(), Name = "Closed test course" };
        var start = Point("Start");
        var middle = Point("Middle");
        var finish = Point("Finish");
        var layout = new CourseLayout { Id = Guid.NewGuid(), FacilityId = facility.Id, Facility = facility };
        layout.EnsureUnboundedRevision().Segments.Add(new CourseSegment
        {
            Id = Guid.NewGuid(), Order = 1,
            Start = start, End = middle, Length = 125
        });
        layout.EnsureUnboundedRevision().Segments.Add(new CourseSegment
        {
            Id = Guid.NewGuid(), Order = 2,
            Start = middle, End = finish, Length = 200
        });
        layout.EnsureUnboundedRevision().Segments.Add(new CourseSegment
        {
            Id = Guid.NewGuid(), Order = 3,
            Start = finish, End = start, Length = 275
        });
        facility.Layouts.Add(layout);
        storage.Add(facility);
        await storage.SaveChangesAsync();
        return new ClosedFacilityGraph(facility, layout, start, middle,
            start.TimingAssociations.Single().TimingPointId, middle.TimingAssociations.Single().TimingPointId);

        static CoursePoint Point(string name) => new()
        {
            Id = Guid.NewGuid(), Name = name,
            TimingAssociations = [new() { Id = Guid.NewGuid(), TimingPointId = Guid.NewGuid() }]
        };
    }

    private async Task<FacilityGraph> SeedFacilityAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
        var facility = new Facility { Id = Guid.NewGuid(), Name = "Test course" };
        var start = new CoursePoint { Id = Guid.NewGuid(), Name = "Start",
            TimingAssociations = [new() { Id = Guid.NewGuid(), TimingPointId = Guid.NewGuid() }] };
        var middle = new CoursePoint { Id = Guid.NewGuid(), Name = "Middle",
            TimingAssociations = [new() { Id = Guid.NewGuid(), TimingPointId = Guid.NewGuid() }] };
        var end = new CoursePoint { Id = Guid.NewGuid(), Name = "Finish",
            TimingAssociations = [new() { Id = Guid.NewGuid(), TimingPointId = Guid.NewGuid() }] };
        var layout = new CourseLayout { Id = Guid.NewGuid(), FacilityId = facility.Id, Facility = facility };
        layout.EnsureUnboundedRevision().Segments.Add(new CourseSegment
        {
            Id = Guid.NewGuid(),
            Order = 1,
            Start = start,
            End = middle,
            Length = 50
        });
        layout.EnsureUnboundedRevision().Segments.Add(new CourseSegment
        {
            Id = Guid.NewGuid(),
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
    private sealed record ClosedFacilityGraph(Facility Facility, CourseLayout Layout, CoursePoint Start,
        CoursePoint Middle, Guid StartTiming, Guid MiddleTiming);

    private static SetCourseLayoutRevisionRequest Revision(SaveCourseLayoutRequest request, DateTimeOffset? from = null, DateTimeOffset? to = null) =>
        new() { Points = request.Points, Segments = request.Segments, ValidFromUtc = from, ValidToUtc = to };
}
