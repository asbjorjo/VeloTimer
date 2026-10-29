using System.Net;
using System.Net.Http.Json;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;
using VeloTime.WebUI.Mud.Services;
using Contracts = VeloTime.Module.Timing.Interface.Data;
using Timing = VeloTime.Module.Timing.Client;

namespace VeloTime.Platform.Test;

public sealed class TransponderPaginationServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GeneratedClient_SerializesPagingAndDeserializesRowEnvelope(bool administration)
    {
        var id = Guid.NewGuid();
        using var handler = new PageResponseHandler(administration, id);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://timing/") };
        var service = new TransponderManagementService(new Timing.TimingClient(http), NoContentClientCallTests.UnusedOwners());
        var result = await service.GetTranspondersAsync(administration, 2, 25, "", false);
        Assert.Equal(80, result.TotalCount);
        var row = Assert.Single(result.Items);
        Assert.Equal(id, row.Transponder.Id);
        Assert.Equal("Generated client row", row.Transponder.Label);
        Assert.Equal(id, row.Ownership!.OwnerId);
        Assert.Equal(handler.LastSeen, row.Transponder.LastSeen);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmptySearch_RequestsOnlyOneBoundedPage(bool administration)
    {
        var id = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var timing = PlatformServiceTests.TestProxy.Create<Timing.ITimingClient>((method, args) =>
        {
            Assert.Equal(administration ? "QueryTranspondersAsync" : "QueryMyTranspondersAsync", method.Name);
            var request = Assert.IsType<Timing.TransponderPageRequest>(args[0]);
            Assert.Equal(3, request.Page);
            Assert.Equal(25, request.PageSize);
            Assert.False(request.IncludeHistory);
            Assert.Null(request.MatchingTransponderIds);
            Assert.Equal(cancellation.Token, args[1]);
            return new Timing.TransponderPage
            {
                TotalCount = 101,
                Items = [new() { Transponder = new() { Id = id, Label = "Label", System = "MyLaps_X2", SystemId = "106187718", Ownerships = [] } }]
            };
        });
        var service = new TransponderManagementService(timing, NoContentClientCallTests.UnusedOwners());
        var result = await service.GetTranspondersAsync(administration, 3, 25, "", false, cancellation.Token);
        Assert.Equal(101, result.TotalCount);
        Assert.Equal(id, Assert.Single(result.Items).Transponder.Id);
        Assert.Single(((PlatformServiceTests.TestProxy)(object)timing).Calls);
    }

    [Theory]
    [InlineData("q")]
    [InlineData("no statistics profile")]
    [InlineData("user unavailable")]
    [InlineData("24422 106187718")]
    [InlineData(") No statistics")]
    public async Task OwnerSearch_StreamsCandidatesAndMatchesFullDisplayStringBeforePaging(string search)
    {
        var matchId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        var candidatesRead = new List<int>();
        var resolved = new List<Guid>();
        var timing = PlatformServiceTests.TestProxy.Create<Timing.ITimingClient>((method, args) =>
        {
            if (method.Name == "LookupTranspondersAsync")
            {
                var page = Assert.IsType<int>(args[0]);
                Assert.Equal(100, args[1]);
                Assert.Equal("", args[2]);
                Assert.Equal(true, args[3]);
                candidatesRead.Add(page);
                return new Timing.TransponderLookupPage
                {
                    TotalCount = 101,
                    Items = page == 0
                        ? Enumerable.Range(0, 100).Select(_ => new Timing.TransponderLookupItem
                        {
                            Id = Guid.NewGuid(), Label = "Unassigned", SystemId = "123", OwnerIds = []
                        }).ToList()
                        : [new() { Id = matchId, Label = "HT-24422", SystemId = "106187718", OwnerIds = [ownerId, otherOwner] }]
                };
            }
            Assert.Equal("QueryTranspondersAsync", method.Name);
            var request = Assert.IsType<Timing.TransponderPageRequest>(args[0]);
            Assert.Equal(matchId, Assert.Single(request.MatchingTransponderIds));
            Assert.Equal(2, request.Page);
            Assert.Equal(10, request.PageSize);
            Assert.False(request.IncludeHistory);
            return new Timing.TransponderPage { TotalCount = 30, Items = [] };
        });
        var owners = PlatformServiceTests.TestProxy.Create<ITransponderOwnerService>((method, args) =>
        {
            Assert.Equal(nameof(ITransponderOwnerService.ResolveAsync), method.Name);
            var ids = Assert.IsAssignableFrom<IReadOnlyList<Guid>>(args[0]);
            Assert.InRange(ids.Count, 1, 100);
            resolved.AddRange(ids);
            return (IReadOnlyList<TransponderOwnerView>)ids.Select(id =>
                new TransponderOwnerView(id, null, id == ownerId ? "Q" : null, null, id == ownerId)).ToList();
        });
        var service = new TransponderManagementService(timing, owners);
        Assert.Equal(30, (await service.GetTranspondersAsync(true, 2, 10, search, false)).TotalCount);
        Assert.Equal(new[] { 0, 1 }, candidatesRead);
        Assert.Equal(search == "24422 106187718" ? Array.Empty<Guid>() : new[] { ownerId, otherOwner }, resolved);
    }

    [Fact]
    public async Task PersonalSearch_OnlyUsesOwnNameAndPreservesNoMatches()
    {
        var id = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var timing = PlatformServiceTests.TestProxy.Create<Timing.ITimingClient>((method, args) =>
        {
            if (method.Name == "LookupMyTranspondersAsync")
                return new Timing.TransponderLookupPage
                {
                    TotalCount = 1, Items = [new() { Id = id, Label = "Code", SystemId = "123", OwnerIds = [ownerId] }]
                };
            Assert.Equal("QueryMyTranspondersAsync", method.Name);
            var request = Assert.IsType<Timing.TransponderPageRequest>(args[0]);
            Assert.Empty(request.MatchingTransponderIds);
            return new Timing.TransponderPage { TotalCount = 0, Items = [] };
        });
        var owners = PlatformServiceTests.TestProxy.Create<ITransponderOwnerService>((method, _) =>
        {
            Assert.Equal(nameof(ITransponderOwnerService.GetOwnProfileAsync), method.Name);
            return new OwnTransponderProfile(ownerId, "Own name");
        });
        Assert.Empty((await new TransponderManagementService(timing, owners).GetTranspondersAsync(false, search: "another owner")).Items);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task InvalidPagination_IsRejectedBeforeAnyUpstreamCall(int page, int pageSize)
    {
        var timing = PlatformServiceTests.TestProxy.Create<Timing.ITimingClient>((method, _) => throw new NotSupportedException(method.Name));
        var service = new TransponderManagementService(timing, NoContentClientCallTests.UnusedOwners());
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetTranspondersAsync(true, page, pageSize, "name"));
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Empty(((PlatformServiceTests.TestProxy)(object)timing).Calls);
    }

    [Fact]
    public async Task SearchDependencyFailure_DoesNotReturnAnEmptySuccessPage()
    {
        var timing = PlatformServiceTests.TestProxy.Create<Timing.ITimingClient>((_, _) => new Timing.TransponderLookupPage
        {
            TotalCount = 1, Items = [new() { Id = Guid.NewGuid(), Label = "Code", SystemId = "123", OwnerIds = [Guid.NewGuid()] }]
        });
        var owners = PlatformServiceTests.TestProxy.Create<ITransponderOwnerService>((_, _) =>
            throw new HttpRequestException("Owner lookup unavailable.", null, HttpStatusCode.ServiceUnavailable));
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new TransponderManagementService(timing, owners).GetTranspondersAsync(true, search: "name"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Single(((PlatformServiceTests.TestProxy)(object)timing).Calls);
    }

    private sealed class PageResponseHandler(bool administration, Guid id) : HttpMessageHandler
    {
        public DateTimeOffset LastSeen { get; } = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(administration ? "/api/timing/transponders/query" : "/api/timing/my/transponders/query", request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadFromJsonAsync<Contracts.TransponderPageRequest>(cancellationToken);
            Assert.NotNull(body);
            Assert.Equal(2, body.Page);
            Assert.Equal(25, body.PageSize);
            Assert.False(body.IncludeHistory);
            Assert.Null(body.MatchingTransponderIds);
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new Contracts.TransponderPage(
                    [new(new(id, "MyLaps_X2", "106187718", "Generated client row", [], LastSeen),
                        new(Guid.NewGuid(), id, id, LastSeen.AddDays(-1), null))], 80))
            };
        }
    }
}

public sealed partial class WebUiBffTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TransponderPage_ForwardsAllBrowserQueryValues(bool administration)
    {
        var backend = PlatformServiceTests.TestProxy.Create<ITransponderManagementService>((method, args) =>
        {
            Assert.Equal(nameof(ITransponderManagementService.GetTranspondersAsync), method.Name);
            Assert.Equal(administration, args[0]);
            Assert.Equal(3, args[1]);
            Assert.Equal(25, args[2]);
            Assert.Equal("Name & code +", args[3]);
            Assert.Equal(false, args[4]);
            return new Contracts.TransponderPage([], 123);
        });
        await using var app = await CreateAppAsync(transponders: backend);
        using var client = CreateClient(app);
        await client.GetAsync(administration ? "signin?role=timing-admin" : "signin?role=user");
        var page = await new TransponderManagementHttpService(client).GetTranspondersAsync(administration, 3, 25, "Name & code +", false);
        Assert.Empty(page.Items);
        Assert.Equal(123, page.TotalCount);
    }

    [Fact]
    public async Task TransponderLookup_ForwardsSearchAndRequiresAdministrator()
    {
        var backend = PlatformServiceTests.TestProxy.Create<ITransponderManagementService>((method, args) =>
        {
            Assert.Equal(nameof(ITransponderManagementService.LookupTranspondersAsync), method.Name);
            Assert.Equal(2, args[0]);
            Assert.Equal("Code &", args[1]);
            return new Contracts.TransponderLookupPage([], 27);
        });
        await using var app = await CreateAppAsync(transponders: backend);
        using var client = CreateClient(app);
        await client.GetAsync("signin?role=user");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("api/admin/timing/transponders/lookup")).StatusCode);
        await client.GetAsync("signin?role=timing-admin");
        Assert.Equal(27, (await new TransponderManagementHttpService(client).LookupTranspondersAsync(2, "Code &")).TotalCount);
    }
}
