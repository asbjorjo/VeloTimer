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

public sealed class PassingReplayPanelTests
{
    private static AgentCapabilitiesDTO StoreReport() => new()
    {
        ReportedAtUtc = DateTime.UtcNow.AddMinutes(-5), AgentType = "Dummy", AgentVersion = "1", ControlProtocolVersion = 1,
        Capabilities =
        [
            new AgentCapabilityDTO
            {
                Name = "passing-replay",
                PassingReplay = new PassingReplayCapabilityDTO { Mode = "AgentStore", MaxRangeSeconds = 30 * 86400d, SupportsLoopFilter = true }
            }
        ]
    };

    [Fact]
    public async Task Panel_OffersReplayOnlyWhenTheAgentAdvertisesIt()
    {
        var advertised = await Render(StoreReport(), []);
        Assert.Contains("Replay passings", advertised);
        Assert.Contains("No replays requested yet.", advertised);
        Assert.DoesNotContain("has not said it can replay", advertised);

        var silent = await Render(null, []);
        Assert.Contains("The agent has not said it can replay passings.", silent);
        Assert.DoesNotContain("Replay passings", silent);
    }

    [Fact]
    public async Task Panel_ShowsHistoryWithStateReasonAndCounts()
    {
        var history = new List<PassingReplayRequestDTO>
        {
            new()
            {
                RequestId = Guid.NewGuid(), Mode = "AgentStore", State = "Completed", LoopIds = ["L1"],
                Read = 12, Published = 12, Recorded = 3, Duplicate = 9
            },
            new() { RequestId = Guid.NewGuid(), Mode = "AgentStore", State = "Rejected", Reason = "Busy" }
        };

        var html = await Render(StoreReport(), history);

        Assert.Contains("From the agent&#x27;s store (L1)", html);
        Assert.Contains("12 read, 3 recorded, 9 duplicates, 0 conflicts, 0 rejected", html);
        Assert.Contains("Rejected: A replay is already running for this agent.", html);
    }

    private static async Task<string> Render(AgentCapabilitiesDTO? report, IReadOnlyList<PassingReplayRequestDTO> history)
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
            var service = DispatchProxy.Create<IInfrastructureAdministrationService, HistoryService>();
            ((HistoryService)(object)service).History = history;
            services.AddSingleton(service);
            await using var provider = services.BuildServiceProvider();
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            return await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var output = await renderer.RenderComponentAsync<PassingReplayPanel>(ParameterView.FromDictionary(
                    new Dictionary<string, object?>
                    {
                        [nameof(PassingReplayPanel.InstallationId)] = Guid.NewGuid(),
                        [nameof(PassingReplayPanel.Report)] = report,
                        [nameof(PassingReplayPanel.LastSeen)] = DateTime.UtcNow,
                        [nameof(PassingReplayPanel.LoopIds)] = (IReadOnlyList<string>)["L1", "L2"]
                    }));
                await output.QuiescenceTask;
                return output.ToHtmlString();
            });
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    public class HistoryService : DispatchProxy
    {
        public IReadOnlyList<PassingReplayRequestDTO> History { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IInfrastructureAdministrationService.GetPassingReplaysAsync)
                ? Task.FromResult(History)
                : throw new NotSupportedException(targetMethod?.Name);
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
