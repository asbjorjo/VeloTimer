using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Api.Test;

public sealed class TimingAssociationEndpointTests(FacilitiesApiFactory factory)
    : IClassFixture<FacilitiesApiFactory>, IAsyncLifetime
{
    private static readonly DateTimeOffset T = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public Task InitializeAsync() => factory.InitializeDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AssociationWrite_RejectsPostgresNegativeInfinitySentinelAsBadRequest()
    {
        var coursePointId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
            storage.Add(new CoursePoint { Id = coursePointId });
            await storage.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "timing-admin");
        var requests = new[]
        {
            new SetCoursePointTimingAssociationRequest
            {
                TimingPointId = null,
                ValidFromUtc = DateTimeOffset.MinValue,
                ValidToUtc = null
            },
            new SetCoursePointTimingAssociationRequest
            {
                TimingPointId = null,
                ValidFromUtc = null,
                ValidToUtc = new DateTimeOffset(1, 1, 1, 1, 0, 0, TimeSpan.FromHours(1))
            }
        };

        foreach (var request in requests)
        {
            using var response = await client.PutAsJsonAsync(
                $"/api/facilities/coursepoint/{coursePointId}/timing-association", request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(400, body.RootElement.GetProperty("status").GetInt32());
            Assert.Contains("minimum UTC instant", body.RootElement.GetProperty("detail").GetString());
        }

        Assert.Empty(factory.Events.Enqueued);
        var history = await client.GetFromJsonAsync<CoursePointTimingAssociationSnapshot[]>(
            $"/api/facilities/coursepoint/{coursePointId}/timing-associations");
        Assert.Empty(history!);
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    [InlineData("statistics-admin", HttpStatusCode.Forbidden)]
    public async Task Schedule_AuthorizesBeforeExistenceAndReturnsBodylessErrors(string role, HttpStatusCode status)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
        var existing = Guid.NewGuid();
        storage.Add(new CoursePoint { Id = existing });
        await storage.SaveChangesAsync();
        using var client = factory.CreateClient();
        if (role == "anonymous") client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        else client.DefaultRequestHeaders.Add("X-Test-Role", role);
        foreach (var id in new[] { existing, Guid.NewGuid() })
        {
            using var response = await client.GetAsync($"/api/facilities/coursepoint/{id}/timing-associations");
            Assert.Equal(status, response.StatusCode);
            Assert.Equal("", await response.Content.ReadAsStringAsync());
            using var write = await client.PutAsJsonAsync($"/api/facilities/coursepoint/{id}/timing-association",
                new SetCoursePointTimingAssociationRequest
                {
                    TimingPointId = null, ValidFromUtc = null, ValidToUtc = null
                });
            Assert.Equal(status, write.StatusCode);
            Assert.Equal("", await write.Content.ReadAsStringAsync());
        }
        Assert.Empty(factory.Events.Enqueued);
    }

    [Theory]
    [InlineData("timing-admin")]
    [InlineData("velotime-admin")]
    public async Task Schedule_ExistingEmptyIsArrayAndMissingIsBodyless404(string role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
        var point = new CoursePoint { Id = Guid.NewGuid() };
        storage.Add(point);
        await storage.SaveChangesAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        using var response = await client.GetAsync($"/api/facilities/coursepoint/{point.Id}/timing-associations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
        using var missing = await client.GetAsync($"/api/facilities/coursepoint/{Guid.NewGuid()}/timing-associations");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("", await missing.Content.ReadAsStringAsync());
    }

    [TimingAssociationPostgresFact]
    public async Task Schedule_ReturnsCompletePastCurrentFutureIntervalsWithRequiredUtcBounds()
    {
        var graph = await SeedAsync(T);
        using var client = factory.CreateClient();
        var history = (await client.GetFromJsonAsync<CoursePointTimingAssociationSnapshot[]>(
            $"/api/facilities/coursepoint/{graph.Start}/timing-associations"))!;
        Assert.Equal(
            [new(graph.OldTiming, graph.Start, null, T),
             new CoursePointTimingAssociationSnapshot(graph.CurrentTiming, graph.Start, T, T.AddYears(5)),
             new(graph.FutureTiming, graph.Start, T.AddYears(5), null)], history);
        using var response = await client.GetAsync($"/api/facilities/coursepoint/{graph.Start}/timing-associations");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var row in json.RootElement.EnumerateArray())
        {
            Assert.Equal(["coursePointId", "timingPointId", "validFromUtc", "validToUtc"],
                row.EnumerateObject().Select(p => p.Name).Order().ToArray());
        }
        Assert.All(history.SelectMany(row => new[] { row.ValidFromUtc, row.ValidToUtc }).OfType<DateTimeOffset>(),
            instant => Assert.Equal(TimeSpan.Zero, instant.Offset));
    }

    [TimingAssociationPostgresFact]
    public async Task AsOfReads_ExactHalfOpenBoundaryAndOffsetsPreserveNestedJsonShape()
    {
        var graph = await SeedAsync(T);
        using var client = factory.CreateClient();
        foreach (var (instant, timing, unmapped) in new[]
        {
            (T.AddTicks(-10), graph.OldTiming, graph.CurrentTiming),
            (T, graph.CurrentTiming, graph.OldTiming),
            (T.ToOffset(TimeSpan.FromHours(2)).AddTicks(9), graph.CurrentTiming, graph.OldTiming)
        })
        {
            var at = Uri.EscapeDataString(instant.ToString("O"));
            var pointRoute = $"/api/facilities/coursepoint/{graph.Start}?at={at}";
            await AssertPointAsync(client, pointRoute, graph.Start, timing);
            await AssertPointAsync(client, $"/api/facilities/timingpoint/{timing}?at={at}", graph.Start, timing);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/facilities/timingpoint/{unmapped}?at={at}")).StatusCode);
            var layout = (await client.GetFromJsonAsync<CourseLayoutDTO>($"/api/facilities/layouts/{graph.Layout}?at={at}"))!;
            Assert.Equal(timing, Assert.Single(layout.Segments).Start.TimingPointId);
            var layouts = (await client.GetFromJsonAsync<CourseLayoutDTO[]>(
                $"/api/facilities/facility/{graph.Facility}/layouts?at={at}"))!;
            Assert.Equal(timing, Assert.Single(Assert.Single(layouts).Segments).Start.TimingPointId);
        }
        // Default-now must not pick the latest (future scheduled) row.
        await AssertPointAsync(client, $"/api/facilities/coursepoint/{graph.Start}", graph.Start, graph.CurrentTiming);
        await AssertPointAsync(client, $"/api/facilities/timingpoint/{graph.CurrentTiming}", graph.Start, graph.CurrentTiming);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/facilities/timingpoint/{graph.FutureTiming}")).StatusCode);
        Assert.Empty(factory.OutputCache.Stored);
    }

    [TimingAssociationPostgresFact]
    public async Task CurrentReads_CrossScheduledBoundaryWithoutWritesOrCacheInvalidation()
    {
        factory.OutputCache.Reset();
        var boundary = DateTimeOffset.UtcNow.AddSeconds(3);
        var graph = await SeedAsync(boundary);
        using var client = factory.CreateClient();
        await AssertPointAsync(client, $"/api/facilities/coursepoint/{graph.Start}", graph.Start, graph.OldTiming);
        await AssertPointAsync(client, $"/api/facilities/timingpoint/{graph.OldTiming}", graph.Start, graph.OldTiming);
        using var layoutRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/facilities/layouts/{graph.Layout}");
        layoutRequest.Headers.Add("X-Test-Anonymous", "true");
        using var beforeLayout = await client.SendAsync(layoutRequest);
        Assert.Equal(HttpStatusCode.OK, beforeLayout.StatusCode);
        Assert.Equal(graph.OldTiming, Assert.Single((await beforeLayout.Content.ReadFromJsonAsync<CourseLayoutDTO>())!.Segments).Start.TimingPointId);
        Assert.True(DateTimeOffset.UtcNow < boundary, "Fixture setup missed the scheduled boundary.");
        await Task.Delay(boundary - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(50));
        await AssertPointAsync(client, $"/api/facilities/coursepoint/{graph.Start}", graph.Start, graph.CurrentTiming);
        await AssertPointAsync(client, $"/api/facilities/timingpoint/{graph.CurrentTiming}", graph.Start, graph.CurrentTiming);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/facilities/timingpoint/{graph.OldTiming}")).StatusCode);
        using var afterLayoutRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/facilities/layouts/{graph.Layout}");
        afterLayoutRequest.Headers.Add("X-Test-Anonymous", "true");
        using var afterLayout = await client.SendAsync(afterLayoutRequest);
        Assert.Equal(HttpStatusCode.OK, afterLayout.StatusCode);
        var layout = (await afterLayout.Content.ReadFromJsonAsync<CourseLayoutDTO>())!;
        Assert.Equal(graph.CurrentTiming, Assert.Single(layout.Segments).Start.TimingPointId);
        Assert.Empty(factory.OutputCache.Stored);
        Assert.Empty(factory.Events.Enqueued);
        Assert.Equal(0, factory.OutputCache.EvictionCalls);
    }

    [TimingAssociationPostgresFact]
    public async Task Distance_IndependentStartEndInstantsAndMixedPath422()
    {
        var graph = await SeedAsync(T);
        using var client = factory.CreateClient();
        var past = Uri.EscapeDataString(T.AddTicks(-10).ToString("O"));
        // Start only: the other instant independently defaults to now.
        using var startOnly = await client.GetAsync(
            $"/api/facilities/timingpoint/{graph.OldTiming}/distance/{graph.EndTiming}?startAt={past}");
        Assert.Equal(HttpStatusCode.OK, startOnly.StatusCode);
        var distance = (await startOnly.Content.ReadFromJsonAsync<CoursePointDistance>())!;
        Assert.Equal(new CoursePointDistance(graph.Start, graph.End, 125, graph.Facility), distance);
        // End only must not change the start's now default.
        using var endOnly = await client.GetAsync(
            $"/api/facilities/timingpoint/{graph.CurrentTiming}/distance/{graph.EndTiming}?endAt={past}");
        Assert.Equal(HttpStatusCode.OK, endOnly.StatusCode);
        Assert.Equal(distance, await endOnly.Content.ReadFromJsonAsync<CoursePointDistance>());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/facilities/timingpoint/{graph.OldTiming}/distance/{graph.EndTiming}?endAt={past}")).StatusCode);
        var other = await SeedAsync(T);
        using var mixed = await client.GetAsync(
            $"/api/facilities/timingpoint/{graph.CurrentTiming}/distance/{other.EndTiming}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, mixed.StatusCode);
        using var problem = JsonDocument.Parse(await mixed.Content.ReadAsStringAsync());
        Assert.Equal(422, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Empty(factory.OutputCache.Stored);
    }

    [TimingAssociationPostgresFact]
    public async Task ConflictEndpoint_PreviewHasNoWritesAndConfirmedRetryHasNoExtraEvent()
    {
        var graph = await SeedAsync(T);
        factory.References.FacilityId = graph.Facility;
        using var client = factory.CreateClient();
        var route = $"/api/facilities/coursepoint/{graph.End}/timing-association";
        var request = new SetCoursePointTimingAssociationRequest
        {
            TimingPointId = graph.CurrentTiming, ValidFromUtc = T, ValidToUtc = T.AddDays(1)
        };
        var before = await client.GetFromJsonAsync<CoursePointTimingAssociationSnapshot[]>(
            $"/api/facilities/coursepoint/{graph.End}/timing-associations");
        using var rejected = await client.PutAsJsonAsync(route, request);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        var preview = (await rejected.Content.ReadFromJsonAsync<TimingAssociationConflictPreview>())!;
        Assert.Equal("destination-occupied", preview.Code);
        Assert.Equal(T, Assert.Single(preview.Conflicts).OverlapValidFromUtc);
        Assert.Equal(T.AddDays(1), preview.Conflicts.Single().OverlapValidToUtc);
        Assert.Equal(before, await client.GetFromJsonAsync<CoursePointTimingAssociationSnapshot[]>(
            $"/api/facilities/coursepoint/{graph.End}/timing-associations"));
        Assert.Empty(factory.Events.Enqueued);
        using var accepted = await client.PutAsJsonAsync(route, request with { ConfirmationToken = preview.ConfirmationToken });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var result = (await accepted.Content.ReadFromJsonAsync<SetCoursePointTimingAssociationResponse>())!;
        Assert.Equal(result.ChangeId, Assert.Single(factory.Events.Enqueued).ChangeId);
        using var retry = await client.PutAsJsonAsync(route, request);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var noOp = (await retry.Content.ReadFromJsonAsync<SetCoursePointTimingAssociationResponse>())!;
        Assert.Null(noOp.ChangeId);
        Assert.Empty(noOp.Changes);
        Assert.Single(factory.Events.Enqueued);
    }

    [TimingAssociationPostgresFact]
    public async Task LegacyImmediateWrite_UsesNowAndRetainsPriorMappingInsteadOfUnboundedReplacement()
    {
        var graph = await SeedAsync(T);
        factory.References.FacilityId = graph.Facility;
        using var client = factory.CreateClient();
        var replacement = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow.AddTicks(-10);
        using var response = await client.PutAsJsonAsync($"/api/facilities/coursepoint/{graph.End}/timing-link",
            new SetCoursePointTimingLinkRequest(replacement));
        var after = DateTimeOffset.UtcNow;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var history = (await client.GetFromJsonAsync<CoursePointTimingAssociationSnapshot[]>(
            $"/api/facilities/coursepoint/{graph.End}/timing-associations"))!;
        Assert.Equal(2, history.Length);
        Assert.Equal(graph.EndTiming, history[0].TimingPointId);
        Assert.Null(history[0].ValidFromUtc);
        Assert.InRange(history[0].ValidToUtc!.Value, before, after);
        Assert.Equal(history[0].ValidToUtc, history[1].ValidFromUtc);
        Assert.Equal(replacement, history[1].TimingPointId);
        Assert.Null(history[1].ValidToUtc);
        var message = Assert.Single(factory.Events.Enqueued);
        var delta = Assert.Single(message.Changes);
        Assert.Equal(history[1].ValidFromUtc, delta.Before!.ValidFromUtc);
        Assert.Equal(new CoursePointTimingAssociationSnapshot(graph.EndTiming, graph.End,
            history[1].ValidFromUtc, null), delta.Before);
        Assert.Equal(history[1], delta.After);
    }

    [TimingAssociationPostgresFact]
    public async Task ExplicitAllTimeUnlink_ExistingCoursePointReturnsNullMappingAndTimingPoint404()
    {
        var graph = await SeedAsync(T);
        using var client = factory.CreateClient();
        using var response = await client.PutAsJsonAsync(
            $"/api/facilities/coursepoint/{graph.End}/timing-association",
            new SetCoursePointTimingAssociationRequest
            {
                TimingPointId = null, ValidFromUtc = null, ValidToUtc = null
            });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var point = await client.GetAsync($"/api/facilities/coursepoint/{graph.End}");
        Assert.Equal(HttpStatusCode.OK, point.StatusCode);
        using var json = JsonDocument.Parse(await point.Content.ReadAsStringAsync());
        Assert.Equal(["id", "name", "timingPointId"], json.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(graph.End, json.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("timingPointId").ValueKind);
        Assert.Equal("Finish", json.RootElement.GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/facilities/timingpoint/{graph.EndTiming}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<CoursePointTimingAssociationSnapshot[]>(
            $"/api/facilities/coursepoint/{graph.End}/timing-associations"))!);
        Assert.Equal(new CoursePointTimingAssociationChange(new(graph.EndTiming, graph.End, null, null), null),
            Assert.Single(Assert.Single(factory.Events.Enqueued).Changes));
    }

    [TimingAssociationPostgresFact]
    public async Task IntervalContract_RequiresBothNullableBoundsAndRejectsInvalidIntervalWithoutEvents()
    {
        var graph = await SeedAsync(T);
        using var client = factory.CreateClient();
        var route = $"/api/facilities/coursepoint/{graph.End}/timing-association";
        foreach (var body in new object[]
        {
            new { timingPointId = (Guid?)null, validFromUtc = (DateTimeOffset?)null },
            new { timingPointId = (Guid?)null, validToUtc = (DateTimeOffset?)null },
            new { timingPointId = (Guid?)null, validFromUtc = T, validToUtc = T },
            new { timingPointId = Guid.Empty, validFromUtc = (DateTimeOffset?)null, validToUtc = (DateTimeOffset?)null }
        })
        {
            using var response = await client.PutAsJsonAsync(route, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Empty(factory.Events.Enqueued);
        var history = (await client.GetFromJsonAsync<CoursePointTimingAssociationSnapshot[]>(
            $"/api/facilities/coursepoint/{graph.End}/timing-associations"))!;
        Assert.Equal(new CoursePointTimingAssociationSnapshot(graph.EndTiming, graph.End, null, null),
            Assert.Single(history));
    }

    private static async Task AssertPointAsync(HttpClient client, string route, Guid id, Guid timing)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Add("X-Test-Anonymous", "true"); // Exercise shared output-cache eligibility too.
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["id", "name", "timingPointId"], json.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(id, json.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(timing, json.RootElement.GetProperty("timingPointId").GetGuid());
        Assert.Equal("Start", json.RootElement.GetProperty("name").GetString());
    }

    private async Task<Graph> SeedAsync(DateTimeOffset boundary)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
        var start = new CoursePoint { Id = Guid.NewGuid(), Name = "Start" };
        var end = new CoursePoint { Id = Guid.NewGuid(), Name = "Finish" };
        var facility = new Facility { Id = Guid.NewGuid(), Name = "Test-only track" };
        var layout = new CourseLayout { Id = Guid.NewGuid(), Facility = facility,
            Segments = [new() { Id = Guid.NewGuid(), Start = start, End = end, Length = 125 }] };
        var graph = new Graph(facility.Id, layout.Id, start.Id, end.Id,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        storage.Add(layout);
        storage.AddRange(
            Association(start, graph.OldTiming, null, boundary),
            Association(start, graph.CurrentTiming, boundary, boundary.AddYears(5)),
            Association(start, graph.FutureTiming, boundary.AddYears(5), null),
            Association(end, graph.EndTiming, null, null));
        await storage.SaveChangesAsync();
        return graph;
    }

    private static CoursePointTimingAssociation Association(CoursePoint point, Guid timing,
        DateTimeOffset? from, DateTimeOffset? to) => new()
    {
        Id = Guid.NewGuid(), CoursePoint = point, TimingPointId = timing, ValidFromUtc = from, ValidToUtc = to
    };

    private sealed record Graph(Guid Facility, Guid Layout, Guid Start, Guid End,
        Guid OldTiming, Guid CurrentTiming, Guid FutureTiming, Guid EndTiming);
}
