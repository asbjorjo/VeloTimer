using System.Globalization;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.WebUI.Mud.Client;
using VeloTime.WebUI.Mud.Client.Pages.Admin;
using VeloTime.WebUI.Mud.Client.Pages.Statistics;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.Platform.Test;

/// <summary>Actual Razor rendering and editor lifecycle, not browser/Mud input interaction.</summary>
public sealed class HallOfFameUiTests
{
    [Theory]
    [InlineData("m/s", "12,500 m/s")]
    [InlineData("km/h", "45,000 km/h")]
    [InlineData("mph", "27,962 mph")]
    public void Presentation_ConvertsSiOnlyAtBoundaryAndUsesBrowserNumberLocale(string unit, string expected)
    {
        var entry = HallOfFameTransportTests.FullEntry;
        var display = EffectiveDisplaySettings.Create(new("m", unit, "utc", null), "Europe/Oslo", "de-DE",
            CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal(expected, HallOfFamePresentation.Value(entry, HallOfFameColumn.SpeedOfBestResult, display));
        Assert.Equal("2.147.483.770", HallOfFamePresentation.Value(entry, HallOfFameColumn.PassingCount, display));
        Assert.Equal("00:00:12.345", HallOfFamePresentation.Value(entry, HallOfFameColumn.BestTime, display));
        Assert.Equal("10/25/2026 1:30:00\u202fAM +00:00 [UTC]",
            HallOfFamePresentation.Value(entry, HallOfFameColumn.DateOfBestResult, display));
        Assert.Equal(12.5, entry.SpeedOfBestResult);
        Assert.Equal(123456789, entry.BestTime!.Value.Ticks);
        Assert.Equal(TimeSpan.Zero, entry.DateOfBestResult!.Value.Offset);
    }

    [Fact]
    public void Presentation_ExplicitZoneAndDateCultureAndMissingValuesDoNotMutateUtc()
    {
        var entry = HallOfFameTransportTests.FullEntry;
        var display = EffectiveDisplaySettings.Create(new("m", "m/s", "timeZone", "Europe/Oslo", "nb-NO"),
            "America/New_York", "en-US");
        var timestamp = HallOfFamePresentation.Value(entry, HallOfFameColumn.DateOfBestResult, display);
        Assert.Contains("25.10.2026", timestamp);
        Assert.Contains("02:30:00 +01:00 [Europe/Oslo]", timestamp);
        Assert.Equal(TimeSpan.Zero, entry.DateOfBestResult!.Value.Offset);
        var empty = new HallOfFameEntryDTO(1, "Rider", null, null, null, null, null);
        Assert.All(Enum.GetValues<HallOfFameColumn>(), column =>
            Assert.Equal("-", HallOfFamePresentation.Value(empty, column, display)));
    }

    [Fact]
    public void Draft_SnapshotIsIsolatedAndBlankRowCountIsInvalid()
    {
        var columns = new List<HallOfFameColumn> { HallOfFameColumn.PassingCount };
        var original = new HallOfFameSectionSettingsDTO(HallOfFameTransportTests.Item, HallOfFameRankingMode.BestTime, true, 10, columns);
        var draft = new HallOfFameSectionDraft(original);
        var snapshot = draft.Snapshot();
        columns.Clear();
        draft.Columns.Add(HallOfFameColumn.BestTime);
        Assert.Equal(new[] { HallOfFameColumn.PassingCount }, snapshot.Columns);
        Assert.Equal(2, draft.Columns.Count);
        draft.RowCount = null;
        Assert.NotEmpty(HallOfFameQuery.Validate(new(0, [draft.Snapshot()])));
    }

    [Fact]
    public async Task PublicRazor_RendersSavedSectionAndColumnOrderFixedHeadersEscapingAndFocusableTable()
    {
        await using var harness = await Harness<HallOfFame>.CreateAsync(results: HallOfFameTransportTests.Results());
        var html = await harness.HtmlAsync();
        Assert.True(html.IndexOf("Count first", StringComparison.Ordinal) < html.IndexOf("Time second", StringComparison.Ordinal));
        Assert.Contains("<thead", html);
        var firstHead = html[html.IndexOf("<thead", StringComparison.Ordinal)..html.IndexOf("</thead>", StringComparison.Ordinal)];
        var headers = new[] { "Rank", "Name", "Completed-result count", "Date of best result", "Best time", "Speed of best result", "Facility of best result" };
        var positions = headers.Select(value => firstHead.IndexOf(">" + value + "<", StringComparison.Ordinal)).ToArray();
        Assert.All(positions, position => Assert.True(position >= 0, firstHead));
        Assert.Equal(positions.Order(), positions);
        Assert.Contains("&lt;Rider&gt;", html);
        Assert.DoesNotContain("<Rider>", html);
        Assert.Contains("scope=\"row\"", html);
        Assert.Contains("tabindex=\"0\"", html);
        Assert.Contains("overflow-x:auto", html);
        Assert.Equal(2, html.Split("<table ").Length - 1);
        Assert.Equal(1, harness.StatisticsCalls);
    }

    [Theory]
    [InlineData("none", "No Hall of Fame sections")]
    [InlineData("empty", "No completed results from public profiles")]
    [InlineData("500", "settings")]
    [InlineData("503", "Retry")]
    public async Task PublicRazor_HasDistinctEmptyAndRetryStates(string scenario, string expected)
    {
        var results = scenario == "empty"
            ? new HallOfFameDTO([new(HallOfFameTransportTests.Item, "Lap", HallOfFameRankingMode.BestTime, 10, [], [])])
            : new HallOfFameDTO([]);
        await using var harness = await Harness<HallOfFame>.CreateAsync(results: results,
            publicFailure: int.TryParse(scenario, out var status) ? (HttpStatusCode)status : null);
        var html = await harness.HtmlAsync();
        Assert.Contains(expected, html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<table ", html);
        if (status != 0) Assert.Contains("role=\"alert\"", html);
    }

    [Fact]
    public async Task Editor_AddsDefault10BothModesAndSavesWholeOrderedDocumentIncludingDisabledSections()
    {
        await using var harness = await Harness<HallOfFameSettings>.CreateAsync();
        Assert.False(harness.Tracker.HasUnsavedChanges);
        await harness.CallAsync("AddSection");
        await harness.CallAsync("AddSection");
        var sections = harness.Field<List<HallOfFameSectionDraft>>("_sections");
        Assert.Equal(new[] { HallOfFameRankingMode.BestTime, HallOfFameRankingMode.MostPassings }, sections.Select(section => section.RankingMode));
        Assert.All(sections, section => Assert.Equal(10, section.RowCount));
        Assert.True(harness.Tracker.HasUnsavedChanges);
        await harness.CallAsync("AddSection");
        Assert.Equal(2, sections.Count);
        sections[0].IsEnabled = false;
        await harness.CallAsync("ToggleColumn", sections[0], HallOfFameColumn.BestTime, true);
        await harness.CallAsync("ToggleColumn", sections[0], HallOfFameColumn.PassingCount, true);
        await harness.CallAsync("MoveColumn", sections[0], HallOfFameColumn.PassingCount, -1);
        await harness.CallAsync("MoveSection", sections[1], -1);
        await harness.SaveWithoutInteractiveFormAsync();
        var saved = Assert.Single(harness.Saves);
        Assert.Equal(7, saved.ExpectedVersion);
        Assert.Equal(HallOfFameRankingMode.MostPassings, saved.Sections[0].RankingMode);
        Assert.False(saved.Sections[1].IsEnabled);
        Assert.Equal(new[] { HallOfFameColumn.PassingCount, HallOfFameColumn.BestTime }, saved.Sections[1].Columns);
        Assert.False(harness.Tracker.HasUnsavedChanges);
        Assert.Equal(8, harness.Field<long>("_version"));
        Assert.True(harness.Field<bool>("_saved"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(10, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public async Task Editor_SharedDraftValidationEnforcesRequired1To100WithoutLosingEdits(int? rows, bool valid)
    {
        await using var harness = await Harness<HallOfFameSettings>.CreateAsync();
        await harness.CallAsync("AddSection");
        var section = Assert.Single(harness.Field<List<HallOfFameSectionDraft>>("_sections"));
        section.RowCount = rows;
        await harness.SaveWithoutInteractiveFormAsync();
        Assert.Equal(valid ? 1 : 0, harness.Saves.Count);
        Assert.Equal(!valid, harness.Tracker.HasUnsavedChanges);
        if (!valid)
        {
            Assert.Same(section, Assert.Single(harness.Field<List<HallOfFameSectionDraft>>("_sections")));
            Assert.Equal("HallOfFameValidationFailed", harness.Field<string>("_error"));
        }
    }

    [Fact]
    public async Task Editor_ConflictBlocksBlindRetryFailedReloadKeepsDraftAndSuccessfulReloadResetsVersion()
    {
        await using var harness = await Harness<HallOfFameSettings>.CreateAsync();
        await harness.CallAsync("AddSection");
        harness.SaveFailure = new HttpRequestException("Conflict", null, HttpStatusCode.Conflict);
        await harness.SaveWithoutInteractiveFormAsync();
        Assert.True(harness.Field<bool>("_conflict"));
        Assert.True(harness.Tracker.HasUnsavedChanges);
        await harness.SaveWithoutInteractiveFormAsync();
        Assert.Single(harness.Saves);
        var draft = harness.Field<List<HallOfFameSectionDraft>>("_sections");
        harness.LoadFailure = new HttpRequestException("Unavailable", null, HttpStatusCode.ServiceUnavailable);
        // LoadAsync is the post-confirmation operation; this does not simulate the discard dialog.
        await harness.CallAsync("LoadAsync");
        Assert.Same(draft, harness.Field<List<HallOfFameSectionDraft>>("_sections"));
        Assert.True(harness.Tracker.HasUnsavedChanges);
        Assert.True(harness.Field<bool>("_conflict"));
        harness.LoadFailure = null;
        harness.Settings = new(12, []);
        await harness.CallAsync("LoadAsync");
        Assert.False(harness.Tracker.HasUnsavedChanges);
        Assert.False(harness.Field<bool>("_conflict"));
        Assert.Equal(12, harness.Field<long>("_version"));
        harness.SaveFailure = null;
        await harness.CallAsync("AddSection");
        await harness.SaveWithoutInteractiveFormAsync();
        Assert.Equal(12, harness.Saves[^1].ExpectedVersion);
    }

    [Fact]
    public async Task Editor_NetworkFailureRetainsVersionDraftAndDisposalUnregistersDirtyTracker()
    {
        await using var harness = await Harness<HallOfFameSettings>.CreateAsync();
        await harness.CallAsync("AddSection");
        var draft = harness.Field<List<HallOfFameSectionDraft>>("_sections");
        harness.SaveFailure = new HttpRequestException("Network");
        await harness.SaveWithoutInteractiveFormAsync();
        Assert.Same(draft, harness.Field<List<HallOfFameSectionDraft>>("_sections"));
        Assert.Equal(7, harness.Field<long>("_version"));
        Assert.True(harness.Tracker.HasUnsavedChanges);
        await harness.SaveWithoutInteractiveFormAsync();
        Assert.Equal(new long[] { 7, 7 }, harness.Saves.Select(save => save.ExpectedVersion));
        Assert.False(harness.Field<bool>("_conflict"));
        await harness.DisposeRendererAsync();
        Assert.False(harness.Tracker.HasUnsavedChanges);
    }

    private sealed class Harness<T> : IAsyncDisposable where T : IComponent
    {
        private readonly ServiceProvider provider;
        private readonly HtmlRenderer renderer;
        private Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent root;
        private bool disposed;
        private T component = default!;
        internal UnsavedChangesTracker Tracker { get; } = new();
        internal HallOfFameSettingsDTO Settings { get; set; } = new(7, []);
        internal Exception? SaveFailure { get; set; }
        internal Exception? LoadFailure { get; set; }
        internal List<SaveHallOfFameSettingsRequest> Saves { get; } = [];
        internal int StatisticsCalls { get; private set; }

        private Harness(HallOfFameDTO results, HttpStatusCode? failure)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLocalization(options => options.ResourcesPath = "Resources");
            services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            services.AddTransient<MudLocalizer, LocalizedMudLocalizer>();
            services.AddSingleton<IJSRuntime, NoJavaScript>();
            services.AddSingleton<NavigationManager, TestNavigation>();
            services.AddSingleton(Tracker);
            services.AddSingleton(PlatformServiceTests.TestProxy.Create<IStatisticsService>((method, _) =>
            {
                Assert.Equal(nameof(IStatisticsService.GetHallOfFameAsync), method.Name);
                StatisticsCalls++;
                if (failure.HasValue) throw new HttpRequestException("Unavailable", null, failure);
                return results;
            }));
            services.AddSingleton(PlatformServiceTests.TestProxy.Create<IStatisticsAdministrationService>((method, args) =>
            {
                if (method.Name == nameof(IStatisticsAdministrationService.SaveHallOfFameSettingsAsync))
                {
                    var request = Assert.IsType<SaveHallOfFameSettingsRequest>(args[0]);
                    Saves.Add(request);
                    if (SaveFailure is not null) throw SaveFailure;
                    return Settings = new(request.ExpectedVersion + 1, request.Sections);
                }
                if (LoadFailure is not null) throw LoadFailure;
                return method.Name switch
                {
                    nameof(IStatisticsAdministrationService.GetItemsAsync) =>
                        (IReadOnlyList<StatisticsItemDTO>)new[] { new StatisticsItemDTO(HallOfFameTransportTests.Item, "Lap", "", 250) },
                    nameof(IStatisticsAdministrationService.GetHallOfFameSettingsAsync) => (object)Settings,
                    _ => throw new NotSupportedException(method.Name)
                };
            }));
            provider = services.BuildServiceProvider();
            renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        }

        internal static async Task<Harness<T>> CreateAsync(HallOfFameDTO? results = null, HttpStatusCode? publicFailure = null)
        {
            var harness = new Harness<T>(results ?? new([]), publicFailure);
            var capture = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(CaptureHost<T>.Capture)] = (Action<T>)(component => harness.component = component)
            });
            harness.root = await harness.renderer.Dispatcher.InvokeAsync(() =>
                harness.renderer.RenderComponentAsync<CascadingValue<EffectiveDisplaySettings>>(
                    DisplayTestRendering.Parameters<CaptureHost<T>>(capture)));
            return harness;
        }

        internal V Field<V>(string name) => (V)typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
        internal Task CallAsync(string name, params object?[] args) => renderer.Dispatcher.InvokeAsync(async () =>
        {
            var value = typeof(T).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args);
            if (value is Task task) await task;
        });
        internal async Task SaveWithoutInteractiveFormAsync()
        {
            // Static HtmlRenderer cannot exercise numeric input parsing/MudForm interactivity.
            await renderer.Dispatcher.InvokeAsync(() => typeof(T).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, null));
            await CallAsync("SaveAsync");
        }
        internal Task<string> HtmlAsync() => renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        internal async Task DisposeRendererAsync()
        {
            if (disposed) return;
            disposed = true;
            await renderer.DisposeAsync();
        }
        public async ValueTask DisposeAsync() { await DisposeRendererAsync(); await provider.DisposeAsync(); }
    }

    public sealed class CaptureHost<T> : ComponentBase where T : IComponent
    {
        [Parameter] public Action<T> Capture { get; set; } = null!;
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<T>(0);
            builder.AddComponentReferenceCapture(1, value => Capture((T)value));
            builder.CloseComponent();
        }
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://webui.invalid/", "https://webui.invalid/");
    }
    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new JSDisconnectedException("Static renderer");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken token, object?[]? args) =>
            throw new JSDisconnectedException("Static renderer");
    }
}
