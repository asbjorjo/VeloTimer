using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.WebUI.Mud.Client.Components;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.Platform.Test;

public sealed class AgentCapabilitiesPanelTests
{
    [Fact]
    public async Task Panel_SaysNoReportYetWhenTheAgentNeverReported()
    {
        var html = await Render(null);

        Assert.Contains("No report yet", html);
        Assert.Contains("Ask agent for a new report", html);
    }

    [Fact]
    public async Task Panel_ListsKnownAndUnknownCapabilitiesWithReplayLimits()
    {
        var report = new AgentCapabilitiesDTO
        {
            ReportedAtUtc = DateTime.UtcNow.AddMinutes(-5), AgentType = "P3", AgentVersion = "2.1.0", ControlProtocolVersion = 1,
            Capabilities =
            [
                new AgentCapabilityDTO
                {
                    Name = "passing-replay",
                    PassingReplay = new PassingReplayCapabilityDTO { Mode = "AgentStore", MaxRangeSeconds = 30 * 86400d }
                },
                new AgentCapabilityDTO { Name = "control.pause-resume" },
                new AgentCapabilityDTO { Name = "future.capability" }
            ]
        };

        var html = await Render(report);

        Assert.Contains("Agent P3, version 2.1.0", html);
        Assert.Contains("Passing replay", html);
        Assert.Contains("From the agent&#x27;s store", html);
        Assert.Contains("up to 30 days per request", html);
        Assert.Contains("oldest passing unknown", html);
        Assert.Contains("Pause and resume", html);
        Assert.Contains("future.capability (not known to this version)", html);
        Assert.DoesNotContain("more than a day old", html);
    }

    [Fact]
    public async Task Panel_WarnsWhenTheReportIsOlderThanADay()
    {
        var report = new AgentCapabilitiesDTO { ReportedAtUtc = DateTime.UtcNow.AddDays(-2), AgentType = "X2", AgentVersion = "1" };

        var html = await Render(report);

        Assert.Contains("more than a day old", html);
        Assert.Contains("The agent advertises no capabilities.", html);
    }

    private static async Task<string> Render(AgentCapabilitiesDTO? report)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLocalization(options => options.ResourcesPath = "Resources");
            services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            services.AddTransient<MudLocalizer, LocalizedMudLocalizer>();
            services.AddSingleton<IJSRuntime, NoJavaScript>();
            services.AddSingleton<NavigationManager, TestNavigation>();
            services.AddSingleton(DispatchProxy.Create<IInfrastructureAdministrationService, UnusedService>());
            await using var provider = services.BuildServiceProvider();
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            return await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var output = await renderer.RenderComponentAsync<AgentCapabilitiesPanel>(ParameterView.FromDictionary(
                    new Dictionary<string, object?>
                    {
                        [nameof(AgentCapabilitiesPanel.InstallationId)] = Guid.NewGuid(),
                        [nameof(AgentCapabilitiesPanel.Report)] = report
                    }));
                return output.ToHtmlString();
            });
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    public class UnusedService : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/admin/timing/installations");
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) =>
            throw new JSDisconnectedException("Static component test.");
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new JSDisconnectedException("Static component test.");
    }
}
