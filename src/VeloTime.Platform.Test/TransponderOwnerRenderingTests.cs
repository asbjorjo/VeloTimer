using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.WebUI.Mud.Client.Components;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.Platform.Test;

public sealed class TransponderOwnerRenderingTests
{
    private static readonly Guid OwnerId = Guid.Parse("da04e68b-d0e2-4590-9e83-689eab2bbf83");
    private static readonly DateTimeOffset LastSeen = new(2026, 4, 3, 2, 5, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false, "Profile rider")]
    [InlineData(false, null)]
    [InlineData(true, "Private profile")]
    [InlineData(true, null)]
    public async Task List_RendersProfileNamesAndRestrictsKeycloakNamesToAdministrators(bool administration, string? profile)
    {
        var services = CreateServices();
        var transponderId = Guid.NewGuid();
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<ITransponderManagementService>((method, _) =>
        {
            Assert.Equal(nameof(ITransponderManagementService.GetTranspondersAsync), method.Name);
            return (IReadOnlyList<TransponderDTO>)new[]
            {
                new TransponderDTO(transponderId, "MyLaps_X2", "106187718", "HT-24422",
                    [new(Guid.NewGuid(), transponderId, OwnerId, DateTimeOffset.UtcNow.AddDays(-1), null)], LastSeen)
            };
        }));
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<ITransponderOwnerService>((method, _) => method.Name switch
        {
            nameof(ITransponderOwnerService.GetOwnProfileAsync) => new OwnTransponderProfile(OwnerId, profile),
            nameof(ITransponderOwnerService.ResolveAsync) => (IReadOnlyList<TransponderOwnerView>)new[]
                { new TransponderOwnerView(OwnerId, profile, "KeycloakFirst", "KeycloakLast", true) },
            _ => throw new NotSupportedException(method.Name)
        }));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TransponderManagement>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(TransponderManagement.Administration)] = administration }))).ToHtmlString());

        Assert.Contains(profile ?? "No statistics profile", html);
        Assert.Contains("HT-24422", html);
        if (administration)
        {
            Assert.Contains("KeycloakFirst KeycloakLast", html);
            Assert.Contains("Last seen (UTC)", html);
            Assert.Contains("2026-04-03 02:05", html);
        }
        else
        {
            Assert.DoesNotContain("KeycloakFirst", html);
            Assert.DoesNotContain(OwnerId.ToString(), html);
            Assert.DoesNotContain("Last seen (UTC)", html);
            Assert.DoesNotContain("2026-04-03 02:05", html);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Picker_PreselectsNamedOwnerAndRetainsUnavailableHistoricalOwner(bool available)
    {
        var services = CreateServices();
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<ITransponderOwnerService>((method, _) => throw new NotSupportedException(method.Name)));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var owner = new TransponderOwnerView(OwnerId, "Saved profile", available ? "First" : null, available ? "Last" : null, available);

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TransponderOwnerPicker>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(TransponderOwnerPicker.Value)] = owner }))).ToHtmlString());

        Assert.Contains("Saved profile", html);
        Assert.Contains(available ? "First Last" : "User unavailable", html);
        Assert.Contains("Find owner by profile or name", html);
        Assert.Contains("readonly", html);
        Assert.DoesNotContain("Owner user ID", html);
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
        services.AddSingleton<IJSRuntime, NoJavaScript>();
        services.AddSingleton<NavigationManager, TestNavigation>();
        return services;
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new JSDisconnectedException("The static renderer has no browser connection.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new JSDisconnectedException("The static renderer has no browser connection.");
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/transponders");
    }
}
