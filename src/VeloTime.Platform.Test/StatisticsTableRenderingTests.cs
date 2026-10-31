using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using MudBlazor.Services;
using MudBlazor;
using VeloTime.WebUI.Mud.Client.Components;
using VeloTime.WebUI.Mud.Client.Pages;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.Platform.Test;

public sealed class StatisticsTableRenderingTests
{
    [Fact]
    public async Task Table_RendersRequiredColumnsAndRightAlignedThreeDecimalValues()
    {
        var row = new BestStatisticsResultView(Guid.NewGuid(), "Long ride", "Public rider", "Sola",
            new TimeSpan(1, 2, 3, 4, 567), 45.25);
        var html = await Render<StatisticsTable>(CreateService([row]));
        foreach (var text in new[] { "Statistics item", "Profile", "Average speed (km/h)", "Time", "Facility",
            "Long ride", "Public rider", "Sola", "1.02:03:04.567" })
            Assert.Contains(text, html);
        Assert.DoesNotContain("1.02:03:04.5670000", html);
        Assert.Matches("<th[^>]*class=\"[^\"]*text-right[^\"]*\"[^>]*>Average speed", html);
        Assert.Matches("<th[^>]*class=\"[^\"]*text-right[^\"]*\"[^>]*>Time", html);
        Assert.Matches("<td[^>]*class=\"[^\"]*text-right[^\"]*\"[^>]*>" +
            System.Text.RegularExpressions.Regex.Escape(row.Speed.ToString("N3")) + "</td>", html);
        Assert.Matches("<td[^>]*class=\"[^\"]*text-right[^\"]*\"[^>]*>1.02:03:04.567</td>", html);
        Assert.DoesNotContain(row.Id.ToString(), html);
    }

    [Theory]
    [InlineData(0, "00:00:00.000")]
    [InlineData(20123.4567, "00:00:20.123")]
    [InlineData(3600000, "01:00:00.000")]
    public async Task Table_FormatsDurationsWithExactlyThreeDecimals(double milliseconds, string expected)
    {
        var row = new BestStatisticsResultView(Guid.NewGuid(), "Lap", "Rider", "Arena",
            TimeSpan.FromMilliseconds(milliseconds), 45);
        var html = await Render<StatisticsTable>(CreateService([row]));
        Assert.Contains(">" + expected + "</td>", html);
    }

    [Fact]
    public async Task Table_RendersEmptyState()
    {
        var html = await Render<StatisticsTable>(CreateService([]));
        Assert.Contains("No public best times", html);
        Assert.DoesNotContain("Retry", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Table_RendersExplicitErrorInsteadOfEmptyState(bool timeout)
    {
        var service = PlatformServiceTests.TestProxy.Create<IStatisticsService>((_, _) => timeout
            ? throw new TaskCanceledException("timeout")
            : throw new HttpRequestException("secret"));
        var html = await Render<StatisticsTable>(service);
        Assert.Contains("Best times could not be loaded", html);
        Assert.Contains("Retry", html);
        Assert.DoesNotContain("No public best times", html);
        Assert.DoesNotContain("secret", html);
    }

    [Fact]
    public async Task Home_RequestsRollingThirtyDaysAcrossAllFacilitiesAndRemovesStatus()
    {
        var before = DateTimeOffset.UtcNow;
        var service = PlatformServiceTests.TestProxy.Create<IStatisticsService>((_, args) =>
        {
            Assert.Null(args[0]);
            var from = Assert.IsType<DateTimeOffset>(args[1]);
            var to = Assert.IsType<DateTimeOffset>(args[2]);
            Assert.Equal(TimeSpan.FromDays(30), to - from);
            Assert.InRange(to, before, DateTimeOffset.UtcNow);
            return (IReadOnlyList<BestStatisticsResultView>)Array.Empty<BestStatisticsResultView>();
        });
        var html = await Render<Home>(service);
        Assert.Contains("Best times - last 30 days", html);
        Assert.DoesNotContain("Status", html);
        Assert.Single(((PlatformServiceTests.TestProxy)(object)service).Calls);
    }

    [Fact]
    public async Task Table_CancelsSupersededRequestsAndIgnoresStaleResults()
    {
        var first = new TaskCompletionSource<IReadOnlyList<BestStatisticsResultView>>();
        var second = new TaskCompletionSource<IReadOnlyList<BestStatisticsResultView>>();
        var tokens = new List<CancellationToken>();
        var facilityId = Guid.NewGuid();
        var from = DateTimeOffset.UtcNow.AddDays(-7);
        var to = from.AddDays(7);
        var service = new DeferredStatisticsService((facility, start, end, token) =>
        {
            tokens.Add(token);
            if (facility.HasValue)
            {
                Assert.Equal(facilityId, facility);
                Assert.Equal(from, start);
                Assert.Equal(to, end);
            }
            return facility.HasValue ? second.Task : first.Task;
        });
        await using var provider = Services(service).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        StatisticsTable? table = null;
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.BeginRenderingComponent<TableHost>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(TableHost.Capture)] = (Action<StatisticsTable>)(value => table = value) })));
        Assert.Contains("Loading best times", await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        var update = renderer.Dispatcher.InvokeAsync(() => table!.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(StatisticsTable.FacilityId)] = facilityId,
                [nameof(StatisticsTable.From)] = from, [nameof(StatisticsTable.To)] = to })));
        Assert.True(tokens[0].IsCancellationRequested);
        second.SetResult([new(Guid.NewGuid(), "Lap", "New winner", "Arena", TimeSpan.FromSeconds(10), 90)]);
        await update;
        first.SetResult([new(Guid.NewGuid(), "Lap", "Stale winner", "Arena", TimeSpan.FromSeconds(20), 45)]);
        await root.QuiescenceTask;
        var html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Contains("New winner", html);
        Assert.DoesNotContain("Stale winner", html);
    }

    [Fact]
    public async Task Table_DisposalCancelsPendingRequest()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<BestStatisticsResultView>>();
        CancellationToken token = default;
        var service = new DeferredStatisticsService((_, _, _, value) => { token = value; return pending.Task; });
        await using var provider = Services(service).BuildServiceProvider();
        var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(() => renderer.BeginRenderingComponent<StatisticsTable>());
        await renderer.DisposeAsync();
        Assert.True(token.IsCancellationRequested);
        pending.SetResult([]);
    }

    private static IStatisticsService CreateService(IReadOnlyList<BestStatisticsResultView> rows) =>
        PlatformServiceTests.TestProxy.Create<IStatisticsService>((_, _) => rows);

    private static async Task<string> Render<T>(IStatisticsService service) where T : IComponent
    {
        await using var provider = Services(service).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<T>()).ToHtmlString());
    }

    private static ServiceCollection Services(IStatisticsService service)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
        services.AddTransient<MudLocalizer, LocalizedMudLocalizer>();
        services.AddSingleton<IJSRuntime, NoJavaScript>();
        services.AddSingleton<NavigationManager, TestNavigation>();
        services.AddSingleton(service);
        return services;
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
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/");
    }

    private sealed class DeferredStatisticsService(
        Func<Guid?, DateTimeOffset?, DateTimeOffset?, CancellationToken, Task<IReadOnlyList<BestStatisticsResultView>>> get) : IStatisticsService
    {
        public Task<VeloTime.Module.Statistics.Interface.Data.PublicStatisticsDiscoveryDTO> GetPublicDiscoveryAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<VeloTime.Module.Statistics.Interface.Data.PublicStatisticsResultsPageDTO> GetPublicResultsAsync(
            VeloTime.Module.Statistics.Interface.Data.GetPublicStatisticsResultsRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BestStatisticsResultView>> GetBestResultsAsync(Guid? facilityId = null, DateTimeOffset? from = null,
            DateTimeOffset? to = null, CancellationToken cancellationToken = default) => get(facilityId, from, to, cancellationToken);
        public Task<IEnumerable<SampleView>> GetSamplesAsync(DateTime? cursor, bool isNextPage, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    public sealed class TableHost : ComponentBase
    {
        [Parameter] public Action<StatisticsTable> Capture { get; set; } = null!;
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<StatisticsTable>(0);
            builder.AddComponentReferenceCapture(1, component => Capture((StatisticsTable)component));
            builder.CloseComponent();
        }
    }
}
