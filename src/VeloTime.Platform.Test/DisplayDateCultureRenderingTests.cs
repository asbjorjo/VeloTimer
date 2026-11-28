using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;
using VeloTime.WebUI.Mud.Client.Components;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.Platform.Test;

public sealed class DisplayDateCultureRenderingTests
{
    [Theory]
    [InlineData("en", "nb-NO", null, "en")]
    [InlineData("nb-NO", "en-US", null, "nb-NO")]
    [InlineData("en", "nb-NO", "browser", "nb-NO")]
    [InlineData("nb-NO", "en-US", "en", "en")]
    [InlineData("en", "en-US", "nb-NO", "nb-NO")]
    public async Task RealDateAndTimePickers_AndFixedFilterBoundaries_UseDateCulture(
        string ui, string browser, string? preference, string expected)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
        services.AddSingleton<IJSRuntime, NoJavaScript>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var settings = EffectiveDisplaySettings.Create(new("m", "km/h", "timeZone", "Europe/Oslo", preference),
            "Europe/Oslo", browser, CultureInfo.GetCultureInfo(ui));
        var instant = new DateTimeOffset(2026, 2, 1, 12, 15, 0, TimeSpan.Zero);
        var draft = ZonedInstantDraft.FromInstant(instant, settings);
        var culture = CultureInfo.GetCultureInfo(expected);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = new Dictionary<string, object?>
            {
                [nameof(ZonedInstantEditor.Draft)] = draft,
                [nameof(ZonedInstantEditor.EditorSettings)] = settings,
                [nameof(ZonedInstantEditor.Label)] = "Boundary"
            };
            var root = await renderer.RenderComponentAsync<ZonedInstantEditor>(ParameterView.FromDictionary(parameters));
            var html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains($"value=\"{draft.Date!.Value.ToString(culture.DateTimeFormat.ShortDatePattern, culture)}\"", html);
            Assert.Contains($"value=\"{draft.Wall!.Value.ToString(culture.DateTimeFormat.ShortTimePattern, culture)}\"", html);
            parameters[nameof(ZonedInstantEditor.FixedWall)] = true;
            var boundary = await renderer.RenderComponentAsync<ZonedInstantEditor>(ParameterView.FromDictionary(parameters));
            Assert.Contains(draft.Wall.Value.ToString("G", culture), WebUtility.HtmlDecode(boundary.ToHtmlString()));
            Assert.Equal(instant, draft.Resolve(settings));
            Assert.Equal(browser, settings.NumberCulture.Name);
        });
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new JSDisconnectedException("No browser in static component tests.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new JSDisconnectedException("No browser in static component tests.");
    }
}
