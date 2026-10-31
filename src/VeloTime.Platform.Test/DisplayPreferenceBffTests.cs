using System.Net;
using System.Net.Http.Json;
using VeloTime.WebUI.Mud.Client.Services;
using Contracts = VeloTime.Module.Profiles.Client;
namespace VeloTime.Platform.Test;
public sealed partial class WebUiBffTests
{
    private static Contracts.DisplayPreferenceDTO Snapshot() => new() { DistanceUnit = "ft", SpeedUnit = "mph", TimeMode = "timeZone", TimeZoneId = "Europe/Oslo" };
    [Fact]
    public async Task DisplayPreferences_BffAndUiTransport_AgreeOnRoutesShapeAndNoStore()
    {
        var profiles = PlatformServiceTests.TestProxy.Create<Contracts.IProfilesClient>((method, args) => method.Name switch
        {
            nameof(Contracts.IProfilesClient.GetCurrentDisplayPreferenceAsync) => Snapshot(),
            nameof(Contracts.IProfilesClient.UpdateDisplayPreferenceAsync) => VerifySaved(args),
            nameof(Contracts.IProfilesClient.GetSupportedTimeZonesAsync) => new List<Contracts.TimeZoneOptionDTO> { new() { Id = "Europe/Oslo", DisplayName = "Oslo" } },
            _ => throw new NotSupportedException(method.Name)
        });
        await using var app = await CreateAppAsync(profiles: profiles); using var client = CreateClient(app); await client.GetAsync("signin?role=user");
        var transport = new DisplayPreferenceHttpService(client);
        Assert.Equal(new("ft", "mph", "timeZone", "Europe/Oslo"), await transport.GetAsync());
        Assert.Equal("Europe/Oslo", Assert.Single(await transport.GetTimeZonesAsync()).Id);
        Assert.Equal(new("ft", "mph", "timeZone", "Europe/Oslo"), await transport.SaveAsync(new("ft", "mph", "timeZone", "Europe/Oslo")));
        foreach (var route in new[] { "api/profile/preferences/display", "api/profile/preferences/time-zones" })
        {
            using var response = await client.GetAsync(route); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        }
        var calls = ((PlatformServiceTests.TestProxy)(object)profiles).Calls;
        Assert.Equal(4, calls.Count);
        Assert.Single(calls, call => call.Method.Name == nameof(Contracts.IProfilesClient.GetSupportedTimeZonesAsync));
        Assert.Equal(2, calls.Count(call => call.Method.Name == nameof(Contracts.IProfilesClient.GetCurrentDisplayPreferenceAsync)));
    }
    private static Contracts.DisplayPreferenceDTO VerifySaved(object?[] args)
    {
        var request = Assert.IsType<Contracts.UpdateDisplayPreferenceRequest>(args[0]);
        Assert.Equal("ft", request.DistanceUnit); Assert.Equal("mph", request.SpeedUnit); Assert.Equal("timeZone", request.TimeMode); Assert.Equal("Europe/Oslo", request.TimeZoneId); Assert.IsType<CancellationToken>(args[1]); return Snapshot();
    }
    [Fact]
    public async Task DisplayPreferences_UnauthenticatedOrMissingCsrf_NeverReachProfiles()
    {
        var profiles = PlatformServiceTests.TestProxy.Create<Contracts.IProfilesClient>((_, _) => throw new InvalidOperationException("must not call upstream"));
        await using var app = await CreateAppAsync(profiles: profiles); using var client = CreateClient(app, antiforgery: false);
        foreach (var route in new[] { "api/profile/preferences/display", "api/profile/preferences/time-zones" }) Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync("api/profile/preferences/display", new { distanceUnit = "m", speedUnit = "km/h", timeMode = "browser", timeZoneId = (string?)null })).StatusCode);
        await client.GetAsync("signin?role=user");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("api/profile/preferences/display", new { distanceUnit = "m", speedUnit = "km/h", timeMode = "browser", timeZoneId = (string?)null })).StatusCode);
        Assert.Empty(((PlatformServiceTests.TestProxy)(object)profiles).Calls);
    }
    [Fact]
    public async Task DisplayPreferences_PropagatesValidationProblemUnchangedAndNoStore()
    {
        const string problem = "{\"type\":\"about:blank\",\"status\":400,\"code\":\"profiles.invalid_speed_unit\"}";
        var profiles = PlatformServiceTests.TestProxy.Create<Contracts.IProfilesClient>((_, _) => throw new Contracts.ApiException("invalid", 400, problem, null!, null!));
        await using var app = await CreateAppAsync(profiles: profiles); using var client = CreateClient(app); await client.GetAsync("signin?role=user");
        using var response = await client.PutAsJsonAsync("api/profile/preferences/display", new { distanceUnit = "m", speedUnit = "bad", timeMode = "browser", timeZoneId = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(problem, await response.Content.ReadAsStringAsync()); Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType); Assert.True(response.Headers.CacheControl!.NoStore);
    }
}
