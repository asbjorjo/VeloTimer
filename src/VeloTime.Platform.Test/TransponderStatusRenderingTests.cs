using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Microsoft.Extensions.Localization;
using MudBlazor;
using MudBlazor.Services;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;
using StatusPage = VeloTime.WebUI.Mud.Client.Pages.TransponderStatus;

namespace VeloTime.Platform.Test;

public sealed class TransponderStatusRenderingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Page_RendersIdentityLocationsWarningsAndScopedOwnership(bool administration)
    {
        var view = TransponderStatusTestData.View(administration);
        var html = await Render(view);
        Assert.Contains("Training label", html);
        Assert.Contains("HT-24422", html);
        Assert.Contains("106187718", html);
        Assert.Contains("Arena / Track installation / Finish", html);
        Assert.Matches(@"10/30/2026 12:00:00\sPM \+00:00 \[UTC\]", System.Net.WebUtility.HtmlDecode(html));
        Assert.Contains("Low battery", html);
        Assert.Contains("Low signal strength", html);
        Assert.Contains("Low hit count", html);
        Assert.Contains("Profile rider", html);
        if (administration)
        {
            Assert.Contains("KeycloakFirst KeycloakLast", html);
        }
        else
        {
            Assert.Contains("Your ownership history", html);
            Assert.DoesNotContain("KeycloakFirst", html);
            Assert.DoesNotContain(TransponderStatusTestData.OwnerId.ToString(), html);
        }
        Assert.Equal("/transponders/{Id:guid}", Assert.Single(typeof(StatusPage).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template);
        Assert.Single(typeof(StatusPage).GetCustomAttributes(typeof(AuthorizeAttribute), true));
    }

    [Fact]
    public async Task Page_DistinguishesUnseenUnassignedAndReportedHealth()
    {
        var view = TransponderStatusTestData.View();
        var unseen = view with { Status = view.Status with { LastSeen = null, Warnings = [], WarningCount = 0,
            LowBattery = false, LowStrength = false, LowHits = false, Ownerships = [], OwnershipCount = 0 } };
        var empty = await Render(unseen);
        Assert.Contains("No passings recorded.", empty);
        Assert.Contains("No warnings reported in the last 7 days", empty);
        Assert.Contains("not a guarantee", empty);
        Assert.Contains("No ownership history recorded.", empty);
        var unassigned = view.Status.LastSeen! with { FacilityId = Guid.Empty };
        var html = await Render(view with { Status = view.Status with { LastSeen = unassigned, Warnings = [unassigned] } });
        Assert.Contains("Unassigned facility", html);
        Assert.DoesNotContain("Facility name unavailable", html);
    }

    [Fact]
    public async Task Page_NameFailuresRemainExplicitWithoutHidingStatus()
    {
        var view = TransponderStatusTestData.View();
        var html = await Render(view with { FacilityNames = new Dictionary<Guid, string>(), FacilityError = "Facility lookup failed." },
            failOwners: true);
        Assert.Contains("Some facility names could not be loaded. Retry to resolve assigned facilities.", html);
        Assert.Contains("Facility name unavailable", html);
        Assert.Contains("Owner names could not be loaded", html);
        Assert.Contains("Retry owner names", html);
        Assert.Contains("Training label", html);
        Assert.Contains("Finish", html);
        Assert.DoesNotContain("User unavailable", html);
    }

    [Fact]
    public async Task SamplePage_LinksDisplayedTransponderIdsToTheSharedStatusRoute()
    {
        var id = Guid.NewGuid();
        var services = NavigationServices(new StatusServiceStub((_, _, _, _) => throw new InvalidOperationException()));
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<IStatisticsService>((_, _) =>
            new[] { new SampleView { TransponderId = id, TransponderLabel = id.ToString(), StartPoint = "Start", EndPoint = "Finish" } }));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<CascadingValue<EffectiveDisplaySettings>>(DisplayTestRendering.Parameters<VeloTime.WebUI.Mud.Client.Pages.Statistics.Samples>())).ToHtmlString());
        Assert.Contains($"href=\"/transponders/{id}\"", html);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "Transponder not found or not available")]
    [InlineData(HttpStatusCode.Forbidden, "Sign in with an account allowed")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Transponder status could not be loaded")]
    public async Task Page_DenialAndOutageDoNotRenderPrivateStatus(HttpStatusCode code, string message)
    {
        var html = await Render(TransponderStatusTestData.View(), statusError: code);
        Assert.Contains(message, html);
        Assert.Contains("Retry status", html);
        Assert.DoesNotContain("Training label", html);
        Assert.DoesNotContain("HT-24422", html);
        Assert.DoesNotContain("Arena", html);
    }

    [Fact]
    public async Task Page_CancelsOldNavigationAndDoesNotRenderItsLatePrivateResult()
    {
        var oldView = TransponderStatusTestData.View();
        oldView = oldView with { Status = oldView.Status with { Label = "Old private transponder" } };
        var newView = TransponderStatusTestData.View();
        newView = newView with { Status = newView.Status with { Label = "New transponder" } };
        var first = new TaskCompletionSource<TransponderStatusView>();
        var second = new TaskCompletionSource<TransponderStatusView>();
        CancellationToken firstToken = default;
        var service = new StatusServiceStub((id, _, _, token) =>
        {
            if (id == oldView.Status.Id) { firstToken = token; return first.Task; }
            Assert.Equal(newView.Status.Id, id);
            return second.Task;
        });
        await using var provider = NavigationServices(service).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        StatusHost? host = null;
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.BeginRenderingComponent<StatusHost>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(StatusHost.InitialId)] = oldView.Status.Id,
                [nameof(StatusHost.Capture)] = (Action<StatusHost>)(value => host = value)
            })));
        await renderer.Dispatcher.InvokeAsync(() => host!.Navigate(newView.Status.Id));
        Assert.True(firstToken.IsCancellationRequested);
        second.SetResult(newView);
        first.SetResult(oldView);
        await root.QuiescenceTask;
        var html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Contains("New transponder", html);
        Assert.DoesNotContain("Old private transponder", html);
    }

    [Fact]
    public async Task Page_PagesHistoriesIndependentlyAndClearsStatusWhenAccessIsRevoked()
    {
        var view = TransponderStatusTestData.View();
        view = view with { Status = view.Status with { OwnershipCount = 11, WarningCount = 11 } };
        var calls = new List<(int Owners, int Warnings)>();
        var denied = false;
        var service = new StatusServiceStub((_, owners, warnings, _) =>
        {
            calls.Add((owners, warnings));
            if (denied) throw new HttpRequestException("expired", null, HttpStatusCode.NotFound);
            return Task.FromResult(view);
        });
        await using var provider = NavigationServices(service).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        StatusHost? host = null;
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<StatusHost>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(StatusHost.InitialId)] = view.Status.Id,
                [nameof(StatusHost.Capture)] = (Action<StatusHost>)(value => host = value)
            })));
        await Invoke("NextWarnings");
        await Invoke("NextOwners");
        Assert.Equal(new[] { (0, 0), (0, 1), (1, 1) }, calls);
        denied = true;
        await Invoke("Load");
        var html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Contains("Transponder not found or not available", html);
        Assert.DoesNotContain("Training label", html);
        denied = false;
        await Invoke("Load");
        Assert.Contains("Training label", await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));

        Task Invoke(string method) => renderer.Dispatcher.InvokeAsync(() =>
            EventCallback.Factory.Create(host!.Page, () =>
                (Task)typeof(StatusPage).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(host.Page, null)!).InvokeAsync());
    }

    private static ServiceCollection NavigationServices(ITransponderStatusService service)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
        services.AddTransient<MudLocalizer, LocalizedMudLocalizer>();
        services.AddSingleton<IJSRuntime, NoJavaScript>();
        services.AddSingleton<NavigationManager, TestNavigation>();
        services.AddSingleton<UnsavedChangesTracker>();
        services.AddSingleton(service);
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<ITransponderOwnerService>((_, _) =>
            (IReadOnlyList<TransponderOwnerView>)Array.Empty<TransponderOwnerView>()));
        return services;
    }

    public sealed class StatusHost : ComponentBase
    {
        [Parameter] public Guid InitialId { get; set; }
        [Parameter] public Action<StatusHost>? Capture { get; set; }
        public StatusPage Page { get; private set; } = null!;
        private Guid _id;
        protected override void OnInitialized() { _id = InitialId; Capture?.Invoke(this); }
        public void Navigate(Guid id) { _id = id; StateHasChanged(); }
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<EffectiveDisplaySettings>>(0);
            builder.AddAttribute(1, nameof(CascadingValue<EffectiveDisplaySettings>.Value), DisplayTestRendering.Utc);
            builder.AddAttribute(2, nameof(CascadingValue<EffectiveDisplaySettings>.ChildContent), (RenderFragment)(child =>
            {
                child.OpenComponent<StatusPage>(0);
                child.AddAttribute(1, nameof(StatusPage.Id), _id);
                child.AddComponentReferenceCapture(2, component => Page = (StatusPage)component);
                child.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    private sealed class StatusServiceStub(Func<Guid, int, int, CancellationToken, Task<TransponderStatusView>> handler) : ITransponderStatusService
    {
        public Task<TransponderStatusView> GetAsync(Guid id, int ownershipPage = 0, int warningPage = 0,
            int pageSize = 10, CancellationToken cancellationToken = default) =>
            handler(id, ownershipPage, warningPage, cancellationToken);
    }

    private static async Task<string> Render(TransponderStatusView view, bool failOwners = false, HttpStatusCode? statusError = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
        services.AddTransient<MudLocalizer, LocalizedMudLocalizer>();
        services.AddSingleton<IJSRuntime, NoJavaScript>();
        services.AddSingleton<NavigationManager, TestNavigation>();
        services.AddSingleton<UnsavedChangesTracker>();
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<ITransponderStatusService>((_, args) =>
        {
            Assert.Equal(view.Status.Id, args[0]);
            if (statusError.HasValue) throw new HttpRequestException("private upstream error", null, statusError);
            return view;
        }));
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<ITransponderOwnerService>((method, _) =>
        {
            if (statusError.HasValue) throw new InvalidOperationException("Owner lookup must not follow denied status");
            if (failOwners) throw new HttpRequestException("directory offline", null, HttpStatusCode.ServiceUnavailable);
            if (view.Status.Administration)
            {
                Assert.Equal(nameof(ITransponderOwnerService.ResolveAsync), method.Name);
                return (IReadOnlyList<TransponderOwnerView>)new[]
                {
                    new TransponderOwnerView(TransponderStatusTestData.OwnerId, "Profile rider", "KeycloakFirst", "KeycloakLast", true)
                };
            }
            Assert.Equal(nameof(ITransponderOwnerService.GetOwnProfileAsync), method.Name);
            return new OwnTransponderProfile(TransponderStatusTestData.OwnerId, "Profile rider");
        }));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<CascadingValue<EffectiveDisplaySettings>>(DisplayTestRendering.Parameters<StatusPage>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(StatusPage.Id)] = view.Status.Id })))).ToHtmlString());
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/transponders");
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => throw new JSDisconnectedException("Static rendering");
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new JSDisconnectedException("Static rendering");
    }
}
