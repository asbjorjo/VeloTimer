using Microsoft.AspNetCore.Components;
using VeloTime.WebUI.Mud.Client.Services;
namespace VeloTime.Platform.Test;
// Static component tests supply browser-resolved preferences; no server-local fallback.
internal static class DisplayTestRendering
{
    internal static EffectiveDisplaySettings Utc => EffectiveDisplaySettings.Create(new("m", "km/h", "utc", null), "Europe/Oslo", "en-US");
    internal static ParameterView Parameters<T>(string locale = "en-US") where T : IComponent => Parameters<T>(ParameterView.Empty, locale);
    internal static ParameterView Parameters<T>(ParameterView parameters, string locale = "en-US") where T : IComponent => ParameterView.FromDictionary(new Dictionary<string, object?>
    {
        [nameof(CascadingValue<EffectiveDisplaySettings>.Value)] = EffectiveDisplaySettings.Create(new("m", "km/h", "utc", null), "Europe/Oslo", locale),
        [nameof(CascadingValue<EffectiveDisplaySettings>.ChildContent)] = (RenderFragment)(builder =>
        {
            builder.OpenComponent<T>(0);
            builder.AddMultipleAttributes(1, parameters.ToDictionary().Select(x => new KeyValuePair<string, object>(x.Key, x.Value!)));
            builder.CloseComponent();
        })
    });
}
