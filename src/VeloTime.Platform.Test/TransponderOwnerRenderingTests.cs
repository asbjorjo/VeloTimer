using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Collections;
using MudBlazor;
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
            return new TransponderPage([new(new(transponderId, "MyLaps_X2", "106187718", "HT-24422", [], LastSeen, true),
                new(Guid.NewGuid(), transponderId, OwnerId, DateTimeOffset.UtcNow.AddDays(-1), null))], 1);
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
        {
            TransponderManagement? component = null;
            var root = await renderer.RenderComponentAsync<ManagementHost>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(ManagementHost.Administration)] = administration,
                    [nameof(ManagementHost.Capture)] = (Action<TransponderManagement>)(value => component = value)
                }));
            // Static rendering does not run MudDataGrid's interactive OnAfterRender load.
            var grid = Grid(component!);
            Assert.True(grid.Filterable);
            Assert.Equal(DataGridFilterMode.ColumnFilterRow, grid.FilterMode);
            Assert.Equal(SortMode.Single, grid.SortMode);
            await grid.ReloadServerData();
            return root.ToHtmlString();
        });

        Assert.Contains(profile ?? "No statistics profile", html);
        Assert.Contains("HT-24422", html);
        Assert.Contains($"href=\"/transponders/{transponderId}\"", html);
        Assert.Contains("Transponder", html);
        Assert.Contains("System", html);
        Assert.Contains("Owner", html);
        Assert.Contains("Status", html);
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

    [Fact]
    public async Task List_LoadsOnlyRequestedRowsAndResolvesOnlyDisplayedOwners()
    {
        var id = Guid.NewGuid();
        var services = CreateServices();
        var calls = 0;
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<ITransponderManagementService>((method, args) =>
        {
            Assert.Equal(nameof(ITransponderManagementService.GetTranspondersAsync), method.Name);
            Assert.Equal(2, args[1]);
            Assert.Equal(10, args[2]);
            calls++;
            return new TransponderPage([new(new(id, "X2", "123", "Page three", []),
                new(Guid.NewGuid(), id, OwnerId, DateTimeOffset.UtcNow.AddDays(-1), null))], 100);
        }));
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<ITransponderOwnerService>((method, args) =>
        {
            Assert.Equal(nameof(ITransponderOwnerService.ResolveAsync), method.Name);
            Assert.Equal(OwnerId, Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<Guid>>(args[0])));
            return (IReadOnlyList<TransponderOwnerView>)new[] { new TransponderOwnerView(OwnerId, "Visible owner", null, null, false) };
        }));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            TransponderManagement? component = null;
            var root = await renderer.RenderComponentAsync<ManagementHost>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(ManagementHost.Administration)] = true,
                    [nameof(ManagementHost.Capture)] = (Action<TransponderManagement>)(value => component = value) }));
            Assert.Equal(0, calls);
            var grid = Grid(component!);
            grid.CurrentPage = 2;
            await grid.ReloadServerData();
            Assert.Equal(100, grid.GetFilteredItemsCount());
            Assert.Contains("Visible owner", root.ToHtmlString());
            Assert.Contains("Page three", root.ToHtmlString());
        });
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task List_SelectedPageSizeSurvivesParentRerenders()
    {
        var sizes = new List<int>();
        var services = CreateServices();
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<ITransponderManagementService>((_, args) =>
        {
            var size = Assert.IsType<int>(args[2]);
            sizes.Add(size);
            return new TransponderPage(Enumerable.Range(0, size).Select(index =>
                new TransponderRow(new(Guid.NewGuid(), "X2", index.ToString(), $"Label {index}", []), null)).ToList(), 100);
        }));
        services.AddSingleton(NoContentClientCallTests.UnusedOwners());
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            TransponderManagement? component = null;
            await renderer.RenderComponentAsync<ManagementHost>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(ManagementHost.Administration)] = true,
                    [nameof(ManagementHost.Capture)] = (Action<TransponderManagement>)(value => component = value) }));
            var grid = Grid(component!);
            foreach (var size in new[] { 25, 50 })
            {
                await grid.SetRowsPerPageAsync(size);
                await grid.ReloadServerData();
                Assert.Equal(size, grid.RowsPerPage);
                Assert.Equal(size, grid.FilteredItemsCount);
            }
        });
        Assert.Equal(new[] { 25, 50 }, sizes);
    }

    [Fact]
    public async Task List_CancelsSupersededLoadsAndPreventsLateResultsOverwritingNewRows()
    {
        var first = new TaskCompletionSource<TransponderPage>();
        var second = new TaskCompletionSource<TransponderPage>();
        var tokens = new List<CancellationToken>();
        var services = CreateServices();
        var management = System.Reflection.DispatchProxy.Create<ITransponderManagementService, DeferredManagementProxy>();
        ((DeferredManagementProxy)(object)management).Handler = args =>
        {
            tokens.Add(Assert.IsType<CancellationToken>(args[5]));
            return tokens.Count == 1 ? first.Task : second.Task;
        };
        services.AddSingleton(management);
        services.AddSingleton(NoContentClientCallTests.UnusedOwners());
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        TransponderManagement? component = null;
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<ManagementHost>(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(ManagementHost.Administration)] = true,
                [nameof(ManagementHost.Capture)] = (Action<TransponderManagement>)(value => component = value) })));
        var grid = Grid(component!);
        var initial = renderer.Dispatcher.InvokeAsync((Func<Task>)grid.ReloadServerData);
        var latest = renderer.Dispatcher.InvokeAsync((Func<Task>)grid.ReloadServerData);
        Assert.True(tokens[0].IsCancellationRequested);
        second.SetResult(new([new(new(Guid.NewGuid(), "X2", "123", "Newest page", []), null)], 1));
        await latest;
        first.SetResult(new([new(new(Guid.NewGuid(), "X2", "456", "Stale page", []), null)], 1));
        await initial;
        var html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Contains("Newest page", html);
        Assert.DoesNotContain("Stale page", html);
    }

    [Fact]
    public async Task List_RecoversLastValidPageAfterDeletion()
    {
        var requested = new List<int>();
        var services = CreateServices();
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<ITransponderManagementService>((_, args) =>
        {
            var page = Assert.IsType<int>(args[1]);
            requested.Add(page);
            return new TransponderPage(page == 2 ? [new(new(Guid.NewGuid(), "X2", "123", "Last row", []), null)] : [], 21);
        }));
        services.AddSingleton(NoContentClientCallTests.UnusedOwners());
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            TransponderManagement? component = null;
            var root = await renderer.RenderComponentAsync<ManagementHost>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(ManagementHost.Administration)] = true,
                    [nameof(ManagementHost.Capture)] = (Action<TransponderManagement>)(value => component = value) }));
            var grid = Grid(component!);
            grid.CurrentPage = 3;
            await grid.ReloadServerData();
            Assert.Equal(2, grid.CurrentPage);
            Assert.Contains("Last row", root.ToHtmlString());
        });
        Assert.Equal(new[] { 3, 2 }, requested);
    }

    [Fact]
    public async Task List_ShowsExplicitFailureInsteadOfSuccessfulEmptyState()
    {
        var services = CreateServices();
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<ITransponderManagementService>((_, _) =>
            throw new HttpRequestException("Directory unavailable.")));
        services.AddSingleton(NoContentClientCallTests.UnusedOwners());
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            TransponderManagement? component = null;
            var root = await renderer.RenderComponentAsync<ManagementHost>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(ManagementHost.Administration)] = true,
                    [nameof(ManagementHost.Capture)] = (Action<TransponderManagement>)(value => component = value) }));
            await Grid(component!).ReloadServerData();
            var html = root.ToHtmlString();
            Assert.Contains("The operation could not be completed.", html);
            Assert.Contains("data is unavailable", html);
            Assert.DoesNotContain("No transponders match", html);
        });
    }

    [Fact]
    public async Task List_SearchAndHistoryChangesResetThePageAndForwardFilters()
    {
        var requests = new List<(int Page, string Search, bool History)>();
        var services = CreateServices();
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<ITransponderManagementService>((_, args) =>
        {
            requests.Add((Assert.IsType<int>(args[1]), Assert.IsType<string>(args[3]), Assert.IsType<bool>(args[4])));
            return new TransponderPage([], 0);
        }));
        services.AddSingleton(NoContentClientCallTests.UnusedOwners());
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            TransponderManagement? component = null;
            await renderer.RenderComponentAsync<ManagementHost>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(ManagementHost.Administration)] = true,
                    [nameof(ManagementHost.Capture)] = (Action<TransponderManagement>)(value => component = value) }));
            var grid = Grid(component!);
            grid.CurrentPage = 3;
            await Invoke("SearchChanged", "Rider");
            Assert.Equal(0, grid.CurrentPage);
            await grid.ReloadServerData();
            grid.CurrentPage = 2;
            await Invoke("HistoryChanged", false);
            Assert.Equal(0, grid.CurrentPage);
            await grid.ReloadServerData();

            Task Invoke(string name, object value) => Assert.IsAssignableFrom<Task>(typeof(TransponderManagement)
                .GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(component, [value]));
        });
        Assert.Equal(new[] { (0, "Rider", true), (0, "Rider", false) }, requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TransponderPicker_KeepsSelectionAndOnlyLoadsABoundedLookupWhenEditable(bool disabled)
    {
        var services = CreateServices();
        var selected = new TransponderLookupItem(Guid.NewGuid(), "Selected transponder", "123", []);
        var service = PlatformServiceTests.TestProxy.Create<ITransponderManagementService>((method, args) =>
        {
            Assert.False(disabled);
            Assert.Equal(nameof(ITransponderManagementService.LookupTranspondersAsync), method.Name);
            Assert.Equal(0, args[0]);
            Assert.Equal("", args[1]);
            return new TransponderLookupPage([new(Guid.NewGuid(), "Other result", "456", [])], 100);
        });
        services.AddSingleton(service);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<TransponderPicker>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(TransponderPicker.Value)] = selected, [nameof(TransponderPicker.Disabled)] = disabled
            }))).ToHtmlString());
        Assert.Contains("Selected transponder", html);
        if (!disabled)
        {
            Assert.Contains("Other result", html);
            Assert.Contains("Load more transponders", html);
        }
        Assert.Equal(disabled ? 0 : 1, ((PlatformServiceTests.TestProxy)(object)service).Calls.Count);
    }

    public class DeferredManagementProxy : System.Reflection.DispatchProxy
    {
        public Func<object?[], Task<TransponderPage>> Handler { get; set; } = _ => throw new NotSupportedException();
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            Assert.Equal(nameof(ITransponderManagementService.GetTranspondersAsync), targetMethod!.Name);
            return Handler(args!);
        }
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
        services.AddTransient<MudLocalizer, LocalizedMudLocalizer>();
        services.AddSingleton<IJSRuntime, NoJavaScript>();
        services.AddSingleton<NavigationManager, TestNavigation>();
        services.AddSingleton<UnsavedChangesTracker>();
        return services;
    }

    private static GridHarness Grid(TransponderManagement component)
    {
        var grid = typeof(TransponderManagement)
            .GetField("_grid", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(component)!;
        Assert.Equal(typeof(MudDataGrid<>), grid.GetType().GetGenericTypeDefinition());
        return new GridHarness(grid);
    }

    private sealed class GridHarness(object grid)
    {
        private object Grid { get; } = grid;

        public int CurrentPage
        {
            get => (int)Property(nameof(CurrentPage)).GetValue(Grid)!;
            set => Property(nameof(CurrentPage)).SetValue(Grid, value);
        }

        public int RowsPerPage => (int)Property(nameof(RowsPerPage)).GetValue(Grid)!;
        public int FilteredItemsCount => ((IEnumerable)Property("FilteredItems").GetValue(Grid)!).Cast<object>().Count();
        public bool Filterable => (bool)Property(nameof(Filterable)).GetValue(Grid)!;
        public DataGridFilterMode FilterMode => (DataGridFilterMode)Property(nameof(FilterMode)).GetValue(Grid)!;
        public SortMode SortMode => (SortMode)Property(nameof(SortMode)).GetValue(Grid)!;
        public Task ReloadServerData() => (Task)Method(nameof(ReloadServerData)).Invoke(Grid, null)!;
        public Task SetRowsPerPageAsync(int size) => (Task)Method(nameof(SetRowsPerPageAsync), typeof(int)).Invoke(Grid, [size])!;
        public int GetFilteredItemsCount() => (int)Method(nameof(GetFilteredItemsCount)).Invoke(Grid, null)!;

        private System.Reflection.PropertyInfo Property(string name) =>
            Grid.GetType().GetProperty(name)!;

        private System.Reflection.MethodInfo Method(string name, params Type[] parameterTypes) =>
            Grid.GetType().GetMethod(name, parameterTypes)!;
    }

    public sealed class ManagementHost : ComponentBase
    {
        [Parameter] public bool Administration { get; set; }
        [Parameter] public Action<TransponderManagement>? Capture { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<TransponderManagement>(0);
            builder.AddAttribute(1, nameof(TransponderManagement.Administration), Administration);
            builder.AddComponentReferenceCapture(2, value => Capture?.Invoke((TransponderManagement)value));
            builder.CloseComponent();
        }
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
