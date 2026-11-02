using System.Net;
using System.Text.Json;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Services;
using Facilities = VeloTime.Module.Facilities.Client;
using Contracts = VeloTime.Module.Facilities.Interface.Data;
using Timing = VeloTime.Module.Timing.Client;

namespace VeloTime.Platform.Test;

public sealed class WebUiTimingAssociationClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IntervalWrite_MapsNullableBoundsAndConfirmationToGeneratedRequest(bool bounded)
    {
        var id = Guid.NewGuid();
        var start = DateTimeOffset.Parse("2026-11-02T12:00:00+00:00");
        var request = new Contracts.SetCoursePointTimingAssociationRequest
        {
            TimingPointId = bounded ? Guid.NewGuid() : null,
            ValidFromUtc = bounded ? start : null,
            ValidToUtc = bounded ? start.AddHours(1) : null,
            ConfirmationToken = bounded ? "exact-state-token" : null
        };
        using var handler = new ResponseHandler(HttpStatusCode.OK, """{"changeId":null,"changes":[]}""");
        using var http = new HttpClient(handler) { BaseAddress = new("http://facilities.test/") };
        using var host = new WebUiCacheTestHost();
        var service = Service(http, host);
        await service.SetCoursePointTimingAssociationAsync(id, request);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(request.TimingPointId, body.RootElement.GetProperty("timingPointId").Deserialize<Guid?>());
        Assert.Equal(request.ValidFromUtc, body.RootElement.GetProperty("validFromUtc").Deserialize<DateTimeOffset?>());
        Assert.Equal(request.ValidToUtc, body.RootElement.GetProperty("validToUtc").Deserialize<DateTimeOffset?>());
        Assert.Equal(request.ConfirmationToken, body.RootElement.TryGetProperty("confirmationToken", out var token)
            ? token.GetString() : null);
        Assert.EndsWith($"/coursepoint/{id}/timing-association", handler.RequestUri!.AbsolutePath);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ScheduleRead_CopiesGeneratedSnapshotsToSharedDtosAndRetainsUnboundedBounds()
    {
        var id = Guid.NewGuid();
        var timingPointId = Guid.NewGuid();
        var snapshot = new Contracts.CoursePointTimingAssociationSnapshot(timingPointId, id, null, null);
        using var handler = new ResponseHandler(HttpStatusCode.OK, JsonSerializer.Serialize(new[] { snapshot },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using var http = new HttpClient(handler) { BaseAddress = new("http://facilities.test/") };
        using var host = new WebUiCacheTestHost();
        var actual = Assert.Single(await Service(http, host).GetCoursePointTimingAssociationsAsync(id));
        Assert.Equal(snapshot, actual);
    }

    [Theory]
    [InlineData("destination-occupied")]
    [InlineData("confirmation-stale")]
    public async Task StructuredConflict_PropagatesExactPreviewWithoutRetry(string code)
    {
        var id = Guid.NewGuid();
        var timingPointId = Guid.NewGuid();
        var start = DateTimeOffset.Parse("2026-11-02T12:00:00+00:00");
        var request = new Contracts.SetCoursePointTimingAssociationRequest
        {
            TimingPointId = timingPointId, ValidFromUtc = start, ValidToUtc = start.AddHours(1)
        };
        var preview = new Contracts.TimingAssociationConflictPreview(code,
            new(id, timingPointId, request.ValidFromUtc, request.ValidToUtc),
            [new(new(timingPointId, Guid.NewGuid(), null, null), start, start.AddHours(1))], "preview-token");
        using var handler = new ResponseHandler(HttpStatusCode.Conflict, JsonSerializer.Serialize(preview,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using var http = new HttpClient(handler) { BaseAddress = new("http://facilities.test/") };
        using var host = new WebUiCacheTestHost();
        var exception = await Assert.ThrowsAsync<TimingAssociationConflictException>(() =>
            Service(http, host).SetCoursePointTimingAssociationAsync(id, request));
        Assert.Equal(code, exception.Preview.Code);
        Assert.Equal("preview-token", exception.Preview.ConfirmationToken);
        Assert.Equal(preview.Conflicts[0], Assert.Single(exception.Preview.Conflicts));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task StructuredConflict_AcceptsFacilitiesMicrosecondNormalizationForBothOffsetBounds()
    {
        var id = Guid.NewGuid();
        var timingPointId = Guid.NewGuid();
        var from = DateTimeOffset.Parse("2026-11-02T12:00:00.1234560Z");
        var to = from.AddHours(1);
        var request = new Contracts.SetCoursePointTimingAssociationRequest
        {
            TimingPointId = timingPointId,
            ValidFromUtc = from.ToOffset(TimeSpan.FromHours(2)).AddTicks(3),
            ValidToUtc = to.ToOffset(TimeSpan.FromHours(-3)).AddTicks(7)
        };
        var preview = new Contracts.TimingAssociationConflictPreview("destination-occupied",
            new(id, timingPointId, from, to), [], "normalized-token");
        using var handler = new ResponseHandler(HttpStatusCode.Conflict, JsonSerializer.Serialize(preview,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using var http = new HttpClient(handler) { BaseAddress = new("http://facilities.test/") };
        using var host = new WebUiCacheTestHost();

        var exception = await Assert.ThrowsAsync<TimingAssociationConflictException>(() =>
            Service(http, host).SetCoursePointTimingAssociationAsync(id, request));

        Assert.Equal("destination-occupied", exception.Preview.Code);
        Assert.Equal(id, exception.Preview.Requested.CoursePointId);
        Assert.Equal(timingPointId, exception.Preview.Requested.TimingPointId);
        Assert.Equal(from, exception.Preview.Requested.ValidFromUtc);
        Assert.Equal(to, exception.Preview.Requested.ValidToUtc);
        Assert.Equal("normalized-token", exception.Preview.ConfirmationToken);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task StructuredConflict_PreservesUnboundedNullBounds(bool nullFrom, bool nullTo)
    {
        var id = Guid.NewGuid();
        var timingPointId = Guid.NewGuid();
        var from = DateTimeOffset.Parse("2026-11-02T12:00:00.1234560Z");
        var to = from.AddHours(1);
        var request = new Contracts.SetCoursePointTimingAssociationRequest
        {
            TimingPointId = timingPointId,
            ValidFromUtc = nullFrom ? null : from.AddTicks(9),
            ValidToUtc = nullTo ? null : to.AddTicks(1)
        };
        var preview = new Contracts.TimingAssociationConflictPreview("confirmation-stale",
            new(id, timingPointId, nullFrom ? null : from, nullTo ? null : to), [], "nullable-token");
        using var handler = new ResponseHandler(HttpStatusCode.Conflict, JsonSerializer.Serialize(preview,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using var http = new HttpClient(handler) { BaseAddress = new("http://facilities.test/") };
        using var host = new WebUiCacheTestHost();

        var exception = await Assert.ThrowsAsync<TimingAssociationConflictException>(() =>
            Service(http, host).SetCoursePointTimingAssociationAsync(id, request));

        Assert.Equal(nullFrom ? null : from, exception.Preview.Requested.ValidFromUtc);
        Assert.Equal(nullTo ? null : to, exception.Preview.Requested.ValidToUtc);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task StructuredConflict_RejectsBoundsThatDifferBeyondFacilitiesNormalization(bool mismatchFrom, bool mismatchTo)
    {
        var id = Guid.NewGuid();
        var timingPointId = Guid.NewGuid();
        var from = DateTimeOffset.Parse("2026-11-02T12:00:00.1234560Z");
        var to = from.AddHours(1);
        var request = new Contracts.SetCoursePointTimingAssociationRequest
        {
            TimingPointId = timingPointId,
            ValidFromUtc = from.AddTicks(1),
            ValidToUtc = to.AddTicks(9)
        };
        var preview = new Contracts.TimingAssociationConflictPreview("destination-occupied",
            new(id, timingPointId, mismatchFrom ? from.AddTicks(-10) : from, mismatchTo ? to.AddTicks(10) : to),
            [], "mismatch-token");
        using var handler = new ResponseHandler(HttpStatusCode.Conflict, JsonSerializer.Serialize(preview,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using var http = new HttpClient(handler) { BaseAddress = new("http://facilities.test/") };
        using var host = new WebUiCacheTestHost();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Service(http, host).SetCoursePointTimingAssociationAsync(id, request));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("""{"detail":"Conflict without a preview."}""")]
    [InlineData("""{"code":"destination-occupied","confirmationToken":"token","requested":null,"conflicts":null}""")]
    public async Task MalformedConflict_IsExplicitFailureNeverSuccessOrRetry(string body)
    {
        using var handler = new ResponseHandler(HttpStatusCode.Conflict, body);
        using var http = new HttpClient(handler) { BaseAddress = new("http://facilities.test/") };
        using var host = new WebUiCacheTestHost();
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Service(http, host).SetCoursePointTimingAssociationAsync(Guid.NewGuid(),
                new() { TimingPointId = null, ValidFromUtc = null, ValidToUtc = null }));
        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    private static InfrastructureAdministrationService Service(HttpClient http, WebUiCacheTestHost host) =>
        new(new Facilities.FacilitiesClient(http),
            PlatformServiceTests.TestProxy.Create<Timing.ITimingClient>((_, _) => throw new NotSupportedException()), host.Cache);

    private sealed class ResponseHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        internal string? RequestBody { get; private set; }
        internal Uri? RequestUri { get; private set; }
        internal int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
