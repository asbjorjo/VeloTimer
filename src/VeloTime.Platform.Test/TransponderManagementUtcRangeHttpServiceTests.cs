using System.Net;
using System.Net.Http.Json;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.Platform.Test;

public sealed class TransponderManagementUtcRangeHttpServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetTranspondersAsync_RejectsNonZeroOffsetRangeBoundsBeforeSending(bool invalidStart)
    {
        var start = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
        var range = invalidStart
            ? new UtcInstantRange(start.ToOffset(TimeSpan.FromHours(2)), end)
            : new UtcInstantRange(start, end.ToOffset(TimeSpan.FromHours(-5)));
        var filters = new TransponderColumnFilters { LastSeenUtcRange = range };
        var handler = new CapturingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://build.invalid/") };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            new TransponderManagementHttpService(client).GetTranspondersAsync(false, filters: filters));

        Assert.Equal("range", exception.ParamName);
        Assert.Contains("lastSeen", exception.Message);
        Assert.Equal(0, handler.SendCount);
    }

    [Fact]
    public async Task GetTranspondersAsync_SerializesZeroOffsetRangesWithExactPrecision()
    {
        var filters = new TransponderColumnFilters
        {
            LastSeenUtcRange = new(
                new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero).AddTicks(1_234_567),
                new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero).AddTicks(7_654_321)),
            OwnedFromUtcRange = new(
                new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(1),
                new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero).AddTicks(9_876_543)),
            OwnedToUtcRange = new(
                new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(5_432_109),
                new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero).AddTicks(1_000_001))
        };
        var handler = new CapturingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://build.invalid/") };

        await new TransponderManagementHttpService(client).GetTranspondersAsync(false, filters: filters);

        Assert.Equal(1, handler.SendCount);
        Assert.NotNull(handler.Request);
        Assert.Equal("/api/transponders", handler.Request.RequestUri!.AbsolutePath);
        Assert.Equal(
            "?page=0&pageSize=10&search=&includeHistory=True" +
            "&lastSeenUtcStart=2026-03-01T10%3A00%3A00.1234567%2B00%3A00" +
            "&lastSeenUtcEnd=2026-03-02T10%3A00%3A00.7654321%2B00%3A00" +
            "&ownedFromUtcStart=2026-04-01T00%3A00%3A00.0000001%2B00%3A00" +
            "&ownedFromUtcEnd=2026-04-02T00%3A00%3A00.9876543%2B00%3A00" +
            "&ownedToUtcStart=2026-05-01T00%3A00%3A00.5432109%2B00%3A00" +
            "&ownedToUtcEnd=2026-05-02T00%3A00%3A00.1000001%2B00%3A00" +
            "&sortDescending=False",
            handler.Request.RequestUri.Query);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SendCount++;
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new TransponderPage([], 0))
            });
        }
    }
}
