using System.Globalization;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace VeloTime.WebUI.Mud.Client.Services;

public sealed class LocalizedMudLocalizer(IStringLocalizer<SharedResource> strings) : MudLocalizer
{
    public override LocalizedString this[string key]
    {
        get
        {
            var localized = strings[key];
            if (!localized.ResourceNotFound)
                return localized;
            return new LocalizedString(key, key);
        }
    }

    public override LocalizedString this[string key, params object[] arguments] =>
        new(key, string.Format(CultureInfo.CurrentCulture, this[key].Value, arguments));
}
