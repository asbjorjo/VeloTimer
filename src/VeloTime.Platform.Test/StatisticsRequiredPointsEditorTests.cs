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
using MudBlazor.Services;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.WebUI.Mud.Client.Pages.Admin;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;
using static VeloTime.Platform.Test.StatisticsRequiredPointsDraftTests;

namespace VeloTime.Platform.Test;

public sealed class StatisticsRequiredPointsEditorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditAndThresholdOnlySavePreserveSavedLayoutAndRequirements(bool layoutFails)
    {
        await using var test = new Harness();
        await test.Run(async () =>
        {
            test.Facilities.Fail = layoutFails;
            await test.Invoke("EditConfiguration", test.Config);
            test.Host.Refresh();
            Assert.Equal(test.Facilities.SavedLayout.Id, test.Field<Guid?>("_layoutId"));
            Assert.Equal(new[] { Id(1), Id(2) }, test.Draft.Selected);
            Assert.True(test.Tracker.HasUnsavedChanges);
            if (layoutFails)
            {
                Assert.NotNull(test.Field<string?>("_referenceError"));
                Assert.Contains("no requirements have been cleared", test.Html);
            }
            test.Set("_thresholds", new StatisticsThresholdDraft(TimeSpan.FromSeconds(10), null, 250, DisplayTestRendering.Utc));
            test.Host.Refresh();
            await test.Invoke("Save");
            var request = Assert.Single(test.Requests);
            Assert.Null(request.RequiredCoursePoints); // Preserve, not an accidental replacement/clear.
            Assert.Equal(TimeSpan.FromSeconds(10), request.MinDuration);
            Assert.Equal(Id(0), request.CoursePointStart);
            Assert.False(test.Tracker.HasUnsavedChanges);
        });
    }

    [Fact]
    public async Task NewSelectionSavesInCourseOrderAndExistingSelectionCanBeCleared()
    {
        await using var test = new Harness();
        await test.Run(async () =>
        {
            await test.Invoke("New");
            test.Set("_itemId", Id(400));
            await test.Invoke("SelectFacility", Id(300));
            await test.Invoke("SelectLayout", test.Facilities.SavedLayout.Id);
            test.Set("_startId", Id(0));
            test.Set("_endId", Id(3));
            var path = StatisticsRequiredPointsDraft.IntermediatePath(test.Facilities.SavedLayout, Id(0), Id(3))!;
            test.Draft.Toggle(Id(2), path);
            test.Draft.Toggle(Id(1), path);
            test.Host.Refresh();
            await test.Invoke("Save");
            Assert.Equal(new[] { Id(1), Id(2) }, Assert.Single(test.Requests).RequiredCoursePoints!.CoursePointIds);
            Assert.Equal(test.Facilities.SavedLayout.Id, test.Requests[0].RequiredCoursePoints!.CourseLayoutId);
            await test.Invoke("EditConfiguration", test.Config);
            test.Draft.Clear();
            test.Host.Refresh();
            await test.Invoke("Save");
            Assert.Equal(2, test.Requests.Count);
            Assert.Null(test.Requests[1].RequiredCoursePoints!.CourseLayoutId);
            Assert.Empty(test.Requests[1].RequiredCoursePoints!.CoursePointIds!);
        });
    }

    [Fact]
    public async Task EndpointChangesRequireExplicitResolutionWithoutLosingDraft()
    {
        await using var test = new Harness();
        await test.Run(async () =>
        {
            await test.Invoke("EditConfiguration", test.Config);
            test.Set("_endId", Id(2)); // A required point is now the endpoint, not intermediate.
            test.Host.Refresh();
            await test.Invoke("Save");
            Assert.Empty(test.Requests);
            Assert.NotNull(test.Field<string?>("_error"));
            Assert.Equal(new[] { Id(1), Id(2) }, test.Draft.Selected);
            Assert.True(test.Tracker.HasUnsavedChanges);
            test.Draft.Clear();
            test.Host.Refresh();
            await test.Invoke("Save");
            Assert.Empty(Assert.Single(test.Requests).RequiredCoursePoints!.CoursePointIds!);
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "Structural changes")]
    [InlineData(HttpStatusCode.Unauthorized, "sign in")]
    [InlineData(HttpStatusCode.Forbidden, "permission")]
    public async Task RejectedSaveRetainsSelectionAndUnsavedTracking(HttpStatusCode status, string message)
    {
        await using var test = new Harness();
        await test.Run(async () =>
        {
            await test.Invoke("EditConfiguration", test.Config);
            test.SaveFailure = status;
            test.Host.Refresh();
            await test.Invoke("Save");
            Assert.True(test.Tracker.HasUnsavedChanges);
            Assert.True(test.Field<bool>("_editing"));
            Assert.False(test.Field<bool>("_busy"));
            Assert.Equal(new[] { Id(1), Id(2) }, test.Draft.Selected);
            Assert.Contains(message, test.Field<string>("_error"), StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task MultiDoesNotSendRequirementsAndExplainsInheritance()
    {
        await using var test = new Harness();
        await test.Run(async () =>
        {
            var multi = new StatisticsConfigurationDTO(Id(501), Id(400), StatisticsConfigurationType.Multi, null, null, test.Config.Id, 2);
            await test.Invoke("EditConfiguration", multi);
            test.Host.Refresh();
            Assert.Contains("inherit the required intermediate points", test.Html);
            Assert.DoesNotContain("<fieldset", test.Html);
            await test.Invoke("Save");
            Assert.Null(Assert.Single(test.Requests).RequiredCoursePoints);
        });
    }

    [Fact]
    public async Task LayoutChangePreservesSelectionAndSavesExplicitReplacement()
    {
        await using var test = new Harness();
        await test.Run(async () =>
        {
            await test.Invoke("EditConfiguration", test.Config);
            await test.Invoke("SelectLayout", test.Facilities.FirstLayout.Id);
            Assert.Equal(new[] { Id(1), Id(2) }, test.Draft.Selected);
            test.Host.Refresh();
            await test.Invoke("Save");
            var replacement = Assert.Single(test.Requests).RequiredCoursePoints!;
            Assert.Equal(test.Facilities.FirstLayout.Id, replacement.CourseLayoutId);
            Assert.Equal(new[] { Id(1), Id(2) }, replacement.CoursePointIds);
        });
    }

    [Fact]
    public async Task EmptyPathAndInvalidPathAreExplicitAndNeverClearSelection()
    {
        await using var test = new Harness();
        await test.Run(async () =>
        {
            await test.Invoke("EditConfiguration", test.Config);
            test.Set("_endId", Id(1));
            test.Host.Refresh();
            Assert.Contains("There are no intermediate points", test.Html);
            Assert.Contains("no longer match", test.Html);
            Assert.DoesNotContain("mud-checkbox", test.Html);
            test.Set("_startId", Id(3));
            test.Host.Refresh();
            Assert.Contains("one continuous, unambiguous forward course", test.Html);
            Assert.Equal(new[] { Id(1), Id(2) }, test.Draft.Selected);
        });
    }

    [Fact]
    public async Task SupersededLayoutResponseCannotOverwriteNewEditor()
    {
        await using var test = new Harness();
        await test.Run(async () =>
        {
            await test.Invoke("EditConfiguration", test.Config);
            var pending = new TaskCompletionSource<CourseLayoutDetailView>(TaskCreationOptions.RunContinuationsAsynchronously);
            test.Facilities.Pending = pending;
            var loading = test.Invoke("SelectLayout", test.Facilities.FirstLayout.Id);
            Assert.True(test.Field<bool>("_referencesLoading"));
            Assert.Equal(new[] { Id(1), Id(2) }, test.Draft.Selected);
            await test.Invoke("New"); // Also invalidates any abandoned editor reference load.
            pending.SetResult(test.Facilities.FirstLayout);
            await loading;
            Assert.Null(test.Field<Guid?>("_layoutId"));
            Assert.Null(test.Field<CourseLayoutDetailView?>("_layoutDetail"));
            Assert.Empty(test.Draft.Selected);
            Assert.False(test.Field<bool>("_referencesLoading"));
        });
    }

    [Theory]
    [InlineData("en", "Required intermediate points", "Clear required points")]
    [InlineData("nb-NO", "Påkrevde mellompunkter", "Fjern påkrevde punkter")]
    public async Task SelectorRendersLocalizedAccessibleSelectionAndLoadingStates(string culture, string legend, string clear)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            await using var test = new Harness();
            await test.Run(async () =>
            {
                await test.Invoke("EditConfiguration", test.Config);
                test.Host.Refresh();
                Assert.Contains("<fieldset", test.Html);
                Assert.Contains(legend, test.Html);
                Assert.Contains(clear, test.Html);
                Assert.Contains(@"aria-live=""polite""", test.Html);
                Assert.Contains(@"type=""checkbox""", test.Html);
                Assert.Contains("<li>Point 1</li>", test.Html);
                test.Set("_referencesLoading", true);
                test.Host.Refresh();
                Assert.Contains(@"role=""status""", test.Html);
                Assert.DoesNotContain(@"type=""checkbox""", test.Html);
                Assert.Contains("<li>Point 1</li>", test.Html);
            });
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly HtmlRenderer _renderer;
        private HtmlRootComponent _root;
        public readonly FacilityStub Facilities = new();
        public readonly UnsavedChangesTracker Tracker = new();
        public readonly List<SaveStatisticsConfigurationRequest> Requests = [];
        public HttpStatusCode? SaveFailure;
        public StatisticsConfigurationDTO Config => new(Id(500), Id(400), StatisticsConfigurationType.Simple, Id(0), Id(3), null, null)
        { RequiredCoursePoints = new(Facilities.SavedLayout.Id, [Id(1), Id(2)]) };
        public StatisticsDefinitions Component = default!;
        public EditorHost Host = default!;
        public StatisticsRequiredPointsDraft Draft => Field<StatisticsRequiredPointsDraft>("_requiredPoints");
        public string Html => WebUtility.HtmlDecode(_root.ToHtmlString());

        public Harness()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLocalization(options => options.ResourcesPath = "Resources");
            services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            services.AddSingleton<IJSRuntime, NoBrowser>();
            services.AddSingleton<NavigationManager, TestNavigation>();
            services.AddSingleton(Tracker);
            services.AddSingleton<IFacilityService>(Facilities);
            services.AddSingleton(PlatformServiceTests.TestProxy.Create<IStatisticsAdministrationService>((method, args) => method.Name switch
            {
                nameof(IStatisticsAdministrationService.GetItemsAsync) => new StatisticsItemDTO[] { new(Id(400), "Distance", "", 250) },
                nameof(IStatisticsAdministrationService.GetConfigurationsAsync) => new[] { Config },
                nameof(IStatisticsAdministrationService.SaveConfigurationAsync) => Save((SaveStatisticsConfigurationRequest)args[1]!),
                _ => throw new NotSupportedException(method.Name)
            }));
            _provider = services.BuildServiceProvider();
            _renderer = new(_provider, _provider.GetRequiredService<ILoggerFactory>());
        }
        private object? Save(SaveStatisticsConfigurationRequest request)
        {
            if (SaveFailure.HasValue) throw new HttpRequestException("Rejected", null, SaveFailure);
            Requests.Add(request);
            return null;
        }
        public Task Run(Func<Task> action) => _renderer.Dispatcher.InvokeAsync(async () =>
        {
            _root = await _renderer.RenderComponentAsync<EditorHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(EditorHost.Capture)] = (Action<StatisticsDefinitions>)(value => Component = value),
                [nameof(EditorHost.CaptureHost)] = (Action<EditorHost>)(value => Host = value)
            }));
            await action();
        });
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public T Field<T>(string name) => (T)typeof(StatisticsDefinitions).GetField(name, Private)!.GetValue(Component)!;
        public void Set(string name, object? value) => typeof(StatisticsDefinitions).GetField(name, Private)!.SetValue(Component, value);
        public Task Invoke(string name, params object?[] args) =>
            (Task?)typeof(StatisticsDefinitions).GetMethod(name, Private, args.Select(arg => arg is Guid ? typeof(Guid?) : arg!.GetType()).ToArray())!
                .Invoke(Component, args) ?? Task.CompletedTask;
        public async ValueTask DisposeAsync() { await _renderer.DisposeAsync(); await _provider.DisposeAsync(); }
    }

    public sealed class EditorHost : ComponentBase
    {
        [Parameter] public Action<StatisticsDefinitions>? Capture { get; set; }
        [Parameter] public Action<EditorHost>? CaptureHost { get; set; }
        public void Refresh() => StateHasChanged();
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            CaptureHost?.Invoke(this);
            builder.OpenComponent<CascadingValue<EffectiveDisplaySettings>>(0);
            builder.AddAttribute(1, "Value", DisplayTestRendering.Utc);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(child =>
            {
                child.OpenComponent<StatisticsDefinitions>(0);
                child.AddComponentReferenceCapture(1, value => Capture?.Invoke((StatisticsDefinitions)value));
                child.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    private sealed class FacilityStub : IFacilityService
    {
        public readonly CourseLayoutDetailView FirstLayout = Layout(id: Id(100));
        public readonly CourseLayoutDetailView SavedLayout = Layout(id: Id(101));
        public bool Fail;
        public TaskCompletionSource<CourseLayoutDetailView>? Pending;
        public Task<IEnumerable<FacilityView>> GetFacilitiesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<FacilityView>>([new() { Id = Id(300), Name = "Facility" }]);
        public Task<IEnumerable<CourseLayoutView>> GetFacilityLayoutsAsync(Guid facilityId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<CourseLayoutView>>([new() { Id = FirstLayout.Id, Segments = [] }, new() { Id = SavedLayout.Id, Segments = [] }]);
        public Task<CourseLayoutDetailView> GetCourseLayoutDetailAsync(Guid layoutId, CancellationToken cancellationToken = default) =>
            Fail ? Task.FromException<CourseLayoutDetailView>(new HttpRequestException("Unavailable")) :
            Pending?.Task ?? Task.FromResult(layoutId == SavedLayout.Id ? SavedLayout : FirstLayout);
        public Task<IEnumerable<InstallationView>> GetInstallationsAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class NoBrowser : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => throw new JSDisconnectedException("Static renderer.");
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new JSDisconnectedException("Static renderer.");
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/admin/statistics/configurations");
    }
}
