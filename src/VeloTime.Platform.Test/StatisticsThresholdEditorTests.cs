using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;
using VeloTime.WebUI.Mud.Client.Pages.Admin;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.Platform.Test;

public sealed class StatisticsThresholdEditorTests
{
    [Fact]
    public async Task EditorShowsInheritedEffectiveClampedAndDisjointStatesWithLivePreview()
    {
        var settings = EffectiveDisplaySettings.Create(new("m", "mph", "utc", null), "UTC", "en-US",
            CultureInfo.GetCultureInfo("en"));
        var inherited = new StatisticsThresholdDraft(null, null, 250, settings);
        var inheritedHtml = WebUtility.HtmlDecode(await Render(inherited));
        Assert.Contains("Inherited", inheritedHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aria-live=\"polite\"", inheritedHtml);
        Assert.Contains("mph", inheritedHtml);
        Assert.Contains("mud-grid-item-xs-12", inheritedHtml);
        var broad = new StatisticsThresholdDraft(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), 250, settings);
        var clamped = WebUtility.HtmlDecode(await Render(broad));
        Assert.Matches("mud-alert-[a-z]+-warning", clamped);
        Assert.Contains("00:00:05.0000000", clamped);
        Assert.Contains("00:00:10.0000000", clamped);
        Assert.Contains("00:00:20.0000000", clamped);
        Assert.Contains("00:00:30.0000000", clamped);
        var disjoint = new StatisticsThresholdDraft(TimeSpan.FromSeconds(21), null, 250, settings);
        var error = WebUtility.HtmlDecode(await Render(disjoint));
        Assert.Matches("mud-alert-[a-z]+-error", error);
        broad.SetLower("malformed");
        var invalid = WebUtility.HtmlDecode(await Render(broad));
        Assert.Contains("malformed", invalid);
        Assert.DoesNotContain("aria-live=\"polite\"", invalid); // Do not announce a misleading valid preview.
    }

    private static async Task<string> Render(StatisticsThresholdDraft draft)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(o => o.ResourcesPath = "Resources");
        services.AddMudServices(o => o.PopoverOptions.CheckForPopoverProvider = false);
        services.AddSingleton<IJSRuntime, NoBrowser>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(StatisticsThresholdEditor.Draft)] = draft,
            [nameof(StatisticsThresholdEditor.Distance)] = 250d,
            [nameof(StatisticsThresholdEditor.Local)] = true,
            [nameof(StatisticsThresholdEditor.GlobalMinimum)] = TimeSpan.FromSeconds(10),
            [nameof(StatisticsThresholdEditor.GlobalMaximum)] = TimeSpan.FromSeconds(20)
        });
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<StatisticsThresholdEditor>(parameters)).ToHtmlString());
    }
    private sealed class NoBrowser : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => throw new JSDisconnectedException("Static regression renderer.");
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new JSDisconnectedException("Static regression renderer.");
    }
}
