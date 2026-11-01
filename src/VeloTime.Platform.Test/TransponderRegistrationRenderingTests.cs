using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.WebUI.Mud.Client.Components;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.Platform.Test;

public sealed class TransponderRegistrationRenderingTests
{
    private static readonly Guid OwnerId = Guid.Parse("00000000-0000-0000-0000-000000000123");

    [Theory]
    [InlineData(3, 29, 23, false)]
    [InlineData(3, 29, 23, true)]
    [InlineData(10, 25, 25, false)]
    [InlineData(10, 25, 25, true)]
    public async Task Save_RegisterOwnership_EmitsInclusiveSameDayUtcPayload(int month, int day, int hours, bool administration)
    {
        var date = new DateTime(2026, month, day);
        var requests = await SaveRegistration("Europe/Oslo", date, date, administration);
        var request = Assert.Single(requests);
        var expectedStart = month == 3 ? new DateTimeOffset(2026, 3, 28, 23, 0, 0, TimeSpan.Zero)
            : new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero);
        Assert.Equal("HT-24422", request.Code);
        Assert.Equal(expectedStart, request.OwnedFrom);
        Assert.Equal(expectedStart.AddHours(hours), request.OwnedTo);
        Assert.Equal(TimeSpan.Zero, request.OwnedFrom.Offset);
        Assert.Equal(TimeSpan.Zero, request.OwnedTo!.Value.Offset);
        Assert.Equal(administration ? OwnerId : (Guid?)null, request.OwnerId);
        Assert.Null(request.ConfirmationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_RegisterOwnership_BlankEndEmitsNull(bool administration)
    {
        var request = Assert.Single(await SaveRegistration("Europe/Oslo", new(2026, 3, 29), null, administration));
        Assert.Equal(new DateTimeOffset(2026, 3, 28, 23, 0, 0, TimeSpan.Zero), request.OwnedFrom);
        Assert.Null(request.OwnedTo);
    }

    [Theory]
    [InlineData(3, 8, false, false)]
    [InlineData(11, 1, false, true)]
    [InlineData(3, 7, true, false)]
    [InlineData(10, 31, true, true)]
    public async Task Save_RegisterOwnership_InvalidOrAmbiguousMidnightDoesNotCallService(int month, int day, bool endBoundary, bool ambiguous)
    {
        var date = new DateTime(2026, month, day);
        var wall = date.AddDays(endBoundary ? 1 : 0);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Havana");
        Assert.Equal(ambiguous, zone.IsAmbiguousTime(wall));
        Assert.Equal(!ambiguous, zone.IsInvalidTime(wall));
        Assert.Empty(await SaveRegistration("America/Havana", date, endBoundary ? date : null, false, succeeds: false));
    }

    private static async Task<List<RegisterTransponderRequest>> SaveRegistration(string zone, DateTime start, DateTime? end,
        bool administration, bool succeeds = true)
    {
        var requests = new List<RegisterTransponderRequest>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
        services.AddTransient<MudLocalizer, LocalizedMudLocalizer>();
        services.AddSingleton<IJSRuntime, NoJavaScript>();
        services.AddSingleton<NavigationManager, TestNavigation>();
        services.AddSingleton<UnsavedChangesTracker>();
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<ITransponderManagementService>((method, args) =>
        {
            if (method.Name == nameof(ITransponderManagementService.RegisterOwnershipAsync))
            {
                Assert.Equal(administration, args[1]);
                Assert.False(Assert.IsType<CancellationToken>(args[2]).IsCancellationRequested);
                requests.Add(Assert.IsType<RegisterTransponderRequest>(args[0]));
                return new RegisterTransponderResult(false, null, []);
            }
            Assert.Equal(nameof(ITransponderManagementService.GetTranspondersAsync), method.Name);
            return new TransponderPage([], 0);
        }));
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<ITransponderOwnerService>((method, _) => method.Name switch
        {
            nameof(ITransponderOwnerService.GetOwnProfileAsync) => new OwnTransponderProfile(OwnerId, "Rider"),
            nameof(ITransponderOwnerService.SearchAsync) => new TransponderOwnerPage([], null, null),
            _ => throw new NotSupportedException(method.Name)
        }));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            TransponderManagement? component = null;
            EditorHost? host = null;
            var root = await renderer.RenderComponentAsync<EditorHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(EditorHost.Administration)] = administration,
                [nameof(EditorHost.Settings)] = EffectiveDisplaySettings.Create(new("m", "km/h", "timeZone", zone), "UTC", "en-US"),
                [nameof(EditorHost.Capture)] = (Action<TransponderManagement>)(value => component = value),
                [nameof(EditorHost.CaptureHost)] = (Action<EditorHost>)(value => host = value)
            }));
            await Invoke(component!, "Register");
            Set(component!, "_code", "HT-24422");
            if (administration) Set(component!, "_owner", new TransponderOwnerView(OwnerId, "Rider", null, null, true));
            var startDraft = Field<ZonedInstantDraft>(component!, "_registrationStart");
            var endDraft = Field<ZonedInstantDraft>(component!, "_registrationEnd");
            Assert.False(startDraft.NextDay);
            Assert.True(endDraft.NextDay);
            startDraft.Date = start;
            endDraft.Date = end;
            host!.Refresh(); // Mount the actual MudForm and editor controls before executing Save.
            Assert.NotNull(Field<MudForm>(component!, "_form"));
            await Invoke(component!, "Save");
            Assert.False(Field<bool>(component!, "_busy"));
            if (succeeds)
            {
                Assert.Null(Field<string?>(component!, "_editor"));
                Assert.Single(requests);
            }
            else
            {
                Assert.Equal("register", Field<string>(component!, "_editor"));
                Assert.Empty(requests);
                Assert.Contains("alert", root.ToHtmlString());
            }
        });
        return requests;
    }

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object component, string name) => (T)component.GetType().GetField(name, Private)!.GetValue(component)!;
    private static void Set(object component, string name, object? value) => component.GetType().GetField(name, Private)!.SetValue(component, value);
    private static Task Invoke(object component, string name) => (Task)component.GetType().GetMethod(name, Private)!.Invoke(component, null)!;

    public sealed class EditorHost : ComponentBase
    {
        [Parameter] public bool Administration { get; set; }
        [Parameter] public EffectiveDisplaySettings Settings { get; set; } = DisplayTestRendering.Utc;
        [Parameter] public Action<TransponderManagement>? Capture { get; set; }
        [Parameter] public Action<EditorHost>? CaptureHost { get; set; }
        public void Refresh() => StateHasChanged();
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            CaptureHost?.Invoke(this);
            builder.OpenComponent<CascadingValue<EffectiveDisplaySettings>>(0);
            builder.AddAttribute(1, "Value", Settings);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(child =>
            {
                child.OpenComponent<TransponderManagement>(0);
                child.AddAttribute(1, "Administration", Administration);
                child.AddComponentReferenceCapture(2, value => Capture?.Invoke((TransponderManagement)value));
                child.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new JSDisconnectedException("No browser in static component tests.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new JSDisconnectedException("No browser in static component tests.");
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/transponders");
    }
}
