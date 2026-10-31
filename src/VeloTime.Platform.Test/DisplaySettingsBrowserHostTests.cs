using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MudBlazor;
using MudBlazor.Services;
using VeloTime.Module.Profiles.Interface.Data;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.WebUI.Mud.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;
using Http = Microsoft.AspNetCore.Http.Results;
using Stats = VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.Platform.Test;

/// <summary>
/// TEST-ONLY, opt-in interactive host for driving the real server-interactive Profile display settings,
/// samples/statistics and transponder ownership editor in an external browser. All backend services are
/// in-memory fakes; display preferences persist per fake user across reloads and fresh browser sessions
/// for the host lifetime. WebAssembly is NOT hosted: every page runs InteractiveServer.
/// Bounded by POST /__test/shutdown or VELOTIMER_DISPLAY_SETTINGS_BROWSER_MINUTES (default and max 10).
/// </summary>
public sealed class DisplaySettingsBrowserHostTests
{
    public const string AuthenticationScheme = "BrowserHarness";
    public static readonly Guid UserId = Guid.Parse("0000000a-0000-0000-0000-00000000000a");
    public static readonly Guid OtherOwnerId = Guid.Parse("0000000b-0000-0000-0000-00000000000b");

    [DisplaySettingsBrowserHostFact]
    public async Task HostRealProfileDisplaySettings_ForExternalBrowserControl()
    {
        var port = int.TryParse(Environment.GetEnvironmentVariable("VELOTIMER_DISPLAY_SETTINGS_BROWSER_PORT"), out var p) ? p : 5187;
        var minutes = double.TryParse(Environment.GetEnvironmentVariable("VELOTIMER_DISPLAY_SETTINGS_BROWSER_MINUTES"), out var m)
            ? Math.Clamp(m, 0.05, 10) : 10;
        var repository = FindRepository();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development", ContentRootPath = repository });
        builder.WebHost.UseKestrel().UseUrls($"http://127.0.0.1:{port}");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddRazorComponents().AddInteractiveServerComponents(options => options.DetailedErrors = true);
        builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
        builder.Services.Configure<RequestLocalizationOptions>(options =>
            options.SetDefaultCulture("en").AddSupportedCultures("en", "nb-NO").AddSupportedUICultures("en", "nb-NO"));
        builder.Services.AddMudServices();
        builder.Services.AddTransient<MudLocalizer, LocalizedMudLocalizer>();
        builder.Services.AddAuthentication(AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, HarnessAuthenticationHandler>(AuthenticationScheme, null);
        builder.Services.AddAuthorization(AdministrationPolicies.Configure);
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<AuthenticationStateProvider, HarnessAuthenticationStateProvider>();
        builder.Services.AddScoped<UnsavedChangesTracker>();

        var store = new HarnessStore();
        builder.Services.AddSingleton(store);
        builder.Services.AddScoped<IDisplayPreferenceService, FakeDisplayPreferenceService>();
        builder.Services.AddScoped<DisplayPreferenceState>();
        builder.Services.AddScoped<IProfileService, FakeProfileService>();
        builder.Services.AddScoped<IStatisticsService, FakeStatisticsService>();
        builder.Services.AddScoped<IFacilityService, FakeFacilityService>();
        builder.Services.AddScoped<ITransponderManagementService, FakeTransponderManagementService>();
        builder.Services.AddScoped<ITransponderOwnerService, FakeTransponderOwnerService>();

        await using var app = builder.Build();
        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deadline = DateTimeOffset.UtcNow.AddMinutes(minutes);
        app.UseRequestLocalization();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        app.MapGet("/__test/ready", () => Http.Json(new { ready = true, deadlineUtc = deadline, webAssemblyHosted = false, renderMode = "InteractiveServer(prerender:false)" }));
        app.MapGet("/__test/evidence", () => Http.Json(store.Evidence()));
        app.MapGet("/__test/time-zones", () => Http.Json(new
        {
            source = "TimeZoneInfo.GetSystemTimeZones() mapped to IANA ids (Profiles API catalogue algorithm)",
            count = HarnessStore.Zones.Count,
            containsEuropeOslo = HarnessStore.Zones.Any(zone => zone.Id == "Europe/Oslo"),
            zones = HarnessStore.Zones
        }));
        app.MapPost("/__test/reset", () => { store.Reset(); return Http.Ok(store.Evidence()); });
        app.MapPost("/__test/fail-next/{operation}", (string operation) =>
        {
            if (operation is not ("get" or "save" or "zones")) return Http.BadRequest();
            store.FailNext[operation] = true;
            return Http.Ok();
        });
        app.MapPost("/__test/shutdown", () => { shutdown.TrySetResult(); return Http.Ok(new { stopping = true }); });
        app.MapGet("/registrationDates.js", () => Http.File(
            Path.Combine(repository, "src", "WebUI.Mud.Client", "wwwroot", "registrationDates.js"), "text/javascript"));
        MapPackageAsset(app, "MudBlazor", "MudBlazor.min.js");
        MapPackageAsset(app, "MudBlazor", "MudBlazor.min.css");
        MapPackageAsset(app, "Microsoft.AspNetCore.App.Internal.Assets", "blazor.web.js", "/_framework/blazor.web.js");
        app.MapRazorComponents<HostDocument>().AddInteractiveServerRenderMode().AddAdditionalAssemblies(typeof(Routes).Assembly);

        await app.StartAsync();
        try
        {
            var address = $"http://127.0.0.1:{port}";
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
            {
                foreach (var route in new[] { "/__test/ready", "/profile", "/registrationDates.js", "/_framework/blazor.web.js" })
                {
                    using var response = await client.GetAsync(address + route);
                    Assert.True(response.IsSuccessStatusCode, $"{route} returned {(int)response.StatusCode}.");
                }
            }
            var ready = $"VELOTIMER_DISPLAY_SETTINGS_BROWSER_READY {address}/profile (deadline {deadline:O})";
            Console.WriteLine(ready);
            Console.Error.WriteLine(ready);
            await Task.WhenAny(shutdown.Task, Task.Delay(deadline - DateTimeOffset.UtcNow));
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await app.StopAsync(stop.Token);
        }
    }

    public sealed class HostDocument : ComponentBase
    {
        [CascadingParameter] public HttpContext? Context { get; set; }
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            var culture = Context?.Features.Get<Microsoft.AspNetCore.Localization.IRequestCultureFeature>()?.RequestCulture.UICulture.Name == "nb-NO" ? "nb-NO" : "en";
            builder.OpenElement(0, "html");
            builder.AddAttribute(1, "lang", culture);
            builder.OpenElement(2, "head");
            builder.AddMarkupContent(3, """
                <meta charset="utf-8"><base href="/"><title>VeloTime display settings harness (TEST ONLY)</title>
                <link rel="stylesheet" href="/_content/MudBlazor/MudBlazor.min.css">
                """);
            builder.CloseElement();
            builder.OpenElement(4, "body");
            builder.OpenComponent<Routes>(5);
            builder.AddComponentRenderMode(new InteractiveServerRenderMode(prerender: false));
            builder.CloseComponent();
            // Test-only language bridge: cookie-backed, same contract as language.js state/select.
            builder.AddMarkupContent(6, """
                <script>
                window.velotimeLanguage = {
                  state: () => ({ language: document.documentElement.lang === "nb-NO" ? "nb-NO" : "en", preferenceUnavailable: false }),
                  select: (language, returnUrl) => {
                    const value = encodeURIComponent("c=" + language + "|uic=" + language);
                    document.cookie = ".AspNetCore.Culture=" + value + "; path=/; SameSite=Lax";
                    window.location.assign(returnUrl || "/");
                  }
                };
                </script>
                <script src="/_content/MudBlazor/MudBlazor.min.js"></script>
                <script src="/_framework/blazor.web.js"></script>
                """);
            builder.CloseElement();
            builder.CloseElement();
        }
    }

    public static ClaimsPrincipal User() => new(new ClaimsIdentity(
    [
        new Claim("sub", UserId.ToString()), new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
        new Claim(ClaimTypes.Name, "Harness Rider"), new Claim("name", "Harness Rider"),
        new Claim(AdministrationPolicies.PermissionClaimType, AdministrationPolicies.GlobalPermission)
    ], AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role));

    public sealed class HarnessAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(User(), Scheme.Name)));
    }

    public sealed class HarnessAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(User()));
    }

    public sealed record SubmittedPreference(DateTimeOffset AtUtc, UpdateDisplayPreferenceRequest Request, string Outcome, DisplayPreferenceDTO? Persisted);
    public sealed record PreferenceRead(DateTimeOffset AtUtc, string Outcome, DisplayPreferenceDTO? Snapshot);
    public sealed record OwnershipSubmission(DateTimeOffset AtUtc, string Operation, Guid? OwnershipId, Guid? TransponderId, Guid? OwnerId,
        DateTimeOffset? OwnedFromUtc, DateTimeOffset? OwnedToUtc);

    public sealed class HarnessStore
    {
        public static readonly IReadOnlyList<TimeZoneOptionDTO> Zones = TimeZoneInfo.GetSystemTimeZones()
            .Select(zone => TimeZoneInfo.TryConvertIanaIdToWindowsId(zone.Id, out _) ? (Id: zone.Id, Zone: zone)
                : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? (Id: iana, Zone: zone) : (Id: null, Zone: zone))
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id!, StringComparer.Ordinal)
            .Select(group => group.OrderBy(item => item.Zone.Id, StringComparer.Ordinal).First())
            .Select(item => new TimeZoneOptionDTO(item.Id!, $"{item.Id} ({item.Zone.DisplayName})"))
            .OrderBy(zone => zone.Id, StringComparer.Ordinal).ToArray();
        public static readonly DisplayPreferenceDTO Default = new("m", "km/h", "browser", null);
        private readonly object _gate = new();
        public DisplayPreferenceDTO Current { get; private set; } = Default;
        public ConcurrentDictionary<string, bool> FailNext { get; } = new();
        private readonly List<SubmittedPreference> _submitted = [];
        private readonly List<PreferenceRead> _reads = [];
        private readonly List<OwnershipSubmission> _ownershipSubmissions = [];
        private int _zoneCatalogueReads;
        public List<TransponderDTO> Transponders { get; } = [];
        public HarnessStore() => Reset();

        public void Reset()
        {
            lock (_gate)
            {
                Current = Default; _submitted.Clear(); _reads.Clear(); _ownershipSubmissions.Clear(); _zoneCatalogueReads = 0; FailNext.Clear();
                Transponders.Clear();
                var dst = Guid.Parse("000000d1-0000-0000-0000-000000000001");
                var plain = Guid.Parse("000000d2-0000-0000-0000-000000000001");
                // 2026-10-25 Europe/Oslo repeated hour: 00:30Z = 02:30+02:00 and 01:30Z = 02:30+01:00.
                Transponders.Add(new(dst, "MyLaps X2", "DST-1001", "Oslo repeated hour",
                    [new(Guid.Parse("000000e1-0000-0000-0000-000000000001"), dst, UserId,
                        new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero))],
                    new DateTimeOffset(2026, 10, 25, 1, 15, 0, TimeSpan.Zero)));
                Transponders.Add(new(plain, "MyLaps X2", "PLN-2002", "Open ownership",
                    [new(Guid.Parse("000000e2-0000-0000-0000-000000000001"), plain, OtherOwnerId,
                        new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.Zero), null)],
                    new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero)));
            }
        }
        public bool ConsumeFailure(string operation) => FailNext.TryRemove(operation, out var fail) && fail;
        public DisplayPreferenceDTO Read()
        {
            lock (_gate)
            {
                if (ConsumeFailure("get")) { _reads.Add(new(DateTimeOffset.UtcNow, "injected-503", null)); throw new HttpRequestException("Injected", null, System.Net.HttpStatusCode.ServiceUnavailable); }
                _reads.Add(new(DateTimeOffset.UtcNow, "ok", Current));
                return Current;
            }
        }
        public DisplayPreferenceDTO Save(UpdateDisplayPreferenceRequest request)
        {
            lock (_gate)
            {
                if (ConsumeFailure("save")) { _submitted.Add(new(DateTimeOffset.UtcNow, request, "injected-503", null)); throw new HttpRequestException("Injected", null, System.Net.HttpStatusCode.ServiceUnavailable); }
                var valid = request.DistanceUnit is "m" or "ft" && request.SpeedUnit is "km/h" or "mph" or "m/s" &&
                    (request.TimeMode is "browser" or "utc" ? request.TimeZoneId is null
                        : request.TimeMode == "timeZone" && Zones.Any(zone => zone.Id == request.TimeZoneId));
                if (!valid) { _submitted.Add(new(DateTimeOffset.UtcNow, request, "rejected-400", null)); throw new HttpRequestException("Invalid", null, System.Net.HttpStatusCode.BadRequest); }
                Current = new(request.DistanceUnit, request.SpeedUnit, request.TimeMode, request.TimeZoneId);
                _submitted.Add(new(DateTimeOffset.UtcNow, request, "persisted", Current));
                return Current;
            }
        }
        public IReadOnlyList<TimeZoneOptionDTO> ReadZones()
        {
            if (ConsumeFailure("zones")) throw new HttpRequestException("Injected", null, System.Net.HttpStatusCode.ServiceUnavailable);
            Interlocked.Increment(ref _zoneCatalogueReads);
            return Zones;
        }
        private TransponderDTO[] SnapshotTransponders() { lock (Transponders) return Transponders.ToArray(); }
        public void RecordOwnership(OwnershipSubmission submission) { lock (_gate) _ownershipSubmissions.Add(submission); }
        public object Evidence()
        {
            lock (_gate)
            {
                return new
                {
                    user = UserId, persisted = Current, submitted = _submitted.ToArray(), reads = _reads.ToArray(),
                    zoneCatalogueReads = _zoneCatalogueReads, zoneCatalogueCount = Zones.Count,
                    ownershipSubmissions = _ownershipSubmissions.ToArray(),
                    transponders = SnapshotTransponders(), pendingInjectedFailures = FailNext.Keys.ToArray()
                };
            }
        }
    }

    public sealed class FakeDisplayPreferenceService(HarnessStore store) : IDisplayPreferenceService
    {
        public Task<DisplayPreferenceDTO> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(store.Read());
        public Task<DisplayPreferenceDTO> SaveAsync(UpdateDisplayPreferenceRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(store.Save(request));
        public Task<IReadOnlyList<TimeZoneOptionDTO>> GetTimeZonesAsync(CancellationToken cancellationToken = default) => Task.FromResult(store.ReadZones());
    }

    public sealed class FakeProfileService : IProfileService
    {
        public Task<string> GetUsernameAsync(CancellationToken ct = default) => Task.FromResult("harness.rider");
        public Task<IEnumerable<LinkedAccountView>> GetLinkedAccountsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<LinkedAccountView>>([new() { DisplayName = "Harness Strava", ProviderName = "strava", IsConnected = true, IsSocial = true }]);
        public Task<string> GetLinkAccountAsync(string RedirectUri, string Provider, CancellationToken cancellation = default) => Task.FromResult("/profile");
        public Task DeleteLinkedAccountAsync(string Provider, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static readonly Guid FacilityId = Guid.Parse("000000f1-0000-0000-0000-000000000001");
    private static readonly Guid ItemId = Guid.Parse("000000a1-0000-0000-0000-000000000001");
    private static readonly Guid ConfigId = Guid.Parse("000000c1-0000-0000-0000-000000000001");

    public sealed class FakeFacilityService : IFacilityService
    {
        public Task<IEnumerable<FacilityView>> GetFacilitiesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<FacilityView>>([new FacilityView { Id = FacilityId, Name = "Harness Arena" }]);
        public Task<IEnumerable<CourseLayoutView>> GetFacilityLayoutsAsync(Guid facilityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IEnumerable<InstallationView>> GetInstallationsAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CourseLayoutDetailView> GetCourseLayoutDetailAsync(Guid layoutId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    public sealed class FakeStatisticsService : IStatisticsService
    {
        public Task<Stats.PublicStatisticsDiscoveryDTO> GetPublicDiscoveryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new Stats.PublicStatisticsDiscoveryDTO([new(ItemId, "Lap", "250m", 250)],
                [new(ConfigId, ItemId, FacilityId, Stats.StatisticsConfigurationType.Simple, Guid.NewGuid(), "Start", Guid.NewGuid(), "Finish", null, null)], []));
        public Task<Stats.PublicStatisticsResultsPageDTO> GetPublicResultsAsync(Stats.GetPublicStatisticsResultsRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Stats.PublicStatisticsResultsPageDTO(
            [
                new(Guid.Parse("00000101-0000-0000-0000-000000000001"), "Rider 45 kmh", ItemId, ConfigId, FacilityId, TimeSpan.FromSeconds(20), 45, new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero)),
                new(Guid.Parse("00000102-0000-0000-0000-000000000001"), "Rider 36 kmh", ItemId, ConfigId, FacilityId, TimeSpan.FromSeconds(25), 36, new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero))
            ], null, null));
        public Task<IReadOnlyList<BestStatisticsResultView>> GetBestResultsAsync(Guid? facilityId = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BestStatisticsResultView>>([new(Guid.NewGuid(), "Lap", "Rider 45 kmh", "Harness Arena", TimeSpan.FromSeconds(20), 45)]);
        // Repeated Oslo hour, base SI/API units: metres, km/h, UTC DateTime.
        public Task<IEnumerable<SampleView>> GetSamplesAsync(DateTime? cursor, bool isNextPage, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<SampleView>>(
            [
                new() { Time = new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc), TransponderLabel = "Sample before fall-back", StartPoint = "Start", EndPoint = "Finish", Distance = 250, Duration = TimeSpan.FromSeconds(20), Speed = 45 },
                new() { Time = new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc), TransponderLabel = "Sample after fall-back", StartPoint = "Start", EndPoint = "Finish", Distance = 1000, Duration = TimeSpan.FromSeconds(100), Speed = 36 }
            ]);
    }

    public sealed class FakeTransponderOwnerService : ITransponderOwnerService
    {
        private static readonly TransponderOwnerView[] Owners =
            [new(UserId, "Harness Rider", "Harness", "Rider", true), new(OtherOwnerId, "Other Rider", "Other", "Rider", true)];
        public Task<OwnTransponderProfile> GetOwnProfileAsync(CancellationToken cancellationToken = default) => Task.FromResult(new OwnTransponderProfile(UserId, "Harness Rider"));
        public Task<IReadOnlyList<TransponderOwnerView>> ResolveAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TransponderOwnerView>>(Owners.Where(owner => userIds.Contains(owner.UserId)).ToArray());
        public Task<TransponderOwnerPage> SearchAsync(string query, int? profileOffset = 0, int? keycloakOffset = 0, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TransponderOwnerPage(Owners.Where(owner => owner.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray(), null, null));
    }

    public sealed class FakeTransponderManagementService(HarnessStore store) : ITransponderManagementService
    {
        public Task<TransponderPage> GetTranspondersAsync(bool administration, int page = 0, int pageSize = 10, string search = "", bool includeHistory = true,
            CancellationToken cancellationToken = default, TransponderColumnFilters? filters = null, string? sortBy = null, bool sortDescending = false)
        {
            lock (store.Transponders)
            {
                var rows = store.Transponders
                    .Where(t => string.IsNullOrWhiteSpace(search) || t.Label.Contains(search, StringComparison.OrdinalIgnoreCase) || t.SystemId.Contains(search, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(t => t.Ownerships.Where(o => administration || o.OwnerId == UserId).Select(o => new TransponderRow(t, o))
                        .DefaultIfEmpty(administration ? new TransponderRow(t, null) : null!))
                    .Where(row => row is not null).ToArray();
                return Task.FromResult(new TransponderPage(rows.Skip(page * pageSize).Take(pageSize).ToArray(), rows.Length));
            }
        }
        public Task<TransponderLookupPage> LookupTranspondersAsync(int page, string search, CancellationToken cancellationToken = default)
        {
            lock (store.Transponders)
            {
                var items = store.Transponders.Where(t => string.IsNullOrWhiteSpace(search) || t.Label.Contains(search, StringComparison.OrdinalIgnoreCase))
                    .Select(t => new TransponderLookupItem(t.Id, t.Label, t.SystemId, t.Ownerships.Select(o => o.OwnerId).ToArray())).ToArray();
                return Task.FromResult(new TransponderLookupPage(items, items.Length));
            }
        }
        public Task RegisterAsync(string code, bool administration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RegisterTransponderResult> RegisterOwnershipAsync(RegisterTransponderRequest request, bool administration, CancellationToken cancellationToken = default)
        {
            lock (store.Transponders)
            {
                var id = Guid.NewGuid();
                var owner = request.OwnerId ?? UserId;
                store.Transponders.Add(new(id, "MyLaps X2", request.Code, request.Code, [new(Guid.NewGuid(), id, owner, request.OwnedFrom.ToUniversalTime(), request.OwnedTo?.ToUniversalTime())]));
                store.RecordOwnership(new(DateTimeOffset.UtcNow, "register", null, id, owner, request.OwnedFrom, request.OwnedTo));
            }
            return Task.FromResult(new RegisterTransponderResult(false, null, []));
        }
        public Task SaveOwnershipAsync(Guid? id, SaveTransponderOwnershipRequest request, CancellationToken cancellationToken = default)
        {
            lock (store.Transponders)
            {
                var index = store.Transponders.FindIndex(t => t.Id == request.TransponderId);
                if (index < 0) throw new HttpRequestException("Missing", null, System.Net.HttpStatusCode.NotFound);
                var transponder = store.Transponders[index];
                var ownership = new TransponderOwnershipDTO(id ?? Guid.NewGuid(), request.TransponderId, request.OwnerId,
                    request.OwnedFrom.ToUniversalTime(), request.OwnedTo?.ToUniversalTime());
                store.Transponders[index] = transponder with { Ownerships = transponder.Ownerships.Where(o => o.Id != ownership.Id).Append(ownership).ToArray() };
                store.RecordOwnership(new(DateTimeOffset.UtcNow, id.HasValue ? "update" : "create", ownership.Id, request.TransponderId, request.OwnerId, request.OwnedFrom, request.OwnedTo));
            }
            return Task.CompletedTask;
        }
        public Task EndOwnershipAsync(Guid id, bool administration, CancellationToken cancellationToken = default) => Mutate(id, "end",
            o => o with { OwnedTo = DateTimeOffset.UtcNow });
        public Task DeleteOwnershipAsync(Guid id, bool administration, CancellationToken cancellationToken = default) => Mutate(id, "delete", _ => null);
        public Task SaveTransponderAsync(Guid id, string label, CancellationToken cancellationToken = default)
        {
            lock (store.Transponders)
            {
                var index = store.Transponders.FindIndex(t => t.Id == id);
                if (index >= 0) store.Transponders[index] = store.Transponders[index] with { Label = label };
            }
            return Task.CompletedTask;
        }
        public Task DeleteTransponderAsync(Guid id, CancellationToken cancellationToken = default)
        {
            lock (store.Transponders) store.Transponders.RemoveAll(t => t.Id == id && t.Ownerships.Count == 0);
            return Task.CompletedTask;
        }
        private Task Mutate(Guid ownershipId, string operation, Func<TransponderOwnershipDTO, TransponderOwnershipDTO?> change)
        {
            lock (store.Transponders)
            {
                for (var i = 0; i < store.Transponders.Count; i++)
                {
                    var t = store.Transponders[i];
                    if (t.Ownerships.FirstOrDefault(o => o.Id == ownershipId) is not { } existing) continue;
                    var changed = change(existing);
                    store.Transponders[i] = t with { Ownerships = t.Ownerships.Select(o => o.Id == ownershipId ? changed : o).Where(o => o is not null).ToArray()! };
                    store.RecordOwnership(new(DateTimeOffset.UtcNow, operation, ownershipId, t.Id, existing.OwnerId, changed?.OwnedFrom, changed?.OwnedTo));
                }
            }
            return Task.CompletedTask;
        }
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "VeloTimer.sln"))) return directory.FullName;
        throw new InvalidOperationException("Could not locate repository.");
    }

    private static void MapPackageAsset(WebApplication app, string package, string asset, string? route = null)
    {
        var root = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var folder = Path.Combine(root, package.ToLowerInvariant());
        var path = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, asset, SearchOption.AllDirectories).FirstOrDefault() : null;
        if (path is null) throw new InvalidOperationException($"Missing restored {package}/{asset} browser asset.");
        app.MapGet(route ?? $"/_content/{package}/{asset}", () => Http.File(path, asset.EndsWith(".css") ? "text/css" : "text/javascript"));
    }
}

public sealed class DisplaySettingsBrowserHostFactAttribute : FactAttribute
{
    public DisplaySettingsBrowserHostFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VELOTIMER_DISPLAY_SETTINGS_BROWSER_HOST") != "1")
            Skip = "Interactive test-only host. Opt in with VELOTIMER_DISPLAY_SETTINGS_BROWSER_HOST=1; bounded by /__test/shutdown or 10 minutes.";
    }
}
