using System.Globalization;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Profiles.Client;
using VeloTime.WebUI.Mud.Client;
using VeloTime.WebUI.Mud.Client.Components;
using VeloTime.WebUI.Mud.Client.Pages.Admin;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Components;
using VeloTime.WebUI.Mud.Localization;

namespace VeloTime.Platform.Test;

public sealed class LocalizationContractTests
{
    [Theory]
    [InlineData("nb-NO", "nb-NO")]
    [InlineData("nb", "nb-NO")]
    [InlineData("nn-NO", "nb-NO")]
    [InlineData("nn", "nb-NO")]
    [InlineData("no", "nb-NO")]
    [InlineData("no-NO", "nb-NO")]
    [InlineData("en-US", "en")]
    [InlineData("en", "en")]
    [InlineData("fr-FR", null)]
    [InlineData("", null)]
    public void Normalize_MapsOnlySupportedLanguages(string? input, string? expected) =>
        Assert.Equal(expected, CultureResolver.Normalize(input));

    [Theory]
    [InlineData("fr-CA, nn-NO;q=0.8, en;q=0.4", "nb-NO")]
    [InlineData("en-US;q=0.3, nb;q=0.9", "nb-NO")]
    [InlineData("fr-FR, en-US", "en")]
    [InlineData("fr-FR", "en")]
    [InlineData("nb-NO;q=0, en;q=0.8", "en")]
    public async Task BrowserNegotiation_UsesQualityAndFallsBackToEnglish(string acceptLanguage, string expected)
    {
        var profile = new RecordingProfilesClient();
        var result = await ResolveCulture(profile, acceptLanguage: acceptLanguage);
        Assert.Equal(expected, result);
        Assert.Empty(profile.Reads);
    }

    [Theory]
    [InlineData("/_blazor", "GET")]
    [InlineData("/_blazor", "POST")]
    [InlineData("/_blazor/negotiate", "POST")]
    public async Task CircuitRequests_ResolveTheSameCultureAsPrerender(string path, string method)
    {
        var profiles = new RecordingProfilesClient { Language = "nb-NO" };
        Assert.Equal("nb-NO", await ResolveCulture(profiles, authenticated: true,
            acceptLanguage: "en", path: path, method: method, accept: "*/*"));
        Assert.Equal("nb-NO", await ResolveCulture(new RecordingProfilesClient(),
            anonymousCookie: "nb-NO", acceptLanguage: "en", path: path, method: method, accept: "*/*"));
        Assert.Equal("nb-NO", await ResolveCulture(new RecordingProfilesClient(),
            acceptLanguage: "nn-NO", path: path, method: method, accept: "*/*"));
        Assert.Single(profiles.Reads);
    }

    [Fact]
    public async Task Resolution_UsesSignedInProfileThenAnonymousSelectionThenBrowserThenEnglish()
    {
        var saved = new RecordingProfilesClient { Language = "nb-NO" };
        Assert.Equal("nb-NO", await ResolveCulture(saved, authenticated: true,
            anonymousCookie: "en", acceptLanguage: "en-US"));
        Assert.Single(saved.Reads);

        var anonymous = new RecordingProfilesClient();
        Assert.Equal("nb-NO", await ResolveCulture(anonymous, anonymousCookie: "nb-NO", acceptLanguage: "en-US"));
        Assert.Empty(anonymous.Reads);

        Assert.Equal("nb-NO", await ResolveCulture(new RecordingProfilesClient(), acceptLanguage: "nn"));
        Assert.Equal("en", await ResolveCulture(new RecordingProfilesClient(), acceptLanguage: "fr-CA"));
    }

    [Fact]
    public async Task SignedInUnsetPreference_DoesNotInheritAnonymousSelection()
    {
        var profiles = new RecordingProfilesClient { Language = null };
        Assert.Equal("nb-NO", await ResolveCulture(profiles, authenticated: true,
            anonymousCookie: "nb-NO", acceptLanguage: "en-US"));
        Assert.Single(profiles.Reads);
    }

    [Fact]
    public async Task SignedInProfileReadFailure_FallsBackToBrowserWithoutTreatingFailureAsUnset()
    {
        var profiles = new RecordingProfilesClient
        {
            ReadFailure = new HttpRequestException("private profile dependency detail")
        };
        Assert.Equal("nb-NO", await ResolveCulture(profiles, authenticated: true, acceptLanguage: "nb-NO"));
        Assert.Single(profiles.Reads);
    }

    [Fact]
    public void Resources_ProvideCompleteNorwegianTranslationsAndMatchingPlaceholders()
    {
        var resourceDirectory = Path.Combine(AppContext.BaseDirectory, "Resources");
        var englishPath = Path.Combine(resourceDirectory, "SharedResource.resx");
        var norwegianPath = Path.Combine(resourceDirectory, "SharedResource.nb-NO.resx");
        Assert.True(File.Exists(englishPath), $"English resources were not copied to {englishPath}");
        Assert.True(File.Exists(norwegianPath), $"Norwegian resources were not copied to {norwegianPath}");

        var english = ReadResources(englishPath);
        var norwegian = ReadResources(norwegianPath);
        Assert.NotEmpty(english);
        Assert.Equal(english.Keys.Order(), norwegian.Keys.Order());
        foreach (var key in english.Keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(norwegian[key]), $"Norwegian resource '{key}' is empty.");
            Assert.Equal(Placeholders(english[key]), Placeholders(norwegian[key]));
        }

        foreach (var key in new[] { "Home", "BestTimesLast30Days", "Profile", "FacilitiesAdministration",
                     "FacilityName", "DeleteFacilityConfirm", "RequestFailed", "LanguageSaveFailed" })
            Assert.NotEqual(english[key], norwegian[key]);
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("nb-NO", "nb-NO")]
    public void DocumentLanguage_ReflectsResolvedRequestCultureAndTransfersItBeforeWasmStartup(
        string cultureName, string expectedLanguage)
    {
        var app = new App();
        var context = new DefaultHttpContext();
        context.Features.Set<IRequestCultureFeature>(new RequestCultureFeature(
            new RequestCulture(CultureInfo.GetCultureInfo(cultureName)), provider: null));
        typeof(App).GetProperty("HttpContext", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!.SetValue(app, context);

        var language = typeof(App).GetProperty("documentLanguage", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!.GetValue(app);
        Assert.Equal(expectedLanguage, language);

        var markup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "App.razor"));
        Assert.Contains("<html lang=\"@documentLanguage\">", markup);
        Assert.Contains("autostart=\"false\"", markup);
        Assert.Contains("Blazor.start({ webAssembly: { applicationCulture: document.documentElement.lang } });", markup);

        var languageScript = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "language.js"));
        Assert.Contains("fetch(\"/language/select\"", languageScript);
        Assert.Contains("window.location.assign(result.redirectUrl);", languageScript);
    }

    [Theory]
    [InlineData("en", "Statistics item", "Facility", "45.250", "1.02:03:04.567")]
    [InlineData("nb-NO", "Statistikkelement", "Anlegg", "45,250", "1.02:03:04.567")]
    public async Task PublicStatisticsOutput_UsesCultureLabelsAndNumbersWithoutChangingTimingPrecision(
        string cultureName, string expectedItemHeading, string expectedFacilityHeading, string expectedSpeed,
        string expectedDuration)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLocalization(options => options.ResourcesPath = "Resources");
            services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            services.AddTransient<MudLocalizer, LocalizedMudLocalizer>();
            services.AddSingleton<IJSRuntime, NoJavaScript>();
            services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager, TestNavigation>();
            services.AddSingleton<IStatisticsService>(PlatformServiceTests.TestProxy.Create<IStatisticsService>((_, _) =>
                (IReadOnlyList<VeloTime.WebUI.Mud.Client.ViewModel.BestStatisticsResultView>)
                [
                    new(Guid.NewGuid(), "Lap", "Rider", "Velodrome", TimeSpan.FromDays(1) +
                        TimeSpan.FromHours(2) + TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(4) +
                        TimeSpan.FromMilliseconds(567), 45.25)
                ]));
            await using var provider = services.BuildServiceProvider();
            await using var renderer = new Microsoft.AspNetCore.Components.Web.HtmlRenderer(provider,
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>());
            var html = await renderer.Dispatcher.InvokeAsync(async () =>
                (await renderer.RenderComponentAsync<VeloTime.WebUI.Mud.Client.Components.StatisticsTable>()).ToHtmlString());

            Assert.Contains(expectedItemHeading, html);
            Assert.Contains(expectedFacilityHeading, html);
            Assert.Contains(expectedSpeed, html);
            Assert.Contains(expectedDuration, html);
            Assert.Contains("Lap", html);
            Assert.Contains("Rider", html);
            Assert.Contains("Velodrome", html);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Theory]
    [InlineData("en", "Cancel", "This field is required.", "Time")]
    [InlineData("nb-NO", "Avbryt", "Dette feltet må fylles ut.", "Tid")]
    public void MudLocalizer_UsesResourceText(string cultureName, string cancel, string required, string time)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLocalization(options => options.ResourcesPath = "Resources");
            using var provider = services.BuildServiceProvider();
            var strings = provider.GetRequiredService<IStringLocalizer<SharedResource>>();
            var localizer = new LocalizedMudLocalizer(strings);
            Assert.Equal(cancel, localizer["Cancel"].Value);
            Assert.Equal(required, localizer["RequiredError"].Value);
            Assert.Equal(time, localizer["Time"].Value);
            Assert.Equal(cultureName == "nb-NO" ? "Rader per side:" : "Rows per page:",
                localizer["MudDataGridPager_RowsPerPage"].Value);
            Assert.Equal(cultureName == "nb-NO" ? "1-10 av 25" : "1-10 of 25",
                localizer["MudDataGridPager_InfoFormat", 1, 10, 25].Value);
            Assert.Equal("UnknownMudKey", localizer["UnknownMudKey"].Value);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [Theory]
    [InlineData("en", "Facilities", "No facilities.")]
    [InlineData("nb-NO", "Anlegg", "Ingen anlegg.")]
    public async Task AdminFacilitiesOutput_UsesLocalizedHeadingsAndEmptyState(
        string cultureName, string expectedHeading, string expectedEmptyState)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLocalization(options => options.ResourcesPath = "Resources");
            services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            services.AddTransient<MudLocalizer, LocalizedMudLocalizer>();
            services.AddSingleton<IJSRuntime, NoJavaScript>();
            services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager, TestNavigation>();
            services.AddSingleton<UnsavedChangesTracker>();
            services.AddSingleton<IInfrastructureAdministrationService>(PlatformServiceTests.TestProxy.Create<IInfrastructureAdministrationService>(
                (method, _) => method.Name == nameof(IInfrastructureAdministrationService.GetFacilitiesAsync)
                    ? (IReadOnlyList<FacilityDTO>)Array.Empty<FacilityDTO>()
                    : throw new NotSupportedException(method.Name)));

            await using var provider = services.BuildServiceProvider();
            await using var renderer = new Microsoft.AspNetCore.Components.Web.HtmlRenderer(provider,
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>());
            var html = await renderer.Dispatcher.InvokeAsync(async () =>
                (await renderer.RenderComponentAsync<Facilities>()).ToHtmlString());

            Assert.Contains(expectedHeading, html);
            Assert.Contains(expectedEmptyState, html);
            Assert.Contains(expectedEmptyState, System.Net.WebUtility.HtmlDecode(html));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public void UnsavedChangesTracker_ReportsDirtyStateUntilRegistrationIsDisposed()
    {
        var tracker = new UnsavedChangesTracker();
        var dirty = false;
        var registration = tracker.Register(() => dirty);
        Assert.False(tracker.HasUnsavedChanges);

        dirty = true;
        Assert.True(tracker.HasUnsavedChanges);
        registration.Dispose();
        Assert.False(tracker.HasUnsavedChanges);
    }

    [Fact]
    public async Task LanguageSelector_CancelingUnsavedWorkDoesNotSendPreferenceWriteOrReload()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        try
        {
            var javascript = new RecordingJavaScript { ConfirmResult = false };
            var tracker = new UnsavedChangesTracker();
            using var dirtyState = tracker.Register(() => true);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLocalization(options => options.ResourcesPath = "Resources");
            services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            services.AddSingleton<MudLocalizer, LocalizedMudLocalizer>();
            services.AddSingleton<IJSRuntime>(javascript);
            services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager, TestNavigation>();
            services.AddSingleton(tracker);
            await using var provider = services.BuildServiceProvider();
            await using var renderer = new Microsoft.AspNetCore.Components.Web.HtmlRenderer(provider,
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>());

            LanguageSelector? selector = null;
            var root = await renderer.Dispatcher.InvokeAsync(() => renderer.BeginRenderingComponent<LanguageSelectorHost>(
                Microsoft.AspNetCore.Components.ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(LanguageSelectorHost.Capture)] = (Action<LanguageSelector>)(value => selector = value)
                })));
            await root.QuiescenceTask;
            await renderer.Dispatcher.InvokeAsync(() => (Task)typeof(LanguageSelector)
                .GetMethod("ChangeLanguage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(selector, ["nb-NO"])!);

            Assert.Contains("confirm", javascript.Invocations);
            Assert.DoesNotContain("velotimeLanguage.select", javascript.Invocations);
            Assert.DoesNotContain("window.location.assign", javascript.Invocations);
            Assert.True(tracker.HasUnsavedChanges);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static async Task<string> ResolveCulture(RecordingProfilesClient profiles, bool authenticated = false,
        string? anonymousCookie = null, string? acceptLanguage = null,
        string path = "/", string method = "GET", string accept = "text/html")
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IProfilesClient>(profiles);
        CultureResolver.Configure(builder);
        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Headers.Accept = accept;
        if (acceptLanguage is not null)
            context.Request.Headers.AcceptLanguage = acceptLanguage;
        if (anonymousCookie is not null)
            context.Request.Headers.Cookie = $"{CultureResolver.AnonymousPreferenceCookie}={anonymousCookie}";
        if (authenticated)
            context.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity("test"));
        context.RequestServices = provider;

        var result = await options.RequestCultureProviders.Single().DetermineProviderCultureResult(context);
        return Assert.Single(Assert.IsType<ProviderCultureResult>(result).Cultures).ToString();
    }

    private static Dictionary<string, string> ReadResources(string path)
    {
        var entries = XDocument.Load(path).Root!.Elements("data").Select(element =>
            (Key: (string)element.Attribute("name")!, Value: element.Element("value")?.Value ?? "")).ToArray();
        Assert.Equal(entries.Length, entries.Select(entry => entry.Key).Distinct(StringComparer.Ordinal).Count());
        return entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
    }

    private static string[] Placeholders(string value) =>
        System.Text.RegularExpressions.Regex.Matches(value, @"\{(\d+)(?:,[^}:]+)?(?::[^}]+)?\}")
            .Select(match => match.Groups[1].Value)
            .Order()
            .ToArray();

    private sealed class TestNavigation : Microsoft.AspNetCore.Components.NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/admin/facilities");
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new JSDisconnectedException("Static rendering does not have a browser.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken,
            object?[]? args) => throw new JSDisconnectedException("Static rendering does not have a browser.");
    }

    public sealed class LanguageSelectorHost : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Microsoft.AspNetCore.Components.Parameter]
        public Action<LanguageSelector>? Capture { get; set; }

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent<LanguageSelector>(0);
            builder.AddComponentReferenceCapture(1, component => Capture?.Invoke((LanguageSelector)component));
            builder.CloseComponent();
        }
    }

    private sealed class RecordingJavaScript : IJSRuntime
    {
        public bool ConfirmResult { get; init; }
        public List<string> Invocations { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Invocations.Add(identifier);
            object? result = identifier switch
            {
                "velotimeLanguage.state" => new SelectorLanguageState("en", false),
                "confirm" => ConfirmResult,
                _ => default(TValue)
            };
            return ValueTask.FromResult(result is null ? default! : (TValue)result);
        }

        private sealed record SelectorLanguageState(string Language, bool PreferenceUnavailable);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken,
            object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class RecordingProfilesClient : IProfilesClient
    {
        public string? Language { get; init; }
        public Exception? ReadFailure { get; init; }
        public List<CancellationToken> Reads { get; } = [];

        public Task<LanguagePreferenceDTO> GetCurrentLanguagePreferenceAsync() =>
            GetCurrentLanguagePreferenceAsync(CancellationToken.None);

        public Task<LanguagePreferenceDTO> GetCurrentLanguagePreferenceAsync(CancellationToken cancellationToken)
        {
            Reads.Add(cancellationToken);
            return ReadFailure is null
                ? Task.FromResult(new LanguagePreferenceDTO { Language = Language })
                : Task.FromException<LanguagePreferenceDTO>(ReadFailure);
        }

        public Task<LanguagePreferenceDTO> UpdateLanguagePreferenceAsync(UpdateLanguagePreferenceRequest body) =>
            UpdateLanguagePreferenceAsync(body, CancellationToken.None);

        public Task<LanguagePreferenceDTO> UpdateLanguagePreferenceAsync(UpdateLanguagePreferenceRequest body,
            CancellationToken cancellationToken) =>
            Task.FromResult(new LanguagePreferenceDTO { Language = body.Language });
    }
}
