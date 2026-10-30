using System.Globalization;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Localization;
using VeloTime.Module.Profiles.Client;

namespace VeloTime.WebUI.Mud.Localization;

internal static class CultureResolver
{
    internal const string AnonymousPreferenceCookie = "velotime.language";
    internal static readonly string[] SupportedCultures = ["en", "nb-NO"];

    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
        builder.Services.Configure<RequestLocalizationOptions>(options =>
        {
            var english = CultureInfo.GetCultureInfo("en");
            var norwegian = CultureInfo.GetCultureInfo("nb-NO");
            options.DefaultRequestCulture = new RequestCulture(english);
            options.SupportedCultures = [english, norwegian];
            options.SupportedUICultures = [english, norwegian];
            options.RequestCultureProviders.Clear();
            options.RequestCultureProviders.Add(new CustomRequestCultureProvider(async context =>
            {
                if (!IsCultureRequest(context.Request))
                    return null;

                var state = await ResolveAsync(context);
                return new ProviderCultureResult(state.Language, state.Language);
            }));
        });
    }

    internal static async Task<LanguageSelectionState> ResolveAsync(HttpContext context)
    {
        string? selected = null;
        var unavailable = false;
        if (context.User.Identity?.IsAuthenticated == true)
        {
            try
            {
                var profile = context.RequestServices.GetRequiredService<IProfilesClient>();
                var preference = await profile.GetCurrentLanguagePreferenceAsync(context.RequestAborted);
                selected = Normalize(preference.Language)
                    ?? Normalize(context.Request.Cookies[AnonymousPreferenceCookie]);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                unavailable = true;
                context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("WebUI.LanguagePreference")
                    .LogError("Could not resolve the signed-in language preference ({ExceptionType}).",
                        exception.GetType().Name);
            }
        }
        else
        {
            selected = Normalize(context.Request.Cookies[AnonymousPreferenceCookie]);
        }

        selected ??= BrowserLanguage(context.Request.Headers.AcceptLanguage.ToString());
        return new LanguageSelectionState(selected ?? "en", unavailable);
    }

    internal static string? Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return null;
        var tag = language.Trim().Replace('_', '-');
        if (tag.Equals("en", StringComparison.OrdinalIgnoreCase) ||
            tag.StartsWith("en-", StringComparison.OrdinalIgnoreCase))
            return "en";
        if (tag.Equals("nb", StringComparison.OrdinalIgnoreCase) ||
            tag.StartsWith("nb-", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("nn", StringComparison.OrdinalIgnoreCase) ||
            tag.StartsWith("nn-", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            tag.StartsWith("no-", StringComparison.OrdinalIgnoreCase))
            return "nb-NO";
        return null;
    }

    private static string? BrowserLanguage(string header)
    {
        foreach (var language in header.Split(',')
                     .Select(value => StringWithQualityHeaderValue.TryParse(value.Trim(), out var parsed) ? parsed : null)
                     .Where(value => value is not null)
                          .Where(value => value!.Quality is null or > 0)
                          .OrderByDescending(value => value!.Quality ?? 1))
        {
            var normalized = Normalize(language!.Value);
            if (normalized is not null)
                return normalized;
        }
        return null;
    }

    private static bool IsCultureRequest(HttpRequest request) =>
        request.Path.StartsWithSegments("/_blazor") ||
        (HttpMethods.IsGet(request.Method) &&
        !request.Path.StartsWithSegments("/api") &&
        !request.Path.StartsWithSegments("/language") &&
        !request.Path.StartsWithSegments("/_") &&
        !request.Path.StartsWithSegments("/favicon") &&
        request.Headers.Accept.Any(value => value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true));
}
