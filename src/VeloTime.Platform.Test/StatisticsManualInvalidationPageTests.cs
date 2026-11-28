using System.Globalization;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.WebUI.Mud.Client.Pages.Admin;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.Platform.Test;

public sealed class StatisticsManualInvalidationPageTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DialogCancellationDoesNotWriteAndConfirmationForwardsNoteThenRefreshes(bool cancel)
    {
        await using var test = new Harness(cancel);
        await test.Run(async () =>
        {
            Assert.Single(test.Queries);
            await test.Invoke("ChangeManualState", test.Entry);
            if (cancel)
            {
                Assert.Empty(test.Changes);
                Assert.Single(test.Queries);
            }
            else
            {
                var saved = Assert.Single(test.Changes);
                Assert.False(saved.IsManuallyInvalidated);
                Assert.Equal("Reviewed note", saved.Note);
                Assert.Equal(2, test.Queries.Count);
                Assert.Equal(test.Queries[0], test.Queries[1]);
            }
        });
    }

    [Fact]
    public async Task ApplyingFiltersClearsCursorAndPreviousNextKeepSelections()
    {
        await using var test = new Harness(true);
        await test.Run(async () =>
        {
            var facility = Guid.NewGuid();
            var item = Guid.NewGuid();
            var config = Guid.NewGuid();
            test.Set("_facilityId", facility);
            test.Set("_itemId", item);
            test.Set("_configurationId", config);
            test.Set("_isValid", false);
            await test.Invoke("ApplyFilters");
            var filter = test.Queries[^1];
            Assert.Equal(new(facility, item, config, IsValid: false), filter);
            await test.Invoke("Next");
            Assert.Equal(filter with { Cursor = test.Cursor, Direction = AdminStatisticsEntriesPageDirection.Next }, test.Queries[^1]);
            await test.Invoke("Previous");
            Assert.Equal(filter with { Cursor = test.Cursor, Direction = AdminStatisticsEntriesPageDirection.Previous }, test.Queries[^1]);
            await test.Invoke("ApplyFilters");
            Assert.Equal(filter, test.Queries[^1]);
            await test.Invoke("SelectItem", Guid.NewGuid());
            Assert.Null(test.Field<Guid?>("_configurationId"));
        });
    }

    [Theory]
    [InlineData("en-US", "55.923 mph")]
    [InlineData("nb-NO", "55,923 mph")]
    public async Task PageFormatsSiSpeedUsingExplicitDisplayPreferencesAndEscapesAuditNote(string locale, string expectedSpeed)
    {
        await using var test = new Harness(true, locale);
        await test.Run(() =>
        {
            Assert.Contains(expectedSpeed, test.Html);
            Assert.Contains("<script>audit</script>", WebUtility.HtmlDecode(test.Html));
            Assert.DoesNotContain("<script>audit</script>", test.Html);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(true, "  Reviewed ø  ", "Reviewed ø")]
    [InlineData(false, "   ", null)]
    public void NoteDialogBuildsOppositeStateAndTrimsOptionalNote(bool active, string note, string? expected)
    {
        DialogResult? result = null;
        var dialog = PlatformServiceTests.TestProxy.Create<IMudDialogInstance>((method, args) =>
        {
            Assert.Equal("Close", method.Name);
            result = Assert.IsType<DialogResult>(args[0]);
            return null;
        });
        var component = new StatisticsEntryInvalidationDialog();
        typeof(StatisticsEntryInvalidationDialog).GetProperty("Entry")!.SetValue(component,
            StatisticsManualInvalidationTransportTests.Entry() with { IsManuallyInvalidated = active });
        typeof(StatisticsEntryInvalidationDialog).GetProperty("Dialog", Hidden)!.SetValue(component, dialog);
        typeof(StatisticsEntryInvalidationDialog).GetField("_note", Hidden)!.SetValue(component, note);
        typeof(StatisticsEntryInvalidationDialog).GetMethod("Confirm", Hidden)!.Invoke(component, null);
        Assert.NotNull(result);
        Assert.False(result.Canceled);
        Assert.Equal(new StatisticsEntryManualInvalidationRequest(!active, expected), Assert.IsType<StatisticsEntryManualInvalidationRequest>(result.Data));
    }

    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly HtmlRenderer renderer;
        private HtmlRootComponent root;
        private StatisticsEntries component = default!;
        public readonly List<GetAdminStatisticsEntriesRequest> Queries = [];
        public readonly List<StatisticsEntryManualInvalidationRequest> Changes = [];
        public readonly AdminStatisticsEntryDTO Entry = StatisticsManualInvalidationTransportTests.Entry() with
        { SpeedMetresPerSecond = 25, ManualInvalidationNote = "<script>audit</script>" };
        public AdminStatisticsEntryCursorDTO Cursor => new(Entry.TimeEnd, Entry.Id);
        public string Html => root.ToHtmlString();

        public Harness(bool cancel, string locale = "en-US")
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLocalization(options => options.ResourcesPath = "Resources");
            services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            services.AddSingleton<IJSRuntime, NoBrowser>();
            services.AddSingleton<NavigationManager, TestNavigation>();
            services.AddSingleton(PlatformServiceTests.TestProxy.Create<IFacilityService>((_, _) => Array.Empty<FacilityView>()));
            services.AddSingleton(PlatformServiceTests.TestProxy.Create<IStatisticsAdministrationService>((method, args) =>
            {
                switch (method.Name)
                {
                    case nameof(IStatisticsAdministrationService.GetItemsAsync): return Array.Empty<StatisticsItemDTO>();
                    case nameof(IStatisticsAdministrationService.GetConfigurationsAsync): return Array.Empty<StatisticsConfigurationDTO>();
                    case nameof(IStatisticsAdministrationService.ListEntriesAsync):
                        Queries.Add((GetAdminStatisticsEntriesRequest)args[0]!);
                        return new AdminStatisticsEntriesPageDTO([Entry], Cursor, Cursor);
                    case nameof(IStatisticsAdministrationService.SetManualInvalidationAsync):
                        Assert.Equal(Entry.Id, args[0]);
                        Changes.Add((StatisticsEntryManualInvalidationRequest)args[1]!);
                        return Entry;
                    default: throw new NotSupportedException(method.Name);
                }
            }));
            var reference = PlatformServiceTests.TestProxy.Create<IDialogReference>((method, _) =>
            {
                Assert.Equal("get_Result", method.Name);
                return cancel ? DialogResult.Cancel() : DialogResult.Ok(new StatisticsEntryManualInvalidationRequest(false, "Reviewed note"));
            });
            services.AddSingleton(PlatformServiceTests.TestProxy.Create<IDialogService>((method, _) =>
            {
                Assert.Equal("ShowAsync", method.Name);
                return reference;
            }));
            provider = services.BuildServiceProvider();
            renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
            Settings = EffectiveDisplaySettings.Create(new("m", "mph", "utc", null), "Europe/Oslo", locale, CultureInfo.GetCultureInfo(locale));
        }
        private EffectiveDisplaySettings Settings { get; }
        public Task Run(Func<Task> action) => renderer.Dispatcher.InvokeAsync(async () =>
        {
            root = await renderer.RenderComponentAsync<PageHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(PageHost.Capture)] = (Action<StatisticsEntries>)(value => component = value),
                [nameof(PageHost.Settings)] = Settings
            }));
            await action();
        });
        public void Set(string name, object? value) => typeof(StatisticsEntries).GetField(name, Hidden)!.SetValue(component, value);
        public T Field<T>(string name) => (T)typeof(StatisticsEntries).GetField(name, Hidden)!.GetValue(component)!;
        public Task Invoke(string name, params object?[] args) =>
            (Task?)typeof(StatisticsEntries).GetMethod(name, Hidden)!.Invoke(component, args) ?? Task.CompletedTask;
        public async ValueTask DisposeAsync() { await renderer.DisposeAsync(); await provider.DisposeAsync(); }
    }

    public sealed class PageHost : ComponentBase
    {
        [Parameter] public Action<StatisticsEntries>? Capture { get; set; }
        [Parameter] public EffectiveDisplaySettings Settings { get; set; } = default!;
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<EffectiveDisplaySettings>>(0);
            builder.AddAttribute(1, "Value", Settings);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(child =>
            {
                child.OpenComponent<StatisticsEntries>(0);
                child.AddComponentReferenceCapture(1, value => Capture?.Invoke((StatisticsEntries)value));
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
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/admin/statistics/entries");
    }
}
