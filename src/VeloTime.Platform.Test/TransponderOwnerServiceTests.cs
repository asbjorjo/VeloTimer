using System.Net;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Client;
using VeloTime.WebUI.Mud.Client.ViewModel;
using VeloTime.WebUI.Mud.Services;
using Directory = VeloTime.WebUI.KeycloakAdmin;

namespace VeloTime.Platform.Test;

public sealed class TransponderOwnerServiceTests
{
    private static readonly Guid OwnerId = Guid.Parse("da04e68b-d0e2-4590-9e83-689eab2bbf83");

    [Theory]
    [InlineData(true, "Private profile")]
    [InlineData(false, "Private profile")]
    [InlineData(false, null)]
    public async Task Self_ReturnsOnlyOwnStatisticsNameWithoutCallingKeycloak(bool httpRequest, string? name)
    {
        var statistics = Stats((method, _) =>
        {
            Assert.Equal(nameof(IStatisticsClient.GetOwnOwnerProfileAsync), method.Name);
            return new OwnerProfileDTO { UserId = OwnerId, ProfileName = name };
        });
        using var context = new TestContext(statistics, Accounts(), administration: false, httpRequest);

        var result = await context.Service.GetOwnProfileAsync();

        Assert.Equal(OwnerId, result.UserId);
        Assert.Equal(name, result.ProfileName);
        Assert.Equal(name ?? "No statistics profile", result.DisplayName);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.DoesNotContain("FirstName", json);
        Assert.DoesNotContain("LastName", json);
    }

    [Fact]
    public async Task Self_RejectsProfileResponseForAnotherSubject()
    {
        using var context = new TestContext(Stats((_, _) => new OwnerProfileDTO { UserId = Guid.NewGuid(), ProfileName = "Wrong user" }), Accounts());
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => context.Service.GetOwnProfileAsync());
        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Administration_EnforcesPermissionInHttpAndServerCircuit(bool httpRequest)
    {
        using var context = new TestContext(Stats(), Accounts(), administration: false, httpRequest);
        var search = await Assert.ThrowsAsync<HttpRequestException>(() => context.Service.SearchAsync("racer"));
        var resolve = await Assert.ThrowsAsync<HttpRequestException>(() => context.Service.ResolveAsync([OwnerId]));
        Assert.Equal(HttpStatusCode.Forbidden, search.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, resolve.StatusCode);
    }

    [Fact]
    public async Task Resolve_DeduplicatesIdsJoinsNamesAndRetainsUnavailableHistory()
    {
        var deleted = Guid.NewGuid();
        var missingProfile = Guid.NewGuid();
        var statistics = Stats((method, args) =>
        {
            Assert.Equal(nameof(IStatisticsClient.ResolveOwnerProfilesAsync), method.Name);
            var ids = Assert.IsType<ResolveOwnerProfilesRequest>(args[0]).UserIds;
            Assert.Equal(3, ids.Count);
            return new List<OwnerProfileDTO> { new() { UserId = OwnerId, ProfileName = "Racer" } };
        });
        var keycloak = Accounts((method, args) =>
        {
            Assert.Equal(nameof(Directory.IKeycloakAdminClient.UsersGET2Async), method.Name);
            var id = Guid.Parse(Assert.IsType<string>(args[2]));
            if (id == deleted) throw DirectoryError(404);
            return Account(id, "First", "Last");
        });
        using var context = new TestContext(statistics, keycloak);

        var owners = await context.Service.ResolveAsync([OwnerId, deleted, OwnerId, missingProfile]);

        Assert.Equal(3, owners.Count);
        Assert.Equal("Racer (First Last)", owners.Single(owner => owner.UserId == OwnerId).DisplayName);
        Assert.Equal("No statistics profile (First Last)", owners.Single(owner => owner.UserId == missingProfile).DisplayName);
        Assert.False(owners.Single(owner => owner.UserId == deleted).AccountAvailable);
        Assert.Contains("User unavailable", owners.Single(owner => owner.UserId == deleted).DisplayName);
        Assert.Equal(3, ((PlatformServiceTests.TestProxy)(object)keycloak).Calls.Count);
    }

    [Fact]
    public async Task Search_JoinsBothSourcesDeduplicatesUsersAndKeepsDuplicateNames()
    {
        var profileOnly = Guid.NewGuid();
        var directoryOnly = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        var statistics = Stats((method, args) => method.Name switch
        {
            nameof(IStatisticsClient.SearchOwnerProfilesAsync) => new OwnerProfilePage
            {
                Profiles = [
                    new() { UserId = OwnerId, ProfileName = "Racer" },
                    new() { UserId = profileOnly, ProfileName = "Racer" },
                    new() { UserId = deleted, ProfileName = "Deleted" }],
                NextOffset = 10
            },
            nameof(IStatisticsClient.ResolveOwnerProfilesAsync) => new List<OwnerProfileDTO>(),
            _ => throw new NotSupportedException(method.Name)
        });
        var keycloak = Accounts((method, args) =>
        {
            if (method.Name == nameof(Directory.IKeycloakAdminClient.UsersAll3Async))
            {
                Assert.Equal(0, args[7]);
                Assert.Equal(11, args[12]);
                Assert.Equal("racer", args[14]);
                return new List<Directory.UserRepresentation> { Account(OwnerId, "Same", "Name"), Account(directoryOnly, "Different", "User") };
            }
            var id = Guid.Parse(Assert.IsType<string>(args[2]));
            if (id == deleted) throw DirectoryError(404);
            return Account(id, "Same", "Name");
        });
        using var context = new TestContext(statistics, keycloak);

        var result = await context.Service.SearchAsync(" racer ");

        Assert.Equal(3, result.Owners.Count);
        Assert.Equal(2, result.Owners.Count(owner => owner.DisplayName == "Racer (Same Name)"));
        Assert.DoesNotContain(result.Owners, owner => owner.UserId == deleted);
        Assert.Contains(result.Owners, owner => owner.UserId == directoryOnly && owner.ProfileName is null);
        Assert.Equal(10, result.NextProfileOffset);
        Assert.Null(result.NextKeycloakOffset);
    }

    [Fact]
    public async Task Search_UsesIndependentOffsetsAndNeverFetchesMoreThanOneBoundedPage()
    {
        var users = Enumerable.Range(0, 11).Select(_ => Account(Guid.NewGuid(), "First", "Last")).ToList();
        var statistics = Stats((method, args) =>
        {
            Assert.Equal(nameof(IStatisticsClient.ResolveOwnerProfilesAsync), method.Name);
            Assert.Equal(10, Assert.IsType<ResolveOwnerProfilesRequest>(args[0]).UserIds.Count);
            return new List<OwnerProfileDTO>();
        });
        var keycloak = Accounts((method, args) =>
        {
            Assert.Equal(nameof(Directory.IKeycloakAdminClient.UsersAll3Async), method.Name);
            Assert.Equal(30, args[7]);
            Assert.Equal(11, args[12]);
            return users;
        });
        using var context = new TestContext(statistics, keycloak);

        var result = await context.Service.SearchAsync("first", null, 30);

        Assert.Equal(10, result.Owners.Count);
        Assert.Null(result.NextProfileOffset);
        Assert.Equal(40, result.NextKeycloakOffset);
        Assert.Single(((PlatformServiceTests.TestProxy)(object)keycloak).Calls);
    }

    [Fact]
    public async Task Search_SkipsExhaustedKeycloakSourceButResolvesProfileMatches()
    {
        var statistics = Stats((method, args) =>
        {
            Assert.Equal(nameof(IStatisticsClient.SearchOwnerProfilesAsync), method.Name);
            Assert.Equal(20, args[1]);
            Assert.Equal(10, args[2]);
            return new OwnerProfilePage { Profiles = [new() { UserId = OwnerId, ProfileName = "Racer" }], NextOffset = null };
        });
        var keycloak = Accounts((method, _) =>
        {
            Assert.Equal(nameof(Directory.IKeycloakAdminClient.UsersGET2Async), method.Name);
            return Account(OwnerId, "First", "Last");
        });
        using var context = new TestContext(statistics, keycloak);

        var result = await context.Service.SearchAsync("racer", 20, null);

        Assert.Single(result.Owners);
        Assert.Null(result.NextProfileOffset);
        Assert.Null(result.NextKeycloakOffset);
    }

    [Fact]
    public async Task Search_ReturnsAtMostTwentyUsersWhileContinuingBothSources()
    {
        var profileIds = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();
        var directoryIds = Enumerable.Range(0, 11).Select(_ => Guid.NewGuid()).ToArray();
        var statistics = Stats((method, _) => method.Name switch
        {
            nameof(IStatisticsClient.SearchOwnerProfilesAsync) => new OwnerProfilePage
            {
                Profiles = profileIds.Select(id => new OwnerProfileDTO { UserId = id, ProfileName = "Racer" }).ToList(),
                NextOffset = 10
            },
            nameof(IStatisticsClient.ResolveOwnerProfilesAsync) => new List<OwnerProfileDTO>(),
            _ => throw new NotSupportedException(method.Name)
        });
        var keycloak = Accounts((method, args) => method.Name == nameof(Directory.IKeycloakAdminClient.UsersAll3Async)
            ? directoryIds.Select(id => Account(id, "First", "Last")).ToList()
            : Account(Guid.Parse(Assert.IsType<string>(args[2])), "First", "Last"));
        using var context = new TestContext(statistics, keycloak);

        var page = await context.Service.SearchAsync("racer");

        Assert.Equal(20, page.Owners.Count);
        Assert.Equal(20, page.Owners.Select(owner => owner.UserId).Distinct().Count());
        Assert.Equal(10, page.NextProfileOffset);
        Assert.Equal(10, page.NextKeycloakOffset);
        Assert.DoesNotContain(page.Owners, owner => owner.UserId == directoryIds[10]);
    }

    [Theory]
    [InlineData(403, HttpStatusCode.Forbidden)]
    [InlineData(500, HttpStatusCode.ServiceUnavailable)]
    public async Task Resolve_DoesNotMislabelDirectoryFailuresAsMissingAccounts(int upstream, HttpStatusCode status)
    {
        using var context = new TestContext(Stats((_, _) => new List<OwnerProfileDTO>()), Accounts((_, _) => throw DirectoryError(upstream)));
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => context.Service.ResolveAsync([OwnerId]));
        Assert.Equal(status, exception.StatusCode);
        Assert.Contains("Keycloak", exception.Message);
    }

    [Fact]
    public async Task Resolve_DoesNotMislabelStatisticsFailureAsMissingProfiles()
    {
        using var context = new TestContext(Stats((_, _) => throw new ApiException("down", 500, "", new Dictionary<string, IEnumerable<string>>(), null!)), Accounts());
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => context.Service.ResolveAsync([OwnerId]));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Contains("Statistics", exception.Message);
    }

    [Fact]
    public async Task Resolve_RejectsMalformedDirectoryIdentity()
    {
        using var context = new TestContext(Stats((_, _) => new List<OwnerProfileDTO>()),
            Accounts((_, _) => Account(Guid.NewGuid(), "Wrong", "User")));
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => context.Service.ResolveAsync([OwnerId]));
        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
    }

    [Theory]
    [InlineData("a", 0, 0)]
    [InlineData("valid", -1, 0)]
    [InlineData("valid", 0, -1)]
    public async Task Search_RejectsInvalidInputBeforeCallingDependencies(string query, int profileOffset, int keycloakOffset)
    {
        using var context = new TestContext(Stats(), Accounts());
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => context.Service.SearchAsync(query, profileOffset, keycloakOffset));
        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task Resolve_RejectsInvalidAndOversizedInputBeforeCallingDependencies()
    {
        using var context = new TestContext(Stats(), Accounts());
        Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<HttpRequestException>(() => context.Service.ResolveAsync([Guid.Empty]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<HttpRequestException>(() =>
            context.Service.ResolveAsync(Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray()))).StatusCode);
        Assert.Empty(await context.Service.ResolveAsync([]));
    }

    private static IStatisticsClient Stats(Func<MethodInfo, object?[], object?>? handler = null) =>
        PlatformServiceTests.TestProxy.Create<IStatisticsClient>(handler ?? ((method, _) => throw new NotSupportedException(method.Name)));

    private static Directory.IKeycloakAdminClient Accounts(Func<MethodInfo, object?[], object?>? handler = null) =>
        PlatformServiceTests.TestProxy.Create<Directory.IKeycloakAdminClient>(handler ?? ((method, _) => throw new NotSupportedException(method.Name)));

    private static Directory.UserRepresentation Account(Guid id, string first, string last) =>
        new() { Id = id.ToString(), FirstName = first, LastName = last };

    private static Directory.ApiException DirectoryError(int status) =>
        new("directory failure", status, "", new Dictionary<string, IEnumerable<string>>(), null!);

    private sealed class TestContext : IDisposable
    {
        private readonly ServiceProvider services;
        internal TransponderOwnerService Service { get; }

        internal TestContext(IStatisticsClient statistics, Directory.IKeycloakAdminClient accounts, bool administration = true, bool httpRequest = true)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, OwnerId.ToString()),
                .. administration ? new[] { new Claim(AdministrationAuthorization.PermissionClaimType, AdministrationAuthorization.TimingPermission) } : []
            ], "test"));
            services = new ServiceCollection().AddLogging().AddAdministrationAuthorization().BuildServiceProvider();
            var accessor = new HttpContextAccessor { HttpContext = httpRequest ? new DefaultHttpContext { User = principal } : null };
            Service = new TransponderOwnerService(statistics, accounts, accessor, new TestStateProvider(principal),
                services.GetRequiredService<IAuthorizationService>());
        }

        public void Dispose() => services.Dispose();
    }

    private sealed class TestStateProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal));
    }
}
