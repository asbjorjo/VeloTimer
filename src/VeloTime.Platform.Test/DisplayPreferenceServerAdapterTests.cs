using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using System.Text.Json;
using VeloTime.Module.Profiles.Interface.Data;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Services;
using ProfilesClient = VeloTime.Module.Profiles.Client;

namespace VeloTime.Platform.Test;

public sealed class DisplayPreferenceServerAdapterTests
{
    [Fact]
    public async Task DelegatedServiceMapsPreferenceAndZoneDtosAndForwardsCancellation()
    {
        var expected = new ProfilesClient.DisplayPreferenceDTO
        {
            DistanceUnit = "ft", SpeedUnit = "mph", TimeMode = "timeZone", TimeZoneId = "Europe/Oslo"
        };
        var token = new CancellationTokenSource().Token;
        var profiles = PlatformServiceTests.TestProxy.Create<ProfilesClient.IProfilesClient>((method, arguments) =>
        {
            Assert.Equal(token, arguments[^1]);
            return method.Name switch
            {
                nameof(ProfilesClient.IProfilesClient.GetCurrentDisplayPreferenceAsync) => expected,
                nameof(ProfilesClient.IProfilesClient.UpdateDisplayPreferenceAsync) => VerifyUpdate(arguments),
                nameof(ProfilesClient.IProfilesClient.GetSupportedTimeZonesAsync) => new List<ProfilesClient.TimeZoneOptionDTO>
                {
                    new() { Id = "Europe/Oslo", DisplayName = "Oslo" },
                    new() { Id = "Pacific/Honolulu", DisplayName = "Honolulu" }
                },
                _ => throw new NotSupportedException(method.Name)
            };
        });
        var service = new DisplayPreferenceService(profiles);

        Assert.Equal(new DisplayPreferenceDTO("ft", "mph", "timeZone", "Europe/Oslo"), await service.GetAsync(token));
        Assert.Equal(new DisplayPreferenceDTO("ft", "mph", "timeZone", "Europe/Oslo"),
            await service.SaveAsync(new UpdateDisplayPreferenceRequest("ft", "mph", "timeZone", "Europe/Oslo"), token));
        var zones = await service.GetTimeZonesAsync(token);
        Assert.Equal(new TimeZoneOptionDTO("Europe/Oslo", "Oslo"), zones[0]);
        Assert.Equal(new TimeZoneOptionDTO("Pacific/Honolulu", "Honolulu"), zones[1]);
        Assert.Equal(3, ((PlatformServiceTests.TestProxy)(object)profiles).Calls.Count);
    }

    [Fact]
    public async Task DelegatedServicePropagatesCancellationAndUpstreamFailures()
    {
        using var source = new CancellationTokenSource();
        var cancellation = new OperationCanceledException(source.Token);
        var canceledProfiles = PlatformServiceTests.TestProxy.Create<ProfilesClient.IProfilesClient>((_, arguments) =>
        {
            Assert.Equal(source.Token, arguments[^1]);
            throw cancellation;
        });
        var canceledService = new DisplayPreferenceService(canceledProfiles);
        Assert.Same(cancellation, await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledService.GetAsync(source.Token)));

        var upstreamError = new InvalidOperationException("Profiles upstream failed");
        var failingProfiles = PlatformServiceTests.TestProxy.Create<ProfilesClient.IProfilesClient>((_, _) => throw upstreamError);
        var failingService = new DisplayPreferenceService(failingProfiles);
        Assert.Same(upstreamError, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failingService.GetTimeZonesAsync(CancellationToken.None)));
    }

    [Fact]
    public async Task ProductionHostRegistersScopedStateAndInitializationUsesBrowserZone()
    {
        using var factory = new WebApplicationFactory<DisplayPreferenceService>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:cache", "127.0.0.1:1,abortConnect=false,connectTimeout=100");
            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(IJSRuntime)).ToArray())
                    services.Remove(descriptor);
                services.AddScoped<IJSRuntime>(_ => new BrowserRuntime("Pacific/Honolulu", "en-US"));
            });
        });

        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var firstState = firstScope.ServiceProvider.GetRequiredService<DisplayPreferenceState>();
        var firstService = firstScope.ServiceProvider.GetRequiredService<IDisplayPreferenceService>();
        Assert.IsType<DisplayPreferenceService>(firstService);
        Assert.Same(firstState, firstScope.ServiceProvider.GetRequiredService<DisplayPreferenceState>());
        Assert.NotSame(firstState, secondScope.ServiceProvider.GetRequiredService<DisplayPreferenceState>());

        await firstState.InitializeAsync(signedIn: false, CancellationToken.None);

        Assert.Equal("Pacific/Honolulu", firstState.Effective.ZoneLabel);
        Assert.True(firstState.Effective.IsReady);
    }

    private static ProfilesClient.DisplayPreferenceDTO VerifyUpdate(object?[] arguments)
    {
        var request = Assert.IsType<ProfilesClient.UpdateDisplayPreferenceRequest>(arguments[0]);
        Assert.Equal("ft", request.DistanceUnit);
        Assert.Equal("mph", request.SpeedUnit);
        Assert.Equal("timeZone", request.TimeMode);
        Assert.Equal("Europe/Oslo", request.TimeZoneId);
        return new ProfilesClient.DisplayPreferenceDTO
        {
            DistanceUnit = request.DistanceUnit,
            SpeedUnit = request.SpeedUnit,
            TimeMode = request.TimeMode,
            TimeZoneId = request.TimeZoneId
        };
    }

    private sealed class BrowserRuntime(string zone, string locale) : IJSRuntime, IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (identifier == "import") return ValueTask.FromResult((TValue)(object)this);
            if (identifier != "browserPresentation") throw new NotSupportedException(identifier);
            var value = JsonSerializer.Deserialize<TValue>(JsonSerializer.Serialize(new { TimeZone = zone, Locale = locale }));
            return ValueTask.FromResult(value!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
