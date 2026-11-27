using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.WebUI.Mud.Client.Pages.Admin;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.Platform.Test;

/// <summary>Admin recompute page (replay-safe plan Phase 5).</summary>
public sealed class StatisticsRecomputePageTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public async Task PageShowsBacklogAndJobProgressAndEscapesNotes()
    {
        await using var test = new Harness();
        await test.Run(() =>
        {
            Assert.Equal(1, test.Loads);
            var html = test.Html;
            Assert.Contains("recompute-backlog", html);
            Assert.Contains("Passings without a facility or course point", html);
            Assert.Contains("7 of 12 transponders", html);
            Assert.Contains("1 retrying", html);
            Assert.Contains("HttpRequestException: down", html);
            Assert.Contains("&lt;b&gt;after layout fix&lt;/b&gt;", html);
            Assert.DoesNotContain("<b>after layout fix</b>", html);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task AllHistoryNeedsConfirmationAndScopedJobsSendTheParsedId()
    {
        await using var test = new Harness();
        await test.Run(async () =>
        {
            test.Set("_fromUnbounded", true);
            Assert.True(test.Get<bool>("AllHistory"));

            test.Set("_scope", Enum.Parse(test.ScopeType, "Installation"));
            test.Set("_scopeId", "not-a-guid");
            await test.Invoke("Submit");
            Assert.Empty(test.Created);
            Assert.Equal("Enter a valid ID.", test.Get<string>("_formError"));

            var installation = Guid.NewGuid();
            test.Set("_scopeId", $" {installation} ");
            test.Set("_note", "  ");
            await test.Invoke("Submit");
            Assert.Equal(new CreateRecomputeJobRequest(null, null, null, installation, null), Assert.Single(test.Created));
            Assert.Null(test.Get<string>("_formError"));
            Assert.Equal(2, test.Loads);
        });
    }

    [Fact]
    public async Task BoundedRangeIsResolvedWithDisplaySettingsAndBackendErrorsAreShown()
    {
        await using var test = new Harness { CreateError = new HttpRequestException("ToUtc must be after FromUtc.", null, System.Net.HttpStatusCode.BadRequest) };
        await test.Run(async () =>
        {
            var from = test.Get<ZonedInstantDraft>("_fromDraft");
            from.Date = new DateTime(2026, 11, 20); from.Time = TimeSpan.FromHours(18); from.Changed();
            test.Set("_toUnbounded", false);
            var to = test.Get<ZonedInstantDraft>("_toDraft");
            to.Date = new DateTime(2026, 11, 20); to.Time = TimeSpan.FromHours(17); to.Changed();
            await test.Invoke("Submit");
            Assert.Empty(test.Created);
            Assert.Equal("The end must be after the start.", test.Get<string>("_formError"));

            to.Date = new DateTime(2026, 11, 21); to.Changed();
            await test.Invoke("Submit");
            var request = Assert.Single(test.Created);
            // The display preference is UTC, so wall times are UTC instants.
            Assert.Equal(new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.Zero), request.FromUtc);
            Assert.Equal(new DateTimeOffset(2026, 11, 21, 17, 0, 0, TimeSpan.Zero), request.ToUtc);
            Assert.Equal("ToUtc must be after FromUtc.", test.Get<string>("_formError"));
        });
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly HtmlRenderer renderer;
        private HtmlRootComponent root;
        private StatisticsRecompute component = default!;
        public readonly List<CreateRecomputeJobRequest> Created = [];
        public int Loads;
        public Exception? CreateError { get; init; }
        public Type ScopeType => typeof(StatisticsRecompute).GetNestedType("RecomputeScope", BindingFlags.NonPublic)!;
        public string Html => root.ToHtmlString();

        public Harness()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLocalization(options => options.ResourcesPath = "Resources");
            services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            services.AddSingleton<IJSRuntime, NoBrowser>();
            services.AddSingleton<NavigationManager, TestNavigation>();
            var job = StatisticsRecomputeTransportTests.Job() with { Note = "<b>after layout fix</b>", Status = RecomputeJobStatus.Completed };
            services.AddSingleton(PlatformServiceTests.TestProxy.Create<IStatisticsAdministrationService>((method, args) =>
            {
                switch (method.Name)
                {
                    case nameof(IStatisticsAdministrationService.GetRecomputeOverviewAsync):
                        Loads++;
                        // Nothing pending, so the page does not start polling.
                        return new RecomputeOverviewDTO(new RecomputeBacklogDTO(0, 0, 0, null, null, [], 3, 0, null), [job]);
                    case nameof(IStatisticsAdministrationService.CreateRecomputeJobAsync):
                        Created.Add((CreateRecomputeJobRequest)args[0]!);
                        if (CreateError is not null) throw CreateError;
                        return job;
                    default: throw new NotSupportedException(method.Name);
                }
            }));
            provider = services.BuildServiceProvider();
            renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
            Settings = EffectiveDisplaySettings.Create(new("m", "km/h", "utc", null), "Europe/Oslo", "en-US", CultureInfo.GetCultureInfo("en-US"));
        }

        private EffectiveDisplaySettings Settings { get; }

        public Task Run(Func<Task> action) => renderer.Dispatcher.InvokeAsync(async () =>
        {
            root = await renderer.RenderComponentAsync<PageHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(PageHost.Capture)] = (Action<StatisticsRecompute>)(value => component = value),
                [nameof(PageHost.Settings)] = Settings
            }));
            await root.QuiescenceTask;
            await action();
        });

        public void Set(string name, object? value) => typeof(StatisticsRecompute).GetField(name, Hidden)!.SetValue(component, value);
        public T Get<T>(string name) =>
            (T)(typeof(StatisticsRecompute).GetField(name, Hidden)?.GetValue(component)
                ?? typeof(StatisticsRecompute).GetProperty(name, Hidden)?.GetValue(component))!;
        public Task Invoke(string name) => (Task?)typeof(StatisticsRecompute).GetMethod(name, Hidden)!.Invoke(component, null) ?? Task.CompletedTask;
        public async ValueTask DisposeAsync() { await renderer.DisposeAsync(); await provider.DisposeAsync(); }
    }

    public sealed class PageHost : ComponentBase
    {
        [Parameter] public Action<StatisticsRecompute>? Capture { get; set; }
        [Parameter] public EffectiveDisplaySettings Settings { get; set; } = default!;
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<EffectiveDisplaySettings>>(0);
            builder.AddAttribute(1, "Value", Settings);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(child =>
            {
                child.OpenComponent<StatisticsRecompute>(0);
                child.AddComponentReferenceCapture(1, value => Capture?.Invoke((StatisticsRecompute)value));
                child.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    private sealed class NoBrowser : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => throw new JSDisconnectedException("Static renderer.");
        public ValueTask<T> InvokeAsync<T>(string id, CancellationToken token, object?[]? args) => throw new JSDisconnectedException("Static renderer.");
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/admin/statistics/recompute");
    }
}
