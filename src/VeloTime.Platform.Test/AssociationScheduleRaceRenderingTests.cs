using System.Reflection;
using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.WebUI.Mud.Client.Pages.Admin;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.Platform.Test;

public sealed class AssociationScheduleRaceRenderingTests
{
    [Fact]
    public async Task InstallationEditor_StaleSuccessCannotReplaceNewSelectionOrFinishItsLoading()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = PendingSchedule();
        var second = PendingSchedule();
        var provider = CreateServices((id, _) => id == firstId ? first.Task : second.Task);
        await using var services = provider;
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var (root, editor) = await RenderInstallationEditor(renderer);
        PrepareExistingInstallationEditor(editor, firstId);

        var firstLoad = await StartEditPoint(renderer, editor);
        var secondLoad = await StartSelectCoursePoint(renderer, editor, secondId);
        Assert.True(Field<bool>(editor, "_scheduleLoading"));
        Assert.Equal(secondId, Field<Guid?>(editor, "_scheduleCoursePointId"));
        Assert.Contains("mud-progress-linear", await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));

        var current = Snapshot(secondId, Guid.NewGuid());
        second.SetResult([current]);
        await secondLoad;
        Assert.False(Field<bool>(editor, "_scheduleLoading"));
        Assert.Equal(current, Assert.Single(Field<IReadOnlyList<CoursePointTimingAssociationSnapshot>>(editor, "_associationSchedule")));

        first.SetResult([Snapshot(firstId, Guid.NewGuid())]);
        await firstLoad;
        Assert.Equal(secondId, Field<Guid?>(editor, "_scheduleCoursePointId"));
        Assert.False(Field<bool>(editor, "_scheduleLoading"));
        Assert.Null(Field<string?>(editor, "_scheduleError"));
        Assert.Equal(current, Assert.Single(Field<IReadOnlyList<CoursePointTimingAssociationSnapshot>>(editor, "_associationSchedule")));
        await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
    }

    [Fact]
    public async Task InstallationEditor_StaleErrorCannotClearCurrentLoadingOrSetError_AndNoSelectionInvalidates()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = PendingSchedule();
        var second = PendingSchedule();
        var provider = CreateServices((id, _) => id == firstId ? first.Task : second.Task);
        await using var services = provider;
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var (_, editor) = await RenderInstallationEditor(renderer);
        PrepareExistingInstallationEditor(editor, firstId);

        var firstLoad = await StartEditPoint(renderer, editor);
        var secondLoad = await StartSelectCoursePoint(renderer, editor, secondId);
        first.SetException(new HttpRequestException("private upstream error", null, HttpStatusCode.Forbidden));
        await firstLoad;
        Assert.True(Field<bool>(editor, "_scheduleLoading"));
        Assert.Equal(secondId, Field<Guid?>(editor, "_scheduleCoursePointId"));
        Assert.Null(Field<string?>(editor, "_scheduleError"));

        var current = Snapshot(secondId, Guid.NewGuid());
        second.SetResult([current]);
        await secondLoad;
        Assert.False(Field<bool>(editor, "_scheduleLoading"));
        Assert.Equal(current, Assert.Single(Field<IReadOnlyList<CoursePointTimingAssociationSnapshot>>(editor, "_associationSchedule")));

        var pendingAfterReset = PendingSchedule();
        var resetId = Guid.NewGuid();
        var noSelectionServices = CreateServices((id, _) => id == resetId ? pendingAfterReset.Task : Task.FromResult<IReadOnlyList<CoursePointTimingAssociationSnapshot>>([]));
        await using var resetProvider = noSelectionServices;
        await using var resetRenderer = new HtmlRenderer(resetProvider, resetProvider.GetRequiredService<ILoggerFactory>());
        var (resetRoot, resetEditor) = await RenderInstallationEditor(resetRenderer);
        PrepareExistingInstallationEditor(resetEditor, resetId, associateTimingPoint: false);
        await StartEditPoint(resetRenderer, resetEditor);
        var resetLoad = await StartSelectCoursePoint(resetRenderer, resetEditor, resetId);
        var noSelection = await StartSelectCoursePoint(resetRenderer, resetEditor, null);
        await noSelection;
        Assert.False(Field<bool>(resetEditor, "_scheduleLoading"));
        Assert.Null(Field<Guid?>(resetEditor, "_scheduleCoursePointId"));
        Assert.Empty(Field<IReadOnlyList<CoursePointTimingAssociationSnapshot>>(resetEditor, "_associationSchedule"));
        Assert.DoesNotContain("mud-progress-linear", await resetRenderer.Dispatcher.InvokeAsync(resetRoot.ToHtmlString));

        pendingAfterReset.SetException(new HttpRequestException("late failure", null, HttpStatusCode.Forbidden));
        await resetLoad;
        Assert.False(Field<bool>(resetEditor, "_scheduleLoading"));
        Assert.Null(Field<string?>(resetEditor, "_scheduleError"));
        Assert.Empty(Field<IReadOnlyList<CoursePointTimingAssociationSnapshot>>(resetEditor, "_associationSchedule"));
    }

    private static TaskCompletionSource<IReadOnlyList<CoursePointTimingAssociationSnapshot>> PendingSchedule() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static CoursePointTimingAssociationSnapshot Snapshot(Guid coursePointId, Guid timingPointId) =>
        new(timingPointId, coursePointId, null, null);

    private static async Task<(HtmlRootComponent Root, InstallationEditor Editor)> RenderInstallationEditor(HtmlRenderer renderer)
    {
        InstallationEditor? captured = null;
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.BeginRenderingComponent<InstallationHost>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(InstallationHost.Capture)] = (Action<InstallationEditor>)(component => captured = component)
            })));
        await root.QuiescenceTask;
        return (root, captured!);
    }

    private static void PrepareExistingInstallationEditor(
        InstallationEditor editor, Guid coursePointId, bool associateTimingPoint = true)
    {
        var timingPointId = Guid.NewGuid();
        Set(editor, "_installation", new VeloTime.Module.Timing.Interface.Data.InstallationDTO
        {
            AgentId = "test-agent",
            TimingSystem = new() { Name = "test" }
        });
        Set(editor, "_coursePoints", new List<CoursePointDTO>
        {
            new() { Id = coursePointId, Name = "Test point", TimingPointId = associateTimingPoint ? timingPointId : Guid.NewGuid() }
        });
        Set(editor, "_timingPointNames", new Dictionary<Guid, string> { [timingPointId] = "test timing point" });
    }

    private static async Task<Task> StartEditPoint(HtmlRenderer renderer, InstallationEditor editor)
    {
        var timingPointId = Field<Dictionary<Guid, string>>(editor, "_timingPointNames").Keys.Single();
        Task? pending = null;
        await renderer.Dispatcher.InvokeAsync((Action)(() => pending = EventCallback.Factory
            .Create(editor, () => Invoke(editor, "EditPoint", new VeloTime.Module.Timing.Interface.Data.TimingPointDTO
            {
                Id = timingPointId,
                Description = "test point",
                SystemId = "test-system"
            })).InvokeAsync()));
        return pending!;
    }

    private static async Task<Task> StartSelectCoursePoint(HtmlRenderer renderer, InstallationEditor editor, Guid? id)
    {
        Task? pending = null;
        await renderer.Dispatcher.InvokeAsync((Action)(() => pending = EventCallback.Factory
            .Create(editor, () => Invoke(editor, "SelectCoursePoint", id)).InvokeAsync()));
        return pending!;
    }

    private static ServiceProvider CreateServices(
        Func<Guid, CancellationToken, Task<IReadOnlyList<CoursePointTimingAssociationSnapshot>>> getSchedule)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
        services.AddTransient<MudLocalizer, LocalizedMudLocalizer>();
        services.AddSingleton<IJSRuntime, NoJavaScript>();
        services.AddSingleton<NavigationManager, TestNavigation>();
        services.AddSingleton<UnsavedChangesTracker>();
        services.AddSingleton<IInfrastructureAdministrationService>(CreateAdministrationService(getSchedule));
        return services.BuildServiceProvider();
    }

    private static IInfrastructureAdministrationService CreateAdministrationService(
        Func<Guid, CancellationToken, Task<IReadOnlyList<CoursePointTimingAssociationSnapshot>>> getSchedule)
    {
        var proxy = DispatchProxy.Create<IInfrastructureAdministrationService, AdministrationServiceProxy>();
        ((AdministrationServiceProxy)(object)proxy).Handler = (method, args) => method.Name switch
        {
            nameof(IInfrastructureAdministrationService.GetFacilitiesAsync) =>
                Task.FromResult<IReadOnlyList<FacilityDTO>>([]),
            nameof(IInfrastructureAdministrationService.GetInstallationsAsync) =>
                Task.FromResult<IReadOnlyList<VeloTime.Module.Timing.Interface.Data.InstallationDTO>>([]),
            nameof(IInfrastructureAdministrationService.GetLayoutsAsync) =>
                Task.FromResult<IReadOnlyList<CourseLayoutDTO>>([]),
            nameof(IInfrastructureAdministrationService.GetCoursePointTimingAssociationsAsync) =>
                getSchedule((Guid)args[0]!, (CancellationToken)args[1]!),
            _ => throw new NotSupportedException(method.Name)
        };
        return proxy;
    }

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, Private)!.GetValue(instance)!;
    private static void Set(object instance, string name, object? value) =>
        instance.GetType().GetField(name, Private)!.SetValue(instance, value);
    private static Task Invoke(object instance, string name, params object?[] parameters) =>
        (Task)instance.GetType().GetMethod(name, Private)!.Invoke(instance, parameters)!;

    public sealed class InstallationHost : ComponentBase
    {
        [Parameter] public Action<InstallationEditor>? Capture { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<EffectiveDisplaySettings>>(0);
            builder.AddAttribute(1, nameof(CascadingValue<EffectiveDisplaySettings>.Value),
                EffectiveDisplaySettings.Create(new("m", "km/h", "utc", null), "UTC", "en-US"));
            builder.AddAttribute(2, nameof(CascadingValue<EffectiveDisplaySettings>.ChildContent), (RenderFragment)(child =>
            {
                child.OpenComponent<InstallationEditor>(0);
                child.AddComponentReferenceCapture(1, value => Capture?.Invoke((InstallationEditor)value));
                child.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    public class AdministrationServiceProxy : DispatchProxy
    {
        internal Func<MethodInfo, object?[], object?> Handler { get; set; } = (_, _) => throw new NotSupportedException();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Handler(targetMethod ?? throw new InvalidOperationException("Missing service method."), args ?? []);
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) =>
            throw new JSDisconnectedException("Static component test.");
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new JSDisconnectedException("Static component test.");
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/admin/timing/installations/new");
    }
}
