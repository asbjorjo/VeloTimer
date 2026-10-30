using System.Globalization;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace VeloTime.WebUI.Mud.Client.Services;

public sealed class LocalizedMudLocalizer(IStringLocalizer<SharedResource> strings) : MudLocalizer
{
    private static readonly IReadOnlyDictionary<string, string> Norwegian = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Cancel"] = "Avbryt", ["Clear"] = "Tøm", ["Close"] = "Lukk",
        ["Today"] = "I dag", ["Ok"] = "OK", ["Apply"] = "Bruk",
        ["Select"] = "Velg", ["Search"] = "Søk", ["No options"] = "Ingen alternativer",
        ["No records"] = "Ingen oppføringer", ["First page"] = "Første side",
        ["Last page"] = "Siste side", ["Next page"] = "Neste side",
        ["Previous page"] = "Forrige side", ["Rows per page"] = "Rader per side",
        ["All"] = "Alle", ["Filter"] = "Filter", ["Sort"] = "Sorter",
        ["Required"] = "Obligatorisk", ["RequiredError"] = "Dette feltet er obligatorisk.",
        ["Previous month"] = "Forrige måned", ["Next month"] = "Neste måned",
        ["Previous year"] = "Forrige år", ["Next year"] = "Neste år",
        ["Go to today"] = "Gå til i dag", ["Choose date"] = "Velg dato",
        ["Choose time"] = "Velg klokkeslett", ["Time"] = "Klokkeslett",
        ["Date"] = "Dato", ["Open"] = "Åpne", ["Remove"] = "Fjern",
        ["Select all"] = "Velg alle", ["Deselect all"] = "Fjern alle valg",
        ["No data"] = "Ingen data", ["Loading"] = "Laster inn"
    };

    public override LocalizedString this[string key]
    {
        get
        {
            if (Norwegian.TryGetValue(key, out var value))
            {
                if (CultureInfo.CurrentUICulture.Name == "nb-NO")
                    return new LocalizedString(key, value);
            }
            var localized = strings[key];
            if (!localized.ResourceNotFound)
                return localized;
            return new LocalizedString(key, key);
        }
    }

    public override LocalizedString this[string key, params object[] arguments] =>
        new(key, string.Format(CultureInfo.CurrentCulture, this[key].Value, arguments));
}
