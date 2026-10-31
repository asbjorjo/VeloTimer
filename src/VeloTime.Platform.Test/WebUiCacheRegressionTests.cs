using System.Globalization;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using VeloTime.WebUI.Mud.Services;
using Profiles = VeloTime.Module.Profiles.Client;
using Facilities = VeloTime.Module.Facilities.Client;
using Timing = VeloTime.Module.Timing.Client;

namespace VeloTime.Platform.Test;

public sealed class WebUiCacheRegressionTests
{
    [Theory]
    [InlineData(null, "alice")]
    [InlineData("", "alice")]
    [InlineData("https://issuer", null)]
    [InlineData("https://issuer", " ")]
    public async Task MissingValidatedClaims_ForbidBeforeAnyPreferenceOrCatalogueHit(string? issuer, string? subject)
    {
        var profiles = ProfilesClient();
        using var host = new WebUiCacheTestHost(profiles);
        var service = new DisplayPreferenceService(profiles, host.Cache);
        await service.GetAsync();
        await service.GetTimeZonesAsync();
        host.Authentication.User = WebUiCacheTestHost.Principal(issuer, subject);
        foreach (var action in new Func<Task>[] { async () => await service.GetAsync(), async () => await service.GetTimeZonesAsync(),
                     async () => await service.SaveAsync(new("ft", "mph", "utc", null)) })
            Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<HttpRequestException>(action)).StatusCode);
        Assert.Equal(2, Calls(profiles));
    }

    [Fact]
    public async Task ActiveHttpPrincipalIsAuthoritativeEvenWhenAnonymous_NotStaleCircuit()
    {
        var profiles = ProfilesClient();
        using var host = new WebUiCacheTestHost(profiles);
        var service = new DisplayPreferenceService(profiles, host.Cache);
        await service.GetAsync();
        host.Http.HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity()) };
        try
        {
            Assert.Equal(HttpStatusCode.Forbidden,
                (await Assert.ThrowsAsync<HttpRequestException>(() => service.GetAsync())).StatusCode);
            host.Http.HttpContext.User = WebUiCacheTestHost.Principal("https://active", "bob");
            await service.GetAsync();
            Assert.Equal(2, Calls(profiles));
        }
        finally { host.Http.HttpContext = null; }
    }

    [Fact]
    public async Task StartedResponseExclusivelyUsesCurrentCircuitIdentity_NeverCapturedHttpUser()
    {
        var profiles = ProfilesClient();
        using var host = new WebUiCacheTestHost(profiles);
        var service = new DisplayPreferenceService(profiles, host.Cache);
        var context = new DefaultHttpContext { User = WebUiCacheTestHost.Principal() };
        context.Features.Set<IHttpResponseFeature>(new StartedResponse());
        host.Http.HttpContext = context;
        try
        {
            await service.GetAsync();
            host.Authentication.User = WebUiCacheTestHost.Principal("https://circuit", "bob");
            await service.GetAsync();
            Assert.Equal(2, Calls(profiles));
            host.Authentication.User = new(new ClaimsIdentity());
            await Assert.ThrowsAsync<HttpRequestException>(() => service.GetAsync());
            Assert.Equal(2, Calls(profiles));
        }
        finally { host.Http.HttpContext = null; }
    }

    private sealed class StartedResponse : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    [Fact]
    public async Task SessionIssuerAndLogoutLoginNamespacesPreventReusingAnotherIdentity()
    {
        var profiles = ProfilesClient();
        using var host = new WebUiCacheTestHost(profiles);
        var service = new DisplayPreferenceService(profiles, host.Cache);
        foreach (var principal in new[]
                 {
                     WebUiCacheTestHost.Principal("https://one", "same", "sid1"),
                     WebUiCacheTestHost.Principal("https://one", "same", "sid2"),
                     WebUiCacheTestHost.Principal("https://two", "same", "sid1"),
                     WebUiCacheTestHost.Principal("https://one", "different", "sid1"),
                     WebUiCacheTestHost.Principal("https://one", "same", null)
                 })
        {
            host.Authentication.User = principal;
            await service.GetAsync();
            await service.GetAsync();
        }
        Assert.Equal(5, Calls(profiles));
        host.Authentication.User = new(new ClaimsIdentity());
        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetAsync());
        host.Authentication.User = WebUiCacheTestHost.Principal("https://one", "same", "new-login");
        await service.GetAsync();
        Assert.Equal(6, Calls(profiles));
    }

    [Theory]
    [InlineData("distance")]
    [InlineData("speed")]
    [InlineData("mode")]
    [InlineData("zone")]
    [InlineData("null")]
    [InlineData("failure")]
    public async Task InvalidOrFailedReadsAreNeverCached_RecoveryBecomesWarm(string kind)
    {
        var valid = false;
        var profiles = PlatformServiceTests.TestProxy.Create<Profiles.IProfilesClient>((_, _) =>
        {
            if (valid) return Display();
            if (kind == "failure") throw new HttpRequestException("offline");
            if (kind == "null") return null;
            var value = Display();
            if (kind == "distance") value.DistanceUnit = "FT";
            if (kind == "speed") value.SpeedUnit = "NaN";
            if (kind == "mode") value.TimeMode = "local";
            if (kind == "zone") { value.TimeMode = "timeZone"; value.TimeZoneId = "Eastern Standard Time"; }
            return value;
        });
        using var host = new WebUiCacheTestHost(profiles);
        var service = new DisplayPreferenceService(profiles, host.Cache);
        for (var i = 0; i < 2; i++)
        {
            if (kind == "failure") await Assert.ThrowsAsync<HttpRequestException>(() => service.GetAsync());
            else await Assert.ThrowsAsync<InvalidDataException>(() => service.GetAsync());
        }
        Assert.Equal(2, Calls(profiles));
        valid = true;
        Assert.Equal("m", (await service.GetAsync()).DistanceUnit);
        await service.GetAsync();
        Assert.Equal(3, Calls(profiles));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("invalid-dto")]
    [InlineData("malformed-success")]
    [InlineData("failed-write")]
    public async Task SaveEvictsAfterDurableSuccessIncludingInvalidResponse_ButNotFailedWrite(string kind)
    {
        var distance = "m";
        var reads = 0;
        var profiles = PlatformServiceTests.TestProxy.Create<Profiles.IProfilesClient>((method, _) =>
        {
            if (method.Name == nameof(Profiles.IProfilesClient.GetCurrentDisplayPreferenceAsync))
            {
                reads++;
                return Display(distance);
            }
            if (kind == "failed-write") throw new Profiles.ApiException("failed", 400, "problem", null!, null!);
            distance = "ft"; // upstream has committed
            if (kind == "malformed-success") throw new Profiles.ApiException("bad json", 200, "not-json", null!, null!);
            return Display(kind == "invalid-dto" ? "bogus" : distance);
        });
        using var host = new WebUiCacheTestHost(profiles);
        var service = new DisplayPreferenceService(profiles, host.Cache);
        await service.GetAsync();
        if (kind == "failed-write")
            await Assert.ThrowsAsync<Profiles.ApiException>(() => service.SaveAsync(new("ft", "mph", "utc", null)));
        else if (kind == "valid")
            Assert.Equal("ft", (await service.SaveAsync(new("ft", "mph", "utc", null))).DistanceUnit);
        else
            await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsync(new("ft", "mph", "utc", null)));
        Assert.Equal(kind == "failed-write" ? "m" : "ft", (await service.GetAsync()).DistanceUnit);
        await service.GetAsync();
        Assert.Equal(kind == "failed-write" ? 1 : 2, reads);
    }

    [Fact]
    public async Task CatalogueIsCulturePartitionedDeepCopiedAndRejectsMalformedOptions()
    {
        var zones = new List<Profiles.TimeZoneOptionDTO> { new() { Id = "Europe/Oslo", DisplayName = "Oslo" } };
        var profiles = PlatformServiceTests.TestProxy.Create<Profiles.IProfilesClient>((_, _) => zones);
        using var host = new WebUiCacheTestHost(profiles);
        var service = new DisplayPreferenceService(profiles, host.Cache);
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            var first = await service.GetTimeZonesAsync();
            ((VeloTime.Module.Profiles.Interface.Data.TimeZoneOptionDTO[])first)[0] = new("UTC", "Poisoned");
            zones[0].DisplayName = "Upstream changed";
            Assert.Equal("Oslo", (await service.GetTimeZonesAsync())[0].DisplayName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nb-NO");
            Assert.Equal("Upstream changed", (await service.GetTimeZonesAsync())[0].DisplayName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            zones[0].Id = "bogus";
            await Assert.ThrowsAsync<InvalidDataException>(() => service.GetTimeZonesAsync());
            await Assert.ThrowsAsync<InvalidDataException>(() => service.GetTimeZonesAsync());
            zones[0].Id = "Europe/Oslo";
            await service.GetTimeZonesAsync();
            Assert.Equal(5, Calls(profiles));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public async Task UnsetLanguageIsCachedButFailureAndMalformedLanguageAreNot_IndependentSaveTag()
    {
        string? language = null;
        Exception? failure = null;
        var reads = 0;
        var profiles = PlatformServiceTests.TestProxy.Create<Profiles.IProfilesClient>((method, _) =>
        {
            if (method.Name == nameof(Profiles.IProfilesClient.GetCurrentDisplayPreferenceAsync)) return Display();
            if (method.Name == nameof(Profiles.IProfilesClient.GetCurrentLanguagePreferenceAsync))
            {
                reads++;
                if (failure is not null) throw failure;
            }
            return new Profiles.LanguagePreferenceDTO { Language = language };
        });
        using var host = new WebUiCacheTestHost(profiles);
        var service = new LanguagePreferenceService(profiles, host.Cache);
        var display = new DisplayPreferenceService(profiles, host.Cache);
        await display.GetAsync();
        Assert.Null((await service.GetAsync()).Language);
        Assert.Null((await service.GetAsync()).Language);
        Assert.Equal(1, reads);
        language = "fr";
        await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsync("nb-NO"));
        for (var i = 0; i < 2; i++) await Assert.ThrowsAsync<InvalidDataException>(() => service.GetAsync());
        failure = new HttpRequestException("offline");
        for (var i = 0; i < 2; i++) await Assert.ThrowsAsync<HttpRequestException>(() => service.GetAsync());
        failure = null;
        language = "nb-NO";
        Assert.Equal("nb-NO", (await service.GetAsync()).Language);
        await service.GetAsync();
        await display.GetAsync();
        Assert.Equal(6, reads);
        Assert.Equal(8, Calls(profiles)); // display once + language save once + language reads
    }

    [Fact]
    public async Task WarmAdminPolicyRevocationForbidsWithoutUpstream_PrivateCallerAndPublicShapeAreSeparate()
    {
        var id = Guid.NewGuid();
        var facilities = PlatformServiceTests.TestProxy.Create<Facilities.IFacilitiesClient>((method, _) =>
            method.Name == nameof(Facilities.IFacilitiesClient.FacilityAllAsync)
                ? new List<Facilities.FacilityDTO> { new() { Id = id, Name = "Track", Layouts = [] } }
                : throw new NotSupportedException(method.Name));
        var timing = PlatformServiceTests.TestProxy.Create<Timing.ITimingClient>((_, _) => throw new NotSupportedException());
        using var host = new WebUiCacheTestHost(facilities);
        var admin = new InfrastructureAdministrationService(facilities, timing, host.Cache);
        var publicService = new FacilityService(timing, host.Cache);
        var first = Assert.Single(await admin.GetFacilitiesAsync());
        first.Name = "Poisoned";
        first.Layouts.Add(Guid.NewGuid());
        Assert.Equal("Track", Assert.Single(await admin.GetFacilitiesAsync()).Name);
        Assert.Empty(Assert.Single(await admin.GetFacilitiesAsync()).Layouts);
        Assert.Equal("Track", Assert.Single(await publicService.GetFacilitiesAsync(default)).Name);
        Assert.Equal(2, Calls(facilities));
        host.Authentication.User = WebUiCacheTestHost.Principal(subject: "another");
        await admin.GetFacilitiesAsync();
        Assert.Equal(3, Calls(facilities));
        host.Authentication.User = WebUiCacheTestHost.Principal(admin: false);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Assert.ThrowsAsync<HttpRequestException>(() => admin.GetFacilitiesAsync())).StatusCode);
        Assert.Equal(3, Calls(facilities));
        await publicService.GetFacilitiesAsync(default);
        Assert.Equal(3, Calls(facilities));
    }

    [Fact]
    public async Task PrivateUsernameIsCallerPartitioned_InvalidNamesRetry_AccountSecurityReadsStayLive()
    {
        var name = "";
        var keycloak = PlatformServiceTests.TestProxy.Create<VeloTime.WebUI.Keycloak.IKeycloakClient>((method, _) =>
            method.Name switch
            {
                nameof(VeloTime.WebUI.Keycloak.IKeycloakClient.GetAccountAsync) =>
                    new VeloTime.WebUI.Keycloak.AccountRepresentation { Username = name },
                nameof(VeloTime.WebUI.Keycloak.IKeycloakClient.GetLinkedAccountsAsync) =>
                    new List<VeloTime.WebUI.Keycloak.LinkedAccountRepresentation>(),
                _ => throw new NotSupportedException(method.Name)
            });
        using var host = new WebUiCacheTestHost(keycloak);
        var service = new ProfileService(keycloak, host.Cache);
        for (var i = 0; i < 2; i++) await Assert.ThrowsAsync<HttpRequestException>(() => service.GetUsernameAsync(default));
        name = "Alice";
        Assert.Equal("Alice", await service.GetUsernameAsync(default));
        name = "Upstream renamed";
        Assert.Equal("Alice", await service.GetUsernameAsync(default));
        host.Authentication.User = WebUiCacheTestHost.Principal(subject: "bob");
        Assert.Equal("Upstream renamed", await service.GetUsernameAsync(default));
        host.Authentication.User = WebUiCacheTestHost.Principal(issuer: "https://other", subject: "rider");
        Assert.Equal("Upstream renamed", await service.GetUsernameAsync(default));
        host.Authentication.User = new(new ClaimsIdentity());
        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetUsernameAsync(default));
        await service.GetLinkedAccountsAsync(default);
        await service.GetLinkedAccountsAsync(default);
        var calls = ((PlatformServiceTests.TestProxy)(object)keycloak).Calls;
        Assert.Equal(5, calls.Count(call => call.Method.Name == nameof(VeloTime.WebUI.Keycloak.IKeycloakClient.GetAccountAsync)));
        Assert.Equal(2, calls.Count(call => call.Method.Name == nameof(VeloTime.WebUI.Keycloak.IKeycloakClient.GetLinkedAccountsAsync)));
    }

    [Fact]
    public async Task StatisticsDefinitionsAreCallerAndPolicyScoped_SavesEvictBothDefinitionShapes()
    {
        var itemId = Guid.NewGuid();
        var configurationId = Guid.NewGuid();
        var distance = 250f;
        var statistics = PlatformServiceTests.TestProxy.Create<VeloTime.Module.Statistics.Client.IStatisticsClient>((method, _) =>
        {
            if (method.Name == nameof(VeloTime.Module.Statistics.Client.IStatisticsClient.ListStatisticsConfigurationsAsync))
                return new List<VeloTime.Module.Statistics.Client.StatisticsConfigurationDTO>
                {
                    new() { Id = configurationId, StatisticsItemId = itemId, Type = 0,
                        CoursePointStart = Guid.NewGuid(), CoursePointEnd = Guid.NewGuid() }
                };
            var item = new VeloTime.Module.Statistics.Client.StatisticsItemDTO
            { Id = itemId, Name = "Lap", Description = "Definition", Distance = distance };
            if (method.Name == nameof(VeloTime.Module.Statistics.Client.IStatisticsClient.ListStatisticsItemsAsync))
                return new List<VeloTime.Module.Statistics.Client.StatisticsItemDTO> { item };
            distance = 500;
            item.Distance = distance;
            return item;
        });
        using var host = new WebUiCacheTestHost(statistics);
        var service = new StatisticsAdministrationService(statistics, host.Cache);
        var items = await service.GetItemsAsync();
        ((VeloTime.Module.Statistics.Interface.Data.StatisticsItemDTO[])items)[0] = new(itemId, "Poisoned", "", 1);
        Assert.Equal(250, Assert.Single(await service.GetItemsAsync()).Distance);
        await service.GetConfigurationsAsync();
        await service.GetConfigurationsAsync();
        Assert.Equal(2, Calls(statistics));
        await service.SaveItemAsync(itemId, new("Lap", "Definition", 500));
        Assert.Equal(500, Assert.Single(await service.GetItemsAsync()).Distance);
        await service.GetConfigurationsAsync();
        Assert.Equal(5, Calls(statistics));
        host.Authentication.User = WebUiCacheTestHost.Principal(subject: "another-admin");
        await service.GetItemsAsync();
        Assert.Equal(6, Calls(statistics));
        host.Authentication.User = WebUiCacheTestHost.Principal(admin: false);
        foreach (var action in new Func<Task>[] { async () => await service.GetItemsAsync(), async () => await service.GetConfigurationsAsync() })
            Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<HttpRequestException>(action)).StatusCode);
        Assert.Equal(6, Calls(statistics));
    }

    private static Profiles.DisplayPreferenceDTO Display(string distance = "m") => new()
    { DistanceUnit = distance, SpeedUnit = "km/h", TimeMode = "utc", TimeZoneId = null };
    private static Profiles.IProfilesClient ProfilesClient() => PlatformServiceTests.TestProxy.Create<Profiles.IProfilesClient>((method, _) =>
        method.Name == nameof(Profiles.IProfilesClient.GetSupportedTimeZonesAsync)
            ? new List<Profiles.TimeZoneOptionDTO> { new() { Id = "Europe/Oslo", DisplayName = "Oslo" } }
            : Display());
    private static int Calls(object client) => ((PlatformServiceTests.TestProxy)client).Calls.Count;
}
