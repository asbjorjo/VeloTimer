using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Api.Test;

public sealed class FacilitiesEndpointTests(FacilitiesApiFactory factory) : IClassFixture<FacilitiesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

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

    private sealed record FacilityGraph(Facility Facility, CourseLayout Layout, CoursePoint Start, CoursePoint End);
}
